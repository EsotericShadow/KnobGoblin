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
        private static float ComputeSpiralRidgeOffset(
            float x,
            float y,
            float radialDistance,
            float topRadius,
            float ridgeHeight,
            float ridgeWidth,
            float spiralTurns)
        {
            if (ridgeHeight <= 0f || topRadius <= 1e-6f || spiralTurns <= 1e-6f)
            {
                return 0f;
            }

            float rNorm = Math.Clamp(radialDistance / topRadius, 0f, 1f);
            float theta = MathF.Atan2(y, x);
            if (theta < 0f)
            {
                theta += MathF.PI * 2f;
            }

            float thetaNorm = theta / (MathF.PI * 2f);
            float ringCount = MathF.Max(1f, spiralTurns);
            float phaseNoise = ValueNoise2D((rNorm * 60f) + 17.2f, (thetaNorm * 40f) + 9.7f);
            float phaseJitter = (phaseNoise - 0.5f) * 0.15f;
            float phase = (rNorm * ringCount) + phaseJitter;
            float nearest = MathF.Round(phase);
            float absDist = MathF.Abs((phase - nearest) / ringCount);

            float microHeight = ridgeHeight * 0.075f;
            float widthNoise = ValueNoise2D((rNorm * 80f) + 2.3f, (thetaNorm * 48f) + 4.1f);
            float widthJitter = 1f + ((widthNoise - 0.5f) * 0.08f);
            float widthNorm = MathF.Max(1e-6f, (ridgeWidth * 1.25f * widthJitter) / MathF.Max(topRadius, 1e-4f));
            float halfWidth = widthNorm * 0.5f;
            if (absDist >= halfWidth)
            {
                return 0f;
            }

            float t = absDist / halfWidth;
            float vProfile = 1f - t;
            vProfile *= vProfile;
            vProfile *= vProfile;
            float heightNoise = ValueNoise2D((rNorm * 96f) + 13.9f, (thetaNorm * 56f) + 5.8f);
            float heightJitter = 1f + ((heightNoise - 0.5f) * 0.04f);
            float edgeT = Math.Clamp((rNorm - 0.975f) / 0.025f, 0f, 1f);
            float edgeFade = 1f - (edgeT * edgeT * (3f - (2f * edgeT)));
            return -microHeight * heightJitter * vProfile * edgeFade;
        }

        private static float ComputeBodyRadius(float t, float radiusStart, float radiusEnd, float bodyBulge)
        {
            float baseRadius = radiusStart + ((radiusEnd - radiusStart) * t);
            float arch = 1f - MathF.Pow((2f * t) - 1f, 2f);
            float bulgeScale = radiusStart * bodyBulge * 0.22f;
            return MathF.Max(1f, baseRadius + (bulgeScale * arch));
        }

        private static float ComputeIndicatorOffset(
            float x,
            float y,
            float topRadius,
            bool enabled,
            IndicatorShape shape,
            IndicatorRelief relief,
            IndicatorProfile profile,
            float widthRatio,
            float lengthRatio,
            float positionRatio,
            float thicknessRatio,
            float roundness)
        {
            if (!enabled || thicknessRatio <= 1e-6f || topRadius <= 1e-6f)
            {
                return 0f;
            }

            Vector2 p = new(x / topRadius, y / topRadius);
            float t = p.Y;
            float start = Math.Clamp(positionRatio, 0.05f, 0.90f);
            float end = Math.Clamp(start + lengthRatio, start + 1e-4f, 0.98f);
            float halfWidth = MathF.Max(0.001f, widthRatio * 0.5f);
            float along = (t - start) / MathF.Max(1e-4f, end - start);
            float v;
            float edgeDistance;
            if (shape == IndicatorShape.Dot)
            {
                float dotRadius = halfWidth;
                float centerY = end - MathF.Min(dotRadius * 0.35f, (end - start) * 0.25f);
                float dx = p.X;
                float dy = p.Y - centerY;
                v = MathF.Sqrt((dx * dx) + (dy * dy)) / MathF.Max(dotRadius, 1e-6f);
                edgeDistance = v;
            }
            else
            {
                if (t < start || t > end)
                {
                    return 0f;
                }

                float localHalfWidth = halfWidth;
                if (shape == IndicatorShape.Tapered)
                {
                    localHalfWidth *= MathF.Max(0.20f, 1f - (along * 0.80f));
                }
                else if (shape == IndicatorShape.Needle)
                {
                    localHalfWidth *= MathF.Max(0.06f, 1f - (along * 0.94f));
                }
                else if (shape == IndicatorShape.Triangle)
                {
                    localHalfWidth *= MathF.Max(0.02f, 1f - along);
                }

                if (shape == IndicatorShape.Diamond)
                {
                    float qx = MathF.Abs(p.X) / MathF.Max(halfWidth, 1e-6f);
                    float qy = MathF.Abs((along * 2f) - 1f);
                    v = qx + qy;
                    edgeDistance = v;
                }
                else
                {
                    v = MathF.Abs(p.X) / MathF.Max(localHalfWidth, 1e-6f);
                    edgeDistance = v;
                }
            }

            if (edgeDistance >= 1f)
            {
                return 0f;
            }

            float edgeMask;
            if (profile == IndicatorProfile.Straight)
            {
                // Straight profile should remain hard-edged, independent from roundness feathering.
                edgeMask = 1f;
            }
            else if (roundness <= 1e-4f)
            {
                edgeMask = 1f;
            }
            else
            {
                float feather = Math.Clamp(roundness, 0f, 1f) * 0.45f;
                edgeMask = 1f - SmoothStep(1f - feather, 1f, edgeDistance);
            }

            float capMask = 1f;
            if (shape == IndicatorShape.Capsule)
            {
                float endDistance = MathF.Min(along, 1f - along);
                capMask = SmoothStep(0f, 0.22f, endDistance);
            }
            else if (shape == IndicatorShape.Dot || shape == IndicatorShape.Diamond)
            {
                capMask = 1f;
            }

            float profileMask = profile switch
            {
                IndicatorProfile.Straight => 1f,
                IndicatorProfile.Rounded => 1f - (edgeDistance * edgeDistance),
                IndicatorProfile.Convex => MathF.Sqrt(MathF.Max(0f, 1f - (edgeDistance * edgeDistance))),
                IndicatorProfile.Concave => MathF.Pow(MathF.Max(0f, 1f - edgeDistance), 2f),
                _ => 1f
            };

            float sign = relief == IndicatorRelief.Inset ? -1f : 1f;
            float amplitude = thicknessRatio * topRadius;
            return sign * amplitude * edgeMask * capMask * profileMask;
        }

        private static void AppendIndicatorHardWalls(
            List<Vector3> positions,
            List<Vector3> normals,
            List<int> indices,
            float topRadius,
            float zFront,
            float crownProfile,
            float radius,
            float height,
            float spiralHeight,
            float spiralWidth,
            float spiralTurns,
            IndicatorShape shape,
            IndicatorRelief relief,
            float widthRatio,
            float lengthRatio,
            float positionRatio,
            float thicknessRatio)
        {
            List<Vector2> contour = BuildIndicatorContour(shape, widthRatio, lengthRatio, positionRatio);
            if (contour.Count < 3)
            {
                return;
            }

            EnsureCounterClockwise(contour);

            float signedAmplitude = (relief == IndicatorRelief.Inset ? -1f : 1f) * (thicknessRatio * topRadius);
            for (int i = 0; i < contour.Count; i++)
            {
                Vector2 c0 = contour[i] * topRadius;
                Vector2 c1 = contour[(i + 1) % contour.Count] * topRadius;
                Vector2 edge2 = c1 - c0;
                if (edge2.LengthSquared() <= 1e-10f)
                {
                    continue;
                }

                edge2 = Normalize2(edge2);
                Vector3 outward = Vector3.Normalize(new Vector3(edge2.Y, -edge2.X, 0f));

                float r0 = c0.Length();
                float r1 = c1.Length();
                float zOuter0 = zFront +
                                ComputeCrownOffset(Math.Clamp(r0 / MathF.Max(topRadius, 1e-6f), 0f, 1f), crownProfile, radius, height) +
                                ComputeSpiralRidgeOffset(c0.X, c0.Y, r0, topRadius, spiralHeight, spiralWidth, spiralTurns);
                float zOuter1 = zFront +
                                ComputeCrownOffset(Math.Clamp(r1 / MathF.Max(topRadius, 1e-6f), 0f, 1f), crownProfile, radius, height) +
                                ComputeSpiralRidgeOffset(c1.X, c1.Y, r1, topRadius, spiralHeight, spiralWidth, spiralTurns);

                Vector3 v0 = new(c0.X, c0.Y, zOuter0);
                Vector3 v1 = new(c1.X, c1.Y, zOuter1);
                Vector3 v2 = new(c1.X, c1.Y, zOuter1 + signedAmplitude);
                Vector3 v3 = new(c0.X, c0.Y, zOuter0 + signedAmplitude);

                int baseIndex = positions.Count;
                positions.Add(v0);
                positions.Add(v1);
                positions.Add(v2);
                positions.Add(v3);
                normals.Add(outward);
                normals.Add(outward);
                normals.Add(outward);
                normals.Add(outward);

                Vector3 faceNormal = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
                bool aligned = Vector3.Dot(faceNormal, outward) >= 0f;
                if (aligned)
                {
                    indices.Add(baseIndex + 0);
                    indices.Add(baseIndex + 1);
                    indices.Add(baseIndex + 2);
                    indices.Add(baseIndex + 0);
                    indices.Add(baseIndex + 2);
                    indices.Add(baseIndex + 3);
                }
                else
                {
                    indices.Add(baseIndex + 0);
                    indices.Add(baseIndex + 2);
                    indices.Add(baseIndex + 1);
                    indices.Add(baseIndex + 0);
                    indices.Add(baseIndex + 3);
                    indices.Add(baseIndex + 2);
                }
            }
        }

        private static List<Vector2> BuildIndicatorContour(
            IndicatorShape shape,
            float widthRatio,
            float lengthRatio,
            float positionRatio)
        {
            float start = Math.Clamp(positionRatio, 0.05f, 0.90f);
            float end = Math.Clamp(start + lengthRatio, start + 1e-4f, 0.98f);
            float halfWidth = MathF.Max(0.001f, widthRatio * 0.5f);
            var contour = new List<Vector2>(64);

            switch (shape)
            {
                case IndicatorShape.Dot:
                {
                    float centerY = end - MathF.Min(halfWidth * 0.35f, (end - start) * 0.25f);
                    const int segments = 28;
                    for (int i = 0; i < segments; i++)
                    {
                        float a = i * (MathF.Tau / segments);
                        contour.Add(new Vector2(MathF.Cos(a) * halfWidth, centerY + MathF.Sin(a) * halfWidth));
                    }

                    break;
                }
                case IndicatorShape.Diamond:
                {
                    float centerY = (start + end) * 0.5f;
                    contour.Add(new Vector2(0f, start));
                    contour.Add(new Vector2(halfWidth, centerY));
                    contour.Add(new Vector2(0f, end));
                    contour.Add(new Vector2(-halfWidth, centerY));
                    break;
                }
                case IndicatorShape.Capsule:
                {
                    float radius = halfWidth;
                    float y0 = start + radius;
                    float y1 = end - radius;
                    if (y1 <= y0 + 1e-5f)
                    {
                        float centerY = (start + end) * 0.5f;
                        const int segments = 28;
                        for (int i = 0; i < segments; i++)
                        {
                            float a = i * (MathF.Tau / segments);
                            contour.Add(new Vector2(MathF.Cos(a) * radius, centerY + MathF.Sin(a) * radius));
                        }
                        break;
                    }

                    const int arcSegments = 14;
                    contour.Add(new Vector2(radius, y0));
                    contour.Add(new Vector2(radius, y1));
                    for (int i = 1; i <= arcSegments; i++)
                    {
                        float a = (MathF.PI * i) / (arcSegments + 1);
                        contour.Add(new Vector2(MathF.Cos(a) * radius, y1 + MathF.Sin(a) * radius));
                    }

                    contour.Add(new Vector2(-radius, y1));
                    contour.Add(new Vector2(-radius, y0));
                    for (int i = 1; i <= arcSegments; i++)
                    {
                        float a = MathF.PI + ((MathF.PI * i) / (arcSegments + 1));
                        contour.Add(new Vector2(MathF.Cos(a) * radius, y0 + MathF.Sin(a) * radius));
                    }

                    break;
                }
                default:
                {
                    const int samples = 24;
                    for (int i = 0; i <= samples; i++)
                    {
                        float t = i / (float)samples;
                        float y = Lerp(start, end, t);
                        float along = (y - start) / MathF.Max(1e-5f, end - start);
                        float hw = halfWidth;
                        if (shape == IndicatorShape.Tapered)
                        {
                            hw *= MathF.Max(0.20f, 1f - (along * 0.80f));
                        }
                        else if (shape == IndicatorShape.Needle)
                        {
                            hw *= MathF.Max(0.06f, 1f - (along * 0.94f));
                        }
                        else if (shape == IndicatorShape.Triangle)
                        {
                            hw *= MathF.Max(0.02f, 1f - along);
                        }

                        contour.Add(new Vector2(hw, y));
                    }

                    for (int i = samples; i >= 0; i--)
                    {
                        float t = i / (float)samples;
                        float y = Lerp(start, end, t);
                        float along = (y - start) / MathF.Max(1e-5f, end - start);
                        float hw = halfWidth;
                        if (shape == IndicatorShape.Tapered)
                        {
                            hw *= MathF.Max(0.20f, 1f - (along * 0.80f));
                        }
                        else if (shape == IndicatorShape.Needle)
                        {
                            hw *= MathF.Max(0.06f, 1f - (along * 0.94f));
                        }
                        else if (shape == IndicatorShape.Triangle)
                        {
                            hw *= MathF.Max(0.02f, 1f - along);
                        }

                        contour.Add(new Vector2(-hw, y));
                    }

                    break;
                }
            }

            return contour;
        }

        private static void EnsureCounterClockwise(List<Vector2> points)
        {
            double area2 = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % points.Count];
                area2 += (a.X * b.Y) - (b.X * a.Y);
            }

            if (area2 < 0.0)
            {
                points.Reverse();
            }
        }

        private static float ComputeIndicatorMask(
            float x,
            float y,
            float topRadius,
            bool enabled,
            IndicatorShape shape,
            float widthRatio,
            float lengthRatio,
            float positionRatio,
            float roundness)
        {
            if (!enabled || topRadius <= 1e-6f)
            {
                return 0f;
            }

            Vector2 p = new(x / topRadius, y / topRadius);
            float t = p.Y;
            float start = Math.Clamp(positionRatio, 0.05f, 0.90f);
            float end = Math.Clamp(start + lengthRatio, start + 1e-4f, 0.98f);
            float halfWidth = MathF.Max(0.001f, widthRatio * 0.5f);
            float along = (t - start) / MathF.Max(1e-4f, end - start);
            float edgeDistance;
            if (shape == IndicatorShape.Dot)
            {
                float dotRadius = halfWidth;
                float centerY = end - MathF.Min(dotRadius * 0.35f, (end - start) * 0.25f);
                float dx = p.X;
                float dy = p.Y - centerY;
                edgeDistance = MathF.Sqrt((dx * dx) + (dy * dy)) / MathF.Max(dotRadius, 1e-6f);
            }
            else
            {
                if (t < start || t > end)
                {
                    return 0f;
                }

                float localHalfWidth = halfWidth;
                if (shape == IndicatorShape.Tapered)
                {
                    localHalfWidth *= MathF.Max(0.20f, 1f - (along * 0.80f));
                }
                else if (shape == IndicatorShape.Needle)
                {
                    localHalfWidth *= MathF.Max(0.06f, 1f - (along * 0.94f));
                }
                else if (shape == IndicatorShape.Triangle)
                {
                    localHalfWidth *= MathF.Max(0.02f, 1f - along);
                }

                if (shape == IndicatorShape.Diamond)
                {
                    float qx = MathF.Abs(p.X) / MathF.Max(halfWidth, 1e-6f);
                    float qy = MathF.Abs((along * 2f) - 1f);
                    edgeDistance = qx + qy;
                }
                else
                {
                    edgeDistance = MathF.Abs(p.X) / MathF.Max(localHalfWidth, 1e-6f);
                }
            }

            if (edgeDistance >= 1f)
            {
                return 0f;
            }

            float edgeMask;
            if (roundness <= 1e-4f)
            {
                edgeMask = 1f;
            }
            else
            {
                float feather = Math.Clamp(roundness, 0f, 1f) * 0.45f;
                edgeMask = 1f - SmoothStep(1f - feather, 1f, edgeDistance);
            }

            float capMask = 1f;
            if (shape == IndicatorShape.Capsule)
            {
                float endDistance = MathF.Min(along, 1f - along);
                capMask = SmoothStep(0f, 0.22f, endDistance);
            }

            return edgeMask * capMask;
        }
    }
}
