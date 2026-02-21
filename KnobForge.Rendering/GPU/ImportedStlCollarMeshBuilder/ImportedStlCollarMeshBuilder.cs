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
    private const uint GlbMagic = 0x46546C67;
    private const uint GlbJsonChunkType = 0x4E4F534A;
    private const uint GlbBinChunkType = 0x004E4942;
    private static readonly object ImportedMeshCacheLock = new();
    private static string? _cachedImportedMeshPath;
    private static long _cachedImportedMeshTicks;
    private static List<Vector3>? _cachedImportedMeshPositions;
    private static List<uint>? _cachedImportedMeshIndices;

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
        if (collarNode is null ||
            !collarNode.Enabled ||
            !CollarNode.IsImportedMeshPreset(collarNode.Preset))
        {
            return null;
        }

        string importedMeshPath = CollarNode.ResolveImportedMeshPath(collarNode.Preset, collarNode.ImportedMeshPath);
        if (string.IsNullOrWhiteSpace(importedMeshPath))
        {
            return null;
        }

        if (!File.Exists(importedMeshPath))
        {
            return null;
        }

        if (!TryReadImportedMesh(importedMeshPath, out List<Vector3> sourcePositions, out List<uint> indices) ||
            sourcePositions.Count == 0 ||
            indices.Count < 3)
        {
            return null;
        }

        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);
        for (int i = 0; i < sourcePositions.Count; i++)
        {
            Vector3 p = sourcePositions[i];
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        Vector3 center = (min + max) * 0.5f;
        var centered = new Vector3[sourcePositions.Count];
        for (int i = 0; i < sourcePositions.Count; i++)
        {
            centered[i] = sourcePositions[i] - center;
        }

        // Auto-orient imported mesh so its dominant ring plane lies on XY (knob face plane).
        centered = AutoOrientToKnobPlane(centered);

        // Recenter around body loop (not head outlier) so imported collar lands on knob center.
        int initialHeadIndex = FindHeadAnchorIndex(centered);
        float initialHeadAngle = MathF.Atan2(centered[initialHeadIndex].Y, centered[initialHeadIndex].X);
        Vector2 initialBodyCenter = ComputeWeightedBodyCenter(centered, initialHeadAngle, 0.20f, 0.85f);
        for (int i = 0; i < centered.Length; i++)
        {
            centered[i] = new Vector3(
                centered[i].X - initialBodyCenter.X,
                centered[i].Y - initialBodyCenter.Y,
                centered[i].Z);
        }

        (_, float sourceCenterRadiusPreDeform, _) = ComputeRobustRadialBands(centered);
        centered = ApplyBodyLengthThicknessDeform(
            centered,
            sourceCenterRadiusPreDeform,
            collarNode.ImportedBodyLengthScale,
            collarNode.ImportedBodyThicknessScale,
            collarNode.ImportedHeadLengthScale,
            collarNode.ImportedHeadThicknessScale,
            collarNode.ImportedHeadAngleOffsetRadians);

        (float sourceInnerRadius, float sourceCenterRadius, float sourceOuterRadius) = ComputeRobustRadialBands(centered);
        if (sourceOuterRadius <= 1e-6f || sourceCenterRadius <= 1e-6f)
        {
            return null;
        }

        float knobRadius = MathF.Max(10f, modelNode.Radius);
        float knobHalfHeight = MathF.Max(10f, modelNode.Height * 0.5f);
        float targetInnerRadius = knobRadius * MathF.Max(0.4f, collarNode.InnerRadiusRatio + collarNode.GapToKnobRatio);
        float targetBodyRadius = knobRadius * MathF.Max(0.03f, collarNode.BodyRadiusRatio);
        float targetCenterRadius = targetInnerRadius + targetBodyRadius;
        float scale = (targetCenterRadius / sourceCenterRadius) * collarNode.ImportedScale;
        float rotation = collarNode.OverallRotationRadians + collarNode.ImportedRotationRadians;
        float cosA = MathF.Cos(rotation);
        float sinA = MathF.Sin(rotation);
        float zOffset = knobHalfHeight * collarNode.ElevationRatio;
        float xOffset = collarNode.ImportedOffsetXRatio * knobRadius;
        float yOffset = collarNode.ImportedOffsetYRatio * knobRadius;
        Vector3 mirrorScale = new(
            collarNode.ImportedMirrorX ? -1f : 1f,
            collarNode.ImportedMirrorY ? -1f : 1f,
            collarNode.ImportedMirrorZ ? -1f : 1f);

        var positions = new Vector3[centered.Length];
        for (int i = 0; i < centered.Length; i++)
        {
            Vector3 p = (centered[i] * scale) * mirrorScale;
            positions[i] = new Vector3(
                ((p.X * cosA) - (p.Y * sinA)) + xOffset,
                ((p.X * sinA) + (p.Y * cosA)) + yOffset,
                p.Z + zOffset);
        }

        // Enforce consistent triangle orientation across adjacency, then make components outward.
        NormalizeTriangleWinding(positions, indices);

        Vector3[] normals = ComputeVertexNormals(positions, indices);
        float inflateWorld = collarNode.ImportedInflateRatio * knobRadius;
        if (MathF.Abs(inflateWorld) > 1e-6f)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i] += normals[i] * inflateWorld;
            }

            normals = ComputeVertexNormals(positions, indices);
        }

        var tangents = new Vector4[positions.Length];
        var uvs = new Vector2[positions.Length];
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;
        float referenceRadius = knobRadius;
        for (int i = 0; i < positions.Length; i++)
        {
            minZ = MathF.Min(minZ, positions[i].Z);
            maxZ = MathF.Max(maxZ, positions[i].Z);
            referenceRadius = MathF.Max(referenceRadius, positions[i].Length());
        }

        float zSpan = MathF.Max(1e-6f, maxZ - minZ);
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 n = normals[i];
            Vector3 t = Vector3.Cross(Vector3.UnitZ, n);
            if (t.LengthSquared() <= 1e-8f)
            {
                t = Vector3.Cross(Vector3.UnitX, n);
            }

            t = t.LengthSquared() > 1e-8f ? Vector3.Normalize(t) : Vector3.UnitX;
            tangents[i] = new Vector4(t, 1f);

            float angle = MathF.Atan2(positions[i].Y, positions[i].X);
            float u = Wrap01((angle / (MathF.PI * 2f)) + 0.5f);
            float v = (positions[i].Z - minZ) / zSpan;
            uvs[i] = new Vector2(u, v);
        }

        var vertices = new MetalVertex[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            vertices[i] = new MetalVertex
            {
                Position = positions[i],
                Normal = normals[i],
                Tangent = tangents[i]
            };
        }

        return new CollarMesh
        {
            Vertices = vertices,
            Indices = indices.ToArray(),
            UVs = uvs,
            Tangents = tangents,
            ReferenceRadius = referenceRadius
        };
    }
}
