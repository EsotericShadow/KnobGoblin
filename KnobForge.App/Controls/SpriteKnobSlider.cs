using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;

namespace KnobForge.App.Controls
{
    public class SpriteKnobSlider : Slider
    {
        public static readonly StyledProperty<string> SpriteSheetPathProperty =
            AvaloniaProperty.Register<SpriteKnobSlider, string>(
                nameof(SpriteSheetPath),
                "Assets/green_channel_strip_over_right_spritesheet.png");

        public static readonly StyledProperty<int> FrameCountProperty =
            AvaloniaProperty.Register<SpriteKnobSlider, int>(nameof(FrameCount), 156);

        public static readonly StyledProperty<int> ColumnCountProperty =
            AvaloniaProperty.Register<SpriteKnobSlider, int>(nameof(ColumnCount), 13);

        public static readonly StyledProperty<int> FrameWidthProperty =
            AvaloniaProperty.Register<SpriteKnobSlider, int>(nameof(FrameWidth), 128);

        public static readonly StyledProperty<int> FrameHeightProperty =
            AvaloniaProperty.Register<SpriteKnobSlider, int>(nameof(FrameHeight), 128);

        public static readonly StyledProperty<int> FramePaddingProperty =
            AvaloniaProperty.Register<SpriteKnobSlider, int>(nameof(FramePadding), 12);

        public static readonly StyledProperty<int> FrameStartXProperty =
            AvaloniaProperty.Register<SpriteKnobSlider, int>(nameof(FrameStartX), 12);

        public static readonly StyledProperty<int> FrameStartYProperty =
            AvaloniaProperty.Register<SpriteKnobSlider, int>(nameof(FrameStartY), 12);

        public static readonly StyledProperty<double> KnobDiameterProperty =
            AvaloniaProperty.Register<SpriteKnobSlider, double>(nameof(KnobDiameter), 128d);

        public static readonly StyledProperty<double> DragPixelsForFullRangeProperty =
            AvaloniaProperty.Register<SpriteKnobSlider, double>(nameof(DragPixelsForFullRange), 220d);

        public static readonly DirectProperty<SpriteKnobSlider, IImage?> CurrentFrameProperty =
            AvaloniaProperty.RegisterDirect<SpriteKnobSlider, IImage?>(
                nameof(CurrentFrame),
                knob => knob.CurrentFrame);

        public static readonly DirectProperty<SpriteKnobSlider, double> EffectiveKnobDiameterProperty =
            AvaloniaProperty.RegisterDirect<SpriteKnobSlider, double>(
                nameof(EffectiveKnobDiameter),
                knob => knob.EffectiveKnobDiameter);

        private bool _dragging;
        private Point _dragStart;
        private double _valueAtDragStart;
        private IImage? _currentFrame;
        private double _effectiveKnobDiameter = 128d;
        private Bitmap? _sheetBitmap;
        private List<IImage>? _frames;

        public string SpriteSheetPath
        {
            get => GetValue(SpriteSheetPathProperty);
            set => SetValue(SpriteSheetPathProperty, value);
        }

        public int FrameCount
        {
            get => GetValue(FrameCountProperty);
            set => SetValue(FrameCountProperty, value);
        }

        public int ColumnCount
        {
            get => GetValue(ColumnCountProperty);
            set => SetValue(ColumnCountProperty, value);
        }

        public int FrameWidth
        {
            get => GetValue(FrameWidthProperty);
            set => SetValue(FrameWidthProperty, value);
        }

        public int FrameHeight
        {
            get => GetValue(FrameHeightProperty);
            set => SetValue(FrameHeightProperty, value);
        }

        public int FramePadding
        {
            get => GetValue(FramePaddingProperty);
            set => SetValue(FramePaddingProperty, value);
        }

        public int FrameStartX
        {
            get => GetValue(FrameStartXProperty);
            set => SetValue(FrameStartXProperty, value);
        }

        public int FrameStartY
        {
            get => GetValue(FrameStartYProperty);
            set => SetValue(FrameStartYProperty, value);
        }

        public double KnobDiameter
        {
            get => GetValue(KnobDiameterProperty);
            set => SetValue(KnobDiameterProperty, value);
        }

        public double DragPixelsForFullRange
        {
            get => GetValue(DragPixelsForFullRangeProperty);
            set => SetValue(DragPixelsForFullRangeProperty, value);
        }

        public IImage? CurrentFrame
        {
            get => _currentFrame;
            private set => SetAndRaise(CurrentFrameProperty, ref _currentFrame, value);
        }

        public double EffectiveKnobDiameter
        {
            get => _effectiveKnobDiameter;
            private set => SetAndRaise(EffectiveKnobDiameterProperty, ref _effectiveKnobDiameter, value);
        }

        public SpriteKnobSlider()
        {
            Focusable = true;
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == SpriteSheetPathProperty ||
                change.Property == FrameCountProperty ||
                change.Property == ColumnCountProperty ||
                change.Property == FrameWidthProperty ||
                change.Property == FrameHeightProperty ||
                change.Property == FramePaddingProperty ||
                change.Property == FrameStartXProperty ||
                change.Property == FrameStartYProperty)
            {
                ResetFrameCache();
                UpdateCurrentFrame();
                return;
            }

