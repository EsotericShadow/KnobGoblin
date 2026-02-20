using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using KnobForge.Core;
using KnobForge.Core.Scene;
using KnobForge.Rendering;
using SkiaSharp;
using System;
using System.Linq;
using System.Numerics;

namespace KnobForge.App.Controls
{
    public sealed class ViewportControl : Control
    {
        private readonly KnobProject _project;
        private readonly PreviewRenderer _renderer;
        private InteractionMode _mode = InteractionMode.None;
        private int _dragLightIndex = -1;
        private float _zoom = 1.0f;
        private SKPoint _panPx = SKPoint.Empty;
        private SKPoint _lastDragScreenPx = SKPoint.Empty;
        private float _orbitYawDeg;
        private float _orbitPitchDeg;
        private ContextMenu? _debugContextMenu;

        public KnobProject Project => _project;
        public OrientationDebug CurrentOrientation => _renderer.Orientation;
        public event EventHandler? ProjectChanged;

        public ViewportControl()
        {
            Focusable = true;
            _project = new KnobProject();
            _renderer = new PreviewRenderer(_project);

            AttachedToVisualTree += (_, _) =>
            {
                RaiseProjectChanged();
            };
            PointerPressed += OnPointerPressed;
            PointerMoved += OnPointerMoved;
            PointerReleased += OnPointerReleased;
            PointerWheelChanged += OnPointerWheelChanged;
            KeyDown += OnKeyDown;
            PointerCaptureLost += (_, _) =>
            {
                _mode = InteractionMode.None;
                _dragLightIndex = -1;
            };
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.FillRectangle(Brushes.DarkSlateGray, new Rect(Bounds.Size));
            context.Custom(new LeaseRenderDrawOp(new Rect(Bounds.Size), _renderer, _zoom, _panPx, _orbitYawDeg, _orbitPitchDeg));
        }

        public void CenterLight()
        {
            _project.EnsureSelection();
            var light = _project.SelectedLight;
            if (light == null)
            {
                return;
            }

            if (Bounds.Width <= 0 || Bounds.Height <= 0)
            {
                return;
            }

            light.X = 0f;
            light.Y = 0f;
            light.Z = 0f;
            RaiseProjectChanged();
            InvalidateVisual();
        }

        public void ResetView()
        {
            _zoom = 1.0f;
            _panPx = SKPoint.Empty;
            _orbitYawDeg = 0f;
            _orbitPitchDeg = 0f;
            InvalidateVisual();
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            Focus();
            var p = e.GetPosition(this);
            var point = e.GetCurrentPoint(this);
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            bool command = e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            bool left = point.Properties.IsLeftButtonPressed;
            bool middle = point.Properties.IsMiddleButtonPressed;

            if (command && left)
            {
                _mode = InteractionMode.OrbitView;
                _lastDragScreenPx = DipToScreen(p);
            }
            else if (middle)
            {
                _mode = InteractionMode.PanView;
                _lastDragScreenPx = DipToScreen(p);
            }
            else if (left && TryHitLight(p, out var hitLightIndex))
            {
                _project.SetSelectedLightIndex(hitLightIndex);
                _dragLightIndex = hitLightIndex;
                _mode = InteractionMode.MoveLight;
            }
            else if (shift && left)
            {
                _mode = InteractionMode.MoveLight;
                _dragLightIndex = _project.SelectedLightIndex;
            }
            else if (left && IsInsideKnob(p))
            {
                _mode = InteractionMode.Rotate;
            }
            else
            {
                _mode = InteractionMode.None;
            }

            if (_mode == InteractionMode.None)
            {
                return;
            }

            e.Pointer.Capture(this);
            if (_mode == InteractionMode.MoveLight)
            {
                if (_dragLightIndex >= 0 && _dragLightIndex < _project.Lights.Count)
                {
                    UpdateLightPosition(_project.Lights[_dragLightIndex], p);
                }
            }
            else if (_mode == InteractionMode.Rotate)
            {
                UpdateRotationFromPointer(p);
            }

            if (_mode == InteractionMode.MoveLight || _mode == InteractionMode.Rotate)
            {
                RaiseProjectChanged();
            }

            InvalidateVisual();
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (_mode == InteractionMode.None)
            {
                return;
            }

            var p = e.GetPosition(this);
            if (_mode == InteractionMode.MoveLight)
            {
                if (_dragLightIndex >= 0 && _dragLightIndex < _project.Lights.Count)
                {
                    UpdateLightPosition(_project.Lights[_dragLightIndex], p);
                }
            }
            else if (_mode == InteractionMode.Rotate)
            {
                UpdateRotationFromPointer(p);
            }
            else
            {
                SKPoint currentScreenPx = DipToScreen(p);
                SKPoint delta = new(currentScreenPx.X - _lastDragScreenPx.X, currentScreenPx.Y - _lastDragScreenPx.Y);

                if (_mode == InteractionMode.PanView)
                {
                    _panPx = new SKPoint(_panPx.X + delta.X, _panPx.Y + delta.Y);
                }
                else if (_mode == InteractionMode.OrbitView)
                {
                    _orbitYawDeg += delta.X * 0.25f;
                    _orbitPitchDeg = Math.Clamp(_orbitPitchDeg + delta.Y * 0.25f, -85f, 85f);
                }

                _lastDragScreenPx = currentScreenPx;
            }

            if (_mode == InteractionMode.MoveLight || _mode == InteractionMode.Rotate)
            {
                RaiseProjectChanged();
            }

            InvalidateVisual();
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (e.InitialPressMouseButton == MouseButton.Right)
            {
                ShowDebugContextMenu(e.GetPosition(this));
                e.Handled = true;
            }

            _mode = InteractionMode.None;
            _dragLightIndex = -1;
            if (e.Pointer.Captured == this)
            {
                e.Pointer.Capture(null);
            }
        }

