using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KnobForge.App.Controls;
using KnobForge.Core;
using KnobForge.Core.Scene;
using KnobForge.Rendering;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Reflection;

namespace KnobForge.App.Views
{
        public partial class MainWindow : Window
        {
        private async void OnRenderButtonClick(object? sender, RoutedEventArgs e)
        {
            if (_metalViewport == null)
            {
                return;
            }

            var dialog = new RenderSettingsWindow(_project, _metalViewport.CurrentOrientation, _metalViewport.CurrentCameraState, _metalViewport)
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            await dialog.ShowDialog(this);
        }

        private void AddLight()
        {
            float offset = 120f * _project.Lights.Count;
            _project.AddLight(offset, offset * 0.25f, 0f);
            NotifyProjectStateChanged();
        }

        private void RemoveSelectedLight()
        {
            if (_project.RemoveSelectedLight())
            {
                NotifyProjectStateChanged();
            }
        }

        private void CenterLight()
        {
            _project.EnsureSelection();
            KnobLight? light = _project.SelectedLight;
            if (light == null)
            {
                return;
            }

            light.X = 0f;
            light.Y = 0f;
            light.Z = 0f;
            NotifyProjectStateChanged();
        }

        private void NotifyProjectStateChanged(
            InspectorRefreshTabPolicy tabPolicy = InspectorRefreshTabPolicy.PreserveCurrentTab,
            bool syncSelectionFromInspectorContext = true)
        {
            _metalViewport?.InvalidateGpu();
            if (syncSelectionFromInspectorContext)
            {
                TryAdoptSceneSelectionFromInspectorContext();
            }

            RefreshSceneTree();
            RefreshInspectorFromProject(tabPolicy);
            CaptureUndoSnapshotIfChanged();
        }

        private void NotifyRenderOnly(bool syncSelectionFromInspectorContext = true)
        {
            _metalViewport?.InvalidateGpu();
            if (syncSelectionFromInspectorContext && TryAdoptSceneSelectionFromInspectorContext())
            {
                RefreshSceneTree();
            }

            UpdateReadouts();
            CaptureUndoSnapshotIfChanged();
        }

        private void RefreshSceneTree()
        {
            if (_sceneListBox == null)
            {
                return;
            }

            if (_sceneListBox.Bounds.Height <= 0)
            {
                if (!_sceneRefreshDeferredPending)
                {
                    _sceneRefreshDeferredPending = true;
                    Dispatcher.UIThread.Post(() =>
                    {
                        _sceneRefreshDeferredPending = false;
                        RefreshSceneTree();
                    }, DispatcherPriority.Loaded);

                    Dispatcher.UIThread.Post(() =>
                    {
                        _sceneRefreshDeferredPending = false;
                        RefreshSceneTree();
                    }, DispatcherPriority.Render);
                }

                return;
            }

            WithUiRefreshSuppressed(() =>
            {
                var flatList = new List<SceneNode>();

                void Traverse(SceneNode node)
                {
                    flatList.Add(node);
                    foreach (var child in node.Children)
                    {
                        Traverse(child);
                    }
                }

                Traverse(_project.SceneRoot);

                if (_sceneNodes.Count == flatList.Count)
                {
                    bool identical = true;
                    for (int i = 0; i < flatList.Count; i++)
                    {
                        if (_sceneNodes[i].Id != flatList[i].Id)
                        {
                            identical = false;
                            break;
                        }
                    }

                    if (identical)
                    {
                        SyncSceneListSelectionToProjectNode();
                        return;
                    }
                }

                int sharedCount = Math.Min(_sceneNodes.Count, flatList.Count);
                for (int i = 0; i < sharedCount; i++)
                {
                    if (_sceneNodes[i].Id != flatList[i].Id)
                    {
                        _sceneNodes[i] = flatList[i];
                    }
                }

                if (_sceneNodes.Count > flatList.Count)
                {
                    for (int i = _sceneNodes.Count - 1; i >= flatList.Count; i--)
                    {
                        _sceneNodes.RemoveAt(i);
                    }
                }
                else if (_sceneNodes.Count < flatList.Count)
                {
                    for (int i = _sceneNodes.Count; i < flatList.Count; i++)
                    {
                        _sceneNodes.Add(flatList[i]);
                    }
                }

                SyncSceneListSelectionToProjectNode();

                if (_sceneNodes.Count > 0)
                {
                    _sceneListBox.ScrollIntoView(_sceneNodes[0]);
                    _sceneListBox.ScrollIntoView(_sceneNodes[_sceneNodes.Count - 1]);
                    _sceneListBox.ScrollIntoView(_sceneNodes[0]);

                    _sceneListBox.InvalidateVisual();
                    _sceneListBox.InvalidateMeasure();
                    _sceneListBox.InvalidateArrange();
                    (_sceneListBox.ContainerFromIndex(0) as Control)?.InvalidateVisual();
                }
            });
        }

