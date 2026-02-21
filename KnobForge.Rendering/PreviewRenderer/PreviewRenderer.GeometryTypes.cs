using System;
using System.Numerics;
using KnobForge.Core;
using KnobForge.Core.Scene;
using SkiaSharp;

namespace KnobForge.Rendering
{
    public sealed partial class PreviewRenderer
    {
        private static Vector3 Hadamard(Vector3 a, Vector3 b)
        {
            return new Vector3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        }

        private static Vector3 Clamp01(Vector3 c)
        {
            return new Vector3(
                Math.Clamp(c.X, 0f, 1f),
                Math.Clamp(c.Y, 0f, 1f),
                Math.Clamp(c.Z, 0f, 1f));
        }

        private struct TriangleDraw
        {
            public SKPoint P0;
            public SKPoint P1;
            public SKPoint P2;
            public SKColor C0;
            public SKColor C1;
            public SKColor C2;
            public float Depth;
        }

        private readonly record struct MeshKey(
            float Radius,
            float Height,
            float Bevel,
            float TopScale,
            int Segments,
            float CrownProfile,
            float BevelCurve,
            float BodyTaper,
            float BodyBulge,
            float RidgeHeight,
            float RidgeWidth,
            float RidgeTurns,
            GripType GripType,
            float GripStart,
            float GripHeight,
            float GripDensity,
            float GripPitch,
            float GripDepth,
            float GripWidth,
            float GripSharpness,
            bool IndicatorEnabled,
            IndicatorShape IndicatorShape,
            IndicatorRelief IndicatorRelief,
            IndicatorProfile IndicatorProfile,
            float IndicatorWidthRatio,
            float IndicatorLengthRatio,
            float IndicatorPositionRatio,
            float IndicatorThicknessRatio,
            float IndicatorRoundness,
            bool IndicatorCadWallsEnabled);

        private readonly record struct SpiralNormalMapKey(
            float ReferenceRadius,
            float TopScale,
            float SpiralHeight,
            float SpiralWidth,
            float SpiralTurns);

        private sealed class SpiralNormalMap
        {
            public int Size { get; init; }
            public Vector3[] Normals { get; init; } = Array.Empty<Vector3>();
        }

        private sealed class MeshCache
        {
            public MeshKey Key { get; init; }
            public Vector3[] Positions { get; init; } = Array.Empty<Vector3>();
            public Vector3[] Normals { get; init; } = Array.Empty<Vector3>();
            public int[] Indices { get; init; } = Array.Empty<int>();
            public float FrontZ { get; init; }
            public float ReferenceRadius { get; init; }
        }
    }
}
