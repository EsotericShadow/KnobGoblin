using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using KnobForge.Core;
using KnobForge.Core.Export;
using KnobForge.Core.Scene;
using KnobForge.Rendering.GPU;
using SkiaSharp;

namespace KnobForge.Rendering
{
    public readonly record struct KnobExportProgress(int CompletedFrames, int TotalFrames, string Stage);

    public sealed class KnobExportResult
    {
        public required string OutputDirectory { get; init; }
        public required string FirstFramePath { get; init; }
        public string? SpritesheetPath { get; init; }
        public int ExportedViewCount { get; init; } = 1;
        public int RenderedFrames { get; init; }
        public int SpritesheetWidth { get; init; }
        public int SpritesheetHeight { get; init; }
        public SpritesheetLayout? EffectiveSpritesheetLayout { get; init; }
    }

    public sealed class KnobExporter
    {
        private const int MaxFrames = 1440;
        private const int MaxSpritesheetDimension = 16384;
        private const long MaxSpritesheetPixels = 16384L * 16384L;
        private const int MaxSupersampleScale = 4;

        private readonly KnobProject _project;
        private readonly OrientationDebug _orientation;
        private readonly ViewportCameraState? _cameraState;
        private readonly Func<int, int, ViewportCameraState, SKBitmap?> _gpuFrameProvider;
        private readonly PreviewRenderer _renderer;

        public KnobExporter(
            KnobProject project,
            OrientationDebug orientation,
            ViewportCameraState? cameraState = null,
            Func<int, int, ViewportCameraState, SKBitmap?>? gpuFrameProvider = null)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
            _orientation = orientation ?? throw new ArgumentNullException(nameof(orientation));
            _cameraState = cameraState;
            _gpuFrameProvider = gpuFrameProvider
                ?? throw new ArgumentNullException(nameof(gpuFrameProvider), "GPU-only export requires an offscreen GPU frame provider.");
            _renderer = new PreviewRenderer(_project);
            _renderer.Orientation.InvertX = _orientation.InvertX;
            _renderer.Orientation.InvertY = _orientation.InvertY;
            _renderer.Orientation.InvertZ = _orientation.InvertZ;
            _renderer.Orientation.FlipCamera180 = _orientation.FlipCamera180;
        }

        public Task<KnobExportResult> ExportAsync(
            KnobExportSettings settings,
            string outputPath,
            IProgress<KnobExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var legacy = ResolveLegacyOutput(outputPath);
            return ExportAsync(settings, legacy.OutputRootFolder, legacy.BaseName, progress, cancellationToken);
        }

        public Task<KnobExportResult> ExportAsync(
            KnobExportSettings settings,
            string outputRootFolder,
            string baseName,
            IProgress<KnobExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (string.IsNullOrWhiteSpace(outputRootFolder))
            {
                throw new ArgumentException("Output root folder is required.", nameof(outputRootFolder));
            }

            ValidateBaseName(baseName);

            return Task.Run(() => ExportInternal(settings, outputRootFolder, baseName, progress, cancellationToken), cancellationToken);
        }

