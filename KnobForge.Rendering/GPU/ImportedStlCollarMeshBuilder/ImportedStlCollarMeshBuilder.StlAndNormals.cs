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
}
