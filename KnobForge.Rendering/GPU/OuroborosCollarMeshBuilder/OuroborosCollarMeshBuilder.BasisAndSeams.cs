using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using KnobForge.Core;
using KnobForge.Core.Scene;

namespace KnobForge.Rendering.GPU;

public static partial class OuroborosCollarMeshBuilder
{
    private static void BuildCircularFrames(
        int pathSegments,
        float rotation,
        out Vector3[] tangents,
        out Vector3[] normals,
        out Vector3[] bitangents)
    {
        int count = Math.Max(3, pathSegments);
        tangents = new Vector3[count];
        normals = new Vector3[count];
        bitangents = new Vector3[count];

        // Analytic rotation-minimizing frame for a planar circular centerline.
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / count;
            float theta = (t * MathF.PI * 2f) + rotation;
            float cos = MathF.Cos(theta);
            float sin = MathF.Sin(theta);

            Vector3 tangent = new(-sin, cos, 0f);
            Vector3 normal = new(cos, sin, 0f);
            Vector3 bitangent = Vector3.UnitZ;

            tangents[i] = tangent;
            normals[i] = normal;
            bitangents[i] = bitangent;
        }
    }

    private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback)
    {
        return value.LengthSquared() > 1e-8f ? Vector3.Normalize(value) : fallback;
    }

    private static int VertexIndex(int ring, int slice, int crossSegments)
    {
        return (ring * crossSegments) + slice;
    }

    private static int WrapIndex(int index, int count)
    {
        int wrapped = index % count;
        return wrapped < 0 ? wrapped + count : wrapped;
    }

    private static float SmootherStep01(float t)
    {
        float x = Math.Clamp(t, 0f, 1f);
        return x * x * x * (x * ((x * 6f) - 15f) + 10f);
    }

    private static Vector3 ProjectOntoPlane(Vector3 value, Vector3 normal)
    {
        return value - (normal * Vector3.Dot(value, normal));
    }

    private static Vector4 BuildTangentWithSign(Vector3 normal, Vector3 tangentGuess, Vector3 dPdv)
    {
        Vector3 n = SafeNormalize(normal, Vector3.UnitZ);
        Vector3 t = SafeNormalize(ProjectOntoPlane(tangentGuess, n), Vector3.UnitX);
        float sign = ComputeSign(n, t, dPdv);
        return new Vector4(t, sign);
    }

    private static float ComputeSign(Vector3 normal, Vector3 tangent, Vector3 dPdv)
    {
        float s = Vector3.Dot(Vector3.Cross(normal, tangent), dPdv);
        return s >= 0f ? 1f : -1f;
    }

    private static void AlignTangentForShortestArc(
        Vector3 baseTangent,
        ref Vector3 targetTangent,
        ref float targetSign)
    {
        if (Vector3.Dot(baseTangent, targetTangent) < 0f)
        {
            targetTangent = -targetTangent;
            targetSign = -targetSign;
        }
    }

    private static void OverrideBoundaryBasis(
        int ring,
        int crossSegments,
        Vector3[] bodyNormals,
        Vector4[] bodyTangents,
        Vector3[] finalNormals,
        Vector4[] finalTangents)
    {
        int row = ring * crossSegments;
        for (int j = 0; j < crossSegments; j++)
        {
            int vi = row + j;
            finalNormals[vi] = bodyNormals[vi];
            finalTangents[vi] = bodyTangents[vi];
        }
    }

    private static void BlendInnerSeamBasis(
        int[] replacedRings,
        int crossSegments,
        int pathSegments,
        int replaceK,
        float alpha,
        Vector3[] bodyNormals,
        Vector4[] bodyTangents,
        Vector3[] headTargetNormals,
        Vector4[] headTargetTangents,
        Vector3[] finalPositions,
        Vector3[] finalNormals,
        Vector4[] finalTangents)
    {
        if (replaceK < 0 || replaceK >= replacedRings.Length)
        {
            return;
        }

        int ring = replacedRings[replaceK];
        int row = ring * crossSegments;
        for (int j = 0; j < crossSegments; j++)
        {
            int vi = row + j;
            int jPrev = WrapIndex(j - 1, crossSegments);
            int jNext = WrapIndex(j + 1, crossSegments);
            Vector3 dPdv = finalPositions[row + jNext] - finalPositions[row + jPrev];

            Vector3 nBody = bodyNormals[vi];
            Vector3 nHead = headTargetNormals[vi];
            Vector3 nBlend = SafeNormalize(Vector3.Lerp(nBody, nHead, alpha), nBody);

            Vector3 tBody = bodyTangents[vi].XYZ();
            Vector3 tHead = headTargetTangents[vi].XYZ();
            float headSign = headTargetTangents[vi].W >= 0f ? 1f : -1f;
            AlignTangentForShortestArc(tBody, ref tHead, ref headSign);
            Vector3 tBlend = SafeNormalize(Vector3.Lerp(tBody, tHead, alpha), tBody);
            tBlend = SafeNormalize(ProjectOntoPlane(tBlend, nBlend), tBody);

            finalNormals[vi] = nBlend;
            finalTangents[vi] = BuildTangentWithSign(nBlend, tBlend, dPdv);
        }
    }

    private static void RebuildRingBasisFromGeometry(
        int ring,
        int pathSegments,
        int crossSegments,
        Vector3[] curveCenters,
        Vector3[] positions,
        Vector3[] normals,
        Vector4[] tangents)
    {
        int row = ring * crossSegments;
        int prevRing = WrapIndex(ring - 1, pathSegments);
        int nextRing = WrapIndex(ring + 1, pathSegments);
        int prevRow = prevRing * crossSegments;
        int nextRow = nextRing * crossSegments;

        for (int j = 0; j < crossSegments; j++)
        {
            int jPrev = WrapIndex(j - 1, crossSegments);
            int jNext = WrapIndex(j + 1, crossSegments);
            Vector3 du = positions[nextRow + j] - positions[prevRow + j];
            Vector3 dv = positions[row + jNext] - positions[row + jPrev];
            if (du.LengthSquared() <= 1e-8f || dv.LengthSquared() <= 1e-8f)
            {
                continue;
            }

            Vector3 n = SafeNormalize(Vector3.Cross(du, dv), Vector3.UnitZ);
            Vector3 radial = positions[row + j] - curveCenters[ring];
            if (Vector3.Dot(n, radial) < 0f)
            {
                n = -n;
            }

            Vector3 t = SafeNormalize(ProjectOntoPlane(du, n), Vector3.UnitX);
            normals[row + j] = n;
            tangents[row + j] = BuildTangentWithSign(n, t, dv);
        }
    }

    private static int ResolveTargetPatchRing(int replaceK, int patchRingCount)
    {
        if (replaceK < NeckInRings)
        {
            return Math.Min(1, patchRingCount - 1);
        }

        if (replaceK >= NeckInRings + HeadCoreRings)
        {
            return Math.Max(0, patchRingCount - 2);
        }

        return replaceK - NeckInRings;
    }
}
