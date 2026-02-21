using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using KnobForge.Core;
using KnobForge.Core.Scene;

namespace KnobForge.Rendering.GPU;

public static partial class MetalMeshBuilder
{
    private static void AppendIndicatorHardWalls(
        List<Vector3> positions,
        List<Vector3> normals,
        List<uint> indices,
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

            uint baseIndex = (uint)positions.Count;
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
            // Straight profile should read as a hard-machined edge (no feathering).
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
            IndicatorProfile.Concave => MathF.Pow(MathF.Max(0f, 1f - edgeDistance), 2.0f),
            _ => 1f
        };

        float sign = relief == IndicatorRelief.Inset ? -1f : 1f;
        float amplitude = thicknessRatio * topRadius;
        return sign * amplitude * edgeMask * capMask * profileMask;
    }

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
        // Keep knurl width stable; previous mapping was overly aggressive and caused lobe blow-ups.
        float lineWidth = Math.Clamp(0.02f + (gripWidth * 0.10f), 0.015f, 0.35f);
        float pattern = ComputeKnurlPattern(gripType, u, v, lineWidth);
        float sharpExponent = Math.Clamp(1f + ((Math.Clamp(gripSharpness, 0.5f, 8f) - 1f) * 0.7f), 0.45f, 5f);
        float shape = MathF.Pow(Math.Clamp(pattern, 0f, 1f), sharpExponent);
        float bandFade = SmoothStep(0f, 0.06f, localZNorm) * (1f - SmoothStep(0.94f, 1f, localZNorm));
        // Depth in world units should scale with side span, not explode with grip width.
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

    private static float Fract(float x)
    {
        return x - MathF.Floor(x);
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
