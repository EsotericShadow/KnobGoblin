using System.Numerics;
using SkiaSharp;

namespace KnobForge.Rendering
{
    public readonly record struct ViewportCameraState(
        float OrbitYawDeg,
        float OrbitPitchDeg,
        float Zoom,
        SKPoint PanPx);

    public readonly record struct Camera(
        Vector3 Position,
        Vector3 Forward,
        Vector3 Right,
        Vector3 Up,
        float Zoom,
        SKPoint PanPx);
}
