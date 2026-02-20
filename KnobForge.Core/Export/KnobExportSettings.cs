using System.Collections.Generic;

namespace KnobForge.Core.Export
{
    public enum SpritesheetLayout
    {
        Horizontal,
        Grid
    }

    public enum ExportFilterPreset
    {
        None,
        Neutral,
        Cinematic,
        HighContrast,
        Matte
    }

    public enum ExportOutputStrategy
    {
        JuceFilmstripBestDefault,
        JuceFilmstripRetina,
        IPlug2Filmstrip101,
        HiseFilmstrip128,
        AtlasGridMaster
    }

    public readonly record struct ExportOutputStrategyDefinition(
        ExportOutputStrategy Strategy,
        string DisplayName,
        string Description,
        int FrameCount,
        int Resolution,
        int SupersampleScale,
        SpritesheetLayout SpritesheetLayout,
        float Padding,
        bool ExportIndividualFrames,
        bool ExportSpritesheet,
        float CameraDistanceScale,
        ExportFilterPreset FilterPreset);

    public static class ExportOutputStrategies
    {
        private static readonly ExportOutputStrategyDefinition[] Definitions =
        new[]
        {
            new ExportOutputStrategyDefinition(
                ExportOutputStrategy.JuceFilmstripBestDefault,
                "JUCE Filmstrip Best (Default)",
                "Recommended default for JUCE: 64 x 256px frames in one horizontal filmstrip PNG (16384px wide), good quality and safe texture width.",
                64,
                256,
                2,
                SpritesheetLayout.Horizontal,
                0f,
                false,
                true,
                6f,
                ExportFilterPreset.None),
            new ExportOutputStrategyDefinition(
                ExportOutputStrategy.JuceFilmstripRetina,
                "JUCE Filmstrip Retina",
                "Higher per-frame detail for large desktop knobs: 32 x 512px horizontal filmstrip.",
                32,
                512,
                2,
                SpritesheetLayout.Horizontal,
                0f,
                false,
                true,
                6f,
                ExportFilterPreset.None),
            new ExportOutputStrategyDefinition(
                ExportOutputStrategy.IPlug2Filmstrip101,
                "iPlug2 Filmstrip 101",
                "Standard 0..100 normalized step strip for iPlug2-style UIs: 101 x 128px horizontal filmstrip.",
                101,
                128,
                2,
                SpritesheetLayout.Horizontal,
                0f,
                false,
                true,
                6f,
                ExportFilterPreset.None),
            new ExportOutputStrategyDefinition(
                ExportOutputStrategy.HiseFilmstrip128,
                "HISE/Kontakt Filmstrip 128",
                "Smooth 128-step filmstrip preset used in many sampler/plugin workflows: 128 x 128px horizontal strip.",
                128,
                128,
                2,
                SpritesheetLayout.Horizontal,
                0f,
                false,
                true,
                6f,
                ExportFilterPreset.None),
            new ExportOutputStrategyDefinition(
                ExportOutputStrategy.AtlasGridMaster,
                "Atlas Master (Grid + Frames)",
                "Production master for downstream transcode pipelines (BC7/WebP/AVIF externally): 156 x 256px grid sheet plus frame sequence.",
                156,
                256,
                2,
                SpritesheetLayout.Grid,
                2f,
                true,
                true,
                6f,
                ExportFilterPreset.None)
        };

        public static IReadOnlyList<ExportOutputStrategyDefinition> All => Definitions;

        public static ExportOutputStrategyDefinition Get(ExportOutputStrategy strategy)
        {
            foreach (ExportOutputStrategyDefinition definition in Definitions)
            {
                if (definition.Strategy == strategy)
                {
                    return definition;
                }
            }

            return Definitions[0];
        }

        public static KnobExportSettings CreateSettings(ExportOutputStrategy strategy)
        {
            ExportOutputStrategyDefinition definition = Get(strategy);
            return new KnobExportSettings
            {
                Strategy = definition.Strategy,
                FrameCount = definition.FrameCount,
                Resolution = definition.Resolution,
                SupersampleScale = definition.SupersampleScale,
                SpritesheetLayout = definition.SpritesheetLayout,
                Padding = definition.Padding,
                FilterPreset = definition.FilterPreset,
                ExportIndividualFrames = definition.ExportIndividualFrames,
                ExportSpritesheet = definition.ExportSpritesheet,
                CameraDistanceScale = definition.CameraDistanceScale
            };
        }
    }

    public sealed class KnobExportSettings
    {
        private static readonly ExportOutputStrategyDefinition DefaultStrategy =
            ExportOutputStrategies.Get(ExportOutputStrategy.JuceFilmstripBestDefault);
        private const float DefaultOrbitVariantYawOffsetDeg = 12f;
        private const float DefaultOrbitVariantPitchOffsetDeg = 8f;

        public ExportOutputStrategy Strategy { get; set; } = DefaultStrategy.Strategy;
        public int FrameCount { get; set; } = DefaultStrategy.FrameCount;
        public int Resolution { get; set; } = DefaultStrategy.Resolution;
        public int SupersampleScale { get; set; } = DefaultStrategy.SupersampleScale;
        public SpritesheetLayout SpritesheetLayout { get; set; } = DefaultStrategy.SpritesheetLayout;
        public float Padding { get; set; } = DefaultStrategy.Padding;
        public ExportFilterPreset FilterPreset { get; set; } = DefaultStrategy.FilterPreset;
        public bool ExportIndividualFrames { get; set; } = DefaultStrategy.ExportIndividualFrames;
        public bool ExportSpritesheet { get; set; } = DefaultStrategy.ExportSpritesheet;
        public float CameraDistanceScale { get; set; } = DefaultStrategy.CameraDistanceScale;
        public bool ExportOrbitVariants { get; set; }
        public float OrbitVariantYawOffsetDeg { get; set; } = DefaultOrbitVariantYawOffsetDeg;
        public float OrbitVariantPitchOffsetDeg { get; set; } = DefaultOrbitVariantPitchOffsetDeg;
    }
}
