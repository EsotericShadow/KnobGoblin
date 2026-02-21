using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using KnobForge.Rendering;
using SkiaSharp;

namespace KnobForge.App.Controls
{
    public sealed partial class MetalViewport
    {
        private bool TryScreenToScene(SKPoint screenPoint, out SKPoint scenePoint)
        {
            GetScreenCenterPx(out float centerX, out float centerY);
            GetCameraBasis(out Vector3 right, out Vector3 up, out _);

            float m00 = _zoom * right.X;
            float m01 = _zoom * right.Y;
            float m10 = _zoom * up.X;
            float m11 = _zoom * up.Y;
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

            Vector3 worldUp = Vector3.UnitY;
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

            if (_orientation.InvertX)
            {
                right *= -1f;
            }

            if (_orientation.InvertY)
            {
                up *= -1f;
            }

            if (_orientation.InvertZ)
            {
                forward *= -1f;
            }

            if (_orientation.FlipCamera180)
            {
                forward = -forward;
                right = -right;
            }
        }

        private bool ResolveFrontFacingClockwise(Vector3 right, Vector3 up, Vector3 forward)
        {
            // In this renderer we project manually using camera right/up vectors.
            // If the basis is mirrored, front-face winding must be flipped to keep outward culling stable.
            float handedness = Vector3.Dot(Vector3.Cross(right, up), forward);
            return handedness < 0f;
        }

        private static SKPoint ProjectSceneOffset(SKPoint scene, float zoom, Vector3 right, Vector3 up)
        {
            float x = zoom * (scene.X * right.X + scene.Y * right.Y);
            float y = -zoom * (scene.X * up.X + scene.Y * up.Y);
            return new SKPoint(x, y);
        }

