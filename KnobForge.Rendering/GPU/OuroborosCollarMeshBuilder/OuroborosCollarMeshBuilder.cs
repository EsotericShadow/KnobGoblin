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

public static partial class OuroborosCollarMeshBuilder
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
}
