using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using KnobForge.Core;
using KnobForge.Core.Export;
using KnobForge.Core.Scene;
using KnobForge.Rendering.GPU;
using SkiaSharp;

namespace KnobForge.Rendering;

public sealed partial class KnobExporter
{
        private static Camera BuildExportCamera(
            float referenceRadius,
            KnobExportSettings settings,
            int outputResolution,
            int renderResolution,
            OrientationDebug orientation,
            ViewportCameraState? cameraState)
        {
            if (cameraState.HasValue)
            {
                ViewportCameraState state = cameraState.Value;
                float yaw = state.OrbitYawDeg * (MathF.PI / 180f);
                float pitch = Math.Clamp(state.OrbitPitchDeg, -85f, 85f) * (MathF.PI / 180f);
                Vector3 forward = Vector3.Normalize(new Vector3(
                    MathF.Sin(yaw) * MathF.Cos(pitch),
                    MathF.Sin(pitch),
                    -MathF.Cos(yaw) * MathF.Cos(pitch)));

                Vector3 worldUp = Vector3.UnitY;
                Vector3 right = Vector3.Cross(worldUp, forward);
                if (right.LengthSquared() < 1e-6f)
                {
                    right = Vector3.UnitX;
                }
                else
                {
                    right = Vector3.Normalize(right);
                }

                Vector3 up = Vector3.Normalize(Vector3.Cross(forward, right));

                if (orientation.InvertX)
                {
                    right *= -1f;
                }

                if (orientation.InvertY)
                {
                    up *= -1f;
                }

                if (orientation.InvertZ)
                {
                    forward *= -1f;
                }

                if (orientation.FlipCamera180)
                {
                    forward = -forward;
                    right = -right;
                }

                float distance = MathF.Max(1f, referenceRadius) * 6f;
                Vector3 position = -forward * distance;
                float resolutionScale = renderResolution / (float)Math.Max(1, outputResolution);
                float zoom = Math.Clamp(state.Zoom * resolutionScale, 0.2f, 32f);
                SKPoint pan = new(state.PanPx.X * resolutionScale, state.PanPx.Y * resolutionScale);
                zoom = MathF.Min(zoom, ComputeSafeZoomForFrame(referenceRadius, renderResolution, settings.Padding * resolutionScale, pan));
                return new Camera(position, forward, right, up, zoom, pan);
            }

            // Fallback when launched without a live viewport state.
            Vector3 fallbackForward = new(0f, 0f, 1f);
            Vector3 fallbackWorldUp = new(0f, 1f, 0f);
            Vector3 fallbackRight = Vector3.Normalize(Vector3.Cross(fallbackWorldUp, fallbackForward));
            Vector3 fallbackUp = Vector3.Normalize(Vector3.Cross(fallbackForward, fallbackRight));
            float fallbackDistance = settings.CameraDistanceScale * MathF.Max(1f, referenceRadius);
            Vector3 fallbackPosition = -fallbackForward * fallbackDistance;
            float padding = MathF.Max(0f, settings.Padding);
            float contentPixels = MathF.Max(1f, renderResolution - (padding * 2f));
            float fallbackZoom = contentPixels / MathF.Max(1f, referenceRadius * 2f);
            return new Camera(fallbackPosition, fallbackForward, fallbackRight, fallbackUp, fallbackZoom, SKPoint.Empty);
        }

