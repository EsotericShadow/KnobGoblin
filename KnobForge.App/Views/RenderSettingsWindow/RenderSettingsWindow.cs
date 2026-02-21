using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using KnobForge.App.Controls;
using KnobForge.Core;
using KnobForge.Core.Export;
using KnobForge.Core.Scene;
using KnobForge.Rendering;
using KnobForge.Rendering.GPU;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KnobForge.App.Views
{
    public partial class RenderSettingsWindow : Window
    {
        private const int MinFrameCount = 1;
        private const int MaxFrameCount = 1440;
        private const int MinResolution = 1;
        private const int MaxResolution = 16384;
        private const int MinSupersample = 1;
        private const int MaxSupersample = 4;
        private const float MinOrbitOffsetDeg = 0f;
        private const float MaxOrbitYawOffsetDeg = 180f;
        private const float MaxOrbitPitchOffsetDeg = 85f;

        private readonly KnobProject _project;
        private readonly OrientationDebug _orientation;
        private readonly ViewportCameraState _cameraState;
        private readonly MetalViewport? _gpuViewport;
        private readonly Control _settingsPanel;
        private readonly ComboBox _outputStrategyComboBox;
        private readonly TextBlock _outputStrategyDescriptionTextBlock;
        private readonly TextBox _frameCountTextBox;
        private readonly ComboBox _resolutionComboBox;
        private readonly ComboBox _supersampleComboBox;
        private readonly TextBox _paddingTextBox;
        private readonly TextBox _cameraDistanceScaleTextBox;
        private readonly CheckBox _exportOrbitVariantsCheckBox;
        private readonly TextBox _orbitYawOffsetTextBox;
        private readonly TextBox _orbitPitchOffsetTextBox;
        private readonly ComboBox _filterPresetComboBox;
        private readonly TextBox _baseNameTextBox;
        private readonly TextBox _outputFolderTextBox;
        private readonly Button _browseOutputButton;
        private readonly ComboBox _spritesheetLayoutComboBox;
        private readonly CheckBox _exportFramesCheckBox;
        private readonly CheckBox _exportSpritesheetCheckBox;
        private readonly Button _autoCorrectButton;
        private readonly ComboBox _rotaryPreviewVariantComboBox;
        private readonly Button _createRotaryPreviewButton;
        private readonly SpriteKnobSlider _rotaryPreviewKnob;
        private readonly TextBlock _rotaryPreviewInfoTextBlock;
        private readonly TextBlock _rotaryPreviewValueTextBlock;
        private readonly Button _startRenderButton;
        private readonly Button _cancelButton;
        private readonly ProgressBar _exportProgressBar;
        private readonly TextBlock _statusTextBlock;
        private readonly TextBlock _scratchParityNoteTextBlock;
        private readonly OutputStrategyOption[] _outputStrategyOptions;
        private readonly PreviewVariantOption[] _previewVariantOptions;

        private CancellationTokenSource? _exportCts;
        private CancellationTokenSource? _rotaryPreviewCts;
        private string? _rotaryPreviewTempPath;
        private bool _isBuildingRotaryPreview;
        private bool _isRendering;
        private bool _isApplyingOutputStrategy;
        private bool CanUseGpuExport => _gpuViewport?.CanRenderOffscreen == true;

        public RenderSettingsWindow()
            : this(
                new KnobProject(),
                new OrientationDebug(),
                new ViewportCameraState(30f, -20f, 1f, SKPoint.Empty),
                null)
        {
        }

        public RenderSettingsWindow(KnobProject project, OrientationDebug orientation, ViewportCameraState cameraState, MetalViewport? gpuViewport)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
            _orientation = orientation ?? throw new ArgumentNullException(nameof(orientation));
            _cameraState = cameraState;
            _gpuViewport = gpuViewport;

            InitializeComponent();

            _settingsPanel = this.FindControl<Control>("SettingsPanel")
                ?? throw new InvalidOperationException("SettingsPanel not found.");
            _outputStrategyComboBox = this.FindControl<ComboBox>("OutputStrategyComboBox")
                ?? throw new InvalidOperationException("OutputStrategyComboBox not found.");
            _outputStrategyDescriptionTextBlock = this.FindControl<TextBlock>("OutputStrategyDescriptionTextBlock")
                ?? throw new InvalidOperationException("OutputStrategyDescriptionTextBlock not found.");
            _frameCountTextBox = this.FindControl<TextBox>("FrameCountTextBox")
                ?? throw new InvalidOperationException("FrameCountTextBox not found.");
            _resolutionComboBox = this.FindControl<ComboBox>("ResolutionComboBox")
                ?? throw new InvalidOperationException("ResolutionComboBox not found.");
            _supersampleComboBox = this.FindControl<ComboBox>("SupersampleComboBox")
                ?? throw new InvalidOperationException("SupersampleComboBox not found.");
            _paddingTextBox = this.FindControl<TextBox>("PaddingTextBox")
                ?? throw new InvalidOperationException("PaddingTextBox not found.");
            _cameraDistanceScaleTextBox = this.FindControl<TextBox>("CameraDistanceScaleTextBox")
                ?? throw new InvalidOperationException("CameraDistanceScaleTextBox not found.");
            _exportOrbitVariantsCheckBox = this.FindControl<CheckBox>("ExportOrbitVariantsCheckBox")
                ?? throw new InvalidOperationException("ExportOrbitVariantsCheckBox not found.");
            _orbitYawOffsetTextBox = this.FindControl<TextBox>("OrbitYawOffsetTextBox")
                ?? throw new InvalidOperationException("OrbitYawOffsetTextBox not found.");
            _orbitPitchOffsetTextBox = this.FindControl<TextBox>("OrbitPitchOffsetTextBox")
                ?? throw new InvalidOperationException("OrbitPitchOffsetTextBox not found.");
            _filterPresetComboBox = this.FindControl<ComboBox>("FilterPresetComboBox")
                ?? throw new InvalidOperationException("FilterPresetComboBox not found.");
            _baseNameTextBox = this.FindControl<TextBox>("BaseNameTextBox")
                ?? throw new InvalidOperationException("BaseNameTextBox not found.");
            _outputFolderTextBox = this.FindControl<TextBox>("OutputFolderTextBox")
                ?? throw new InvalidOperationException("OutputFolderTextBox not found.");
            _browseOutputButton = this.FindControl<Button>("BrowseOutputButton")
                ?? throw new InvalidOperationException("BrowseOutputButton not found.");
            _spritesheetLayoutComboBox = this.FindControl<ComboBox>("SpritesheetLayoutComboBox")
                ?? throw new InvalidOperationException("SpritesheetLayoutComboBox not found.");
            _exportFramesCheckBox = this.FindControl<CheckBox>("ExportFramesCheckBox")
                ?? throw new InvalidOperationException("ExportFramesCheckBox not found.");
            _exportSpritesheetCheckBox = this.FindControl<CheckBox>("ExportSpritesheetCheckBox")
                ?? throw new InvalidOperationException("ExportSpritesheetCheckBox not found.");
            _autoCorrectButton = this.FindControl<Button>("AutoCorrectButton")
                ?? throw new InvalidOperationException("AutoCorrectButton not found.");
            _rotaryPreviewVariantComboBox = this.FindControl<ComboBox>("RotaryPreviewVariantComboBox")
                ?? throw new InvalidOperationException("RotaryPreviewVariantComboBox not found.");
            _createRotaryPreviewButton = this.FindControl<Button>("CreateRotaryPreviewButton")
                ?? throw new InvalidOperationException("CreateRotaryPreviewButton not found.");
            _rotaryPreviewKnob = this.FindControl<SpriteKnobSlider>("RotaryPreviewKnob")
                ?? throw new InvalidOperationException("RotaryPreviewKnob not found.");
            _rotaryPreviewInfoTextBlock = this.FindControl<TextBlock>("RotaryPreviewInfoTextBlock")
                ?? throw new InvalidOperationException("RotaryPreviewInfoTextBlock not found.");
            _rotaryPreviewValueTextBlock = this.FindControl<TextBlock>("RotaryPreviewValueTextBlock")
                ?? throw new InvalidOperationException("RotaryPreviewValueTextBlock not found.");
            _startRenderButton = this.FindControl<Button>("StartRenderButton")
                ?? throw new InvalidOperationException("StartRenderButton not found.");
            _cancelButton = this.FindControl<Button>("CancelButton")
                ?? throw new InvalidOperationException("CancelButton not found.");
            _exportProgressBar = this.FindControl<ProgressBar>("ExportProgressBar")
                ?? throw new InvalidOperationException("ExportProgressBar not found.");
            _statusTextBlock = this.FindControl<TextBlock>("StatusTextBlock")
                ?? throw new InvalidOperationException("StatusTextBlock not found.");
            _scratchParityNoteTextBlock = this.FindControl<TextBlock>("ScratchParityNoteTextBlock")
                ?? throw new InvalidOperationException("ScratchParityNoteTextBlock not found.");

            _outputStrategyOptions = BuildOutputStrategyOptions();
            _outputStrategyComboBox.ItemsSource = _outputStrategyOptions;
            _previewVariantOptions = BuildPreviewVariantOptions();
            _rotaryPreviewVariantComboBox.ItemsSource = _previewVariantOptions;
            _rotaryPreviewVariantComboBox.SelectedIndex = 0;

            _resolutionComboBox.ItemsSource = new[] { "128", "192", "256", "384", "512", "1024", "2048" };

            _supersampleComboBox.ItemsSource = new[] { "1", "2", "3", "4" };

            _spritesheetLayoutComboBox.ItemsSource = Enum.GetValues<SpritesheetLayout>();

            _filterPresetComboBox.ItemsSource = Enum.GetValues<ExportFilterPreset>();

            _outputFolderTextBox.Text = GetDefaultOutputFolder();
            var defaultExportSettings = new KnobExportSettings();
            _exportOrbitVariantsCheckBox.IsChecked = defaultExportSettings.ExportOrbitVariants;
            _orbitYawOffsetTextBox.Text = defaultExportSettings.OrbitVariantYawOffsetDeg.ToString("0.###", CultureInfo.InvariantCulture);
            _orbitPitchOffsetTextBox.Text = defaultExportSettings.OrbitVariantPitchOffsetDeg.ToString("0.###", CultureInfo.InvariantCulture);
            _exportProgressBar.Value = 0d;
            _scratchParityNoteTextBlock.Text = "Scratch carve parity: export uses the GPU viewport path. Expect close match; tiny edge differences can appear on ultra-thin strokes or fallback hits.";
            _statusTextBlock.Text = CanUseGpuExport
                ? "Idle."
                : "GPU offscreen rendering is unavailable. Export is GPU-only.";

            _outputStrategyComboBox.SelectionChanged += OnOutputStrategySelectionChanged;
            _browseOutputButton.Click += OnBrowseOutputButtonClick;
            _autoCorrectButton.Click += OnAutoCorrectButtonClick;
            _createRotaryPreviewButton.Click += OnCreateRotaryPreviewButtonClick;
            _rotaryPreviewVariantComboBox.SelectionChanged += OnRotaryPreviewVariantSelectionChanged;
            _rotaryPreviewKnob.PropertyChanged += OnRotaryPreviewKnobPropertyChanged;
            _startRenderButton.Click += OnStartRenderButtonClick;
            _cancelButton.Click += OnCancelButtonClick;
            _exportSpritesheetCheckBox.IsCheckedChanged += OnExportSpritesheetCheckedChanged;
            _exportOrbitVariantsCheckBox.IsCheckedChanged += OnExportOrbitVariantsCheckedChanged;
            Closing += OnWindowClosing;
            WireLiveValidationHandlers();

            ApplyOutputStrategy(ExportOutputStrategies.Get(ExportOutputStrategy.JuceFilmstripBestDefault));
            UpdateSpritesheetLayoutEnabled();
            UpdateOrbitVariantControlsEnabled();
            UpdateStartRenderAvailability();
            _rotaryPreviewInfoTextBlock.Text = "Choose perspective, then click Create Rotary Preview. Interactive spin uses CPU UI drawing.";
        }

        private static OutputStrategyOption[] BuildOutputStrategyOptions()
        {
            var definitions = ExportOutputStrategies.All;
            OutputStrategyOption[] options = new OutputStrategyOption[definitions.Count];
            for (int i = 0; i < definitions.Count; i++)
            {
                options[i] = new OutputStrategyOption(definitions[i]);
            }

            return options;
        }

        private static PreviewVariantOption[] BuildPreviewVariantOptions()
        {
            return
            [
                new PreviewVariantOption(PreviewViewVariantKind.Primary, "Straight On"),
                new PreviewVariantOption(PreviewViewVariantKind.UnderLeft, "Under Left"),
                new PreviewVariantOption(PreviewViewVariantKind.UnderRight, "Under Right"),
                new PreviewVariantOption(PreviewViewVariantKind.OverLeft, "Over Left"),
                new PreviewVariantOption(PreviewViewVariantKind.OverRight, "Over Right")
            ];
        }

        private void OnOutputStrategySelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_isApplyingOutputStrategy)
            {
                return;
            }

            if (_outputStrategyComboBox.SelectedItem is OutputStrategyOption option)
            {
                ApplyOutputStrategy(option.Definition);
            }
        }

        private void ApplyOutputStrategy(ExportOutputStrategyDefinition definition)
        {
            _isApplyingOutputStrategy = true;
            try
            {
                for (int i = 0; i < _outputStrategyOptions.Length; i++)
                {
                    if (_outputStrategyOptions[i].Definition.Strategy == definition.Strategy)
                    {
                        _outputStrategyComboBox.SelectedItem = _outputStrategyOptions[i];
                        break;
                    }
                }

                string frameCountText = definition.FrameCount.ToString(CultureInfo.InvariantCulture);
                string resolutionText = definition.Resolution.ToString(CultureInfo.InvariantCulture);
                string supersampleText = definition.SupersampleScale.ToString(CultureInfo.InvariantCulture);

                _frameCountTextBox.Text = frameCountText;
                _resolutionComboBox.SelectedItem = resolutionText;
                _resolutionComboBox.Text = resolutionText;
                _supersampleComboBox.SelectedItem = supersampleText;
                _supersampleComboBox.Text = supersampleText;
                _paddingTextBox.Text = definition.Padding.ToString("0.###", CultureInfo.InvariantCulture);
                _cameraDistanceScaleTextBox.Text = definition.CameraDistanceScale.ToString("0.###", CultureInfo.InvariantCulture);
                _spritesheetLayoutComboBox.SelectedItem = definition.SpritesheetLayout;
                _filterPresetComboBox.SelectedItem = definition.FilterPreset;
                _exportFramesCheckBox.IsChecked = definition.ExportIndividualFrames;
                _exportSpritesheetCheckBox.IsChecked = definition.ExportSpritesheet;
                _outputStrategyDescriptionTextBlock.Text = definition.Description;
            }
            finally
            {
                _isApplyingOutputStrategy = false;
                UpdateSpritesheetLayoutEnabled();
                UpdateOrbitVariantControlsEnabled();
                UpdateStartRenderAvailability();
            }
        }

        private void WireLiveValidationHandlers()
        {
            _frameCountTextBox.TextChanged += OnLiveValidationTextChanged;
            _paddingTextBox.TextChanged += OnLiveValidationTextChanged;
            _cameraDistanceScaleTextBox.TextChanged += OnLiveValidationTextChanged;
            _orbitYawOffsetTextBox.TextChanged += OnLiveValidationTextChanged;
            _orbitPitchOffsetTextBox.TextChanged += OnLiveValidationTextChanged;
            _baseNameTextBox.TextChanged += OnLiveValidationTextChanged;
            _outputFolderTextBox.TextChanged += OnLiveValidationTextChanged;

            _resolutionComboBox.SelectionChanged += OnLiveValidationSelectionChanged;
            _supersampleComboBox.SelectionChanged += OnLiveValidationSelectionChanged;
            _spritesheetLayoutComboBox.SelectionChanged += OnLiveValidationSelectionChanged;
            _filterPresetComboBox.SelectionChanged += OnLiveValidationSelectionChanged;
            _outputStrategyComboBox.SelectionChanged += OnLiveValidationSelectionChanged;

            _resolutionComboBox.PropertyChanged += OnLiveValidationComboPropertyChanged;
            _supersampleComboBox.PropertyChanged += OnLiveValidationComboPropertyChanged;

            _exportFramesCheckBox.IsCheckedChanged += OnLiveValidationCheckedChanged;
            _exportSpritesheetCheckBox.IsCheckedChanged += OnLiveValidationCheckedChanged;
            _exportOrbitVariantsCheckBox.IsCheckedChanged += OnLiveValidationCheckedChanged;
        }

        private void OnLiveValidationTextChanged(object? sender, TextChangedEventArgs e)
        {
            UpdateStartRenderAvailability();
            MarkRotaryPreviewDirty();
        }

        private void OnLiveValidationSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            UpdateStartRenderAvailability();
            MarkRotaryPreviewDirty();
        }

        private void OnLiveValidationCheckedChanged(object? sender, RoutedEventArgs e)
        {
            UpdateStartRenderAvailability();
            MarkRotaryPreviewDirty();
        }

        private void OnLiveValidationComboPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == ComboBox.TextProperty)
            {
                UpdateStartRenderAvailability();
                MarkRotaryPreviewDirty();
            }
        }

        private static string GetDefaultOutputFolder()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrWhiteSpace(desktop))
            {
                return desktop;
            }

            return Directory.GetCurrentDirectory();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
