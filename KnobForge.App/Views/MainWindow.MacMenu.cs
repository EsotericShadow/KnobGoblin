using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using KnobForge.App.ProjectFiles;
using KnobForge.Core;
using System;
using System.IO;
using System.Linq;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private NativeMenu? _nativeOpenRecentMenu;
        private NativeMenuItem? _nativeBrushEnabledMenuItem;
        private NativeMenuItem? _nativeBrushChannelColorMenuItem;
        private NativeMenuItem? _nativeBrushChannelScratchMenuItem;
        private NativeMenuItem? _nativeBrushChannelRustMenuItem;
        private NativeMenuItem? _nativeBrushChannelWearMenuItem;
        private NativeMenuItem? _nativeBrushChannelGunkMenuItem;
        private NativeMenuItem? _nativeDeleteActivePaintLayerMenuItem;
        private NativeMenuItem? _nativeFocusActivePaintLayerMenuItem;

        private void InitializeNativeMenuBar()
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            try
            {
                ApplyNativeMenuBar();
            }
            catch (Exception ex)
            {
                LogMenuFailure("InitializeNativeMenuBar", ex);
            }
        }

        private void RefreshNativeMenuBar()
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            try
            {
                ApplyNativeMenuBar();
            }
            catch (Exception ex)
            {
                LogMenuFailure("RefreshNativeMenuBar", ex);
            }
        }

        private void ApplyNativeMenuBar()
        {
            var menuBar = new NativeMenu();
            menuBar.Add(BuildFileMenuRoot());
            menuBar.Add(BuildEditMenuRoot());
            menuBar.Add(BuildViewMenuRoot());
            menuBar.Add(BuildBrushMenuRoot());

            NativeMenu.SetMenu(this, menuBar);
            UpdateBrushMenuState();
        }

        private NativeMenuItem BuildFileMenuRoot()
        {
            var fileMenu = new NativeMenu();

            fileMenu.Add(CreateActionMenuItem(
                header: "New Project Window",
                onClick: OpenNewProjectWindowFromMenu,
                gesture: new KeyGesture(Key.N, KeyModifiers.Meta)));
            fileMenu.Add(CreateActionMenuItem(
                header: "Open...",
                onClick: () => OnOpenProjectButtonClicked(this, new RoutedEventArgs()),
                gesture: new KeyGesture(Key.O, KeyModifiers.Meta)));

            _nativeOpenRecentMenu = new NativeMenu();
            _nativeOpenRecentMenu.NeedsUpdate += (_, _) => RebuildOpenRecentMenu();
            RebuildOpenRecentMenu();
            fileMenu.Add(new NativeMenuItem("Open Recent")
            {
                Menu = _nativeOpenRecentMenu
            });

            fileMenu.Add(new NativeMenuItemSeparator());
            fileMenu.Add(CreateActionMenuItem(
                header: "Save",
                onClick: () => OnSaveProjectButtonClicked(this, new RoutedEventArgs()),
                gesture: new KeyGesture(Key.S, KeyModifiers.Meta)));
            fileMenu.Add(CreateActionMenuItem(
                header: "Save As...",
                onClick: () => OnSaveProjectAsButtonClicked(this, new RoutedEventArgs()),
                gesture: new KeyGesture(Key.S, KeyModifiers.Meta | KeyModifiers.Shift)));
            fileMenu.Add(new NativeMenuItemSeparator());
            fileMenu.Add(CreateActionMenuItem(
                header: "Render...",
                onClick: () => OnRenderButtonClick(this, new RoutedEventArgs()),
                gesture: new KeyGesture(Key.R, KeyModifiers.Meta | KeyModifiers.Shift)));
            fileMenu.Add(new NativeMenuItemSeparator());
            fileMenu.Add(CreateActionMenuItem(
                header: "Quit KnobForge",
                onClick: QuitFromMenu,
                gesture: new KeyGesture(Key.Q, KeyModifiers.Meta)));

            return new NativeMenuItem("File")
            {
                Menu = fileMenu
            };
        }

        private NativeMenuItem BuildEditMenuRoot()
        {
            var editMenu = new NativeMenu();
            editMenu.Add(CreateActionMenuItem(
                header: "Undo",
                onClick: ExecuteUndo,
                gesture: new KeyGesture(Key.Z, KeyModifiers.Meta)));
            editMenu.Add(CreateActionMenuItem(
                header: "Redo",
                onClick: ExecuteRedo,
                gesture: new KeyGesture(Key.Z, KeyModifiers.Meta | KeyModifiers.Shift)));

            return new NativeMenuItem("Edit")
            {
                Menu = editMenu
            };
        }

        private NativeMenuItem BuildViewMenuRoot()
        {
            var viewMenu = new NativeMenu();
            viewMenu.Add(CreateActionMenuItem(
                header: "Reset Camera",
                onClick: () => _metalViewport?.ResetCamera(),
                gesture: new KeyGesture(Key.R, KeyModifiers.Meta)));
            viewMenu.Add(new NativeMenuItemSeparator());
            viewMenu.Add(CreateActionMenuItem(
                header: "Focus Scene Tree",
                onClick: () => _sceneListBox?.Focus(),
                gesture: new KeyGesture(Key.D1, KeyModifiers.Meta)));
            viewMenu.Add(CreateActionMenuItem(
                header: "Focus Inspector Search",
                onClick: () => _inspectorSearchTextBox?.Focus(),
                gesture: new KeyGesture(Key.D2, KeyModifiers.Meta)));

            return new NativeMenuItem("View")
            {
                Menu = viewMenu
            };
        }

        private NativeMenuItem BuildBrushMenuRoot()
        {
            var brushMenu = new NativeMenu();
            brushMenu.NeedsUpdate += (_, _) => UpdateBrushMenuState();

            _nativeBrushEnabledMenuItem = new NativeMenuItem("Brush Painting Enabled")
            {
                ToggleType = NativeMenuItemToggleType.CheckBox,
                IsChecked = _project.BrushPaintingEnabled,
                Gesture = new KeyGesture(Key.B, KeyModifiers.Meta)
            };
            _nativeBrushEnabledMenuItem.Click += (_, _) => ToggleBrushPaintingFromMenu();
            brushMenu.Add(_nativeBrushEnabledMenuItem);
            brushMenu.Add(new NativeMenuItemSeparator());

            _nativeBrushChannelColorMenuItem = CreateBrushChannelMenuItem("Color Channel", PaintChannel.Color);
            _nativeBrushChannelScratchMenuItem = CreateBrushChannelMenuItem("Scratch Channel", PaintChannel.Scratch);
            _nativeBrushChannelRustMenuItem = CreateBrushChannelMenuItem("Rust Channel", PaintChannel.Rust);
            _nativeBrushChannelWearMenuItem = CreateBrushChannelMenuItem("Wear Channel", PaintChannel.Wear);
            _nativeBrushChannelGunkMenuItem = CreateBrushChannelMenuItem("Gunk Channel", PaintChannel.Gunk);
            brushMenu.Add(_nativeBrushChannelColorMenuItem);
            brushMenu.Add(_nativeBrushChannelScratchMenuItem);
            brushMenu.Add(_nativeBrushChannelRustMenuItem);
            brushMenu.Add(_nativeBrushChannelWearMenuItem);
            brushMenu.Add(_nativeBrushChannelGunkMenuItem);
            brushMenu.Add(new NativeMenuItemSeparator());

            brushMenu.Add(CreateActionMenuItem(
                header: "Add Paint Layer",
                onClick: () => OnAddPaintLayerClicked(this, new RoutedEventArgs()),
                gesture: new KeyGesture(Key.L, KeyModifiers.Meta | KeyModifiers.Shift)));

            _nativeDeleteActivePaintLayerMenuItem = CreateActionMenuItem(
                header: "Delete Active Paint Layer",
                onClick: DeleteActivePaintLayerFromMenu);
            brushMenu.Add(_nativeDeleteActivePaintLayerMenuItem);

            _nativeFocusActivePaintLayerMenuItem = new NativeMenuItem("Focus Active Layer")
            {
                ToggleType = NativeMenuItemToggleType.CheckBox
            };
            _nativeFocusActivePaintLayerMenuItem.Click += (_, _) => ToggleFocusActivePaintLayerFromMenu();
            brushMenu.Add(_nativeFocusActivePaintLayerMenuItem);

            brushMenu.Add(CreateActionMenuItem(
                header: "Clear Layer Focus",
                onClick: () => OnClearPaintLayerFocusClicked(this, new RoutedEventArgs())));

            return new NativeMenuItem("Brush")
            {
                Menu = brushMenu
            };
        }

        private void RebuildOpenRecentMenu()
        {
            try
            {
                if (_nativeOpenRecentMenu == null)
                {
                    return;
                }

                _nativeOpenRecentMenu.Items.Clear();

                var entries = KnobProjectFileStore
                    .GetLauncherEntries(12)
                    .Where(entry => File.Exists(entry.FilePath))
                    .ToList();
                if (entries.Count == 0)
                {
                    _nativeOpenRecentMenu.Add(new NativeMenuItem("No Recent Projects")
                    {
                        IsEnabled = false
                    });
                    return;
                }

                foreach (KnobProjectLauncherEntry entry in entries)
                {
                    string path = entry.FilePath;
                    string header = string.IsNullOrWhiteSpace(entry.DisplayName)
                        ? Path.GetFileNameWithoutExtension(path)
                        : entry.DisplayName;
                    var item = new NativeMenuItem(header);
                    item.Click += async (_, _) =>
                    {
                        if (!TryLoadProjectFromFile(path, out string error))
                        {
                            await ShowProjectFileInfoDialogAsync("Open Project Failed", error);
                        }
                    };
                    _nativeOpenRecentMenu.Add(item);
                }
            }
            catch (Exception ex)
            {
                LogMenuFailure("RebuildOpenRecentMenu", ex);
                if (_nativeOpenRecentMenu != null)
                {
                    _nativeOpenRecentMenu.Items.Clear();
                    _nativeOpenRecentMenu.Add(new NativeMenuItem("Recent Projects Unavailable")
                    {
                        IsEnabled = false
                    });
                }
            }
        }

        private static NativeMenuItem CreateActionMenuItem(string header, Action onClick, KeyGesture? gesture = null)
        {
            var item = new NativeMenuItem(header);
            if (gesture != null)
            {
                item.Gesture = gesture;
            }

            item.Click += (_, _) => onClick();
            return item;
        }

        private NativeMenuItem CreateBrushChannelMenuItem(string header, PaintChannel channel)
        {
            var item = new NativeMenuItem(header)
            {
                ToggleType = NativeMenuItemToggleType.Radio,
                IsChecked = _project.BrushChannel == channel
            };
            item.Click += (_, _) => SelectBrushChannelFromMenu(channel);
            return item;
        }

        private void OpenNewProjectWindowFromMenu()
        {
            var window = new MainWindow();
            window.Show();
            window.Activate();
        }

        private void QuitFromMenu()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
                return;
            }

            Close();
        }

        private void ToggleBrushPaintingFromMenu()
        {
            bool enabled = !_project.BrushPaintingEnabled;
            if (_brushPaintEnabledCheckBox != null)
            {
                _brushPaintEnabledCheckBox.IsChecked = enabled;
            }
            else
            {
                _project.BrushPaintingEnabled = enabled;
                NotifyProjectStateChanged();
            }

            UpdateBrushMenuState();
        }

        private void SelectBrushChannelFromMenu(PaintChannel channel)
        {
            if (_brushPaintChannelCombo != null)
            {
                _brushPaintChannelCombo.SelectedItem = channel;
            }
            else
            {
                _project.BrushChannel = channel;
                NotifyProjectStateChanged();
            }

            UpdateBrushMenuState();
        }

        private void DeleteActivePaintLayerFromMenu()
        {
            if (_metalViewport == null)
            {
                return;
            }

            if (_metalViewport.GetPaintLayers().Count <= 1)
            {
                return;
            }

            int targetIndex = _metalViewport.ActivePaintLayerIndex;
            _metalViewport.DeletePaintLayer(targetIndex);
            _metalViewport.SetFocusedPaintLayer(_metalViewport.ActivePaintLayerIndex);
            RefreshPaintLayerListFromViewport(preferActiveSelection: true);
            CaptureUndoSnapshotIfChanged();
            UpdateBrushMenuState();
        }

        private void ToggleFocusActivePaintLayerFromMenu()
        {
            if (_metalViewport == null)
            {
                return;
            }

            bool hasFocus = _metalViewport.FocusedPaintLayerIndex >= 0;
            if (hasFocus)
            {
                _metalViewport.SetFocusedPaintLayer(-1);
                if (_focusPaintLayerCheckBox != null)
                {
                    _focusPaintLayerCheckBox.IsChecked = false;
                }
            }
            else
            {
                _metalViewport.SetFocusedPaintLayer(_metalViewport.ActivePaintLayerIndex);
                if (_focusPaintLayerCheckBox != null)
                {
                    _focusPaintLayerCheckBox.IsChecked = true;
                }
            }

            RefreshPaintLayerListFromViewport(preferActiveSelection: true);
            CaptureUndoSnapshotIfChanged();
            UpdateBrushMenuState();
        }

        private void UpdateBrushMenuState()
        {
            _nativeBrushEnabledMenuItem?.SetCurrentValue(NativeMenuItem.IsCheckedProperty, _project.BrushPaintingEnabled);

            PaintChannel channel = _project.BrushChannel;
            if (_nativeBrushChannelColorMenuItem != null)
            {
                _nativeBrushChannelColorMenuItem.IsChecked = channel == PaintChannel.Color;
            }

            if (_nativeBrushChannelScratchMenuItem != null)
            {
                _nativeBrushChannelScratchMenuItem.IsChecked = channel == PaintChannel.Scratch;
            }

            if (_nativeBrushChannelRustMenuItem != null)
            {
                _nativeBrushChannelRustMenuItem.IsChecked = channel == PaintChannel.Rust;
            }

            if (_nativeBrushChannelWearMenuItem != null)
            {
                _nativeBrushChannelWearMenuItem.IsChecked = channel == PaintChannel.Wear;
            }

            if (_nativeBrushChannelGunkMenuItem != null)
            {
                _nativeBrushChannelGunkMenuItem.IsChecked = channel == PaintChannel.Gunk;
            }

            int layerCount = _metalViewport?.GetPaintLayers().Count ?? 0;
            bool canDeleteLayer = layerCount > 1;
            if (_nativeDeleteActivePaintLayerMenuItem != null)
            {
                _nativeDeleteActivePaintLayerMenuItem.IsEnabled = canDeleteLayer;
            }

            bool focused = (_metalViewport?.FocusedPaintLayerIndex ?? -1) >= 0;
            if (_nativeFocusActivePaintLayerMenuItem != null)
            {
                _nativeFocusActivePaintLayerMenuItem.IsChecked = focused;
            }
        }

        private static void LogMenuFailure(string context, Exception ex)
        {
            Console.Error.WriteLine($">>> [NativeMenu] {context} failed: {ex.Message}");
            try
            {
                string path = Path.Combine(Path.GetTempPath(), "knobforge_menu_errors.log");
                File.AppendAllText(
                    path,
                    $"{DateTime.UtcNow:O} [{context}] {ex}{Environment.NewLine}");
            }
            catch
            {
                // best effort only
            }
        }
    }
}
