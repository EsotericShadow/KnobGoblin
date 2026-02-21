using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using KnobForge.Core;
using KnobForge.Core.Scene;
using SkiaSharp;

namespace KnobForge.Rendering
{
    public sealed partial class PreviewRenderer
    {
        public float GetMaxModelReferenceRadius()
        {
            float maxReferenceRadius = 1f;
            foreach (var modelNode in _project.SceneRoot.Children.OfType<ModelNode>())
            {
                MeshCache mesh = GetOrBuildMesh(modelNode);
                float candidate = mesh.ReferenceRadius;
                if (candidate > maxReferenceRadius)
                {
                    maxReferenceRadius = candidate;
                }
            }

            return maxReferenceRadius;
        }

        private void DrawLightGizmos(
            SKCanvas canvas,
            Vector3 right,
            Vector3 up,
            float centerX,
            float centerY,
            float zoom,
            Vector3 cameraPos,
            Vector3 forward,
            float referenceRadius)
        {
            float depthRange = MathF.Max(1f, referenceRadius * 2f);
            Vector3 viewOrigin = -cameraPos;
            float referenceDepth = Vector3.Dot(viewOrigin, forward);

            for (int i = 0; i < _project.Lights.Count; i++)
            {
                var light = _project.Lights[i];
                Vector3 lightPos = ApplyGizmoOrientation(new Vector3(light.X, light.Y, light.Z));
                Vector3 viewLight = lightPos - cameraPos;
                SKPoint g = new(
                    centerX + Vector3.Dot(viewLight, right) * zoom,
                    centerY - Vector3.Dot(viewLight, up) * zoom);
                SKPoint origin = new(
                    centerX + Vector3.Dot(viewOrigin, right) * zoom,
                    centerY - Vector3.Dot(viewOrigin, up) * zoom);
                bool isSelected = i == _project.SelectedLightIndex;

                float depth = Vector3.Dot(viewLight, forward);
                float depthOffset = (depth - referenceDepth) / depthRange;
                float nearFactor = (1f - Math.Clamp(depthOffset, -1f, 1f)) * 0.5f;
                float gizmoRadius = 4f + (nearFactor * 5f);
                byte gizmoAlpha = (byte)(110 + (nearFactor * 145f));
                byte lineAlpha = (byte)(70 + (nearFactor * 120f));
                byte textAlpha = (byte)(120 + (nearFactor * 120f));

                _gizmoLinePaint.Color = WithAlpha(light.Color, lineAlpha);
                _gizmoFillPaint.Color = WithAlpha(light.Color, gizmoAlpha);
                _directionPaint.Color = WithAlpha(light.Color, gizmoAlpha);
                _gizmoTextPaint.Color = new SKColor(230, 236, 245, textAlpha);

                canvas.DrawLine(g, origin, _gizmoLinePaint);
                canvas.DrawCircle(g, gizmoRadius, _gizmoFillPaint);

                string zLabel = lightPos.Z >= 0f ? "Z+" : "Z-";
                canvas.DrawText(
                    zLabel,
                    g.X + gizmoRadius + 4f,
                    g.Y - gizmoRadius - 2f,
                    SKTextAlign.Left,
                    _gizmoFont,
                    _gizmoTextPaint);

                if (light.Type == LightType.Directional)
                {
                    Vector3 lightDir = GetDirectionalVector(light);
                    lightDir = ApplyGizmoOrientation(lightDir);
                    SKPoint d2 = new(
                        g.X + (lightDir.X * right.X + lightDir.Y * right.Y + lightDir.Z * right.Z) * 20f,
                        g.Y - (lightDir.X * up.X + lightDir.Y * up.Y + lightDir.Z * up.Z) * 20f);
                    canvas.DrawLine(g, d2, _directionPaint);
                    canvas.DrawCircle(d2, 2.5f, _directionPaint);
                }

                if (isSelected)
                {
                    canvas.DrawCircle(g, 10f, _gizmoRingPaint);
                }
            }
        }

    }
}
