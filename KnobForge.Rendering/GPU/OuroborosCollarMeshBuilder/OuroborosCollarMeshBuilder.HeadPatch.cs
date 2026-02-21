using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using KnobForge.Core;
using KnobForge.Core.Scene;

namespace KnobForge.Rendering.GPU;

public static partial class OuroborosCollarMeshBuilder
{
    private static OuroborosHeadPatchAsset BuildHeadPatchAsset(
        int crossSegments,
        int ringCount,
        float headScale,
        float jawBulge,
        float ellipseScale)
    {
        var positions = new Vector3[ringCount * crossSegments];
        var normals = new Vector3[ringCount * crossSegments];
        var tangents = new Vector4[ringCount * crossSegments];
        var uvs = new Vector2[ringCount * crossSegments];
        var indices = new List<uint>((ringCount - 1) * crossSegments * 6);

        float hs = Math.Clamp(headScale, 0f, 2f);
        float jb = Math.Clamp(jawBulge, 0f, 1f);
        float e = Math.Max(0.45f, ellipseScale);

        // Authored-style head patch: explicit skull/jaw form, not radial tube inflation.
        for (int r = 0; r < ringCount; r++)
        {
            float u = ringCount <= 1 ? 0f : (float)r / (ringCount - 1);
            for (int j = 0; j < crossSegments; j++)
            {
                float v = (float)j / crossSegments;
                float phi = v * MathF.PI * 2f;
                int vi = (r * crossSegments) + j;
                positions[vi] = EvaluateHeadLocalPoint(u, phi, hs, jb, e);
                uvs[vi] = new Vector2(u, v);
            }
        }

        // Derive stable shading basis from actual authored geometry.
        for (int r = 0; r < ringCount; r++)
        {
            int prevR = Math.Max(0, r - 1);
            int nextR = Math.Min(ringCount - 1, r + 1);
            for (int j = 0; j < crossSegments; j++)
            {
                int vi = (r * crossSegments) + j;
                int prevJ = WrapIndex(j - 1, crossSegments);
                int nextJ = WrapIndex(j + 1, crossSegments);

                Vector3 dPdu = positions[(nextR * crossSegments) + j] - positions[(prevR * crossSegments) + j];
                Vector3 dPdv = positions[(r * crossSegments) + nextJ] - positions[(r * crossSegments) + prevJ];
                if (dPdu.LengthSquared() <= 1e-8f)
                {
                    dPdu = new Vector3(1f, 0f, 0f);
                }

                if (dPdv.LengthSquared() <= 1e-8f)
                {
                    dPdv = new Vector3(0f, 1f, 0f);
                }

                Vector3 n = SafeNormalize(Vector3.Cross(dPdu, dPdv), Vector3.UnitZ);
                Vector3 outwardHint = new Vector3(0f, positions[vi].Y, positions[vi].Z);
                if (outwardHint.LengthSquared() > 1e-8f && Vector3.Dot(n, outwardHint) < 0f)
                {
                    n = -n;
                }

                Vector3 tU = SafeNormalize(ProjectOntoPlane(dPdu, n), Vector3.UnitX);
                normals[vi] = n;
                tangents[vi] = BuildTangentWithSign(n, tU, dPdv);
            }
        }

        for (int r = 0; r < ringCount - 1; r++)
        {
            int row0 = r * crossSegments;
            int row1 = (r + 1) * crossSegments;
            for (int j = 0; j < crossSegments; j++)
            {
                int jNext = WrapIndex(j + 1, crossSegments);
                uint a0 = (uint)(row0 + j);
                uint a1 = (uint)(row0 + jNext);
                uint b0 = (uint)(row1 + j);
                uint b1 = (uint)(row1 + jNext);
                indices.Add(a0);
                indices.Add(a1);
                indices.Add(b0);
                indices.Add(a1);
                indices.Add(b1);
                indices.Add(b0);
            }
        }

        return new OuroborosHeadPatchAsset(crossSegments, ringCount, positions, normals, tangents, uvs, indices.ToArray());
    }

