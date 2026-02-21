using Avalonia.Interactivity;
using KnobForge.Core;
using KnobForge.Core.Scene;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private sealed class UserReferenceProfileStore
        {
            public int Version { get; set; } = 1;
            public List<UserReferenceProfile> Profiles { get; set; } = new();
        }

        private sealed class UserReferenceProfile
        {
            public string Name { get; set; } = string.Empty;
            public UserReferenceProfileSnapshot Snapshot { get; set; } = new();
        }

        private sealed class UserReferenceProfileSnapshot
        {
            public bool HasLightingEnvironmentShadowSnapshot { get; set; }
            public LightingMode Mode { get; set; } = LightingMode.Both;
            public float EnvironmentTopColorX { get; set; } = 0.34f;
            public float EnvironmentTopColorY { get; set; } = 0.36f;
            public float EnvironmentTopColorZ { get; set; } = 0.37f;
            public float EnvironmentBottomColorX { get; set; }
            public float EnvironmentBottomColorY { get; set; }
            public float EnvironmentBottomColorZ { get; set; }
            public float EnvironmentIntensity { get; set; } = 0.36f;
            public float EnvironmentRoughnessMix { get; set; } = 1f;
            public bool ShadowsEnabled { get; set; } = true;
            public ShadowLightMode ShadowMode { get; set; } = ShadowLightMode.Weighted;
            public float ShadowStrength { get; set; } = 1f;
            public float ShadowSoftness { get; set; } = 0.55f;
            public float ShadowDistance { get; set; } = 1f;
            public float ShadowScale { get; set; } = 1f;
            public float ShadowQuality { get; set; } = 0.65f;
            public float ShadowGray { get; set; } = 0.14f;
            public float ShadowDiffuseInfluence { get; set; } = 1f;
            public List<LightStateSnapshot> Lights { get; set; } = new();
            public int SelectedLightIndex { get; set; }
            public float Radius { get; set; } = 220f;
            public float Height { get; set; } = 120f;
            public float Bevel { get; set; } = 18f;
            public float TopRadiusScale { get; set; } = 0.86f;
            public int RadialSegments { get; set; } = 180;
            public float RotationRadians { get; set; }
            public float SpiralRidgeHeight { get; set; } = 19.89f;
            public float SpiralRidgeWidth { get; set; } = 18.92f;
            public float SpiralRidgeHeightVariance { get; set; } = 0.15f;
            public float SpiralRidgeWidthVariance { get; set; } = 0.12f;
            public float SpiralHeightVarianceThreshold { get; set; } = 0.45f;
            public float SpiralWidthVarianceThreshold { get; set; } = 0.45f;
            public float SpiralTurns { get; set; } = 150f;
            public float CrownProfile { get; set; }
            public float BevelCurve { get; set; } = 1f;
            public float BodyTaper { get; set; }
            public float BodyBulge { get; set; }
            public GripType GripType { get; set; } = GripType.None;
            public GripStyle GripStyle { get; set; } = GripStyle.BoutiqueSynthPremium;
            public BodyStyle BodyStyle { get; set; } = BodyStyle.Straight;
            public MountPreset MountPreset { get; set; } = MountPreset.Custom;
            public float GripStart { get; set; } = 0.15f;
            public float GripHeight { get; set; } = 0.55f;
            public float GripDensity { get; set; } = 60f;
            public float GripPitch { get; set; } = 6f;
            public float GripDepth { get; set; } = 1.2f;
            public float GripWidth { get; set; } = 1.2f;
            public float GripSharpness { get; set; } = 1f;
            public float BoreRadiusRatio { get; set; }
            public float BoreDepthRatio { get; set; }
            public float CollarWidthRatio { get; set; }
            public float CollarHeightRatio { get; set; }
            public float IndicatorGrooveDepthRatio { get; set; }
            public float IndicatorGrooveWidthDegrees { get; set; } = 7f;
            public float IndicatorRadiusRatio { get; set; } = 0.70f;
            public float IndicatorLengthRatio { get; set; } = 0.22f;
            public bool IndicatorEnabled { get; set; } = true;
            public IndicatorShape IndicatorShape { get; set; } = IndicatorShape.Bar;
            public IndicatorRelief IndicatorRelief { get; set; } = IndicatorRelief.Extrude;
            public IndicatorProfile IndicatorProfile { get; set; } = IndicatorProfile.Straight;
            public float IndicatorWidthRatio { get; set; } = 0.06f;
            public float IndicatorLengthRatioTop { get; set; } = 0.28f;
            public float IndicatorPositionRatio { get; set; } = 0.46f;
            public float IndicatorThicknessRatio { get; set; } = 0.012f;
            public float IndicatorRoundness { get; set; }
            public float IndicatorColorX { get; set; } = 0.97f;
            public float IndicatorColorY { get; set; } = 0.96f;
            public float IndicatorColorZ { get; set; } = 0.92f;
            public float IndicatorColorBlend { get; set; } = 1f;
            public bool IndicatorCadWallsEnabled { get; set; } = true;
            public float MaterialBaseColorX { get; set; } = 0.55f;
            public float MaterialBaseColorY { get; set; } = 0.16f;
            public float MaterialBaseColorZ { get; set; } = 0.16f;
            public float MaterialMetallic { get; set; } = 1f;
            public float MaterialRoughness { get; set; } = 0.04f;
            public float MaterialPearlescence { get; set; }
            public float MaterialRustAmount { get; set; }
            public float MaterialWearAmount { get; set; }
            public float MaterialGunkAmount { get; set; }
            public float MaterialRadialBrushStrength { get; set; } = 0.65f;
            public float MaterialRadialBrushDensity { get; set; } = 280.5f;
            public float MaterialSurfaceCharacter { get; set; } = 1f;
            public float MaterialSpecularPower { get; set; } = 64f;
            public float MaterialDiffuseStrength { get; set; } = 1f;
            public float MaterialSpecularStrength { get; set; } = 1f;
            public bool MaterialPartMaterialsEnabled { get; set; }
            public float MaterialTopBaseColorX { get; set; } = 0.55f;
            public float MaterialTopBaseColorY { get; set; } = 0.16f;
            public float MaterialTopBaseColorZ { get; set; } = 0.16f;
            public float MaterialTopMetallic { get; set; } = 1f;
            public float MaterialTopRoughness { get; set; } = 0.04f;
            public float MaterialBevelBaseColorX { get; set; } = 0.55f;
            public float MaterialBevelBaseColorY { get; set; } = 0.16f;
            public float MaterialBevelBaseColorZ { get; set; } = 0.16f;
            public float MaterialBevelMetallic { get; set; } = 1f;
            public float MaterialBevelRoughness { get; set; } = 0.04f;
            public float MaterialSideBaseColorX { get; set; } = 0.55f;
            public float MaterialSideBaseColorY { get; set; } = 0.16f;
            public float MaterialSideBaseColorZ { get; set; } = 0.16f;
            public float MaterialSideMetallic { get; set; } = 1f;
            public float MaterialSideRoughness { get; set; } = 0.04f;
            public bool BrushPaintingEnabled { get; set; }
            public PaintBrushType BrushType { get; set; } = PaintBrushType.Spray;
            public PaintChannel BrushChannel { get; set; } = PaintChannel.Rust;
            public ScratchAbrasionType ScratchAbrasionType { get; set; } = ScratchAbrasionType.Needle;
            public float BrushSizePx { get; set; } = 32f;
            public float BrushOpacity { get; set; } = 0.5f;
            public float BrushSpread { get; set; } = 0.35f;
            public float BrushDarkness { get; set; } = 0.58f;
            public float PaintCoatMetallic { get; set; } = 0.02f;
            public float PaintCoatRoughness { get; set; } = 0.56f;
            public float ClearCoatAmount { get; set; }
            public float ClearCoatRoughness { get; set; } = 0.18f;
            public float AnisotropyAngleDegrees { get; set; }
            public float PaintColorX { get; set; } = 0.85f;
            public float PaintColorY { get; set; } = 0.24f;
            public float PaintColorZ { get; set; } = 0.24f;
            public float ScratchWidthPx { get; set; } = 20f;
            public float ScratchDepth { get; set; } = 0.45f;
            public float ScratchDragResistance { get; set; } = 0.38f;
            public float ScratchDepthRamp { get; set; } = 0.0015f;
            public float ScratchExposeColorX { get; set; } = 0.88f;
            public float ScratchExposeColorY { get; set; } = 0.88f;
            public float ScratchExposeColorZ { get; set; } = 0.90f;
            public float ScratchExposeMetallic { get; set; } = 0.92f;
            public float ScratchExposeRoughness { get; set; } = 0.20f;
            public bool SpiralNormalInfluenceEnabled { get; set; } = true;
            public float SpiralNormalLodFadeStart { get; set; } = 4.22f;
            public float SpiralNormalLodFadeEnd { get; set; } = 4.23f;
            public float SpiralRoughnessLodBoost { get; set; } = 0.78f;
            public CollarStateSnapshot? CollarSnapshot { get; set; }
        }

        private sealed class ReferenceStyleOption
        {
            public ReferenceStyleOption(ReferenceKnobStyle? builtInStyle, string? userProfileName, string displayName, bool isSelectable = true)
            {
                BuiltInStyle = builtInStyle;
                UserProfileName = userProfileName;
                DisplayName = displayName;
                IsSelectable = isSelectable;
            }

            public ReferenceKnobStyle? BuiltInStyle { get; }
            public string? UserProfileName { get; }
            public string DisplayName { get; }
            public bool IsSelectable { get; }

            public static ReferenceStyleOption CreateGroupLabel(string displayName)
            {
                return new ReferenceStyleOption(null, null, displayName, false);
            }

            public override string ToString() => DisplayName;
        }
    }
}
