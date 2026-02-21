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
