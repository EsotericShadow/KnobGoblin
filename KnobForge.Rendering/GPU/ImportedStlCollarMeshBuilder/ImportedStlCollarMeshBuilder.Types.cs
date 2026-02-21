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
