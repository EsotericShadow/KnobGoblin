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

        private async void OnBrowseOutputButtonClick(object? sender, RoutedEventArgs e)
        {
            FolderPickerOpenOptions options = new()
            {
                AllowMultiple = false,
                Title = "Select output folder"
            };

            if (Directory.Exists(_outputFolderTextBox.Text))
            {
                IStorageFolder? suggested = await StorageProvider.TryGetFolderFromPathAsync(_outputFolderTextBox.Text);
                if (suggested != null)
                {
                    options.SuggestedStartLocation = suggested;
                }
            }

            var folders = await StorageProvider.OpenFolderPickerAsync(options);
            if (folders.Count == 0)
            {
                return;
            }

            string? selectedPath = folders[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                selectedPath = folders[0].Path.LocalPath;
            }

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                _outputFolderTextBox.Text = selectedPath;
            }
        }

        private void OnAutoCorrectButtonClick(object? sender, RoutedEventArgs e)
        {
            string resolutionText = (_resolutionComboBox.Text ?? _resolutionComboBox.SelectedItem?.ToString() ?? string.Empty).Trim();
            if (!TryParseInt(resolutionText, MinResolution, MaxResolution, "Resolution", out int resolution, out string resolutionError))
            {
                _statusTextBlock.Text = $"Auto-correct skipped: {resolutionError}";
                return;
            }

            if (!TryParseInt(_frameCountTextBox.Text, MinFrameCount, MaxFrameCount, "FrameCount", out int frameCount, out string frameError))
            {
                _statusTextBlock.Text = $"Auto-correct skipped: {frameError}";
                return;
            }

            int supersample = Math.Clamp(GetMinimumSupersampleScaleForResolution(resolution), MinSupersample, MaxSupersample);
            int maxSupersampleForDimension = Math.Max(1, MaxResolution / Math.Max(1, resolution));
            supersample = Math.Min(supersample, Math.Clamp(maxSupersampleForDimension, MinSupersample, MaxSupersample));

            string supersampleText = supersample.ToString(CultureInfo.InvariantCulture);
            _supersampleComboBox.SelectedItem = supersampleText;
            _supersampleComboBox.Text = supersampleText;

            if (!TryParseFloat(_paddingTextBox.Text, 0f, float.MaxValue, "Padding", out float padding, out _))
            {
                padding = 0f;
                _paddingTextBox.Text = "0";
            }

            int paddingPx = Math.Max(0, (int)MathF.Round(padding));
            if (_exportSpritesheetCheckBox.IsChecked == true &&
                (_spritesheetLayoutComboBox.SelectedItem as SpritesheetLayout? ?? SpritesheetLayout.Horizontal) == SpritesheetLayout.Horizontal &&
                WouldHorizontalLayoutOverflow(frameCount, resolution, paddingPx))
            {
                _spritesheetLayoutComboBox.SelectedItem = SpritesheetLayout.Grid;
            }

            _filterPresetComboBox.SelectedItem = ExportFilterPreset.None;

            UpdateStartRenderAvailability();
            MarkRotaryPreviewDirty();
            _statusTextBlock.Text = $"Applied clean settings: {supersample}x supersampling with export-safe layout.";
        }

        private async void OnStartRenderButtonClick(object? sender, RoutedEventArgs e)
        {
            if (_isRendering)
            {
                return;
            }

            if (!CanUseGpuExport)
            {
                await ShowInfoDialogAsync(
                    "GPU export unavailable",
                    "Offscreen GPU rendering is unavailable, and export is currently GPU-only.");
                return;
            }

            if (!TryBuildRequest(out KnobExportSettings settings, out string outputRootFolder, out string baseName, out string validationError))
            {
                await ShowInfoDialogAsync("Invalid export settings", validationError);
                return;
            }

            _exportProgressBar.Value = 0d;
            _statusTextBlock.Text = "Preparing export...";
            SetRenderingState(true);

            _exportCts = new CancellationTokenSource();

            try
            {
                Func<int, int, ViewportCameraState, SKBitmap?> gpuFrameProvider = (width, height, cameraState) =>
                    Dispatcher.UIThread
                        .InvokeAsync(
                            () =>
                            {
                                if (_gpuViewport != null &&
                                    _gpuViewport.TryRenderFrameToBitmap(width, height, cameraState, out SKBitmap? frame))
                                {
                                    return frame;
                                }

                                return null;
                            },
                            DispatcherPriority.Render)
                        .GetAwaiter()
                        .GetResult();

                var exporter = new KnobExporter(_project, _orientation, _cameraState, gpuFrameProvider);
                var progress = new Progress<KnobExportProgress>(UpdateProgress);
                KnobExportResult result = await exporter.ExportAsync(
                    settings,
                    outputRootFolder,
                    baseName,
                    progress,
                    _exportCts.Token);

                _exportProgressBar.Value = 1d;
                _statusTextBlock.Text = "Export complete.";

                bool shouldOpen = await ShowConfirmDialogAsync(
                    "Export complete",
                    "Export complete.\nOpen folder?");
                if (shouldOpen)
                {
                    try
                    {
                        OpenFolder(result.OutputDirectory);
                    }
                    catch (Exception ex)
                    {
                        await ShowInfoDialogAsync("Open folder failed", ex.Message);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _statusTextBlock.Text = "Export cancelled.";
            }
            catch (Exception ex)
            {
                _statusTextBlock.Text = "Export failed.";
                await ShowInfoDialogAsync("Export failed", ex.Message);
            }
            finally
            {
                _exportCts?.Dispose();
                _exportCts = null;
                SetRenderingState(false);
            }
        }

        private void OnCancelButtonClick(object? sender, RoutedEventArgs e)
        {
            if (_isRendering)
            {
                _statusTextBlock.Text = "Cancelling...";
                _exportCts?.Cancel();
                return;
            }

            Close();
        }

        private void OnExportSpritesheetCheckedChanged(object? sender, RoutedEventArgs e)
        {
            UpdateSpritesheetLayoutEnabled();
            UpdateStartRenderAvailability();
        }

        private void OnExportOrbitVariantsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            UpdateOrbitVariantControlsEnabled();
            UpdateStartRenderAvailability();
        }

        private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
        {
            if (_isRendering)
            {
                _statusTextBlock.Text = "Cancelling...";
                _exportCts?.Cancel();
                e.Cancel = true;
                return;
            }

            _rotaryPreviewCts?.Cancel();
            _rotaryPreviewCts?.Dispose();
            _rotaryPreviewCts = null;
            CleanupRotaryPreviewTempPath();
        }

        private void UpdateProgress(KnobExportProgress progress)
        {
            double value = 0d;
            if (progress.TotalFrames > 0)
            {
                value = Math.Clamp((double)progress.CompletedFrames / progress.TotalFrames, 0d, 1d);
            }

            _exportProgressBar.Value = value;
            _statusTextBlock.Text = progress.Stage;
        }

        private void SetRenderingState(bool isRendering)
        {
            _isRendering = isRendering;
            _settingsPanel.IsEnabled = !isRendering;
            _autoCorrectButton.IsEnabled = !isRendering;
            _createRotaryPreviewButton.IsEnabled = !isRendering && !_isBuildingRotaryPreview;
            _rotaryPreviewVariantComboBox.IsEnabled = !isRendering && !_isBuildingRotaryPreview;
            _cancelButton.Content = isRendering ? "Cancel Render" : "Cancel";
            UpdateOrbitVariantControlsEnabled();

            if (!isRendering)
            {
                UpdateSpritesheetLayoutEnabled();
                UpdateStartRenderAvailability(preserveCurrentNonErrorStatus: true);
            }
            else
            {
                _startRenderButton.IsEnabled = false;
                _startRenderButton.Opacity = 0.55;
                ToolTip.SetTip(_startRenderButton, "Rendering in progress.");
            }
        }

        private void UpdateStartRenderAvailability(bool preserveCurrentNonErrorStatus = false)
        {
            if (_isRendering)
            {
                _startRenderButton.IsEnabled = false;
                _startRenderButton.Opacity = 0.55;
                ToolTip.SetTip(_startRenderButton, "Rendering in progress.");
                return;
            }

            const string gpuUnavailableMessage = "GPU offscreen rendering is unavailable. Export is GPU-only.";
            if (!CanUseGpuExport)
            {
                _startRenderButton.IsEnabled = false;
                _startRenderButton.Opacity = 0.55;
                _statusTextBlock.Text = gpuUnavailableMessage;
                ToolTip.SetTip(_startRenderButton, gpuUnavailableMessage);
                return;
            }

            if (!TryBuildRequest(out _, out _, out _, out string validationError))
            {
                string message = $"Cannot render: {validationError}";
                _startRenderButton.IsEnabled = false;
                _startRenderButton.Opacity = 0.55;
                _statusTextBlock.Text = message;
                ToolTip.SetTip(_startRenderButton, message);
                return;
            }

            _startRenderButton.IsEnabled = true;
            _startRenderButton.Opacity = 1.0;
            ToolTip.SetTip(_startRenderButton, "Ready to render.");

            if (preserveCurrentNonErrorStatus &&
                !string.IsNullOrWhiteSpace(_statusTextBlock.Text) &&
                !_statusTextBlock.Text.StartsWith("Cannot render:", StringComparison.Ordinal))
            {
                return;
            }

            _statusTextBlock.Text = "Ready to render.";
        }

        private void UpdateSpritesheetLayoutEnabled()
        {
            _spritesheetLayoutComboBox.IsEnabled = !_isRendering && _exportSpritesheetCheckBox.IsChecked == true;
        }

        private void UpdateOrbitVariantControlsEnabled()
        {
            bool enabled = !_isRendering && _exportOrbitVariantsCheckBox.IsChecked == true;
            _orbitYawOffsetTextBox.IsEnabled = enabled;
            _orbitPitchOffsetTextBox.IsEnabled = enabled;
        }

        private void MarkRotaryPreviewDirty()
        {
            if (_isBuildingRotaryPreview || !string.IsNullOrWhiteSpace(_rotaryPreviewTempPath))
            {
                _rotaryPreviewInfoTextBlock.Text = "Settings changed. Click Create Rotary Preview to refresh.";
            }
        }

        private void OnRotaryPreviewVariantSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            MarkRotaryPreviewDirty();
        }

        private void OnRotaryPreviewKnobPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == RangeBase.ValueProperty ||
                e.Property == RangeBase.MinimumProperty ||
                e.Property == RangeBase.MaximumProperty)
            {
                UpdateRotaryPreviewValueText();
            }
        }

        private async void OnCreateRotaryPreviewButtonClick(object? sender, RoutedEventArgs e)
        {
            if (_isBuildingRotaryPreview)
            {
                return;
            }

            if (!CanUseGpuExport)
            {
                _rotaryPreviewInfoTextBlock.Text = "Rotary preview unavailable: GPU offscreen rendering is unavailable.";
                return;
            }

            if (!TryBuildPreviewRequest(out PreviewRenderRequest request, out string validationError))
            {
                _rotaryPreviewInfoTextBlock.Text = $"Cannot build rotary preview: {validationError}";
                return;
            }

            var variant = _rotaryPreviewVariantComboBox.SelectedItem as PreviewVariantOption
                ?? _previewVariantOptions[0];

            _rotaryPreviewCts?.Cancel();
            _rotaryPreviewCts?.Dispose();
            _rotaryPreviewCts = new CancellationTokenSource();

            SetRotaryPreviewBusy(true, $"Generating {variant.DisplayName} preview...");

            try
            {
                RotaryPreviewSheet previewSheet = await BuildRotaryPreviewSheetAsync(request, variant, _rotaryPreviewCts.Token);
                CleanupRotaryPreviewTempPath();
                _rotaryPreviewTempPath = previewSheet.SpriteSheetPath;
                ApplyRotaryPreviewSheet(previewSheet);
                _rotaryPreviewInfoTextBlock.Text = $"Ready: {variant.DisplayName}, {previewSheet.FrameCount} frames at {previewSheet.FrameSizePx}px. Drag to spin.";
            }
            catch (OperationCanceledException)
            {
                _rotaryPreviewInfoTextBlock.Text = "Rotary preview canceled.";
            }
            catch (Exception ex)
            {
                _rotaryPreviewInfoTextBlock.Text = $"Rotary preview failed: {ex.Message}";
            }
            finally
            {
                _rotaryPreviewCts?.Dispose();
                _rotaryPreviewCts = null;
                SetRotaryPreviewBusy(false);
            }
        }

        private void SetRotaryPreviewBusy(bool isBusy, string? status = null)
        {
            _isBuildingRotaryPreview = isBusy;
            _createRotaryPreviewButton.IsEnabled = !isBusy && !_isRendering;
            _rotaryPreviewVariantComboBox.IsEnabled = !isBusy && !_isRendering;
            if (!string.IsNullOrWhiteSpace(status))
            {
                _rotaryPreviewInfoTextBlock.Text = status;
            }
        }

        private async Task<RotaryPreviewSheet> BuildRotaryPreviewSheetAsync(
            PreviewRenderRequest request,
            PreviewVariantOption variant,
            CancellationToken cancellationToken)
        {
            if (_gpuViewport == null)
            {
                throw new InvalidOperationException("GPU viewport is unavailable.");
            }

            var defaults = new KnobExportSettings();
            float yawOffsetDeg = defaults.OrbitVariantYawOffsetDeg;
            float pitchOffsetDeg = defaults.OrbitVariantPitchOffsetDeg;
            TryParseFloat(_orbitYawOffsetTextBox.Text, MinOrbitOffsetDeg, MaxOrbitYawOffsetDeg, "Orbit yaw offset", out yawOffsetDeg, out _);
            TryParseFloat(_orbitPitchOffsetTextBox.Text, MinOrbitOffsetDeg, MaxOrbitPitchOffsetDeg, "Orbit pitch offset", out pitchOffsetDeg, out _);

            ViewportCameraState cameraState = ApplyPreviewVariant(
                request.CameraState,
                variant.Kind,
                yawOffsetDeg,
                pitchOffsetDeg);

            int frameCount = request.FrameCount;
            int resolution = request.Resolution;
            int columns = (int)Math.Ceiling(Math.Sqrt(frameCount));
            int rows = (int)Math.Ceiling(frameCount / (double)columns);

            using var sheetBitmap = new SKBitmap(new SKImageInfo(
                checked(columns * resolution),
                checked(rows * resolution),
                SKColorType.Bgra8888,
                SKAlphaType.Premul));
            using var sheetCanvas = new SKCanvas(sheetBitmap);
            sheetCanvas.Clear(new SKColor(0, 0, 0, 0));

            using var frameBitmap = new SKBitmap(new SKImageInfo(
                resolution,
                resolution,
                SKColorType.Bgra8888,
                SKAlphaType.Premul));
            using var frameCanvas = new SKCanvas(frameBitmap);
            using var downsamplePaint = new SKPaint
            {
                BlendMode = SKBlendMode.Src,
                IsAntialias = true,
                IsDither = true
            };
            using var sheetPaint = new SKPaint
            {
                BlendMode = SKBlendMode.Src,
                IsAntialias = false
            };
            SKSamplingOptions downsampleSampling = new(new SKCubicResampler(1f / 3f, 1f / 3f));
            SKSamplingOptions directSampling = new(SKFilterMode.Linear, SKMipmapMode.None);

            ModelRotationSnapshot[] snapshots = await Dispatcher.UIThread.InvokeAsync(
                CaptureModelRotations,
                DispatcherPriority.Background);

            float angleStep = 2f * MathF.PI / frameCount;
            int progressStep = Math.Max(1, frameCount / 8);
            try
            {
                int fittingSamples = Math.Clamp(Math.Min(frameCount, 12), 4, 12);
                cameraState = await FitRotaryPreviewCameraAsync(request, cameraState, snapshots, fittingSamples, cancellationToken);

                for (int i = 0; i < frameCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (i == 0 || i == frameCount - 1 || ((i + 1) % progressStep) == 0)
                    {
                        int progress = i + 1;
                        await Dispatcher.UIThread.InvokeAsync(
                            () => _rotaryPreviewInfoTextBlock.Text = $"Generating {variant.DisplayName} preview... {progress}/{frameCount}",
                            DispatcherPriority.Background);
                    }

                    float angle = i * angleStep;
                    await Dispatcher.UIThread.InvokeAsync(
                        () => ApplyModelRotationDelta(snapshots, angle),
                        DispatcherPriority.Render);

                    SKBitmap? gpuFrame = await Dispatcher.UIThread.InvokeAsync(
                        () =>
                        {
                            if (_gpuViewport.TryRenderFrameToBitmap(
                                request.RenderResolution,
                                request.RenderResolution,
                                cameraState,
                                out SKBitmap? frame))
                            {
                                return frame;
                            }

                            return null;
                        },
                        DispatcherPriority.Render);

                    if (gpuFrame == null)
                    {
                        throw new InvalidOperationException("GPU frame capture failed while building rotary preview.");
                    }

                    using (gpuFrame)
                    using (SKImage sourceImage = SKImage.FromBitmap(gpuFrame))
                    {
                        frameCanvas.Clear(new SKColor(0, 0, 0, 0));
                        if (request.SupersampleScale > 1 ||
                            gpuFrame.Width != resolution ||
                            gpuFrame.Height != resolution)
                        {
                            frameCanvas.DrawImage(
                                sourceImage,
                                new SKRect(0, 0, gpuFrame.Width, gpuFrame.Height),
                                new SKRect(0, 0, resolution, resolution),
                                downsampleSampling,
                                downsamplePaint);
                        }
                        else
                        {
                            frameCanvas.DrawImage(
                                sourceImage,
                                new SKRect(0, 0, resolution, resolution),
                                directSampling,
                                downsamplePaint);
                        }
                    }

                    int col = i % columns;
                    int row = i / columns;
                    sheetCanvas.DrawBitmap(frameBitmap, col * resolution, row * resolution, sheetPaint);
                }
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(
                    () => RestoreModelRotations(snapshots),
                    DispatcherPriority.Render);
            }

            string outputPath = CreateRotaryPreviewTempPath();
            using SKData pngData = sheetBitmap.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream outputStream = File.Create(outputPath);
            pngData.SaveTo(outputStream);

            return new RotaryPreviewSheet(outputPath, frameCount, columns, resolution);
        }

        private static ViewportCameraState ApplyPreviewVariant(
            ViewportCameraState baseState,
            PreviewViewVariantKind kind,
            float yawOffsetDeg,
            float pitchOffsetDeg)
        {
            float yaw = MathF.Abs(yawOffsetDeg);
            float pitch = MathF.Abs(pitchOffsetDeg);

            float yawDelta = 0f;
            float pitchDelta = 0f;
            switch (kind)
            {
                case PreviewViewVariantKind.UnderLeft:
                    yawDelta = -yaw;
                    pitchDelta = pitch;
                    break;
                case PreviewViewVariantKind.UnderRight:
                    yawDelta = yaw;
                    pitchDelta = pitch;
                    break;
                case PreviewViewVariantKind.OverLeft:
                    yawDelta = -yaw;
                    pitchDelta = -pitch;
                    break;
                case PreviewViewVariantKind.OverRight:
                    yawDelta = yaw;
                    pitchDelta = -pitch;
                    break;
            }

            float resultYaw = baseState.OrbitYawDeg + yawDelta;
            float resultPitch = Math.Clamp(baseState.OrbitPitchDeg + pitchDelta, -85f, 85f);
            return new ViewportCameraState(resultYaw, resultPitch, baseState.Zoom, baseState.PanPx);
        }

        private async Task<ViewportCameraState> FitRotaryPreviewCameraAsync(
            PreviewRenderRequest request,
            ViewportCameraState cameraState,
            ModelRotationSnapshot[] snapshots,
            int sampleCount,
            CancellationToken cancellationToken)
        {
            if (_gpuViewport == null || snapshots.Length == 0)
            {
                return cameraState;
            }

            float marginPx = MathF.Max(4f, request.Resolution * 0.04f);
            const int maxFitIterations = 5;

            try
            {
                for (int iteration = 0; iteration < maxFitIterations; iteration++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    float fitScale = 1f;
                    for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        float angle = (2f * MathF.PI * sampleIndex) / sampleCount;

                        await Dispatcher.UIThread.InvokeAsync(
                            () => ApplyModelRotationDelta(snapshots, angle),
                            DispatcherPriority.Render);

                        SKBitmap? sampleBitmap = await Dispatcher.UIThread.InvokeAsync(
                            () =>
                            {
                                if (_gpuViewport.TryRenderFrameToBitmap(
                                    request.Resolution,
                                    request.Resolution,
                                    cameraState,
                                    out SKBitmap? frame))
                                {
                                    return frame;
                                }

                                return null;
                            },
                            DispatcherPriority.Render);

                        if (sampleBitmap == null)
                        {
                            continue;
                        }

                        using (sampleBitmap)
                        {
                            if (!TryGetOpaqueBounds(sampleBitmap, 2, out PixelAlphaBounds bounds))
                            {
                                continue;
                            }

                            float frameMin = 0f;
                            float frameMaxX = request.Resolution - 1f;
                            float frameMaxY = request.Resolution - 1f;
                            float centerX = (frameMin + frameMaxX) * 0.5f;
                            float centerY = (frameMin + frameMaxY) * 0.5f;

                            float availableLeft = MathF.Max(1f, centerX - marginPx);
                            float availableRight = MathF.Max(1f, (frameMaxX - marginPx) - centerX);
                            float availableTop = MathF.Max(1f, centerY - marginPx);
                            float availableBottom = MathF.Max(1f, (frameMaxY - marginPx) - centerY);

                            float usedLeft = MathF.Max(1f, centerX - bounds.MinX);
                            float usedRight = MathF.Max(1f, bounds.MaxX - centerX);
                            float usedTop = MathF.Max(1f, centerY - bounds.MinY);
                            float usedBottom = MathF.Max(1f, bounds.MaxY - centerY);

                            float scaleLeft = availableLeft / usedLeft;
                            float scaleRight = availableRight / usedRight;
                            float scaleTop = availableTop / usedTop;
                            float scaleBottom = availableBottom / usedBottom;
                            float frameScale = MathF.Min(MathF.Min(scaleLeft, scaleRight), MathF.Min(scaleTop, scaleBottom));
                            fitScale = MathF.Min(fitScale, frameScale);
                        }
                    }

                    if (fitScale >= 0.998f)
                    {
                        break;
                    }

                    float appliedScale = Math.Clamp(fitScale * 0.985f, 0.65f, 0.995f);
                    cameraState = cameraState with
                    {
                        Zoom = MathF.Max(0.2f, cameraState.Zoom * appliedScale)
                    };
                }
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(
                    () => RestoreModelRotations(snapshots),
                    DispatcherPriority.Render);
            }

            return cameraState;
        }

        private static bool TryGetOpaqueBounds(SKBitmap bitmap, byte alphaThreshold, out PixelAlphaBounds bounds)
        {
            int minX = bitmap.Width;
            int minY = bitmap.Height;
            int maxX = -1;
            int maxY = -1;

            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y).Alpha <= alphaThreshold)
                    {
                        continue;
                    }

                    if (x < minX)
                    {
                        minX = x;
                    }

                    if (y < minY)
                    {
                        minY = y;
                    }

                    if (x > maxX)
                    {
                        maxX = x;
                    }

                    if (y > maxY)
                    {
                        maxY = y;
                    }
                }
            }

            if (maxX < minX || maxY < minY)
            {
                bounds = default;
                return false;
            }

            bounds = new PixelAlphaBounds(minX, minY, maxX, maxY);
            return true;
        }

        private ModelRotationSnapshot[] CaptureModelRotations()
        {
            return _project.SceneRoot.Children
                .OfType<ModelNode>()
                .Select(model => new ModelRotationSnapshot(model, model.RotationRadians))
                .ToArray();
        }

        private void ApplyModelRotationDelta(ModelRotationSnapshot[] snapshots, float angleDeltaRadians)
        {
            for (int i = 0; i < snapshots.Length; i++)
            {
                snapshots[i].Model.RotationRadians = snapshots[i].RotationRadians + angleDeltaRadians;
            }
        }

        private void RestoreModelRotations(ModelRotationSnapshot[] snapshots)
        {
            for (int i = 0; i < snapshots.Length; i++)
            {
                snapshots[i].Model.RotationRadians = snapshots[i].RotationRadians;
            }
        }

        private void ApplyRotaryPreviewSheet(RotaryPreviewSheet sheet)
        {
            _rotaryPreviewKnob.SpriteSheetPath = sheet.SpriteSheetPath;
            _rotaryPreviewKnob.FrameCount = sheet.FrameCount;
            _rotaryPreviewKnob.ColumnCount = sheet.ColumnCount;
            _rotaryPreviewKnob.FrameWidth = sheet.FrameSizePx;
            _rotaryPreviewKnob.FrameHeight = sheet.FrameSizePx;
            _rotaryPreviewKnob.FramePadding = 0;
            _rotaryPreviewKnob.FrameStartX = 0;
            _rotaryPreviewKnob.FrameStartY = 0;
            _rotaryPreviewKnob.Minimum = 0d;
            _rotaryPreviewKnob.Maximum = Math.Max(1d, sheet.FrameCount - 1d);
            _rotaryPreviewKnob.KnobDiameter = sheet.FrameSizePx;
            _rotaryPreviewKnob.Value = 0d;
            _rotaryPreviewKnob.IsEnabled = true;
            UpdateRotaryPreviewValueText();
        }

        private void UpdateRotaryPreviewValueText()
        {
            int maxFrame = (int)Math.Max(1, Math.Round(_rotaryPreviewKnob.Maximum));
            int frameIndex = (int)Math.Round(Math.Clamp(_rotaryPreviewKnob.Value, _rotaryPreviewKnob.Minimum, _rotaryPreviewKnob.Maximum)) + 1;
            frameIndex = Math.Clamp(frameIndex, 1, maxFrame + 1);
            _rotaryPreviewValueTextBlock.Text = $"Frame {frameIndex} / {maxFrame + 1}";
        }

        private static string CreateRotaryPreviewTempPath()
        {
            string folder = Path.Combine(Path.GetTempPath(), "KnobForge", "rotary-preview");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, $"rotary_preview_{Guid.NewGuid():N}.png");
        }

        private void CleanupRotaryPreviewTempPath()
        {
            if (string.IsNullOrWhiteSpace(_rotaryPreviewTempPath))
            {
                return;
            }

            try
            {
                if (File.Exists(_rotaryPreviewTempPath))
                {
                    File.Delete(_rotaryPreviewTempPath);
                }
            }
            catch
            {
            }

            _rotaryPreviewTempPath = null;
        }

        private bool TryBuildPreviewRequest(out PreviewRenderRequest request, out string error)
        {
            request = default;
            error = string.Empty;

            if (!TryParseInt(_frameCountTextBox.Text, MinFrameCount, MaxFrameCount, "FrameCount", out int frameCount, out error))
            {
                return false;
            }

            string resolutionText = (_resolutionComboBox.Text ?? _resolutionComboBox.SelectedItem?.ToString() ?? string.Empty).Trim();
            if (!TryParseInt(resolutionText, MinResolution, MaxResolution, "Resolution", out int resolution, out error))
            {
                return false;
            }

            string supersampleText = (_supersampleComboBox.Text ?? _supersampleComboBox.SelectedItem?.ToString() ?? string.Empty).Trim();
            if (!TryParseInt(supersampleText, MinSupersample, MaxSupersample, "Supersampling", out int supersampleScale, out error))
            {
                return false;
            }

            int minimumSupersample = GetMinimumSupersampleScaleForResolution(resolution);
            if (supersampleScale < minimumSupersample)
            {
                error = $"Supersampling must be at least {minimumSupersample}x at {resolution}px for clean output.";
                return false;
            }

            int renderResolution = checked(resolution * supersampleScale);
            if (renderResolution > MaxResolution)
            {
                error = $"Resolution x Supersampling exceeds max {MaxResolution}px.";
                return false;
            }

            int columns = (int)Math.Ceiling(Math.Sqrt(frameCount));
            int rows = (int)Math.Ceiling(frameCount / (double)columns);
            long sheetWidth = (long)columns * resolution;
            long sheetHeight = (long)rows * resolution;
            if (sheetWidth > MaxResolution || sheetHeight > MaxResolution)
            {
                error = $"Rotary preview sheet would be {sheetWidth}x{sheetHeight}px. Reduce frame count or resolution for preview.";
                return false;
            }

            if (!TryParseFloat(_paddingTextBox.Text, 0f, float.MaxValue, "Padding", out float padding, out error))
            {
                return false;
            }

            if (!TryParseFloat(_cameraDistanceScaleTextBox.Text, 0.0001f, float.MaxValue, "CameraDistanceScale", out float cameraDistanceScale, out error))
            {
                return false;
            }

            float referenceRadius = GetSceneReferenceRadius();
            ViewportCameraState previewCamera = BuildPreviewCameraState(
                referenceRadius,
                resolution,
                renderResolution,
                padding,
                cameraDistanceScale);

            request = new PreviewRenderRequest(
                frameCount,
                resolution,
                supersampleScale,
                renderResolution,
                padding,
                previewCamera);
            return true;
        }

        private ViewportCameraState BuildPreviewCameraState(
            float referenceRadius,
            int outputResolution,
            int renderResolution,
            float padding,
            float cameraDistanceScale)
        {
            float resolutionScale = renderResolution / (float)Math.Max(1, outputResolution);
            float zoom = Math.Clamp(_cameraState.Zoom * resolutionScale, 0.2f, 32f);
            SKPoint pan = new(_cameraState.PanPx.X * resolutionScale, _cameraState.PanPx.Y * resolutionScale);
            zoom = MathF.Min(zoom, ComputeSafeZoomForFrame(referenceRadius, renderResolution, padding * resolutionScale, pan));

            if (zoom <= 0.0001f)
            {
                float contentPixels = MathF.Max(1f, renderResolution - (MathF.Max(0f, padding) * 2f));
                float fallbackZoom = contentPixels / MathF.Max(1f, MathF.Max(referenceRadius, cameraDistanceScale) * 2f);
                zoom = Math.Clamp(fallbackZoom, 0.2f, 32f);
            }

            return new ViewportCameraState(_cameraState.OrbitYawDeg, _cameraState.OrbitPitchDeg, zoom, pan);
        }

        private float GetSceneReferenceRadius()
        {
            float maxReferenceRadius = 1f;
            var previewRenderer = new PreviewRenderer(_project);
            maxReferenceRadius = MathF.Max(maxReferenceRadius, previewRenderer.GetMaxModelReferenceRadius());

            MetalMesh? mesh = MetalMeshBuilder.TryBuildFromProject(_project);
            if (mesh != null)
            {
                maxReferenceRadius = MathF.Max(maxReferenceRadius, mesh.ReferenceRadius);
            }

            CollarMesh? collarMesh = CollarMeshBuilder.TryBuildFromProject(_project);
            if (collarMesh != null)
            {
                maxReferenceRadius = MathF.Max(maxReferenceRadius, collarMesh.ReferenceRadius);
            }

            return maxReferenceRadius;
        }

        private static float ComputeSafeZoomForFrame(
            float referenceRadius,
            int renderResolution,
            float paddingPx,
            SKPoint panPx)
        {
            float radius = MathF.Max(1f, referenceRadius);
            float halfWidthAvailable = MathF.Max(1f, (renderResolution * 0.5f) - paddingPx - MathF.Abs(panPx.X));
            float halfHeightAvailable = MathF.Max(1f, (renderResolution * 0.5f) - paddingPx - MathF.Abs(panPx.Y));
            float halfSpan = MathF.Min(halfWidthAvailable, halfHeightAvailable);
            return MathF.Max(0.2f, (halfSpan * 0.96f) / radius);
        }

        private static bool WouldHorizontalLayoutOverflow(int frameCount, int resolution, int paddingPx)
        {
            long width = ((long)frameCount * resolution) + (((long)frameCount + 1L) * paddingPx);
            return width > MaxResolution;
        }

        private bool TryBuildRequest(
            out KnobExportSettings settings,
            out string outputRootFolder,
            out string baseName,
            out string error)
        {
            settings = null!;
            outputRootFolder = string.Empty;
            baseName = string.Empty;
            error = string.Empty;

            if (!TryParseInt(_frameCountTextBox.Text, MinFrameCount, MaxFrameCount, "FrameCount", out int frameCount, out error))
            {
                return false;
            }

            string resolutionText = (_resolutionComboBox.Text ?? _resolutionComboBox.SelectedItem?.ToString() ?? string.Empty).Trim();
            if (!TryParseInt(resolutionText, MinResolution, MaxResolution, "Resolution", out int resolution, out error))
            {
                return false;
            }

            string supersampleText = (_supersampleComboBox.Text ?? _supersampleComboBox.SelectedItem?.ToString() ?? string.Empty).Trim();
            if (!TryParseInt(supersampleText, MinSupersample, MaxSupersample, "Supersampling", out int supersampleScale, out error))
            {
                return false;
            }

            int minimumSupersample = GetMinimumSupersampleScaleForResolution(resolution);
            if (supersampleScale < minimumSupersample)
            {
                error = $"Supersampling {supersampleScale}x is too low for {resolution}px output and will cause visible aliasing. Use {minimumSupersample}x or higher.";
                return false;
            }

            if (!TryParseFloat(_paddingTextBox.Text, 0f, float.MaxValue, "Padding", out float padding, out error))
            {
                return false;
            }

            if (!TryParseFloat(_cameraDistanceScaleTextBox.Text, 0.0001f, float.MaxValue, "CameraDistanceScale", out float cameraDistanceScale, out error))
            {
                return false;
            }

            bool exportOrbitVariants = _exportOrbitVariantsCheckBox.IsChecked == true;
            var orbitVariantDefaults = new KnobExportSettings();
            float orbitYawOffsetDeg = orbitVariantDefaults.OrbitVariantYawOffsetDeg;
            float orbitPitchOffsetDeg = orbitVariantDefaults.OrbitVariantPitchOffsetDeg;

            if (exportOrbitVariants)
            {
                if (!TryParseFloat(
                        _orbitYawOffsetTextBox.Text,
                        MinOrbitOffsetDeg,
                        MaxOrbitYawOffsetDeg,
                        "Orbit yaw offset",
                        out orbitYawOffsetDeg,
                        out error))
                {
                    return false;
                }

                if (!TryParseFloat(
                        _orbitPitchOffsetTextBox.Text,
                        MinOrbitOffsetDeg,
                        MaxOrbitPitchOffsetDeg,
                        "Orbit pitch offset",
                        out orbitPitchOffsetDeg,
                        out error))
                {
                    return false;
                }
            }

            bool exportFrames = _exportFramesCheckBox.IsChecked == true;
            bool exportSpritesheet = _exportSpritesheetCheckBox.IsChecked == true;
            if (!exportFrames && !exportSpritesheet)
            {
                error = "Enable at least one output type: frames and/or spritesheet.";
                return false;
            }

            baseName = (_baseNameTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(baseName))
            {
                error = "Base Name is required.";
                return false;
            }

            if (baseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                error = "Base Name contains invalid file name characters.";
                return false;
            }

            outputRootFolder = (_outputFolderTextBox.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(outputRootFolder))
            {
                error = "Output folder is required.";
                return false;
            }

            try
            {
                outputRootFolder = Path.GetFullPath(outputRootFolder);
            }
            catch (Exception ex)
            {
                error = $"Output folder path is invalid: {ex.Message}";
                return false;
            }

            if (!Directory.Exists(outputRootFolder))
            {
                error = "Selected output folder does not exist.";
                return false;
            }

            var selectedLayout = _spritesheetLayoutComboBox.SelectedItem as SpritesheetLayout?;
            SpritesheetLayout layout = selectedLayout ?? SpritesheetLayout.Horizontal;

            var selectedFilter = _filterPresetComboBox.SelectedItem as ExportFilterPreset?;
            ExportFilterPreset filterPreset = selectedFilter ?? ExportFilterPreset.None;

            ExportOutputStrategy strategy = _outputStrategyComboBox.SelectedItem is OutputStrategyOption option
                ? option.Definition.Strategy
                : ExportOutputStrategy.JuceFilmstripBestDefault;

            settings = new KnobExportSettings
            {
                Strategy = strategy,
                FrameCount = frameCount,
                Resolution = resolution,
                SupersampleScale = supersampleScale,
                ExportIndividualFrames = exportFrames,
                ExportSpritesheet = exportSpritesheet,
                SpritesheetLayout = layout,
                Padding = padding,
                CameraDistanceScale = cameraDistanceScale,
                FilterPreset = filterPreset,
                ExportOrbitVariants = exportOrbitVariants,
                OrbitVariantYawOffsetDeg = orbitYawOffsetDeg,
                OrbitVariantPitchOffsetDeg = orbitPitchOffsetDeg
            };

            return true;
        }

        private static int GetMinimumSupersampleScaleForResolution(int resolution)
        {
            if (resolution <= 128)
            {
                return 4;
            }

            if (resolution <= 512)
            {
                return 2;
            }

            return 1;
        }

        private readonly record struct PreviewRenderRequest(
            int FrameCount,
            int Resolution,
            int SupersampleScale,
            int RenderResolution,
            float Padding,
            ViewportCameraState CameraState);

        private readonly record struct RotaryPreviewSheet(
            string SpriteSheetPath,
            int FrameCount,
            int ColumnCount,
            int FrameSizePx);

        private readonly record struct ModelRotationSnapshot(
            ModelNode Model,
            float RotationRadians);

        private readonly record struct PixelAlphaBounds(
            int MinX,
            int MinY,
            int MaxX,
            int MaxY);

        private enum PreviewViewVariantKind
        {
            Primary,
            UnderLeft,
            UnderRight,
            OverLeft,
            OverRight
        }

        private sealed class PreviewVariantOption
        {
            public PreviewVariantOption(PreviewViewVariantKind kind, string displayName)
            {
                Kind = kind;
                DisplayName = displayName;
            }

            public PreviewViewVariantKind Kind { get; }

            public string DisplayName { get; }

            public override string ToString()
            {
                return DisplayName;
            }
        }

        private sealed class OutputStrategyOption
        {
            public OutputStrategyOption(ExportOutputStrategyDefinition definition)
            {
                Definition = definition;
            }

            public ExportOutputStrategyDefinition Definition { get; }

            public override string ToString()
            {
                return Definition.DisplayName;
            }
        }

        private static bool TryParseInt(
            string? text,
            int minInclusive,
            int maxInclusive,
            string fieldName,
            out int value,
            out string error)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                error = $"{fieldName} must be an integer.";
                return false;
            }

            if (value < minInclusive || value > maxInclusive)
            {
                error = $"{fieldName} must be between {minInclusive} and {maxInclusive}.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool TryParseFloat(
            string? text,
            float minInclusive,
            float maxInclusive,
            string fieldName,
            out float value,
            out string error)
        {
            if (!float.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value))
            {
                error = $"{fieldName} must be a number.";
                return false;
            }

            if (value < minInclusive || value > maxInclusive)
            {
                error = $"{fieldName} must be between {minInclusive} and {maxInclusive}.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private async Task ShowInfoDialogAsync(string title, string message)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 440,
                Height = 180,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var okButton = new Button
            {
                Content = "OK",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                MinWidth = 90
            };
            okButton.Click += (_, _) => dialog.Close();

            dialog.Content = new Grid
            {
                Margin = new Thickness(16),
                RowDefinitions = new RowDefinitions("*,Auto"),
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { okButton },
                        [Grid.RowProperty] = 1
                    }
                }
            };

            await dialog.ShowDialog(this);
        }

        private async Task<bool> ShowConfirmDialogAsync(string title, string message)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 460,
                Height = 190,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var openButton = new Button
            {
                Content = "Open Folder",
                MinWidth = 110
            };
            var closeButton = new Button
            {
                Content = "Close",
                MinWidth = 90
            };

            openButton.Click += (_, _) => dialog.Close(true);
            closeButton.Click += (_, _) => dialog.Close(false);

            dialog.Content = new Grid
            {
                Margin = new Thickness(16),
                RowDefinitions = new RowDefinitions("*,Auto"),
                Children =
                {
                    new TextBlock
                    {
                        Text = message,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { closeButton, openButton },
                        [Grid.RowProperty] = 1
                    }
                }
            };

            return await dialog.ShowDialog<bool>(this);
        }

        private static void OpenFolder(string folderPath)
        {
            ProcessStartInfo startInfo;
            if (OperatingSystem.IsMacOS())
            {
                startInfo = new ProcessStartInfo("open");
            }
            else if (OperatingSystem.IsWindows())
            {
                startInfo = new ProcessStartInfo("explorer");
            }
            else
            {
                startInfo = new ProcessStartInfo("xdg-open");
            }

            startInfo.ArgumentList.Add(folderPath);
            startInfo.UseShellExecute = false;
            Process.Start(startInfo);
        }
    }
}
