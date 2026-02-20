using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KnobForge.App.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private bool _paintLayerUiInitialized;

        private void InitializePaintLayerUx()
        {
            if (_paintLayerUiInitialized || _metalViewport == null || _paintLayerListBox == null)
            {
                return;
            }

            _paintLayerUiInitialized = true;

            _paintLayerListBox.SelectionChanged += OnPaintLayerSelectionChanged;
            if (_addPaintLayerButton != null)
            {
                _addPaintLayerButton.Click += OnAddPaintLayerClicked;
            }

            if (_renamePaintLayerButton != null)
            {
                _renamePaintLayerButton.Click += OnRenamePaintLayerClicked;
            }

            if (_deletePaintLayerButton != null)
            {
                _deletePaintLayerButton.Click += OnDeletePaintLayerClicked;
            }

            if (_clearPaintLayerFocusButton != null)
            {
                _clearPaintLayerFocusButton.Click += OnClearPaintLayerFocusClicked;
            }

            if (_focusPaintLayerCheckBox != null)
            {
                _focusPaintLayerCheckBox.PropertyChanged += OnFocusPaintLayerChanged;
            }

            if (_paintLayerNameTextBox != null)
            {
                _paintLayerNameTextBox.KeyDown += OnPaintLayerNameTextBoxKeyDown;
            }

            _metalViewport.PaintLayersChanged += OnViewportPaintLayersChanged;
            _metalViewport.PaintHistoryRevisionChanged += OnViewportPaintHistoryRevisionChanged;
            RefreshPaintLayerListFromViewport(preferActiveSelection: true);
        }

        private void OnViewportPaintLayersChanged()
        {
            RefreshPaintLayerListFromViewport(preferActiveSelection: false);
        }

        private void OnViewportPaintHistoryRevisionChanged(int _)
        {
            CaptureUndoSnapshotIfChanged();
        }

        private void OnPaintLayerSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_metalViewport == null || _paintLayerListBox == null || _updatingUi)
            {
                return;
            }

            if (_paintLayerListBox.SelectedItem is not PaintLayerListItem selected)
            {
                _metalViewport.SetFocusedPaintLayer(-1);
                if (_focusPaintLayerCheckBox != null)
                {
                    WithUiRefreshSuppressed(() =>
                    {
                        _focusPaintLayerCheckBox.IsChecked = false;
                    });
                }

                RefreshPaintLayerListFromViewport(preferActiveSelection: false);
                CaptureUndoSnapshotIfChanged();
                return;
            }

            _metalViewport.SetActivePaintLayer(selected.Index);
            _metalViewport.SetFocusedPaintLayer(selected.Index);
            if (_focusPaintLayerCheckBox != null && _focusPaintLayerCheckBox.IsChecked != true)
            {
                WithUiRefreshSuppressed(() =>
                {
                    _focusPaintLayerCheckBox.IsChecked = true;
                });
            }

            if (_paintLayerNameTextBox != null)
            {
                _paintLayerNameTextBox.Text = selected.Name;
            }

            _metalViewport.RefreshPaintHud();
            RefreshPaintLayerListFromViewport(preferActiveSelection: false);
            CaptureUndoSnapshotIfChanged();
        }

        private void OnAddPaintLayerClicked(object? sender, RoutedEventArgs e)
        {
            if (_metalViewport == null)
            {
                return;
            }

            string? name = _paintLayerNameTextBox?.Text;
            _metalViewport.AddPaintLayer(name);
            _metalViewport.SetFocusedPaintLayer(_metalViewport.ActivePaintLayerIndex);
            RefreshPaintLayerListFromViewport(preferActiveSelection: true);
            CaptureUndoSnapshotIfChanged();
        }

        private void OnRenamePaintLayerClicked(object? sender, RoutedEventArgs e)
        {
            if (_metalViewport == null || !TryGetSelectedPaintLayerIndex(out int index))
            {
                return;
            }

            if (_paintLayerNameTextBox == null)
            {
                return;
            }

            _metalViewport.RenamePaintLayer(index, _paintLayerNameTextBox.Text);
            RefreshPaintLayerListFromViewport(preferActiveSelection: false);
            CaptureUndoSnapshotIfChanged();
        }

        private void OnDeletePaintLayerClicked(object? sender, RoutedEventArgs e)
        {
            if (_metalViewport == null || !TryGetSelectedPaintLayerIndex(out int index))
            {
                return;
            }

            _metalViewport.DeletePaintLayer(index);
            _metalViewport.SetFocusedPaintLayer(_metalViewport.ActivePaintLayerIndex);
            RefreshPaintLayerListFromViewport(preferActiveSelection: true);
            CaptureUndoSnapshotIfChanged();
        }

        private void OnClearPaintLayerFocusClicked(object? sender, RoutedEventArgs e)
        {
            if (_metalViewport == null)
            {
                return;
            }

            _metalViewport.SetFocusedPaintLayer(-1);
            WithUiRefreshSuppressed(() =>
            {
                if (_paintLayerListBox != null)
                {
                    _paintLayerListBox.SelectedItem = null;
                }

                if (_focusPaintLayerCheckBox != null)
                {
                    _focusPaintLayerCheckBox.IsChecked = false;
                }
            });

            RefreshPaintLayerListFromViewport(preferActiveSelection: false);
            CaptureUndoSnapshotIfChanged();
        }

        private void OnFocusPaintLayerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != CheckBox.IsCheckedProperty || _metalViewport == null || _updatingUi)
            {
                return;
            }

            bool focusEnabled = _focusPaintLayerCheckBox?.IsChecked == true;
            if (!focusEnabled)
            {
                _metalViewport.SetFocusedPaintLayer(-1);
                RefreshPaintLayerListFromViewport(preferActiveSelection: false);
                CaptureUndoSnapshotIfChanged();
                return;
            }

            if (TryGetSelectedPaintLayerIndex(out int index))
            {
                _metalViewport.SetFocusedPaintLayer(index);
                RefreshPaintLayerListFromViewport(preferActiveSelection: false);
                CaptureUndoSnapshotIfChanged();
                return;
            }

            _metalViewport.SetFocusedPaintLayer(_metalViewport.ActivePaintLayerIndex);
            RefreshPaintLayerListFromViewport(preferActiveSelection: true);
            CaptureUndoSnapshotIfChanged();
        }

        private void OnPaintLayerNameTextBoxKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            OnRenamePaintLayerClicked(sender, new RoutedEventArgs());
            e.Handled = true;
        }

        private bool TryGetSelectedPaintLayerIndex(out int index)
        {
            index = -1;
            if (_paintLayerListBox?.SelectedItem is not PaintLayerListItem selected)
            {
                return false;
            }

            index = selected.Index;
            return true;
        }

        private void RefreshPaintLayerListFromViewport(bool preferActiveSelection)
        {
            if (_metalViewport == null || _paintLayerListBox == null)
            {
                return;
            }

            IReadOnlyList<MetalViewport.PaintLayerInfo> layers = _metalViewport.GetPaintLayers();
            int previousSelectedIndex = _paintLayerListBox.SelectedItem is PaintLayerListItem previous ? previous.Index : -1;

            _paintLayerItems.Clear();
            for (int i = 0; i < layers.Count; i++)
            {
                MetalViewport.PaintLayerInfo info = layers[i];
                string activeTag = info.IsActive ? "Active" : "Idle";
                string focusTag = info.IsFocused ? ", Focus" : string.Empty;
                string display = $"{info.Index + 1}. {info.Name} ({activeTag}{focusTag})";
                _paintLayerItems.Add(new PaintLayerListItem(info.Index, info.Name, display));
            }

            int targetIndex = preferActiveSelection
                ? _metalViewport.ActivePaintLayerIndex
                : previousSelectedIndex;
            if (!preferActiveSelection && targetIndex < 0 && _metalViewport.FocusedPaintLayerIndex >= 0)
            {
                targetIndex = _metalViewport.FocusedPaintLayerIndex;
            }

            bool selectLayer = targetIndex >= 0 && targetIndex < _paintLayerItems.Count;
            if (preferActiveSelection && !selectLayer)
            {
                targetIndex = Math.Clamp(_metalViewport.ActivePaintLayerIndex, 0, Math.Max(0, _paintLayerItems.Count - 1));
                selectLayer = _paintLayerItems.Count > 0;
            }

            WithUiRefreshSuppressed(() =>
            {
                _paintLayerListBox.ItemsSource = _paintLayerItems.ToList();
                if (selectLayer)
                {
                    PaintLayerListItem selected = _paintLayerItems[targetIndex];
                    _paintLayerListBox.SelectedItem = selected;
                    if (_paintLayerNameTextBox != null)
                    {
                        _paintLayerNameTextBox.Text = selected.Name;
                    }
                }
                else
                {
                    _paintLayerListBox.SelectedItem = null;
                    if (_paintLayerNameTextBox != null)
                    {
                        _paintLayerNameTextBox.Text = string.Empty;
                    }
                }

                if (_focusPaintLayerCheckBox != null)
                {
                    _focusPaintLayerCheckBox.IsChecked = _metalViewport.FocusedPaintLayerIndex >= 0;
                }
            });

            bool hasSelection = _paintLayerListBox.SelectedItem is PaintLayerListItem;
            if (_renamePaintLayerButton != null)
            {
                _renamePaintLayerButton.IsEnabled = hasSelection;
            }

            if (_deletePaintLayerButton != null)
            {
                _deletePaintLayerButton.IsEnabled = _paintLayerItems.Count > 1;
            }

            if (_clearPaintLayerFocusButton != null)
            {
                _clearPaintLayerFocusButton.IsEnabled = hasSelection && _metalViewport.FocusedPaintLayerIndex >= 0;
            }
        }

        private sealed class PaintLayerListItem
        {
            public PaintLayerListItem(int index, string name, string displayName)
            {
                Index = index;
                Name = name;
                DisplayName = displayName;
            }

            public int Index { get; }
            public string Name { get; }
            public string DisplayName { get; }

            public override string ToString()
            {
                return DisplayName;
            }
        }
    }
}
