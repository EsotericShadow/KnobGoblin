using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using KnobForge.Core;
using KnobForge.Core.Scene;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private void WireBrushQuickToolbarButtons()
        {
            if (_brushQuickToggleButton != null)
            {
                _brushQuickToggleButton.Click += (_, _) =>
                {
                    if (_brushPaintEnabledCheckBox != null)
                    {
                        _brushPaintEnabledCheckBox.IsChecked = !(_brushPaintEnabledCheckBox.IsChecked ?? false);
                    }
                };
            }

            if (_brushQuickColorButton != null)
            {
                _brushQuickColorButton.Click += (_, _) => SelectQuickBrushChannel(PaintChannel.Color);
            }

            if (_brushQuickScratchButton != null)
            {
                _brushQuickScratchButton.Click += (_, _) => SelectQuickBrushChannel(PaintChannel.Scratch);
            }

            if (_brushQuickEraseButton != null)
            {
                _brushQuickEraseButton.Click += (_, _) => SelectQuickBrushChannel(PaintChannel.Erase);
            }

            if (_brushQuickRustButton != null)
            {
                _brushQuickRustButton.Click += (_, _) => SelectQuickBrushChannel(PaintChannel.Rust);
            }

            if (_brushQuickWearButton != null)
            {
                _brushQuickWearButton.Click += (_, _) => SelectQuickBrushChannel(PaintChannel.Wear);
            }

            if (_brushQuickGunkButton != null)
            {
                _brushQuickGunkButton.Click += (_, _) => SelectQuickBrushChannel(PaintChannel.Gunk);
            }

            if (_brushQuickSprayButton != null)
            {
                _brushQuickSprayButton.Click += (_, _) => SelectQuickBrushType(PaintBrushType.Spray);
            }

            if (_brushQuickStrokeButton != null)
            {
                _brushQuickStrokeButton.Click += (_, _) => SelectQuickBrushType(PaintBrushType.Stroke);
            }

            if (_brushQuickNeedleButton != null)
            {
                _brushQuickNeedleButton.Click += (_, _) => SelectQuickAbrasionType(ScratchAbrasionType.Needle);
            }

            if (_brushQuickScuffButton != null)
            {
                _brushQuickScuffButton.Click += (_, _) => SelectQuickAbrasionType(ScratchAbrasionType.Scuff);
            }

            if (_brushQuickAddLayerButton != null)
            {
                _brushQuickAddLayerButton.Click += OnAddPaintLayerClicked;
            }

            if (_brushQuickClearMaskButton != null)
            {
                _brushQuickClearMaskButton.Click += (_, _) => OnClearPaintMask();
            }

            if (_inspectorTabControl != null)
            {
                _inspectorTabControl.SelectionChanged += (_, _) =>
                {
                    UpdateBrushQuickToolbarState();
                    UpdateContextStrip();
                };
            }

            UpdateBrushQuickToolbarState();
            UpdateContextStrip();
        }

        private void SelectQuickBrushChannel(PaintChannel channel)
        {
            if (_brushPaintChannelCombo != null)
            {
                _brushPaintChannelCombo.SelectedItem = channel;
            }
        }

        private void SelectQuickBrushType(PaintBrushType type)
        {
            if (_brushTypeCombo != null)
            {
                _brushTypeCombo.SelectedItem = type;
            }
        }

        private void SelectQuickAbrasionType(ScratchAbrasionType type)
        {
            if (_scratchAbrasionTypeCombo != null)
            {
                _scratchAbrasionTypeCombo.SelectedItem = type;
            }
        }

        private void UpdateBrushQuickToolbarState()
        {
            bool brushEnabled = _brushPaintEnabledCheckBox?.IsChecked ?? _project.BrushPaintingEnabled;
            PaintChannel channel = _brushPaintChannelCombo?.SelectedItem is PaintChannel selectedChannel
                ? selectedChannel
                : _project.BrushChannel;
            PaintBrushType brushType = _brushTypeCombo?.SelectedItem is PaintBrushType selectedBrushType
                ? selectedBrushType
                : _project.BrushType;
            ScratchAbrasionType abrasionType = _scratchAbrasionTypeCombo?.SelectedItem is ScratchAbrasionType selectedAbrasionType
                ? selectedAbrasionType
                : _project.ScratchAbrasionType;
            bool brushTabActive = ReferenceEquals(_inspectorTabControl?.SelectedItem, _brushTabItem);
            bool hasModel = GetModelNode() != null;

            ApplyQuickButtonState(_brushQuickToggleButton, brushEnabled);
            ApplyQuickButtonState(_brushQuickColorButton, channel == PaintChannel.Color);
            ApplyQuickButtonState(_brushQuickScratchButton, channel == PaintChannel.Scratch);
            ApplyQuickButtonState(_brushQuickEraseButton, channel == PaintChannel.Erase);
            ApplyQuickButtonState(_brushQuickRustButton, channel == PaintChannel.Rust);
            ApplyQuickButtonState(_brushQuickWearButton, channel == PaintChannel.Wear);
            ApplyQuickButtonState(_brushQuickGunkButton, channel == PaintChannel.Gunk);
            ApplyQuickButtonState(_brushQuickSprayButton, brushType == PaintBrushType.Spray);
            ApplyQuickButtonState(_brushQuickStrokeButton, brushType == PaintBrushType.Stroke);
            ApplyQuickButtonState(_brushQuickNeedleButton, abrasionType == ScratchAbrasionType.Needle);
            ApplyQuickButtonState(_brushQuickScuffButton, abrasionType == ScratchAbrasionType.Scuff);

            if (_viewportBrushDock != null)
            {
                _viewportBrushDock.IsVisible = brushTabActive;
                _viewportBrushDock.IsEnabled = hasModel;
                _viewportBrushDock.Opacity = hasModel ? 1d : 0.55d;
            }
        }

        private void UpdateContextStrip(SceneNode? selectedNode = null)
        {
            SceneNode? node = selectedNode ?? _project.SelectedNode;
            string nodeLabel = node switch
            {
                LightNode => "Light",
                CollarNode => "Collar",
                MaterialNode => "Material",
                ModelNode => "Model",
                SceneRootNode => "SceneRoot",
                _ => "None"
            };

            if (_contextStripNodeText != null)
            {
                _contextStripNodeText.Text = $"Node: {nodeLabel}";
            }

            PaintChannel channel = _brushPaintChannelCombo?.SelectedItem is PaintChannel selectedChannel
                ? selectedChannel
                : _project.BrushChannel;
            PaintBrushType brushType = _brushTypeCombo?.SelectedItem is PaintBrushType selectedBrushType
                ? selectedBrushType
                : _project.BrushType;

            bool brushTabActive = ReferenceEquals(_inspectorTabControl?.SelectedItem, _brushTabItem);
            bool lightingTabActive = ReferenceEquals(_inspectorTabControl?.SelectedItem, _lightingTabItem);
            bool modelTabActive = ReferenceEquals(_inspectorTabControl?.SelectedItem, _modelTabItem);

            string toolLabel = brushTabActive
                ? $"Tool: Brush/{channel}/{brushType}"
                : lightingTabActive
                    ? "Tool: Lighting"
                    : modelTabActive
                        ? "Tool: Node Inspector"
                        : "Tool: View";

            if (_contextStripToolText != null)
            {
                _contextStripToolText.Text = toolLabel;
            }

            if (_contextStripHintText != null)
            {
                _contextStripHintText.Text = brushTabActive
                    ? "Tip: Use the dock in viewport for fast brush switching. Deep parameters stay in Brush tab."
                    : "Tip: Tabs hold deep controls. Scene selection drives what the Node tab focuses.";
            }
        }

        private static void ApplyQuickButtonState(Button? button, bool active)
        {
            if (button == null)
            {
                return;
            }

            button.Opacity = active ? 1d : 0.80d;
            button.Background = active
                ? new SolidColorBrush(Color.Parse("#284662"))
                : new SolidColorBrush(Color.Parse("#1A2531"));
            button.BorderBrush = active
                ? new SolidColorBrush(Color.Parse("#63A2D8"))
                : new SolidColorBrush(Color.Parse("#3A4A5C"));
            button.BorderThickness = new Avalonia.Thickness(1);
        }
    }
}