            if (change.Property == KnobDiameterProperty)
            {
                UpdateEffectiveKnobDiameter();
                return;
            }

            if (change.Property == ValueProperty ||
                change.Property == MinimumProperty ||
                change.Property == MaximumProperty)
            {
                UpdateCurrentFrame();
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            UpdateEffectiveKnobDiameter();
            UpdateCurrentFrame();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            ResetFrameCache();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            _dragging = true;
            _dragStart = point.Position;
            _valueAtDragStart = Value;
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            if (!_dragging)
            {
                return;
            }

            Point current = e.GetCurrentPoint(this).Position;
            double deltaY = _dragStart.Y - current.Y;
            double deltaX = current.X - _dragStart.X;
            double blendedDelta = deltaY + (deltaX * 0.35d);
            SetValueFromDelta(blendedDelta);
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            if (e.Pointer.Captured == this)
            {
                e.Pointer.Capture(null);
            }
            e.Handled = true;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            _dragging = false;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);

            double range = Maximum - Minimum;
            if (range <= 0d)
            {
                return;
            }

            int frameSteps = Math.Max(2, FrameCount);
            double step = range / (frameSteps - 1);
            double delta = (e.Delta.Y + (e.Delta.X * 0.25d)) * step;
            SetCurrentValue(ValueProperty, Math.Clamp(Value + delta, Minimum, Maximum));
            e.Handled = true;
        }

        private void SetValueFromDelta(double dragDeltaPx)
        {
            double range = Maximum - Minimum;
            if (range <= 0d)
            {
                return;
            }

            double fullRangePixels = Math.Max(12d, DragPixelsForFullRange);
            double valueDelta = (dragDeltaPx / fullRangePixels) * range;
            double value = Math.Clamp(_valueAtDragStart + valueDelta, Minimum, Maximum);
            SetCurrentValue(ValueProperty, value);
        }

        private void UpdateCurrentFrame()
        {
            EnsureFramesLoaded();

            if (_frames == null || _frames.Count == 0)
            {
                CurrentFrame = null;
                return;
            }

            double range = Maximum - Minimum;
            double normalized = range > 0d ? (Value - Minimum) / range : 0d;
            normalized = Math.Clamp(normalized, 0d, 1d);
            normalized = 1d - normalized;
            int index = (int)Math.Round(normalized * (_frames.Count - 1));
            index = Math.Clamp(index, 0, _frames.Count - 1);
            CurrentFrame = _frames[index];
        }

        private void EnsureFramesLoaded()
        {
            if (_frames != null)
            {
                return;
            }

            string? resolvedPath = ResolveSpriteSheetPath(SpriteSheetPath);
            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                _frames = new List<IImage>();
                return;
            }

            _sheetBitmap = new Bitmap(resolvedPath);
            _frames = BuildFrames(_sheetBitmap);
        }

        private List<IImage> BuildFrames(Bitmap sheet)
        {
            var frames = new List<IImage>();

            int frameCount = Math.Max(1, FrameCount);
            int columns = Math.Max(1, ColumnCount);
            int frameWidth = Math.Max(1, FrameWidth);
            int frameHeight = Math.Max(1, FrameHeight);
            int step = Math.Max(1, frameWidth + FramePadding);

            for (int index = 0; index < frameCount; index++)
            {
                int col = index % columns;
                int row = index / columns;
                int x = FrameStartX + (col * step);
                int y = FrameStartY + (row * step);

                if (x < 0 || y < 0 ||
                    x + frameWidth > sheet.PixelSize.Width ||
                    y + frameHeight > sheet.PixelSize.Height)
                {
                    break;
                }

                var cropRect = new PixelRect(x, y, frameWidth, frameHeight);
                frames.Add(new CroppedBitmap(sheet, cropRect));
            }

            return frames;
        }

        private static string? ResolveSpriteSheetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (Path.IsPathRooted(path))
            {
                return path;
            }

            string fromAppBase = Path.Combine(AppContext.BaseDirectory, path);
            if (File.Exists(fromAppBase))
            {
                return fromAppBase;
            }

            string fromCwd = Path.GetFullPath(path);
            if (File.Exists(fromCwd))
            {
                return fromCwd;
            }

            return fromAppBase;
        }

        private void UpdateEffectiveKnobDiameter()
        {
            double renderScale = 1d;
            if (VisualRoot is TopLevel topLevel && topLevel.RenderScaling > 0.01d)
            {
                renderScale = topLevel.RenderScaling;
            }

            EffectiveKnobDiameter = Math.Max(1d, KnobDiameter / renderScale);
        }

        private void ResetFrameCache()
        {
            if (_frames != null)
            {
                _frames.Clear();
                _frames = null;
            }

            _sheetBitmap?.Dispose();
            _sheetBitmap = null;
            CurrentFrame = null;
        }
    }
}