    private static Vector3 EvaluateHeadLocalPoint(float u, float phi, float hs, float jb, float ellipseScale)
    {
        float cs = MathF.Cos(phi);
        float sn = MathF.Sin(phi);
        float absCs = MathF.Abs(cs);
        float absSn = MathF.Abs(sn);
        float topMask = MathF.Max(0f, sn);
        float jawMask = MathF.Max(0f, -sn);
        float sideMask = MathF.Pow(absCs, 0.72f);
        float centerMask = MathF.Pow(MathF.Max(0f, 1f - absCs), 1.45f);

        float forward = EvalPiecewise(u, 0.00f, 0f, 0.18f, 0.32f, 0.42f, 1.25f, 0.68f, 1.05f, 0.86f, 0.42f, 1.00f, 0f);
        float width = EvalPiecewise(u, 0.00f, 0.95f, 0.18f, 1.05f, 0.42f, 1.55f, 0.68f, 1.45f, 0.86f, 1.18f, 1.00f, 0.92f);
        float height = EvalPiecewise(u, 0.00f, 0.86f, 0.18f, 0.88f, 0.42f, 1.05f, 0.68f, 0.95f, 0.86f, 0.76f, 1.00f, 0.84f);
        float jawDrop = EvalPiecewise(u, 0.00f, 0.05f, 0.18f, 0.08f, 0.42f, 0.30f, 0.68f, 0.42f, 0.86f, 0.28f, 1.00f, 0.08f);

        float neckInPinch = 1f - (0.18f * (1f - SmoothStep(0f, 0.20f, u)));
        float neckOutPinch = 1f - (0.10f * SmoothStep(0.82f, 1.0f, u));
        float neckPinch = neckInPinch * neckOutPinch;

        float x = forward * (0.78f + (0.55f * hs));
        float y = MathF.Sign(cs) * MathF.Pow(absCs, 0.72f) * width * neckPinch;
        float zUpper = topMask * MathF.Pow(MathF.Max(1e-5f, topMask), 0.86f) * height;
        float zLower = -jawMask * MathF.Pow(MathF.Max(1e-5f, jawMask), 0.96f) * (height * 0.82f + (jawDrop * 0.35f));
        float z = zUpper + zLower;

        // Skull flattening + temple widening.
        float skullFlatten = 1f - (0.34f * SmoothStep(0.20f, 0.85f, u) * topMask);
        z *= skullFlatten;
        y *= 1f + (0.22f * SmoothStep(0.18f, 0.78f, u) * sideMask);

        // Eye sockets and brow ridges.
        float eyeBand = GaussianMask(u, 0.56f, 0.17f);
        float eyeL = GaussianMask(cs, 0.72f, 0.19f) * GaussianMask(sn, 0.34f, 0.20f);
        float eyeR = GaussianMask(cs, -0.72f, 0.19f) * GaussianMask(sn, 0.34f, 0.20f);
        float eyes = eyeBand * (eyeL + eyeR);
        z -= eyes * (0.16f + (0.10f * hs));
        y *= 1f - (eyes * 0.08f);

        float browL = GaussianMask(cs, 0.65f, 0.22f) * GaussianMask(sn, 0.58f, 0.17f);
        float browR = GaussianMask(cs, -0.65f, 0.22f) * GaussianMask(sn, 0.58f, 0.17f);
        z += eyeBand * (browL + browR) * (0.10f + (0.07f * jb));

        // Snout ridge and mouth plane around bite.
        float snoutRidge = SmoothStep(0.45f, 0.90f, u) * centerMask * topMask;
        z += snoutRidge * (0.08f + (0.05f * hs));

        float mouthCut = SmoothStep(0.55f, 0.98f, u) * jawMask * centerMask;
        z -= mouthCut * (0.16f + (0.12f * jb));
        y *= 1f - (0.10f * mouthCut);

        return new Vector3(x, y, z * ellipseScale);
    }

    private static float EvalPiecewise(
        float u,
        float u0, float v0,
        float u1, float v1,
        float u2, float v2,
        float u3, float v3,
        float u4, float v4,
        float u5, float v5)
    {
        if (u <= u0) return v0;
        if (u <= u1) return Lerp(v0, v1, SmoothStep(u0, u1, u));
        if (u <= u2) return Lerp(v1, v2, SmoothStep(u1, u2, u));
        if (u <= u3) return Lerp(v2, v3, SmoothStep(u2, u3, u));
        if (u <= u4) return Lerp(v3, v4, SmoothStep(u3, u4, u));
        if (u <= u5) return Lerp(v4, v5, SmoothStep(u4, u5, u));
        return v5;
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * t);
    }

    private static float GaussianMask(float value, float mean, float sigma)
    {
        float s = MathF.Max(1e-4f, sigma);
        float d = (value - mean) / s;
        return MathF.Exp(-(d * d));
    }

    private static float Gaussian(float x, float sigma)
    {
        float s = MathF.Max(1e-4f, sigma);
        float a = x / s;
        return MathF.Exp(-(a * a));
    }


    private sealed class OuroborosHeadPatchAsset
    {
        public OuroborosHeadPatchAsset(
            int crossSegments,
            int ringCount,
            Vector3[] positions,
            Vector3[] normals,
            Vector4[] tangents,
            Vector2[] uvs,
            uint[] indices)
        {
            CrossSegments = crossSegments;
            RingCount = ringCount;
            BoundaryPositions = positions;
            BoundaryNormals = normals;
            BoundaryTangents = tangents;
            BoundaryUVs = uvs;
            Indices = indices;
        }

        public int CrossSegments { get; }
        public int BoundaryVertexCount => CrossSegments;
        public int RingCount { get; }
        public Vector3[] BoundaryPositions { get; }
        public Vector3[] BoundaryNormals { get; }
        public Vector4[] BoundaryTangents { get; }
        public Vector2[] BoundaryUVs { get; }
        public uint[] Indices { get; }

        public int Index(int ring, int slice)
        {
            return (ring * CrossSegments) + slice;
        }
    }
}
