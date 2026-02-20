using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using KnobForge.App.Controls;
using KnobForge.Core;
using KnobForge.Core.Export;
using KnobForge.Rendering;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
        private readonly ComboBox _filterPresetComboBox;
        private readonly TextBox _baseNameTextBox;
        private readonly TextBox _outputFolderTextBox;
        private readonly Button _browseOutputButton;
        private readonly ComboBox _spritesheetLayoutComboBox;
        private readonly CheckBox _exportFramesCheckBox;
        private readonly CheckBox _exportSpritesheetCheckBox;
        private readonly Button _startRenderButton;
        private readonly Button _cancelButton;
        private readonly ProgressBar _exportProgressBar;
        private readonly TextBlock _statusTextBlock;
        private readonly TextBlock _scratchParityNoteTextBlock;
        private readonly OutputStrategyOption[] _outputStrategyOptions;

        private CancellationTokenSource? _exportCts;
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

            _resolutionComboBox.ItemsSource = new[] { "128", "192", "256", "384", "512", "1024", "2048" };

            _supersampleComboBox.ItemsSource = new[] { "1", "2", "3", "4" };

            _spritesheetLayoutComboBox.ItemsSource = Enum.GetValues<SpritesheetLayout>();

            _filterPresetComboBox.ItemsSource = Enum.GetValues<ExportFilterPreset>();

            _outputFolderTextBox.Text = GetDefaultOutputFolder();
            _exportProgressBar.Value = 0d;
            _scratchParityNoteTextBlock.Text = "Scratch carve parity: export uses the GPU viewport path. Expect close match; tiny edge differences can appear on ultra-thin strokes or fallback hits.";
            _statusTextBlock.Text = CanUseGpuExport
                ? "Idle."
                : "GPU offscreen rendering is unavailable. Export is GPU-only.";

            _outputStrategyComboBox.SelectionChanged += OnOutputStrategySelectionChanged;
            _browseOutputButton.Click += OnBrowseOutputButtonClick;
            _startRenderButton.Click += OnStartRenderButtonClick;
            _cancelButton.Click += OnCancelButtonClick;
            _exportSpritesheetCheckBox.IsCheckedChanged += OnExportSpritesheetCheckedChanged;
            Closing += OnWindowClosing;

            ApplyOutputStrategy(ExportOutputStrategies.Get(ExportOutputStrategy.JuceFilmstripBestDefault));
            UpdateSpritesheetLayoutEnabled();
            _startRenderButton.IsEnabled = CanUseGpuExport;
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
        }

        private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
        {
            if (!_isRendering)
            {
                return;
            }

            _statusTextBlock.Text = "Cancelling...";
            _exportCts?.Cancel();
            e.Cancel = true;
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
            _startRenderButton.IsEnabled = !isRendering && CanUseGpuExport;
            _cancelButton.Content = isRendering ? "Cancel Render" : "Cancel";

            if (!isRendering)
            {
                UpdateSpritesheetLayoutEnabled();
                if (!CanUseGpuExport)
                {
                    _statusTextBlock.Text = "GPU offscreen rendering is unavailable. Export is GPU-only.";
                }
            }
        }

        private void UpdateSpritesheetLayoutEnabled()
        {
            _spritesheetLayoutComboBox.IsEnabled = !_isRendering && _exportSpritesheetCheckBox.IsChecked == true;
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

            if (!TryParseFloat(_paddingTextBox.Text, 0f, float.MaxValue, "Padding", out float padding, out error))
            {
                return false;
            }

            if (!TryParseFloat(_cameraDistanceScaleTextBox.Text, 0.0001f, float.MaxValue, "CameraDistanceScale", out float cameraDistanceScale, out error))
            {
                return false;
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
                FilterPreset = filterPreset
            };

            return true;
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
