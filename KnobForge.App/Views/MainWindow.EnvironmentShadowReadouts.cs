using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using KnobForge.Core;
using KnobForge.Core.Scene;
using System;
using System.Numerics;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private void OnEnvironmentChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi || e.Property != Slider.ValueProperty ||
                _envIntensitySlider == null || _envRoughnessMixSlider == null ||
                _envTopRSlider == null || _envTopGSlider == null || _envTopBSlider == null ||
                _envBottomRSlider == null || _envBottomGSlider == null || _envBottomBSlider == null)
            {
                return;
            }

            _project.EnvironmentIntensity = (float)_envIntensitySlider.Value;
            _project.EnvironmentRoughnessMix = (float)_envRoughnessMixSlider.Value;
            _project.EnvironmentTopColor = new Vector3(
                (float)_envTopRSlider.Value,
                (float)_envTopGSlider.Value,
                (float)_envTopBSlider.Value);
            _project.EnvironmentBottomColor = new Vector3(
                (float)_envBottomRSlider.Value,
                (float)_envBottomGSlider.Value,
                (float)_envBottomBSlider.Value);

            NotifyRenderOnly();
        }

        private void OnShadowSettingsChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi ||
                _shadowEnabledCheckBox == null ||
                _shadowSourceModeCombo == null ||
                _shadowStrengthSlider == null ||
                _shadowSoftnessSlider == null ||
                _shadowDistanceSlider == null ||
                _shadowScaleSlider == null ||
                _shadowQualitySlider == null ||
                _shadowGraySlider == null ||
                _shadowDiffuseInfluenceSlider == null)
            {
                return;
            }

            if (ReferenceEquals(sender, _shadowEnabledCheckBox))
            {
                if (e.Property != ToggleButton.IsCheckedProperty)
                {
                    return;
                }
            }
            else if (ReferenceEquals(sender, _shadowSourceModeCombo))
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

            _project.ShadowsEnabled = _shadowEnabledCheckBox.IsChecked ?? true;
            _project.ShadowMode = _shadowSourceModeCombo.SelectedItem is ShadowLightMode shadowMode
                ? shadowMode
                : ShadowLightMode.Weighted;
            _project.ShadowStrength = (float)_shadowStrengthSlider.Value;
            _project.ShadowSoftness = (float)_shadowSoftnessSlider.Value;
            _project.ShadowDistance = (float)_shadowDistanceSlider.Value;
            _project.ShadowScale = (float)_shadowScaleSlider.Value;
            _project.ShadowQuality = (float)_shadowQualitySlider.Value;
            _project.ShadowGray = (float)_shadowGraySlider.Value;
            _project.ShadowDiffuseInfluence = (float)_shadowDiffuseInfluenceSlider.Value;
            NotifyRenderOnly();
        }
        private void UpdateReadouts()
        {
            if (_rotationSlider != null && _rotationValueText != null)
            {
                _rotationValueText.Text = $"{RadiansToDegrees(_rotationSlider.Value):0.0} deg";
            }

            if (_lightXSlider != null && _lightXValueText != null)
            {
                _lightXValueText.Text = $"{_lightXSlider.Value:0}";
            }

            if (_lightYSlider != null && _lightYValueText != null)
            {
                _lightYValueText.Text = $"{_lightYSlider.Value:0}";
            }

            if (_lightZSlider != null && _lightZValueText != null)
            {
                _lightZValueText.Text = $"{_lightZSlider.Value:0}";
            }

            if (_directionSlider != null && _directionValueText != null)
            {
                _directionValueText.Text = $"{_directionSlider.Value:0.0} deg";
            }

            if (_intensitySlider != null && _intensityValueText != null)
            {
                _intensityValueText.Text = $"{_intensitySlider.Value:0.00}";
            }

            if (_falloffSlider != null && _falloffValueText != null)
            {
                _falloffValueText.Text = $"{_falloffSlider.Value:0.00}";
            }

            if (_lightRSlider != null && _lightRValueText != null)
            {
                _lightRValueText.Text = $"{_lightRSlider.Value:0}";
            }

            if (_lightGSlider != null && _lightGValueText != null)
            {
                _lightGValueText.Text = $"{_lightGSlider.Value:0}";
            }

            if (_lightBSlider != null && _lightBValueText != null)
            {
                _lightBValueText.Text = $"{_lightBSlider.Value:0}";
            }

            if (_diffuseBoostSlider != null && _diffuseBoostValueText != null)
            {
                _diffuseBoostValueText.Text = $"{_diffuseBoostSlider.Value:0.00}";
            }

            if (_specularBoostSlider != null && _specularBoostValueText != null)
            {
                _specularBoostValueText.Text = $"{_specularBoostSlider.Value:0.00}";
            }

            if (_specularPowerSlider != null && _specularPowerValueText != null)
            {
                _specularPowerValueText.Text = $"{_specularPowerSlider.Value:0.0}";
            }

            if (_modelRadiusSlider != null && _modelRadiusValueText != null)
            {
                _modelRadiusValueText.Text = $"{_modelRadiusSlider.Value:0}";
            }

            if (_modelHeightSlider != null && _modelHeightValueText != null)
            {
                _modelHeightValueText.Text = $"{_modelHeightSlider.Value:0}";
            }

            if (_modelTopScaleSlider != null && _modelTopScaleValueText != null)
            {
                _modelTopScaleValueText.Text = $"{_modelTopScaleSlider.Value:0.00}";
            }

            if (_modelBevelSlider != null && _modelBevelValueText != null)
            {
                _modelBevelValueText.Text = $"{_modelBevelSlider.Value:0}";
            }

            if (_bevelCurveSlider != null && _bevelCurveValueText != null)
            {
                _bevelCurveValueText.Text = $"{_bevelCurveSlider.Value:0.00}";
            }

            if (_crownProfileSlider != null && _crownProfileValueText != null)
            {
                double v = _crownProfileSlider.Value;
                string shape = v < -0.02 ? "Concave" : v > 0.02 ? "Convex" : "Flat";
                _crownProfileValueText.Text = $"{v:0.00} ({shape})";
            }

            if (_bodyTaperSlider != null && _bodyTaperValueText != null)
            {
                _bodyTaperValueText.Text = $"{_bodyTaperSlider.Value:0.00}";
            }

            if (_bodyBulgeSlider != null && _bodyBulgeValueText != null)
            {
                _bodyBulgeValueText.Text = $"{_bodyBulgeSlider.Value:0.00}";
            }

            if (_modelSegmentsSlider != null && _modelSegmentsValueText != null)
            {
                _modelSegmentsValueText.Text = $"{Math.Round(_modelSegmentsSlider.Value):0}";
            }

            if (_spiralRidgeHeightSlider != null && _spiralRidgeHeightValueText != null)
            {
                _spiralRidgeHeightValueText.Text = $"{_spiralRidgeHeightSlider.Value:0.00}";
            }

            if (_spiralRidgeWidthSlider != null && _spiralRidgeWidthValueText != null)
            {
                _spiralRidgeWidthValueText.Text = $"{_spiralRidgeWidthSlider.Value:0.00}";
            }

            if (_spiralTurnsSlider != null && _spiralTurnsValueText != null)
            {
                _spiralTurnsValueText.Text = $"{_spiralTurnsSlider.Value:0.00}";
            }

            if (_gripStartSlider != null && _gripStartValueText != null)
            {
                _gripStartValueText.Text = $"{_gripStartSlider.Value:0.00}";
            }

            if (_gripHeightSlider != null && _gripHeightValueText != null)
            {
                _gripHeightValueText.Text = $"{_gripHeightSlider.Value:0.00}";
            }

            if (_gripDensitySlider != null && _gripDensityValueText != null)
            {
                _gripDensityValueText.Text = $"{_gripDensitySlider.Value:0.0}";
            }

            if (_gripPitchSlider != null && _gripPitchValueText != null)
            {
                _gripPitchValueText.Text = $"{_gripPitchSlider.Value:0.00}";
            }

            if (_gripDepthSlider != null && _gripDepthValueText != null)
            {
                _gripDepthValueText.Text = $"{_gripDepthSlider.Value:0.00}";
            }

            if (_gripWidthSlider != null && _gripWidthValueText != null)
            {
                _gripWidthValueText.Text = $"{_gripWidthSlider.Value:0.00}";
            }

            if (_gripSharpnessSlider != null && _gripSharpnessValueText != null)
            {
                _gripSharpnessValueText.Text = $"{_gripSharpnessSlider.Value:0.00}";
            }

            if (_collarScaleSlider != null && _collarScaleValueText != null)
            {
                _collarScaleValueText.Text = $"{_collarScaleSlider.Value:0.00}";
            }

            if (_collarBodyLengthSlider != null && _collarBodyLengthValueText != null)
            {
                _collarBodyLengthValueText.Text = $"{_collarBodyLengthSlider.Value:0.00}";
            }

            if (_collarBodyThicknessSlider != null && _collarBodyThicknessValueText != null)
            {
                _collarBodyThicknessValueText.Text = $"{_collarBodyThicknessSlider.Value:0.00}";
            }

            if (_collarHeadLengthSlider != null && _collarHeadLengthValueText != null)
            {
                _collarHeadLengthValueText.Text = $"{_collarHeadLengthSlider.Value:0.00}";
            }

            if (_collarHeadThicknessSlider != null && _collarHeadThicknessValueText != null)
            {
                _collarHeadThicknessValueText.Text = $"{_collarHeadThicknessSlider.Value:0.00}";
            }

            if (_collarRotateSlider != null && _collarRotateValueText != null)
            {
                _collarRotateValueText.Text = $"{_collarRotateSlider.Value:0.0} deg";
            }

            if (_collarOffsetXSlider != null && _collarOffsetXValueText != null)
            {
                _collarOffsetXValueText.Text = $"{_collarOffsetXSlider.Value:0.00}";
            }

            if (_collarOffsetYSlider != null && _collarOffsetYValueText != null)
            {
                _collarOffsetYValueText.Text = $"{_collarOffsetYSlider.Value:0.00}";
            }

            if (_collarElevationSlider != null && _collarElevationValueText != null)
            {
                _collarElevationValueText.Text = $"{_collarElevationSlider.Value:0.00}";
            }

            if (_collarInflateSlider != null && _collarInflateValueText != null)
            {
                _collarInflateValueText.Text = $"{_collarInflateSlider.Value:0.000}";
            }

            if (_collarMaterialBaseRSlider != null && _collarMaterialBaseRValueText != null)
            {
                _collarMaterialBaseRValueText.Text = $"{_collarMaterialBaseRSlider.Value:0.00}";
            }

            if (_collarMaterialBaseGSlider != null && _collarMaterialBaseGValueText != null)
            {
                _collarMaterialBaseGValueText.Text = $"{_collarMaterialBaseGSlider.Value:0.00}";
            }

            if (_collarMaterialBaseBSlider != null && _collarMaterialBaseBValueText != null)
            {
                _collarMaterialBaseBValueText.Text = $"{_collarMaterialBaseBSlider.Value:0.00}";
            }

            if (_collarMaterialMetallicSlider != null && _collarMaterialMetallicValueText != null)
            {
                _collarMaterialMetallicValueText.Text = $"{_collarMaterialMetallicSlider.Value:0.00}";
            }

            if (_collarMaterialRoughnessSlider != null && _collarMaterialRoughnessValueText != null)
            {
                _collarMaterialRoughnessValueText.Text = $"{_collarMaterialRoughnessSlider.Value:0.00}";
            }

            if (_collarMaterialPearlescenceSlider != null && _collarMaterialPearlescenceValueText != null)
            {
                _collarMaterialPearlescenceValueText.Text = $"{_collarMaterialPearlescenceSlider.Value:0.00}";
            }

            if (_collarMaterialRustSlider != null && _collarMaterialRustValueText != null)
            {
                _collarMaterialRustValueText.Text = $"{_collarMaterialRustSlider.Value:0.00}";
            }

            if (_collarMaterialWearSlider != null && _collarMaterialWearValueText != null)
            {
                _collarMaterialWearValueText.Text = $"{_collarMaterialWearSlider.Value:0.00}";
            }

            if (_collarMaterialGunkSlider != null && _collarMaterialGunkValueText != null)
            {
                _collarMaterialGunkValueText.Text = $"{_collarMaterialGunkSlider.Value:0.00}";
            }

            if (_indicatorWidthSlider != null && _indicatorWidthValueText != null)
            {
                _indicatorWidthValueText.Text = $"{_indicatorWidthSlider.Value:0.000}";
            }

            if (_indicatorLengthSlider != null && _indicatorLengthValueText != null)
            {
                _indicatorLengthValueText.Text = $"{_indicatorLengthSlider.Value:0.00}";
            }

            if (_indicatorPositionSlider != null && _indicatorPositionValueText != null)
            {
                _indicatorPositionValueText.Text = $"{_indicatorPositionSlider.Value:0.00}";
            }

            if (_indicatorThicknessSlider != null && _indicatorThicknessValueText != null)
            {
                _indicatorThicknessValueText.Text = $"{_indicatorThicknessSlider.Value:0.000}";
            }

            if (_indicatorRoundnessSlider != null && _indicatorRoundnessValueText != null)
            {
                _indicatorRoundnessValueText.Text = $"{_indicatorRoundnessSlider.Value:0.00}";
            }

            if (_indicatorColorBlendSlider != null && _indicatorColorBlendValueText != null)
            {
                _indicatorColorBlendValueText.Text = $"{_indicatorColorBlendSlider.Value:0.00}";
            }

            if (_indicatorColorRSlider != null && _indicatorColorRValueText != null)
            {
                _indicatorColorRValueText.Text = $"{_indicatorColorRSlider.Value:0.00}";
            }

            if (_indicatorColorGSlider != null && _indicatorColorGValueText != null)
            {
                _indicatorColorGValueText.Text = $"{_indicatorColorGSlider.Value:0.00}";
            }

            if (_indicatorColorBSlider != null && _indicatorColorBValueText != null)
            {
                _indicatorColorBValueText.Text = $"{_indicatorColorBSlider.Value:0.00}";
            }

            if (_materialBaseRSlider != null && _materialBaseRValueText != null)
            {
                _materialBaseRValueText.Text = $"{_materialBaseRSlider.Value:0.00}";
            }

            if (_materialBaseGSlider != null && _materialBaseGValueText != null)
            {
                _materialBaseGValueText.Text = $"{_materialBaseGSlider.Value:0.00}";
            }

            if (_materialBaseBSlider != null && _materialBaseBValueText != null)
            {
                _materialBaseBValueText.Text = $"{_materialBaseBSlider.Value:0.00}";
            }

            if (_materialMetallicSlider != null && _materialMetallicValueText != null)
            {
                _materialMetallicValueText.Text = $"{_materialMetallicSlider.Value:0.00}";
            }

            if (_materialRoughnessSlider != null && _materialRoughnessValueText != null)
            {
                _materialRoughnessValueText.Text = $"{_materialRoughnessSlider.Value:0.00}";
            }

            if (_materialPearlescenceSlider != null && _materialPearlescenceValueText != null)
            {
                _materialPearlescenceValueText.Text = $"{_materialPearlescenceSlider.Value:0.00}";
            }

            if (_materialRustSlider != null && _materialRustValueText != null)
            {
                _materialRustValueText.Text = $"{_materialRustSlider.Value:0.00}";
            }

            if (_materialWearSlider != null && _materialWearValueText != null)
            {
                _materialWearValueText.Text = $"{_materialWearSlider.Value:0.00}";
            }

            if (_materialGunkSlider != null && _materialGunkValueText != null)
            {
                _materialGunkValueText.Text = $"{_materialGunkSlider.Value:0.00}";
            }

            if (_materialBrushStrengthSlider != null && _materialBrushStrengthValueText != null)
            {
                _materialBrushStrengthValueText.Text = $"{_materialBrushStrengthSlider.Value:0.00}";
            }

            if (_materialBrushDensitySlider != null && _materialBrushDensityValueText != null)
            {
                _materialBrushDensityValueText.Text = $"{_materialBrushDensitySlider.Value:0.0}";
            }

            if (_materialCharacterSlider != null && _materialCharacterValueText != null)
            {
                _materialCharacterValueText.Text = $"{_materialCharacterSlider.Value:0.00}";
            }

            if (_microLodFadeStartSlider != null && _microLodFadeStartValueText != null)
            {
                _microLodFadeStartValueText.Text = $"{_microLodFadeStartSlider.Value:0.00}";
            }

            if (_microLodFadeEndSlider != null && _microLodFadeEndValueText != null)
            {
                _microLodFadeEndValueText.Text = $"{_microLodFadeEndSlider.Value:0.00}";
            }

            if (_microRoughnessLodBoostSlider != null && _microRoughnessLodBoostValueText != null)
            {
                _microRoughnessLodBoostValueText.Text = $"{_microRoughnessLodBoostSlider.Value:0.00}";
            }

            if (_envIntensitySlider != null && _envIntensityValueText != null)
            {
                _envIntensityValueText.Text = $"{_envIntensitySlider.Value:0.000}";
            }

            if (_envRoughnessMixSlider != null && _envRoughnessMixValueText != null)
            {
                _envRoughnessMixValueText.Text = $"{_envRoughnessMixSlider.Value:0.000}";
            }

            if (_envTopRSlider != null && _envTopRValueText != null)
            {
                _envTopRValueText.Text = $"{_envTopRSlider.Value:0.00}";
            }

            if (_envTopGSlider != null && _envTopGValueText != null)
            {
                _envTopGValueText.Text = $"{_envTopGSlider.Value:0.00}";
            }

            if (_envTopBSlider != null && _envTopBValueText != null)
            {
                _envTopBValueText.Text = $"{_envTopBSlider.Value:0.00}";
            }

            if (_envBottomRSlider != null && _envBottomRValueText != null)
            {
                _envBottomRValueText.Text = $"{_envBottomRSlider.Value:0.00}";
            }

            if (_envBottomGSlider != null && _envBottomGValueText != null)
            {
                _envBottomGValueText.Text = $"{_envBottomGSlider.Value:0.00}";
            }

            if (_envBottomBSlider != null && _envBottomBValueText != null)
            {
                _envBottomBValueText.Text = $"{_envBottomBSlider.Value:0.00}";
            }

            if (_shadowStrengthSlider != null && _shadowStrengthValueText != null)
            {
                _shadowStrengthValueText.Text = $"{_shadowStrengthSlider.Value:0.000}";
            }

            if (_shadowSoftnessSlider != null && _shadowSoftnessValueText != null)
            {
                _shadowSoftnessValueText.Text = $"{_shadowSoftnessSlider.Value:0.000}";
            }

            if (_shadowDistanceSlider != null && _shadowDistanceValueText != null)
            {
                _shadowDistanceValueText.Text = $"{_shadowDistanceSlider.Value:0.00}";
            }

            if (_shadowScaleSlider != null && _shadowScaleValueText != null)
            {
                _shadowScaleValueText.Text = $"{_shadowScaleSlider.Value:0.00}";
            }

            if (_shadowQualitySlider != null && _shadowQualityValueText != null)
            {
                _shadowQualityValueText.Text = $"{_shadowQualitySlider.Value:0.000}";
            }

            if (_shadowGraySlider != null && _shadowGrayValueText != null)
            {
                _shadowGrayValueText.Text = $"{_shadowGraySlider.Value:0.00}";
            }

            if (_shadowDiffuseInfluenceSlider != null && _shadowDiffuseInfluenceValueText != null)
            {
                _shadowDiffuseInfluenceValueText.Text = $"{_shadowDiffuseInfluenceSlider.Value:0.00}";
            }

            if (_brushSizeSlider != null && _brushSizeValueText != null)
            {
                _brushSizeValueText.Text = $"{_brushSizeSlider.Value:0.0}px";
            }

            if (_brushOpacitySlider != null && _brushOpacityValueText != null)
            {
                _brushOpacityValueText.Text = $"{_brushOpacitySlider.Value:0.00}";
            }

            if (_brushDarknessSlider != null && _brushDarknessValueText != null)
            {
                _brushDarknessValueText.Text = $"{_brushDarknessSlider.Value:0.00}";
            }

            if (_brushSpreadSlider != null && _brushSpreadValueText != null)
            {
                _brushSpreadValueText.Text = $"{_brushSpreadSlider.Value:0.00}";
            }

            if (_paintCoatMetallicSlider != null && _paintCoatMetallicValueText != null)
            {
                _paintCoatMetallicValueText.Text = $"{_paintCoatMetallicSlider.Value:0.00}";
            }

            if (_paintCoatRoughnessSlider != null && _paintCoatRoughnessValueText != null)
            {
                _paintCoatRoughnessValueText.Text = $"{_paintCoatRoughnessSlider.Value:0.00}";
            }

            if (_clearCoatAmountSlider != null && _clearCoatAmountValueText != null)
            {
                _clearCoatAmountValueText.Text = $"{_clearCoatAmountSlider.Value:0.00}";
            }

            if (_clearCoatRoughnessSlider != null && _clearCoatRoughnessValueText != null)
            {
                _clearCoatRoughnessValueText.Text = $"{_clearCoatRoughnessSlider.Value:0.00}";
            }

            if (_anisotropyAngleSlider != null && _anisotropyAngleValueText != null)
            {
                _anisotropyAngleValueText.Text = $"{_anisotropyAngleSlider.Value:0.0}deg";
            }

            if (_scratchWidthSlider != null && _scratchWidthValueText != null)
            {
                _scratchWidthValueText.Text = $"{_scratchWidthSlider.Value:0.0}px";
            }

            if (_scratchDepthSlider != null && _scratchDepthValueText != null)
            {
                _scratchDepthValueText.Text = $"{_scratchDepthSlider.Value:0.00}";
            }

            if (_scratchResistanceSlider != null && _scratchResistanceValueText != null)
            {
                _scratchResistanceValueText.Text = $"{_scratchResistanceSlider.Value:0.00}";
            }

            if (_scratchDepthRampSlider != null && _scratchDepthRampValueText != null)
            {
                _scratchDepthRampValueText.Text = $"{_scratchDepthRampSlider.Value:0.0000}";
            }

            if (_scratchExposeColorRSlider != null && _scratchExposeColorRValueText != null)
            {
                _scratchExposeColorRValueText.Text = $"{_scratchExposeColorRSlider.Value:0.00}";
            }

            if (_scratchExposeColorGSlider != null && _scratchExposeColorGValueText != null)
            {
                _scratchExposeColorGValueText.Text = $"{_scratchExposeColorGSlider.Value:0.00}";
            }

            if (_scratchExposeColorBSlider != null && _scratchExposeColorBValueText != null)
            {
                _scratchExposeColorBValueText.Text = $"{_scratchExposeColorBSlider.Value:0.00}";
            }

            if (_scratchExposeMetallicSlider != null && _scratchExposeMetallicValueText != null)
            {
                _scratchExposeMetallicValueText.Text = $"{_scratchExposeMetallicSlider.Value:0.00}";
            }

            if (_scratchExposeRoughnessSlider != null && _scratchExposeRoughnessValueText != null)
            {
                _scratchExposeRoughnessValueText.Text = $"{_scratchExposeRoughnessSlider.Value:0.00}";
            }

            UpdatePrecisionControlEntryText();
        }
    }
}