        private KnobExportResult ExportInternal(
            KnobExportSettings settings,
            string outputRootFolder,
            string baseName,
            IProgress<KnobExportProgress>? progress,
            CancellationToken cancellationToken)
        {
            ValidateSettings(settings);

            bool exportFrames = settings.ExportIndividualFrames;
            bool exportSpritesheet = settings.ExportSpritesheet;
            int frameCount = settings.FrameCount;
            int resolution = settings.Resolution;
            int supersampleScale = Math.Clamp(settings.SupersampleScale, 1, MaxSupersampleScale);
            int renderResolution = checked(resolution * supersampleScale);
            int paddingPx = Math.Max(0, (int)MathF.Round(settings.Padding));

            int frameDigits = GetFrameNumberDigits(frameCount);
            ExportPathPlan paths = ResolveExportPaths(
                outputRootFolder,
                baseName,
                exportFrames,
                exportSpritesheet,
                frameDigits);

            string outputDirectory = paths.OutputDirectory;
            Directory.CreateDirectory(outputDirectory);
            ValidateOutputPathWritable(outputDirectory);

            var modelNodes = _project.SceneRoot.Children.OfType<ModelNode>().ToList();
            var originalRotations = modelNodes
                .Select(model => (Model: model, Rotation: model.RotationRadians))
                .ToList();

            try
            {
                float referenceRadius = GetSceneReferenceRadius();
                ViewportCameraState baseExportViewportCamera = BuildExportViewportCameraState(
                    referenceRadius,
                    settings,
                    resolution,
                    renderResolution,
                    _cameraState);
                ViewVariant[] viewVariants = ResolveExportViewVariants(settings);
                int totalFrames = checked(frameCount * viewVariants.Length);
                int completedFrames = 0;

                string? firstFramePath = null;
                string? firstSpritesheetPath = null;
                int spritesheetWidth = 0;
                int spritesheetHeight = 0;
                SpritesheetLayout? effectiveLayout = null;

                using var frameBitmap = new SKBitmap(new SKImageInfo(
                    resolution,
                    resolution,
                    SKColorType.Rgba8888,
                    SKAlphaType.Premul));
                using var frameCanvas = new SKCanvas(frameBitmap);
                SKSamplingOptions downsampleSampling = new(new SKCubicResampler(1f / 3f, 1f / 3f));
                SKSamplingOptions directSampling = new(SKFilterMode.Linear, SKMipmapMode.None);
                using var downsamplePaint = new SKPaint
                {
                    BlendMode = SKBlendMode.Src,
                    IsAntialias = true,
                    IsDither = true
                };

                float angleStep = 2f * MathF.PI / frameCount;
                for (int viewIndex = 0; viewIndex < viewVariants.Length; viewIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    ViewVariant viewVariant = viewVariants[viewIndex];
                    ViewportCameraState exportViewportCamera = ApplyViewVariant(baseExportViewportCamera, viewVariant);

                    SpritesheetPlan? spritesheetPlan = null;
                    SKBitmap? spritesheetBitmap = null;
                    SKCanvas? spritesheetCanvas = null;
                    SKPaint? spritesheetPaint = null;
                    try
                    {
                        if (exportSpritesheet)
                        {
                            spritesheetPlan = ResolveSpritesheetPlan(
                                frameCount,
                                resolution,
                                paddingPx,
                                settings.SpritesheetLayout,
                                progress);

                            spritesheetBitmap = new SKBitmap(new SKImageInfo(
                                spritesheetPlan.Value.Width,
                                spritesheetPlan.Value.Height,
                                SKColorType.Rgba8888,
                                SKAlphaType.Premul));
                            spritesheetCanvas = new SKCanvas(spritesheetBitmap);
                            spritesheetCanvas.Clear(new SKColor(0, 0, 0, 0));
                            spritesheetPaint = new SKPaint
                            {
                                BlendMode = SKBlendMode.Src,
                                IsAntialias = false
                            };

                            if (effectiveLayout == null)
                            {
                                spritesheetWidth = spritesheetPlan.Value.Width;
                                spritesheetHeight = spritesheetPlan.Value.Height;
                                effectiveLayout = spritesheetPlan.Value.Layout;
                            }
                        }

                        for (int i = 0; i < frameCount; i++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            progress?.Report(new KnobExportProgress(
                                completedFrames,
                                totalFrames,
                                $"Rendering {viewVariant.DisplayLabel} {i + 1}/{frameCount}"));

                            float angle = i * angleStep;
                            for (int modelIndex = 0; modelIndex < originalRotations.Count; modelIndex++)
                            {
                                var entry = originalRotations[modelIndex];
                                entry.Model.RotationRadians = entry.Rotation + angle;
                            }

                            frameCanvas.Clear(new SKColor(0, 0, 0, 0));

                            using SKBitmap? gpuFrame = _gpuFrameProvider(renderResolution, renderResolution, exportViewportCamera);
                            if (gpuFrame == null)
                            {
                                throw new InvalidOperationException("GPU frame provider returned null frame.");
                            }

                            using SKImage gpuImage = SKImage.FromBitmap(gpuFrame);
                            if (supersampleScale > 1 || gpuFrame.Width != resolution || gpuFrame.Height != resolution)
                            {
                                frameCanvas.DrawImage(
                                    gpuImage,
                                    new SKRect(0, 0, gpuFrame.Width, gpuFrame.Height),
                                    new SKRect(0, 0, resolution, resolution),
                                    downsampleSampling,
                                    downsamplePaint);
                            }
                            else
                            {
                                frameCanvas.DrawImage(
                                    gpuImage,
                                    new SKRect(0, 0, resolution, resolution),
                                    directSampling,
                                    downsamplePaint);
                            }

                            if (exportFrames)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                string framePath = ResolveFramePath(outputDirectory, baseName, viewVariant.FileTag, i, frameDigits);
                                using SKData framePng = frameBitmap.Encode(SKEncodedImageFormat.Png, 100);
                                using FileStream frameOutput = File.Create(framePath);
                                framePng.SaveTo(frameOutput);
                                firstFramePath ??= framePath;
                            }

                            if (exportSpritesheet && spritesheetCanvas != null && spritesheetPaint != null && spritesheetPlan.HasValue)
                            {
                                var origin = spritesheetPlan.Value.GetFrameOrigin(i);
                                spritesheetCanvas.DrawBitmap(frameBitmap, origin.X, origin.Y, spritesheetPaint);
                            }

                            completedFrames++;
                        }

                        if (exportSpritesheet && spritesheetBitmap != null)
                        {
                            progress?.Report(new KnobExportProgress(
                                completedFrames,
                                totalFrames,
                                $"Writing spritesheet ({viewVariant.DisplayLabel})"));
                            cancellationToken.ThrowIfCancellationRequested();

                            string spritesheetPath = ResolveSpritesheetPath(outputDirectory, baseName, viewVariant.FileTag);
                            using SKData sheetPng = spritesheetBitmap.Encode(SKEncodedImageFormat.Png, 100);
                            using FileStream sheetOutput = File.Create(spritesheetPath);
                            sheetPng.SaveTo(sheetOutput);
                            firstSpritesheetPath ??= spritesheetPath;
                        }
                    }
                    finally
                    {
                        spritesheetPaint?.Dispose();
                        spritesheetCanvas?.Dispose();
                        spritesheetBitmap?.Dispose();
                    }
                }

                progress?.Report(new KnobExportProgress(totalFrames, totalFrames, "Writing files"));

                string primaryFirstFramePath = ResolveFramePath(outputDirectory, baseName, 0, frameDigits);
                string primarySpritesheetPath = ResolveSpritesheetPath(outputDirectory, baseName);

                return new KnobExportResult
                {
                    OutputDirectory = outputDirectory,
                    FirstFramePath = exportFrames
                        ? (firstFramePath ?? primaryFirstFramePath)
                        : (firstSpritesheetPath ?? primarySpritesheetPath),
                    SpritesheetPath = exportSpritesheet ? (firstSpritesheetPath ?? primarySpritesheetPath) : null,
                    ExportedViewCount = viewVariants.Length,
                    RenderedFrames = totalFrames,
                    SpritesheetWidth = spritesheetWidth,
                    SpritesheetHeight = spritesheetHeight,
                    EffectiveSpritesheetLayout = effectiveLayout
                };
            }
            finally
            {
                foreach (var entry in originalRotations)
                {
                    entry.Model.RotationRadians = entry.Rotation;
                }
            }
        }

        private static void ValidateSettings(KnobExportSettings settings)
        {
            if (settings.FrameCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(settings.FrameCount), "FrameCount must be greater than zero.");
            }

            if (settings.FrameCount > MaxFrames)
            {
                throw new ArgumentOutOfRangeException(nameof(settings.FrameCount), $"FrameCount must be <= {MaxFrames}.");
            }

            if (settings.Resolution <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(settings.Resolution), "Resolution must be greater than zero.");
            }

            if (settings.Resolution > MaxSpritesheetDimension)
            {
                throw new ArgumentOutOfRangeException(nameof(settings.Resolution), $"Resolution exceeds max dimension {MaxSpritesheetDimension}.");
            }

            if (settings.SupersampleScale < 1 || settings.SupersampleScale > MaxSupersampleScale)
            {
                throw new ArgumentOutOfRangeException(nameof(settings.SupersampleScale), $"SupersampleScale must be between 1 and {MaxSupersampleScale}.");
            }

            int minimumSupersample = GetMinimumSupersampleScaleForResolution(settings.Resolution);
            if (settings.SupersampleScale < minimumSupersample)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(settings.SupersampleScale),
                    $"SupersampleScale {settings.SupersampleScale} is too low for {settings.Resolution}px output. Use at least {minimumSupersample} to avoid visible aliasing.");
            }

            int renderResolution = checked(settings.Resolution * settings.SupersampleScale);
            if (renderResolution > MaxSpritesheetDimension)
            {
                throw new ArgumentOutOfRangeException(nameof(settings.SupersampleScale), $"Resolution * SupersampleScale must be <= {MaxSpritesheetDimension}.");
            }

            long framePixels = (long)settings.Resolution * settings.Resolution;
            if (framePixels > MaxSpritesheetPixels)
            {
                throw new ArgumentOutOfRangeException(nameof(settings.Resolution), "Resolution exceeds maximum pixel budget.");
            }

            if (!settings.ExportIndividualFrames && !settings.ExportSpritesheet)
            {
                throw new InvalidOperationException("Enable at least one export target.");
            }

            if (settings.CameraDistanceScale <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(settings.CameraDistanceScale), "CameraDistanceScale must be > 0.");
            }

            if (settings.Padding < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(settings.Padding), "Padding must be >= 0.");
            }

            if (settings.OrbitVariantYawOffsetDeg < 0f || settings.OrbitVariantYawOffsetDeg > 180f)
            {
                throw new ArgumentOutOfRangeException(nameof(settings.OrbitVariantYawOffsetDeg), "OrbitVariantYawOffsetDeg must be between 0 and 180.");
            }

            if (settings.OrbitVariantPitchOffsetDeg < 0f || settings.OrbitVariantPitchOffsetDeg > 85f)
            {
                throw new ArgumentOutOfRangeException(nameof(settings.OrbitVariantPitchOffsetDeg), "OrbitVariantPitchOffsetDeg must be between 0 and 85.");
            }

        }

        private static int GetMinimumSupersampleScaleForResolution(int resolution)
        {
            if (resolution <= 128)
            {
                return 4;
            }

            if (resolution <= 512)
            {
                return 2;
            }

            return 1;
        }

        private SKPaint? CreateShadowPaint(ShadowConfig config)
        {
            if (!config.Enabled || config.Opacity <= 0f)
            {
                return null;
            }

            byte gray = (byte)Math.Clamp((int)MathF.Round(config.Gray * 255f), 0, 255);
            byte alpha = (byte)Math.Clamp((int)MathF.Round(config.Opacity * 255f), 0, 255);

            var paint = new SKPaint
            {
                BlendMode = SKBlendMode.SrcOver,
                IsAntialias = true,
                ColorFilter = SKColorFilter.CreateBlendMode(new SKColor(gray, gray, gray, alpha), SKBlendMode.SrcIn)
            };

            if (config.BlurPx > 0.01f)
            {
                paint.ImageFilter = SKImageFilter.CreateBlur(config.BlurPx, config.BlurPx);
            }

            return paint;
        }

        private static void ComposeFrameWithDropShadow(
            SKCanvas destinationCanvas,
            SKBitmap destinationBitmap,
            SKCanvas sourceCanvas,
            SKBitmap sourceBitmap,
            SKPaint sourceCopyPaint,
            SKPaint shadowPaint,
            ShadowConfig config)
        {
            sourceCanvas.Clear(new SKColor(0, 0, 0, 0));
            sourceCanvas.DrawBitmap(destinationBitmap, 0, 0, sourceCopyPaint);

            destinationCanvas.Clear(new SKColor(0, 0, 0, 0));

            float width = sourceBitmap.Width;
            float height = sourceBitmap.Height;
            float centerX = width * 0.5f;
            float centerY = height * 0.5f;
            float shadowScale = MathF.Max(0.5f, config.Scale);
            float shadowWidth = width * shadowScale;
            float shadowHeight = height * shadowScale;
            float left = centerX - (shadowWidth * 0.5f) + config.OffsetXPx;
            float top = centerY - (shadowHeight * 0.5f) + config.OffsetYPx;
            var shadowDst = new SKRect(left, top, left + shadowWidth, top + shadowHeight);

            destinationCanvas.DrawBitmap(sourceBitmap, shadowDst, shadowPaint);
            destinationCanvas.DrawBitmap(sourceBitmap, 0, 0, sourceCopyPaint);
        }

        private ShadowConfig ResolveLightDrivenShadowConfig(Camera camera, int resolution)
        {
            if (!_project.ShadowsEnabled)
            {
                return new ShadowConfig(false, 0f, 0f, 0f, 0f, 1f, 0f);
            }

            // Keep Selected shadow mode stable when exporting.
            _project.EnsureSelection();

            if (!TryGetDominantLightDirection(out Vector3 lightDir, out float lightPower))
            {
                return new ShadowConfig(false, 0f, 0f, 0f, 0f, 1f, 0f);
            }

            float sx = Vector3.Dot(lightDir, camera.Right);
            float sy = -Vector3.Dot(lightDir, camera.Up);
            Vector2 screenDir = new(sx, sy);
            if (screenDir.LengthSquared() <= 1e-8f)
            {
                screenDir = new Vector2(0f, 1f);
            }
            else
            {
                // Shadow falls opposite the incoming light direction.
                screenDir = -Vector2.Normalize(screenDir);
            }

            float viewIncidence = MathF.Abs(Vector3.Dot(lightDir, camera.Forward));
            float planarFactor = MathF.Sqrt(MathF.Max(0f, 1f - (viewIncidence * viewIncidence)));
            float intensityNorm = Math.Clamp(lightPower / 3f, 0.15f, 1.25f);

            float offsetMag = resolution * (0.014f + (0.028f * planarFactor)) * intensityNorm * _project.ShadowDistance;
            float blurPx = resolution * (0.010f + (0.020f * _project.ShadowSoftness * (1f - planarFactor)));
            float opacity = Math.Clamp((0.10f + (0.32f * intensityNorm)) * _project.ShadowStrength, 0f, 0.70f);
            float scale = _project.ShadowScale * (1.0f + (0.03f * planarFactor));

            return new ShadowConfig(
                true,
                opacity,
                blurPx,
                screenDir.X * offsetMag,
                screenDir.Y * offsetMag,
                scale,
                _project.ShadowGray);
        }

        private bool TryGetDominantLightDirection(out Vector3 lightDirection, out float lightPower)
        {
            lightDirection = default;
            lightPower = 0f;

            bool TryEvaluate(KnobLight light, out Vector3 dir, out float weight)
            {
                dir = default;
                weight = 0f;
                float intensity = MathF.Max(0f, light.Intensity);
                if (intensity <= 1e-5f)
                {
                    return false;
                }

                if (light.Type == LightType.Directional)
                {
                    dir = ApplyGizmoOrientation(GetDirectionalVector(light));
                    if (dir.LengthSquared() <= 1e-8f)
                    {
                        return false;
                    }

                    dir = Vector3.Normalize(dir);
                }
                else
                {
                    Vector3 lightPos = ApplyGizmoOrientation(new Vector3(light.X, light.Y, light.Z));
                    if (lightPos.LengthSquared() <= 1e-8f)
                    {
                        return false;
                    }

                    dir = Vector3.Normalize(lightPos);
                }

                float luminance = ((0.2126f * light.Color.Red) + (0.7152f * light.Color.Green) + (0.0722f * light.Color.Blue)) / 255f;
                float diffuse = MathF.Max(0f, light.DiffuseBoost);
                float diffuseTerm = 0.35f + (0.65f * MathF.Pow(diffuse, MathF.Max(0f, _project.ShadowDiffuseInfluence)));
                weight = intensity * diffuseTerm * (0.35f + (0.65f * luminance));
                return weight > 1e-6f;
            }

            if (_project.ShadowMode == ShadowLightMode.Selected)
            {
                KnobLight? selected = _project.SelectedLight;
                if (selected == null || !TryEvaluate(selected, out Vector3 selectedDir, out float selectedWeight))
                {
                    return false;
                }

                lightDirection = selectedDir;
                lightPower = selectedWeight;
                return true;
            }

            if (_project.ShadowMode == ShadowLightMode.Dominant)
            {
                float bestWeight = 0f;
                for (int i = 0; i < _project.Lights.Count; i++)
                {
                    if (!TryEvaluate(_project.Lights[i], out Vector3 dir, out float weight) || weight <= bestWeight)
                    {
                        continue;
                    }

                    bestWeight = weight;
                    lightDirection = dir;
                    lightPower = weight;
                }

                return bestWeight > 0f;
            }

            Vector3 weightedDir = Vector3.Zero;
            float totalWeight = 0f;
            for (int i = 0; i < _project.Lights.Count; i++)
            {
                if (!TryEvaluate(_project.Lights[i], out Vector3 dir, out float weight))
                {
                    continue;
                }

                weightedDir += dir * weight;
                totalWeight += weight;
            }

            if (totalWeight <= 1e-6f || weightedDir.LengthSquared() <= 1e-8f)
            {
                return false;
            }

            lightDirection = Vector3.Normalize(weightedDir);
            lightPower = totalWeight;
            return true;
        }

        private Vector3 ApplyGizmoOrientation(Vector3 value)
        {
            if (_orientation.InvertX)
            {
                value.X = -value.X;
            }

            if (_orientation.InvertY)
            {
                value.Y = -value.Y;
            }

            if (_orientation.InvertZ)
            {
                value.Z = -value.Z;
            }

            return value;
        }

        private static Vector3 GetDirectionalVector(KnobLight light)
        {
            float z = light.Z / 300f;
            Vector3 dir = new(MathF.Cos(light.DirectionRadians), MathF.Sin(light.DirectionRadians), z);
            if (dir.LengthSquared() < 1e-6f)
            {
                return Vector3.UnitZ;
            }

            return Vector3.Normalize(dir);
        }

        private static Camera BuildExportCamera(
            float referenceRadius,
            KnobExportSettings settings,
            int outputResolution,
            int renderResolution,
            OrientationDebug orientation,
            ViewportCameraState? cameraState)
        {
            if (cameraState.HasValue)
            {
                ViewportCameraState state = cameraState.Value;
                float yaw = state.OrbitYawDeg * (MathF.PI / 180f);
                float pitch = Math.Clamp(state.OrbitPitchDeg, -85f, 85f) * (MathF.PI / 180f);
                Vector3 forward = Vector3.Normalize(new Vector3(
                    MathF.Sin(yaw) * MathF.Cos(pitch),
                    MathF.Sin(pitch),
                    -MathF.Cos(yaw) * MathF.Cos(pitch)));

                Vector3 worldUp = Vector3.UnitY;
                Vector3 right = Vector3.Cross(worldUp, forward);
                if (right.LengthSquared() < 1e-6f)
                {
                    right = Vector3.UnitX;
                }
                else
                {
                    right = Vector3.Normalize(right);
                }

                Vector3 up = Vector3.Normalize(Vector3.Cross(forward, right));

                if (orientation.InvertX)
                {
                    right *= -1f;
                }

                if (orientation.InvertY)
                {
                    up *= -1f;
                }

                if (orientation.InvertZ)
                {
                    forward *= -1f;
                }

                if (orientation.FlipCamera180)
                {
                    forward = -forward;
                    right = -right;
                }

                float distance = MathF.Max(1f, referenceRadius) * 6f;
                Vector3 position = -forward * distance;
                float resolutionScale = renderResolution / (float)Math.Max(1, outputResolution);
                float zoom = Math.Clamp(state.Zoom * resolutionScale, 0.2f, 32f);
                SKPoint pan = new(state.PanPx.X * resolutionScale, state.PanPx.Y * resolutionScale);
                zoom = MathF.Min(zoom, ComputeSafeZoomForFrame(referenceRadius, renderResolution, settings.Padding * resolutionScale, pan));
                return new Camera(position, forward, right, up, zoom, pan);
            }

            // Fallback when launched without a live viewport state.
            Vector3 fallbackForward = new(0f, 0f, 1f);
            Vector3 fallbackWorldUp = new(0f, 1f, 0f);
            Vector3 fallbackRight = Vector3.Normalize(Vector3.Cross(fallbackWorldUp, fallbackForward));
            Vector3 fallbackUp = Vector3.Normalize(Vector3.Cross(fallbackForward, fallbackRight));
            float fallbackDistance = settings.CameraDistanceScale * MathF.Max(1f, referenceRadius);
            Vector3 fallbackPosition = -fallbackForward * fallbackDistance;
            float padding = MathF.Max(0f, settings.Padding);
            float contentPixels = MathF.Max(1f, renderResolution - (padding * 2f));
            float fallbackZoom = contentPixels / MathF.Max(1f, referenceRadius * 2f);
            return new Camera(fallbackPosition, fallbackForward, fallbackRight, fallbackUp, fallbackZoom, SKPoint.Empty);
        }

        private static ViewportCameraState BuildExportViewportCameraState(
            float referenceRadius,
            KnobExportSettings settings,
            int outputResolution,
            int renderResolution,
            ViewportCameraState? cameraState)
        {
            if (cameraState.HasValue)
            {
                ViewportCameraState state = cameraState.Value;
                float resolutionScale = renderResolution / (float)Math.Max(1, outputResolution);
                float zoom = Math.Clamp(state.Zoom * resolutionScale, 0.2f, 32f);
                SKPoint pan = new(state.PanPx.X * resolutionScale, state.PanPx.Y * resolutionScale);
                zoom = MathF.Min(zoom, ComputeSafeZoomForFrame(referenceRadius, renderResolution, settings.Padding * resolutionScale, pan));
                return new ViewportCameraState(state.OrbitYawDeg, state.OrbitPitchDeg, zoom, pan);
            }

            float padding = MathF.Max(0f, settings.Padding);
            float contentPixels = MathF.Max(1f, renderResolution - (padding * 2f));
            float zoomFallback = contentPixels / MathF.Max(1f, referenceRadius * 2f);
            return new ViewportCameraState(30f, -20f, zoomFallback, SKPoint.Empty);
        }

        private static ViewVariant[] ResolveExportViewVariants(KnobExportSettings settings)
        {
            var variants = new List<ViewVariant>(5);
            var dedupe = new HashSet<(int Yaw, int Pitch)>();

            void AddVariant(ViewVariant variant)
            {
                var key = (QuantizeAngle(variant.YawOffsetDeg), QuantizeAngle(variant.PitchOffsetDeg));
                if (dedupe.Add(key))
                {
                    variants.Add(variant);
                }
            }

            AddVariant(new ViewVariant(string.Empty, "Primary", 0f, 0f));

            if (settings.ExportOrbitVariants)
            {
                float yaw = MathF.Abs(settings.OrbitVariantYawOffsetDeg);
                float pitch = MathF.Abs(settings.OrbitVariantPitchOffsetDeg);

                AddVariant(new ViewVariant("under_left", "Under Left", -yaw, pitch));
                AddVariant(new ViewVariant("under_right", "Under Right", yaw, pitch));
                AddVariant(new ViewVariant("over_left", "Over Left", -yaw, -pitch));
                AddVariant(new ViewVariant("over_right", "Over Right", yaw, -pitch));
            }

            return variants.ToArray();
        }

        private static ViewportCameraState ApplyViewVariant(ViewportCameraState baseState, ViewVariant variant)
        {
            float yaw = baseState.OrbitYawDeg + variant.YawOffsetDeg;
            float pitch = Math.Clamp(baseState.OrbitPitchDeg + variant.PitchOffsetDeg, -85f, 85f);
            return new ViewportCameraState(yaw, pitch, baseState.Zoom, baseState.PanPx);
        }

        private static int QuantizeAngle(float value)
        {
            return (int)MathF.Round(value * 1000f);
        }

        private static float ComputeSafeZoomForFrame(
            float referenceRadius,
            int renderResolution,
            float paddingPx,
            SKPoint panPx)
        {
            float radius = MathF.Max(1f, referenceRadius);
            float halfWidthAvailable = MathF.Max(1f, (renderResolution * 0.5f) - paddingPx - MathF.Abs(panPx.X));
            float halfHeightAvailable = MathF.Max(1f, (renderResolution * 0.5f) - paddingPx - MathF.Abs(panPx.Y));
            float halfSpan = MathF.Min(halfWidthAvailable, halfHeightAvailable);
            // Leave a little guard band so rotating protrusions don't clip due rasterization/AA.
            return MathF.Max(0.2f, (halfSpan * 0.96f) / radius);
        }

        private float GetSceneReferenceRadius()
        {
            float maxReferenceRadius = MathF.Max(1f, _renderer.GetMaxModelReferenceRadius());

            MetalMesh? mesh = MetalMeshBuilder.TryBuildFromProject(_project);
            if (mesh != null)
            {
                maxReferenceRadius = MathF.Max(maxReferenceRadius, mesh.ReferenceRadius);
            }

            CollarMesh? collarMesh = CollarMeshBuilder.TryBuildFromProject(_project);
            if (collarMesh != null)
            {
                maxReferenceRadius = MathF.Max(maxReferenceRadius, collarMesh.ReferenceRadius);
            }

            return maxReferenceRadius;
        }

        private static SpritesheetPlan ResolveSpritesheetPlan(
            int frameCount,
            int resolution,
            int paddingPx,
            SpritesheetLayout requestedLayout,
            IProgress<KnobExportProgress>? progress)
        {
            if (requestedLayout == SpritesheetLayout.Horizontal)
            {
                SpritesheetPlan horizontal = BuildHorizontalPlan(frameCount, resolution, paddingPx);
                if (horizontal.Width > MaxSpritesheetDimension)
                {
                    progress?.Report(new KnobExportProgress(0, frameCount, "Horizontal layout exceeds max width; switching to grid"));
                    requestedLayout = SpritesheetLayout.Grid;
                }
                else
                {
                    ValidateSpritesheetPlan(horizontal);
                    return horizontal;
                }
            }

            SpritesheetPlan grid = BuildGridPlan(frameCount, resolution, paddingPx);
            ValidateSpritesheetPlan(grid);
            return grid;
        }

        private static SpritesheetPlan BuildHorizontalPlan(int frameCount, int resolution, int paddingPx)
        {
            long width = ((long)frameCount * resolution) + (((long)frameCount + 1L) * paddingPx);
            long height = resolution + (2L * paddingPx);
            return new SpritesheetPlan(
                SpritesheetLayout.Horizontal,
                ToIntChecked(width, "Horizontal spritesheet width"),
                ToIntChecked(height, "Horizontal spritesheet height"),
                1,
                resolution,
                paddingPx);
        }

        private static SpritesheetPlan BuildGridPlan(int frameCount, int resolution, int paddingPx)
        {
            int gridSize = (int)Math.Ceiling(Math.Sqrt(frameCount));
            long width = ((long)gridSize * resolution) + (((long)gridSize + 1L) * paddingPx);
            long height = ((long)gridSize * resolution) + (((long)gridSize + 1L) * paddingPx);
            return new SpritesheetPlan(
                SpritesheetLayout.Grid,
                ToIntChecked(width, "Grid spritesheet width"),
                ToIntChecked(height, "Grid spritesheet height"),
                gridSize,
                resolution,
                paddingPx);
        }

        private static void ValidateSpritesheetPlan(SpritesheetPlan plan)
        {
            if (plan.Width <= 0 || plan.Height <= 0)
            {
                throw new InvalidOperationException("Spritesheet dimensions must be positive.");
            }

            if (plan.Width > MaxSpritesheetDimension || plan.Height > MaxSpritesheetDimension)
            {
                throw new InvalidOperationException(
                    $"Spritesheet dimensions {plan.Width}x{plan.Height} exceed max {MaxSpritesheetDimension}.");
            }

            long pixels = (long)plan.Width * plan.Height;
            if (pixels > MaxSpritesheetPixels)
            {
                throw new InvalidOperationException(
                    $"Spritesheet pixel count {pixels} exceeds max {MaxSpritesheetPixels}.");
            }
        }

        private static int ToIntChecked(long value, string fieldName)
        {
            if (value <= 0 || value > int.MaxValue)
            {
                throw new InvalidOperationException($"{fieldName} is out of supported range: {value}.");
            }

            return (int)value;
        }

        private static int GetFrameNumberDigits(int frameCount)
        {
            int digits = (frameCount - 1).ToString(CultureInfo.InvariantCulture).Length;
            return Math.Max(2, digits);
        }

        private static string ResolveFramePath(string outputDirectory, string baseName, int frameIndex, int digits)
        {
            return ResolveFramePath(outputDirectory, baseName, string.Empty, frameIndex, digits);
        }

        private static string ResolveFramePath(string outputDirectory, string baseName, string viewTag, int frameIndex, int digits)
        {
            string number = frameIndex.ToString($"D{digits}");
            string prefix = string.IsNullOrWhiteSpace(viewTag) ? baseName : $"{baseName}_{viewTag}";
            return Path.Combine(outputDirectory, $"{prefix}_{number}.png");
        }

        private static string ResolveSpritesheetPath(string outputDirectory, string baseName)
        {
            return ResolveSpritesheetPath(outputDirectory, baseName, string.Empty);
        }

        private static string ResolveSpritesheetPath(string outputDirectory, string baseName, string viewTag)
        {
            string prefix = string.IsNullOrWhiteSpace(viewTag) ? baseName : $"{baseName}_{viewTag}";
            return Path.Combine(outputDirectory, $"{prefix}_spritesheet.png");
        }

        private static ExportPathPlan ResolveExportPaths(
            string outputRootFolder,
            string baseName,
            bool exportFrames,
            bool exportSpritesheet,
            int frameDigits)
        {
            string outputRoot = Path.GetFullPath(outputRootFolder);
            string outputDirectory = exportFrames && exportSpritesheet
                ? Path.Combine(outputRoot, baseName)
                : outputRoot;

            string firstFramePath = ResolveFramePath(outputDirectory, baseName, 0, frameDigits);
            string spritesheetPath = ResolveSpritesheetPath(outputDirectory, baseName);
            string firstPath = exportFrames ? firstFramePath : spritesheetPath;

            return new ExportPathPlan(outputDirectory, firstPath, spritesheetPath);
        }

        private static LegacyExportOutput ResolveLegacyOutput(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("Output path is required.", nameof(outputPath));
            }

            string fullPath = Path.GetFullPath(outputPath);
            if (!Path.HasExtension(fullPath))
            {
                return new LegacyExportOutput(fullPath, "frame");
            }

            string? directory = Path.GetDirectoryName(fullPath);
            string outputRootFolder = string.IsNullOrWhiteSpace(directory) ? Directory.GetCurrentDirectory() : directory;
            string baseName = Path.GetFileNameWithoutExtension(fullPath);

            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = "frame";
            }

            const string spritesheetSuffix = "_spritesheet";
            if (baseName.EndsWith(spritesheetSuffix, StringComparison.OrdinalIgnoreCase))
            {
                string trimmed = baseName[..^spritesheetSuffix.Length];
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    baseName = trimmed;
                }
            }

            return new LegacyExportOutput(outputRootFolder, baseName);
        }

        private static void ValidateBaseName(string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName))
            {
                throw new ArgumentException("Base name is required.", nameof(baseName));
            }

            if (baseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException("Base name contains invalid filename characters.", nameof(baseName));
            }
        }

        private static void ValidateOutputPathWritable(string outputDirectory)
        {
            string probePath = Path.Combine(outputDirectory, $".knobforge_write_probe_{Guid.NewGuid():N}");
            using (File.Create(probePath))
            {
            }

            File.Delete(probePath);
        }

        private readonly record struct SpritesheetPlan(
            SpritesheetLayout Layout,
            int Width,
            int Height,
            int GridSize,
            int Resolution,
            int PaddingPx)
        {
            public (int X, int Y) GetFrameOrigin(int frameIndex)
            {
                if (Layout == SpritesheetLayout.Horizontal)
                {
                    int x = PaddingPx + frameIndex * (Resolution + PaddingPx);
                    return (x, PaddingPx);
                }

                int col = frameIndex % GridSize;
                int row = frameIndex / GridSize;
                int gx = PaddingPx + col * (Resolution + PaddingPx);
                int gy = PaddingPx + row * (Resolution + PaddingPx);
                return (gx, gy);
            }
        }

        private readonly record struct ViewVariant(
            string FileTag,
            string DisplayLabel,
            float YawOffsetDeg,
            float PitchOffsetDeg);

        private readonly record struct ExportPathPlan(
            string OutputDirectory,
            string FirstFramePath,
            string SpritesheetPath);

        private readonly record struct ShadowConfig(
            bool Enabled,
            float Opacity,
            float BlurPx,
            float OffsetXPx,
            float OffsetYPx,
            float Scale,
            float Gray);

        private readonly record struct LegacyExportOutput(
            string OutputRootFolder,
            string BaseName);
    }
}
