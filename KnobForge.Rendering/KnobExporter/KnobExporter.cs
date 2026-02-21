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

    public sealed partial class KnobExporter
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

    }
}
