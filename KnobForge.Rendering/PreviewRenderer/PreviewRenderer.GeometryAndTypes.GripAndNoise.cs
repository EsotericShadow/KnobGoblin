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
        private static float ComputeCrownOffset(float rNorm, float crownProfile, float radius, float height)
        {
            if (MathF.Abs(crownProfile) <= 1e-5f)
            {
                return 0f;
            }

            float t = 1f - Math.Clamp(rNorm, 0f, 1f);
            float magnitude = MathF.Abs(crownProfile);
            float exponent = 1.6f + ((1f - magnitude) * 1.2f);
            float falloff = MathF.Pow(t, exponent);
            float maxAmplitude = MathF.Min(radius, height) * 0.08f;
            return MathF.Sign(crownProfile) * maxAmplitude * magnitude * falloff;
        }

        private static float ComputeGripOffset(
            GripType gripType,
            float angle,
            float z,
            float sideStartZ,
            float sideEndZ,
            float gripStart,
            float gripHeight,
            float gripDensity,
            float gripPitch,
            float gripDepth,
            float gripWidth,
            float gripSharpness)
        {
            if (gripType == GripType.None || gripDepth <= 1e-6f)
            {
                return 0f;
            }

            float sideSpan = MathF.Max(1e-4f, sideEndZ - sideStartZ);
            float zNorm = Math.Clamp((z - sideStartZ) / sideSpan, 0f, 1f);
            float start = Math.Clamp(gripStart, 0f, 1f);
            float end = Math.Clamp(start + Math.Clamp(gripHeight, 0.05f, 1f), start + 0.001f, 1f);
            if (zNorm < start || zNorm > end)
            {
                return 0f;
            }

            float localZNorm = (zNorm - start) / MathF.Max(1e-4f, end - start);
            float thetaNorm = angle / (MathF.PI * 2f);
            float density = MathF.Max(1f, gripDensity);
            float pitch = MathF.Max(0.2f, gripPitch);
            float u = thetaNorm * density;
            float v = localZNorm * pitch;
            float lineWidth = Math.Clamp(0.02f + (gripWidth * 0.10f), 0.015f, 0.35f);
            float pattern = ComputeKnurlPattern(gripType, u, v, lineWidth);
            float sharpExponent = Math.Clamp(1f + ((Math.Clamp(gripSharpness, 0.5f, 8f) - 1f) * 0.7f), 0.45f, 5f);
            float shape = MathF.Pow(Math.Clamp(pattern, 0f, 1f), sharpExponent);
            float bandFade = SmoothStep(0f, 0.06f, localZNorm) * (1f - SmoothStep(0.94f, 1f, localZNorm));
            float depthWorld = MathF.Max(0f, gripDepth) * sideSpan * 0.016f;
            float depthScale = 0.75f + (MathF.Min(1.5f, gripWidth) * 0.25f);
            return depthWorld * depthScale * shape * bandFade;
        }

        private static float ComputeKnurlPattern(GripType gripType, float u, float v, float width)
        {
            switch (gripType)
            {
                case GripType.VerticalFlutes:
                {
                    return RidgeMask(NearestIntegerDistance(u), width);
                }
                case GripType.DiamondKnurl:
                {
                    float m1 = RidgeMask(NearestIntegerDistance(u + v), width);
                    float m2 = RidgeMask(NearestIntegerDistance(u - v), width);
                    return m1 * m2;
                }
                case GripType.SquareKnurl:
                {
                    float m1 = RidgeMask(NearestIntegerDistance(u), width);
                    float m2 = RidgeMask(NearestIntegerDistance(v), width);
                    return m1 * m2;
                }
                case GripType.HexKnurl:
                {
                    const float cos60 = 0.5f;
                    const float sin60 = 0.8660254f;
                    float m1 = RidgeMask(NearestIntegerDistance(u), width);
                    float m2 = RidgeMask(NearestIntegerDistance((cos60 * u) + (sin60 * v)), width);
                    float m3 = RidgeMask(NearestIntegerDistance((cos60 * u) - (sin60 * v)), width);
                    return ((m1 * m2) + (m2 * m3) + (m3 * m1)) / 3f;
                }
                default:
                    return 0f;
            }
        }

        private static float NearestIntegerDistance(float x)
        {
            return MathF.Abs(Fract(x + 0.5f) - 0.5f);
        }

        private static float RidgeMask(float distance, float width)
        {
            return Math.Clamp(1f - (distance / MathF.Max(width, 1e-4f)), 0f, 1f);
        }

        private static void RecomputeSideNormals(Vector3[] positions, Vector3[] normals, int ringCount, int radialSegments)
        {
            for (int ring = 0; ring < ringCount; ring++)
            {
                int prevRing = Math.Max(0, ring - 1);
                int nextRing = Math.Min(ringCount - 1, ring + 1);
                int ringStart = ring * radialSegments;
                int prevRingStart = prevRing * radialSegments;
                int nextRingStart = nextRing * radialSegments;
                for (int s = 0; s < radialSegments; s++)
                {
                    int prevSeg = (s + radialSegments - 1) % radialSegments;
                    int nextSeg = (s + 1) % radialSegments;
                    Vector3 dTan = positions[ringStart + nextSeg] - positions[ringStart + prevSeg];
                    Vector3 dRing = positions[nextRingStart + s] - positions[prevRingStart + s];
                    Vector3 n = Vector3.Cross(dTan, dRing);
                    if (n.LengthSquared() < 1e-8f)
                    {
                        Vector3 p = positions[ringStart + s];
                        Vector2 xy = new(p.X, p.Y);
                        n = xy.LengthSquared() > 1e-8f
                            ? Vector3.Normalize(new Vector3(xy.X, xy.Y, 0f))
                            : Vector3.UnitX;
                    }
                    else
                    {
                        n = Vector3.Normalize(n);
                    }

                    Vector3 pos = positions[ringStart + s];
                    Vector3 outward = new(pos.X, pos.Y, 0f);
                    if (outward.LengthSquared() > 1e-8f && Vector3.Dot(n, outward) < 0f)
                    {
                        n = -n;
                    }

                    normals[ringStart + s] = n;
                }
            }
        }

        private static float ValueNoise2D(float x, float y)
        {
            int x0 = (int)MathF.Floor(x);
            int y0 = (int)MathF.Floor(y);
            int x1 = x0 + 1;
            int y1 = y0 + 1;
            float tx = x - x0;
            float ty = y - y0;
            float sx = tx * tx * (3f - (2f * tx));
            float sy = ty * ty * (3f - (2f * ty));

            float n00 = Hash2(x0, y0);
            float n10 = Hash2(x1, y0);
            float n01 = Hash2(x0, y1);
            float n11 = Hash2(x1, y1);
            float nx0 = n00 + ((n10 - n00) * sx);
            float nx1 = n01 + ((n11 - n01) * sx);
            return nx0 + ((nx1 - nx0) * sy);
        }

        private static float Hash2(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393) + (uint)(y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0x00FFFFFF) / 16777215f;
            }
        }

        private static float Fract(float x)
        {
            return x - MathF.Floor(x);
        }

        private static float QuantizeDensity(float gripDensity, int radialSegments)
        {
            int segments = Math.Max(1, radialSegments);
            int target = Math.Clamp((int)MathF.Round(gripDensity), 1, segments);
            int best = 1;
            int bestDelta = int.MaxValue;
            for (int d = 1; d <= segments; d++)
            {
                if (segments % d != 0)
                {
                    continue;
                }

                int delta = Math.Abs(d - target);
                if (delta < bestDelta)
                {
                    best = d;
                    bestDelta = delta;
                }
            }

            return MathF.Max(1f, best);
        }
    }
}
