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
        private static Vector3 RotateAroundZ(Vector3 v, float cosA, float sinA)
        {
            return new Vector3(v.X * cosA - v.Y * sinA, v.X * sinA + v.Y * cosA, v.Z);
        }

        private static SKPoint Project(Vector3 world, Vector3 right, Vector3 up, float centerX, float centerY, float zoom)
        {
            float sx = centerX + Vector3.Dot(world, right) * zoom;
            float sy = centerY - Vector3.Dot(world, up) * zoom;
            return new SKPoint(sx, sy);
        }

        private void GetCameraBasis(float yawDeg, float pitchDeg, out Vector3 right, out Vector3 up, out Vector3 forward)
        {
            float yaw = yawDeg * (MathF.PI / 180f);
            float pitch = Math.Clamp(pitchDeg, -85f, 85f) * (MathF.PI / 180f);

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

            if (_orientation.FlipCamera180)
            {
                forward = -forward;
                right = -right;
            }
        }

        private Vector3 ApplyGizmoOrientation(Vector3 value)
        {
            if (_orientation.InvertX)
            {
                value.X = -value.X;
            }

            if (_orientation.InvertY)
            {
                value.Y = -value.Y;
            }

            if (_orientation.InvertZ)
            {
                value.Z = -value.Z;
            }

            return value;
        }

        private static Vector3 GetDirectionalVector(KnobLight light)
        {
            float z = light.Z / 300f;
            Vector3 dir = new(MathF.Cos(light.DirectionRadians), MathF.Sin(light.DirectionRadians), z);
            if (dir.LengthSquared() < 1e-6f)
            {
                return Vector3.UnitZ;
            }

            return Vector3.Normalize(dir);
        }

        private static void ApplyGeometryLodFlatten(
            ref Vector3 pos,
            ref Vector3 normal,
            float frontZ,
            float geometryKeep,
            float indicatorProtect)
        {
            float topMask = SmoothStep(0.60f, 0.98f, normal.Z);
            float flatten = (1f - geometryKeep) * topMask * (1f - Math.Clamp(indicatorProtect, 0f, 1f));
            if (flatten <= 1e-4f)
            {
                return;
            }

            pos.Z = Lerp(pos.Z, frontZ, flatten);
            Vector3 adjusted = Vector3.Lerp(normal, Vector3.UnitZ, flatten);
            normal = adjusted.LengthSquared() > 1e-8f ? Vector3.Normalize(adjusted) : Vector3.UnitZ;
        }

        private static float ComputeIndicatorFlattenProtect(
            Vector3 worldPos,
            float topRadius,
            ModelNode modelNode,
            float modelCos,
            float modelSin)
        {
            float localX = (worldPos.X * modelCos) + (worldPos.Y * modelSin);
            float localY = (-worldPos.X * modelSin) + (worldPos.Y * modelCos);
            return ComputeIndicatorMask(
                localX,
                localY,
                topRadius,
                modelNode.IndicatorEnabled,
                modelNode.IndicatorShape,
                modelNode.IndicatorWidthRatio,
                modelNode.IndicatorLengthRatioTop,
                modelNode.IndicatorPositionRatio,
                modelNode.IndicatorRoundness);
        }

        private SpiralNormalMap GetOrBuildSpiralNormalMap(
            float referenceRadius,
            float topScale,
            float spiralHeight,
            float spiralWidth,
            float spiralTurns)
        {
            SpiralNormalMapKey key = new(
                MathF.Round(referenceRadius, 3),
                MathF.Round(topScale, 4),
                MathF.Round(spiralHeight, 4),
                MathF.Round(spiralWidth, 4),
                MathF.Round(spiralTurns, 4));

            if (_spiralNormalMap != null && key.Equals(_spiralNormalMapKey))
            {
                return _spiralNormalMap;
            }

            float topRadius = MathF.Max(1e-4f, referenceRadius * topScale);
            Vector3[] normals = BuildSpiralNormalMapNormals(
                SpiralNormalMapSize,
                topRadius,
                spiralHeight,
                spiralWidth,
                spiralTurns);

            _spiralNormalMap = new SpiralNormalMap
            {
                Size = SpiralNormalMapSize,
                Normals = normals
            };
            _spiralNormalMapKey = key;
            return _spiralNormalMap;
        }

        private static Vector3[] BuildSpiralNormalMapNormals(
            int size,
            float topRadius,
            float spiralHeight,
            float spiralWidth,
            float spiralTurns)
        {
            int clampedSize = Math.Clamp(size, 128, 4096);
            var normals = new Vector3[clampedSize * clampedSize];
            float invSizeMinusOne = 1f / MathF.Max(1, clampedSize - 1);
            float epsilon = (2f * topRadius) * invSizeMinusOne;

            for (int y = 0; y < clampedSize; y++)
            {
                float v = (y * invSizeMinusOne * 2f) - 1f;
                float py = v * topRadius;
                for (int x = 0; x < clampedSize; x++)
                {
                    float u = (x * invSizeMinusOne * 2f) - 1f;
                    float px = u * topRadius;
                    int idx = (y * clampedSize) + x;
                    float radialDistance = MathF.Sqrt((px * px) + (py * py));
                    if (radialDistance > topRadius)
                    {
                        normals[idx] = Vector3.UnitZ;
                        continue;
                    }

                    float xL = px - epsilon;
                    float xR = px + epsilon;
                    float yD = py - epsilon;
                    float yU = py + epsilon;

                    float hL = ComputeSpiralRidgeOffset(xL, py, MathF.Sqrt((xL * xL) + (py * py)), topRadius, spiralHeight, spiralWidth, spiralTurns);
                    float hR = ComputeSpiralRidgeOffset(xR, py, MathF.Sqrt((xR * xR) + (py * py)), topRadius, spiralHeight, spiralWidth, spiralTurns);
                    float hD = ComputeSpiralRidgeOffset(px, yD, MathF.Sqrt((px * px) + (yD * yD)), topRadius, spiralHeight, spiralWidth, spiralTurns);
                    float hU = ComputeSpiralRidgeOffset(px, yU, MathF.Sqrt((px * px) + (yU * yU)), topRadius, spiralHeight, spiralWidth, spiralTurns);

                    float dhdx = (hR - hL) / MathF.Max(1e-6f, 2f * epsilon);
                    float dhdy = (hU - hD) / MathF.Max(1e-6f, 2f * epsilon);
                    Vector3 n = Vector3.Normalize(new Vector3(-dhdx, -dhdy, 1f));
                    if (float.IsNaN(n.X) || float.IsNaN(n.Y) || float.IsNaN(n.Z))
                    {
                        n = Vector3.UnitZ;
                    }

                    normals[idx] = n;
                }
            }

            return normals;
        }

        private static Vector3 SampleSpiralNormalBilinear(SpiralNormalMap map, float u, float v)
        {
            float uc = Math.Clamp(u, 0f, 1f);
            float vc = Math.Clamp(v, 0f, 1f);
            float x = uc * (map.Size - 1);
            float y = vc * (map.Size - 1);
            int x0 = (int)MathF.Floor(x);
            int y0 = (int)MathF.Floor(y);
            int x1 = Math.Min(x0 + 1, map.Size - 1);
            int y1 = Math.Min(y0 + 1, map.Size - 1);
            float tx = x - x0;
            float ty = y - y0;

            Vector3 n00 = map.Normals[(y0 * map.Size) + x0];
            Vector3 n10 = map.Normals[(y0 * map.Size) + x1];
            Vector3 n01 = map.Normals[(y1 * map.Size) + x0];
            Vector3 n11 = map.Normals[(y1 * map.Size) + x1];
            Vector3 nx0 = Vector3.Lerp(n00, n10, tx);
            Vector3 nx1 = Vector3.Lerp(n01, n11, tx);
            Vector3 n = Vector3.Lerp(nx0, nx1, ty);
            return n.LengthSquared() > 1e-8f ? Vector3.Normalize(n) : Vector3.UnitZ;
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

        private static float Lerp(float a, float b, float t)
        {
            return a + ((b - a) * t);
        }

        private static void ApplyModeShaping(LightingMode mode, KnobLight light, ref float diffuse, ref float spec)
        {
            switch (mode)
            {
                case LightingMode.Realistic:
                    break;
                case LightingMode.Artistic:
                    diffuse = MathF.Pow(diffuse, 0.80f) * MathF.Max(0f, light.DiffuseBoost);
                    spec = MathF.Pow(spec, 0.65f) * MathF.Max(0f, light.SpecularBoost);
                    break;
                case LightingMode.Both:
                    diffuse = MathF.Pow(diffuse, 0.90f) * (0.65f + 0.35f * MathF.Max(0f, light.DiffuseBoost));
                    spec = MathF.Pow(spec, 0.78f) * (0.65f + 0.35f * MathF.Max(0f, light.SpecularBoost));
                    break;
            }
        }

        private static SKColor WithAlpha(SKColor color, byte alpha)
        {
            return new SKColor(color.Red, color.Green, color.Blue, alpha);
        }

        private static Vector2 Normalize2(Vector2 v)
        {
            float len = v.Length();
            if (len < 1e-6f)
            {
                return new Vector2(1f, 0f);
            }

            return v / len;
        }

    }
}
