using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Globalization;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private const double DefaultEnvIntensity = 0.36;
        private const double DefaultEnvRoughnessMix = 1.00;
        private const double DefaultShadowStrength = 1.00;
        private const double DefaultShadowSoftness = 0.55;
        private const double DefaultShadowQuality = 0.65;

        private void InitializePrecisionControls()
        {
            WirePrecisionTextEntry(_envIntensityInputTextBox, _envIntensitySlider);
            WirePrecisionTextEntry(_envRoughnessMixInputTextBox, _envRoughnessMixSlider);
            WirePrecisionTextEntry(_shadowStrengthInputTextBox, _shadowStrengthSlider);
            WirePrecisionTextEntry(_shadowSoftnessInputTextBox, _shadowSoftnessSlider);
            WirePrecisionTextEntry(_shadowQualityInputTextBox, _shadowQualitySlider);
            WirePrecisionTextEntry(_collarScaleInputTextBox, _collarScaleSlider, "0.000");
            WirePrecisionTextEntry(_collarBodyLengthInputTextBox, _collarBodyLengthSlider, "0.000");
            WirePrecisionTextEntry(_collarBodyThicknessInputTextBox, _collarBodyThicknessSlider, "0.000");
            WirePrecisionTextEntry(_collarHeadLengthInputTextBox, _collarHeadLengthSlider, "0.000");
            WirePrecisionTextEntry(_collarHeadThicknessInputTextBox, _collarHeadThicknessSlider, "0.000");
            WirePrecisionTextEntry(_collarRotateInputTextBox, _collarRotateSlider, "0.00");
            WirePrecisionTextEntry(_collarOffsetXInputTextBox, _collarOffsetXSlider, "0.000");
            WirePrecisionTextEntry(_collarOffsetYInputTextBox, _collarOffsetYSlider, "0.000");
            WirePrecisionTextEntry(_collarElevationInputTextBox, _collarElevationSlider, "0.000");
            WirePrecisionTextEntry(_collarInflateInputTextBox, _collarInflateSlider, "0.0000");
            WirePrecisionTextEntry(_brushSizeInputTextBox, _brushSizeSlider, "0.0");
            WirePrecisionTextEntry(_brushOpacityInputTextBox, _brushOpacitySlider, "0.000");
            WirePrecisionTextEntry(_brushDarknessInputTextBox, _brushDarknessSlider, "0.000");
            WirePrecisionTextEntry(_brushSpreadInputTextBox, _brushSpreadSlider, "0.000");
            WirePrecisionTextEntry(_paintCoatMetallicInputTextBox, _paintCoatMetallicSlider, "0.000");
            WirePrecisionTextEntry(_paintCoatRoughnessInputTextBox, _paintCoatRoughnessSlider, "0.000");
            WirePrecisionTextEntry(_scratchWidthInputTextBox, _scratchWidthSlider, "0.0");
            WirePrecisionTextEntry(_scratchDepthInputTextBox, _scratchDepthSlider, "0.000");
            WirePrecisionTextEntry(_scratchResistanceInputTextBox, _scratchResistanceSlider, "0.000");
            WirePrecisionTextEntry(_scratchDepthRampInputTextBox, _scratchDepthRampSlider, "0.0000");
            WirePrecisionTextEntry(_scratchExposeColorRInputTextBox, _scratchExposeColorRSlider, "0.000");
            WirePrecisionTextEntry(_scratchExposeColorGInputTextBox, _scratchExposeColorGSlider, "0.000");
            WirePrecisionTextEntry(_scratchExposeColorBInputTextBox, _scratchExposeColorBSlider, "0.000");

            WireResetButton(_envIntensityResetButton, _envIntensitySlider, DefaultEnvIntensity);
            WireResetButton(_envRoughnessMixResetButton, _envRoughnessMixSlider, DefaultEnvRoughnessMix);
            WireResetButton(_shadowStrengthResetButton, _shadowStrengthSlider, DefaultShadowStrength);
            WireResetButton(_shadowSoftnessResetButton, _shadowSoftnessSlider, DefaultShadowSoftness);
            WireResetButton(_shadowQualityResetButton, _shadowQualitySlider, DefaultShadowQuality);

            AttachPrecisionNudgeHandlers(_envIntensitySlider);
            AttachPrecisionNudgeHandlers(_envRoughnessMixSlider);
            AttachPrecisionNudgeHandlers(_shadowStrengthSlider);
            AttachPrecisionNudgeHandlers(_shadowSoftnessSlider);
            AttachPrecisionNudgeHandlers(_shadowQualitySlider);
        }

        private static void WirePrecisionTextEntry(TextBox? input, Slider? slider, string format = "0.000")
        {
            if (input == null || slider == null)
            {
                return;
            }

            input.Tag = format;
            input.LostFocus += (_, _) => ApplyPrecisionTextEntry(input, slider);
            input.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter)
                {
                    return;
                }

                ApplyPrecisionTextEntry(input, slider);
                e.Handled = true;
            };
        }

        private static void ApplyPrecisionTextEntry(TextBox input, Slider slider)
        {
            string text = (input.Text ?? string.Empty).Trim();
            if (!double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double parsed))
            {
                input.Text = slider.Value.ToString(GetPrecisionFormat(input), CultureInfo.InvariantCulture);
                return;
            }

            double clamped = Math.Clamp(parsed, slider.Minimum, slider.Maximum);
            slider.Value = clamped;
        }

        private static void WireResetButton(Button? button, Slider? slider, double defaultValue)
        {
            if (button == null || slider == null)
            {
                return;
            }

            button.Click += (_, _) => slider.Value = Math.Clamp(defaultValue, slider.Minimum, slider.Maximum);
        }

        private static void AttachPrecisionNudgeHandlers(Slider? slider)
        {
            if (slider == null)
            {
                return;
            }

            slider.AddHandler(InputElement.PointerWheelChangedEvent, OnPrecisionSliderPointerWheel, RoutingStrategies.Tunnel);
            slider.KeyDown += OnPrecisionSliderKeyDown;
        }

        private static void OnPrecisionSliderPointerWheel(object? sender, PointerWheelEventArgs e)
        {
            if (sender is not Slider slider)
            {
                return;
            }

            double direction = e.Delta.Y >= 0 ? 1d : -1d;
            double delta = GetPrecisionNudgeStep(slider, e.KeyModifiers) * direction;
            slider.Value = Math.Clamp(slider.Value + delta, slider.Minimum, slider.Maximum);
            e.Handled = true;
        }

        private static void OnPrecisionSliderKeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is not Slider slider)
            {
                return;
            }

            int direction = e.Key switch
            {
                Key.Up => 1,
                Key.Right => 1,
                Key.Down => -1,
                Key.Left => -1,
                _ => 0
            };
            if (direction == 0)
            {
                return;
            }

            double delta = GetPrecisionNudgeStep(slider, e.KeyModifiers) * direction;
            slider.Value = Math.Clamp(slider.Value + delta, slider.Minimum, slider.Maximum);
            e.Handled = true;
        }

        private static double GetPrecisionNudgeStep(Slider slider, KeyModifiers modifiers)
        {
            double range = Math.Max(slider.Maximum - slider.Minimum, 1e-6);
            if (modifiers.HasFlag(KeyModifiers.Shift))
            {
                return range * 0.040;
            }

            if (modifiers.HasFlag(KeyModifiers.Alt))
            {
                return range * 0.0025;
            }

            return range * 0.010;
        }

        private void UpdatePrecisionControlEntryText()
        {
            SyncPrecisionEntry(_envIntensityInputTextBox, _envIntensitySlider);
            SyncPrecisionEntry(_envRoughnessMixInputTextBox, _envRoughnessMixSlider);
            SyncPrecisionEntry(_shadowStrengthInputTextBox, _shadowStrengthSlider);
            SyncPrecisionEntry(_shadowSoftnessInputTextBox, _shadowSoftnessSlider);
            SyncPrecisionEntry(_shadowQualityInputTextBox, _shadowQualitySlider);
            SyncPrecisionEntry(_collarScaleInputTextBox, _collarScaleSlider);
            SyncPrecisionEntry(_collarBodyLengthInputTextBox, _collarBodyLengthSlider);
            SyncPrecisionEntry(_collarBodyThicknessInputTextBox, _collarBodyThicknessSlider);
            SyncPrecisionEntry(_collarHeadLengthInputTextBox, _collarHeadLengthSlider);
            SyncPrecisionEntry(_collarHeadThicknessInputTextBox, _collarHeadThicknessSlider);
            SyncPrecisionEntry(_collarRotateInputTextBox, _collarRotateSlider);
            SyncPrecisionEntry(_collarOffsetXInputTextBox, _collarOffsetXSlider);
            SyncPrecisionEntry(_collarOffsetYInputTextBox, _collarOffsetYSlider);
            SyncPrecisionEntry(_collarElevationInputTextBox, _collarElevationSlider);
            SyncPrecisionEntry(_collarInflateInputTextBox, _collarInflateSlider);
            SyncPrecisionEntry(_brushSizeInputTextBox, _brushSizeSlider);
            SyncPrecisionEntry(_brushOpacityInputTextBox, _brushOpacitySlider);
            SyncPrecisionEntry(_brushDarknessInputTextBox, _brushDarknessSlider);
            SyncPrecisionEntry(_brushSpreadInputTextBox, _brushSpreadSlider);
            SyncPrecisionEntry(_paintCoatMetallicInputTextBox, _paintCoatMetallicSlider);
            SyncPrecisionEntry(_paintCoatRoughnessInputTextBox, _paintCoatRoughnessSlider);
            SyncPrecisionEntry(_scratchWidthInputTextBox, _scratchWidthSlider);
            SyncPrecisionEntry(_scratchDepthInputTextBox, _scratchDepthSlider);
            SyncPrecisionEntry(_scratchResistanceInputTextBox, _scratchResistanceSlider);
            SyncPrecisionEntry(_scratchDepthRampInputTextBox, _scratchDepthRampSlider);
            SyncPrecisionEntry(_scratchExposeColorRInputTextBox, _scratchExposeColorRSlider);
            SyncPrecisionEntry(_scratchExposeColorGInputTextBox, _scratchExposeColorGSlider);
            SyncPrecisionEntry(_scratchExposeColorBInputTextBox, _scratchExposeColorBSlider);
        }

        private static void SyncPrecisionEntry(TextBox? input, Slider? slider)
        {
            if (input == null || slider == null || input.IsFocused)
            {
                return;
            }

            string text = slider.Value.ToString(GetPrecisionFormat(input), CultureInfo.InvariantCulture);
            if (!string.Equals(input.Text, text, StringComparison.Ordinal))
            {
                input.Text = text;
            }
        }

        private static string GetPrecisionFormat(TextBox input)
        {
            return input.Tag as string ?? "0.000";
        }
    }
}
