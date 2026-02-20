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
            private readonly KnobProject _project;
            private readonly MetalViewport? _metalViewport;
            private readonly Control? _viewportOverlay;
            private readonly Border? _paintHudBorder;
            private readonly TextBlock? _paintHudTitleText;
            private readonly TextBlock? _paintHudLine1Text;
            private readonly TextBlock? _paintHudLine2Text;
            private readonly TextBlock? _paintHudLine3Text;
            private readonly TextBlock? _paintHudLine4Text;
            private readonly ListBox? _sceneListBox;
            private readonly TabControl? _inspectorTabControl;
            private readonly TabItem? _lightingTabItem;
            private readonly TabItem? _modelTabItem;
            private readonly TabItem? _brushTabItem;
            private readonly TabItem? _cameraTabItem;
            private readonly TabItem? _environmentTabItem;
            private readonly TabItem? _shadowsTabItem;
            private readonly Button? _undoButton;
            private readonly Button? _redoButton;
            private readonly ScrollViewer? _inspectorScrollViewer;
            private readonly TextBox? _inspectorSearchTextBox;
            private readonly ListBox? _inspectorSearchResultsListBox;
            private readonly ListBox? _recentTweaksListBox;
            private readonly Button? _favoriteReferenceProfilesButton;
            private readonly Button? _favoriteRoughnessButton;
            private readonly Button? _favoriteScratchDepthButton;
            private readonly Button? _favoriteCollarPresetButton;
            private readonly Button? _favoriteShadowSoftnessButton;
            private readonly Button? _favoriteEnvIntensityButton;
            private readonly ComboBox? _lightingModeCombo;
            private readonly ListBox? _lightListBox;
        private readonly Button? _addLightButton;
        private readonly Button? _removeLightButton;
        private readonly Button? _resetViewButton;
        private readonly Slider? _rotationSlider;
        private readonly ComboBox? _lightTypeCombo;
        private readonly Slider? _lightXSlider;
        private readonly Slider? _lightYSlider;
        private readonly Slider? _lightZSlider;
        private readonly Slider? _directionSlider;
        private readonly Slider? _intensitySlider;
        private readonly Slider? _falloffSlider;
        private readonly Slider? _lightRSlider;
        private readonly Slider? _lightGSlider;
        private readonly Slider? _lightBSlider;
        private readonly Slider? _diffuseBoostSlider;
        private readonly Slider? _specularBoostSlider;
        private readonly Slider? _specularPowerSlider;
        private readonly Slider? _modelRadiusSlider;
        private readonly Slider? _modelHeightSlider;
        private readonly Slider? _modelTopScaleSlider;
        private readonly Slider? _modelBevelSlider;
        private readonly ComboBox? _referenceStyleCombo;
        private readonly TextBox? _referenceStyleSaveNameTextBox;
        private readonly Button? _saveReferenceProfileButton;
        private readonly Button? _overwriteReferenceProfileButton;
        private readonly Button? _renameReferenceProfileButton;
        private readonly Button? _duplicateReferenceProfileButton;
        private readonly Button? _deleteReferenceProfileButton;
        private readonly TextBlock? _referenceProfileStatusText;
        private readonly ComboBox? _bodyStyleCombo;
        private readonly Slider? _bevelCurveSlider;
        private readonly Slider? _crownProfileSlider;
        private readonly Slider? _bodyTaperSlider;
        private readonly Slider? _bodyBulgeSlider;
        private readonly Slider? _modelSegmentsSlider;
        private readonly Slider? _spiralRidgeHeightSlider;
        private readonly Slider? _spiralRidgeWidthSlider;
        private readonly Slider? _spiralTurnsSlider;
        private readonly ComboBox? _gripStyleCombo;
        private readonly ComboBox? _gripTypeCombo;
        private readonly Slider? _gripStartSlider;
        private readonly Slider? _gripHeightSlider;
        private readonly Slider? _gripDensitySlider;
        private readonly Slider? _gripPitchSlider;
        private readonly Slider? _gripDepthSlider;
        private readonly Slider? _gripWidthSlider;
        private readonly Slider? _gripSharpnessSlider;
        private readonly CheckBox? _collarEnabledCheckBox;
        private readonly ComboBox? _collarPresetCombo;
        private readonly TextBox? _collarMeshPathTextBox;
        private readonly TextBlock? _collarResolvedMeshPathText;
        private readonly TextBlock? _collarMeshPathStatusText;
        private readonly Slider? _collarScaleSlider;
        private readonly Slider? _collarBodyLengthSlider;
        private readonly Slider? _collarBodyThicknessSlider;
        private readonly Slider? _collarHeadLengthSlider;
        private readonly Slider? _collarHeadThicknessSlider;
        private readonly Slider? _collarRotateSlider;
        private readonly Slider? _collarOffsetXSlider;
        private readonly Slider? _collarOffsetYSlider;
        private readonly Slider? _collarElevationSlider;
        private readonly Slider? _collarInflateSlider;
        private readonly TextBox? _collarScaleInputTextBox;
        private readonly TextBox? _collarBodyLengthInputTextBox;
        private readonly TextBox? _collarBodyThicknessInputTextBox;
        private readonly TextBox? _collarHeadLengthInputTextBox;
        private readonly TextBox? _collarHeadThicknessInputTextBox;
        private readonly TextBox? _collarRotateInputTextBox;
        private readonly TextBox? _collarOffsetXInputTextBox;
        private readonly TextBox? _collarOffsetYInputTextBox;
        private readonly TextBox? _collarElevationInputTextBox;
        private readonly TextBox? _collarInflateInputTextBox;
        private readonly Slider? _collarMaterialBaseRSlider;
        private readonly Slider? _collarMaterialBaseGSlider;
        private readonly Slider? _collarMaterialBaseBSlider;
        private readonly Slider? _collarMaterialMetallicSlider;
        private readonly Slider? _collarMaterialRoughnessSlider;
        private readonly Slider? _collarMaterialPearlescenceSlider;
        private readonly Slider? _collarMaterialRustSlider;
        private readonly Slider? _collarMaterialWearSlider;
        private readonly Slider? _collarMaterialGunkSlider;
        private readonly CheckBox? _indicatorEnabledCheckBox;
        private readonly CheckBox? _indicatorCadWallsCheckBox;
        private readonly ComboBox? _indicatorShapeCombo;
        private readonly ComboBox? _indicatorReliefCombo;
        private readonly ComboBox? _indicatorProfileCombo;
        private readonly Slider? _indicatorWidthSlider;
        private readonly Slider? _indicatorLengthSlider;
        private readonly Slider? _indicatorPositionSlider;
        private readonly Slider? _indicatorThicknessSlider;
        private readonly Slider? _indicatorRoundnessSlider;
        private readonly Slider? _indicatorColorBlendSlider;
        private readonly Slider? _indicatorColorRSlider;
        private readonly Slider? _indicatorColorGSlider;
        private readonly Slider? _indicatorColorBSlider;
        private readonly Slider? _materialBaseRSlider;
        private readonly Slider? _materialBaseGSlider;
        private readonly Slider? _materialBaseBSlider;
        private readonly ComboBox? _materialRegionCombo;
        private readonly Slider? _materialMetallicSlider;
        private readonly Slider? _materialRoughnessSlider;
        private readonly Slider? _materialPearlescenceSlider;
        private readonly Slider? _materialRustSlider;
        private readonly Slider? _materialWearSlider;
        private readonly Slider? _materialGunkSlider;
        private readonly Slider? _materialBrushStrengthSlider;
        private readonly Slider? _materialBrushDensitySlider;
        private readonly Slider? _materialCharacterSlider;
        private readonly CheckBox? _spiralNormalInfluenceCheckBox;
        private readonly ComboBox? _basisDebugModeCombo;
        private readonly Slider? _microLodFadeStartSlider;
        private readonly Slider? _microLodFadeEndSlider;
        private readonly Slider? _microRoughnessLodBoostSlider;
        private readonly Slider? _envIntensitySlider;
        private readonly Slider? _envRoughnessMixSlider;
        private readonly TextBox? _envIntensityInputTextBox;
        private readonly TextBox? _envRoughnessMixInputTextBox;
        private readonly Button? _envIntensityResetButton;
        private readonly Button? _envRoughnessMixResetButton;
        private readonly Slider? _envTopRSlider;
        private readonly Slider? _envTopGSlider;
        private readonly Slider? _envTopBSlider;
        private readonly Slider? _envBottomRSlider;
        private readonly Slider? _envBottomGSlider;
        private readonly Slider? _envBottomBSlider;
        private readonly CheckBox? _shadowEnabledCheckBox;
        private readonly ComboBox? _shadowSourceModeCombo;
        private readonly Slider? _shadowStrengthSlider;
        private readonly Slider? _shadowSoftnessSlider;
        private readonly Slider? _shadowDistanceSlider;
        private readonly Slider? _shadowScaleSlider;
        private readonly Slider? _shadowQualitySlider;
        private readonly TextBox? _shadowStrengthInputTextBox;
        private readonly TextBox? _shadowSoftnessInputTextBox;
        private readonly TextBox? _shadowQualityInputTextBox;
        private readonly Button? _shadowStrengthResetButton;
        private readonly Button? _shadowSoftnessResetButton;
        private readonly Button? _shadowQualityResetButton;
        private readonly Slider? _shadowGraySlider;
        private readonly Slider? _shadowDiffuseInfluenceSlider;
        private readonly CheckBox? _brushPaintEnabledCheckBox;
            private readonly ComboBox? _brushPaintChannelCombo;
            private readonly ComboBox? _brushTypeCombo;
            private readonly ColorPicker? _brushPaintColorPicker;
            private readonly StackPanel? _colorChannelPanel;
            private readonly ListBox? _paintLayerListBox;
            private readonly TextBox? _paintLayerNameTextBox;
            private readonly Button? _addPaintLayerButton;
            private readonly Button? _renamePaintLayerButton;
            private readonly Button? _deletePaintLayerButton;
            private readonly CheckBox? _focusPaintLayerCheckBox;
            private readonly Button? _clearPaintLayerFocusButton;
            private readonly ComboBox? _scratchAbrasionTypeCombo;
            private readonly Border? _scratchContextBannerBorder;
            private readonly StackPanel? _scratchPrimaryPanel;
            private readonly StackPanel? _generalPaintPrimaryPanel;
            private readonly Expander? _brushAdvancedExpander;
            private readonly StackPanel? _generalPaintAdvancedPanel;
            private readonly StackPanel? _scratchAdvancedPanel;
            private readonly Slider? _brushSizeSlider;
            private readonly Slider? _brushOpacitySlider;
        private readonly Slider? _brushDarknessSlider;
        private readonly Slider? _brushSpreadSlider;
        private readonly Slider? _paintCoatMetallicSlider;
        private readonly Slider? _paintCoatRoughnessSlider;
        private readonly Slider? _clearCoatAmountSlider;
        private readonly Slider? _clearCoatRoughnessSlider;
        private readonly Slider? _anisotropyAngleSlider;
        private readonly TextBox? _brushSizeInputTextBox;
        private readonly TextBox? _brushOpacityInputTextBox;
        private readonly TextBox? _brushDarknessInputTextBox;
        private readonly TextBox? _brushSpreadInputTextBox;
        private readonly TextBox? _paintCoatMetallicInputTextBox;
        private readonly TextBox? _paintCoatRoughnessInputTextBox;
        private readonly TextBox? _clearCoatAmountInputTextBox;
        private readonly TextBox? _clearCoatRoughnessInputTextBox;
        private readonly TextBox? _anisotropyAngleInputTextBox;
        private readonly Slider? _scratchWidthSlider;
        private readonly Slider? _scratchDepthSlider;
        private readonly Slider? _scratchResistanceSlider;
        private readonly Slider? _scratchDepthRampSlider;
        private readonly Slider? _scratchExposeColorRSlider;
        private readonly Slider? _scratchExposeColorGSlider;
        private readonly Slider? _scratchExposeColorBSlider;
        private readonly Slider? _scratchExposeMetallicSlider;
        private readonly Slider? _scratchExposeRoughnessSlider;
        private readonly TextBox? _scratchWidthInputTextBox;
        private readonly TextBox? _scratchDepthInputTextBox;
        private readonly TextBox? _scratchResistanceInputTextBox;
        private readonly TextBox? _scratchDepthRampInputTextBox;
        private readonly TextBox? _scratchExposeColorRInputTextBox;
        private readonly TextBox? _scratchExposeColorGInputTextBox;
        private readonly TextBox? _scratchExposeColorBInputTextBox;
        private readonly TextBox? _scratchExposeMetallicInputTextBox;
        private readonly TextBox? _scratchExposeRoughnessInputTextBox;
        private readonly Button? _clearPaintMaskButton;
        private readonly Button? _openProjectButton;
        private readonly Button? _saveProjectButton;
        private readonly Button? _saveProjectAsButton;
        private readonly Button? _renderButton;
        private readonly TextBlock? _rotationValueText;
        private readonly TextBlock? _lightXValueText;
        private readonly TextBlock? _lightYValueText;
        private readonly TextBlock? _lightZValueText;
        private readonly TextBlock? _directionValueText;
        private readonly TextBlock? _intensityValueText;
        private readonly TextBlock? _falloffValueText;
        private readonly TextBlock? _lightRValueText;
        private readonly TextBlock? _lightGValueText;
        private readonly TextBlock? _lightBValueText;
        private readonly TextBlock? _diffuseBoostValueText;
        private readonly TextBlock? _specularBoostValueText;
        private readonly TextBlock? _specularPowerValueText;
        private readonly TextBlock? _modelRadiusValueText;
        private readonly TextBlock? _modelHeightValueText;
        private readonly TextBlock? _modelTopScaleValueText;
        private readonly TextBlock? _modelBevelValueText;
        private readonly TextBlock? _bevelCurveValueText;
        private readonly TextBlock? _crownProfileValueText;
        private readonly TextBlock? _bodyTaperValueText;
        private readonly TextBlock? _bodyBulgeValueText;
        private readonly TextBlock? _modelSegmentsValueText;
        private readonly TextBlock? _spiralRidgeHeightValueText;
        private readonly TextBlock? _spiralRidgeWidthValueText;
        private readonly TextBlock? _spiralTurnsValueText;
        private readonly TextBlock? _gripStartValueText;
        private readonly TextBlock? _gripHeightValueText;
        private readonly TextBlock? _gripDensityValueText;
        private readonly TextBlock? _gripPitchValueText;
        private readonly TextBlock? _gripDepthValueText;
        private readonly TextBlock? _gripWidthValueText;
        private readonly TextBlock? _gripSharpnessValueText;
        private readonly TextBlock? _collarScaleValueText;
        private readonly TextBlock? _collarBodyLengthValueText;
        private readonly TextBlock? _collarBodyThicknessValueText;
        private readonly TextBlock? _collarHeadLengthValueText;
        private readonly TextBlock? _collarHeadThicknessValueText;
        private readonly TextBlock? _collarRotateValueText;
        private readonly TextBlock? _collarOffsetXValueText;
        private readonly TextBlock? _collarOffsetYValueText;
        private readonly TextBlock? _collarElevationValueText;
        private readonly TextBlock? _collarInflateValueText;
        private readonly TextBlock? _collarMaterialBaseRValueText;
        private readonly TextBlock? _collarMaterialBaseGValueText;
        private readonly TextBlock? _collarMaterialBaseBValueText;
        private readonly TextBlock? _collarMaterialMetallicValueText;
        private readonly TextBlock? _collarMaterialRoughnessValueText;
        private readonly TextBlock? _collarMaterialPearlescenceValueText;
        private readonly TextBlock? _collarMaterialRustValueText;
        private readonly TextBlock? _collarMaterialWearValueText;
        private readonly TextBlock? _collarMaterialGunkValueText;
        private readonly TextBlock? _indicatorWidthValueText;
        private readonly TextBlock? _indicatorLengthValueText;
        private readonly TextBlock? _indicatorPositionValueText;
        private readonly TextBlock? _indicatorThicknessValueText;
        private readonly TextBlock? _indicatorRoundnessValueText;
        private readonly TextBlock? _indicatorColorBlendValueText;
        private readonly TextBlock? _indicatorColorRValueText;
        private readonly TextBlock? _indicatorColorGValueText;
        private readonly TextBlock? _indicatorColorBValueText;
        private readonly TextBlock? _materialBaseRValueText;
        private readonly TextBlock? _materialBaseGValueText;
        private readonly TextBlock? _materialBaseBValueText;
        private readonly TextBlock? _materialMetallicValueText;
        private readonly TextBlock? _materialRoughnessValueText;
        private readonly TextBlock? _materialPearlescenceValueText;
        private readonly TextBlock? _materialRustValueText;
        private readonly TextBlock? _materialWearValueText;
        private readonly TextBlock? _materialGunkValueText;
        private readonly TextBlock? _materialBrushStrengthValueText;
        private readonly TextBlock? _materialBrushDensityValueText;
        private readonly TextBlock? _materialCharacterValueText;
        private readonly TextBlock? _microLodFadeStartValueText;
        private readonly TextBlock? _microLodFadeEndValueText;
        private readonly TextBlock? _microRoughnessLodBoostValueText;
        private readonly TextBlock? _envIntensityValueText;
        private readonly TextBlock? _envRoughnessMixValueText;
        private readonly TextBlock? _envTopRValueText;
        private readonly TextBlock? _envTopGValueText;
        private readonly TextBlock? _envTopBValueText;
        private readonly TextBlock? _envBottomRValueText;
        private readonly TextBlock? _envBottomGValueText;
        private readonly TextBlock? _envBottomBValueText;
        private readonly TextBlock? _shadowStrengthValueText;
        private readonly TextBlock? _shadowSoftnessValueText;
        private readonly TextBlock? _shadowDistanceValueText;
        private readonly TextBlock? _shadowScaleValueText;
        private readonly TextBlock? _shadowQualityValueText;
        private readonly TextBlock? _shadowGrayValueText;
        private readonly TextBlock? _shadowDiffuseInfluenceValueText;
        private readonly TextBlock? _brushSizeValueText;
        private readonly TextBlock? _brushOpacityValueText;
        private readonly TextBlock? _brushDarknessValueText;
        private readonly TextBlock? _brushSpreadValueText;
        private readonly TextBlock? _paintCoatMetallicValueText;
        private readonly TextBlock? _paintCoatRoughnessValueText;
        private readonly TextBlock? _clearCoatAmountValueText;
        private readonly TextBlock? _clearCoatRoughnessValueText;
        private readonly TextBlock? _anisotropyAngleValueText;
        private readonly TextBlock? _scratchWidthValueText;
        private readonly TextBlock? _scratchDepthValueText;
        private readonly TextBlock? _scratchResistanceValueText;
        private readonly TextBlock? _scratchDepthRampValueText;
        private readonly TextBlock? _scratchExposeColorRValueText;
        private readonly TextBlock? _scratchExposeColorGValueText;
        private readonly TextBlock? _scratchExposeColorBValueText;
        private readonly TextBlock? _scratchExposeMetallicValueText;
        private readonly TextBlock? _scratchExposeRoughnessValueText;
            private readonly Button? _centerLightButton;
            private readonly ObservableCollection<SceneNode> _sceneNodes;
            private readonly List<UserReferenceProfile> _userReferenceProfiles = new();
            private readonly List<ReferenceStyleOption> _referenceStyleOptions = new();
            private readonly List<PaintLayerListItem> _paintLayerItems = new();
            private string? _selectedUserReferenceProfileName;
            private string? _currentProjectFilePath;
            private int _uiRefreshDepth;
            private bool _updatingUi;
            private bool _sceneRefreshDeferredPending;

            private enum InspectorRefreshTabPolicy
            {
                PreserveCurrentTab = 0,
                FollowSceneSelection = 1
            }

            private bool IsUiRefreshing => _uiRefreshDepth > 0;

        private void WithUiRefreshSuppressed(Action action)
        {
            _uiRefreshDepth++;
            _updatingUi = true;
            try
            {
                action();
            }
            finally
            {
                // IMPORTANT: delay turning off _updatingUi until after UI thread processes queued property events
                Dispatcher.UIThread.Post(() =>
                {
                    _uiRefreshDepth = Math.Max(0, _uiRefreshDepth - 1);
                    if (_uiRefreshDepth == 0)
                    {
                        _updatingUi = false;
                    }
                }, DispatcherPriority.Background);
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            _project = new KnobProject();
            _sceneNodes = new ObservableCollection<SceneNode>();

            _metalViewport = this.FindControl<MetalViewport>("MetalViewport");
            _viewportOverlay = this.FindControl<Control>("ViewportOverlay");
            _paintHudBorder = this.FindControl<Border>("PaintHudBorder");
            _paintHudTitleText = this.FindControl<TextBlock>("PaintHudTitleText");
            _paintHudLine1Text = this.FindControl<TextBlock>("PaintHudLine1Text");
            _paintHudLine2Text = this.FindControl<TextBlock>("PaintHudLine2Text");
            _paintHudLine3Text = this.FindControl<TextBlock>("PaintHudLine3Text");
            _paintHudLine4Text = this.FindControl<TextBlock>("PaintHudLine4Text");
            _sceneListBox = this.FindControl<ListBox>("SceneListBox");
            _inspectorTabControl = this.FindControl<TabControl>("InspectorTabControl");
            _lightingTabItem = this.FindControl<TabItem>("LightingTabItem");
            _modelTabItem = this.FindControl<TabItem>("ModelTabItem");
            _brushTabItem = this.FindControl<TabItem>("BrushTabItem");
            _cameraTabItem = this.FindControl<TabItem>("CameraTabItem");
            _environmentTabItem = this.FindControl<TabItem>("EnvironmentTabItem");
            _shadowsTabItem = this.FindControl<TabItem>("ShadowsTabItem");
            _undoButton = this.FindControl<Button>("UndoButton");
            _redoButton = this.FindControl<Button>("RedoButton");
            _inspectorScrollViewer = this.FindControl<ScrollViewer>("InspectorScrollViewer");
            _inspectorSearchTextBox = this.FindControl<TextBox>("InspectorSearchTextBox");
            _inspectorSearchResultsListBox = this.FindControl<ListBox>("InspectorSearchResultsListBox");
            _recentTweaksListBox = this.FindControl<ListBox>("RecentTweaksListBox");
            _favoriteReferenceProfilesButton = this.FindControl<Button>("FavoriteReferenceProfilesButton");
            _favoriteRoughnessButton = this.FindControl<Button>("FavoriteRoughnessButton");
            _favoriteScratchDepthButton = this.FindControl<Button>("FavoriteScratchDepthButton");
            _favoriteCollarPresetButton = this.FindControl<Button>("FavoriteCollarPresetButton");
            _favoriteShadowSoftnessButton = this.FindControl<Button>("FavoriteShadowSoftnessButton");
            _favoriteEnvIntensityButton = this.FindControl<Button>("FavoriteEnvIntensityButton");
            _lightingModeCombo = this.FindControl<ComboBox>("LightingModeCombo");
            _lightListBox = this.FindControl<ListBox>("LightListBox");
            _addLightButton = this.FindControl<Button>("AddLightButton");
            _removeLightButton = this.FindControl<Button>("RemoveLightButton");
            _resetViewButton = this.FindControl<Button>("ResetViewButton");
            _lightTypeCombo = this.FindControl<ComboBox>("LightTypeCombo");
            _rotationSlider = this.FindControl<Slider>("RotationSlider");
            _lightXSlider = this.FindControl<Slider>("LightXSlider");
            _lightYSlider = this.FindControl<Slider>("LightYSlider");
            _lightZSlider = this.FindControl<Slider>("LightZSlider");
            _directionSlider = this.FindControl<Slider>("DirectionSlider");
            _intensitySlider = this.FindControl<Slider>("IntensitySlider");
            _falloffSlider = this.FindControl<Slider>("FalloffSlider");
            _lightRSlider = this.FindControl<Slider>("LightRSlider");
            _lightGSlider = this.FindControl<Slider>("LightGSlider");
            _lightBSlider = this.FindControl<Slider>("LightBSlider");
            _diffuseBoostSlider = this.FindControl<Slider>("DiffuseBoostSlider");
            _specularBoostSlider = this.FindControl<Slider>("SpecularBoostSlider");
            _specularPowerSlider = this.FindControl<Slider>("SpecularPowerSlider");
            _modelRadiusSlider = this.FindControl<Slider>("ModelRadiusSlider");
            _modelHeightSlider = this.FindControl<Slider>("ModelHeightSlider");
            _modelTopScaleSlider = this.FindControl<Slider>("ModelTopScaleSlider");
            _modelBevelSlider = this.FindControl<Slider>("ModelBevelSlider");
            _referenceStyleCombo = this.FindControl<ComboBox>("ReferenceStyleCombo");
            _referenceStyleSaveNameTextBox = this.FindControl<TextBox>("ReferenceStyleSaveNameTextBox");
            _saveReferenceProfileButton = this.FindControl<Button>("SaveReferenceProfileButton");
            _overwriteReferenceProfileButton = this.FindControl<Button>("OverwriteReferenceProfileButton");
            _renameReferenceProfileButton = this.FindControl<Button>("RenameReferenceProfileButton");
            _duplicateReferenceProfileButton = this.FindControl<Button>("DuplicateReferenceProfileButton");
            _deleteReferenceProfileButton = this.FindControl<Button>("DeleteReferenceProfileButton");
            _referenceProfileStatusText = this.FindControl<TextBlock>("ReferenceProfileStatusText");
            _bodyStyleCombo = this.FindControl<ComboBox>("BodyStyleCombo");
            _bevelCurveSlider = this.FindControl<Slider>("BevelCurveSlider");
            _crownProfileSlider = this.FindControl<Slider>("CrownProfileSlider");
            _bodyTaperSlider = this.FindControl<Slider>("BodyTaperSlider");
            _bodyBulgeSlider = this.FindControl<Slider>("BodyBulgeSlider");
            _modelSegmentsSlider = this.FindControl<Slider>("ModelSegmentsSlider");
            _spiralRidgeHeightSlider = this.FindControl<Slider>("SpiralRidgeHeightSlider");
            _spiralRidgeWidthSlider = this.FindControl<Slider>("SpiralRidgeWidthSlider");
            _spiralTurnsSlider = this.FindControl<Slider>("SpiralTurnsSlider");
            _gripStyleCombo = this.FindControl<ComboBox>("GripStyleCombo");
            _gripTypeCombo = this.FindControl<ComboBox>("GripTypeCombo");
            _gripStartSlider = this.FindControl<Slider>("GripStartSlider");
            _gripHeightSlider = this.FindControl<Slider>("GripHeightSlider");
            _gripDensitySlider = this.FindControl<Slider>("GripDensitySlider");
            _gripPitchSlider = this.FindControl<Slider>("GripPitchSlider");
            _gripDepthSlider = this.FindControl<Slider>("GripDepthSlider");
            _gripWidthSlider = this.FindControl<Slider>("GripWidthSlider");
            _gripSharpnessSlider = this.FindControl<Slider>("GripSharpnessSlider");
            _collarEnabledCheckBox = this.FindControl<CheckBox>("CollarEnabledCheckBox");
            _collarPresetCombo = this.FindControl<ComboBox>("CollarPresetCombo");
            _collarMeshPathTextBox = this.FindControl<TextBox>("CollarMeshPathTextBox");
            _collarResolvedMeshPathText = this.FindControl<TextBlock>("CollarResolvedMeshPathText");
            _collarMeshPathStatusText = this.FindControl<TextBlock>("CollarMeshPathStatusText");
            _collarScaleSlider = this.FindControl<Slider>("CollarScaleSlider");
            _collarBodyLengthSlider = this.FindControl<Slider>("CollarBodyLengthSlider");
            _collarBodyThicknessSlider = this.FindControl<Slider>("CollarBodyThicknessSlider");
            _collarHeadLengthSlider = this.FindControl<Slider>("CollarHeadLengthSlider");
            _collarHeadThicknessSlider = this.FindControl<Slider>("CollarHeadThicknessSlider");
            _collarRotateSlider = this.FindControl<Slider>("CollarRotateSlider");
            _collarOffsetXSlider = this.FindControl<Slider>("CollarOffsetXSlider");
            _collarOffsetYSlider = this.FindControl<Slider>("CollarOffsetYSlider");
            _collarElevationSlider = this.FindControl<Slider>("CollarElevationSlider");
            _collarInflateSlider = this.FindControl<Slider>("CollarInflateSlider");
            _collarScaleInputTextBox = this.FindControl<TextBox>("CollarScaleInputTextBox");
            _collarBodyLengthInputTextBox = this.FindControl<TextBox>("CollarBodyLengthInputTextBox");
            _collarBodyThicknessInputTextBox = this.FindControl<TextBox>("CollarBodyThicknessInputTextBox");
            _collarHeadLengthInputTextBox = this.FindControl<TextBox>("CollarHeadLengthInputTextBox");
            _collarHeadThicknessInputTextBox = this.FindControl<TextBox>("CollarHeadThicknessInputTextBox");
            _collarRotateInputTextBox = this.FindControl<TextBox>("CollarRotateInputTextBox");
            _collarOffsetXInputTextBox = this.FindControl<TextBox>("CollarOffsetXInputTextBox");
            _collarOffsetYInputTextBox = this.FindControl<TextBox>("CollarOffsetYInputTextBox");
            _collarElevationInputTextBox = this.FindControl<TextBox>("CollarElevationInputTextBox");
            _collarInflateInputTextBox = this.FindControl<TextBox>("CollarInflateInputTextBox");
            _collarMaterialBaseRSlider = this.FindControl<Slider>("CollarMaterialBaseRSlider");
            _collarMaterialBaseGSlider = this.FindControl<Slider>("CollarMaterialBaseGSlider");
            _collarMaterialBaseBSlider = this.FindControl<Slider>("CollarMaterialBaseBSlider");
            _collarMaterialMetallicSlider = this.FindControl<Slider>("CollarMaterialMetallicSlider");
            _collarMaterialRoughnessSlider = this.FindControl<Slider>("CollarMaterialRoughnessSlider");
            _collarMaterialPearlescenceSlider = this.FindControl<Slider>("CollarMaterialPearlescenceSlider");
            _collarMaterialRustSlider = this.FindControl<Slider>("CollarMaterialRustSlider");
            _collarMaterialWearSlider = this.FindControl<Slider>("CollarMaterialWearSlider");
            _collarMaterialGunkSlider = this.FindControl<Slider>("CollarMaterialGunkSlider");
            _indicatorEnabledCheckBox = this.FindControl<CheckBox>("IndicatorEnabledCheckBox");
            _indicatorCadWallsCheckBox = this.FindControl<CheckBox>("IndicatorCadWallsCheckBox");
            _indicatorShapeCombo = this.FindControl<ComboBox>("IndicatorShapeCombo");
            _indicatorReliefCombo = this.FindControl<ComboBox>("IndicatorReliefCombo");
            _indicatorProfileCombo = this.FindControl<ComboBox>("IndicatorProfileCombo");
            _indicatorWidthSlider = this.FindControl<Slider>("IndicatorWidthSlider");
            _indicatorLengthSlider = this.FindControl<Slider>("IndicatorLengthSlider");
            _indicatorPositionSlider = this.FindControl<Slider>("IndicatorPositionSlider");
            _indicatorThicknessSlider = this.FindControl<Slider>("IndicatorThicknessSlider");
            _indicatorRoundnessSlider = this.FindControl<Slider>("IndicatorRoundnessSlider");
            _indicatorColorBlendSlider = this.FindControl<Slider>("IndicatorColorBlendSlider");
            _indicatorColorRSlider = this.FindControl<Slider>("IndicatorColorRSlider");
            _indicatorColorGSlider = this.FindControl<Slider>("IndicatorColorGSlider");
            _indicatorColorBSlider = this.FindControl<Slider>("IndicatorColorBSlider");
            _materialBaseRSlider = this.FindControl<Slider>("MaterialBaseRSlider");
            _materialBaseGSlider = this.FindControl<Slider>("MaterialBaseGSlider");
            _materialBaseBSlider = this.FindControl<Slider>("MaterialBaseBSlider");
            _materialRegionCombo = this.FindControl<ComboBox>("MaterialRegionCombo");
            _materialMetallicSlider = this.FindControl<Slider>("MaterialMetallicSlider");
            _materialRoughnessSlider = this.FindControl<Slider>("MaterialRoughnessSlider");
            _materialPearlescenceSlider = this.FindControl<Slider>("MaterialPearlescenceSlider");
            _materialRustSlider = this.FindControl<Slider>("MaterialRustSlider");
            _materialWearSlider = this.FindControl<Slider>("MaterialWearSlider");
            _materialGunkSlider = this.FindControl<Slider>("MaterialGunkSlider");
            _materialBrushStrengthSlider = this.FindControl<Slider>("MaterialBrushStrengthSlider");
            _materialBrushDensitySlider = this.FindControl<Slider>("MaterialBrushDensitySlider");
            _materialCharacterSlider = this.FindControl<Slider>("MaterialCharacterSlider");
            _spiralNormalInfluenceCheckBox = this.FindControl<CheckBox>("SpiralNormalInfluenceCheckBox");
            _basisDebugModeCombo = this.FindControl<ComboBox>("BasisDebugModeCombo");
            _microLodFadeStartSlider = this.FindControl<Slider>("MicroLodFadeStartSlider");
            _microLodFadeEndSlider = this.FindControl<Slider>("MicroLodFadeEndSlider");
            _microRoughnessLodBoostSlider = this.FindControl<Slider>("MicroRoughnessLodBoostSlider");
            _envIntensitySlider = this.FindControl<Slider>("EnvIntensitySlider");
            _envRoughnessMixSlider = this.FindControl<Slider>("EnvRoughnessMixSlider");
            _envIntensityInputTextBox = this.FindControl<TextBox>("EnvIntensityInputTextBox");
            _envRoughnessMixInputTextBox = this.FindControl<TextBox>("EnvRoughnessMixInputTextBox");
            _envIntensityResetButton = this.FindControl<Button>("EnvIntensityResetButton");
            _envRoughnessMixResetButton = this.FindControl<Button>("EnvRoughnessMixResetButton");
            _envTopRSlider = this.FindControl<Slider>("EnvTopRSlider");
            _envTopGSlider = this.FindControl<Slider>("EnvTopGSlider");
            _envTopBSlider = this.FindControl<Slider>("EnvTopBSlider");
            _envBottomRSlider = this.FindControl<Slider>("EnvBottomRSlider");
            _envBottomGSlider = this.FindControl<Slider>("EnvBottomGSlider");
            _envBottomBSlider = this.FindControl<Slider>("EnvBottomBSlider");
            _shadowEnabledCheckBox = this.FindControl<CheckBox>("ShadowEnabledCheckBox");
            _shadowSourceModeCombo = this.FindControl<ComboBox>("ShadowSourceModeCombo");
            _shadowStrengthSlider = this.FindControl<Slider>("ShadowStrengthSlider");
            _shadowSoftnessSlider = this.FindControl<Slider>("ShadowSoftnessSlider");
            _shadowDistanceSlider = this.FindControl<Slider>("ShadowDistanceSlider");
            _shadowScaleSlider = this.FindControl<Slider>("ShadowScaleSlider");
            _shadowQualitySlider = this.FindControl<Slider>("ShadowQualitySlider");
            _shadowStrengthInputTextBox = this.FindControl<TextBox>("ShadowStrengthInputTextBox");
            _shadowSoftnessInputTextBox = this.FindControl<TextBox>("ShadowSoftnessInputTextBox");
            _shadowQualityInputTextBox = this.FindControl<TextBox>("ShadowQualityInputTextBox");
            _shadowStrengthResetButton = this.FindControl<Button>("ShadowStrengthResetButton");
            _shadowSoftnessResetButton = this.FindControl<Button>("ShadowSoftnessResetButton");
            _shadowQualityResetButton = this.FindControl<Button>("ShadowQualityResetButton");
            _shadowGraySlider = this.FindControl<Slider>("ShadowGraySlider");
            _shadowDiffuseInfluenceSlider = this.FindControl<Slider>("ShadowDiffuseInfluenceSlider");
            _brushPaintEnabledCheckBox = this.FindControl<CheckBox>("BrushPaintEnabledCheckBox");
            _brushPaintChannelCombo = this.FindControl<ComboBox>("BrushPaintChannelCombo");
            _brushTypeCombo = this.FindControl<ComboBox>("BrushTypeCombo");
            _brushPaintColorPicker = this.FindControl<ColorPicker>("BrushPaintColorPicker");
            _colorChannelPanel = this.FindControl<StackPanel>("ColorChannelPanel");
            _paintLayerListBox = this.FindControl<ListBox>("PaintLayerListBox");
            _paintLayerNameTextBox = this.FindControl<TextBox>("PaintLayerNameTextBox");
            _addPaintLayerButton = this.FindControl<Button>("AddPaintLayerButton");
            _renamePaintLayerButton = this.FindControl<Button>("RenamePaintLayerButton");
            _deletePaintLayerButton = this.FindControl<Button>("DeletePaintLayerButton");
            _focusPaintLayerCheckBox = this.FindControl<CheckBox>("FocusPaintLayerCheckBox");
            _clearPaintLayerFocusButton = this.FindControl<Button>("ClearPaintLayerFocusButton");
            _scratchAbrasionTypeCombo = this.FindControl<ComboBox>("ScratchAbrasionTypeCombo");
            _scratchContextBannerBorder = this.FindControl<Border>("ScratchContextBannerBorder");
            _scratchPrimaryPanel = this.FindControl<StackPanel>("ScratchPrimaryPanel");
            _generalPaintPrimaryPanel = this.FindControl<StackPanel>("GeneralPaintPrimaryPanel");
            _brushAdvancedExpander = this.FindControl<Expander>("BrushAdvancedExpander");
            _generalPaintAdvancedPanel = this.FindControl<StackPanel>("GeneralPaintAdvancedPanel");
            _scratchAdvancedPanel = this.FindControl<StackPanel>("ScratchAdvancedPanel");
            _brushSizeSlider = this.FindControl<Slider>("BrushSizeSlider");
            _brushOpacitySlider = this.FindControl<Slider>("BrushOpacitySlider");
            _brushDarknessSlider = this.FindControl<Slider>("BrushDarknessSlider");
            _brushSpreadSlider = this.FindControl<Slider>("BrushSpreadSlider");
            _paintCoatMetallicSlider = this.FindControl<Slider>("PaintCoatMetallicSlider");
            _paintCoatRoughnessSlider = this.FindControl<Slider>("PaintCoatRoughnessSlider");
            _clearCoatAmountSlider = this.FindControl<Slider>("ClearCoatAmountSlider");
            _clearCoatRoughnessSlider = this.FindControl<Slider>("ClearCoatRoughnessSlider");
            _anisotropyAngleSlider = this.FindControl<Slider>("AnisotropyAngleSlider");
            _brushSizeInputTextBox = this.FindControl<TextBox>("BrushSizeInputTextBox");
            _brushOpacityInputTextBox = this.FindControl<TextBox>("BrushOpacityInputTextBox");
            _brushDarknessInputTextBox = this.FindControl<TextBox>("BrushDarknessInputTextBox");
            _brushSpreadInputTextBox = this.FindControl<TextBox>("BrushSpreadInputTextBox");
            _paintCoatMetallicInputTextBox = this.FindControl<TextBox>("PaintCoatMetallicInputTextBox");
            _paintCoatRoughnessInputTextBox = this.FindControl<TextBox>("PaintCoatRoughnessInputTextBox");
            _clearCoatAmountInputTextBox = this.FindControl<TextBox>("ClearCoatAmountInputTextBox");
            _clearCoatRoughnessInputTextBox = this.FindControl<TextBox>("ClearCoatRoughnessInputTextBox");
            _anisotropyAngleInputTextBox = this.FindControl<TextBox>("AnisotropyAngleInputTextBox");
            _scratchWidthSlider = this.FindControl<Slider>("ScratchWidthSlider");
            _scratchDepthSlider = this.FindControl<Slider>("ScratchDepthSlider");
            _scratchResistanceSlider = this.FindControl<Slider>("ScratchResistanceSlider");
            _scratchDepthRampSlider = this.FindControl<Slider>("ScratchDepthRampSlider");
            _scratchExposeColorRSlider = this.FindControl<Slider>("ScratchExposeColorRSlider");
            _scratchExposeColorGSlider = this.FindControl<Slider>("ScratchExposeColorGSlider");
            _scratchExposeColorBSlider = this.FindControl<Slider>("ScratchExposeColorBSlider");
            _scratchExposeMetallicSlider = this.FindControl<Slider>("ScratchExposeMetallicSlider");
            _scratchExposeRoughnessSlider = this.FindControl<Slider>("ScratchExposeRoughnessSlider");
            _scratchWidthInputTextBox = this.FindControl<TextBox>("ScratchWidthInputTextBox");
            _scratchDepthInputTextBox = this.FindControl<TextBox>("ScratchDepthInputTextBox");
            _scratchResistanceInputTextBox = this.FindControl<TextBox>("ScratchResistanceInputTextBox");
            _scratchDepthRampInputTextBox = this.FindControl<TextBox>("ScratchDepthRampInputTextBox");
            _scratchExposeColorRInputTextBox = this.FindControl<TextBox>("ScratchExposeColorRInputTextBox");
            _scratchExposeColorGInputTextBox = this.FindControl<TextBox>("ScratchExposeColorGInputTextBox");
            _scratchExposeColorBInputTextBox = this.FindControl<TextBox>("ScratchExposeColorBInputTextBox");
            _scratchExposeMetallicInputTextBox = this.FindControl<TextBox>("ScratchExposeMetallicInputTextBox");
            _scratchExposeRoughnessInputTextBox = this.FindControl<TextBox>("ScratchExposeRoughnessInputTextBox");
            _clearPaintMaskButton = this.FindControl<Button>("ClearPaintMaskButton");
            _openProjectButton = this.FindControl<Button>("OpenProjectButton");
            _saveProjectButton = this.FindControl<Button>("SaveProjectButton");
            _saveProjectAsButton = this.FindControl<Button>("SaveProjectAsButton");
            _renderButton = this.FindControl<Button>("RenderButton");

            _rotationValueText = this.FindControl<TextBlock>("RotationValueText");
            _lightXValueText = this.FindControl<TextBlock>("LightXValueText");
            _lightYValueText = this.FindControl<TextBlock>("LightYValueText");
            _lightZValueText = this.FindControl<TextBlock>("LightZValueText");
            _directionValueText = this.FindControl<TextBlock>("DirectionValueText");
            _intensityValueText = this.FindControl<TextBlock>("IntensityValueText");
            _falloffValueText = this.FindControl<TextBlock>("FalloffValueText");
            _lightRValueText = this.FindControl<TextBlock>("LightRValueText");
            _lightGValueText = this.FindControl<TextBlock>("LightGValueText");
            _lightBValueText = this.FindControl<TextBlock>("LightBValueText");
            _diffuseBoostValueText = this.FindControl<TextBlock>("DiffuseBoostValueText");
            _specularBoostValueText = this.FindControl<TextBlock>("SpecularBoostValueText");
            _specularPowerValueText = this.FindControl<TextBlock>("SpecularPowerValueText");
            _modelRadiusValueText = this.FindControl<TextBlock>("ModelRadiusValueText");
            _modelHeightValueText = this.FindControl<TextBlock>("ModelHeightValueText");
            _modelTopScaleValueText = this.FindControl<TextBlock>("ModelTopScaleValueText");
            _modelBevelValueText = this.FindControl<TextBlock>("ModelBevelValueText");
            _bevelCurveValueText = this.FindControl<TextBlock>("BevelCurveValueText");
            _crownProfileValueText = this.FindControl<TextBlock>("CrownProfileValueText");
            _bodyTaperValueText = this.FindControl<TextBlock>("BodyTaperValueText");
            _bodyBulgeValueText = this.FindControl<TextBlock>("BodyBulgeValueText");
            _modelSegmentsValueText = this.FindControl<TextBlock>("ModelSegmentsValueText");
            _spiralRidgeHeightValueText = this.FindControl<TextBlock>("SpiralRidgeHeightValueText");
            _spiralRidgeWidthValueText = this.FindControl<TextBlock>("SpiralRidgeWidthValueText");
            _spiralTurnsValueText = this.FindControl<TextBlock>("SpiralTurnsValueText");
            _gripStartValueText = this.FindControl<TextBlock>("GripStartValueText");
            _gripHeightValueText = this.FindControl<TextBlock>("GripHeightValueText");
            _gripDensityValueText = this.FindControl<TextBlock>("GripDensityValueText");
            _gripPitchValueText = this.FindControl<TextBlock>("GripPitchValueText");
            _gripDepthValueText = this.FindControl<TextBlock>("GripDepthValueText");
            _gripWidthValueText = this.FindControl<TextBlock>("GripWidthValueText");
            _gripSharpnessValueText = this.FindControl<TextBlock>("GripSharpnessValueText");
            _collarScaleValueText = this.FindControl<TextBlock>("CollarScaleValueText");
            _collarBodyLengthValueText = this.FindControl<TextBlock>("CollarBodyLengthValueText");
            _collarBodyThicknessValueText = this.FindControl<TextBlock>("CollarBodyThicknessValueText");
            _collarHeadLengthValueText = this.FindControl<TextBlock>("CollarHeadLengthValueText");
            _collarHeadThicknessValueText = this.FindControl<TextBlock>("CollarHeadThicknessValueText");
            _collarRotateValueText = this.FindControl<TextBlock>("CollarRotateValueText");
            _collarOffsetXValueText = this.FindControl<TextBlock>("CollarOffsetXValueText");
            _collarOffsetYValueText = this.FindControl<TextBlock>("CollarOffsetYValueText");
            _collarElevationValueText = this.FindControl<TextBlock>("CollarElevationValueText");
            _collarInflateValueText = this.FindControl<TextBlock>("CollarInflateValueText");
            _collarMaterialBaseRValueText = this.FindControl<TextBlock>("CollarMaterialBaseRValueText");
            _collarMaterialBaseGValueText = this.FindControl<TextBlock>("CollarMaterialBaseGValueText");
            _collarMaterialBaseBValueText = this.FindControl<TextBlock>("CollarMaterialBaseBValueText");
            _collarMaterialMetallicValueText = this.FindControl<TextBlock>("CollarMaterialMetallicValueText");
            _collarMaterialRoughnessValueText = this.FindControl<TextBlock>("CollarMaterialRoughnessValueText");
            _collarMaterialPearlescenceValueText = this.FindControl<TextBlock>("CollarMaterialPearlescenceValueText");
            _collarMaterialRustValueText = this.FindControl<TextBlock>("CollarMaterialRustValueText");
            _collarMaterialWearValueText = this.FindControl<TextBlock>("CollarMaterialWearValueText");
            _collarMaterialGunkValueText = this.FindControl<TextBlock>("CollarMaterialGunkValueText");
            _indicatorWidthValueText = this.FindControl<TextBlock>("IndicatorWidthValueText");
            _indicatorLengthValueText = this.FindControl<TextBlock>("IndicatorLengthValueText");
            _indicatorPositionValueText = this.FindControl<TextBlock>("IndicatorPositionValueText");
            _indicatorThicknessValueText = this.FindControl<TextBlock>("IndicatorThicknessValueText");
            _indicatorRoundnessValueText = this.FindControl<TextBlock>("IndicatorRoundnessValueText");
            _indicatorColorBlendValueText = this.FindControl<TextBlock>("IndicatorColorBlendValueText");
            _indicatorColorRValueText = this.FindControl<TextBlock>("IndicatorColorRValueText");
            _indicatorColorGValueText = this.FindControl<TextBlock>("IndicatorColorGValueText");
            _indicatorColorBValueText = this.FindControl<TextBlock>("IndicatorColorBValueText");
            _materialBaseRValueText = this.FindControl<TextBlock>("MaterialBaseRValueText");
            _materialBaseGValueText = this.FindControl<TextBlock>("MaterialBaseGValueText");
            _materialBaseBValueText = this.FindControl<TextBlock>("MaterialBaseBValueText");
            _materialMetallicValueText = this.FindControl<TextBlock>("MaterialMetallicValueText");
            _materialRoughnessValueText = this.FindControl<TextBlock>("MaterialRoughnessValueText");
            _materialPearlescenceValueText = this.FindControl<TextBlock>("MaterialPearlescenceValueText");
            _materialRustValueText = this.FindControl<TextBlock>("MaterialRustValueText");
            _materialWearValueText = this.FindControl<TextBlock>("MaterialWearValueText");
            _materialGunkValueText = this.FindControl<TextBlock>("MaterialGunkValueText");
            _materialBrushStrengthValueText = this.FindControl<TextBlock>("MaterialBrushStrengthValueText");
            _materialBrushDensityValueText = this.FindControl<TextBlock>("MaterialBrushDensityValueText");
            _materialCharacterValueText = this.FindControl<TextBlock>("MaterialCharacterValueText");
            _microLodFadeStartValueText = this.FindControl<TextBlock>("MicroLodFadeStartValueText");
            _microLodFadeEndValueText = this.FindControl<TextBlock>("MicroLodFadeEndValueText");
            _microRoughnessLodBoostValueText = this.FindControl<TextBlock>("MicroRoughnessLodBoostValueText");
            _envIntensityValueText = this.FindControl<TextBlock>("EnvIntensityValueText");
            _envRoughnessMixValueText = this.FindControl<TextBlock>("EnvRoughnessMixValueText");
            _envTopRValueText = this.FindControl<TextBlock>("EnvTopRValueText");
            _envTopGValueText = this.FindControl<TextBlock>("EnvTopGValueText");
            _envTopBValueText = this.FindControl<TextBlock>("EnvTopBValueText");
            _envBottomRValueText = this.FindControl<TextBlock>("EnvBottomRValueText");
            _envBottomGValueText = this.FindControl<TextBlock>("EnvBottomGValueText");
            _envBottomBValueText = this.FindControl<TextBlock>("EnvBottomBValueText");
            _shadowStrengthValueText = this.FindControl<TextBlock>("ShadowStrengthValueText");
            _shadowSoftnessValueText = this.FindControl<TextBlock>("ShadowSoftnessValueText");
            _shadowDistanceValueText = this.FindControl<TextBlock>("ShadowDistanceValueText");
            _shadowScaleValueText = this.FindControl<TextBlock>("ShadowScaleValueText");
            _shadowQualityValueText = this.FindControl<TextBlock>("ShadowQualityValueText");
            _shadowGrayValueText = this.FindControl<TextBlock>("ShadowGrayValueText");
            _shadowDiffuseInfluenceValueText = this.FindControl<TextBlock>("ShadowDiffuseInfluenceValueText");
            _brushSizeValueText = this.FindControl<TextBlock>("BrushSizeValueText");
            _brushOpacityValueText = this.FindControl<TextBlock>("BrushOpacityValueText");
            _brushDarknessValueText = this.FindControl<TextBlock>("BrushDarknessValueText");
            _brushSpreadValueText = this.FindControl<TextBlock>("BrushSpreadValueText");
            _paintCoatMetallicValueText = this.FindControl<TextBlock>("PaintCoatMetallicValueText");
            _paintCoatRoughnessValueText = this.FindControl<TextBlock>("PaintCoatRoughnessValueText");
            _clearCoatAmountValueText = this.FindControl<TextBlock>("ClearCoatAmountValueText");
            _clearCoatRoughnessValueText = this.FindControl<TextBlock>("ClearCoatRoughnessValueText");
            _anisotropyAngleValueText = this.FindControl<TextBlock>("AnisotropyAngleValueText");
            _scratchWidthValueText = this.FindControl<TextBlock>("ScratchWidthValueText");
            _scratchDepthValueText = this.FindControl<TextBlock>("ScratchDepthValueText");
            _scratchResistanceValueText = this.FindControl<TextBlock>("ScratchResistanceValueText");
            _scratchDepthRampValueText = this.FindControl<TextBlock>("ScratchDepthRampValueText");
            _scratchExposeColorRValueText = this.FindControl<TextBlock>("ScratchExposeColorRValueText");
            _scratchExposeColorGValueText = this.FindControl<TextBlock>("ScratchExposeColorGValueText");
            _scratchExposeColorBValueText = this.FindControl<TextBlock>("ScratchExposeColorBValueText");
            _scratchExposeMetallicValueText = this.FindControl<TextBlock>("ScratchExposeMetallicValueText");
            _scratchExposeRoughnessValueText = this.FindControl<TextBlock>("ScratchExposeRoughnessValueText");
            _centerLightButton = this.FindControl<Button>("CenterLightButton");
            if (!HasRequiredControls())
            {
                return;
            }

            LoadUserReferenceProfiles();
            InitializeViewportAndSceneBindings();
            WireButtonHandlers();
            WireControlPropertyHandlers();
            InitializeUpdatePolicy();
            InitializePrecisionControls();
            InitializeBrushContextAndHudUx();
            InitializeInspectorUx();
            InitializeUndoRedoSupport();
            WireOpenedHandlers();
            UpdateWindowTitleForProject();
        }
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
            bool customImportedPreset = importedPreset && preset == CollarPreset.ImportedStl;
            _collarMeshPathTextBox.IsEnabled = customImportedPreset;
            _collarMeshPathTextBox.IsReadOnly = importedPreset && !customImportedPreset;
            _collarScaleSlider.IsEnabled = importedPreset;
            _collarBodyLengthSlider.IsEnabled = importedPreset;
            _collarBodyThicknessSlider.IsEnabled = importedPreset;
            _collarHeadLengthSlider.IsEnabled = importedPreset;
            _collarHeadThicknessSlider.IsEnabled = importedPreset;
            _collarRotateSlider.IsEnabled = importedPreset;
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
            UpdateCollarMeshPathFeedback(preset, _collarMeshPathTextBox.Text);
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
                        _collarPresetCombo.SelectedItem = collar.Preset;
                        _collarMeshPathTextBox.Text = CollarNode.ResolveImportedMeshPath(collar.Preset, collar.ImportedMeshPath);
                        _collarScaleSlider.Value = collar.ImportedScale;
                        _collarBodyLengthSlider.Value = collar.ImportedBodyLengthScale;
                        _collarBodyThicknessSlider.Value = collar.ImportedBodyThicknessScale;
                        _collarHeadLengthSlider.Value = collar.ImportedHeadLengthScale;
                        _collarHeadThicknessSlider.Value = collar.ImportedHeadThicknessScale;
                        _collarRotateSlider.Value = RadiansToDegrees(collar.ImportedRotationRadians);
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
                        _collarPresetCombo.SelectedItem = CollarPreset.None;
                        _collarMeshPathTextBox.Text = string.Empty;
                        _collarScaleSlider.Value = 1.0;
                        _collarBodyLengthSlider.Value = 1.0;
                        _collarBodyThicknessSlider.Value = 1.0;
                        _collarHeadLengthSlider.Value = 1.0;
                        _collarHeadThicknessSlider.Value = 1.0;
                        _collarRotateSlider.Value = 0.0;
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
