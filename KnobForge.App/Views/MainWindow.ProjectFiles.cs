using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using KnobForge.Core;
using KnobForge.Core.Scene;
using KnobForge.App.ProjectFiles;
using KnobForge.Rendering;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private static readonly JsonSerializerOptions ProjectSnapshotJsonOptions = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        public string? CurrentProjectFilePath => _currentProjectFilePath;

        public bool TryLoadProjectFromFile(string path, out string error)
        {
            error = string.Empty;
            try
            {
                if (!KnobProjectFileStore.TryLoadEnvelope(path, out KnobProjectFileEnvelope? envelope, out error) || envelope == null)
                {
                    return false;
                }

                InspectorUndoSnapshot? snapshot;
                try
                {
                    snapshot = JsonSerializer.Deserialize<InspectorUndoSnapshot>(envelope.SnapshotJson, ProjectSnapshotJsonOptions);
                }
                catch (Exception ex)
                {
                    error = $"Project snapshot is invalid: {ex.Message}";
                    return false;
                }

                if (snapshot == null)
                {
                    error = "Project snapshot is missing.";
                    return false;
                }

                bool previousApplyingUndoRedo = _applyingUndoRedo;
                _applyingUndoRedo = true;
                try
                {
                    ApplyInspectorUndoSnapshot(snapshot);
                    if (_metalViewport != null)
                    {
                        if (!string.IsNullOrWhiteSpace(envelope.PaintStateJson))
                        {
                            _metalViewport.TryImportPaintStateJson(envelope.PaintStateJson);
                        }

                        if (!string.IsNullOrWhiteSpace(envelope.ViewportStateJson))
                        {
                            _metalViewport.TryImportViewportStateJson(envelope.ViewportStateJson);
                        }

                        _metalViewport.InvalidateGpu();
                    }
                }
                catch (Exception ex)
                {
                    error = $"Failed to apply project state: {ex.Message}";
                    return false;
                }
                finally
                {
                    _applyingUndoRedo = previousApplyingUndoRedo;
                }

                RefreshSceneTree();
                RefreshInspectorFromProject(InspectorRefreshTabPolicy.FollowSceneSelection);
                InitializeUndoRedoHistory(resetStacks: true);

                _currentProjectFilePath = Path.GetFullPath(path);
                UpdateWindowTitleForProject();
                KnobProjectFileStore.MarkRecentProject(_currentProjectFilePath);
                RefreshNativeMenuBar();
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to load project: {ex.Message}";
                Console.Error.WriteLine($">>> [ProjectLoad] Unexpected exception loading '{path}': {ex}");
                return false;
            }
        }

        private bool TrySaveProjectToFile(string path, out string error)
        {
            error = string.Empty;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                error = $"Project path is invalid: {ex.Message}";
                return false;
            }

            CommitLightingEnvironmentShadowStateFromUi();

            string snapshotJson;
            try
            {
                snapshotJson = JsonSerializer.Serialize(CaptureInspectorUndoSnapshot(), ProjectSnapshotJsonOptions);
            }
            catch (Exception ex)
            {
                error = $"Failed to capture project snapshot: {ex.Message}";
                return false;
            }

            string? paintStateJson = _metalViewport?.ExportPaintStateJson();
            string? viewportStateJson = _metalViewport?.ExportViewportStateJson();
            string? thumbnailBase64 = TryBuildProjectThumbnailBase64();

            var envelope = new KnobProjectFileEnvelope
            {
                DisplayName = Path.GetFileNameWithoutExtension(fullPath),
                SnapshotJson = snapshotJson,
                PaintStateJson = paintStateJson,
                ViewportStateJson = viewportStateJson,
                ThumbnailPngBase64 = thumbnailBase64
            };

            if (!KnobProjectFileStore.TrySaveEnvelope(fullPath, envelope, out error))
            {
                return false;
            }

            _currentProjectFilePath = fullPath;
            UpdateWindowTitleForProject();
            KnobProjectFileStore.MarkRecentProject(_currentProjectFilePath);
            RefreshNativeMenuBar();
            return true;
        }

        private void CommitLightingEnvironmentShadowStateFromUi()
        {
            if (_lightingModeCombo?.SelectedItem is LightingMode mode)
            {
                _project.Mode = mode;
            }

            if (_lightListBox != null && _project.Lights.Count > 0)
            {
                int selected = _lightListBox.SelectedIndex;
                if (selected >= 0)
                {
                    _project.SetSelectedLightIndex(Math.Clamp(selected, 0, _project.Lights.Count - 1));
                }
            }

            KnobLight? light = _project.SelectedLight;
            if (light != null)
            {
                if (_lightTypeCombo?.SelectedItem is LightType type)
                {
                    light.Type = type;
                }

                if (_lightXSlider != null)
                {
                    light.X = (float)_lightXSlider.Value;
                }

                if (_lightYSlider != null)
                {
                    light.Y = (float)_lightYSlider.Value;
                }

                if (_lightZSlider != null)
                {
                    light.Z = (float)_lightZSlider.Value;
                }

                if (_directionSlider != null)
                {
                    light.DirectionRadians = (float)DegreesToRadians(_directionSlider.Value);
                }

                if (_intensitySlider != null)
                {
                    light.Intensity = (float)_intensitySlider.Value;
                }

                if (_falloffSlider != null)
                {
                    light.Falloff = (float)_falloffSlider.Value;
                }

                if (_lightRSlider != null && _lightGSlider != null && _lightBSlider != null)
                {
                    light.Color = new SKColor(
                        (byte)Math.Clamp((int)_lightRSlider.Value, 0, 255),
                        (byte)Math.Clamp((int)_lightGSlider.Value, 0, 255),
                        (byte)Math.Clamp((int)_lightBSlider.Value, 0, 255),
                        light.Color.Alpha);
                }

                if (_diffuseBoostSlider != null)
                {
                    light.DiffuseBoost = (float)_diffuseBoostSlider.Value;
                }

                if (_specularBoostSlider != null)
                {
                    light.SpecularBoost = (float)_specularBoostSlider.Value;
                }

                if (_specularPowerSlider != null)
                {
                    light.SpecularPower = (float)_specularPowerSlider.Value;
                }
            }

            if (_envIntensityInputTextBox != null && _envIntensitySlider != null)
            {
                ApplyPrecisionTextEntry(_envIntensityInputTextBox, _envIntensitySlider);
            }

            if (_envRoughnessMixInputTextBox != null && _envRoughnessMixSlider != null)
            {
                ApplyPrecisionTextEntry(_envRoughnessMixInputTextBox, _envRoughnessMixSlider);
            }

            if (_envIntensitySlider != null && _envRoughnessMixSlider != null &&
                _envTopRSlider != null && _envTopGSlider != null && _envTopBSlider != null &&
                _envBottomRSlider != null && _envBottomGSlider != null && _envBottomBSlider != null)
            {
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
            }

            if (_shadowStrengthInputTextBox != null && _shadowStrengthSlider != null)
            {
                ApplyPrecisionTextEntry(_shadowStrengthInputTextBox, _shadowStrengthSlider);
            }

            if (_shadowSoftnessInputTextBox != null && _shadowSoftnessSlider != null)
            {
                ApplyPrecisionTextEntry(_shadowSoftnessInputTextBox, _shadowSoftnessSlider);
            }

            if (_shadowQualityInputTextBox != null && _shadowQualitySlider != null)
            {
                ApplyPrecisionTextEntry(_shadowQualityInputTextBox, _shadowQualitySlider);
            }

            if (_shadowEnabledCheckBox != null &&
                _shadowSourceModeCombo != null &&
                _shadowStrengthSlider != null &&
                _shadowSoftnessSlider != null &&
                _shadowDistanceSlider != null &&
                _shadowScaleSlider != null &&
                _shadowQualitySlider != null &&
                _shadowGraySlider != null &&
                _shadowDiffuseInfluenceSlider != null)
            {
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
            }
        }

        private string? TryBuildProjectThumbnailBase64()
        {
            if (_metalViewport == null)
            {
                return null;
            }

            try
            {
                ViewportCameraState camera = _metalViewport.CurrentCameraState;
                if (!_metalViewport.TryRenderFrameToBitmap(320, 320, camera, out SKBitmap? bitmap) || bitmap == null)
                {
                    return null;
                }

                using (bitmap)
                using (SKImage image = SKImage.FromBitmap(bitmap))
                using (SKData? pngData = image.Encode(SKEncodedImageFormat.Png, 90))
                {
                    if (pngData == null || pngData.Size == 0)
                    {
                        return null;
                    }

                    return Convert.ToBase64String(pngData.ToArray());
                }
            }
            catch
            {
                return null;
            }
        }

        private async void OnOpenProjectButtonClicked(object? sender, RoutedEventArgs e)
        {
            FilePickerOpenOptions options = new()
            {
                AllowMultiple = false,
                Title = "Open KnobForge Project",
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("KnobForge Project")
                    {
                        Patterns = new[] { $"*{KnobProjectFileStore.FileExtension}" }
                    }
                }
            };

            if (!string.IsNullOrWhiteSpace(_currentProjectFilePath))
            {
                string? directory = Path.GetDirectoryName(_currentProjectFilePath);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                {
                    IStorageFolder? folder = await StorageProvider.TryGetFolderFromPathAsync(directory);
                    if (folder != null)
                    {
                        options.SuggestedStartLocation = folder;
                    }
                }
            }

            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(options);
            if (files.Count == 0)
            {
                return;
            }

            string? path = files[0].TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                path = files[0].Path.LocalPath;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                await ShowProjectFileInfoDialogAsync("Open Project", "Selected file path is unavailable.");
                return;
            }

            if (!TryLoadProjectFromFile(path, out string error))
            {
                await ShowProjectFileInfoDialogAsync("Open Project Failed", error);
            }
        }

        private async void OnSaveProjectButtonClicked(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_currentProjectFilePath))
            {
                await SaveProjectAsInteractiveAsync();
                return;
            }

            if (!TrySaveProjectToFile(_currentProjectFilePath, out string error))
            {
                await ShowProjectFileInfoDialogAsync("Save Project Failed", error);
            }
        }

        private async void OnSaveProjectAsButtonClicked(object? sender, RoutedEventArgs e)
        {
            await SaveProjectAsInteractiveAsync();
        }

        private async Task SaveProjectAsInteractiveAsync()
        {
            string suggestedPath = !string.IsNullOrWhiteSpace(_currentProjectFilePath)
                ? _currentProjectFilePath
                : KnobProjectFileStore.BuildDefaultProjectPath("Untitled Knob");
            string suggestedFileName = Path.GetFileName(suggestedPath);

            FilePickerSaveOptions options = new()
            {
                Title = "Save KnobForge Project",
                SuggestedFileName = suggestedFileName,
                DefaultExtension = KnobProjectFileStore.FileExtension.TrimStart('.'),
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("KnobForge Project")
                    {
                        Patterns = new[] { $"*{KnobProjectFileStore.FileExtension}" }
                    }
                }
            };

            string? suggestedDirectory = Path.GetDirectoryName(suggestedPath);
            if (!string.IsNullOrWhiteSpace(suggestedDirectory) && Directory.Exists(suggestedDirectory))
            {
                IStorageFolder? folder = await StorageProvider.TryGetFolderFromPathAsync(suggestedDirectory);
                if (folder != null)
                {
                    options.SuggestedStartLocation = folder;
                }
            }

            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(options);
            if (file == null)
            {
                return;
            }

            string? path = file.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                path = file.Path.LocalPath;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                await ShowProjectFileInfoDialogAsync("Save Project", "Selected save path is unavailable.");
                return;
            }

            if (!path.EndsWith(KnobProjectFileStore.FileExtension, StringComparison.OrdinalIgnoreCase))
            {
                path += KnobProjectFileStore.FileExtension;
            }

            if (!TrySaveProjectToFile(path, out string error))
            {
                await ShowProjectFileInfoDialogAsync("Save Project Failed", error);
            }
        }

        private void UpdateWindowTitleForProject()
        {
            string suffix = string.IsNullOrWhiteSpace(_currentProjectFilePath)
                ? "Untitled"
                : Path.GetFileNameWithoutExtension(_currentProjectFilePath);
            Title = $"KnobForge - {suffix}";
        }

        private async Task ShowProjectFileInfoDialogAsync(string title, string message)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 460,
                Height = 190,
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
    }
}