        private void ShowOrientationContextMenu(Point pointerDip)
        {
            _debugContextMenu?.Close();

            var invertXItem = new MenuItem
            {
                Header = "Invert X",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _orientation.InvertX
            };
            invertXItem.Click += (_, _) =>
            {
                _orientation.InvertX = !_orientation.InvertX;
                PrintOrientation();
                InvalidateGpu();
            };

            var invertYItem = new MenuItem
            {
                Header = "Invert Y",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _orientation.InvertY
            };
            invertYItem.Click += (_, _) =>
            {
                _orientation.InvertY = !_orientation.InvertY;
                PrintOrientation();
                InvalidateGpu();
            };

            var invertZItem = new MenuItem
            {
                Header = "Invert Z",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _orientation.InvertZ
            };
            invertZItem.Click += (_, _) =>
            {
                _orientation.InvertZ = !_orientation.InvertZ;
                PrintOrientation();
                InvalidateGpu();
            };

            var gizmoInvertXItem = new MenuItem
            {
                Header = "Gizmo Invert X",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _gizmoInvertX
            };
            gizmoInvertXItem.Click += (_, _) =>
            {
                _gizmoInvertX = !_gizmoInvertX;
                PrintOrientation();
                InvalidateGpu();
            };

            var gizmoInvertYItem = new MenuItem
            {
                Header = "Gizmo Invert Y",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _gizmoInvertY
            };
            gizmoInvertYItem.Click += (_, _) =>
            {
                _gizmoInvertY = !_gizmoInvertY;
                PrintOrientation();
                InvalidateGpu();
            };

            var gizmoInvertZItem = new MenuItem
            {
                Header = "Gizmo Invert Z",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _gizmoInvertZ
            };
            gizmoInvertZItem.Click += (_, _) =>
            {
                _gizmoInvertZ = !_gizmoInvertZ;
                PrintOrientation();
                InvalidateGpu();
            };

            var brushInvertXItem = new MenuItem
            {
                Header = "Brush Invert X",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _brushInvertX
            };
            brushInvertXItem.Click += (_, _) =>
            {
                _brushInvertX = !_brushInvertX;
                PrintOrientation();
                InvalidateGpu();
            };

            var brushInvertYItem = new MenuItem
            {
                Header = "Brush Invert Y",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _brushInvertY
            };
            brushInvertYItem.Click += (_, _) =>
            {
                _brushInvertY = !_brushInvertY;
                PrintOrientation();
                InvalidateGpu();
            };

            var brushInvertZItem = new MenuItem
            {
                Header = "Brush Invert Z (Depth)",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _brushInvertZ
            };
            brushInvertZItem.Click += (_, _) =>
            {
                _brushInvertZ = !_brushInvertZ;
                PrintOrientation();
                InvalidateGpu();
            };

            var flipCameraItem = new MenuItem
            {
                Header = "Flip Camera 180°",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _orientation.FlipCamera180
            };
            flipCameraItem.Click += (_, _) =>
            {
                _orientation.FlipCamera180 = !_orientation.FlipCamera180;
                PrintOrientation();
                InvalidateGpu();
            };

            var invertCollarOrbitItem = new MenuItem
            {
                Header = "Invert Collar Orbit (Imported Mesh)",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _invertImportedCollarOrbit
            };
            invertCollarOrbitItem.Click += (_, _) =>
            {
                _invertImportedCollarOrbit = !_invertImportedCollarOrbit;
                PrintOrientation();
                InvalidateGpu();
            };

            var invertKnobFrontFaceWindingItem = new MenuItem
            {
                Header = "Invert Front-Face Winding (Knob)",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _invertKnobFrontFaceWinding
            };
            invertKnobFrontFaceWindingItem.Click += (_, _) =>
            {
                _invertKnobFrontFaceWinding = !_invertKnobFrontFaceWinding;
                PrintOrientation();
                InvalidateGpu();
            };

            var invertImportedStlFrontFaceWindingItem = new MenuItem
            {
                Header = "Invert Front-Face Winding (Imported Mesh Collar)",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _invertImportedStlFrontFaceWinding
            };
            invertImportedStlFrontFaceWindingItem.Click += (_, _) =>
            {
                _invertImportedStlFrontFaceWinding = !_invertImportedStlFrontFaceWinding;
                PrintOrientation();
                InvalidateGpu();
            };

            var resetItem = new MenuItem
            {
                Header = "Reset Orientation"
            };
            resetItem.Click += (_, _) =>
            {
                _orientation = new OrientationDebug
                {
                    InvertX = true,
                    InvertY = true,
                    InvertZ = true,
                    FlipCamera180 = true
                };
                _gizmoInvertX = false;
                _gizmoInvertY = true;
                _gizmoInvertZ = false;
                _brushInvertX = false;
                _brushInvertY = true;
                _brushInvertZ = false;
                _invertImportedCollarOrbit = false;
                _invertKnobFrontFaceWinding = true;
                _invertImportedStlFrontFaceWinding = true;
                PrintOrientation();
                InvalidateGpu();
            };

            _debugContextMenu = new ContextMenu
            {
                Items =
                {
                    invertXItem,
                    invertYItem,
                    invertZItem,
                    new Separator(),
                    gizmoInvertXItem,
                    gizmoInvertYItem,
                    gizmoInvertZItem,
                    new Separator(),
                    brushInvertXItem,
                    brushInvertYItem,
                    brushInvertZItem,
                    new Separator(),
                    flipCameraItem,
                    invertCollarOrbitItem,
                    invertKnobFrontFaceWindingItem,
                    invertImportedStlFrontFaceWindingItem,
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
            Console.WriteLine("---- Orientation Debug ----");
            Console.WriteLine($"InvertX: {_orientation.InvertX}");
            Console.WriteLine($"InvertY: {_orientation.InvertY}");
            Console.WriteLine($"InvertZ: {_orientation.InvertZ}");
            Console.WriteLine($"GizmoInvertX: {_gizmoInvertX}");
            Console.WriteLine($"GizmoInvertY: {_gizmoInvertY}");
            Console.WriteLine($"GizmoInvertZ: {_gizmoInvertZ}");
            Console.WriteLine($"BrushInvertX: {_brushInvertX}");
            Console.WriteLine($"BrushInvertY: {_brushInvertY}");
            Console.WriteLine($"BrushInvertZ: {_brushInvertZ}");
            Console.WriteLine($"FlipCamera180: {_orientation.FlipCamera180}");
            Console.WriteLine($"InvertImportedCollarOrbit: {_invertImportedCollarOrbit}");
            Console.WriteLine($"InvertKnobFrontFaceWinding: {_invertKnobFrontFaceWinding}");
            Console.WriteLine($"InvertImportedStlFrontFaceWinding: {_invertImportedStlFrontFaceWinding}");
            Console.WriteLine("---------------------------");
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

        private static float DegreesToRadians(float degrees)
        {
            return degrees * (MathF.PI / 180f);
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            if (edge1 <= edge0)
            {
                return x < edge0 ? 0f : 1f;
            }

            float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - (2f * t));
        }

        private static IntPtr ToNSString(string value)
        {
            IntPtr utf8Ptr = Marshal.StringToHGlobalAnsi(value);
            try
            {
                return ObjC.IntPtr_objc_msgSend_IntPtr(ObjCClasses.NSString, Selectors.StringWithUTF8String, utf8Ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(utf8Ptr);
            }
        }

        private static string DescribeNSError(IntPtr error)
        {
            if (error == IntPtr.Zero)
            {
                return "unknown error";
            }

            IntPtr description = ObjC.IntPtr_objc_msgSend(error, Selectors.LocalizedDescription);
            if (description == IntPtr.Zero)
            {
                return $"NSError(0x{error.ToInt64():X})";
            }

            IntPtr utf8 = ObjC.IntPtr_objc_msgSend(description, Selectors.UTF8String);
            if (utf8 == IntPtr.Zero)
            {
                return $"NSError(0x{error.ToInt64():X})";
            }

            return Marshal.PtrToStringAnsi(utf8) ?? $"NSError(0x{error.ToInt64():X})";
        }

        private static void LogPaintStampError(string message)
        {
            Console.Error.WriteLine($"[MetalViewport] {message}");
        }
    }
}
