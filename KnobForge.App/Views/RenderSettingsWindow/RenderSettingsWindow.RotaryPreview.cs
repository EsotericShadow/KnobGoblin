using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using KnobForge.App.Controls;
using KnobForge.Core;
using KnobForge.Core.Export;
using KnobForge.Core.Scene;
using KnobForge.Rendering;
using KnobForge.Rendering.GPU;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


namespace KnobForge.App.Views
{
    public partial class RenderSettingsWindow : Window
    {
        private void MarkRotaryPreviewDirty()
        {
            if (_isBuildingRotaryPreview || !string.IsNullOrWhiteSpace(_rotaryPreviewTempPath))
            {
                _rotaryPreviewInfoTextBlock.Text = "Settings changed. Click Create Rotary Preview to refresh.";
            }
        }

        private void OnRotaryPreviewVariantSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            MarkRotaryPreviewDirty();
        }

        private void OnRotaryPreviewKnobPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == RangeBase.ValueProperty ||
                e.Property == RangeBase.MinimumProperty ||
                e.Property == RangeBase.MaximumProperty)
            {
                UpdateRotaryPreviewValueText();
            }
        }

        private async void OnCreateRotaryPreviewButtonClick(object? sender, RoutedEventArgs e)
        {
            if (_isBuildingRotaryPreview)
            {
                return;
            }

            if (!CanUseGpuExport)
            {
                _rotaryPreviewInfoTextBlock.Text = "Rotary preview unavailable: GPU offscreen rendering is unavailable.";
                return;
            }

            if (!TryBuildPreviewRequest(out PreviewRenderRequest request, out string validationError))
            {
                _rotaryPreviewInfoTextBlock.Text = $"Cannot build rotary preview: {validationError}";
                return;
            }

            var variant = _rotaryPreviewVariantComboBox.SelectedItem as PreviewVariantOption
                ?? _previewVariantOptions[0];

            _rotaryPreviewCts?.Cancel();
            _rotaryPreviewCts?.Dispose();
            _rotaryPreviewCts = new CancellationTokenSource();

            SetRotaryPreviewBusy(true, $"Generating {variant.DisplayName} preview...");

            try
            {
                RotaryPreviewSheet previewSheet = await BuildRotaryPreviewSheetAsync(request, variant, _rotaryPreviewCts.Token);
                CleanupRotaryPreviewTempPath();
                _rotaryPreviewTempPath = previewSheet.SpriteSheetPath;
                ApplyRotaryPreviewSheet(previewSheet);
                _rotaryPreviewInfoTextBlock.Text = $"Ready: {variant.DisplayName}, {previewSheet.FrameCount} frames at {previewSheet.FrameSizePx}px. Drag to spin.";
            }
            catch (OperationCanceledException)
            {
                _rotaryPreviewInfoTextBlock.Text = "Rotary preview canceled.";
            }
            catch (Exception ex)
            {
                _rotaryPreviewInfoTextBlock.Text = $"Rotary preview failed: {ex.Message}";
            }
            finally
            {
                _rotaryPreviewCts?.Dispose();
                _rotaryPreviewCts = null;
                SetRotaryPreviewBusy(false);
            }
        }

        private void SetRotaryPreviewBusy(bool isBusy, string? status = null)
        {
            _isBuildingRotaryPreview = isBusy;
            _createRotaryPreviewButton.IsEnabled = !isBusy && !_isRendering;
            _rotaryPreviewVariantComboBox.IsEnabled = !isBusy && !_isRendering;
            if (!string.IsNullOrWhiteSpace(status))
            {
                _rotaryPreviewInfoTextBlock.Text = status;
            }
        }

        private async Task<RotaryPreviewSheet> BuildRotaryPreviewSheetAsync(
            PreviewRenderRequest request,
            PreviewVariantOption variant,
            CancellationToken cancellationToken)
        {
            if (_gpuViewport == null)
            {
                throw new InvalidOperationException("GPU viewport is unavailable.");
            }

            var defaults = new KnobExportSettings();
            float yawOffsetDeg = defaults.OrbitVariantYawOffsetDeg;
            float pitchOffsetDeg = defaults.OrbitVariantPitchOffsetDeg;
            TryParseFloat(_orbitYawOffsetTextBox.Text, MinOrbitOffsetDeg, MaxOrbitYawOffsetDeg, "Orbit yaw offset", out yawOffsetDeg, out _);
            TryParseFloat(_orbitPitchOffsetTextBox.Text, MinOrbitOffsetDeg, MaxOrbitPitchOffsetDeg, "Orbit pitch offset", out pitchOffsetDeg, out _);

            ViewportCameraState cameraState = ApplyPreviewVariant(
                request.CameraState,
                variant.Kind,
                yawOffsetDeg,
                pitchOffsetDeg);

            int frameCount = request.FrameCount;
            int resolution = request.Resolution;
            int columns = (int)Math.Ceiling(Math.Sqrt(frameCount));
            int rows = (int)Math.Ceiling(frameCount / (double)columns);

            using var sheetBitmap = new SKBitmap(new SKImageInfo(
                checked(columns * resolution),
                checked(rows * resolution),
                SKColorType.Bgra8888,
                SKAlphaType.Premul));
            using var sheetCanvas = new SKCanvas(sheetBitmap);
            sheetCanvas.Clear(new SKColor(0, 0, 0, 0));

            using var frameBitmap = new SKBitmap(new SKImageInfo(
                resolution,
                resolution,
                SKColorType.Bgra8888,
                SKAlphaType.Premul));
            using var frameCanvas = new SKCanvas(frameBitmap);
            using var downsamplePaint = new SKPaint
            {
                BlendMode = SKBlendMode.Src,
                IsAntialias = true,
                IsDither = true
            };
            using var sheetPaint = new SKPaint
            {
                BlendMode = SKBlendMode.Src,
                IsAntialias = false
            };
            SKSamplingOptions downsampleSampling = new(new SKCubicResampler(1f / 3f, 1f / 3f));
            SKSamplingOptions directSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

            ModelRotationSnapshot[] snapshots = await Dispatcher.UIThread.InvokeAsync(
                CaptureModelRotations,
                DispatcherPriority.Background);

            float angleStep = 2f * MathF.PI / frameCount;
            int progressStep = Math.Max(1, frameCount / 8);
            try
            {
                int fittingSamples = Math.Clamp(Math.Min(frameCount, 12), 4, 12);
                cameraState = await FitRotaryPreviewCameraAsync(request, cameraState, snapshots, fittingSamples, cancellationToken);

                for (int i = 0; i < frameCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (i == 0 || i == frameCount - 1 || ((i + 1) % progressStep) == 0)
                    {
                        int progress = i + 1;
                        await Dispatcher.UIThread.InvokeAsync(
                            () => _rotaryPreviewInfoTextBlock.Text = $"Generating {variant.DisplayName} preview... {progress}/{frameCount}",
                            DispatcherPriority.Background);
                    }

                    float angle = i * angleStep;
                    await Dispatcher.UIThread.InvokeAsync(
                        () => ApplyModelRotationDelta(snapshots, angle),
                        DispatcherPriority.Render);

                    SKBitmap? gpuFrame = await Dispatcher.UIThread.InvokeAsync(
                        () =>
                        {
                            if (_gpuViewport.TryRenderFrameToBitmap(
                                request.RenderResolution,
                                request.RenderResolution,
                                cameraState,
                                out SKBitmap? frame))
                            {
                                return frame;
                            }

                            return null;
                        },
                        DispatcherPriority.Render);

                    if (gpuFrame == null)
                    {
                        throw new InvalidOperationException("GPU frame capture failed while building rotary preview.");
                    }

                    using (gpuFrame)
                    using (SKImage sourceImage = SKImage.FromBitmap(gpuFrame))
                    {
                        frameCanvas.Clear(new SKColor(0, 0, 0, 0));
                        if (request.SupersampleScale > 1 ||
                            gpuFrame.Width != resolution ||
                            gpuFrame.Height != resolution)
                        {
                            frameCanvas.DrawImage(
                                sourceImage,
                                new SKRect(0, 0, gpuFrame.Width, gpuFrame.Height),
                                new SKRect(0, 0, resolution, resolution),
                                downsampleSampling,
                                downsamplePaint);
                        }
                        else
                        {
                            frameCanvas.DrawImage(
                                sourceImage,
                                new SKRect(0, 0, resolution, resolution),
                                directSampling,
                                downsamplePaint);
                        }
                    }

                    int col = i % columns;
                    int row = i / columns;
                    sheetCanvas.DrawBitmap(frameBitmap, col * resolution, row * resolution, sheetPaint);
                }
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(
                    () => RestoreModelRotations(snapshots),
                    DispatcherPriority.Render);
            }

            string outputPath = CreateRotaryPreviewTempPath();
            using SKData pngData = sheetBitmap.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream outputStream = File.Create(outputPath);
            pngData.SaveTo(outputStream);

            return new RotaryPreviewSheet(outputPath, frameCount, columns, resolution);
        }

        private static ViewportCameraState ApplyPreviewVariant(
            ViewportCameraState baseState,
            PreviewViewVariantKind kind,
            float yawOffsetDeg,
            float pitchOffsetDeg)
        {
            float yaw = MathF.Abs(yawOffsetDeg);
            float pitch = MathF.Abs(pitchOffsetDeg);

            float yawDelta = 0f;
            float pitchDelta = 0f;
            switch (kind)
            {
                case PreviewViewVariantKind.UnderLeft:
                    yawDelta = -yaw;
                    pitchDelta = pitch;
                    break;
                case PreviewViewVariantKind.UnderRight:
                    yawDelta = yaw;
                    pitchDelta = pitch;
                    break;
                case PreviewViewVariantKind.OverLeft:
                    yawDelta = -yaw;
                    pitchDelta = -pitch;
                    break;
                case PreviewViewVariantKind.OverRight:
                    yawDelta = yaw;
                    pitchDelta = -pitch;
                    break;
            }

            float resultYaw = baseState.OrbitYawDeg + yawDelta;
            float resultPitch = Math.Clamp(baseState.OrbitPitchDeg + pitchDelta, -85f, 85f);
            return new ViewportCameraState(resultYaw, resultPitch, baseState.Zoom, baseState.PanPx);
        }

        private async Task<ViewportCameraState> FitRotaryPreviewCameraAsync(
            PreviewRenderRequest request,
            ViewportCameraState cameraState,
            ModelRotationSnapshot[] snapshots,
            int sampleCount,
            CancellationToken cancellationToken)
        {
            if (_gpuViewport == null || snapshots.Length == 0)
            {
                return cameraState;
            }

            float marginPx = MathF.Max(4f, request.Resolution * 0.04f);
            const int maxFitIterations = 5;

            try
            {
                for (int iteration = 0; iteration < maxFitIterations; iteration++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    float fitScale = 1f;
                    for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        float angle = (2f * MathF.PI * sampleIndex) / sampleCount;

                        await Dispatcher.UIThread.InvokeAsync(
                            () => ApplyModelRotationDelta(snapshots, angle),
                            DispatcherPriority.Render);

                        SKBitmap? sampleBitmap = await Dispatcher.UIThread.InvokeAsync(
                            () =>
                            {
                                if (_gpuViewport.TryRenderFrameToBitmap(
                                    request.Resolution,
                                    request.Resolution,
                                    cameraState,
                                    out SKBitmap? frame))
                                {
                                    return frame;
                                }

                                return null;
                            },
                            DispatcherPriority.Render);

                        if (sampleBitmap == null)
                        {
                            continue;
                        }

                        using (sampleBitmap)
                        {
                            if (!TryGetOpaqueBounds(sampleBitmap, 2, out PixelAlphaBounds bounds))
                            {
                                continue;
                            }

                            float frameMin = 0f;
                            float frameMaxX = request.Resolution - 1f;
                            float frameMaxY = request.Resolution - 1f;
                            float centerX = (frameMin + frameMaxX) * 0.5f;
                            float centerY = (frameMin + frameMaxY) * 0.5f;

                            float availableLeft = MathF.Max(1f, centerX - marginPx);
                            float availableRight = MathF.Max(1f, (frameMaxX - marginPx) - centerX);
                            float availableTop = MathF.Max(1f, centerY - marginPx);
                            float availableBottom = MathF.Max(1f, (frameMaxY - marginPx) - centerY);

                            float usedLeft = MathF.Max(1f, centerX - bounds.MinX);
                            float usedRight = MathF.Max(1f, bounds.MaxX - centerX);
                            float usedTop = MathF.Max(1f, centerY - bounds.MinY);
                            float usedBottom = MathF.Max(1f, bounds.MaxY - centerY);

                            float scaleLeft = availableLeft / usedLeft;
                            float scaleRight = availableRight / usedRight;
                            float scaleTop = availableTop / usedTop;
                            float scaleBottom = availableBottom / usedBottom;
                            float frameScale = MathF.Min(MathF.Min(scaleLeft, scaleRight), MathF.Min(scaleTop, scaleBottom));
                            fitScale = MathF.Min(fitScale, frameScale);
                        }
                    }

                    if (fitScale >= 0.998f)
                    {
                        break;
                    }

                    float appliedScale = Math.Clamp(fitScale * 0.985f, 0.65f, 0.995f);
                    cameraState = cameraState with
                    {
                        Zoom = MathF.Max(0.2f, cameraState.Zoom * appliedScale)
                    };
                }
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(
                    () => RestoreModelRotations(snapshots),
                    DispatcherPriority.Render);
            }

            return cameraState;
        }

        private static bool TryGetOpaqueBounds(SKBitmap bitmap, byte alphaThreshold, out PixelAlphaBounds bounds)
        {
            int minX = bitmap.Width;
            int minY = bitmap.Height;
            int maxX = -1;
            int maxY = -1;

            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y).Alpha <= alphaThreshold)
                    {
                        continue;
                    }

                    if (x < minX)
                    {
                        minX = x;
                    }

                    if (y < minY)
                    {
                        minY = y;
                    }

                    if (x > maxX)
                    {
                        maxX = x;
                    }

                    if (y > maxY)
                    {
                        maxY = y;
                    }
                }
            }

            if (maxX < minX || maxY < minY)
            {
                bounds = default;
                return false;
            }

            bounds = new PixelAlphaBounds(minX, minY, maxX, maxY);
            return true;
        }

        private ModelRotationSnapshot[] CaptureModelRotations()
        {
            return _project.SceneRoot.Children
                .OfType<ModelNode>()
                .Select(model => new ModelRotationSnapshot(model, model.RotationRadians))
                .ToArray();
        }

        private void ApplyModelRotationDelta(ModelRotationSnapshot[] snapshots, float angleDeltaRadians)
        {
            for (int i = 0; i < snapshots.Length; i++)
            {
                snapshots[i].Model.RotationRadians = snapshots[i].RotationRadians + angleDeltaRadians;
            }
        }

        private void RestoreModelRotations(ModelRotationSnapshot[] snapshots)
        {
            for (int i = 0; i < snapshots.Length; i++)
            {
                snapshots[i].Model.RotationRadians = snapshots[i].RotationRadians;
            }
        }

        private void ApplyRotaryPreviewSheet(RotaryPreviewSheet sheet)
        {
            _rotaryPreviewKnob.SpriteSheetPath = sheet.SpriteSheetPath;
            _rotaryPreviewKnob.FrameCount = sheet.FrameCount;
            _rotaryPreviewKnob.ColumnCount = sheet.ColumnCount;
            _rotaryPreviewKnob.FrameWidth = sheet.FrameSizePx;
            _rotaryPreviewKnob.FrameHeight = sheet.FrameSizePx;
            _rotaryPreviewKnob.FramePadding = 0;
            _rotaryPreviewKnob.FrameStartX = 0;
            _rotaryPreviewKnob.FrameStartY = 0;
            _rotaryPreviewKnob.Minimum = 0d;
            _rotaryPreviewKnob.Maximum = Math.Max(1d, sheet.FrameCount - 1d);
            _rotaryPreviewKnob.KnobDiameter = sheet.FrameSizePx;
            _rotaryPreviewKnob.Value = 0d;
            _rotaryPreviewKnob.IsEnabled = true;
            UpdateRotaryPreviewValueText();
        }

        private void UpdateRotaryPreviewValueText()
        {
            int maxFrame = (int)Math.Max(1, Math.Round(_rotaryPreviewKnob.Maximum));
            int frameIndex = (int)Math.Round(Math.Clamp(_rotaryPreviewKnob.Value, _rotaryPreviewKnob.Minimum, _rotaryPreviewKnob.Maximum)) + 1;
            frameIndex = Math.Clamp(frameIndex, 1, maxFrame + 1);
            _rotaryPreviewValueTextBlock.Text = $"Frame {frameIndex} / {maxFrame + 1}";
        }

        private static string CreateRotaryPreviewTempPath()
        {
            string folder = Path.Combine(Path.GetTempPath(), "KnobForge", "rotary-preview");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, $"rotary_preview_{Guid.NewGuid():N}.png");
        }

        private void CleanupRotaryPreviewTempPath()
        {
            if (string.IsNullOrWhiteSpace(_rotaryPreviewTempPath))
            {
                return;
            }

            try
            {
                if (File.Exists(_rotaryPreviewTempPath))
                {
                    File.Delete(_rotaryPreviewTempPath);
                }
            }
            catch
            {
            }

            _rotaryPreviewTempPath = null;
        }
    }
}
