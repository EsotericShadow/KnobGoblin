using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KnobForge.App.Controls;
using KnobForge.Core;
using KnobForge.Core.Scene;
using System;
using System.Linq;
using System.Reflection;

#pragma warning disable CS8602

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private bool HasRequiredControls()
        {
            if (_metalViewport == null || _viewportOverlay == null || _sceneListBox == null || _lightingModeCombo == null || _lightListBox == null ||
                _addLightButton == null || _removeLightButton == null || _resetViewButton == null ||
                _rotationSlider == null || _lightTypeCombo == null || _lightXSlider == null ||
                _lightYSlider == null || _lightZSlider == null || _directionSlider == null ||
                _intensitySlider == null || _falloffSlider == null || _lightRSlider == null ||
                _lightGSlider == null || _lightBSlider == null || _diffuseBoostSlider == null ||
                _specularBoostSlider == null || _specularPowerSlider == null ||
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
                _collarRotateSlider == null || _collarOffsetXSlider == null || _collarOffsetYSlider == null || _collarElevationSlider == null || _collarInflateSlider == null ||
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
                _scratchExposeMetallicSlider == null || _scratchExposeRoughnessSlider == null ||
                _clearPaintMaskButton == null ||
                _renderButton == null ||
                _rotationValueText == null || _lightXValueText == null || _lightYValueText == null ||
                _lightZValueText == null || _directionValueText == null ||
                _intensityValueText == null || _falloffValueText == null ||
                _lightRValueText == null || _lightGValueText == null || _lightBValueText == null ||
                _diffuseBoostValueText == null || _specularBoostValueText == null ||
                _specularPowerValueText == null || _centerLightButton == null ||
                _modelRadiusValueText == null || _modelHeightValueText == null ||
                _modelTopScaleValueText == null || _modelBevelValueText == null || _bevelCurveValueText == null ||
                _crownProfileValueText == null || _bodyTaperValueText == null || _bodyBulgeValueText == null ||
                _modelSegmentsValueText == null ||
                _spiralRidgeHeightValueText == null || _spiralRidgeWidthValueText == null || _spiralTurnsValueText == null ||
                _gripStartValueText == null || _gripHeightValueText == null || _gripDensityValueText == null ||
                _gripPitchValueText == null || _gripDepthValueText == null || _gripWidthValueText == null ||
                _gripSharpnessValueText == null ||
                _collarScaleValueText == null || _collarBodyLengthValueText == null || _collarBodyThicknessValueText == null ||
                _collarHeadLengthValueText == null || _collarHeadThicknessValueText == null ||
                _collarRotateValueText == null || _collarOffsetXValueText == null || _collarOffsetYValueText == null || _collarElevationValueText == null || _collarInflateValueText == null ||
                _collarMaterialBaseRValueText == null || _collarMaterialBaseGValueText == null || _collarMaterialBaseBValueText == null ||
                _collarMaterialMetallicValueText == null || _collarMaterialRoughnessValueText == null || _collarMaterialPearlescenceValueText == null ||
                _collarMaterialRustValueText == null || _collarMaterialWearValueText == null || _collarMaterialGunkValueText == null ||
                _indicatorWidthValueText == null || _indicatorLengthValueText == null || _indicatorPositionValueText == null || _indicatorThicknessValueText == null ||
                _indicatorRoundnessValueText == null || _indicatorColorBlendValueText == null ||
                _indicatorColorRValueText == null || _indicatorColorGValueText == null || _indicatorColorBValueText == null ||
                _materialBaseRValueText == null || _materialBaseGValueText == null || _materialBaseBValueText == null ||
                _materialMetallicValueText == null || _materialRoughnessValueText == null || _materialPearlescenceValueText == null ||
                _materialRustValueText == null || _materialWearValueText == null || _materialGunkValueText == null ||
                _materialBrushStrengthValueText == null || _materialBrushDensityValueText == null || _materialCharacterValueText == null ||
                _microLodFadeStartValueText == null || _microLodFadeEndValueText == null || _microRoughnessLodBoostValueText == null ||
                _envIntensityValueText == null || _envRoughnessMixValueText == null ||
                _envTopRValueText == null || _envTopGValueText == null || _envTopBValueText == null ||
                _envBottomRValueText == null || _envBottomGValueText == null || _envBottomBValueText == null ||
                _shadowStrengthValueText == null || _shadowSoftnessValueText == null || _shadowDistanceValueText == null ||
                _shadowScaleValueText == null || _shadowQualityValueText == null || _shadowGrayValueText == null ||
                _shadowDiffuseInfluenceValueText == null ||
                _brushSizeValueText == null || _brushOpacityValueText == null || _brushDarknessValueText == null || _brushSpreadValueText == null ||
                _paintCoatMetallicValueText == null || _paintCoatRoughnessValueText == null ||
                _clearCoatAmountValueText == null || _clearCoatRoughnessValueText == null || _anisotropyAngleValueText == null ||
                _scratchWidthValueText == null || _scratchDepthValueText == null || _scratchResistanceValueText == null || _scratchDepthRampValueText == null ||
                _scratchExposeColorRValueText == null || _scratchExposeColorGValueText == null || _scratchExposeColorBValueText == null ||
                _scratchExposeMetallicValueText == null || _scratchExposeRoughnessValueText == null)
            {
                return false;
            }
            return true;
        }

        private void InitializeViewportAndSceneBindings()
        {
            _metalViewport.Project = _project;
            _metalViewport.InvalidateGpu();

            _viewportOverlay.Focusable = true;
            _viewportOverlay.IsHitTestVisible = true;
            _viewportOverlay.AddHandler(InputElement.PointerPressedEvent, ViewportOverlay_PointerPressed, RoutingStrategies.Tunnel);
            _viewportOverlay.AddHandler(InputElement.PointerMovedEvent, ViewportOverlay_PointerMoved, RoutingStrategies.Tunnel);
            _viewportOverlay.AddHandler(InputElement.PointerReleasedEvent, ViewportOverlay_PointerReleased, RoutingStrategies.Tunnel);
            _viewportOverlay.AddHandler(InputElement.PointerWheelChangedEvent, ViewportOverlay_PointerWheelChanged, RoutingStrategies.Tunnel);
            _viewportOverlay.AddHandler(InputElement.KeyDownEvent, ViewportOverlay_KeyDown, RoutingStrategies.Tunnel);
            _viewportOverlay.AddHandler(InputElement.KeyUpEvent, ViewportOverlay_KeyUp, RoutingStrategies.Tunnel);
            _sceneListBox.ItemsSource = _sceneNodes;
            _lightingModeCombo.ItemsSource = Enum.GetValues<LightingMode>().Cast<LightingMode>().ToList();
            _lightTypeCombo.ItemsSource = Enum.GetValues<LightType>().Cast<LightType>().ToList();
            RebuildReferenceStyleOptions();
            _bodyStyleCombo.ItemsSource = Enum.GetValues<BodyStyle>().Cast<BodyStyle>().ToList();
            _gripStyleCombo.ItemsSource = Enum.GetValues<GripStyle>().Cast<GripStyle>().ToList();
            _gripTypeCombo.ItemsSource = Enum.GetValues<GripType>().Cast<GripType>().ToList();
            _collarPresetCombo.ItemsSource = Enum.GetValues<CollarPreset>().Cast<CollarPreset>().ToList();
            _indicatorShapeCombo.ItemsSource = Enum.GetValues<IndicatorShape>().Cast<IndicatorShape>().ToList();
            _indicatorReliefCombo.ItemsSource = Enum.GetValues<IndicatorRelief>().Cast<IndicatorRelief>().ToList();
            _indicatorProfileCombo.ItemsSource = Enum.GetValues<IndicatorProfile>().Cast<IndicatorProfile>().ToList();
            _materialRegionCombo.ItemsSource = Enum.GetValues<MaterialRegionTarget>().Cast<MaterialRegionTarget>().ToList();
            _materialRegionCombo.SelectedItem = MaterialRegionTarget.WholeKnob;
            _basisDebugModeCombo.ItemsSource = Enum.GetValues<BasisDebugMode>().Cast<BasisDebugMode>().ToList();
            _shadowSourceModeCombo.ItemsSource = Enum.GetValues<ShadowLightMode>().Cast<ShadowLightMode>().ToList();
            _brushPaintChannelCombo.ItemsSource = Enum.GetValues<PaintChannel>().Cast<PaintChannel>().ToList();
            _brushTypeCombo.ItemsSource = Enum.GetValues<PaintBrushType>().Cast<PaintBrushType>().ToList();
            _scratchAbrasionTypeCombo.ItemsSource = Enum.GetValues<ScratchAbrasionType>().Cast<ScratchAbrasionType>().ToList();

            _sceneListBox.SelectionChanged += (_, _) =>
            {
                if (_sceneListBox == null)
                {
                    return;
                }

                var selectedNode = _sceneListBox.SelectedItem as SceneNode;
                if (IsUiRefreshing)
                {
                    return;
                }

                if (selectedNode is SceneNode node)
                {
                    if (_project.SelectedNode?.Id == node.Id)
                    {
                        SyncInspectorForSelectedSceneNode(node);
                        return;
                    }

                    _project.SetSelectedNode(node);
                    SyncInspectorForSelectedSceneNode(node);
                }
            };
            _lightListBox.SelectionChanged += OnLightListSelectionChanged;
        }

        private void WireButtonHandlers()
        {
            _addLightButton.Click += (_, _) => AddLight();
            _removeLightButton.Click += (_, _) => RemoveSelectedLight();
            _centerLightButton.Click += (_, _) => CenterLight();
            _saveReferenceProfileButton.Click += OnSaveReferenceProfileClicked;
            if (_openProjectButton != null)
            {
                _openProjectButton.Click += OnOpenProjectButtonClicked;
            }

            if (_saveProjectButton != null)
            {
                _saveProjectButton.Click += OnSaveProjectButtonClicked;
            }

            if (_saveProjectAsButton != null)
            {
                _saveProjectAsButton.Click += OnSaveProjectAsButtonClicked;
            }

            if (_overwriteReferenceProfileButton != null)
            {
                _overwriteReferenceProfileButton.Click += OnOverwriteReferenceProfileClicked;
            }
            if (_renameReferenceProfileButton != null)
            {
                _renameReferenceProfileButton.Click += OnRenameReferenceProfileClicked;
            }
            if (_duplicateReferenceProfileButton != null)
            {
                _duplicateReferenceProfileButton.Click += OnDuplicateReferenceProfileClicked;
            }
            if (_deleteReferenceProfileButton != null)
            {
                _deleteReferenceProfileButton.Click += OnDeleteReferenceProfileClicked;
            }
            _resetViewButton.Click += (_, _) => _metalViewport?.ResetCamera();
            _clearPaintMaskButton.Click += (_, _) => OnClearPaintMask();
            _renderButton.Click += OnRenderButtonClick;
            if (_undoButton != null)
            {
                _undoButton.Click += (_, _) => ExecuteUndo();
            }

            if (_redoButton != null)
            {
                _redoButton.Click += (_, _) => ExecuteRedo();
            }
        }

        private void WireControlPropertyHandlers()
        {
            _lightingModeCombo.PropertyChanged += OnLightingModeChanged;
            _lightTypeCombo.PropertyChanged += OnLightTypeChanged;
            _rotationSlider.PropertyChanged += OnRotationChanged;
            _lightXSlider.PropertyChanged += OnLightXChanged;
            _lightYSlider.PropertyChanged += OnLightYChanged;
            _lightZSlider.PropertyChanged += OnLightZChanged;
            _directionSlider.PropertyChanged += OnDirectionChanged;
            _intensitySlider.PropertyChanged += OnIntensityChanged;
            _falloffSlider.PropertyChanged += OnFalloffChanged;
            _lightRSlider.PropertyChanged += OnColorChanged;
            _lightGSlider.PropertyChanged += OnColorChanged;
            _lightBSlider.PropertyChanged += OnColorChanged;
            _diffuseBoostSlider.PropertyChanged += OnDiffuseBoostChanged;
            _specularBoostSlider.PropertyChanged += OnSpecularBoostChanged;
            _specularPowerSlider.PropertyChanged += OnSpecularPowerChanged;
            _modelRadiusSlider.PropertyChanged += OnModelRadiusChanged;
            _modelHeightSlider.PropertyChanged += OnModelHeightChanged;
            _modelTopScaleSlider.PropertyChanged += OnModelTopScaleChanged;
            _modelBevelSlider.PropertyChanged += OnModelBevelChanged;
            _referenceStyleCombo.PropertyChanged += OnReferenceStyleChanged;
            _bodyStyleCombo.PropertyChanged += OnBodyStyleChanged;
            _bevelCurveSlider.PropertyChanged += OnBodyDesignChanged;
            _crownProfileSlider.PropertyChanged += OnBodyDesignChanged;
            _bodyTaperSlider.PropertyChanged += OnBodyDesignChanged;
            _bodyBulgeSlider.PropertyChanged += OnBodyDesignChanged;
            _modelSegmentsSlider.PropertyChanged += OnModelSegmentsChanged;
            _spiralRidgeHeightSlider.PropertyChanged += OnSpiralGeometryChanged;
            _spiralRidgeWidthSlider.PropertyChanged += OnSpiralGeometryChanged;
            _spiralTurnsSlider.PropertyChanged += OnSpiralGeometryChanged;
            _gripStyleCombo.PropertyChanged += OnGripStyleChanged;
            _gripTypeCombo.PropertyChanged += OnGripSettingsChanged;
            _gripStartSlider.PropertyChanged += OnGripSettingsChanged;
            _gripHeightSlider.PropertyChanged += OnGripSettingsChanged;
            _gripDensitySlider.PropertyChanged += OnGripSettingsChanged;
            _gripPitchSlider.PropertyChanged += OnGripSettingsChanged;
            _gripDepthSlider.PropertyChanged += OnGripSettingsChanged;
            _gripWidthSlider.PropertyChanged += OnGripSettingsChanged;
            _gripSharpnessSlider.PropertyChanged += OnGripSettingsChanged;
            _collarEnabledCheckBox.PropertyChanged += OnCollarSettingsChanged;
            _collarPresetCombo.PropertyChanged += OnCollarSettingsChanged;
            _collarMeshPathTextBox.PropertyChanged += OnCollarSettingsChanged;
            _collarScaleSlider.PropertyChanged += OnCollarSettingsChanged;
            _collarBodyLengthSlider.PropertyChanged += OnCollarSettingsChanged;
            _collarBodyThicknessSlider.PropertyChanged += OnCollarSettingsChanged;
            _collarHeadLengthSlider.PropertyChanged += OnCollarSettingsChanged;
            _collarHeadThicknessSlider.PropertyChanged += OnCollarSettingsChanged;
            _collarRotateSlider.PropertyChanged += OnCollarSettingsChanged;
            _collarOffsetXSlider.PropertyChanged += OnCollarSettingsChanged;
            _collarOffsetYSlider.PropertyChanged += OnCollarSettingsChanged;
            _collarElevationSlider.PropertyChanged += OnCollarSettingsChanged;
            _collarInflateSlider.PropertyChanged += OnCollarSettingsChanged;
            _collarMaterialBaseRSlider.PropertyChanged += OnCollarMaterialChanged;
            _collarMaterialBaseGSlider.PropertyChanged += OnCollarMaterialChanged;
            _collarMaterialBaseBSlider.PropertyChanged += OnCollarMaterialChanged;
            _collarMaterialMetallicSlider.PropertyChanged += OnCollarMaterialChanged;
            _collarMaterialRoughnessSlider.PropertyChanged += OnCollarMaterialChanged;
            _collarMaterialPearlescenceSlider.PropertyChanged += OnCollarMaterialChanged;
            _collarMaterialRustSlider.PropertyChanged += OnCollarMaterialChanged;
            _collarMaterialWearSlider.PropertyChanged += OnCollarMaterialChanged;
            _collarMaterialGunkSlider.PropertyChanged += OnCollarMaterialChanged;
            _indicatorEnabledCheckBox.PropertyChanged += OnIndicatorSettingsChanged;
            if (_indicatorCadWallsCheckBox != null)
            {
                _indicatorCadWallsCheckBox.PropertyChanged += OnIndicatorSettingsChanged;
            }
            _indicatorShapeCombo.PropertyChanged += OnIndicatorSettingsChanged;
            _indicatorReliefCombo.PropertyChanged += OnIndicatorSettingsChanged;
            _indicatorProfileCombo.PropertyChanged += OnIndicatorSettingsChanged;
            _indicatorWidthSlider.PropertyChanged += OnIndicatorSettingsChanged;
            _indicatorLengthSlider.PropertyChanged += OnIndicatorSettingsChanged;
            _indicatorPositionSlider.PropertyChanged += OnIndicatorSettingsChanged;
            _indicatorThicknessSlider.PropertyChanged += OnIndicatorSettingsChanged;
            _indicatorRoundnessSlider.PropertyChanged += OnIndicatorSettingsChanged;
            _indicatorColorBlendSlider.PropertyChanged += OnIndicatorSettingsChanged;
            _indicatorColorRSlider.PropertyChanged += OnIndicatorSettingsChanged;
            _indicatorColorGSlider.PropertyChanged += OnIndicatorSettingsChanged;
            _indicatorColorBSlider.PropertyChanged += OnIndicatorSettingsChanged;
            _materialBaseRSlider.PropertyChanged += OnMaterialBaseColorChanged;
            _materialBaseGSlider.PropertyChanged += OnMaterialBaseColorChanged;
            _materialBaseBSlider.PropertyChanged += OnMaterialBaseColorChanged;
            _materialRegionCombo.PropertyChanged += OnMaterialRegionChanged;
            _materialMetallicSlider.PropertyChanged += OnMaterialMetallicChanged;
            _materialRoughnessSlider.PropertyChanged += OnMaterialRoughnessChanged;
            _materialPearlescenceSlider.PropertyChanged += OnMaterialPearlescenceChanged;
            _materialRustSlider.PropertyChanged += OnMaterialAgingChanged;
            _materialWearSlider.PropertyChanged += OnMaterialAgingChanged;
            _materialGunkSlider.PropertyChanged += OnMaterialAgingChanged;
            _materialBrushStrengthSlider.PropertyChanged += OnMaterialSurfaceCharacterChanged;
            _materialBrushDensitySlider.PropertyChanged += OnMaterialSurfaceCharacterChanged;
            _materialCharacterSlider.PropertyChanged += OnMaterialSurfaceCharacterChanged;
            _spiralNormalInfluenceCheckBox.PropertyChanged += OnMicroDetailSettingsChanged;
            _basisDebugModeCombo.PropertyChanged += OnMicroDetailSettingsChanged;
            _microLodFadeStartSlider.PropertyChanged += OnMicroDetailSettingsChanged;
            _microLodFadeEndSlider.PropertyChanged += OnMicroDetailSettingsChanged;
            _microRoughnessLodBoostSlider.PropertyChanged += OnMicroDetailSettingsChanged;
            _envIntensitySlider.PropertyChanged += OnEnvironmentChanged;
            _envRoughnessMixSlider.PropertyChanged += OnEnvironmentChanged;
            _envTopRSlider.PropertyChanged += OnEnvironmentChanged;
            _envTopGSlider.PropertyChanged += OnEnvironmentChanged;
            _envTopBSlider.PropertyChanged += OnEnvironmentChanged;
            _envBottomRSlider.PropertyChanged += OnEnvironmentChanged;
            _envBottomGSlider.PropertyChanged += OnEnvironmentChanged;
            _envBottomBSlider.PropertyChanged += OnEnvironmentChanged;
            _shadowEnabledCheckBox.PropertyChanged += OnShadowSettingsChanged;
            _shadowSourceModeCombo.PropertyChanged += OnShadowSettingsChanged;
            _shadowStrengthSlider.PropertyChanged += OnShadowSettingsChanged;
            _shadowSoftnessSlider.PropertyChanged += OnShadowSettingsChanged;
            _shadowDistanceSlider.PropertyChanged += OnShadowSettingsChanged;
            _shadowScaleSlider.PropertyChanged += OnShadowSettingsChanged;
            _shadowQualitySlider.PropertyChanged += OnShadowSettingsChanged;
            _shadowGraySlider.PropertyChanged += OnShadowSettingsChanged;
            _shadowDiffuseInfluenceSlider.PropertyChanged += OnShadowSettingsChanged;
            _brushPaintEnabledCheckBox.PropertyChanged += OnPaintBrushSettingsChanged;
            _brushPaintChannelCombo.PropertyChanged += OnPaintBrushSettingsChanged;
            _brushTypeCombo.PropertyChanged += OnPaintBrushSettingsChanged;
            _brushPaintColorPicker.PropertyChanged += OnPaintBrushSettingsChanged;
            _scratchAbrasionTypeCombo.PropertyChanged += OnPaintBrushSettingsChanged;
            _brushSizeSlider.PropertyChanged += OnPaintBrushSettingsChanged;
            _brushOpacitySlider.PropertyChanged += OnPaintBrushSettingsChanged;
            _brushDarknessSlider.PropertyChanged += OnPaintBrushSettingsChanged;
            _brushSpreadSlider.PropertyChanged += OnPaintBrushSettingsChanged;
            _paintCoatMetallicSlider.PropertyChanged += OnPaintBrushSettingsChanged;
            _paintCoatRoughnessSlider.PropertyChanged += OnPaintBrushSettingsChanged;
            _clearCoatAmountSlider.PropertyChanged += OnPaintBrushSettingsChanged;
            _clearCoatRoughnessSlider.PropertyChanged += OnPaintBrushSettingsChanged;
            _anisotropyAngleSlider.PropertyChanged += OnPaintBrushSettingsChanged;
            _scratchWidthSlider.PropertyChanged += OnPaintBrushSettingsChanged;
            _scratchDepthSlider.PropertyChanged += OnPaintBrushSettingsChanged;
            _scratchResistanceSlider.PropertyChanged += OnPaintBrushSettingsChanged;
            _scratchDepthRampSlider.PropertyChanged += OnPaintBrushSettingsChanged;
            _scratchExposeColorRSlider.PropertyChanged += OnPaintBrushSettingsChanged;
            _scratchExposeColorGSlider.PropertyChanged += OnPaintBrushSettingsChanged;
            _scratchExposeColorBSlider.PropertyChanged += OnPaintBrushSettingsChanged;
            _scratchExposeMetallicSlider.PropertyChanged += OnPaintBrushSettingsChanged;
            _scratchExposeRoughnessSlider.PropertyChanged += OnPaintBrushSettingsChanged;
        }

        private void WireOpenedHandlers()
        {
            Opened += (_, __) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    RefreshSceneTree();
                    RefreshInspectorFromProject(InspectorRefreshTabPolicy.FollowSceneSelection);
                }, DispatcherPriority.Loaded);
            };
        }

        private static TextBlock? FindFirstTextBlock(Visual? root)
        {
            if (root is TextBlock tb)
            {
                return tb;
            }

            if (root == null)
            {
                return null;
            }

            foreach (var child in root.GetVisualChildren())
            {
                var found = FindFirstTextBlock(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static ContentPresenter? FindFirstContentPresenter(Visual? root)
        {
            if (root is ContentPresenter presenter)
            {
                return presenter;
            }

            if (root == null)
            {
                return null;
            }

            foreach (var child in root.GetVisualChildren())
            {
                var found = FindFirstContentPresenter(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void InvalidateSceneList(string phase)
        {
            _sceneListBox?.InvalidateVisual();
            _sceneListBox?.InvalidateMeasure();
            _sceneListBox?.InvalidateArrange();
            (_sceneListBox?.ContainerFromIndex(0) as Control)?.InvalidateVisual();
            _ = phase;
        }

        private void DumpSceneListVisualState(string prefix)
        {
            var sceneList = _sceneListBox;
            var firstContainer = sceneList?.ContainerFromIndex(0) as ListBoxItem;
            var firstTextBlock = FindFirstTextBlock(firstContainer);
            var presenter = FindFirstContentPresenter(firstContainer);

            string sceneThemeVariant = GetPropertyString(sceneList, "ActualThemeVariant");
            string sceneBackground = GetPropertyString(sceneList, "Background");
            string sceneForeground = GetPropertyString(sceneList, "Foreground");
            string itemBackground = GetPropertyString(firstContainer, "Background");
            string itemForeground = GetPropertyString(firstContainer, "Foreground");
            string itemPseudoClasses = GetPropertyString(firstContainer, "PseudoClasses");
            string tbOpacityMask = GetPropertyString(firstTextBlock, "OpacityMask");
            _ = prefix;
            _ = sceneThemeVariant;
            _ = sceneBackground;
            _ = sceneForeground;
            _ = itemBackground;
            _ = itemForeground;
            _ = itemPseudoClasses;
            _ = tbOpacityMask;
            _ = sceneList;
            _ = firstContainer;
            _ = firstTextBlock;
            _ = presenter;
        }

        private static string GetPropertyString(object? target, string propertyName)
        {
            if (target == null)
            {
                return "<null>";
            }

            var property = target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
            {
                return "<unavailable>";
            }

            var value = property.GetValue(target);
            return value?.ToString() ?? "<null>";
        }

        private void DetachLightListHandler()
        {
            _lightListBox!.SelectionChanged -= OnLightListSelectionChanged;
        }

        private void AttachLightListHandler()
        {
            _lightListBox!.SelectionChanged += OnLightListSelectionChanged;
        }

        private void OnLightListSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_updatingUi || _lightListBox == null)
            {
                return;
            }

            if (_project.SetSelectedLightIndex(_lightListBox.SelectedIndex))
            {
                NotifyProjectStateChanged();
            }
        }

        private void ViewportOverlay_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_metalViewport == null || _viewportOverlay == null)
            {
                return;
            }

            _viewportOverlay.Focus();
            _metalViewport.HandlePointerPressedFromOverlay(e, _viewportOverlay);
            e.Handled = true;
        }

        private void ViewportOverlay_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (_metalViewport == null || _viewportOverlay == null)
            {
                return;
            }

            _metalViewport.HandlePointerMovedFromOverlay(e, _viewportOverlay);
            e.Handled = true;
        }

        private void ViewportOverlay_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_metalViewport == null || _viewportOverlay == null)
            {
                return;
            }

            _metalViewport.HandlePointerReleasedFromOverlay(e, _viewportOverlay);
            e.Handled = true;
        }

        private void ViewportOverlay_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (_metalViewport == null || _viewportOverlay == null)
            {
                return;
            }

            _metalViewport.HandlePointerWheelFromOverlay(e, _viewportOverlay);
            e.Handled = true;
        }

        private void ViewportOverlay_KeyDown(object? sender, KeyEventArgs e)
        {
            _metalViewport?.HandleKeyDownFromOverlay(e);
        }

        private void ViewportOverlay_KeyUp(object? sender, KeyEventArgs e)
        {
            _metalViewport?.HandleKeyUpFromOverlay(e);
        }
    }
}

#pragma warning restore CS8602