        private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            float factor = MathF.Exp((float)e.Delta.Y * 0.12f);
            float newZoom = Math.Clamp(_zoom * factor, 0.10f, 8.0f);
            if (Math.Abs(newZoom - _zoom) < 0.0001f)
            {
                return;
            }

            SKPoint screen = DipToScreen(e.GetPosition(this));
            if (!TryScreenToScene(screen, out SKPoint sceneBefore))
            {
                return;
            }

            float viewHalfX = GetViewportWidthPx() * 0.5f;
            float viewHalfY = GetViewportHeightPx() * 0.5f;
            GetCameraBasis(out Vector3 right, out Vector3 up, out _);
            SKPoint offset = ProjectSceneOffset(sceneBefore, newZoom, right, up);

            _zoom = newZoom;
            _panPx = new SKPoint(
                screen.X - viewHalfX - offset.X,
                screen.Y - viewHalfY - offset.Y);

            InvalidateVisual();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.R)
            {
                ResetView();
            }
        }

        public void SetSelectedLightIndex(int index)
        {
            Console.WriteLine(">>> Viewport.SetSelectedLightIndex");
            if (_project.SetSelectedLightIndex(index))
            {
                RaiseProjectChanged();
                InvalidateVisual();
            }
        }

        public void AddLight()
        {
            Console.WriteLine(">>> Viewport.AddLight");
            float offset = 120f * _project.Lights.Count;
            _project.AddLight(offset, offset * 0.25f, 0f);
            RaiseProjectChanged();
            InvalidateVisual();
        }

        public void RemoveSelectedLight()
        {
            Console.WriteLine(">>> Viewport.RemoveSelectedLight");
            if (_project.RemoveSelectedLight())
            {
                RaiseProjectChanged();
                InvalidateVisual();
            }
        }

        public void NotifyProjectStateChanged()
        {
            Console.WriteLine(">>> NotifyProjectStateChanged");
            RaiseProjectChanged();
            InvalidateVisual();
        }

        private void UpdateLightPosition(KnobLight light, Point dipPoint)
        {
            Console.WriteLine(">>> Viewport.UpdateLightPosition");
            if (!TryDipToScene(dipPoint, out SKPoint scene))
            {
                return;
            }

            light.X = scene.X;
            light.Y = scene.Y;
        }

        private void UpdateRotationFromPointer(Point dipPoint)
        {
            Console.WriteLine(">>> Viewport.UpdateRotationFromPointer");
            if (!TryDipToScene(dipPoint, out SKPoint scene))
            {
                return;
            }

            var modelNode = _project.SceneRoot.Children
                .OfType<ModelNode>()
                .FirstOrDefault();
            if (modelNode == null)
            {
                return;
            }

            float angle = MathF.Atan2(scene.Y, scene.X);
            if (angle < 0f)
            {
                angle += 2f * MathF.PI;
            }

            modelNode.RotationRadians = angle;
        }

        private bool TryHitLight(Point dipPoint, out int hitIndex)
        {
            SKPoint screen = DipToScreen(dipPoint);
            float hitRadius = 14f * GetRenderScale();
            float hitRadiusSq = hitRadius * hitRadius;

            GetScreenCenterPx(out float centerX, out float centerY);
            GetCameraBasis(out Vector3 right, out Vector3 up, out _);
            for (int i = _project.Lights.Count - 1; i >= 0; i--)
            {
                var light = _project.Lights[i];
                SceneToScreen(new Vector3(light.X, light.Y, light.Z), centerX, centerY, _zoom, right, up, out float lightX, out float lightY);
                float dx = screen.X - lightX;
                float dy = screen.Y - lightY;
                if ((dx * dx + dy * dy) <= hitRadiusSq)
                {
                    hitIndex = i;
                    return true;
                }
            }

            hitIndex = -1;
            return false;
        }

        private bool IsInsideKnob(Point dipPoint)
        {
            if (!TryDipToScene(dipPoint, out SKPoint scene))
            {
                return false;
            }

            float r = GetKnobRadiusScene();
            double dx = scene.X;
            double dy = scene.Y;
            return (dx * dx + dy * dy) <= (r * r);
        }

        private float GetKnobRadiusScene()
        {
            var modelNode = _project.SceneRoot.Children
                .OfType<ModelNode>()
                .FirstOrDefault();
            if (modelNode == null)
            {
                return 20f;
            }

            return Math.Max(20f, modelNode.Radius);
        }

        private float GetRenderScale()
        {
            return (float)(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0);
        }

        private float GetViewportWidthPx()
        {
            return (float)(Bounds.Width * GetRenderScale());
        }

        private float GetViewportHeightPx()
        {
            return (float)(Bounds.Height * GetRenderScale());
        }

        private void GetScreenCenterPx(out float centerX, out float centerY)
        {
            centerX = GetViewportWidthPx() * 0.5f + _panPx.X;
            centerY = GetViewportHeightPx() * 0.5f + _panPx.Y;
        }

        private SKPoint DipToScreen(Point dipPoint)
        {
            float scale = GetRenderScale();
            return new SKPoint((float)dipPoint.X * scale, (float)dipPoint.Y * scale);
        }

        private bool TryDipToScene(Point dipPoint, out SKPoint scenePoint)
        {
            return TryScreenToScene(DipToScreen(dipPoint), out scenePoint);
        }

        private bool TryScreenToScene(SKPoint screenPoint, out SKPoint scenePoint)
        {
            GetScreenCenterPx(out float centerX, out float centerY);
            GetCameraBasis(out Vector3 right, out Vector3 up, out _);

            // Restrict interaction mapping to the Z=0 modeling plane.
            float m00 = _zoom * right.X;
            float m01 = _zoom * right.Y;
            float m10 = -_zoom * up.X;
            float m11 = -_zoom * up.Y;
            float det = m00 * m11 - m01 * m10;
            if (MathF.Abs(det) < 1e-5f)
            {
                scenePoint = SKPoint.Empty;
                return false;
            }

            float dx = screenPoint.X - centerX;
            float dy = screenPoint.Y - centerY;
            float sx = (dx * m11 - m01 * dy) / det;
            float sy = (m00 * dy - dx * m10) / det;
            scenePoint = new SKPoint(sx, sy);
            return true;
        }

        private void GetCameraBasis(out Vector3 right, out Vector3 up, out Vector3 forward)
        {
            float yaw = DegreesToRadians(_orbitYawDeg);
            float pitch = DegreesToRadians(_orbitPitchDeg);
            forward = Vector3.Normalize(new Vector3(
                MathF.Sin(yaw) * MathF.Cos(pitch),
                MathF.Sin(pitch),
                -MathF.Cos(yaw) * MathF.Cos(pitch)));

            var worldUp = Vector3.UnitY;
            right = Vector3.Cross(worldUp, forward);
            if (right.LengthSquared() < 1e-6f)
            {
                right = Vector3.UnitX;
            }
            else
            {
                right = Vector3.Normalize(right);
            }

            up = Vector3.Normalize(Vector3.Cross(forward, right));

            if (_renderer.Orientation.FlipCamera180)
            {
                forward = -forward;
                right = -right;
            }
        }

        private static SKPoint ProjectSceneOffset(SKPoint scene, float zoom, Vector3 right, Vector3 up)
        {
            float x = zoom * (scene.X * right.X + scene.Y * right.Y);
            float y = -zoom * (scene.X * up.X + scene.Y * up.Y);
            return new SKPoint(x, y);
        }

        private static void SceneToScreen(Vector3 scene, float centerX, float centerY, float zoom, Vector3 right, Vector3 up, out float screenX, out float screenY)
        {
            screenX = centerX + zoom * Vector3.Dot(scene, right);
            screenY = centerY - zoom * Vector3.Dot(scene, up);
        }

        private static float DegreesToRadians(float degrees)
        {
            return degrees * (MathF.PI / 180f);
        }

        private void ShowDebugContextMenu(Point pointerDip)
        {
            _debugContextMenu?.Close();

            OrientationDebug orientation = _renderer.Orientation;

            var invertXItem = new MenuItem
            {
                Header = "Invert X",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = orientation.InvertX
            };
            invertXItem.Click += (_, _) =>
            {
                orientation.InvertX = !orientation.InvertX;
                PrintOrientation();
                InvalidateVisual();
            };

            var invertYItem = new MenuItem
            {
                Header = "Invert Y",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = orientation.InvertY
            };
            invertYItem.Click += (_, _) =>
            {
                orientation.InvertY = !orientation.InvertY;
                PrintOrientation();
                InvalidateVisual();
            };

            var invertZItem = new MenuItem
            {
                Header = "Invert Z",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = orientation.InvertZ
            };
            invertZItem.Click += (_, _) =>
            {
                orientation.InvertZ = !orientation.InvertZ;
                PrintOrientation();
                InvalidateVisual();
            };

            var flipCameraItem = new MenuItem
            {
                Header = "Flip Camera 180°",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = orientation.FlipCamera180
            };
            flipCameraItem.Click += (_, _) =>
            {
                orientation.FlipCamera180 = !orientation.FlipCamera180;
                PrintOrientation();
                InvalidateVisual();
            };

            var resetItem = new MenuItem
            {
                Header = "Reset Orientation"
            };
            resetItem.Click += (_, _) =>
            {
                _renderer.ResetOrientationDebug();
                PrintOrientation();
                InvalidateVisual();
            };

            _debugContextMenu = new ContextMenu
            {
                Items =
                {
                    invertXItem,
                    invertYItem,
                    invertZItem,
                    flipCameraItem,
                    new Separator(),
                    resetItem
                },
                Placement = PlacementMode.Pointer,
                PlacementRect = new Rect(pointerDip, new Size(1, 1))
            };

            _debugContextMenu.Open(this);
        }

        private void PrintOrientation()
        {
            OrientationDebug orientation = _renderer.Orientation;
            Console.WriteLine("---- Orientation Debug ----");
            Console.WriteLine($"InvertX: {orientation.InvertX}");
            Console.WriteLine($"InvertY: {orientation.InvertY}");
            Console.WriteLine($"InvertZ: {orientation.InvertZ}");
            Console.WriteLine($"FlipCamera180: {orientation.FlipCamera180}");
            Console.WriteLine("---------------------------");
        }

        private void RaiseProjectChanged()
        {
            Console.WriteLine(">>> RaiseProjectChanged");
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }

        private enum InteractionMode
        {
            None,
            Rotate,
            MoveLight,
            PanView,
            OrbitView
        }

        private sealed class LeaseRenderDrawOp : ICustomDrawOperation
        {
            private readonly PreviewRenderer _renderer;
            private readonly float _zoom;
            private readonly SKPoint _panPx;
            private readonly float _orbitYawDeg;
            private readonly float _orbitPitchDeg;

            public Rect Bounds { get; }

            public LeaseRenderDrawOp(Rect bounds, PreviewRenderer renderer, float zoom, SKPoint panPx, float orbitYawDeg, float orbitPitchDeg)
            {
                Bounds = bounds;
                _renderer = renderer;
                _zoom = zoom;
                _panPx = panPx;
                _orbitYawDeg = orbitYawDeg;
                _orbitPitchDeg = orbitPitchDeg;
            }

            public void Dispose()
            {
            }

            public bool HitTest(Point p) => false;

            public bool Equals(ICustomDrawOperation? other) => false;

            public void Render(ImmediateDrawingContext context)
            {
                var featureObj = context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature));
                if (featureObj is not ISkiaSharpApiLeaseFeature leaseFeature)
                {
                    return;
                }

                using var lease = leaseFeature.Lease();
                var canvas = lease.SkCanvas;
                var device = canvas.DeviceClipBounds;
                var renderWidth = Math.Max(1, device.Width);
                var renderHeight = Math.Max(1, device.Height);
                _renderer.Render(canvas, renderWidth, renderHeight, _zoom, _panPx, _orbitYawDeg, _orbitPitchDeg);
            }
        }
    }
}
