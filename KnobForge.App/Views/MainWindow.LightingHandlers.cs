using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using KnobForge.Core;
using KnobForge.Core.Scene;
using SkiaSharp;
using System;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private void OnLightingModeChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi) return;
            if (_lightingModeCombo == null || e.Property != ComboBox.SelectedItemProperty)
            {
                return;
            }

            if (_lightingModeCombo.SelectedItem is LightingMode mode)
            {
                _project.Mode = mode;
                NotifyProjectStateChanged();
            }
        }

        private void OnLightTypeChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi) return;
            if (!CanMutateSelectedLight(e, ComboBox.SelectedItemProperty, out var light))
            {
                return;
            }

            if (_lightTypeCombo!.SelectedItem is LightType type)
            {
                light.Type = type;
                NotifyProjectStateChanged();
            }
        }

        private void OnRotationChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi) return;
            if (_rotationSlider == null || e.Property != Slider.ValueProperty)
            {
                return;
            }

            var model = GetModelNode();
            if (model == null)
            {
                return;
            }

            model.RotationRadians = (float)_rotationSlider.Value;
            NotifyProjectStateChanged();
        }

        private void OnLightXChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != Slider.ValueProperty) return;
            if (_updatingUi) return;
            if (!CanMutateSelectedLight(e, Slider.ValueProperty, out var light) || _lightXSlider == null)
            {
                return;
            }

            light.X = (float)_lightXSlider.Value;
            NotifyProjectStateChanged();
        }

        private void OnLightYChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi) return;
            if (!CanMutateSelectedLight(e, Slider.ValueProperty, out var light) || _lightYSlider == null)
            {
                return;
            }

            light.Y = (float)_lightYSlider.Value;
            NotifyProjectStateChanged();
        }

        private void OnLightZChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi) return;
            if (!CanMutateSelectedLight(e, Slider.ValueProperty, out var light) || _lightZSlider == null)
            {
                return;
            }

            light.Z = (float)_lightZSlider.Value;
            NotifyProjectStateChanged();
        }

        private void OnDirectionChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi) return;
            if (!CanMutateSelectedLight(e, Slider.ValueProperty, out var light) || _directionSlider == null)
            {
                return;
            }

            light.DirectionRadians = (float)DegreesToRadians(_directionSlider.Value);
            NotifyProjectStateChanged();
        }

        private void OnIntensityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi) return;
            if (!CanMutateSelectedLight(e, Slider.ValueProperty, out var light) || _intensitySlider == null)
            {
                return;
            }

            light.Intensity = (float)_intensitySlider.Value;
            NotifyProjectStateChanged();
        }

        private void OnFalloffChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi) return;
            if (!CanMutateSelectedLight(e, Slider.ValueProperty, out var light) || _falloffSlider == null)
            {
                return;
            }

            light.Falloff = (float)_falloffSlider.Value;
            NotifyProjectStateChanged();
        }

        private void OnColorChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi) return;
            if (!CanMutateSelectedLight(e, Slider.ValueProperty, out var light) ||
                _lightRSlider == null || _lightGSlider == null || _lightBSlider == null)
            {
                return;
            }

            light.Color = new SKColor(
                (byte)Math.Clamp((int)_lightRSlider.Value, 0, 255),
                (byte)Math.Clamp((int)_lightGSlider.Value, 0, 255),
                (byte)Math.Clamp((int)_lightBSlider.Value, 0, 255));
            NotifyProjectStateChanged();
        }

        private void OnDiffuseBoostChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi) return;
            if (!CanMutateSelectedLight(e, Slider.ValueProperty, out var light) || _diffuseBoostSlider == null)
            {
                return;
            }

            light.DiffuseBoost = (float)_diffuseBoostSlider.Value;
            NotifyProjectStateChanged();
        }

        private void OnSpecularBoostChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi) return;
            if (!CanMutateSelectedLight(e, Slider.ValueProperty, out var light) || _specularBoostSlider == null)
            {
                return;
            }

            light.SpecularBoost = (float)_specularBoostSlider.Value;
            NotifyProjectStateChanged();
        }

        private void OnSpecularPowerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_updatingUi) return;
            if (!CanMutateSelectedLight(e, Slider.ValueProperty, out var light) || _specularPowerSlider == null)
            {
                return;
            }

            light.SpecularPower = (float)_specularPowerSlider.Value;
            NotifyProjectStateChanged();
        }
        private bool CanMutateSelectedLight(AvaloniaPropertyChangedEventArgs e, AvaloniaProperty expectedProperty, out KnobLight light)
        {
            light = null!;
            if (_updatingUi || e.Property != expectedProperty)
            {
                return false;
            }

            var selected = _project.SelectedLight;
            if (selected == null)
            {
                return false;
            }

            light = selected;
            return true;
        }
        private static double DegreesToRadians(double degrees)
        {
            return degrees * (Math.PI / 180.0);
        }

        private static double RadiansToDegrees(double radians)
        {
            return radians * (180.0 / Math.PI);
        }
    }
}
