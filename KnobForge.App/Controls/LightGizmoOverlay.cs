using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace KnobForge.App.Controls
{
    public sealed class LightGizmoOverlay : Control
    {
        private int _invalidateQueued;
        private readonly DispatcherTimer _refreshTimer;

        public static readonly StyledProperty<MetalViewport?> ViewportProperty =
            AvaloniaProperty.Register<LightGizmoOverlay, MetalViewport?>(nameof(Viewport));

        static LightGizmoOverlay()
        {
            AffectsRender<LightGizmoOverlay>(ViewportProperty);
        }

        public LightGizmoOverlay()
        {
            ClipToBounds = true;
            IsHitTestVisible = true;
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _refreshTimer.Tick += (_, _) =>
            {
                if (Viewport is not null)
                {
                    InvalidateVisual();
                }
            };
        }

        public MetalViewport? Viewport
        {
            get => GetValue(ViewportProperty);
            set => SetValue(ViewportProperty, value);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            MetalViewport? viewport = Viewport;
            if (viewport is null)
            {
                return;
            }

            var gizmos = viewport.GetLightGizmoSnapshots();
            for (int i = 0; i < gizmos.Count; i++)
            {
                MetalViewport.LightGizmoSnapshot gizmo = gizmos[i];
                Color lineColor = Color.FromArgb(gizmo.LineAlpha, gizmo.ColorR, gizmo.ColorG, gizmo.ColorB);
                Color fillColor = Color.FromArgb(gizmo.FillAlpha, gizmo.ColorR, gizmo.ColorG, gizmo.ColorB);
                var linePen = new Pen(new SolidColorBrush(lineColor), 1.15);
                var fillBrush = new SolidColorBrush(fillColor);
                bool onScreen = IsOnScreen(gizmo.PositionDip);

                if (onScreen)
                {
                    context.DrawLine(linePen, gizmo.OriginDip, gizmo.PositionDip);
                    context.DrawEllipse(fillBrush, null, gizmo.PositionDip, gizmo.RadiusDip, gizmo.RadiusDip);
                }
                else
                {
                    Point edgePoint = ClampToViewport(gizmo.PositionDip);
                    var edgePen = new Pen(new SolidColorBrush(Color.FromArgb(220, 230, 236, 245)), 1.25);
                    context.DrawEllipse(fillBrush, edgePen, edgePoint, 5.5, 5.5);
                }

                if (gizmo.HasDirectionTip)
                {
                    if (onScreen)
                    {
                        context.DrawLine(linePen, gizmo.PositionDip, gizmo.DirectionTipDip);
                        context.DrawEllipse(fillBrush, null, gizmo.DirectionTipDip, gizmo.DirectionTipRadiusDip, gizmo.DirectionTipRadiusDip);
                    }
                }

                if (gizmo.IsSelected)
                {
                    var ringPen = new Pen(new SolidColorBrush(Color.FromArgb(230, 230, 236, 245)), 1.35);
                    Point ringPoint = onScreen ? gizmo.PositionDip : ClampToViewport(gizmo.PositionDip);
                    double ringRadius = onScreen ? gizmo.SelectedRingRadiusDip : 8.5;
                    context.DrawEllipse(null, ringPen, ringPoint, ringRadius, ringRadius);
                }
            }
        }

        private bool IsOnScreen(Point point)
        {
            if (Bounds.Width <= 0 || Bounds.Height <= 0)
            {
                return false;
            }

            const double margin = 1.0;
            return point.X >= margin &&
                point.Y >= margin &&
                point.X <= Bounds.Width - margin &&
                point.Y <= Bounds.Height - margin;
        }

        private Point ClampToViewport(Point point)
        {
            const double edgePadding = 8.0;
            double x = Math.Clamp(point.X, edgePadding, Math.Max(edgePadding, Bounds.Width - edgePadding));
            double y = Math.Clamp(point.Y, edgePadding, Math.Max(edgePadding, Bounds.Height - edgePadding));
            return new Point(x, y);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == BoundsProperty)
            {
                InvalidateVisual();
                return;
            }

            if (change.Property != ViewportProperty)
            {
                return;
            }

            MetalViewport? oldViewport = change.GetOldValue<MetalViewport?>();
            if (oldViewport is not null)
            {
                oldViewport.ViewportFrameRendered -= OnViewportFrameRendered;
            }

            MetalViewport? newViewport = change.GetNewValue<MetalViewport?>();
            if (newViewport is not null)
            {
                newViewport.ViewportFrameRendered += OnViewportFrameRendered;
            }

            InvalidateVisual();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _refreshTimer.Start();
            InvalidateVisual();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _refreshTimer.Stop();
            if (Viewport is MetalViewport viewport)
            {
                viewport.ViewportFrameRendered -= OnViewportFrameRendered;
            }
        }

        private void OnViewportFrameRendered()
        {
            if (Interlocked.Exchange(ref _invalidateQueued, 1) == 1)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref _invalidateQueued, 0);
                InvalidateVisual();
            }, DispatcherPriority.Background);
        }
    }
}
