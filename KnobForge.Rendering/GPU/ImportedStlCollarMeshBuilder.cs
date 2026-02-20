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

public static class ImportedStlCollarMeshBuilder
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

        var positions = new Vector3[centered.Length];
        for (int i = 0; i < centered.Length; i++)
        {
            Vector3 p = centered[i] * scale;
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

    private static bool TryReadImportedMesh(string path, out List<Vector3> positions, out List<uint> indices)
    {
        positions = new List<Vector3>();
        indices = new List<uint>();
        long fileTicks;
        try
        {
            fileTicks = File.GetLastWriteTimeUtc(path).Ticks;
        }
        catch
        {
            return false;
        }

        lock (ImportedMeshCacheLock)
        {
            if (_cachedImportedMeshPositions is not null &&
                _cachedImportedMeshIndices is not null &&
                string.Equals(_cachedImportedMeshPath, path, StringComparison.Ordinal) &&
                _cachedImportedMeshTicks == fileTicks)
            {
                positions = new List<Vector3>(_cachedImportedMeshPositions);
                indices = new List<uint>(_cachedImportedMeshIndices);
                return true;
            }
        }

        string extension = Path.GetExtension(path);
        bool readOk = string.Equals(extension, ".glb", StringComparison.OrdinalIgnoreCase)
            ? TryReadBinaryGlb(path, out positions, out indices)
            : string.Equals(extension, ".stl", StringComparison.OrdinalIgnoreCase) &&
              TryReadBinaryStl(path, out positions, out indices);

        if (!readOk || positions.Count == 0 || indices.Count < 3)
        {
            positions = new List<Vector3>();
            indices = new List<uint>();
            return false;
        }

        if (TryExtractLikelyCollarComponent(positions, indices, out List<Vector3> extractedPositions, out List<uint> extractedIndices))
        {
            positions = extractedPositions;
            indices = extractedIndices;
        }

        lock (ImportedMeshCacheLock)
        {
            _cachedImportedMeshPath = path;
            _cachedImportedMeshTicks = fileTicks;
            _cachedImportedMeshPositions = new List<Vector3>(positions);
            _cachedImportedMeshIndices = new List<uint>(indices);
        }

        return true;
    }

    private static bool TryExtractLikelyCollarComponent(
        IReadOnlyList<Vector3> sourcePositions,
        IReadOnlyList<uint> sourceIndices,
        out List<Vector3> extractedPositions,
        out List<uint> extractedIndices)
    {
        extractedPositions = new List<Vector3>();
        extractedIndices = new List<uint>();

        int vertexCount = sourcePositions.Count;
        int triangleCount = sourceIndices.Count / 3;
        if (vertexCount == 0 || triangleCount < 3)
        {
            return false;
        }

        var dsu = new DisjointSet(vertexCount);
        for (int tri = 0; tri < triangleCount; tri++)
        {
            int baseIndex = tri * 3;
            int i0 = (int)sourceIndices[baseIndex + 0];
            int i1 = (int)sourceIndices[baseIndex + 1];
            int i2 = (int)sourceIndices[baseIndex + 2];
            if (!IsVertexIndexValid(i0, vertexCount) ||
                !IsVertexIndexValid(i1, vertexCount) ||
                !IsVertexIndexValid(i2, vertexCount) ||
                i0 == i1 || i1 == i2 || i2 == i0)
            {
                continue;
            }

            dsu.Union(i0, i1);
            dsu.Union(i1, i2);
            dsu.Union(i2, i0);
        }

        int[] rootByVertex = new int[vertexCount];
        var verticesByRoot = new Dictionary<int, List<int>>();
        for (int vertex = 0; vertex < vertexCount; vertex++)
        {
            int root = dsu.Find(vertex);
            rootByVertex[vertex] = root;
            if (!verticesByRoot.TryGetValue(root, out List<int>? list))
            {
                list = new List<int>();
                verticesByRoot[root] = list;
            }

            list.Add(vertex);
        }

        if (verticesByRoot.Count <= 1)
        {
            return false;
        }

        var trianglesByRoot = new Dictionary<int, List<int>>();
        for (int tri = 0; tri < triangleCount; tri++)
        {
            int baseIndex = tri * 3;
            int i0 = (int)sourceIndices[baseIndex + 0];
            int i1 = (int)sourceIndices[baseIndex + 1];
            int i2 = (int)sourceIndices[baseIndex + 2];
            if (!IsVertexIndexValid(i0, vertexCount) ||
                !IsVertexIndexValid(i1, vertexCount) ||
                !IsVertexIndexValid(i2, vertexCount) ||
                i0 == i1 || i1 == i2 || i2 == i0)
            {
                continue;
            }

            int root = rootByVertex[i0];
            if (rootByVertex[i1] != root || rootByVertex[i2] != root)
            {
                continue;
            }

            if (!trianglesByRoot.TryGetValue(root, out List<int>? list))
            {
                list = new List<int>();
                trianglesByRoot[root] = list;
            }

            list.Add(tri);
        }

        int minTriangles = Math.Max(128, triangleCount / 200);
        int minVertices = Math.Max(128, vertexCount / 300);
        int bestRoot = int.MinValue;
        float bestScore = float.MinValue;
        foreach ((int root, List<int> triangles) in trianglesByRoot)
        {
            if (triangles.Count < minTriangles)
            {
                continue;
            }

            if (!verticesByRoot.TryGetValue(root, out List<int>? componentVertices) ||
                componentVertices.Count < minVertices)
            {
                continue;
            }

            var radii = new List<float>(componentVertices.Count);
            for (int i = 0; i < componentVertices.Count; i++)
            {
                Vector3 p = sourcePositions[componentVertices[i]];
                radii.Add(new Vector2(p.X, p.Y).Length());
            }

            if (radii.Count < minVertices)
            {
                continue;
            }

            radii.Sort();
            float r05 = Percentile(radii, 0.05f);
            float r50 = Percentile(radii, 0.50f);
            float r95 = Percentile(radii, 0.95f);
            if (r95 <= 1e-6f)
            {
                continue;
            }

            float holeRatio = r05 / r95;
            float triangleFraction = triangles.Count / (float)triangleCount;
            float score = (holeRatio * 12f) + r50 + (triangleFraction * 2f);
            if (score > bestScore)
            {
                bestScore = score;
                bestRoot = root;
            }
        }

        if (bestRoot == int.MinValue ||
            !trianglesByRoot.TryGetValue(bestRoot, out List<int>? selectedTriangles) ||
            !verticesByRoot.TryGetValue(bestRoot, out List<int>? selectedVertices))
        {
            return false;
        }

        if (selectedTriangles.Count <= 0 || selectedTriangles.Count >= triangleCount)
        {
            return false;
        }

        var oldToNew = new Dictionary<int, uint>(selectedVertices.Count);
        for (int i = 0; i < selectedVertices.Count; i++)
        {
            int oldIndex = selectedVertices[i];
            oldToNew[oldIndex] = (uint)extractedPositions.Count;
            extractedPositions.Add(sourcePositions[oldIndex]);
        }

        extractedIndices.Capacity = selectedTriangles.Count * 3;
        for (int i = 0; i < selectedTriangles.Count; i++)
        {
            int tri = selectedTriangles[i];
            int baseIndex = tri * 3;
            int old0 = (int)sourceIndices[baseIndex + 0];
            int old1 = (int)sourceIndices[baseIndex + 1];
            int old2 = (int)sourceIndices[baseIndex + 2];
            if (!oldToNew.TryGetValue(old0, out uint i0) ||
                !oldToNew.TryGetValue(old1, out uint i1) ||
                !oldToNew.TryGetValue(old2, out uint i2) ||
                i0 == i1 || i1 == i2 || i2 == i0)
            {
                continue;
            }

            extractedIndices.Add(i0);
            extractedIndices.Add(i1);
            extractedIndices.Add(i2);
        }

        if (extractedPositions.Count == 0 || extractedIndices.Count < 3)
        {
            extractedPositions = new List<Vector3>();
            extractedIndices = new List<uint>();
            return false;
        }

        Console.WriteLine(
            $"[ImportedMesh] Extracted outer component for collar: components={verticesByRoot.Count}, selectedTriangles={selectedTriangles.Count}/{triangleCount}, selectedVertices={extractedPositions.Count}/{vertexCount}");
        return true;
    }

    private static bool IsVertexIndexValid(int index, int vertexCount)
    {
        return index >= 0 && index < vertexCount;
    }

    private static bool TryReadBinaryGlb(string path, out List<Vector3> positions, out List<uint> indices)
    {
        positions = new List<Vector3>();
        indices = new List<uint>();

        byte[] fileBytes;
        try
        {
            fileBytes = File.ReadAllBytes(path);
        }
        catch
        {
            return false;
        }

        if (fileBytes.Length < 20)
        {
            return false;
        }

        using var stream = new MemoryStream(fileBytes, writable: false);
        using var reader = new BinaryReader(stream);

        if (reader.ReadUInt32() != GlbMagic)
        {
            return false;
        }

        if (reader.ReadUInt32() != 2u)
        {
            return false;
        }

        uint declaredLength = reader.ReadUInt32();
        if (declaredLength < 20u || declaredLength > fileBytes.Length)
        {
            return false;
        }

        string? jsonChunkText = null;
        byte[]? binaryChunk = null;
        while ((stream.Position + 8) <= declaredLength)
        {
            uint chunkLength = reader.ReadUInt32();
            uint chunkType = reader.ReadUInt32();
            if (chunkLength > int.MaxValue || (stream.Position + chunkLength) > declaredLength)
            {
                return false;
            }

            byte[] chunkData = reader.ReadBytes((int)chunkLength);
            if (chunkData.Length != (int)chunkLength)
            {
                return false;
            }

            if (chunkType == GlbJsonChunkType)
            {
                jsonChunkText = Encoding.UTF8.GetString(chunkData)
                    .TrimEnd('\0', '\t', '\r', '\n', ' ');
            }
            else if (chunkType == GlbBinChunkType && binaryChunk is null)
            {
                binaryChunk = chunkData;
            }
        }

        if (string.IsNullOrWhiteSpace(jsonChunkText) || binaryChunk is null)
        {
            return false;
        }

        using JsonDocument document = JsonDocument.Parse(jsonChunkText);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("meshes", out JsonElement meshesElement) ||
            meshesElement.ValueKind != JsonValueKind.Array ||
            meshesElement.GetArrayLength() == 0)
        {
            return false;
        }

        if (!root.TryGetProperty("accessors", out JsonElement accessorsElement) ||
            accessorsElement.ValueKind != JsonValueKind.Array ||
            accessorsElement.GetArrayLength() == 0)
        {
            return false;
        }

        if (!root.TryGetProperty("bufferViews", out JsonElement bufferViewsElement) ||
            bufferViewsElement.ValueKind != JsonValueKind.Array ||
            bufferViewsElement.GetArrayLength() == 0)
        {
            return false;
        }

        foreach (JsonElement mesh in meshesElement.EnumerateArray())
        {
            if (!mesh.TryGetProperty("primitives", out JsonElement primitivesElement) ||
                primitivesElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement primitive in primitivesElement.EnumerateArray())
            {
                int mode = 4; // TRIANGLES
                if (primitive.TryGetProperty("mode", out JsonElement modeElement) &&
                    modeElement.ValueKind == JsonValueKind.Number &&
                    modeElement.TryGetInt32(out int parsedMode))
                {
                    mode = parsedMode;
                }

                if (mode != 4)
                {
                    continue;
                }

                if (!primitive.TryGetProperty("attributes", out JsonElement attributesElement) ||
                    attributesElement.ValueKind != JsonValueKind.Object ||
                    !attributesElement.TryGetProperty("POSITION", out JsonElement positionAccessorElement) ||
                    !positionAccessorElement.TryGetInt32(out int positionAccessorIndex))
                {
                    continue;
                }

                if (!TryReadAccessorVector3(
                        accessorsElement,
                        bufferViewsElement,
                        binaryChunk,
                        positionAccessorIndex,
                        out Vector3[] primitivePositions) ||
                    primitivePositions.Length == 0)
                {
                    continue;
                }

                int baseVertex = positions.Count;
                for (int i = 0; i < primitivePositions.Length; i++)
                {
                    positions.Add(primitivePositions[i]);
                }

                if (primitive.TryGetProperty("indices", out JsonElement indicesAccessorElement) &&
                    indicesAccessorElement.TryGetInt32(out int indicesAccessorIndex) &&
                    TryReadAccessorIndices(
                        accessorsElement,
                        bufferViewsElement,
                        binaryChunk,
                        indicesAccessorIndex,
                        out uint[] primitiveIndices) &&
                    primitiveIndices.Length >= 3)
                {
                    for (int i = 0; i + 2 < primitiveIndices.Length; i += 3)
                    {
                        uint i0 = primitiveIndices[i + 0];
                        uint i1 = primitiveIndices[i + 1];
                        uint i2 = primitiveIndices[i + 2];
                        if (i0 >= primitivePositions.Length ||
                            i1 >= primitivePositions.Length ||
                            i2 >= primitivePositions.Length ||
                            i0 == i1 || i1 == i2 || i2 == i0)
                        {
                            continue;
                        }

                        indices.Add((uint)(baseVertex + (int)i0));
                        indices.Add((uint)(baseVertex + (int)i1));
                        indices.Add((uint)(baseVertex + (int)i2));
                    }
                }
                else
                {
                    for (int i = 0; i + 2 < primitivePositions.Length; i += 3)
                    {
                        indices.Add((uint)(baseVertex + i + 0));
                        indices.Add((uint)(baseVertex + i + 1));
                        indices.Add((uint)(baseVertex + i + 2));
                    }
                }
            }
        }

        return positions.Count > 0 && indices.Count >= 3;
    }

    private static bool TryReadAccessorVector3(
        JsonElement accessorsElement,
        JsonElement bufferViewsElement,
        byte[] bufferBytes,
        int accessorIndex,
        out Vector3[] vectors)
    {
        vectors = Array.Empty<Vector3>();
        if (!TryResolveAccessorView(accessorsElement, bufferViewsElement, bufferBytes.Length, accessorIndex, out AccessorView view))
        {
            return false;
        }

        if (!string.Equals(view.Type, "VEC3", StringComparison.Ordinal) ||
            view.ComponentType != 5126)
        {
            return false;
        }

        int stride = view.ByteStride > 0 ? view.ByteStride : 12;
        if (stride < 12)
        {
            return false;
        }

        long lastVectorStart = view.DataOffset + ((long)(view.Count - 1) * stride);
        long accessorEnd = view.DataOffset + view.ByteLength;
        if (lastVectorStart < 0 ||
            (lastVectorStart + 12) > bufferBytes.Length ||
            (lastVectorStart + 12) > accessorEnd)
        {
            return false;
        }

        vectors = new Vector3[view.Count];
        for (int i = 0; i < view.Count; i++)
        {
            int offset = view.DataOffset + (i * stride);
            float x = BitConverter.ToSingle(bufferBytes, offset + 0);
            float y = BitConverter.ToSingle(bufferBytes, offset + 4);
            float z = BitConverter.ToSingle(bufferBytes, offset + 8);
            vectors[i] = new Vector3(x, y, z);
        }

        return true;
    }

    private static bool TryReadAccessorIndices(
        JsonElement accessorsElement,
        JsonElement bufferViewsElement,
        byte[] bufferBytes,
        int accessorIndex,
        out uint[] values)
    {
        values = Array.Empty<uint>();
        if (!TryResolveAccessorView(accessorsElement, bufferViewsElement, bufferBytes.Length, accessorIndex, out AccessorView view))
        {
            return false;
        }

        if (!string.Equals(view.Type, "SCALAR", StringComparison.Ordinal))
        {
            return false;
        }

        int componentSize = view.ComponentType switch
        {
            5121 => 1, // UNSIGNED_BYTE
            5123 => 2, // UNSIGNED_SHORT
            5125 => 4, // UNSIGNED_INT
            _ => 0
        };
        if (componentSize == 0)
        {
            return false;
        }

        int stride = view.ByteStride > 0 ? view.ByteStride : componentSize;
        if (stride < componentSize)
        {
            return false;
        }

        long lastValueStart = view.DataOffset + ((long)(view.Count - 1) * stride);
        long accessorEnd = view.DataOffset + view.ByteLength;
        if (lastValueStart < 0 ||
            (lastValueStart + componentSize) > bufferBytes.Length ||
            (lastValueStart + componentSize) > accessorEnd)
        {
            return false;
        }

        values = new uint[view.Count];
        for (int i = 0; i < view.Count; i++)
        {
            int offset = view.DataOffset + (i * stride);
            values[i] = view.ComponentType switch
            {
                5121 => bufferBytes[offset],
                5123 => BitConverter.ToUInt16(bufferBytes, offset),
                5125 => BitConverter.ToUInt32(bufferBytes, offset),
                _ => 0u
            };
        }

        return true;
    }

    private static bool TryResolveAccessorView(
        JsonElement accessorsElement,
        JsonElement bufferViewsElement,
        int bufferLength,
        int accessorIndex,
        out AccessorView view)
    {
        view = default;
        if (!TryGetArrayElement(accessorsElement, accessorIndex, out JsonElement accessorElement) ||
            accessorElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // Sparse accessors are uncommon for static meshes; keep importer strict.
        if (accessorElement.TryGetProperty("sparse", out _))
        {
            return false;
        }

        if (!accessorElement.TryGetProperty("bufferView", out JsonElement accessorBufferViewElement) ||
            !accessorBufferViewElement.TryGetInt32(out int bufferViewIndex) ||
            !TryGetArrayElement(bufferViewsElement, bufferViewIndex, out JsonElement bufferViewElement) ||
            bufferViewElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!accessorElement.TryGetProperty("count", out JsonElement countElement) ||
            !countElement.TryGetInt32(out int count) ||
            count <= 0)
        {
            return false;
        }

        if (!accessorElement.TryGetProperty("componentType", out JsonElement componentTypeElement) ||
            !componentTypeElement.TryGetInt32(out int componentType))
        {
            return false;
        }

        if (!accessorElement.TryGetProperty("type", out JsonElement typeElement) ||
            typeElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string? type = typeElement.GetString();
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        if (bufferViewElement.TryGetProperty("buffer", out JsonElement bufferIndexElement) &&
            bufferIndexElement.TryGetInt32(out int bufferIndex) &&
            bufferIndex != 0)
        {
            // GLB uses the first (and usually only) binary buffer chunk.
            return false;
        }

        int bufferViewOffset = 0;
        if (bufferViewElement.TryGetProperty("byteOffset", out JsonElement bufferViewOffsetElement) &&
            bufferViewOffsetElement.TryGetInt32(out int parsedBufferViewOffset))
        {
            bufferViewOffset = parsedBufferViewOffset;
        }

        if (!bufferViewElement.TryGetProperty("byteLength", out JsonElement bufferViewLengthElement) ||
            !bufferViewLengthElement.TryGetInt32(out int bufferViewLength) ||
            bufferViewLength <= 0)
        {
            return false;
        }

        int accessorOffset = 0;
        if (accessorElement.TryGetProperty("byteOffset", out JsonElement accessorOffsetElement) &&
            accessorOffsetElement.TryGetInt32(out int parsedAccessorOffset))
        {
            accessorOffset = parsedAccessorOffset;
        }

        int byteStride = 0;
        if (bufferViewElement.TryGetProperty("byteStride", out JsonElement byteStrideElement) &&
            byteStrideElement.TryGetInt32(out int parsedByteStride))
        {
            byteStride = parsedByteStride;
        }

        int dataOffset = bufferViewOffset + accessorOffset;
        if (dataOffset < 0 || dataOffset >= bufferLength)
        {
            return false;
        }

        if (dataOffset > (bufferViewOffset + bufferViewLength))
        {
            return false;
        }

        view = new AccessorView(
            DataOffset: dataOffset,
            Count: count,
            ComponentType: componentType,
            Type: type,
            ByteStride: byteStride,
            ByteLength: bufferViewLength - accessorOffset);
        return view.ByteLength > 0;
    }

    private static bool TryGetArrayElement(JsonElement arrayElement, int index, out JsonElement value)
    {
        value = default;
        if (arrayElement.ValueKind != JsonValueKind.Array || index < 0 || index >= arrayElement.GetArrayLength())
        {
            return false;
        }

        value = arrayElement[index];
        return true;
    }

    private static bool TryReadBinaryStl(string path, out List<Vector3> positions, out List<uint> indices)
    {
        positions = new List<Vector3>();
        indices = new List<uint>();

        using var fs = File.OpenRead(path);
        if (fs.Length < 84)
        {
            return false;
        }

        using var br = new BinaryReader(fs);
        br.ReadBytes(80); // header
        uint triCount = br.ReadUInt32();
        long expected = 84L + (triCount * 50L);
        if (expected != fs.Length)
        {
            return false;
        }

        var map = new Dictionary<VertexKey, uint>(Math.Min((int)(triCount * 2), 1_000_000));
        for (uint i = 0; i < triCount; i++)
        {
            // face normal in STL is ignored; recomputed robustly later.
            br.ReadSingle();
            br.ReadSingle();
            br.ReadSingle();

            Vector3 v0 = new(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            Vector3 v1 = new(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            Vector3 v2 = new(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
            br.ReadUInt16(); // attribute byte count

            uint i0 = GetOrAddVertex(v0, map, positions);
            uint i1 = GetOrAddVertex(v1, map, positions);
            uint i2 = GetOrAddVertex(v2, map, positions);
            if (i0 == i1 || i1 == i2 || i2 == i0)
            {
                continue;
            }

            indices.Add(i0);
            indices.Add(i1);
            indices.Add(i2);
        }

        return positions.Count > 0 && indices.Count >= 3;
    }

    private static void NormalizeTriangleWinding(IReadOnlyList<Vector3> positions, List<uint> indices)
    {
        int triangleCount = indices.Count / 3;
        if (triangleCount <= 0)
        {
            return;
        }

        var edgeUses = new Dictionary<EdgeKey, List<EdgeUse>>(triangleCount * 3);
        for (int tri = 0; tri < triangleCount; tri++)
        {
            uint a = indices[(tri * 3) + 0];
            uint b = indices[(tri * 3) + 1];
            uint c = indices[(tri * 3) + 2];
            AddEdgeUse(edgeUses, tri, a, b);
            AddEdgeUse(edgeUses, tri, b, c);
            AddEdgeUse(edgeUses, tri, c, a);
        }

        var adjacency = new List<(int Neighbor, bool SameDirection)>[triangleCount];
        for (int i = 0; i < triangleCount; i++)
        {
            adjacency[i] = new List<(int, bool)>(3);
        }

        foreach (List<EdgeUse> uses in edgeUses.Values)
        {
            if (uses.Count < 2)
            {
                continue;
            }

            for (int i = 0; i < uses.Count - 1; i++)
            {
                for (int j = i + 1; j < uses.Count; j++)
                {
                    EdgeUse a = uses[i];
                    EdgeUse b = uses[j];
                    bool sameDirection = a.Forward == b.Forward;
                    adjacency[a.Triangle].Add((b.Triangle, sameDirection));
                    adjacency[b.Triangle].Add((a.Triangle, sameDirection));
                }
            }
        }

        var component = new int[triangleCount];
        Array.Fill(component, -1);
        var flip = new bool[triangleCount];
        int componentCount = 0;
        var queue = new Queue<int>();
        for (int seed = 0; seed < triangleCount; seed++)
        {
            if (component[seed] >= 0)
            {
                continue;
            }

            component[seed] = componentCount;
            flip[seed] = false;
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                int tri = queue.Dequeue();
                foreach ((int neighbor, bool sameDirection) in adjacency[tri])
                {
                    bool requiredFlip = flip[tri] ^ sameDirection;
                    if (component[neighbor] < 0)
                    {
                        component[neighbor] = componentCount;
                        flip[neighbor] = requiredFlip;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            componentCount++;
        }

        for (int tri = 0; tri < triangleCount; tri++)
        {
            if (!flip[tri])
            {
                continue;
            }

            int i0 = tri * 3;
            (indices[i0 + 1], indices[i0 + 2]) = (indices[i0 + 2], indices[i0 + 1]);
        }

        var componentCenterAccum = new Vector3[componentCount];
        var componentArea = new double[componentCount];
        var componentVolume = new double[componentCount];
        for (int tri = 0; tri < triangleCount; tri++)
        {
            int i0 = tri * 3;
            Vector3 p0 = positions[(int)indices[i0 + 0]];
            Vector3 p1 = positions[(int)indices[i0 + 1]];
            Vector3 p2 = positions[(int)indices[i0 + 2]];
            Vector3 fn = Vector3.Cross(p1 - p0, p2 - p0);
            float area = fn.Length();
            Vector3 triCenter = (p0 + p1 + p2) / 3f;
            int comp = component[tri];
            componentCenterAccum[comp] += triCenter * area;
            componentArea[comp] += area;
            componentVolume[comp] += Vector3.Dot(p0, Vector3.Cross(p1, p2));
        }

        var componentCenter = new Vector3[componentCount];
        for (int comp = 0; comp < componentCount; comp++)
        {
            float invArea = componentArea[comp] > 1e-8 ? (float)(1.0 / componentArea[comp]) : 0f;
            componentCenter[comp] = componentCenterAccum[comp] * invArea;
        }

        var outwardScore = new double[componentCount];
        for (int tri = 0; tri < triangleCount; tri++)
        {
            int i0 = tri * 3;
            Vector3 p0 = positions[(int)indices[i0 + 0]];
            Vector3 p1 = positions[(int)indices[i0 + 1]];
            Vector3 p2 = positions[(int)indices[i0 + 2]];
            Vector3 fn = Vector3.Cross(p1 - p0, p2 - p0);
            Vector3 triCenter = (p0 + p1 + p2) / 3f;
            int comp = component[tri];
            outwardScore[comp] += Vector3.Dot(fn, triCenter - componentCenter[comp]);
        }

        var flipComponent = new bool[componentCount];
        for (int comp = 0; comp < componentCount; comp++)
        {
            // Primary test: orient triangles so geometric normals point outward from component centroid.
            if (Math.Abs(outwardScore[comp]) > 1e-6)
            {
                // Positive score means already outward; only flip inward-oriented components.
                flipComponent[comp] = outwardScore[comp] < 0.0;
                continue;
            }

            // Fallback for near-symmetric/degenerate open components.
            // Positive signed volume indicates outward orientation in RHS coordinates.
            flipComponent[comp] = componentVolume[comp] < 0.0;
        }

        for (int tri = 0; tri < triangleCount; tri++)
        {
            if (!flipComponent[component[tri]])
            {
                continue;
            }

            int i0 = tri * 3;
            (indices[i0 + 1], indices[i0 + 2]) = (indices[i0 + 2], indices[i0 + 1]);
        }
    }

    private static void AddEdgeUse(IDictionary<EdgeKey, List<EdgeUse>> map, int triangle, uint from, uint to)
    {
        EdgeKey key = from < to ? new EdgeKey(from, to) : new EdgeKey(to, from);
        bool forward = from < to;
        if (!map.TryGetValue(key, out List<EdgeUse>? uses))
        {
            uses = new List<EdgeUse>(2);
            map[key] = uses;
        }

        uses.Add(new EdgeUse(triangle, forward));
    }

    private static Vector3[] ComputeVertexNormals(IReadOnlyList<Vector3> positions, IReadOnlyList<uint> indices)
    {
        var normals = new Vector3[positions.Count];
        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            int i0 = (int)indices[i];
            int i1 = (int)indices[i + 1];
            int i2 = (int)indices[i + 2];

            Vector3 p0 = positions[i0];
            Vector3 p1 = positions[i1];
            Vector3 p2 = positions[i2];
            Vector3 fn = Vector3.Cross(p1 - p0, p2 - p0);
            if (fn.LengthSquared() <= 1e-10f)
            {
                continue;
            }

            normals[i0] += fn;
            normals[i1] += fn;
            normals[i2] += fn;
        }

        for (int i = 0; i < normals.Length; i++)
        {
            Vector3 n = normals[i];
            if (n.LengthSquared() <= 1e-10f)
            {
                Vector2 xy = new(positions[i].X, positions[i].Y);
                n = xy.LengthSquared() > 1e-10f
                    ? new Vector3(xy.X, xy.Y, 0f)
                    : Vector3.UnitZ;
            }

            normals[i] = Vector3.Normalize(n);
        }

        return normals;
    }

    private static uint GetOrAddVertex(Vector3 v, IDictionary<VertexKey, uint> map, IList<Vector3> positions)
    {
        VertexKey key = new(v);
        if (map.TryGetValue(key, out uint existing))
        {
            return existing;
        }

        uint index = (uint)positions.Count;
        positions.Add(v);
        map[key] = index;
        return index;
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

    private readonly record struct EdgeKey(uint A, uint B);

    private readonly record struct EdgeUse(int Triangle, bool Forward);

    private readonly record struct VertexKey(int X, int Y, int Z)
    {
        private const float QuantizationScale = 10000f;

        public VertexKey(Vector3 value)
            : this(
                (int)MathF.Round(value.X * QuantizationScale),
                (int)MathF.Round(value.Y * QuantizationScale),
                (int)MathF.Round(value.Z * QuantizationScale))
        {
        }
    }

    private sealed class DisjointSet
    {
        private readonly int[] _parent;
        private readonly byte[] _rank;

        public DisjointSet(int size)
        {
            _parent = new int[size];
            _rank = new byte[size];
            for (int i = 0; i < size; i++)
            {
                _parent[i] = i;
            }
        }

        public int Find(int value)
        {
            int parent = _parent[value];
            if (parent == value)
            {
                return value;
            }

            int root = Find(parent);
            _parent[value] = root;
            return root;
        }

        public void Union(int a, int b)
        {
            int rootA = Find(a);
            int rootB = Find(b);
            if (rootA == rootB)
            {
                return;
            }

            byte rankA = _rank[rootA];
            byte rankB = _rank[rootB];
            if (rankA < rankB)
            {
                _parent[rootA] = rootB;
                return;
            }

            if (rankA > rankB)
            {
                _parent[rootB] = rootA;
                return;
            }

            _parent[rootB] = rootA;
            _rank[rootA]++;
        }
    }

    private readonly record struct AccessorView(
        int DataOffset,
        int Count,
        int ComponentType,
        string Type,
        int ByteStride,
        int ByteLength);
}
