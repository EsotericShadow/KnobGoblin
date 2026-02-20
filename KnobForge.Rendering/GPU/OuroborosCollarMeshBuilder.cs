using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using KnobForge.Core;
using KnobForge.Core.Scene;

namespace KnobForge.Rendering.GPU;

public sealed class CollarMesh
{
    public MetalVertex[] Vertices { get; init; } = Array.Empty<MetalVertex>();

    public uint[] Indices { get; init; } = Array.Empty<uint>();

    public Vector2[] UVs { get; init; } = Array.Empty<Vector2>();

    public Vector4[] Tangents { get; init; } = Array.Empty<Vector4>();

    public float ReferenceRadius { get; init; }
}

public static class OuroborosCollarMeshBuilder
{
    private const int NeckInRings = 6;
    private const int HeadCoreRings = 8;
    private const int NeckOutRings = 6;
    private const int TotalReplacedRings = NeckInRings + HeadCoreRings + NeckOutRings;

    public static CollarMesh? TryBuildFromProject(KnobProject? project)
    {
        if (project is null)
        {
            return null;
        }

        ModelNode? modelNode = project.SceneRoot.Children
            .OfType<ModelNode>()
            .FirstOrDefault();
        if (modelNode is null)
        {
            return null;
        }

        CollarNode? collarNode = modelNode.Children
            .OfType<CollarNode>()
            .FirstOrDefault();
        if (collarNode is null || !collarNode.Enabled || collarNode.Preset != CollarPreset.SnakeOuroboros)
        {
            return null;
        }

        return BuildSweptBody(modelNode, collarNode);
    }

