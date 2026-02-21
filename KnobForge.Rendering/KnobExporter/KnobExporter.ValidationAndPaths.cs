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

namespace KnobForge.Rendering;

public sealed partial class KnobExporter
{
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
