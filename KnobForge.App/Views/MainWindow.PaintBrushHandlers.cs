using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using KnobForge.Core;
using System;
using System.Numerics;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private void OnPaintBrushSettingsChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi ||
                _brushPaintEnabledCheckBox == null ||
                _brushPaintChannelCombo == null ||
                _brushTypeCombo == null ||
                _brushPaintColorPicker == null ||
                _scratchAbrasionTypeCombo == null ||
                _brushSizeSlider == null ||
                _brushOpacitySlider == null ||
                _brushDarknessSlider == null ||
                _brushSpreadSlider == null ||
                _paintCoatMetallicSlider == null ||
                _paintCoatRoughnessSlider == null ||
                _scratchWidthSlider == null ||
                _scratchDepthSlider == null ||
                _scratchResistanceSlider == null ||
                _scratchDepthRampSlider == null ||
                _scratchExposeColorRSlider == null ||
                _scratchExposeColorGSlider == null ||
                _scratchExposeColorBSlider == null)
            {
                return;
            }

            if (ReferenceEquals(sender, _brushPaintEnabledCheckBox))
            {
                if (e.Property != ToggleButton.IsCheckedProperty)
                {
                    return;
                }
            }
            else if (ReferenceEquals(sender, _brushPaintChannelCombo) || ReferenceEquals(sender, _brushTypeCombo) || ReferenceEquals(sender, _scratchAbrasionTypeCombo))
            {
                if (e.Property != ComboBox.SelectedItemProperty)
                {
                    return;
                }
            }
            else if (ReferenceEquals(sender, _brushPaintColorPicker))
            {
                if (!string.Equals(e.Property?.Name, "Color", StringComparison.Ordinal))
                {
                    return;
                }
            }
            else if (e.Property != Slider.ValueProperty)
            {
                return;
            }

            _project.BrushPaintingEnabled = _brushPaintEnabledCheckBox.IsChecked ?? false;
            _project.BrushChannel = _brushPaintChannelCombo.SelectedItem is PaintChannel channel
                ? channel
                : PaintChannel.Rust;
            _project.BrushType = _brushTypeCombo.SelectedItem is PaintBrushType brushType
                ? brushType
                : PaintBrushType.Spray;
            _project.ScratchAbrasionType = _scratchAbrasionTypeCombo.SelectedItem is ScratchAbrasionType abrasionType
                ? abrasionType
                : ScratchAbrasionType.Needle;
            _project.BrushSizePx = (float)_brushSizeSlider.Value;
            _project.BrushOpacity = (float)_brushOpacitySlider.Value;
            _project.BrushDarkness = (float)_brushDarknessSlider.Value;
            _project.BrushSpread = (float)_brushSpreadSlider.Value;
            _project.PaintCoatMetallic = (float)_paintCoatMetallicSlider.Value;
            _project.PaintCoatRoughness = (float)_paintCoatRoughnessSlider.Value;
            _project.PaintColor = ToVector3(_brushPaintColorPicker.Color);
            _project.ScratchWidthPx = (float)_scratchWidthSlider.Value;
            _project.ScratchDepth = (float)_scratchDepthSlider.Value;
            _project.ScratchDragResistance = (float)_scratchResistanceSlider.Value;
            _project.ScratchDepthRamp = (float)_scratchDepthRampSlider.Value;
            _project.ScratchExposeColor = new Vector3(
                (float)_scratchExposeColorRSlider.Value,
                (float)_scratchExposeColorGSlider.Value,
                (float)_scratchExposeColorBSlider.Value);
            UpdateBrushContextUi();
            NotifyRenderOnly();
            _metalViewport?.RefreshPaintHud();
        }

        private void OnClearPaintMask()
        {
            _project.ClearPaintMask();
            _metalViewport?.DiscardPendingPaintStamps();
            _metalViewport?.RequestClearPaintColorTexture();
            _metalViewport?.InvalidateGpu();
            NotifyRenderOnly();
        }

        private static Vector3 ToVector3(Color color)
        {
            return new Vector3(color.R / 255f, color.G / 255f, color.B / 255f);
        }

        private static Color ToAvaloniaColor(Vector3 color)
        {
            byte r = (byte)Math.Clamp((int)MathF.Round(color.X * 255f), 0, 255);
            byte g = (byte)Math.Clamp((int)MathF.Round(color.Y * 255f), 0, 255);
            byte b = (byte)Math.Clamp((int)MathF.Round(color.Z * 255f), 0, 255);
            return Color.FromRgb(r, g, b);
        }
    }
}
