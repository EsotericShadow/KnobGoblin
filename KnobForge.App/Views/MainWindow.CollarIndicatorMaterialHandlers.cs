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
        private void OnCollarSettingsChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi ||
                _collarEnabledCheckBox == null ||
                _collarPresetCombo == null ||
                _collarMeshPathTextBox == null ||
                _collarScaleSlider == null ||
                _collarBodyLengthSlider == null ||
                _collarBodyThicknessSlider == null ||
                _collarHeadLengthSlider == null ||
                _collarHeadThicknessSlider == null ||
                _collarRotateSlider == null ||
                _collarMirrorXCheckBox == null ||
                _collarMirrorYCheckBox == null ||
                _collarMirrorZCheckBox == null ||
                _collarOffsetXSlider == null ||
                _collarOffsetYSlider == null ||
                _collarElevationSlider == null ||
                _collarInflateSlider == null)
            {
                return;
            }

            bool sliderChange = false;
            if (ReferenceEquals(sender, _collarEnabledCheckBox) ||
                ReferenceEquals(sender, _collarMirrorXCheckBox) ||
                ReferenceEquals(sender, _collarMirrorYCheckBox) ||
                ReferenceEquals(sender, _collarMirrorZCheckBox))
            {
                if (e.Property != ToggleButton.IsCheckedProperty)
                {
                    return;
                }
            }
            else if (ReferenceEquals(sender, _collarPresetCombo))
            {
                if (e.Property != ComboBox.SelectedItemProperty)
                {
                    return;
                }

                if (_collarPresetCombo.SelectedItem is CollarPresetOption candidate && !candidate.IsSelectable)
                {
                    CollarPresetOption fallback = ResolveSelectedCollarPresetOption();
                    WithUiRefreshSuppressed(() =>
                    {
                        _collarPresetCombo.SelectedItem = fallback;
                    });
                    return;
                }
            }
            else if (ReferenceEquals(sender, _collarMeshPathTextBox))
            {
                if (e.Property != TextBox.TextProperty)
                {
                    return;
                }
            }
            else if (e.Property != Slider.ValueProperty)
            {
                return;
            }
            else
            {
                sliderChange = true;
            }

            if (GetModelNode() == null)
            {
                return;
            }

            CollarNode collar = EnsureCollarNode();
            collar.Enabled = _collarEnabledCheckBox.IsChecked ?? false;
            CollarPresetOption selectedOption = ResolveSelectedCollarPresetOption();
            _lastSelectableCollarPresetOption = selectedOption;
            collar.Preset = selectedOption.Preset;
            string resolvedImportedMeshPath = selectedOption.ResolveImportedMeshPath(_collarMeshPathTextBox.Text);
            collar.ImportedMeshPath = resolvedImportedMeshPath;
            collar.ImportedScale = (float)_collarScaleSlider.Value;
            collar.ImportedBodyLengthScale = (float)_collarBodyLengthSlider.Value;
            collar.ImportedBodyThicknessScale = (float)_collarBodyThicknessSlider.Value;
            collar.ImportedHeadLengthScale = (float)_collarHeadLengthSlider.Value;
            collar.ImportedHeadThicknessScale = (float)_collarHeadThicknessSlider.Value;
            collar.ImportedRotationRadians = (float)DegreesToRadians(_collarRotateSlider.Value);
            collar.ImportedMirrorX = _collarMirrorXCheckBox.IsChecked ?? false;
            collar.ImportedMirrorY = _collarMirrorYCheckBox.IsChecked ?? false;
            collar.ImportedMirrorZ = _collarMirrorZCheckBox.IsChecked ?? false;
            collar.ImportedOffsetXRatio = (float)_collarOffsetXSlider.Value;
            collar.ImportedOffsetYRatio = (float)_collarOffsetYSlider.Value;
            collar.ElevationRatio = (float)_collarElevationSlider.Value;
            collar.ImportedInflateRatio = (float)_collarInflateSlider.Value;

            if (e.Property == ComboBox.SelectedItemProperty &&
                !string.Equals(_collarMeshPathTextBox.Text, resolvedImportedMeshPath, StringComparison.Ordinal))
            {
                WithUiRefreshSuppressed(() =>
                {
                    _collarMeshPathTextBox.Text = resolvedImportedMeshPath;
                });
            }

            if (sliderChange)
            {
                UpdateReadouts();
                RequestHeavyGeometryRefresh();
            }
            else
            {
                UpdateCollarControlEnablement(true, collar.Preset);
                NotifyProjectStateChanged();
            }
        }

        private void OnCollarMaterialChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi ||
                e.Property != Slider.ValueProperty ||
                _collarMaterialBaseRSlider == null ||
                _collarMaterialBaseGSlider == null ||
                _collarMaterialBaseBSlider == null ||
                _collarMaterialMetallicSlider == null ||
                _collarMaterialRoughnessSlider == null ||
                _collarMaterialPearlescenceSlider == null ||
                _collarMaterialRustSlider == null ||
                _collarMaterialWearSlider == null ||
                _collarMaterialGunkSlider == null)
            {
                return;
            }

            if (GetModelNode() == null)
            {
                return;
            }

            CollarNode collar = EnsureCollarNode();
            collar.BaseColor = new Vector3(
                (float)_collarMaterialBaseRSlider.Value,
                (float)_collarMaterialBaseGSlider.Value,
                (float)_collarMaterialBaseBSlider.Value);
            collar.Metallic = (float)_collarMaterialMetallicSlider.Value;
            collar.Roughness = (float)_collarMaterialRoughnessSlider.Value;
            collar.Pearlescence = (float)_collarMaterialPearlescenceSlider.Value;
            collar.RustAmount = (float)_collarMaterialRustSlider.Value;
            collar.WearAmount = (float)_collarMaterialWearSlider.Value;
            collar.GunkAmount = (float)_collarMaterialGunkSlider.Value;
            NotifyProjectStateChanged();
        }

        private void OnIndicatorSettingsChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi ||
                _indicatorEnabledCheckBox == null ||
                _indicatorCadWallsCheckBox == null ||
                _indicatorShapeCombo == null ||
                _indicatorReliefCombo == null ||
                _indicatorProfileCombo == null ||
                _indicatorWidthSlider == null ||
                _indicatorLengthSlider == null ||
                _indicatorPositionSlider == null ||
                _indicatorThicknessSlider == null ||
                _indicatorRoundnessSlider == null ||
                _indicatorColorBlendSlider == null ||
                _indicatorColorRSlider == null ||
                _indicatorColorGSlider == null ||
                _indicatorColorBSlider == null)
            {
                return;
            }

            if (ReferenceEquals(sender, _indicatorEnabledCheckBox) ||
                ReferenceEquals(sender, _indicatorCadWallsCheckBox))
            {
                if (e.Property != ToggleButton.IsCheckedProperty)
                {
                    return;
                }
            }
            else if (ReferenceEquals(sender, _indicatorShapeCombo) ||
                     ReferenceEquals(sender, _indicatorReliefCombo) ||
                     ReferenceEquals(sender, _indicatorProfileCombo))
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

            model.IndicatorEnabled = _indicatorEnabledCheckBox.IsChecked ?? true;
            model.IndicatorCadWallsEnabled = _indicatorCadWallsCheckBox.IsChecked ?? true;
            model.IndicatorShape = _indicatorShapeCombo.SelectedItem is IndicatorShape shape ? shape : IndicatorShape.Bar;
            model.IndicatorRelief = _indicatorReliefCombo.SelectedItem is IndicatorRelief relief ? relief : IndicatorRelief.Extrude;
            model.IndicatorProfile = _indicatorProfileCombo.SelectedItem is IndicatorProfile profile ? profile : IndicatorProfile.Straight;
            model.IndicatorWidthRatio = (float)_indicatorWidthSlider.Value;
            model.IndicatorLengthRatioTop = (float)_indicatorLengthSlider.Value;
            model.IndicatorPositionRatio = (float)_indicatorPositionSlider.Value;
            model.IndicatorThicknessRatio = (float)_indicatorThicknessSlider.Value;
            model.IndicatorRoundness = (float)_indicatorRoundnessSlider.Value;
            model.IndicatorColorBlend = (float)_indicatorColorBlendSlider.Value;
            model.IndicatorColor = new Vector3(
                (float)_indicatorColorRSlider.Value,
                (float)_indicatorColorGSlider.Value,
                (float)_indicatorColorBSlider.Value);

            NotifyProjectStateChanged();
        }

        private void OnMaterialBaseColorChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (!CanMutateSelectedMaterial(e, Slider.ValueProperty, out var material) ||
                _materialBaseRSlider == null || _materialBaseGSlider == null || _materialBaseBSlider == null)
            {
                return;
            }

            Vector3 color = new(
                (float)_materialBaseRSlider.Value,
                (float)_materialBaseGSlider.Value,
                (float)_materialBaseBSlider.Value);
            MaterialRegionTarget region = ResolveSelectedMaterialRegion();
            if (region == MaterialRegionTarget.WholeKnob)
            {
                material.BaseColor = color;
                if (material.PartMaterialsEnabled)
                {
                    material.PartMaterialsEnabled = false;
                }
                material.SyncPartMaterialsFromGlobal();
            }
            else
            {
                EnsurePartMaterialsEnabled(material);
                SetPartBaseColor(material, region, color);
            }

            NotifyProjectStateChanged();
        }

        private void OnMaterialMetallicChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (!CanMutateSelectedMaterial(e, Slider.ValueProperty, out var material) || _materialMetallicSlider == null)
            {
                return;
            }

            float metallic = (float)_materialMetallicSlider.Value;
            MaterialRegionTarget region = ResolveSelectedMaterialRegion();
            if (region == MaterialRegionTarget.WholeKnob)
            {
                material.Metallic = metallic;
                if (material.PartMaterialsEnabled)
                {
                    material.PartMaterialsEnabled = false;
                }
                material.SyncPartMaterialsFromGlobal();
            }
            else
            {
                EnsurePartMaterialsEnabled(material);
                SetPartMetallic(material, region, metallic);
            }

            NotifyProjectStateChanged();
        }

        private void OnMaterialRoughnessChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (!CanMutateSelectedMaterial(e, Slider.ValueProperty, out var material) || _materialRoughnessSlider == null)
            {
                return;
            }

            float roughness = (float)_materialRoughnessSlider.Value;
            MaterialRegionTarget region = ResolveSelectedMaterialRegion();
            if (region == MaterialRegionTarget.WholeKnob)
            {
                material.Roughness = roughness;
                if (material.PartMaterialsEnabled)
                {
                    material.PartMaterialsEnabled = false;
                }
                material.SyncPartMaterialsFromGlobal();
            }
            else
            {
                EnsurePartMaterialsEnabled(material);
                SetPartRoughness(material, region, roughness);
            }

            NotifyProjectStateChanged();
        }

        private void OnMaterialRegionChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi || _materialRegionCombo == null || e.Property != ComboBox.SelectedItemProperty)
            {
                return;
            }

            if (!TryGetSelectedMaterialNode(out MaterialNode material))
            {
                return;
            }

            MaterialRegionTarget region = ResolveSelectedMaterialRegion();
            bool mutated = false;
            if (region != MaterialRegionTarget.WholeKnob && !material.PartMaterialsEnabled)
            {
                material.SyncPartMaterialsFromGlobal();
                material.PartMaterialsEnabled = true;
                mutated = true;
            }

            ApplyMaterialRegionValuesToSliders(material);
            if (mutated)
            {
                NotifyProjectStateChanged();
            }
        }

        private void OnMaterialPearlescenceChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (!CanMutateSelectedMaterial(e, Slider.ValueProperty, out var material) || _materialPearlescenceSlider == null)
            {
                return;
            }

            material.Pearlescence = (float)_materialPearlescenceSlider.Value;
            NotifyProjectStateChanged();
        }

        private void OnMaterialAgingChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (!CanMutateSelectedMaterial(e, Slider.ValueProperty, out var material) ||
                _materialRustSlider == null || _materialWearSlider == null || _materialGunkSlider == null)
            {
                return;
            }

            material.RustAmount = (float)_materialRustSlider.Value;
            material.WearAmount = (float)_materialWearSlider.Value;
            material.GunkAmount = (float)_materialGunkSlider.Value;
            NotifyProjectStateChanged();
        }

        private void OnMaterialSurfaceCharacterChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (!CanMutateSelectedMaterial(e, Slider.ValueProperty, out var material) ||
                _materialBrushStrengthSlider == null || _materialBrushDensitySlider == null || _materialCharacterSlider == null)
            {
                return;
            }

            material.RadialBrushStrength = (float)_materialBrushStrengthSlider.Value;
            material.RadialBrushDensity = (float)_materialBrushDensitySlider.Value;
            material.SurfaceCharacter = (float)_materialCharacterSlider.Value;
            NotifyProjectStateChanged();
        }

        private void OnMicroDetailSettingsChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi ||
                _spiralNormalInfluenceCheckBox == null ||
                _basisDebugModeCombo == null ||
                _microLodFadeStartSlider == null ||
                _microLodFadeEndSlider == null ||
                _microRoughnessLodBoostSlider == null)
            {
                return;
            }

            if (ReferenceEquals(sender, _spiralNormalInfluenceCheckBox))
            {
                if (e.Property != ToggleButton.IsCheckedProperty)
                {
                    return;
                }
            }
            else if (ReferenceEquals(sender, _basisDebugModeCombo))
            {
                if (e.Property != SelectingItemsControl.SelectedItemProperty)
                {
                    return;
                }
            }
            else if (e.Property != Slider.ValueProperty)
            {
                return;
            }

            float fadeStart = Math.Clamp((float)_microLodFadeStartSlider.Value, 0.1f, 10f);
            float fadeEnd = Math.Clamp((float)_microLodFadeEndSlider.Value, 0.1f, 12f);
            float minEnd = fadeStart + 0.01f;
            if (fadeEnd < minEnd)
            {
                fadeEnd = minEnd;
                bool previousUpdatingUi = _updatingUi;
                _updatingUi = true;
                try
                {
                    _microLodFadeEndSlider.Value = fadeEnd;
                }
                finally
                {
                    _updatingUi = previousUpdatingUi;
                }
            }

            _project.SpiralNormalInfluenceEnabled = _spiralNormalInfluenceCheckBox.IsChecked ?? true;
            _project.BasisDebug = _basisDebugModeCombo.SelectedItem is BasisDebugMode mode
                ? mode
                : BasisDebugMode.Off;
            _project.SpiralNormalLodFadeStart = fadeStart;
            _project.SpiralNormalLodFadeEnd = fadeEnd;
            _project.SpiralRoughnessLodBoost = Math.Clamp((float)_microRoughnessLodBoostSlider.Value, 0f, 1f);
            NotifyRenderOnly();
        }
        private bool CanMutateSelectedMaterial(AvaloniaPropertyChangedEventArgs e, AvaloniaProperty expectedProperty, out MaterialNode material)
        {
            material = null!;
            if (_updatingUi || e.Property != expectedProperty)
            {
                return false;
            }

            if (!TryGetSelectedMaterialNode(out MaterialNode selected))
            {
                return false;
            }

            material = selected;
            return true;
        }

        private bool TryGetSelectedMaterialNode(out MaterialNode material)
        {
            material = null!;
            ModelNode? model = GetModelNode();
            if (model == null)
            {
                return false;
            }

            MaterialNode? selected = model.Children.OfType<MaterialNode>().FirstOrDefault();
            if (selected == null)
            {
                return false;
            }

            material = selected;
            return true;
        }

        private MaterialRegionTarget ResolveSelectedMaterialRegion()
        {
            if (_materialRegionCombo?.SelectedItem is MaterialRegionTarget region)
            {
                return region;
            }

            return MaterialRegionTarget.WholeKnob;
        }

        private void EnsurePartMaterialsEnabled(MaterialNode material)
        {
            if (material.PartMaterialsEnabled)
            {
                return;
            }

            material.SyncPartMaterialsFromGlobal();
            material.PartMaterialsEnabled = true;
        }

        private static Vector3 GetPartBaseColor(MaterialNode material, MaterialRegionTarget region)
        {
            return region switch
            {
                MaterialRegionTarget.TopCap => material.TopBaseColor,
                MaterialRegionTarget.Bevel => material.BevelBaseColor,
                MaterialRegionTarget.Side => material.SideBaseColor,
                _ => material.BaseColor
            };
        }

        private static float GetPartMetallic(MaterialNode material, MaterialRegionTarget region)
        {
            return region switch
            {
                MaterialRegionTarget.TopCap => material.TopMetallic,
                MaterialRegionTarget.Bevel => material.BevelMetallic,
                MaterialRegionTarget.Side => material.SideMetallic,
                _ => material.Metallic
            };
        }

        private static float GetPartRoughness(MaterialNode material, MaterialRegionTarget region)
        {
            return region switch
            {
                MaterialRegionTarget.TopCap => material.TopRoughness,
                MaterialRegionTarget.Bevel => material.BevelRoughness,
                MaterialRegionTarget.Side => material.SideRoughness,
                _ => material.Roughness
            };
        }

        private static void SetPartBaseColor(MaterialNode material, MaterialRegionTarget region, Vector3 color)
        {
            switch (region)
            {
                case MaterialRegionTarget.TopCap:
                    material.TopBaseColor = color;
                    break;
                case MaterialRegionTarget.Bevel:
                    material.BevelBaseColor = color;
                    break;
                case MaterialRegionTarget.Side:
                    material.SideBaseColor = color;
                    break;
                default:
                    material.BaseColor = color;
                    break;
            }
        }

        private static void SetPartMetallic(MaterialNode material, MaterialRegionTarget region, float metallic)
        {
            switch (region)
            {
                case MaterialRegionTarget.TopCap:
                    material.TopMetallic = metallic;
                    break;
                case MaterialRegionTarget.Bevel:
                    material.BevelMetallic = metallic;
                    break;
                case MaterialRegionTarget.Side:
                    material.SideMetallic = metallic;
                    break;
                default:
                    material.Metallic = metallic;
                    break;
            }
        }

        private static void SetPartRoughness(MaterialNode material, MaterialRegionTarget region, float roughness)
        {
            switch (region)
            {
                case MaterialRegionTarget.TopCap:
                    material.TopRoughness = roughness;
                    break;
                case MaterialRegionTarget.Bevel:
                    material.BevelRoughness = roughness;
                    break;
                case MaterialRegionTarget.Side:
                    material.SideRoughness = roughness;
                    break;
                default:
                    material.Roughness = roughness;
                    break;
            }
        }

        private void ApplyMaterialRegionValuesToSliders(MaterialNode material)
        {
            if (_materialRegionCombo == null ||
                _materialBaseRSlider == null ||
                _materialBaseGSlider == null ||
                _materialBaseBSlider == null ||
                _materialMetallicSlider == null ||
                _materialRoughnessSlider == null)
            {
                return;
            }

            bool previousUpdatingUi = _updatingUi;
            _updatingUi = true;
            try
            {
                if (_materialRegionCombo.SelectedItem is not MaterialRegionTarget)
                {
                    _materialRegionCombo.SelectedItem = MaterialRegionTarget.WholeKnob;
                }

                MaterialRegionTarget region = ResolveSelectedMaterialRegion();
                if (!material.PartMaterialsEnabled && region != MaterialRegionTarget.WholeKnob)
                {
                    _materialRegionCombo.SelectedItem = MaterialRegionTarget.WholeKnob;
                    region = MaterialRegionTarget.WholeKnob;
                }
                Vector3 color = GetPartBaseColor(material, region);
                _materialBaseRSlider.Value = color.X;
                _materialBaseGSlider.Value = color.Y;
                _materialBaseBSlider.Value = color.Z;
                _materialMetallicSlider.Value = GetPartMetallic(material, region);
                _materialRoughnessSlider.Value = GetPartRoughness(material, region);
            }
            finally
            {
                _updatingUi = previousUpdatingUi;
            }

            UpdateReadouts();
        }
    }
}
