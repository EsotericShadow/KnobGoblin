using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using KnobForge.Core;
using KnobForge.Core.Scene;


namespace KnobForge.Rendering.GPU;

public static partial class ImportedStlCollarMeshBuilder
{
    private static Vector3[] AutoOrientToKnobPlane(IReadOnlyList<Vector3> input)
    {
        int[] perm = { 0, 1, 2 };
        int[][] perms =
        {
            new[] { 0, 1, 2 },
            new[] { 0, 2, 1 },
            new[] { 1, 0, 2 },
            new[] { 1, 2, 0 },
            new[] { 2, 0, 1 },
            new[] { 2, 1, 0 }
        };

        float bestScore = float.MaxValue;
        int[] bestPerm = perm;
        for (int p = 0; p < perms.Length; p++)
        {
            int ax = perms[p][0];
            int ay = perms[p][1];
            int az = perms[p][2];

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float minZ = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            float maxZ = float.MinValue;
            for (int i = 0; i < input.Count; i++)
            {
                Vector3 v = input[i];
                float x = Axis(v, ax);
                float y = Axis(v, ay);
                float z = Axis(v, az);
                minX = MathF.Min(minX, x);
                minY = MathF.Min(minY, y);
                minZ = MathF.Min(minZ, z);
                maxX = MathF.Max(maxX, x);
                maxY = MathF.Max(maxY, y);
                maxZ = MathF.Max(maxZ, z);
            }

            float spanX = MathF.Max(1e-6f, maxX - minX);
            float spanY = MathF.Max(1e-6f, maxY - minY);
            float spanZ = MathF.Max(1e-6f, maxZ - minZ);

            // Prefer mappings where "height" (Z span) is minimal relative to XY footprint.
            float score = spanZ / MathF.Max(1e-6f, MathF.Min(spanX, spanY));
            if (score < bestScore)
            {
                bestScore = score;
                bestPerm = perms[p];
            }
        }

        var output = new Vector3[input.Count];
        int permutationParity = PermutationParity(bestPerm); // +1 even, -1 odd
        for (int i = 0; i < input.Count; i++)
        {
            Vector3 v = input[i];
            output[i] = new Vector3(
                Axis(v, bestPerm[0]),
                Axis(v, bestPerm[1]),
                Axis(v, bestPerm[2]));
        }

        // Normalize vertical direction convention so most of the mass lies near z <= 0.
        // This keeps imported collars oriented similarly to built-in assets.
        int above = 0;
        int below = 0;
        for (int i = 0; i < output.Length; i++)
        {
            if (output[i].Z >= 0f)
            {
                above++;
            }
            else
            {
                below++;
            }
        }

        if (above > below)
        {
            for (int i = 0; i < output.Length; i++)
            {
                Vector3 p = output[i];
                output[i] = new Vector3(p.X, p.Y, -p.Z);
            }
        }

        // Enforce right-handed basis after permutation/sign normalization.
        // Without this, imported meshes can become mirrored and appear to rotate/orbit
        // opposite to native knob geometry under shared transforms.
        int signZ = above > below ? -1 : 1;
        int handedness = permutationParity * signZ;
        if (handedness < 0)
        {
            for (int i = 0; i < output.Length; i++)
            {
                Vector3 p = output[i];
                output[i] = new Vector3(-p.X, p.Y, p.Z);
            }
        }

        return output;
    }

    private static int PermutationParity(int[] perm)
    {
        int inversions = 0;
        for (int i = 0; i < perm.Length - 1; i++)
        {
            for (int j = i + 1; j < perm.Length; j++)
            {
                if (perm[i] > perm[j])
                {
                    inversions++;
                }
            }
        }

        return (inversions & 1) == 0 ? 1 : -1;
    }

    private static float Axis(in Vector3 value, int axis)
    {
        return axis switch
        {
            0 => value.X,
            1 => value.Y,
            _ => value.Z
        };
    }