        private static ViewportCameraState BuildExportViewportCameraState(
            float referenceRadius,
            KnobExportSettings settings,
            int outputResolution,
            int renderResolution,
            ViewportCameraState? cameraState)
        {
            if (cameraState.HasValue)
            {
                ViewportCameraState state = cameraState.Value;
                float resolutionScale = renderResolution / (float)Math.Max(1, outputResolution);
                float zoom = Math.Clamp(state.Zoom * resolutionScale, 0.2f, 32f);
                SKPoint pan = new(state.PanPx.X * resolutionScale, state.PanPx.Y * resolutionScale);
                zoom = MathF.Min(zoom, ComputeSafeZoomForFrame(referenceRadius, renderResolution, settings.Padding * resolutionScale, pan));
                return new ViewportCameraState(state.OrbitYawDeg, state.OrbitPitchDeg, zoom, pan);
            }

            float padding = MathF.Max(0f, settings.Padding);
            float contentPixels = MathF.Max(1f, renderResolution - (padding * 2f));
            float zoomFallback = contentPixels / MathF.Max(1f, referenceRadius * 2f);
            return new ViewportCameraState(30f, -20f, zoomFallback, SKPoint.Empty);
        }

        private static ViewVariant[] ResolveExportViewVariants(KnobExportSettings settings)
        {
            var variants = new List<ViewVariant>(5);
            var dedupe = new HashSet<(int Yaw, int Pitch)>();

            void AddVariant(ViewVariant variant)
            {
                var key = (QuantizeAngle(variant.YawOffsetDeg), QuantizeAngle(variant.PitchOffsetDeg));
                if (dedupe.Add(key))
                {
                    variants.Add(variant);
                }
            }

            AddVariant(new ViewVariant(string.Empty, "Primary", 0f, 0f));

            if (settings.ExportOrbitVariants)
            {
                float yaw = MathF.Abs(settings.OrbitVariantYawOffsetDeg);
                float pitch = MathF.Abs(settings.OrbitVariantPitchOffsetDeg);

                AddVariant(new ViewVariant("under_left", "Under Left", -yaw, pitch));
                AddVariant(new ViewVariant("under_right", "Under Right", yaw, pitch));
                AddVariant(new ViewVariant("over_left", "Over Left", -yaw, -pitch));
                AddVariant(new ViewVariant("over_right", "Over Right", yaw, -pitch));
            }

            return variants.ToArray();
        }

        private static ViewportCameraState ApplyViewVariant(ViewportCameraState baseState, ViewVariant variant)
        {
            float yaw = baseState.OrbitYawDeg + variant.YawOffsetDeg;
            float pitch = Math.Clamp(baseState.OrbitPitchDeg + variant.PitchOffsetDeg, -85f, 85f);
            return new ViewportCameraState(yaw, pitch, baseState.Zoom, baseState.PanPx);
        }

        private static int QuantizeAngle(float value)
        {
            return (int)MathF.Round(value * 1000f);
        }

        private static float ComputeSafeZoomForFrame(
            float referenceRadius,
            int renderResolution,
            float paddingPx,
            SKPoint panPx)
        {
            float radius = MathF.Max(1f, referenceRadius);
            float halfWidthAvailable = MathF.Max(1f, (renderResolution * 0.5f) - paddingPx - MathF.Abs(panPx.X));
            float halfHeightAvailable = MathF.Max(1f, (renderResolution * 0.5f) - paddingPx - MathF.Abs(panPx.Y));
            float halfSpan = MathF.Min(halfWidthAvailable, halfHeightAvailable);
            // Leave a little guard band so rotating protrusions don't clip due rasterization/AA.
            return MathF.Max(0.2f, (halfSpan * 0.96f) / radius);
        }

        private float GetSceneReferenceRadius()
        {
            float maxReferenceRadius = MathF.Max(1f, _renderer.GetMaxModelReferenceRadius());

            MetalMesh? mesh = MetalMeshBuilder.TryBuildFromProject(_project);
            if (mesh != null)
            {
                maxReferenceRadius = MathF.Max(maxReferenceRadius, mesh.ReferenceRadius);
            }

            CollarMesh? collarMesh = CollarMeshBuilder.TryBuildFromProject(_project);
            if (collarMesh != null)
            {
                maxReferenceRadius = MathF.Max(maxReferenceRadius, collarMesh.ReferenceRadius);
            }

            return maxReferenceRadius;
        }
}
