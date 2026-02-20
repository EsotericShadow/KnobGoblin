using Avalonia;
using Avalonia.Controls.Primitives;
using KnobForge.App.Controls;
using KnobForge.Core;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private void InitializeBrushContextAndHudUx()
        {
            if (_brushPaintChannelCombo != null)
            {
                _brushPaintChannelCombo.SelectionChanged += (_, _) =>
                {
                    UpdateBrushContextUi();
                    _metalViewport?.RefreshPaintHud();
                };
            }

            if (_brushPaintEnabledCheckBox != null)
            {
                _brushPaintEnabledCheckBox.PropertyChanged += OnBrushPaintEnabledContextChanged;
            }

            if (_metalViewport != null)
            {
                _metalViewport.PaintHudUpdated += OnViewportPaintHudUpdated;
            }

            UpdateBrushContextUi();
            _metalViewport?.RefreshPaintHud();
        }

        private void OnBrushPaintEnabledContextChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != ToggleButton.IsCheckedProperty)
            {
                return;
            }

            UpdateBrushContextUi();
            _metalViewport?.RefreshPaintHud();
        }

        private void UpdateBrushContextUi()
        {
            PaintChannel channel = _brushPaintChannelCombo?.SelectedItem is PaintChannel selected
                ? selected
                : _project.BrushChannel;
            bool scratchMode = channel == PaintChannel.Scratch;
            bool colorMode = channel == PaintChannel.Color;

            if (_scratchContextBannerBorder != null)
            {
                _scratchContextBannerBorder.IsVisible = scratchMode;
            }

            if (_scratchPrimaryPanel != null)
            {
                _scratchPrimaryPanel.IsVisible = scratchMode;
            }

            if (_generalPaintPrimaryPanel != null)
            {
                _generalPaintPrimaryPanel.IsVisible = !scratchMode;
            }

            if (_scratchAdvancedPanel != null)
            {
                _scratchAdvancedPanel.IsVisible = scratchMode;
            }

            if (_generalPaintAdvancedPanel != null)
            {
                _generalPaintAdvancedPanel.IsVisible = !scratchMode;
            }

            if (_colorChannelPanel != null)
            {
                _colorChannelPanel.IsVisible = !scratchMode && colorMode;
            }

            if (_brushPaintColorPicker != null)
            {
                _brushPaintColorPicker.IsEnabled = !scratchMode && colorMode;
            }

            if (_brushAdvancedExpander != null && scratchMode && !_brushAdvancedExpander.IsExpanded)
            {
                _brushAdvancedExpander.IsExpanded = true;
            }
        }

        private void OnViewportPaintHudUpdated(MetalViewport.PaintHudSnapshot snapshot)
        {
            if (_paintHudBorder == null ||
                _paintHudTitleText == null ||
                _paintHudLine1Text == null ||
                _paintHudLine2Text == null ||
                _paintHudLine3Text == null ||
                _paintHudLine4Text == null)
            {
                return;
            }

            bool showHud = snapshot.PaintEnabled || snapshot.IsPainting;
            _paintHudBorder.IsVisible = showHud;
            if (!showHud)
            {
                return;
            }

            _paintHudTitleText.Text = snapshot.IsPainting ? "Paint HUD - Active Stroke" : "Paint HUD";
            _paintHudLine1Text.Text = $"Channel: {snapshot.Channel}";

            if (snapshot.Channel == PaintChannel.Scratch)
            {
                _paintHudLine2Text.Text = $"Abrasion: {snapshot.AbrasionType}";
                _paintHudLine3Text.Text = $"Width/Opacity: {snapshot.ActiveSizePx:0.#}px / {snapshot.ActiveOpacity:0.00}";
                string optionState = snapshot.OptionDepthRampActive ? "Alt ramp on" : "Alt ramp off";
                _paintHudLine4Text.Text = $"Depth: {snapshot.LiveScratchDepth:0.000} ({optionState})  Hit: {ToHudHitLabel(snapshot.HitMode)}";
                return;
            }

            _paintHudLine2Text.Text = $"Brush: {snapshot.BrushType}";
            _paintHudLine3Text.Text = $"Size/Opacity: {snapshot.ActiveSizePx:0.#}px / {snapshot.ActiveOpacity:0.00}";
            _paintHudLine4Text.Text = $"Hit: {ToHudHitLabel(snapshot.HitMode)}";
        }

        private static string ToHudHitLabel(MetalViewport.PaintHitMode hitMode)
        {
            return hitMode switch
            {
                MetalViewport.PaintHitMode.MeshHit => "mesh hit",
                MetalViewport.PaintHitMode.Fallback => "fallback",
                _ => "idle"
            };
        }
    }
}