    private static Vector3[] ApplyBodyLengthThicknessDeform(
        IReadOnlyList<Vector3> input,
        float centerRadius,
        float bodyLengthScale,
        float bodyThicknessScale,
        float headLengthScale,
        float headThicknessScale,
        float headAngleOffsetRadians)
    {
        if (input.Count == 0)
        {
            return Array.Empty<Vector3>();
        }

        float lengthScaleBody = Math.Clamp(bodyLengthScale, 0.6f, 2.4f);
        float thicknessScaleBody = Math.Clamp(bodyThicknessScale, 0.5f, 2.5f);
        float lengthScaleHead = Math.Clamp(headLengthScale, 0.5f, 2.5f);
        float thicknessScaleHead = Math.Clamp(headThicknessScale, 0.5f, 2.8f);
        if (MathF.Abs(lengthScaleBody - 1f) <= 1e-5f &&
            MathF.Abs(thicknessScaleBody - 1f) <= 1e-5f &&
            MathF.Abs(lengthScaleHead - 1f) <= 1e-5f &&
            MathF.Abs(thicknessScaleHead - 1f) <= 1e-5f)
        {
            Vector3[] passThrough = new Vector3[input.Count];
            for (int i = 0; i < input.Count; i++)
            {
                passThrough[i] = input[i];
            }

            return passThrough;
        }

        int headAnchorIndex = FindHeadAnchorIndex(input);
        Vector3 headAnchorOriginal = input[headAnchorIndex];
        float headAngle = WrapSignedRadians(MathF.Atan2(headAnchorOriginal.Y, headAnchorOriginal.X) + headAngleOffsetRadians);

        const float headRegionInner = 0.18f;
        const float headRegionOuter = 0.70f;
        const float headWeightPower = 2.2f;

        Vector3 headCentroidOriginal = Vector3.Zero;
        float headWeightSum = 0f;
        for (int i = 0; i < input.Count; i++)
        {
            Vector3 p = input[i];
            float angle = MathF.Atan2(p.Y, p.X);
            float delta = MathF.Abs(WrapSignedRadians(angle - headAngle));
            float headMask = 1f - SmoothStep(headRegionInner, headRegionOuter, delta);
            float w = MathF.Pow(Math.Clamp(headMask, 0f, 1f), headWeightPower);
            headCentroidOriginal += p * w;
            headWeightSum += w;
        }
        if (headWeightSum > 1e-6f)
        {
            headCentroidOriginal /= headWeightSum;
        }

        var output = new Vector3[input.Count];
        Vector3 headCentroidNew = Vector3.Zero;
        float headWeightSumNew = 0f;
        for (int i = 0; i < input.Count; i++)
        {
            Vector3 p = input[i];
            float r = new Vector2(p.X, p.Y).Length();
            if (r <= 1e-8f)
            {
                output[i] = p;
                continue;
            }

            float angle = MathF.Atan2(p.Y, p.X);
            float delta = MathF.Abs(WrapSignedRadians(angle - headAngle));
            float headMask = 1f - SmoothStep(headRegionInner, headRegionOuter, delta);
            headMask = Math.Clamp(headMask, 0f, 1f);
            float bodyMask = 1f - headMask;

            float radialDelta = r - centerRadius;
            float thicknessScale = 1f +
                ((thicknessScaleBody - 1f) * bodyMask) +
                ((thicknessScaleHead - 1f) * headMask);
            float radialDeformed = centerRadius + (radialDelta * thicknessScale);
            float lengthScale = 1f +
                ((lengthScaleBody - 1f) * bodyMask) +
                ((lengthScaleHead - 1f) * headMask);
            float radialLengthScaled = radialDeformed * lengthScale;
            float xyScale = radialLengthScaled / MathF.Max(1e-6f, r);

            float zScale = 1f + ((thicknessScale - 1f) * 0.85f);
            Vector3 deformed = new(
                p.X * xyScale,
                p.Y * xyScale,
                p.Z * zScale);
            output[i] = deformed;

            float w = MathF.Pow(headMask, headWeightPower);
            headCentroidNew += deformed * w;
            headWeightSumNew += w;
        }

        if (headWeightSumNew > 1e-6f)
        {
            headCentroidNew /= headWeightSumNew;
        }

        Vector3 headDelta = headCentroidNew - headCentroidOriginal;
        for (int i = 0; i < output.Length; i++)
        {
            output[i] -= headDelta;
        }

        // Keep body centered after head-preserving deformation.
        Vector2 bodyCenterOriginal = ComputeWeightedBodyCenter(input, headAngle, headRegionInner, headRegionOuter);
        Vector2 bodyCenterNew = ComputeWeightedBodyCenter(output, headAngle, headRegionInner, headRegionOuter);
        Vector2 bodyDelta = bodyCenterNew - bodyCenterOriginal;
        if (bodyDelta.LengthSquared() > 1e-10f)
        {
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = new Vector3(
                    output[i].X - bodyDelta.X,
                    output[i].Y - bodyDelta.Y,
                    output[i].Z);
            }
        }

