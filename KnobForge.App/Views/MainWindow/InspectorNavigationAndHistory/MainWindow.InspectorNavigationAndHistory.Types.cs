using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using KnobForge.Core;
using KnobForge.Core.Scene;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private sealed class InspectorUndoSnapshot
        {
            public LightingMode Mode { get; set; }
            public BasisDebugMode BasisDebug { get; set; }
            public float EnvironmentTopColorX { get; set; }
            public float EnvironmentTopColorY { get; set; }
            public float EnvironmentTopColorZ { get; set; }
            public float EnvironmentBottomColorX { get; set; }
            public float EnvironmentBottomColorY { get; set; }
            public float EnvironmentBottomColorZ { get; set; }
            public float EnvironmentIntensity { get; set; }
            public float EnvironmentRoughnessMix { get; set; }
            public bool ShadowsEnabled { get; set; }
            public ShadowLightMode ShadowMode { get; set; } = ShadowLightMode.Weighted;
            public float ShadowStrength { get; set; }
            public float ShadowSoftness { get; set; }
            public float ShadowDistance { get; set; }
            public float ShadowScale { get; set; }
            public float ShadowQuality { get; set; }
            public float ShadowGray { get; set; }
            public float ShadowDiffuseInfluence { get; set; }
            public int PaintHistoryRevision { get; set; }
            public int ActivePaintLayerIndex { get; set; }
            public int FocusedPaintLayerIndex { get; set; } = -1;
            public bool BrushPaintingEnabled { get; set; }
            public PaintBrushType BrushType { get; set; }
            public PaintChannel BrushChannel { get; set; }
            public ScratchAbrasionType ScratchAbrasionType { get; set; }
            public float BrushSizePx { get; set; }
            public float BrushOpacity { get; set; }
            public float BrushSpread { get; set; }
            public float BrushDarkness { get; set; }
            public float PaintCoatMetallic { get; set; } = 0.02f;
            public float PaintCoatRoughness { get; set; } = 0.56f;
            public float ClearCoatAmount { get; set; }
            public float ClearCoatRoughness { get; set; } = 0.18f;
            public float AnisotropyAngleDegrees { get; set; }
            public float PaintColorX { get; set; }
            public float PaintColorY { get; set; }
            public float PaintColorZ { get; set; }
            public float ScratchWidthPx { get; set; }
            public float ScratchDepth { get; set; }
            public float ScratchDragResistance { get; set; }
            public float ScratchDepthRamp { get; set; }
            public float ScratchExposeColorX { get; set; }
            public float ScratchExposeColorY { get; set; }
            public float ScratchExposeColorZ { get; set; }
            public float ScratchExposeMetallic { get; set; } = 0.92f;
            public float ScratchExposeRoughness { get; set; } = 0.20f;
            public bool SpiralNormalInfluenceEnabled { get; set; }
            public float SpiralNormalLodFadeStart { get; set; }
            public float SpiralNormalLodFadeEnd { get; set; }
            public float SpiralRoughnessLodBoost { get; set; }
            public List<LightStateSnapshot> Lights { get; set; } = new();
            public int SelectedLightIndex { get; set; }
            public bool HasModelMaterialSnapshot { get; set; }
            public UserReferenceProfileSnapshot? ModelMaterialSnapshot { get; set; }
            public ReferenceKnobStyle ModelReferenceStyle { get; set; } = ReferenceKnobStyle.Custom;
            public string? SelectedUserReferenceProfileName { get; set; }
            public CollarStateSnapshot? CollarSnapshot { get; set; }
            public SceneSelectionSnapshot Selection { get; set; } = new();
        }

        private sealed class LightStateSnapshot
        {
            public string Name { get; set; } = "Light";
            public LightType Type { get; set; } = LightType.Point;
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
            public float DirectionRadians { get; set; }
            public byte ColorR { get; set; }
            public byte ColorG { get; set; }
            public byte ColorB { get; set; }
            public byte ColorA { get; set; } = byte.MaxValue;
            public float Intensity { get; set; }
            public float Falloff { get; set; }
            public float DiffuseBoost { get; set; }
            public float SpecularBoost { get; set; }
            public float SpecularPower { get; set; }
        }

        private sealed class CollarStateSnapshot
        {
            public bool Enabled { get; set; }
            public CollarPreset Preset { get; set; }
            public float InnerRadiusRatio { get; set; }
            public float GapToKnobRatio { get; set; }
            public float ElevationRatio { get; set; }
            public float OverallRotationRadians { get; set; }
            public float BiteAngleRadians { get; set; }
            public float BodyRadiusRatio { get; set; }
            public float BodyEllipseYScale { get; set; }
            public float NeckTaper { get; set; }
            public float TailTaper { get; set; }
            public float MassBias { get; set; }
            public float TailUnderlap { get; set; }
            public float HeadScale { get; set; }
            public float JawBulge { get; set; }
            public bool UvSeamFollowBite { get; set; }
            public float UvSeamOffset { get; set; }
            public int PathSegments { get; set; }
            public int CrossSegments { get; set; }
            public float BaseColorX { get; set; }
            public float BaseColorY { get; set; }
            public float BaseColorZ { get; set; }
            public float Metallic { get; set; }
            public float Roughness { get; set; }
            public float Pearlescence { get; set; }
            public float RustAmount { get; set; }
            public float WearAmount { get; set; }
            public float GunkAmount { get; set; }
            public float NormalStrength { get; set; }
            public float HeightStrength { get; set; }
            public float ScaleDensity { get; set; }
            public float ScaleRelief { get; set; }
            public string ImportedMeshPath { get; set; } = string.Empty;
            public float ImportedScale { get; set; }
            public float ImportedRotationRadians { get; set; }
            public bool ImportedMirrorX { get; set; }
            public bool ImportedMirrorY { get; set; }
            public bool ImportedMirrorZ { get; set; }
            public float ImportedHeadAngleOffsetRadians { get; set; }
            public float ImportedOffsetXRatio { get; set; }
            public float ImportedOffsetYRatio { get; set; }
            public float ImportedInflateRatio { get; set; }
            public float ImportedBodyLengthScale { get; set; }
            public float ImportedBodyThicknessScale { get; set; }
            public float ImportedHeadLengthScale { get; set; }
            public float ImportedHeadThicknessScale { get; set; }
        }

        private sealed class SceneSelectionSnapshot
        {
            public SceneSelectionKind Kind { get; set; } = SceneSelectionKind.Unknown;
            public int LightIndex { get; set; } = -1;
        }

        private enum SceneSelectionKind
        {
            Unknown = 0,
            SceneRoot = 1,
            Model = 2,
            Material = 3,
            Collar = 4,
            Light = 5
        }
    }
}
