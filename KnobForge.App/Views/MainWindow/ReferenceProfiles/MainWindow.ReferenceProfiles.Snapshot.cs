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
        private static UserReferenceProfileSnapshot CaptureUserReferenceProfileSnapshot(
            KnobProject project,
            ModelNode model,
            MaterialNode material,
            CollarNode? collar = null,
            bool includeLightingEnvironmentShadowAndLights = false)
        {
            UserReferenceProfileSnapshot snapshot = new()
            {
                Radius = model.Radius,
                Height = model.Height,
                Bevel = model.Bevel,
                TopRadiusScale = model.TopRadiusScale,
                RadialSegments = model.RadialSegments,
                RotationRadians = model.RotationRadians,
                SpiralRidgeHeight = model.SpiralRidgeHeight,
                SpiralRidgeWidth = model.SpiralRidgeWidth,
                SpiralRidgeHeightVariance = model.SpiralRidgeHeightVariance,
                SpiralRidgeWidthVariance = model.SpiralRidgeWidthVariance,
                SpiralHeightVarianceThreshold = model.SpiralHeightVarianceThreshold,
                SpiralWidthVarianceThreshold = model.SpiralWidthVarianceThreshold,
                SpiralTurns = model.SpiralTurns,
                CrownProfile = model.CrownProfile,
                BevelCurve = model.BevelCurve,
                BodyTaper = model.BodyTaper,
                BodyBulge = model.BodyBulge,
                GripType = model.GripType,
                GripStyle = model.GripStyle,
                BodyStyle = model.BodyStyle,
                MountPreset = model.MountPreset,
                GripStart = model.GripStart,
                GripHeight = model.GripHeight,
                GripDensity = model.GripDensity,
                GripPitch = model.GripPitch,
                GripDepth = model.GripDepth,
                GripWidth = model.GripWidth,
                GripSharpness = model.GripSharpness,
                BoreRadiusRatio = model.BoreRadiusRatio,
                BoreDepthRatio = model.BoreDepthRatio,
                CollarWidthRatio = model.CollarWidthRatio,
                CollarHeightRatio = model.CollarHeightRatio,
                IndicatorGrooveDepthRatio = model.IndicatorGrooveDepthRatio,
                IndicatorGrooveWidthDegrees = model.IndicatorGrooveWidthDegrees,
                IndicatorRadiusRatio = model.IndicatorRadiusRatio,
                IndicatorLengthRatio = model.IndicatorLengthRatio,
                IndicatorEnabled = model.IndicatorEnabled,
                IndicatorShape = model.IndicatorShape,
                IndicatorRelief = model.IndicatorRelief,
                IndicatorProfile = model.IndicatorProfile,
                IndicatorWidthRatio = model.IndicatorWidthRatio,
                IndicatorLengthRatioTop = model.IndicatorLengthRatioTop,
                IndicatorPositionRatio = model.IndicatorPositionRatio,
                IndicatorThicknessRatio = model.IndicatorThicknessRatio,
                IndicatorRoundness = model.IndicatorRoundness,
                IndicatorColorX = model.IndicatorColor.X,
                IndicatorColorY = model.IndicatorColor.Y,
                IndicatorColorZ = model.IndicatorColor.Z,
                IndicatorColorBlend = model.IndicatorColorBlend,
                IndicatorCadWallsEnabled = model.IndicatorCadWallsEnabled,
                MaterialBaseColorX = material.BaseColor.X,
                MaterialBaseColorY = material.BaseColor.Y,
                MaterialBaseColorZ = material.BaseColor.Z,
                MaterialMetallic = material.Metallic,
                MaterialRoughness = material.Roughness,
                MaterialPearlescence = material.Pearlescence,
                MaterialRustAmount = material.RustAmount,
                MaterialWearAmount = material.WearAmount,
                MaterialGunkAmount = material.GunkAmount,
                MaterialRadialBrushStrength = material.RadialBrushStrength,
                MaterialRadialBrushDensity = material.RadialBrushDensity,
                MaterialSurfaceCharacter = material.SurfaceCharacter,
                MaterialDiffuseStrength = material.DiffuseStrength,
                MaterialSpecularStrength = material.SpecularStrength,
                MaterialSpecularPower = material.SpecularPower,
                MaterialPartMaterialsEnabled = material.PartMaterialsEnabled,
                MaterialTopBaseColorX = material.TopBaseColor.X,
                MaterialTopBaseColorY = material.TopBaseColor.Y,
                MaterialTopBaseColorZ = material.TopBaseColor.Z,
                MaterialTopMetallic = material.TopMetallic,
                MaterialTopRoughness = material.TopRoughness,
                MaterialBevelBaseColorX = material.BevelBaseColor.X,
                MaterialBevelBaseColorY = material.BevelBaseColor.Y,
                MaterialBevelBaseColorZ = material.BevelBaseColor.Z,
                MaterialBevelMetallic = material.BevelMetallic,
                MaterialBevelRoughness = material.BevelRoughness,
                MaterialSideBaseColorX = material.SideBaseColor.X,
                MaterialSideBaseColorY = material.SideBaseColor.Y,
                MaterialSideBaseColorZ = material.SideBaseColor.Z,
                MaterialSideMetallic = material.SideMetallic,
                MaterialSideRoughness = material.SideRoughness,
                BrushPaintingEnabled = project.BrushPaintingEnabled,
                BrushType = project.BrushType,
                BrushChannel = project.BrushChannel,
                ScratchAbrasionType = project.ScratchAbrasionType,
                BrushSizePx = project.BrushSizePx,
                BrushOpacity = project.BrushOpacity,
                BrushSpread = project.BrushSpread,
                BrushDarkness = project.BrushDarkness,
                PaintCoatMetallic = project.PaintCoatMetallic,
                PaintCoatRoughness = project.PaintCoatRoughness,
                ClearCoatAmount = project.ClearCoatAmount,
                ClearCoatRoughness = project.ClearCoatRoughness,
                AnisotropyAngleDegrees = project.AnisotropyAngleDegrees,
                PaintColorX = project.PaintColor.X,
                PaintColorY = project.PaintColor.Y,
                PaintColorZ = project.PaintColor.Z,
                ScratchWidthPx = project.ScratchWidthPx,
                ScratchDepth = project.ScratchDepth,
                ScratchDragResistance = project.ScratchDragResistance,
                ScratchDepthRamp = project.ScratchDepthRamp,
                ScratchExposeColorX = project.ScratchExposeColor.X,
                ScratchExposeColorY = project.ScratchExposeColor.Y,
                ScratchExposeColorZ = project.ScratchExposeColor.Z,
                ScratchExposeMetallic = project.ScratchExposeMetallic,
                ScratchExposeRoughness = project.ScratchExposeRoughness,
                SpiralNormalInfluenceEnabled = project.SpiralNormalInfluenceEnabled,
                SpiralNormalLodFadeStart = project.SpiralNormalLodFadeStart,
                SpiralNormalLodFadeEnd = project.SpiralNormalLodFadeEnd,
                SpiralRoughnessLodBoost = project.SpiralRoughnessLodBoost,
                CollarSnapshot = collar is null ? null : CaptureCollarStateSnapshot(collar)
            };

            if (includeLightingEnvironmentShadowAndLights)
            {
                snapshot.HasLightingEnvironmentShadowSnapshot = true;
                snapshot.Mode = project.Mode;
                snapshot.EnvironmentTopColorX = project.EnvironmentTopColor.X;
                snapshot.EnvironmentTopColorY = project.EnvironmentTopColor.Y;
                snapshot.EnvironmentTopColorZ = project.EnvironmentTopColor.Z;
                snapshot.EnvironmentBottomColorX = project.EnvironmentBottomColor.X;
                snapshot.EnvironmentBottomColorY = project.EnvironmentBottomColor.Y;
                snapshot.EnvironmentBottomColorZ = project.EnvironmentBottomColor.Z;
                snapshot.EnvironmentIntensity = project.EnvironmentIntensity;
                snapshot.EnvironmentRoughnessMix = project.EnvironmentRoughnessMix;
                snapshot.ShadowsEnabled = project.ShadowsEnabled;
                snapshot.ShadowMode = project.ShadowMode;
                snapshot.ShadowStrength = project.ShadowStrength;
                snapshot.ShadowSoftness = project.ShadowSoftness;
                snapshot.ShadowDistance = project.ShadowDistance;
                snapshot.ShadowScale = project.ShadowScale;
                snapshot.ShadowQuality = project.ShadowQuality;
                snapshot.ShadowGray = project.ShadowGray;
                snapshot.ShadowDiffuseInfluence = project.ShadowDiffuseInfluence;
                snapshot.Lights = project.Lights.Select(CaptureLightState).ToList();
                snapshot.SelectedLightIndex = project.SelectedLightIndex;
            }

            return snapshot;
        }

        private void ApplyUserReferenceProfileSnapshot(
            KnobProject project,
            ModelNode model,
            MaterialNode material,
            UserReferenceProfileSnapshot snapshot,
            CollarNode? collar = null)
        {
            if (snapshot.HasLightingEnvironmentShadowSnapshot)
            {
                project.Mode = snapshot.Mode;
                project.EnvironmentTopColor = new Vector3(
                    snapshot.EnvironmentTopColorX,
                    snapshot.EnvironmentTopColorY,
                    snapshot.EnvironmentTopColorZ);
                project.EnvironmentBottomColor = new Vector3(
                    snapshot.EnvironmentBottomColorX,
                    snapshot.EnvironmentBottomColorY,
                    snapshot.EnvironmentBottomColorZ);
                project.EnvironmentIntensity = snapshot.EnvironmentIntensity;
                project.EnvironmentRoughnessMix = snapshot.EnvironmentRoughnessMix;
                project.ShadowsEnabled = snapshot.ShadowsEnabled;
                project.ShadowMode = snapshot.ShadowMode;
                project.ShadowStrength = snapshot.ShadowStrength;
                project.ShadowSoftness = snapshot.ShadowSoftness;
                project.ShadowDistance = snapshot.ShadowDistance;
                project.ShadowScale = snapshot.ShadowScale;
                project.ShadowQuality = snapshot.ShadowQuality;
                project.ShadowGray = snapshot.ShadowGray;
                project.ShadowDiffuseInfluence = snapshot.ShadowDiffuseInfluence;
                ApplyLightStates(snapshot.Lights, snapshot.SelectedLightIndex);
            }

            model.Radius = snapshot.Radius;
            model.Height = snapshot.Height;
            model.Bevel = snapshot.Bevel;
            model.TopRadiusScale = snapshot.TopRadiusScale;
            model.RadialSegments = snapshot.RadialSegments;
            model.RotationRadians = snapshot.RotationRadians;
            model.SpiralRidgeHeight = snapshot.SpiralRidgeHeight;
            model.SpiralRidgeWidth = snapshot.SpiralRidgeWidth;
            model.SpiralRidgeHeightVariance = snapshot.SpiralRidgeHeightVariance;
            model.SpiralRidgeWidthVariance = snapshot.SpiralRidgeWidthVariance;
            model.SpiralHeightVarianceThreshold = snapshot.SpiralHeightVarianceThreshold;
            model.SpiralWidthVarianceThreshold = snapshot.SpiralWidthVarianceThreshold;
            model.SpiralTurns = snapshot.SpiralTurns;
            model.CrownProfile = snapshot.CrownProfile;
            model.BevelCurve = snapshot.BevelCurve;
            model.BodyTaper = snapshot.BodyTaper;
            model.BodyBulge = snapshot.BodyBulge;
            model.GripType = snapshot.GripType;
            model.GripStyle = snapshot.GripStyle;
            model.BodyStyle = snapshot.BodyStyle;
            model.MountPreset = snapshot.MountPreset;
            model.GripStart = snapshot.GripStart;
            model.GripHeight = snapshot.GripHeight;
            model.GripDensity = snapshot.GripDensity;
            model.GripPitch = snapshot.GripPitch;
            model.GripDepth = snapshot.GripDepth;
            model.GripWidth = snapshot.GripWidth;
            model.GripSharpness = snapshot.GripSharpness;
            model.BoreRadiusRatio = snapshot.BoreRadiusRatio;
            model.BoreDepthRatio = snapshot.BoreDepthRatio;
            model.CollarWidthRatio = snapshot.CollarWidthRatio;
            model.CollarHeightRatio = snapshot.CollarHeightRatio;
            model.IndicatorGrooveDepthRatio = snapshot.IndicatorGrooveDepthRatio;
            model.IndicatorGrooveWidthDegrees = snapshot.IndicatorGrooveWidthDegrees;
            model.IndicatorRadiusRatio = snapshot.IndicatorRadiusRatio;
            model.IndicatorLengthRatio = snapshot.IndicatorLengthRatio;
            model.IndicatorEnabled = snapshot.IndicatorEnabled;
            model.IndicatorShape = snapshot.IndicatorShape;
            model.IndicatorRelief = snapshot.IndicatorRelief;
            model.IndicatorProfile = snapshot.IndicatorProfile;
            model.IndicatorWidthRatio = snapshot.IndicatorWidthRatio;
            model.IndicatorLengthRatioTop = snapshot.IndicatorLengthRatioTop;
            model.IndicatorPositionRatio = snapshot.IndicatorPositionRatio;
            model.IndicatorThicknessRatio = snapshot.IndicatorThicknessRatio;
            model.IndicatorRoundness = snapshot.IndicatorRoundness;
            model.IndicatorColor = new Vector3(snapshot.IndicatorColorX, snapshot.IndicatorColorY, snapshot.IndicatorColorZ);
            model.IndicatorColorBlend = snapshot.IndicatorColorBlend;
            model.IndicatorCadWallsEnabled = snapshot.IndicatorCadWallsEnabled;

            material.BaseColor = new Vector3(snapshot.MaterialBaseColorX, snapshot.MaterialBaseColorY, snapshot.MaterialBaseColorZ);
            material.Metallic = snapshot.MaterialMetallic;
            material.Roughness = snapshot.MaterialRoughness;
            material.Pearlescence = snapshot.MaterialPearlescence;
            material.RustAmount = snapshot.MaterialRustAmount;
            material.WearAmount = snapshot.MaterialWearAmount;
            material.GunkAmount = snapshot.MaterialGunkAmount;
            material.RadialBrushStrength = snapshot.MaterialRadialBrushStrength;
            material.RadialBrushDensity = snapshot.MaterialRadialBrushDensity;
            material.SurfaceCharacter = snapshot.MaterialSurfaceCharacter;
            material.DiffuseStrength = snapshot.MaterialDiffuseStrength;
            material.SpecularStrength = snapshot.MaterialSpecularStrength;
            material.SpecularPower = snapshot.MaterialSpecularPower;
            material.PartMaterialsEnabled = snapshot.MaterialPartMaterialsEnabled;
            material.TopBaseColor = new Vector3(
                snapshot.MaterialTopBaseColorX,
                snapshot.MaterialTopBaseColorY,
                snapshot.MaterialTopBaseColorZ);
            material.TopMetallic = snapshot.MaterialTopMetallic;
            material.TopRoughness = snapshot.MaterialTopRoughness;
            material.BevelBaseColor = new Vector3(
                snapshot.MaterialBevelBaseColorX,
                snapshot.MaterialBevelBaseColorY,
                snapshot.MaterialBevelBaseColorZ);
            material.BevelMetallic = snapshot.MaterialBevelMetallic;
            material.BevelRoughness = snapshot.MaterialBevelRoughness;
            material.SideBaseColor = new Vector3(
                snapshot.MaterialSideBaseColorX,
                snapshot.MaterialSideBaseColorY,
                snapshot.MaterialSideBaseColorZ);
            material.SideMetallic = snapshot.MaterialSideMetallic;
            material.SideRoughness = snapshot.MaterialSideRoughness;

            project.BrushPaintingEnabled = snapshot.BrushPaintingEnabled;
            project.BrushType = snapshot.BrushType;
            project.BrushChannel = snapshot.BrushChannel;
            project.ScratchAbrasionType = snapshot.ScratchAbrasionType;
            project.BrushSizePx = snapshot.BrushSizePx;
            project.BrushOpacity = snapshot.BrushOpacity;
            project.BrushSpread = snapshot.BrushSpread;
            project.BrushDarkness = snapshot.BrushDarkness;
            project.PaintCoatMetallic = snapshot.PaintCoatMetallic;
            project.PaintCoatRoughness = snapshot.PaintCoatRoughness;
            project.ClearCoatAmount = snapshot.ClearCoatAmount;
            project.ClearCoatRoughness = snapshot.ClearCoatRoughness;
            project.AnisotropyAngleDegrees = snapshot.AnisotropyAngleDegrees;
            project.PaintColor = new Vector3(snapshot.PaintColorX, snapshot.PaintColorY, snapshot.PaintColorZ);
            project.ScratchWidthPx = snapshot.ScratchWidthPx;
            project.ScratchDepth = snapshot.ScratchDepth;
            project.ScratchDragResistance = snapshot.ScratchDragResistance;
            project.ScratchDepthRamp = snapshot.ScratchDepthRamp;
            project.ScratchExposeColor = new Vector3(
                snapshot.ScratchExposeColorX,
                snapshot.ScratchExposeColorY,
                snapshot.ScratchExposeColorZ);
            project.ScratchExposeMetallic = snapshot.ScratchExposeMetallic;
            project.ScratchExposeRoughness = snapshot.ScratchExposeRoughness;

            project.SpiralNormalInfluenceEnabled = snapshot.SpiralNormalInfluenceEnabled;
            project.SpiralNormalLodFadeStart = snapshot.SpiralNormalLodFadeStart;
            project.SpiralNormalLodFadeEnd = snapshot.SpiralNormalLodFadeEnd;
            project.SpiralRoughnessLodBoost = snapshot.SpiralRoughnessLodBoost;

            if (collar is not null && snapshot.CollarSnapshot is not null)
            {
                ApplyCollarStateSnapshot(collar, snapshot.CollarSnapshot);
            }
        }

    }
}