        return output;
    }

    private static int FindHeadAnchorIndex(IReadOnlyList<Vector3> points)
    {
        int bestIndex = 0;
        float minRadius = float.MaxValue;
        float maxRadius = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 p = points[i];
            float r = new Vector2(p.X, p.Y).Length();
            minRadius = MathF.Min(minRadius, r);
            maxRadius = MathF.Max(maxRadius, r);
        }

        float epsilon = MathF.Max(1e-4f, (maxRadius - minRadius) * 0.10f);
        float bestY = float.MaxValue;
        float bestR = float.MaxValue;
        bool found = false;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 p = points[i];
            float r = new Vector2(p.X, p.Y).Length();
            if (r > minRadius + epsilon)
            {
                continue;
            }

            if (p.Y < bestY || (!found && p.Y <= bestY + 1e-5f && r < bestR))
            {
                bestY = p.Y;
                bestR = r;
                bestIndex = i;
                found = true;
            }
        }

        if (!found)
        {
            float bestScore = float.MaxValue;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i];
                float r = new Vector2(p.X, p.Y).Length();
                float score = p.Y - (0.25f * r);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }
        }

        return bestIndex;
    }

    private static Vector2 ComputeWeightedBodyCenter(
        IReadOnlyList<Vector3> points,
        float headAngle,
        float headRegionInner,
        float headRegionOuter)
    {
        Vector2 accum = Vector2.Zero;
        float weightSum = 0f;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 p = points[i];
            float angle = MathF.Atan2(p.Y, p.X);
            float delta = MathF.Abs(WrapSignedRadians(angle - headAngle));
            float headMask = 1f - SmoothStep(headRegionInner, headRegionOuter, delta);
            float bodyMask = Math.Clamp(1f - headMask, 0f, 1f);
            float w = bodyMask * bodyMask;
            accum += new Vector2(p.X, p.Y) * w;
            weightSum += w;
        }

        return weightSum > 1e-6f ? accum / weightSum : Vector2.Zero;
    }

    private static float WrapSignedRadians(float value)
    {
        const float twoPi = MathF.PI * 2f;
        float wrapped = value;
        while (wrapped > MathF.PI)
        {
            wrapped -= twoPi;
        }

        while (wrapped < -MathF.PI)
        {
            wrapped += twoPi;
        }

        return wrapped;
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

    private static (float InnerRadius, float CenterRadius, float OuterRadius) ComputeRobustRadialBands(IReadOnlyList<Vector3> points)
    {
        var radial = new float[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            radial[i] = new Vector2(points[i].X, points[i].Y).Length();
        }

        Array.Sort(radial);
        float inner = Percentile(radial, 0.22f);
        float outer = Percentile(radial, 0.78f);
        if (outer <= inner + 1e-5f)
        {
            inner = radial[0];
            outer = radial[radial.Length - 1];
        }

        float center = 0.5f * (inner + outer);
        return (inner, center, outer);
    }

    private static float Percentile(IReadOnlyList<float> sortedValues, float t)
    {
        if (sortedValues.Count == 0)
        {
            return 0f;
        }

        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        float x = Math.Clamp(t, 0f, 1f) * (sortedValues.Count - 1);
        int i0 = (int)MathF.Floor(x);
        int i1 = Math.Min(sortedValues.Count - 1, i0 + 1);
        float a = x - i0;
        return sortedValues[i0] + ((sortedValues[i1] - sortedValues[i0]) * a);
    }
}
