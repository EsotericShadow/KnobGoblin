using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using KnobForge.Core;
using KnobForge.Core.Scene;
using System;
using System.Linq;
using System.Numerics;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private void OnModelRadiusChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi) return;
            if (_modelRadiusSlider == null || e.Property != Slider.ValueProperty)
            {
                return;
            }

            var model = GetModelNode();
            if (model == null)
            {
                return;
            }

            model.Radius = (float)_modelRadiusSlider.Value;
            RequestHeavyGeometryRefresh();
        }

        private void OnModelHeightChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi) return;
            if (_modelHeightSlider == null || e.Property != Slider.ValueProperty)
            {
                return;
            }

            var model = GetModelNode();
            if (model == null)
            {
                return;
            }

            model.Height = (float)_modelHeightSlider.Value;
            RequestHeavyGeometryRefresh();
        }

        private void OnModelTopScaleChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi) return;
            if (_modelTopScaleSlider == null || e.Property != Slider.ValueProperty)
            {
                return;
            }

            var model = GetModelNode();
            if (model == null)
            {
                return;
            }

            model.TopRadiusScale = (float)_modelTopScaleSlider.Value;
            RequestHeavyGeometryRefresh();
        }

        private void OnModelBevelChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi) return;
            if (_modelBevelSlider == null || e.Property != Slider.ValueProperty)
            {
                return;
            }

            var model = GetModelNode();
            if (model == null)
            {
                return;
            }

            model.Bevel = (float)_modelBevelSlider.Value;
            RequestHeavyGeometryRefresh();
        }

        private void OnReferenceStyleChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi || _referenceStyleCombo == null || e.Property != ComboBox.SelectedItemProperty)
            {
                return;
            }

            var model = GetModelNode();
            if (model == null || _referenceStyleCombo.SelectedItem is not ReferenceStyleOption option)
            {
                return;
            }

            if (!option.IsSelectable)
            {
                WithUiRefreshSuppressed(() => SelectReferenceStyleOptionForModel(model));
                return;
            }

            var material = EnsureMaterialNode(model);
            if (option.BuiltInStyle.HasValue)
            {
                ReferenceKnobStyle style = option.BuiltInStyle.Value;
                _selectedUserReferenceProfileName = null;
                model.ReferenceStyle = style;
                ApplyReferenceStylePreset(_project, model, material, style);
                if (_referenceStyleSaveNameTextBox != null)
                {
                    _referenceStyleSaveNameTextBox.Text = string.Empty;
                }
            }
            else if (!string.IsNullOrWhiteSpace(option.UserProfileName) &&
                     TryGetUserReferenceProfile(option.UserProfileName, out UserReferenceProfile? profile) &&
                     profile is not null)
            {
                _selectedUserReferenceProfileName = profile.Name;
                model.ReferenceStyle = ReferenceKnobStyle.Custom;
                CollarNode? collar = profile.Snapshot.CollarSnapshot is not null
                    ? EnsureCollarNode()
                    : GetCollarNode();
                ApplyUserReferenceProfileSnapshot(_project, model, material, profile.Snapshot, collar);
                if (_referenceStyleSaveNameTextBox != null)
                {
                    _referenceStyleSaveNameTextBox.Text = profile.Name;
                }
            }
            else
            {
                return;
            }

            ResetReferenceProfileDestructiveConfirmation();
            UpdateReferenceProfileActionEnablement(hasModel: true);
            NotifyProjectStateChanged();
        }

        private static void ApplyReferenceStylePreset(
            KnobProject project,
            ModelNode model,
            MaterialNode? material,
            ReferenceKnobStyle style)
        {
            if (style == ReferenceKnobStyle.Custom)
            {
                return;
            }

            switch (style)
            {
                case ReferenceKnobStyle.BossFlutedPedal:
                    model.BodyStyle = BodyStyle.Stepped;
                    model.GripStyle = GripStyle.ModernEurorackCoarse;
                    model.Radius = 185f;
                    model.Height = 118f;
                    model.TopRadiusScale = 0.86f;
                    model.Bevel = 20f;
                    model.BevelCurve = 1.5f;
                    model.CrownProfile = 0.02f;
                    model.BodyTaper = 0.10f;
                    model.BodyBulge = -0.06f;
                    model.RadialSegments = 140;
                    model.SpiralRidgeHeight = 0f;
                    model.SpiralRidgeWidth = 18.92f;
                    model.SpiralTurns = 150f;
                    model.GripType = GripType.VerticalFlutes;
                    model.GripStart = 0.08f;
                    model.GripHeight = 0.74f;
                    model.GripDensity = 36f;
                    model.GripPitch = 0.2f;
                    model.GripDepth = 3.8f;
                    model.GripWidth = 1.5f;
                    model.GripSharpness = 2.4f;
                    if (material != null)
                    {
                        material.BaseColor = new Vector3(0.09f, 0.09f, 0.10f);
                        material.Metallic = 0.05f;
                        material.Roughness = 0.42f;
                        material.RadialBrushStrength = 0.02f;
                        material.RadialBrushDensity = 20f;
                        material.SurfaceCharacter = 0.10f;
                    }

                    project.SpiralNormalInfluenceEnabled = false;
                    break;
                case ReferenceKnobStyle.MxrHeptagonPedal:
                    model.BodyStyle = BodyStyle.Stepped;
                    model.GripStyle = GripStyle.VintageBakeliteEra;
                    model.Radius = 175f;
                    model.Height = 108f;
                    model.TopRadiusScale = 0.84f;
                    model.Bevel = 16f;
                    model.BevelCurve = 1.9f;
                    model.CrownProfile = 0f;
                    model.BodyTaper = 0.16f;
                    model.BodyBulge = -0.10f;
                    model.RadialSegments = 126;
                    model.SpiralRidgeHeight = 0f;
                    model.SpiralRidgeWidth = 18.92f;
                    model.SpiralTurns = 150f;
                    model.GripType = GripType.SquareKnurl;
                    model.GripStart = 0.14f;
                    model.GripHeight = 0.60f;
                    model.GripDensity = 49f;
                    model.GripPitch = 0.35f;
                    model.GripDepth = 2.6f;
                    model.GripWidth = 2.4f;
                    model.GripSharpness = 2.2f;
                    if (material != null)
                    {
                        material.BaseColor = new Vector3(0.12f, 0.12f, 0.13f);
                        material.Metallic = 0.03f;
                        material.Roughness = 0.38f;
                        material.RadialBrushStrength = 0f;
                        material.RadialBrushDensity = 12f;
                        material.SurfaceCharacter = 0f;
                    }

                    project.SpiralNormalInfluenceEnabled = false;
                    break;
                case ReferenceKnobStyle.EhxSmoothDomePedal:
                    model.BodyStyle = BodyStyle.Barrel;
                    model.GripStyle = GripStyle.VintageBakeliteEra;
                    model.Radius = 190f;
                    model.Height = 130f;
                    model.TopRadiusScale = 0.92f;
                    model.Bevel = 28f;
                    model.BevelCurve = 0.85f;
                    model.CrownProfile = 0.40f;
                    model.BodyTaper = 0.02f;
                    model.BodyBulge = 0.20f;
                    model.RadialSegments = 140;
                    model.SpiralRidgeHeight = 0f;
                    model.SpiralRidgeWidth = 18.92f;
                    model.SpiralTurns = 150f;
                    model.GripType = GripType.None;
                    if (material != null)
                    {
                        material.BaseColor = new Vector3(0.03f, 0.03f, 0.04f);
                        material.Metallic = 0.02f;
                        material.Roughness = 0.48f;
                        material.RadialBrushStrength = 0f;
                        material.RadialBrushDensity = 8f;
                        material.SurfaceCharacter = 0f;
                    }

                    project.SpiralNormalInfluenceEnabled = false;
                    break;
                case ReferenceKnobStyle.SslChannelStrip:
                    model.BodyStyle = BodyStyle.Straight;
                    model.GripStyle = GripStyle.BoutiqueSynthPremium;
                    model.Radius = 125f;
                    model.Height = 140f;
                    model.TopRadiusScale = 0.97f;
                    model.Bevel = 10f;
                    model.BevelCurve = 1.0f;
                    model.CrownProfile = 0.05f;
                    model.BodyTaper = 0.03f;
                    model.BodyBulge = 0f;
                    model.RadialSegments = 120;
                    model.SpiralRidgeHeight = 0f;
                    model.SpiralRidgeWidth = 18.92f;
                    model.SpiralTurns = 150f;
                    model.GripType = GripType.VerticalFlutes;
                    model.GripStart = 0.06f;
                    model.GripHeight = 0.82f;
                    model.GripDensity = 42f;
                    model.GripPitch = 0.2f;
                    model.GripDepth = 2.2f;
                    model.GripWidth = 1.1f;
                    model.GripSharpness = 2.0f;
                    if (material != null)
                    {
                        material.BaseColor = new Vector3(0.58f, 0.58f, 0.60f);
                        material.Metallic = 0.04f;
                        material.Roughness = 0.33f;
                        material.RadialBrushStrength = 0f;
                        material.RadialBrushDensity = 10f;
                        material.SurfaceCharacter = 0f;
                    }

                    project.SpiralNormalInfluenceEnabled = false;
                    break;
                case ReferenceKnobStyle.SslMonitorLarge:
                    model.BodyStyle = BodyStyle.Straight;
                    model.GripStyle = GripStyle.BoutiqueSynthPremium;
                    model.Radius = 245f;
                    model.Height = 150f;
                    model.TopRadiusScale = 0.96f;
                    model.Bevel = 14f;
                    model.BevelCurve = 1.05f;
                    model.CrownProfile = 0.03f;
                    model.BodyTaper = 0.04f;
                    model.BodyBulge = 0.02f;
                    model.RadialSegments = 160;
                    model.SpiralRidgeHeight = 0f;
                    model.SpiralRidgeWidth = 18.92f;
                    model.SpiralTurns = 150f;
                    model.GripType = GripType.VerticalFlutes;
                    model.GripStart = 0.06f;
                    model.GripHeight = 0.84f;
                    model.GripDensity = 54f;
                    model.GripPitch = 0.2f;
                    model.GripDepth = 2.8f;
                    model.GripWidth = 1.3f;
                    model.GripSharpness = 2.2f;
                    if (material != null)
                    {
                        material.BaseColor = new Vector3(0.08f, 0.08f, 0.09f);
                        material.Metallic = 0.05f;
                        material.Roughness = 0.32f;
                        material.RadialBrushStrength = 0f;
                        material.RadialBrushDensity = 10f;
                        material.SurfaceCharacter = 0f;
                    }

                    project.SpiralNormalInfluenceEnabled = false;
                    break;
                case ReferenceKnobStyle.StratTeleBell:
                    model.BodyStyle = BodyStyle.Stepped;
                    model.GripStyle = GripStyle.VintageBakeliteEra;
                    model.Radius = 165f;
                    model.Height = 105f;
                    model.TopRadiusScale = 0.74f;
                    model.Bevel = 18f;
                    model.BevelCurve = 1.8f;
                    model.CrownProfile = 0.12f;
                    model.BodyTaper = 0.20f;
                    model.BodyBulge = -0.08f;
                    model.RadialSegments = 140;
                    model.SpiralRidgeHeight = 0f;
                    model.SpiralRidgeWidth = 18.92f;
                    model.SpiralTurns = 150f;
                    model.GripType = GripType.VerticalFlutes;
                    model.GripStart = 0.00f;
                    model.GripHeight = 0.48f;
                    model.GripDensity = 40f;
                    model.GripPitch = 0.2f;
                    model.GripDepth = 2.0f;
                    model.GripWidth = 1.25f;
                    model.GripSharpness = 1.7f;
                    if (material != null)
                    {
                        material.BaseColor = new Vector3(0.90f, 0.89f, 0.80f);
                        material.Metallic = 0f;
                        material.Roughness = 0.44f;
                        material.RadialBrushStrength = 0f;
                        material.RadialBrushDensity = 8f;
                        material.SurfaceCharacter = 0f;
                    }

                    project.SpiralNormalInfluenceEnabled = false;
                    break;
                case ReferenceKnobStyle.GibsonSpeed:
                    model.BodyStyle = BodyStyle.Barrel;
                    model.GripStyle = GripStyle.VintageBakeliteEra;
                    model.Radius = 155f;
                    model.Height = 95f;
                    model.TopRadiusScale = 0.82f;
                    model.Bevel = 16f;
                    model.BevelCurve = 1.2f;
                    model.CrownProfile = 0.30f;
                    model.BodyTaper = 0.08f;
                    model.BodyBulge = 0.16f;
                    model.RadialSegments = 132;
                    model.SpiralRidgeHeight = 0f;
                    model.SpiralRidgeWidth = 18.92f;
                    model.SpiralTurns = 150f;
                    model.GripType = GripType.None;
                    if (material != null)
                    {
                        material.BaseColor = new Vector3(0.07f, 0.07f, 0.07f);
                        material.Metallic = 0f;
                        material.Roughness = 0.40f;
                        material.RadialBrushStrength = 0f;
                        material.RadialBrushDensity = 8f;
                        material.SurfaceCharacter = 0f;
                    }

                    project.SpiralNormalInfluenceEnabled = false;
                    break;
                case ReferenceKnobStyle.BrushedAluminumPremium:
                    model.BodyStyle = BodyStyle.Straight;
                    model.GripStyle = GripStyle.BoutiqueSynthPremium;
                    model.Radius = 220f;
                    model.Height = 120f;
                    model.TopRadiusScale = 0.86f;
                    model.Bevel = 18f;
                    model.BevelCurve = 1.0f;
                    model.CrownProfile = 0f;
                    model.BodyTaper = 0f;
                    model.BodyBulge = 0f;
                    model.RadialSegments = 180;
                    model.SpiralRidgeHeight = 19.89f;
                    model.SpiralRidgeWidth = 18.92f;
                    model.SpiralTurns = 150f;
                    model.GripType = GripType.None;
                    if (material != null)
                    {
                        material.BaseColor = new Vector3(0.72f, 0.72f, 0.74f);
                        material.Metallic = 1.00f;
                        material.Roughness = 0.06f;
                        material.RadialBrushStrength = 0.65f;
                        material.RadialBrushDensity = 280.5f;
                        material.SurfaceCharacter = 1.00f;
                    }

                    project.SpiralNormalInfluenceEnabled = true;
                    project.SpiralNormalLodFadeStart = 4.22f;
                    project.SpiralNormalLodFadeEnd = 4.23f;
                    project.SpiralRoughnessLodBoost = 0.78f;
                    break;
            }

            if (material != null)
            {
                material.PartMaterialsEnabled = false;
                material.SyncPartMaterialsFromGlobal();
            }
        }

        private void OnBodyStyleChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi || _bodyStyleCombo == null || e.Property != ComboBox.SelectedItemProperty)
            {
                return;
            }

            var model = GetModelNode();
            if (model == null || _bodyStyleCombo.SelectedItem is not BodyStyle style)
            {
                return;
            }

            model.BodyStyle = style;
            ApplyBodyStyleDefaults(model, style);
            NotifyProjectStateChanged();
        }

        private static void ApplyBodyStyleDefaults(ModelNode model, BodyStyle style)
        {
            switch (style)
            {
                case BodyStyle.Straight:
                    model.CrownProfile = 0f;
                    model.BevelCurve = 1.0f;
                    model.BodyTaper = 0f;
                    model.BodyBulge = 0f;
                    break;
                case BodyStyle.Waisted:
                    model.CrownProfile = -0.10f;
                    model.BevelCurve = 1.35f;
                    model.BodyTaper = 0.12f;
                    model.BodyBulge = -0.22f;
                    break;
                case BodyStyle.Barrel:
                    model.CrownProfile = 0.08f;
                    model.BevelCurve = 0.9f;
                    model.BodyTaper = 0.05f;
                    model.BodyBulge = 0.26f;
                    break;
                case BodyStyle.Stepped:
                    model.CrownProfile = 0f;
                    model.BevelCurve = 2.2f;
                    model.BodyTaper = 0.18f;
                    model.BodyBulge = -0.08f;
                    break;
            }
        }

        private void OnBodyDesignChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi ||
                e.Property != Slider.ValueProperty ||
                _bevelCurveSlider == null ||
                _crownProfileSlider == null ||
                _bodyTaperSlider == null ||
                _bodyBulgeSlider == null)
            {
                return;
            }

            var model = GetModelNode();
            if (model == null)
            {
                return;
            }

            model.BevelCurve = (float)_bevelCurveSlider.Value;
            model.CrownProfile = (float)_crownProfileSlider.Value;
            model.BodyTaper = (float)_bodyTaperSlider.Value;
            model.BodyBulge = (float)_bodyBulgeSlider.Value;
            RequestHeavyGeometryRefresh();
        }

        private void OnModelSegmentsChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi) return;
            if (_modelSegmentsSlider == null || e.Property != Slider.ValueProperty)
            {
                return;
            }

            var model = GetModelNode();
            if (model == null)
            {
                return;
            }

            int seg = Math.Clamp((int)Math.Round(_modelSegmentsSlider.Value), 12, 180);
            model.RadialSegments = seg;
            RequestHeavyGeometryRefresh();
        }

        private void OnSpiralGeometryChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi || e.Property != Slider.ValueProperty ||
                _spiralRidgeHeightSlider == null || _spiralRidgeWidthSlider == null || _spiralTurnsSlider == null)
            {
                return;
            }

            var model = GetModelNode();
            if (model == null)
            {
                return;
            }

            model.SpiralRidgeHeight = (float)_spiralRidgeHeightSlider.Value;
            model.SpiralRidgeWidth = (float)_spiralRidgeWidthSlider.Value;
            model.SpiralTurns = (float)_spiralTurnsSlider.Value;
            RequestHeavyGeometryRefresh();
        }

        private void OnGripStyleChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi || _gripStyleCombo == null || e.Property != ComboBox.SelectedItemProperty)
            {
                return;
            }

            var model = GetModelNode();
            if (model == null || _gripStyleCombo.SelectedItem is not GripStyle style)
            {
                return;
            }

            model.GripStyle = style;
            ApplyGripStyleDefaults(model, style);
            NotifyProjectStateChanged();
        }

        private static void ApplyGripStyleDefaults(ModelNode model, GripStyle style)
        {
            switch (style)
            {
                case GripStyle.BrutalIndustrial:
                    model.GripType = GripType.DiamondKnurl;
                    model.GripStart = 0.18f;
                    model.GripHeight = 0.58f;
                    model.GripDensity = 40f;
                    model.GripPitch = 5.5f;
                    model.GripWidth = 2.2f;
                    model.GripDepth = 8.0f;
                    model.GripSharpness = 5.0f;
                    break;
                case GripStyle.BoutiqueSynthPremium:
                    model.GripType = GripType.DiamondKnurl;
                    model.GripStart = 0.16f;
                    model.GripHeight = 0.52f;
                    model.GripDensity = 84f;
                    model.GripPitch = 7.0f;
                    model.GripWidth = 1.1f;
                    model.GripDepth = 2.8f;
                    model.GripSharpness = 3.2f;
                    break;
                case GripStyle.VintageBakeliteEra:
                    model.GripType = GripType.SquareKnurl;
                    model.GripStart = 0.12f;
                    model.GripHeight = 0.60f;
                    model.GripDensity = 28f;
                    model.GripPitch = 3.0f;
                    model.GripWidth = 2.6f;
                    model.GripDepth = 1.5f;
                    model.GripSharpness = 1.2f;
                    break;
                case GripStyle.ModernEurorackCoarse:
                    model.GripType = GripType.HexKnurl;
                    model.GripStart = 0.20f;
                    model.GripHeight = 0.55f;
                    model.GripDensity = 36f;
                    model.GripPitch = 4.5f;
                    model.GripWidth = 2.0f;
                    model.GripDepth = 4.0f;
                    model.GripSharpness = 3.4f;
                    break;
            }
        }

        private void OnGripSettingsChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi ||
                _gripTypeCombo == null || _gripStartSlider == null || _gripHeightSlider == null ||
                _gripDensitySlider == null || _gripPitchSlider == null || _gripDepthSlider == null ||
                _gripWidthSlider == null || _gripSharpnessSlider == null)
            {
                return;
            }

            if (ReferenceEquals(sender, _gripTypeCombo))
            {
                if (e.Property != ComboBox.SelectedItemProperty)
                {
                    return;
                }
            }
            else if (e.Property != Slider.ValueProperty)
            {
                return;
            }

            var model = GetModelNode();
            if (model == null)
            {
                return;
            }

            model.GripType = _gripTypeCombo.SelectedItem is GripType gripType ? gripType : GripType.None;
            model.GripStart = (float)_gripStartSlider.Value;
            model.GripHeight = (float)_gripHeightSlider.Value;
            model.GripDensity = (float)_gripDensitySlider.Value;
            model.GripPitch = (float)_gripPitchSlider.Value;
            model.GripDepth = (float)_gripDepthSlider.Value;
            model.GripWidth = (float)_gripWidthSlider.Value;
            model.GripSharpness = (float)_gripSharpnessSlider.Value;
            if (ReferenceEquals(sender, _gripTypeCombo))
            {
                NotifyProjectStateChanged();
            }
            else
            {
                RequestHeavyGeometryRefresh();
            }
        }
    }
}