    private static CollarMesh BuildSweptBody(ModelNode modelNode, CollarNode collarNode)
    {
        int pathSegments = Math.Clamp(collarNode.PathSegments, Math.Max(64, TotalReplacedRings * 4), 2048);
        int crossSegments = Math.Clamp(collarNode.CrossSegments, 8, 256);

        float knobRadius = MathF.Max(10f, modelNode.Radius);
        float knobHalfHeight = MathF.Max(10f, modelNode.Height * 0.5f);
        float centerlineBase = MathF.Max(1f, knobRadius * (collarNode.InnerRadiusRatio + collarNode.GapToKnobRatio));
        // Guardrail: keep body thickness below 48% of centerline radius.
        float maxBodyFromCenterline = centerlineBase * (0.48f / (1f - 0.48f));
        float bodyRadiusBase = MathF.Min(knobRadius * collarNode.BodyRadiusRatio, maxBodyFromCenterline);
        bodyRadiusBase = MathF.Max(0.5f, bodyRadiusBase);
        float centerlineRadius =
            centerlineBase +
            bodyRadiusBase;
        float zCenter = knobHalfHeight * collarNode.ElevationRatio;
        float rotation = collarNode.OverallRotationRadians;
        float biteAngle = collarNode.BiteAngleRadians + rotation;
        float seamOffset = collarNode.UvSeamFollowBite
            ? Wrap01(biteAngle / (MathF.PI * 2f))
            : collarNode.UvSeamOffset;
        int biteIndex = WrapIndex((int)MathF.Round(Wrap01(biteAngle / (MathF.PI * 2f)) * pathSegments), pathSegments);
        float minLocalRadius = bodyRadiusBase * 0.15f;

        var curvePoints = new Vector3[pathSegments];
        var localRadiusByRing = new float[pathSegments];
        var localRxByRing = new float[pathSegments];
        var localRyByRing = new float[pathSegments];
        for (int i = 0; i < pathSegments; i++)
        {
            float t = (float)i / pathSegments;
            float a = (t * MathF.PI * 2f) + rotation;
            curvePoints[i] = new Vector3(
                centerlineRadius * MathF.Cos(a),
                centerlineRadius * MathF.Sin(a),
                zCenter);

            float radialPhase = WrapSignedRadians(a - biteAngle);
            float neckFactor = 1f - (collarNode.NeckTaper * Gaussian(radialPhase, 0.22f));
            float tailFactor = 1f - (collarNode.TailTaper * Gaussian(radialPhase + (MathF.PI * 0.18f * (1f + collarNode.TailUnderlap)), 0.28f));
            float massFactor = 1f + (0.20f * collarNode.MassBias * MathF.Cos(radialPhase + MathF.PI));
            float localRadius = MathF.Max(minLocalRadius, bodyRadiusBase * (neckFactor * tailFactor * massFactor));
            localRadiusByRing[i] = localRadius;
            localRxByRing[i] = localRadius;
            localRyByRing[i] = localRadius * MathF.Max(0.45f, collarNode.BodyEllipseYScale);
        }

        BuildCircularFrames(pathSegments, rotation, out Vector3[] tangents, out Vector3[] normals, out Vector3[] bitangents);

        int vertexCount = pathSegments * crossSegments;
        var bodyPositions = new Vector3[vertexCount];
        var bodyNormals = new Vector3[vertexCount];
        var bodyTangents = new Vector4[vertexCount];
        var bodyUVs = new Vector2[vertexCount];
        var finalPositions = new Vector3[vertexCount];
        var finalNormals = new Vector3[vertexCount];
        var finalTangents = new Vector4[vertexCount];
        var finalUVs = new Vector2[vertexCount];
        var headTargetNormals = new Vector3[vertexCount];
        var headTargetTangents = new Vector4[vertexCount];
        var indices = new List<uint>(pathSegments * crossSegments * 6);

        for (int i = 0; i < pathSegments; i++)
        {
            float t = (float)i / pathSegments;
            float rx = localRxByRing[i];
            float ry = localRyByRing[i];

            Vector3 c = curvePoints[i];
            Vector3 pathTangent = tangents[i];
            Vector3 n = normals[i];
            Vector3 b = bitangents[i];

            for (int j = 0; j < crossSegments; j++)
            {
                float v = (float)j / crossSegments;
                float phi = v * MathF.PI * 2f;
                float cs = MathF.Cos(phi);
                float sn = MathF.Sin(phi);

                Vector3 offset = (n * (cs * rx)) + (b * (sn * ry));
                Vector3 tubeNormal = (n * (cs / MathF.Max(rx, 1e-5f))) + (b * (sn / MathF.Max(ry, 1e-5f)));
                tubeNormal = SafeNormalize(tubeNormal, n);
                float scaleRelief = ComputeBodyScaleRelief(
                    i,
                    j,
                    pathSegments,
                    crossSegments,
                    biteIndex,
                    localRadiusByRing[i]);
                Vector3 p = c + offset + (tubeNormal * scaleRelief);
                int vi = (i * crossSegments) + j;
                bodyPositions[vi] = p;

                Vector3 normal = (n * (cs / MathF.Max(rx, 1e-5f))) + (b * (sn / MathF.Max(ry, 1e-5f)));
                Vector3 meshNormal = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : n;
                bodyNormals[vi] = meshNormal;

                // Tangent basis aligned with arc-length U and cross-section V.
                Vector3 tangentU = pathTangent - (Vector3.Dot(pathTangent, meshNormal) * meshNormal);
                tangentU = SafeNormalize(tangentU, n);
                Vector3 dPdv = (-n * (sn * rx)) + (b * (cs * ry));
                bodyTangents[vi] = BuildTangentWithSign(meshNormal, tangentU, dPdv);

                float u = Wrap01(t - seamOffset);
                bodyUVs[vi] = new Vector2(u, v);
            }
        }

        // Rebuild full body basis from displaced geometry so scale relief reads in lighting.
        for (int ring = 0; ring < pathSegments; ring++)
        {
            RebuildRingBasisFromGeometry(
                ring,
                pathSegments,
                crossSegments,
                curvePoints,
                bodyPositions,
                bodyNormals,
                bodyTangents);
        }

        Array.Copy(bodyPositions, finalPositions, vertexCount);
        Array.Copy(bodyNormals, finalNormals, vertexCount);
        Array.Copy(bodyTangents, finalTangents, vertexCount);
        Array.Copy(bodyUVs, finalUVs, vertexCount);

        int headAttachStartIndex = WrapIndex(biteIndex - (NeckInRings + (HeadCoreRings / 2)), pathSegments);

        int[] replacedRings = new int[TotalReplacedRings];
        for (int k = 0; k < TotalReplacedRings; k++)
        {
            int ring = WrapIndex(headAttachStartIndex + k, pathSegments);
            replacedRings[k] = ring;
        }

        // Internal authored head patch: ring-major topology with canonical V in [0, 1).
        OuroborosHeadPatchAsset headPatch = BuildHeadPatchAsset(
            crossSegments,
            HeadCoreRings,
            MathF.Max(0f, collarNode.HeadScale),
            MathF.Max(0f, collarNode.JawBulge),
            MathF.Max(0.45f, collarNode.BodyEllipseYScale));

        for (int k = 0; k < TotalReplacedRings; k++)
        {
            int ring = replacedRings[k];
            int patchRing = ResolveTargetPatchRing(k, headPatch.RingCount);
            bool neckIn = k < NeckInRings;
            bool headCore = k >= NeckInRings && k < (NeckInRings + HeadCoreRings);
            bool neckOut = k >= (NeckInRings + HeadCoreRings);

            float alpha;
            if (neckIn)
            {
                alpha = SmootherStep01((k + 1f) / (NeckInRings + 1f));
            }
            else if (neckOut)
            {
                int outStep = k - (NeckInRings + HeadCoreRings);
                alpha = 1f - SmootherStep01((outStep + 1f) / (NeckOutRings + 1f));
            }
            else
            {
                alpha = 1f;
            }

            Vector3 center = curvePoints[ring];
            Vector3 tVec = tangents[ring];
            Vector3 nVec = normals[ring];
            Vector3 bVec = bitangents[ring];
            float localRadius = localRadiusByRing[ring];
            float ringU = Wrap01(((float)ring / pathSegments) - seamOffset);
            float neckPinchScale = 1f;
            if (neckIn)
            {
                float neckAlpha = SmootherStep01((k + 1f) / (NeckInRings + 1f));
                neckPinchScale = 1f - (0.28f * neckAlpha);
            }

            for (int j = 0; j < crossSegments; j++)
            {
                int vi = VertexIndex(ring, j, crossSegments);
                int pj = headPatch.Index(patchRing, j);
                int pjPrev = headPatch.Index(patchRing, WrapIndex(j - 1, crossSegments));
                int pjNext = headPatch.Index(patchRing, WrapIndex(j + 1, crossSegments));

                Vector3 localPatchPos = headPatch.BoundaryPositions[pj];
                Vector3 targetPos =
                    center +
                    (tVec * (localPatchPos.X * localRadius)) +
                    (nVec * (localPatchPos.Y * localRadius * neckPinchScale)) +
                    (bVec * (localPatchPos.Z * localRadius * neckPinchScale));

                Vector3 localPatchNormal = headPatch.BoundaryNormals[pj];
                Vector3 targetNormal = SafeNormalize(
                    (tVec * localPatchNormal.X) +
                    (nVec * localPatchNormal.Y) +
                    (bVec * localPatchNormal.Z),
                    bodyNormals[vi]);

                Vector3 localPatchTangent = new(headPatch.BoundaryTangents[pj].X, headPatch.BoundaryTangents[pj].Y, headPatch.BoundaryTangents[pj].Z);
                Vector3 targetTangent = SafeNormalize(
                    (tVec * localPatchTangent.X) +
                    (nVec * localPatchTangent.Y) +
                    (bVec * localPatchTangent.Z),
                    bodyTangents[vi].XYZ());
                targetTangent = SafeNormalize(ProjectOntoPlane(targetTangent, targetNormal), bodyTangents[vi].XYZ());

                Vector3 dvLocal = headPatch.BoundaryPositions[pjNext] - headPatch.BoundaryPositions[pjPrev];
                Vector3 dPdvWorld =
                    (tVec * (dvLocal.X * localRadius)) +
                    (nVec * (dvLocal.Y * localRadius * neckPinchScale)) +
                    (bVec * (dvLocal.Z * localRadius * neckPinchScale));

                Vector4 targetTangentSign = BuildTangentWithSign(targetNormal, targetTangent, dPdvWorld);
                headTargetNormals[vi] = targetNormal;
                headTargetTangents[vi] = targetTangentSign;

                if (headCore)
                {
                    finalPositions[vi] = targetPos;
                    finalNormals[vi] = targetNormal;
                    finalTangents[vi] = targetTangentSign;
                }
                else
                {
                    Vector3 bodyPos = bodyPositions[vi];
                    Vector3 bodyOffset = bodyPos - center;
                    float bodyTComp = Vector3.Dot(bodyOffset, tVec);
                    float bodyNComp = Vector3.Dot(bodyOffset, nVec) * neckPinchScale;
                    float bodyBComp = Vector3.Dot(bodyOffset, bVec) * neckPinchScale;
                    Vector3 neckBodyPos = center + (tVec * bodyTComp) + (nVec * bodyNComp) + (bVec * bodyBComp);
                    Vector3 bodyNormal = bodyNormals[vi];
                    Vector3 bodyTangent = bodyTangents[vi].XYZ();
                    float targetSign = targetTangentSign.W >= 0f ? 1f : -1f;
                    Vector3 alignedTargetTangent = targetTangent;
                    AlignTangentForShortestArc(bodyTangent, ref alignedTargetTangent, ref targetSign);

                    Vector3 blendedPos = Vector3.Lerp(neckBodyPos, targetPos, alpha);
                    Vector3 blendedNormal = SafeNormalize(Vector3.Lerp(bodyNormal, targetNormal, alpha), bodyNormal);
                    Vector3 blendedTangent = SafeNormalize(Vector3.Lerp(bodyTangent, alignedTargetTangent, alpha), bodyTangent);
                    blendedTangent = SafeNormalize(ProjectOntoPlane(blendedTangent, blendedNormal), bodyTangent);

                    finalPositions[vi] = blendedPos;
                    finalNormals[vi] = blendedNormal;
                    finalTangents[vi] = BuildTangentWithSign(blendedNormal, blendedTangent, dPdvWorld);
                }

                finalUVs[vi] = new Vector2(ringU, (float)j / crossSegments);
            }
        }

        // Bite-zone mass redistribution: compress under jaw and widen subtly behind jaw.
        var affectedRings = new HashSet<int>();
        for (int d = -3; d <= 3; d++)
        {
            int ring = WrapIndex(biteIndex + d, pathSegments);
            float ringBlend = 1f - (MathF.Abs(d) / 4f);
            float envelope = SmootherStep01(Math.Clamp(ringBlend, 0f, 1f));
            if (envelope <= 1e-4f)
            {
                continue;
            }

            Vector3 center = curvePoints[ring];
            Vector3 tVec = tangents[ring];
            Vector3 nVec = normals[ring];
            Vector3 bVec = bitangents[ring];
            float localRadius = localRadiusByRing[ring];
            for (int j = 0; j < crossSegments; j++)
            {
                float phi = ((float)j / crossSegments) * MathF.PI * 2f;
                float lowerMask = MathF.Max(0f, -MathF.Sin(phi));
                float rearMask = MathF.Max(0f, -MathF.Cos(phi));
                float sideMask = MathF.Pow(MathF.Abs(MathF.Cos(phi)), 0.7f);
                if (lowerMask <= 1e-4f && rearMask <= 1e-4f)
                {
                    continue;
                }

                int vi = VertexIndex(ring, j, crossSegments);
                Vector3 offset = finalPositions[vi] - center;
                float tComp = Vector3.Dot(offset, tVec);
                float nComp = Vector3.Dot(offset, nVec);
                float bComp = Vector3.Dot(offset, bVec);

                float radialCompression = 1f - (0.16f * envelope * lowerMask);
                float verticalCompression = 1f - (0.10f * envelope * lowerMask);
                float rearWiden = 1f + (0.08f * envelope * rearMask * (0.45f + (0.55f * sideMask)));
                nComp = (nComp * radialCompression * rearWiden) - (0.05f * localRadius * envelope * lowerMask);
                bComp *= verticalCompression;

                finalPositions[vi] = center + (tVec * tComp) + (nVec * nComp) + (bVec * bComp);
                affectedRings.Add(ring);
            }
        }

        // Boundary basis override is mandatory on welded boundaries.
        int boundaryAK = NeckInRings;
        int boundaryBK = NeckInRings + HeadCoreRings - 1;
        int boundaryARing = replacedRings[boundaryAK];
        int boundaryBRing = replacedRings[boundaryBK];
        OverrideBoundaryBasis(boundaryARing, crossSegments, bodyNormals, bodyTangents, finalNormals, finalTangents);
        OverrideBoundaryBasis(boundaryBRing, crossSegments, bodyNormals, bodyTangents, finalNormals, finalTangents);

        // 1-2 rings inward from weld: controlled basis transition with re-projection and sign recompute.
        BlendInnerSeamBasis(
            replacedRings,
            crossSegments,
            pathSegments,
            NeckInRings + 1,
            0.35f,
            bodyNormals,
            bodyTangents,
            headTargetNormals,
            headTargetTangents,
            finalPositions,
            finalNormals,
            finalTangents);
        BlendInnerSeamBasis(
            replacedRings,
            crossSegments,
            pathSegments,
            NeckInRings + 2,
            0.70f,
            bodyNormals,
            bodyTangents,
            headTargetNormals,
            headTargetTangents,
            finalPositions,
            finalNormals,
            finalTangents);
        BlendInnerSeamBasis(
            replacedRings,
            crossSegments,
            pathSegments,
            boundaryBK - 1,
            0.70f,
            bodyNormals,
            bodyTangents,
            headTargetNormals,
            headTargetTangents,
            finalPositions,
            finalNormals,
            finalTangents);
        BlendInnerSeamBasis(
            replacedRings,
            crossSegments,
            pathSegments,
            boundaryBK - 2,
            0.35f,
            bodyNormals,
            bodyTangents,
            headTargetNormals,
            headTargetTangents,
            finalPositions,
            finalNormals,
            finalTangents);

        // Tail-under-jaw scaffold deformation and basis re-orthonormalization.
        int tailEntryIndex = WrapIndex(
            biteIndex + (int)MathF.Round((3f + ((10f - 3f) * collarNode.TailUnderlap))),
            pathSegments);
        for (int d = -2; d <= 2; d++)
        {
            int ring = WrapIndex(tailEntryIndex + d, pathSegments);
            float x = 1f - (MathF.Abs(d) / 3f);
            float envelope = SmootherStep01(Math.Clamp(x, 0f, 1f));
            if (envelope <= 1e-4f)
            {
                continue;
            }

            float localRadius = localRadiusByRing[ring];
            Vector3 nVec = normals[ring];
            Vector3 bVec = bitangents[ring];
            for (int j = 0; j < crossSegments; j++)
            {
                float phi = ((float)j / crossSegments) * MathF.PI * 2f;
                float jawMask = MathF.Max(0f, -MathF.Sin(phi));
                if (jawMask <= 1e-4f)
                {
                    continue;
                }

                int vi = VertexIndex(ring, j, crossSegments);
                Vector3 shift =
                    (-bVec * (0.08f * localRadius * jawMask * envelope)) +
                    (-nVec * (0.03f * localRadius * jawMask * envelope));
                finalPositions[vi] += shift;
                affectedRings.Add(ring);
            }
        }

        foreach (int ring in affectedRings)
        {
            RebuildRingBasisFromGeometry(
                ring,
                pathSegments,
                crossSegments,
                curvePoints,
                finalPositions,
                finalNormals,
                finalTangents);
        }

        // Reassert hard seam basis after any local post-deformation.
        OverrideBoundaryBasis(boundaryARing, crossSegments, bodyNormals, bodyTangents, finalNormals, finalTangents);
        OverrideBoundaryBasis(boundaryBRing, crossSegments, bodyNormals, bodyTangents, finalNormals, finalTangents);

        for (int i = 0; i < pathSegments; i++)
        {
            int iNext = (i + 1) % pathSegments;
            int row0 = i * crossSegments;
            int row1 = iNext * crossSegments;
            for (int j = 0; j < crossSegments; j++)
            {
                int jNext = (j + 1) % crossSegments;
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

        var vertices = new MetalVertex[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            vertices[i] = new MetalVertex
            {
                Position = finalPositions[i],
                Normal = finalNormals[i],
                Tangent = finalTangents[i]
            };
        }

        return new CollarMesh
        {
            Vertices = vertices,
            Indices = indices.ToArray(),
            UVs = finalUVs,
            Tangents = finalTangents,
            ReferenceRadius = MathF.Max(knobRadius, centerlineRadius + (bodyRadiusBase * 1.2f))
        };
    }

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

    private static float ComputeBodyScaleRelief(
        int ring,
        int slice,
        int pathSegments,
        int crossSegments,
        int biteIndex,
        float localRadius)
    {
        float u = ((float)ring / Math.Max(1, pathSegments)) * MathF.Max(24f, pathSegments * 0.42f);
        float parityShift = (((int)MathF.Floor(u)) & 1) == 0 ? 0f : 0.5f;
        float v = ((float)slice / Math.Max(1, crossSegments)) * MathF.Max(10f, crossSegments * 0.95f) + parityShift;

        float fu = Fract(u) - 0.5f;
        float fv = Fract(v) - 0.5f;
        float nx = fu / 0.55f;
        float ny = fv / 0.42f;
        float d = MathF.Sqrt((nx * nx) + (ny * ny));
        float cell = Math.Clamp(1f - d, 0f, 1f);
        cell = cell * cell * (3f - (2f * cell));

        float seamU = 1f - SmoothStep(0.42f, 0.50f, MathF.Abs(fu));
        float seamV = 1f - SmoothStep(0.36f, 0.50f, MathF.Abs(fv));
        float seam = MathF.Max(seamU, seamV);

        int ringDelta = MinWrappedDistance(ring, biteIndex, pathSegments);
        float headFade = 1f - SmoothStep(0f, TotalReplacedRings * 1.2f, ringDelta);
        float bodyMask = 1f - headFade;

        float phi = ((float)slice / Math.Max(1, crossSegments)) * MathF.PI * 2f;
        float bellyMask = 0.68f + (0.32f * MathF.Abs(MathF.Sin(phi)));

        float bump = cell * 0.055f;
        float groove = seam * 0.022f;
        return localRadius * bodyMask * bellyMask * (bump - groove);
    }

    private static float Fract(float x)
    {
        return x - MathF.Floor(x);
    }

    private static int MinWrappedDistance(int a, int b, int count)
    {
        int d = Math.Abs(a - b);
        return Math.Min(d, Math.Max(0, count - d));
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

    private static float WrapSignedRadians(float radians)
    {
        float twoPi = MathF.PI * 2f;
        float r = radians % twoPi;
        if (r > MathF.PI)
        {
            r -= twoPi;
        }
        else if (r < -MathF.PI)
        {
            r += twoPi;
        }

        return r;
    }

    private static float Wrap01(float value)
    {
        float wrapped = value - MathF.Floor(value);
        if (wrapped < 0f)
        {
            wrapped += 1f;
        }

        return wrapped;
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

internal static class VectorExtensions
{
    public static Vector3 XYZ(this Vector4 value)
    {
        return new Vector3(value.X, value.Y, value.Z);
    }
}