        private ModelNode? GetModelNode()
        {
            return _project.SceneRoot.Children
                .OfType<ModelNode>()
                .FirstOrDefault();
        }

        private CollarNode? GetCollarNode()
        {
            return GetModelNode()?
                .Children
                .OfType<CollarNode>()
                .FirstOrDefault();
        }

        private CollarNode EnsureCollarNode()
        {
            ModelNode model = GetModelNode() ?? throw new InvalidOperationException("Model node is missing.");
            CollarNode? collar = model.Children.OfType<CollarNode>().FirstOrDefault();
            if (collar is not null)
            {
                return collar;
            }

            collar = new CollarNode("SnakeOuroborosCollar")
            {
                Enabled = false
            };
            model.AddChild(collar);
            return collar;
        }

        private void UpdateCollarControlEnablement(bool hasModel, CollarPreset preset)
        {
            if (_collarEnabledCheckBox == null ||
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
                _collarInflateSlider == null ||
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

            _collarEnabledCheckBox.IsEnabled = hasModel;
            _collarPresetCombo.IsEnabled = hasModel;

            bool importedPreset = hasModel && CollarNode.IsImportedMeshPreset(preset);
            CollarPresetOption selectedOption = ResolveSelectedCollarPresetOption();
            bool customImportedPreset = importedPreset && selectedOption.AllowsCustomPathEntry;
            _collarMeshPathTextBox.IsEnabled = importedPreset;
            _collarMeshPathTextBox.IsReadOnly = importedPreset && !customImportedPreset;
            _collarScaleSlider.IsEnabled = importedPreset;
            _collarBodyLengthSlider.IsEnabled = importedPreset;
            _collarBodyThicknessSlider.IsEnabled = importedPreset;
            _collarHeadLengthSlider.IsEnabled = importedPreset;
            _collarHeadThicknessSlider.IsEnabled = importedPreset;
            _collarRotateSlider.IsEnabled = importedPreset;
            _collarMirrorXCheckBox.IsEnabled = importedPreset;
            _collarMirrorYCheckBox.IsEnabled = importedPreset;
            _collarMirrorZCheckBox.IsEnabled = importedPreset;
            _collarOffsetXSlider.IsEnabled = importedPreset;
            _collarOffsetYSlider.IsEnabled = importedPreset;
            _collarElevationSlider.IsEnabled = hasModel;
            _collarInflateSlider.IsEnabled = importedPreset;
            if (_collarScaleInputTextBox != null)
            {
                _collarScaleInputTextBox.IsEnabled = _collarScaleSlider.IsEnabled;
            }

            if (_collarBodyLengthInputTextBox != null)
            {
                _collarBodyLengthInputTextBox.IsEnabled = _collarBodyLengthSlider.IsEnabled;
            }

            if (_collarBodyThicknessInputTextBox != null)
            {
                _collarBodyThicknessInputTextBox.IsEnabled = _collarBodyThicknessSlider.IsEnabled;
            }

            if (_collarHeadLengthInputTextBox != null)
            {
                _collarHeadLengthInputTextBox.IsEnabled = _collarHeadLengthSlider.IsEnabled;
            }

            if (_collarHeadThicknessInputTextBox != null)
            {
                _collarHeadThicknessInputTextBox.IsEnabled = _collarHeadThicknessSlider.IsEnabled;
            }

            if (_collarRotateInputTextBox != null)
            {
                _collarRotateInputTextBox.IsEnabled = _collarRotateSlider.IsEnabled;
            }

            if (_collarOffsetXInputTextBox != null)
            {
                _collarOffsetXInputTextBox.IsEnabled = _collarOffsetXSlider.IsEnabled;
            }

            if (_collarOffsetYInputTextBox != null)
            {
                _collarOffsetYInputTextBox.IsEnabled = _collarOffsetYSlider.IsEnabled;
            }

            if (_collarElevationInputTextBox != null)
            {
                _collarElevationInputTextBox.IsEnabled = _collarElevationSlider.IsEnabled;
            }

            if (_collarInflateInputTextBox != null)
            {
                _collarInflateInputTextBox.IsEnabled = _collarInflateSlider.IsEnabled;
            }

            bool collarMaterialEnabled = hasModel;
            _collarMaterialBaseRSlider.IsEnabled = collarMaterialEnabled;
            _collarMaterialBaseGSlider.IsEnabled = collarMaterialEnabled;
            _collarMaterialBaseBSlider.IsEnabled = collarMaterialEnabled;
            _collarMaterialMetallicSlider.IsEnabled = collarMaterialEnabled;
            _collarMaterialRoughnessSlider.IsEnabled = collarMaterialEnabled;
            _collarMaterialPearlescenceSlider.IsEnabled = collarMaterialEnabled;
            _collarMaterialRustSlider.IsEnabled = collarMaterialEnabled;
            _collarMaterialWearSlider.IsEnabled = collarMaterialEnabled;
            _collarMaterialGunkSlider.IsEnabled = collarMaterialEnabled;
            UpdateCollarMeshPathFeedback(preset, _collarMeshPathTextBox.Text, customImportedPreset);
        }

        private void RefreshInspectorFromProject(InspectorRefreshTabPolicy tabPolicy = InspectorRefreshTabPolicy.PreserveCurrentTab)
        {
            if (_lightingModeCombo == null || _lightListBox == null ||
                _removeLightButton == null || _rotationSlider == null || _lightTypeCombo == null ||
                _lightXSlider == null || _lightYSlider == null || _lightZSlider == null ||
                _directionSlider == null || _intensitySlider == null || _falloffSlider == null ||
                _lightRSlider == null || _lightGSlider == null || _lightBSlider == null ||
                _diffuseBoostSlider == null || _specularBoostSlider == null || _specularPowerSlider == null ||
                _modelRadiusSlider == null || _modelHeightSlider == null || _modelTopScaleSlider == null ||
                _modelBevelSlider == null || _referenceStyleCombo == null || _referenceStyleSaveNameTextBox == null || _saveReferenceProfileButton == null || _bodyStyleCombo == null || _bevelCurveSlider == null || _crownProfileSlider == null ||
                _bodyTaperSlider == null || _bodyBulgeSlider == null || _modelSegmentsSlider == null ||
                _spiralRidgeHeightSlider == null || _spiralRidgeWidthSlider == null || _spiralTurnsSlider == null ||
                _gripStyleCombo == null || _gripTypeCombo == null || _gripStartSlider == null || _gripHeightSlider == null ||
                _gripDensitySlider == null || _gripPitchSlider == null || _gripDepthSlider == null ||
                _gripWidthSlider == null || _gripSharpnessSlider == null ||
                _collarEnabledCheckBox == null || _collarPresetCombo == null || _collarMeshPathTextBox == null ||
                _collarScaleSlider == null || _collarBodyLengthSlider == null || _collarBodyThicknessSlider == null ||
                _collarHeadLengthSlider == null || _collarHeadThicknessSlider == null ||
                _collarRotateSlider == null || _collarMirrorXCheckBox == null || _collarMirrorYCheckBox == null || _collarMirrorZCheckBox == null ||
                _collarOffsetXSlider == null || _collarOffsetYSlider == null || _collarElevationSlider == null || _collarInflateSlider == null ||
                _collarMaterialBaseRSlider == null || _collarMaterialBaseGSlider == null || _collarMaterialBaseBSlider == null ||
                _collarMaterialMetallicSlider == null || _collarMaterialRoughnessSlider == null || _collarMaterialPearlescenceSlider == null ||
                _collarMaterialRustSlider == null || _collarMaterialWearSlider == null || _collarMaterialGunkSlider == null ||
                _indicatorEnabledCheckBox == null || _indicatorShapeCombo == null || _indicatorReliefCombo == null ||
                _indicatorProfileCombo == null || _indicatorWidthSlider == null || _indicatorLengthSlider == null ||
                _indicatorPositionSlider == null ||
                _indicatorThicknessSlider == null || _indicatorRoundnessSlider == null || _indicatorColorBlendSlider == null ||
                _indicatorColorRSlider == null || _indicatorColorGSlider == null || _indicatorColorBSlider == null ||
                _materialBaseRSlider == null || _materialBaseGSlider == null || _materialBaseBSlider == null || _materialRegionCombo == null ||
                _materialMetallicSlider == null || _materialRoughnessSlider == null || _materialPearlescenceSlider == null ||
                _materialRustSlider == null || _materialWearSlider == null || _materialGunkSlider == null ||
                _materialBrushStrengthSlider == null || _materialBrushDensitySlider == null || _materialCharacterSlider == null ||
                _spiralNormalInfluenceCheckBox == null || _basisDebugModeCombo == null || _microLodFadeStartSlider == null || _microLodFadeEndSlider == null || _microRoughnessLodBoostSlider == null ||
                _envIntensitySlider == null || _envRoughnessMixSlider == null ||
                _envTopRSlider == null || _envTopGSlider == null || _envTopBSlider == null ||
                _envBottomRSlider == null || _envBottomGSlider == null || _envBottomBSlider == null ||
                _shadowEnabledCheckBox == null || _shadowSourceModeCombo == null || _shadowStrengthSlider == null || _shadowSoftnessSlider == null ||
                _shadowDistanceSlider == null || _shadowScaleSlider == null || _shadowQualitySlider == null ||
                _shadowGraySlider == null || _shadowDiffuseInfluenceSlider == null ||
                _brushPaintEnabledCheckBox == null || _brushPaintChannelCombo == null || _brushTypeCombo == null || _brushPaintColorPicker == null || _scratchAbrasionTypeCombo == null ||
                _brushSizeSlider == null || _brushOpacitySlider == null || _brushDarknessSlider == null || _brushSpreadSlider == null ||
                _paintCoatMetallicSlider == null || _paintCoatRoughnessSlider == null ||
                _clearCoatAmountSlider == null || _clearCoatRoughnessSlider == null || _anisotropyAngleSlider == null ||
                _scratchWidthSlider == null || _scratchDepthSlider == null || _scratchResistanceSlider == null || _scratchDepthRampSlider == null ||
                _scratchExposeColorRSlider == null || _scratchExposeColorGSlider == null || _scratchExposeColorBSlider == null ||
                _scratchExposeMetallicSlider == null || _scratchExposeRoughnessSlider == null)
            {
                return;
            }

            RememberInspectorPresentationStateForCurrentTab();
            InspectorFocusState? preservedFocus = CaptureInspectorFocusStateForCurrentTab();
            TabItem? preservedTab = _inspectorTabControl?.SelectedItem as TabItem;
            _updatingUi = true;
            try
            {
                var project = _project;
                project.EnsureSelection();
                var model = GetModelNode();
                var material = model?.Children.OfType<MaterialNode>().FirstOrDefault();
                var collar = model?.Children.OfType<CollarNode>().FirstOrDefault();

                _lightingModeCombo.SelectedItem = project.Mode;

                var lightLabels = new List<string>();
                for (int i = 0; i < project.Lights.Count; i++)
                {
                    var l = project.Lights[i];
                    lightLabels.Add($"{i + 1}. {l.Name} [{l.Type}]");
                }

                DetachLightListHandler();
                _lightListBox.ItemsSource = lightLabels;
                _lightListBox.SelectedIndex = project.SelectedLightIndex;
                AttachLightListHandler();
                _removeLightButton.IsEnabled = project.Lights.Count > 1;

                if (model != null)
                {
                    SelectReferenceStyleOptionForModel(model);
                    _bodyStyleCombo.SelectedItem = model.BodyStyle;
                    _rotationSlider.Value = model.RotationRadians;
                    _modelRadiusSlider.Value = model.Radius;
                    _modelHeightSlider.Value = model.Height;
                    _modelTopScaleSlider.Value = model.TopRadiusScale;
                    _modelBevelSlider.Value = model.Bevel;
                    _bevelCurveSlider.Value = model.BevelCurve;
                    _crownProfileSlider.Value = model.CrownProfile;
                    _bodyTaperSlider.Value = model.BodyTaper;
                    _bodyBulgeSlider.Value = model.BodyBulge;
                    _modelSegmentsSlider.Value = model.RadialSegments;
                    _spiralRidgeHeightSlider.Value = model.SpiralRidgeHeight;
                    _spiralRidgeWidthSlider.Value = model.SpiralRidgeWidth;
                    _spiralTurnsSlider.Value = model.SpiralTurns;
                    _gripStyleCombo.SelectedItem = model.GripStyle;
                    _gripTypeCombo.SelectedItem = model.GripType;
                    _gripStartSlider.Value = model.GripStart;
                    _gripHeightSlider.Value = model.GripHeight;
                    _gripDensitySlider.Value = model.GripDensity;
                    _gripPitchSlider.Value = model.GripPitch;
                    _gripDepthSlider.Value = model.GripDepth;
                    _gripWidthSlider.Value = model.GripWidth;
                    _gripSharpnessSlider.Value = model.GripSharpness;
                    if (collar != null)
                    {
                        _collarEnabledCheckBox.IsChecked = collar.Enabled;
                        CollarPresetOption collarOption = ResolveCollarPresetOptionForState(collar.Preset, collar.ImportedMeshPath);
                        _collarPresetCombo.SelectedItem = collarOption;
                        _lastSelectableCollarPresetOption = collarOption;
                        _collarMeshPathTextBox.Text = collarOption.ResolveImportedMeshPath(collar.ImportedMeshPath);
                        _collarScaleSlider.Value = collar.ImportedScale;
                        _collarBodyLengthSlider.Value = collar.ImportedBodyLengthScale;
                        _collarBodyThicknessSlider.Value = collar.ImportedBodyThicknessScale;
                        _collarHeadLengthSlider.Value = collar.ImportedHeadLengthScale;
                        _collarHeadThicknessSlider.Value = collar.ImportedHeadThicknessScale;
                        _collarRotateSlider.Value = RadiansToDegrees(collar.ImportedRotationRadians);
                        _collarMirrorXCheckBox.IsChecked = collar.ImportedMirrorX;
                        _collarMirrorYCheckBox.IsChecked = collar.ImportedMirrorY;
                        _collarMirrorZCheckBox.IsChecked = collar.ImportedMirrorZ;
                        _collarOffsetXSlider.Value = collar.ImportedOffsetXRatio;
                        _collarOffsetYSlider.Value = collar.ImportedOffsetYRatio;
                        _collarElevationSlider.Value = collar.ElevationRatio;
                        _collarInflateSlider.Value = collar.ImportedInflateRatio;
                        _collarMaterialBaseRSlider.Value = collar.BaseColor.X;
                        _collarMaterialBaseGSlider.Value = collar.BaseColor.Y;
                        _collarMaterialBaseBSlider.Value = collar.BaseColor.Z;
                        _collarMaterialMetallicSlider.Value = collar.Metallic;
                        _collarMaterialRoughnessSlider.Value = collar.Roughness;
                        _collarMaterialPearlescenceSlider.Value = collar.Pearlescence;
                        _collarMaterialRustSlider.Value = collar.RustAmount;
                        _collarMaterialWearSlider.Value = collar.WearAmount;
                        _collarMaterialGunkSlider.Value = collar.GunkAmount;
                    }
                    else
                    {
                        _collarEnabledCheckBox.IsChecked = false;
                        CollarPresetOption noneOption = ResolveCollarPresetOptionForState(CollarPreset.None, null);
                        _collarPresetCombo.SelectedItem = noneOption;
                        _lastSelectableCollarPresetOption = noneOption;
                        _collarMeshPathTextBox.Text = string.Empty;
                        _collarScaleSlider.Value = 1.0;
                        _collarBodyLengthSlider.Value = 1.0;
                        _collarBodyThicknessSlider.Value = 1.0;
                        _collarHeadLengthSlider.Value = 1.0;
                        _collarHeadThicknessSlider.Value = 1.0;
                        _collarRotateSlider.Value = 0.0;
                        _collarMirrorXCheckBox.IsChecked = false;
                        _collarMirrorYCheckBox.IsChecked = false;
                        _collarMirrorZCheckBox.IsChecked = false;
                        _collarOffsetXSlider.Value = 0.0;
                        _collarOffsetYSlider.Value = 0.0;
                        _collarElevationSlider.Value = 0.0;
                        _collarInflateSlider.Value = 0.0;
                        _collarMaterialBaseRSlider.Value = 0.74;
                        _collarMaterialBaseGSlider.Value = 0.74;
                        _collarMaterialBaseBSlider.Value = 0.70;
                        _collarMaterialMetallicSlider.Value = 0.96;
                        _collarMaterialRoughnessSlider.Value = 0.32;
                        _collarMaterialPearlescenceSlider.Value = 0.0;
                        _collarMaterialRustSlider.Value = 0.0;
                        _collarMaterialWearSlider.Value = 0.0;
                        _collarMaterialGunkSlider.Value = 0.0;
                    }
                    _indicatorEnabledCheckBox.IsChecked = model.IndicatorEnabled;
                    if (_indicatorCadWallsCheckBox != null)
                    {
                        _indicatorCadWallsCheckBox.IsChecked = model.IndicatorCadWallsEnabled;
                    }
                    _indicatorShapeCombo.SelectedItem = model.IndicatorShape;
                    _indicatorReliefCombo.SelectedItem = model.IndicatorRelief;
                    _indicatorProfileCombo.SelectedItem = model.IndicatorProfile;
                    _indicatorWidthSlider.Value = model.IndicatorWidthRatio;
                    _indicatorLengthSlider.Value = model.IndicatorLengthRatioTop;
                    _indicatorPositionSlider.Value = model.IndicatorPositionRatio;
                    _indicatorThicknessSlider.Value = model.IndicatorThicknessRatio;
                    _indicatorRoundnessSlider.Value = model.IndicatorRoundness;
                    _indicatorColorBlendSlider.Value = model.IndicatorColorBlend;
                    _indicatorColorRSlider.Value = model.IndicatorColor.X;
                    _indicatorColorGSlider.Value = model.IndicatorColor.Y;
                    _indicatorColorBSlider.Value = model.IndicatorColor.Z;
                }

                bool hasModel = model != null;
                UpdateCollarControlEnablement(hasModel, collar?.Preset ?? CollarPreset.None);
                _indicatorEnabledCheckBox.IsEnabled = hasModel;
                if (_indicatorCadWallsCheckBox != null)
                {
                    _indicatorCadWallsCheckBox.IsEnabled = hasModel;
                }
                _indicatorShapeCombo.IsEnabled = hasModel;
                _indicatorReliefCombo.IsEnabled = hasModel;
                _indicatorProfileCombo.IsEnabled = hasModel;
                _indicatorWidthSlider.IsEnabled = hasModel;
                _indicatorLengthSlider.IsEnabled = hasModel;
                _indicatorPositionSlider.IsEnabled = hasModel;
                _indicatorThicknessSlider.IsEnabled = hasModel;
                _indicatorRoundnessSlider.IsEnabled = hasModel;
                _indicatorColorBlendSlider.IsEnabled = hasModel;
                _indicatorColorRSlider.IsEnabled = hasModel;
                _indicatorColorGSlider.IsEnabled = hasModel;
                _indicatorColorBSlider.IsEnabled = hasModel;

                bool hasMaterial = material != null;
                _materialBaseRSlider.IsEnabled = hasMaterial;
                _materialBaseGSlider.IsEnabled = hasMaterial;
                _materialBaseBSlider.IsEnabled = hasMaterial;
                _materialRegionCombo.IsEnabled = hasMaterial;
                _materialMetallicSlider.IsEnabled = hasMaterial;
                _materialRoughnessSlider.IsEnabled = hasMaterial;
                _materialPearlescenceSlider.IsEnabled = hasMaterial;
                _materialRustSlider.IsEnabled = hasMaterial;
                _materialWearSlider.IsEnabled = hasMaterial;
                _materialGunkSlider.IsEnabled = hasMaterial;
                _materialBrushStrengthSlider.IsEnabled = hasMaterial;
                _materialBrushDensitySlider.IsEnabled = hasMaterial;
                _materialCharacterSlider.IsEnabled = hasMaterial;
                _spiralNormalInfluenceCheckBox.IsEnabled = hasMaterial;
                _basisDebugModeCombo.IsEnabled = hasMaterial;
                _microLodFadeStartSlider.IsEnabled = hasMaterial;
                _microLodFadeEndSlider.IsEnabled = hasMaterial;
                _microRoughnessLodBoostSlider.IsEnabled = hasMaterial;
                _brushPaintEnabledCheckBox.IsEnabled = hasModel;
                _brushPaintChannelCombo.IsEnabled = hasModel;
                _brushTypeCombo.IsEnabled = hasModel;
                _brushPaintColorPicker.IsEnabled = hasModel;
                _scratchAbrasionTypeCombo.IsEnabled = hasModel;
                UpdateReferenceProfileActionEnablement(hasModel);
                _brushSizeSlider.IsEnabled = hasModel;
                _brushOpacitySlider.IsEnabled = hasModel;
                _brushDarknessSlider.IsEnabled = hasModel;
                _brushSpreadSlider.IsEnabled = hasModel;
                _paintCoatMetallicSlider.IsEnabled = hasModel;
                _paintCoatRoughnessSlider.IsEnabled = hasModel;
                _clearCoatAmountSlider.IsEnabled = hasModel;
                _clearCoatRoughnessSlider.IsEnabled = hasModel;
                _anisotropyAngleSlider.IsEnabled = hasModel;
                _scratchWidthSlider.IsEnabled = hasModel;
                _scratchDepthSlider.IsEnabled = hasModel;
                _scratchResistanceSlider.IsEnabled = hasModel;
                _scratchDepthRampSlider.IsEnabled = hasModel;
                _scratchExposeColorRSlider.IsEnabled = hasModel;
                _scratchExposeColorGSlider.IsEnabled = hasModel;
                _scratchExposeColorBSlider.IsEnabled = hasModel;
                _scratchExposeMetallicSlider.IsEnabled = hasModel;
                _scratchExposeRoughnessSlider.IsEnabled = hasModel;
                if (_brushSizeInputTextBox != null)
                {
                    _brushSizeInputTextBox.IsEnabled = hasModel;
                }

                if (_brushOpacityInputTextBox != null)
                {
                    _brushOpacityInputTextBox.IsEnabled = hasModel;
                }

                if (_brushDarknessInputTextBox != null)
                {
                    _brushDarknessInputTextBox.IsEnabled = hasModel;
                }

                if (_brushSpreadInputTextBox != null)
                {
                    _brushSpreadInputTextBox.IsEnabled = hasModel;
                }

                if (_paintCoatMetallicInputTextBox != null)
                {
                    _paintCoatMetallicInputTextBox.IsEnabled = hasModel;
                }

                if (_paintCoatRoughnessInputTextBox != null)
                {
                    _paintCoatRoughnessInputTextBox.IsEnabled = hasModel;
                }

                if (_clearCoatAmountInputTextBox != null)
                {
                    _clearCoatAmountInputTextBox.IsEnabled = hasModel;
                }

                if (_clearCoatRoughnessInputTextBox != null)
                {
                    _clearCoatRoughnessInputTextBox.IsEnabled = hasModel;
                }

                if (_anisotropyAngleInputTextBox != null)
                {
                    _anisotropyAngleInputTextBox.IsEnabled = hasModel;
                }

                if (_scratchWidthInputTextBox != null)
                {
                    _scratchWidthInputTextBox.IsEnabled = hasModel;
                }

                if (_scratchDepthInputTextBox != null)
                {
                    _scratchDepthInputTextBox.IsEnabled = hasModel;
                }

                if (_scratchResistanceInputTextBox != null)
                {
                    _scratchResistanceInputTextBox.IsEnabled = hasModel;
                }

                if (_scratchDepthRampInputTextBox != null)
                {
                    _scratchDepthRampInputTextBox.IsEnabled = hasModel;
                }

                if (_scratchExposeColorRInputTextBox != null)
                {
                    _scratchExposeColorRInputTextBox.IsEnabled = hasModel;
                }

                if (_scratchExposeColorGInputTextBox != null)
                {
                    _scratchExposeColorGInputTextBox.IsEnabled = hasModel;
                }

                if (_scratchExposeColorBInputTextBox != null)
                {
                    _scratchExposeColorBInputTextBox.IsEnabled = hasModel;
                }

                if (_scratchExposeMetallicInputTextBox != null)
                {
                    _scratchExposeMetallicInputTextBox.IsEnabled = hasModel;
                }

                if (_scratchExposeRoughnessInputTextBox != null)
                {
                    _scratchExposeRoughnessInputTextBox.IsEnabled = hasModel;
                }
                if (_clearPaintMaskButton != null)
                {
                    _clearPaintMaskButton.IsEnabled = hasModel;
                }

                if (material != null)
                {
                    ApplyMaterialRegionValuesToSliders(material);
                    _materialPearlescenceSlider.Value = material.Pearlescence;
                    _materialRustSlider.Value = material.RustAmount;
                    _materialWearSlider.Value = material.WearAmount;
                    _materialGunkSlider.Value = material.GunkAmount;
                    _materialBrushStrengthSlider.Value = material.RadialBrushStrength;
                    _materialBrushDensitySlider.Value = material.RadialBrushDensity;
                    _materialCharacterSlider.Value = material.SurfaceCharacter;
                }

                _spiralNormalInfluenceCheckBox.IsChecked = project.SpiralNormalInfluenceEnabled;
                _basisDebugModeCombo.SelectedItem = project.BasisDebug;
                _microLodFadeStartSlider.Value = project.SpiralNormalLodFadeStart;
                _microLodFadeEndSlider.Value = project.SpiralNormalLodFadeEnd;
                _microRoughnessLodBoostSlider.Value = project.SpiralRoughnessLodBoost;

                _envIntensitySlider.Value = project.EnvironmentIntensity;
                _envRoughnessMixSlider.Value = project.EnvironmentRoughnessMix;
                _envTopRSlider.Value = project.EnvironmentTopColor.X;
                _envTopGSlider.Value = project.EnvironmentTopColor.Y;
                _envTopBSlider.Value = project.EnvironmentTopColor.Z;
                _envBottomRSlider.Value = project.EnvironmentBottomColor.X;
                _envBottomGSlider.Value = project.EnvironmentBottomColor.Y;
                _envBottomBSlider.Value = project.EnvironmentBottomColor.Z;
                _shadowEnabledCheckBox.IsChecked = project.ShadowsEnabled;
                _shadowSourceModeCombo.SelectedItem = project.ShadowMode;
                _shadowStrengthSlider.Value = project.ShadowStrength;
                _shadowSoftnessSlider.Value = project.ShadowSoftness;
                _shadowDistanceSlider.Value = project.ShadowDistance;
                _shadowScaleSlider.Value = project.ShadowScale;
                _shadowQualitySlider.Value = project.ShadowQuality;
                _shadowGraySlider.Value = project.ShadowGray;
                _shadowDiffuseInfluenceSlider.Value = project.ShadowDiffuseInfluence;
                _brushPaintEnabledCheckBox.IsChecked = project.BrushPaintingEnabled;
                _brushPaintChannelCombo.SelectedItem = project.BrushChannel;
                _brushTypeCombo.SelectedItem = project.BrushType;
                _brushPaintColorPicker.Color = ToAvaloniaColor(project.PaintColor);
                _scratchAbrasionTypeCombo.SelectedItem = project.ScratchAbrasionType;
                _brushSizeSlider.Value = project.BrushSizePx;
                _brushOpacitySlider.Value = project.BrushOpacity;
                _brushDarknessSlider.Value = project.BrushDarkness;
                _brushSpreadSlider.Value = project.BrushSpread;
                _paintCoatMetallicSlider.Value = project.PaintCoatMetallic;
                _paintCoatRoughnessSlider.Value = project.PaintCoatRoughness;
                _clearCoatAmountSlider.Value = project.ClearCoatAmount;
                _clearCoatRoughnessSlider.Value = project.ClearCoatRoughness;
                _anisotropyAngleSlider.Value = project.AnisotropyAngleDegrees;
                _scratchWidthSlider.Value = project.ScratchWidthPx;
                _scratchDepthSlider.Value = project.ScratchDepth;
                _scratchResistanceSlider.Value = project.ScratchDragResistance;
                _scratchDepthRampSlider.Value = project.ScratchDepthRamp;
                _scratchExposeColorRSlider.Value = project.ScratchExposeColor.X;
                _scratchExposeColorGSlider.Value = project.ScratchExposeColor.Y;
                _scratchExposeColorBSlider.Value = project.ScratchExposeColor.Z;
                _scratchExposeMetallicSlider.Value = project.ScratchExposeMetallic;
                _scratchExposeRoughnessSlider.Value = project.ScratchExposeRoughness;
                UpdateBrushContextUi();
                _metalViewport?.RefreshPaintHud();

                var selectedLight = project.SelectedLight;
                bool hasLight = selectedLight != null;

                _lightTypeCombo.IsEnabled = hasLight;
                _lightXSlider.IsEnabled = hasLight;
                _lightYSlider.IsEnabled = hasLight;
                _lightZSlider.IsEnabled = hasLight;
                _directionSlider.IsEnabled = hasLight;
                _intensitySlider.IsEnabled = hasLight;
                _falloffSlider.IsEnabled = hasLight;
                _lightRSlider.IsEnabled = hasLight;
                _lightGSlider.IsEnabled = hasLight;
                _lightBSlider.IsEnabled = hasLight;
                _diffuseBoostSlider.IsEnabled = hasLight;
                _specularBoostSlider.IsEnabled = hasLight;
                _specularPowerSlider.IsEnabled = hasLight;

                if (selectedLight != null)
                {
                    _lightTypeCombo.SelectedItem = selectedLight.Type;
                    _lightXSlider.Value = selectedLight.X;
                    _lightYSlider.Value = selectedLight.Y;
                    _lightZSlider.Value = selectedLight.Z;
                    _directionSlider.Value = RadiansToDegrees(selectedLight.DirectionRadians);
                    _intensitySlider.Value = selectedLight.Intensity;
                    _falloffSlider.Value = selectedLight.Falloff;
                    _lightRSlider.Value = selectedLight.Color.Red;
                    _lightGSlider.Value = selectedLight.Color.Green;
                    _lightBSlider.Value = selectedLight.Color.Blue;
                    _diffuseBoostSlider.Value = selectedLight.DiffuseBoost;
                    _specularBoostSlider.Value = selectedLight.SpecularBoost;
                    _specularPowerSlider.Value = selectedLight.SpecularPower;
                }

                ApplyInspectorTabPolicy(tabPolicy, preservedTab);
                RestoreInspectorPresentationStateForCurrentTab();
                RestoreInspectorFocusStateForCurrentTab(preservedFocus);
                UpdateReadouts();
            }
            finally
            {
                _updatingUi = false;
            }
        }

        private void ApplyInspectorTabPolicy(InspectorRefreshTabPolicy tabPolicy, TabItem? preservedTab)
        {
            if (_inspectorTabControl == null)
            {
                return;
            }

            if (tabPolicy == InspectorRefreshTabPolicy.FollowSceneSelection || preservedTab == null)
            {
                SelectInspectorTabForSceneNode(_project.SelectedNode);
                return;
            }

            if (!ReferenceEquals(_inspectorTabControl.SelectedItem, preservedTab))
            {
                _inspectorTabControl.SelectedItem = preservedTab;
            }
        }

    }
}
