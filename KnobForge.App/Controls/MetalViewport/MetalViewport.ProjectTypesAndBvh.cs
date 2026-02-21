using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using KnobForge.Core;
using KnobForge.Rendering.GPU;

namespace KnobForge.App.Controls
{
    public sealed partial class MetalViewport
    {
        private readonly record struct PaintStampCommand(
            Vector2 UvCenter,
            float UvRadius,
            float Opacity,
            float Spread,
            PaintChannel Channel,
            PaintBrushType BrushType,
            ScratchAbrasionType ScratchAbrasionType,
            Vector3 PaintColor,
            uint Seed,
            int LayerIndex);

        private readonly record struct PaintStrokeRecord(
            int LayerIndex,
            PaintStampCommand[] Commands);

        private sealed class PaintProjectState
        {
            public List<PaintLayerPersisted>? Layers { get; set; }
            public int ActiveLayerIndex { get; set; }
            public int FocusedLayerIndex { get; set; } = -1;
            public int PaintHistoryRevision { get; set; }
            public List<PaintStrokePersisted>? Strokes { get; set; }
        }

        private sealed class PaintLayerPersisted
        {
            public string? Name { get; set; }
        }

        private sealed class PaintStrokePersisted
        {
            public int LayerIndex { get; set; }
            public List<PaintStampPersisted> Commands { get; set; } = new();
        }

        private sealed class PaintStampPersisted
        {
            public float UvX { get; set; }
            public float UvY { get; set; }
            public float UvRadius { get; set; }
            public float Opacity { get; set; }
            public float Spread { get; set; }
            public PaintChannel Channel { get; set; }
            public PaintBrushType BrushType { get; set; }
            public ScratchAbrasionType ScratchAbrasionType { get; set; }
            public float PaintColorX { get; set; }
            public float PaintColorY { get; set; }
            public float PaintColorZ { get; set; }
            public uint Seed { get; set; }
            public int LayerIndex { get; set; }
        }

        private sealed class ViewportProjectState
        {
            public float OrbitYawDeg { get; set; }
            public float OrbitPitchDeg { get; set; }
            public float Zoom { get; set; }
            public float PanX { get; set; }
            public float PanY { get; set; }
            public OrientationProjectState? Orientation { get; set; }
            public bool GizmoInvertX { get; set; }
            public bool GizmoInvertY { get; set; }
            public bool GizmoInvertZ { get; set; }
            public bool BrushInvertX { get; set; }
            public bool BrushInvertY { get; set; }
            public bool BrushInvertZ { get; set; }
            public bool InvertImportedCollarOrbit { get; set; }
            public bool InvertKnobFrontFaceWinding { get; set; }
            public bool InvertImportedStlFrontFaceWinding { get; set; }
        }

        private sealed class OrientationProjectState
        {
            public bool InvertX { get; set; }
            public bool InvertY { get; set; }
            public bool InvertZ { get; set; }
            public bool FlipCamera180 { get; set; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GpuLight
        {
            public Vector4 PositionType;
            public Vector4 Direction;
            public Vector4 ColorIntensity;
            public Vector4 Params0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GpuUniforms
        {
            public Vector4 CameraPosAndReferenceRadius;
            public Vector4 RightAndScaleX;
            public Vector4 UpAndScaleY;
            public Vector4 ForwardAndScaleZ;
            public Vector4 ProjectionOffsetsAndLightCount;
            public Vector4 MaterialBaseColorAndMetallic;
            public Vector4 MaterialRoughnessDiffuseSpecMode;
            public Vector4 MaterialPartTopColorAndMetallic;
            public Vector4 MaterialPartBevelColorAndMetallic;
            public Vector4 MaterialPartSideColorAndMetallic;
            public Vector4 MaterialPartRoughnessAndEnable;
            public Vector4 MaterialSurfaceBrushParams;
            public Vector4 WeatherParams;
            public Vector4 ScratchExposeColorAndStrength;
            public Vector4 AdvancedMaterialParams;
            public Vector4 IndicatorParams0;
            public Vector4 IndicatorParams1;
            public Vector4 IndicatorColorAndBlend;
            public Vector4 MicroDetailParams;
            public Vector4 EnvironmentTopColorAndIntensity;
            public Vector4 EnvironmentBottomColorAndRoughnessMix;
            public Vector4 ModelRotationCosSin;
            public Vector4 ShadowParams;
            public Vector4 ShadowColorAndOpacity;
            public Vector4 DebugBasisParams;
            public GpuLight Light0;
            public GpuLight Light1;
            public GpuLight Light2;
            public GpuLight Light3;
            public GpuLight Light4;
            public GpuLight Light5;
            public GpuLight Light6;
            public GpuLight Light7;
        }

        private readonly record struct MeshShapeKey(
            float Radius,
            float Height,
            float Bevel,
            float TopScale,
            int Segments,
            float CrownProfile,
            float BevelCurve,
            float BodyTaper,
            float BodyBulge,
            float SpiralHeight,
            float SpiralWidth,
            float SpiralHeightVariance,
            float SpiralWidthVariance,
            float SpiralHeightThreshold,
            float SpiralWidthThreshold,
            float SpiralTurns,
            int GripType,
            float GripStart,
            float GripHeight,
            float GripDensity,
            float GripPitch,
            float GripDepth,
            float GripWidth,
            float GripSharpness,
            int IndicatorEnabled,
            int IndicatorShape,
            int IndicatorRelief,
            int IndicatorProfile,
            float IndicatorWidth,
            float IndicatorLength,
            float IndicatorPosition,
            float IndicatorThickness,
            float IndicatorRoundness,
            int IndicatorCadWallsEnabled);

        private readonly record struct CollarShapeKey(
            int Enabled,
            int Preset,
            float ModelRadius,
            float ModelHeight,
            float InnerRadiusRatio,
            float GapToKnobRatio,
            float ElevationRatio,
            float OverallRotationRadians,
            float BiteAngleRadians,
            float BodyRadiusRatio,
            float BodyEllipseYScale,
            float NeckTaper,
            float TailTaper,
            float MassBias,
            float TailUnderlap,
            float HeadScale,
            float JawBulge,
            int UvSeamFollowBite,
            float UvSeamOffset,
            int PathSegments,
            int CrossSegments,
            float ImportedScale,
            float ImportedBodyLengthScale,
            float ImportedBodyThicknessScale,
            float ImportedHeadLengthScale,
            float ImportedHeadThicknessScale,
            float ImportedRotationRadians,
            float ImportedOffsetXRatio,
            float ImportedOffsetYRatio,
            float ImportedInflateRatio,
            string ImportedMeshPath,
            long ImportedFileTicks);

        private readonly record struct SpiralNormalMapKey(
            float ReferenceRadius,
            float TopScale,
            float SpiralHeight,
            float SpiralWidth,
            float SpiralTurns);

        private readonly record struct ShadowPassConfig(
            bool Enabled,
            float OffsetXClip,
            float OffsetYClip,
            float Scale,
            float Alpha,
            float Gray,
            float SoftRadiusXClip,
            float SoftRadiusYClip,
            int SampleCount);

        private readonly record struct ShadowLightContribution(
            Vector2 ShadowVec,
            float Weight,
            float Planar);

        private sealed class CpuTriangleBvh
        {
            private const int LeafTriangleThreshold = 12;
            private const int MaxTraversalStackDepth = 128;
            private static readonly CpuTriangleBvh Empty = new(Array.Empty<BvhNode>(), Array.Empty<int>());
            private readonly BvhNode[] _nodes;
            private readonly int[] _triangleIndices;

            private CpuTriangleBvh(BvhNode[] nodes, int[] triangleIndices)
            {
                _nodes = nodes;
                _triangleIndices = triangleIndices;
            }

            public static CpuTriangleBvh Build(Vector3[] positions, uint[] indices)
            {
                int triangleCount = indices.Length / 3;
                if (triangleCount <= 0 || positions.Length == 0)
                {
                    return Empty;
                }

                var triangleIndices = new int[triangleCount];
                for (int i = 0; i < triangleCount; i++)
                {
                    triangleIndices[i] = i;
                }

                var nodes = new List<BvhNode>(Math.Max(1, (triangleCount / LeafTriangleThreshold) * 2));
                BuildNode(nodes, triangleIndices, 0, triangleCount, positions, indices);
                return new CpuTriangleBvh(nodes.ToArray(), triangleIndices);
            }

            public bool TryIntersect(
                Vector3 rayOrigin,
                Vector3 rayDirection,
                Vector3[] positions,
                uint[] indices,
                out Vector3 hitPoint,
                out float hitT,
                out bool traversalCompleted)
            {
                hitPoint = default;
                hitT = float.MaxValue;
                traversalCompleted = true;
                if (_nodes.Length == 0)
                {
                    return false;
                }

                Span<int> nodeStack = stackalloc int[MaxTraversalStackDepth];
                int stackSize = 0;
                nodeStack[stackSize++] = 0;

                bool hit = false;
                float bestT = float.MaxValue;
                Vector3 bestPoint = default;

                while (stackSize > 0)
                {
                    int nodeIndex = nodeStack[--stackSize];
                    ref readonly BvhNode node = ref _nodes[nodeIndex];
                    if (!TryIntersectRayAabb(
                            rayOrigin,
                            rayDirection,
                            node.BoundsMin,
                            node.BoundsMax,
                            out float nodeTMin,
                            out _) ||
                        nodeTMin > bestT)
                    {
                        continue;
                    }

                    if (node.IsLeaf)
                    {
                        int leafEnd = node.TriangleStart + node.TriangleCount;
                        for (int triCursor = node.TriangleStart; triCursor < leafEnd; triCursor++)
                        {
                            int triangleIndex = _triangleIndices[triCursor];
                            int baseIndex = triangleIndex * 3;
                            if ((uint)(baseIndex + 2) >= (uint)indices.Length)
                            {
                                continue;
                            }

                            int i0 = (int)indices[baseIndex];
                            int i1 = (int)indices[baseIndex + 1];
                            int i2 = (int)indices[baseIndex + 2];
                            if ((uint)i0 >= positions.Length || (uint)i1 >= positions.Length || (uint)i2 >= positions.Length)
                            {
                                continue;
                            }

                            Vector3 p0 = positions[i0];
                            Vector3 p1 = positions[i1];
                            Vector3 p2 = positions[i2];
                            if (!TryIntersectRayTriangle(rayOrigin, rayDirection, p0, p1, p2, out float t))
                            {
                                continue;
                            }

                            if (t <= 1e-5f || t >= bestT)
                            {
                                continue;
                            }

                            hit = true;
                            bestT = t;
                            bestPoint = rayOrigin + (rayDirection * t);
                        }

                        continue;
                    }

                    int leftChild = node.LeftChildIndex;
                    int rightChild = node.RightChildIndex;
                    bool leftHit = false;
                    bool rightHit = false;
                    float leftTMin = float.MaxValue;
                    float rightTMin = float.MaxValue;

                    if ((uint)leftChild < (uint)_nodes.Length)
                    {
                        ref readonly BvhNode leftNode = ref _nodes[leftChild];
                        leftHit = TryIntersectRayAabb(
                            rayOrigin,
                            rayDirection,
                            leftNode.BoundsMin,
                            leftNode.BoundsMax,
                            out leftTMin,
                            out _) && leftTMin <= bestT;
                    }

                    if ((uint)rightChild < (uint)_nodes.Length)
                    {
                        ref readonly BvhNode rightNode = ref _nodes[rightChild];
                        rightHit = TryIntersectRayAabb(
                            rayOrigin,
                            rayDirection,
                            rightNode.BoundsMin,
                            rightNode.BoundsMax,
                            out rightTMin,
                            out _) && rightTMin <= bestT;
                    }

                    if (leftHit && rightHit)
                    {
                        if (stackSize + 2 > nodeStack.Length)
                        {
                            traversalCompleted = false;
                            return false;
                        }

                        if (leftTMin <= rightTMin)
                        {
                            nodeStack[stackSize++] = rightChild;
                            nodeStack[stackSize++] = leftChild;
                        }
                        else
                        {
                            nodeStack[stackSize++] = leftChild;
                            nodeStack[stackSize++] = rightChild;
                        }
                    }
                    else if (leftHit)
                    {
                        if (stackSize + 1 > nodeStack.Length)
                        {
                            traversalCompleted = false;
                            return false;
                        }

                        nodeStack[stackSize++] = leftChild;
                    }
                    else if (rightHit)
                    {
                        if (stackSize + 1 > nodeStack.Length)
                        {
                            traversalCompleted = false;
                            return false;
                        }

                        nodeStack[stackSize++] = rightChild;
                    }
                }

                if (!hit)
                {
                    return false;
                }

                hitPoint = bestPoint;
                hitT = bestT;
                return true;
            }

            private static int BuildNode(
                List<BvhNode> nodes,
                int[] triangleIndices,
                int start,
                int count,
                Vector3[] positions,
                uint[] indices)
            {
                ComputeBounds(
                    triangleIndices,
                    start,
                    count,
                    positions,
                    indices,
                    out Vector3 boundsMin,
                    out Vector3 boundsMax,
                    out Vector3 centroidMin,
                    out Vector3 centroidMax);

                int nodeIndex = nodes.Count;
                nodes.Add(default);

                if (count <= LeafTriangleThreshold)
                {
                    nodes[nodeIndex] = BvhNode.CreateLeaf(boundsMin, boundsMax, start, count);
                    return nodeIndex;
                }

                Vector3 centroidExtent = centroidMax - centroidMin;
                int splitAxis = 0;
                float splitAxisExtent = centroidExtent.X;
                if (centroidExtent.Y > splitAxisExtent)
                {
                    splitAxis = 1;
                    splitAxisExtent = centroidExtent.Y;
                }

                if (centroidExtent.Z > splitAxisExtent)
                {
                    splitAxis = 2;
                    splitAxisExtent = centroidExtent.Z;
                }

                if (splitAxisExtent <= 1e-6f)
                {
                    nodes[nodeIndex] = BvhNode.CreateLeaf(boundsMin, boundsMax, start, count);
                    return nodeIndex;
                }

                float splitValue = splitAxis switch
                {
                    0 => centroidMin.X + (splitAxisExtent * 0.5f),
                    1 => centroidMin.Y + (splitAxisExtent * 0.5f),
                    _ => centroidMin.Z + (splitAxisExtent * 0.5f)
                };

                int split = PartitionTrianglesByAxis(
                    triangleIndices,
                    start,
                    count,
                    positions,
                    indices,
                    splitAxis,
                    splitValue);

                if (split <= start || split >= start + count)
                {
                    split = start + (count / 2);
                }

                int leftCount = split - start;
                int rightCount = count - leftCount;
                if (leftCount <= 0 || rightCount <= 0)
                {
                    nodes[nodeIndex] = BvhNode.CreateLeaf(boundsMin, boundsMax, start, count);
                    return nodeIndex;
                }

                int leftChild = BuildNode(nodes, triangleIndices, start, leftCount, positions, indices);
                int rightChild = BuildNode(nodes, triangleIndices, split, rightCount, positions, indices);

                nodes[nodeIndex] = BvhNode.CreateInternal(boundsMin, boundsMax, leftChild, rightChild);
                return nodeIndex;
            }

            private static void ComputeBounds(
                int[] triangleIndices,
                int start,
                int count,
                Vector3[] positions,
                uint[] indices,
                out Vector3 boundsMin,
                out Vector3 boundsMax,
                out Vector3 centroidMin,
                out Vector3 centroidMax)
            {
                boundsMin = new Vector3(float.MaxValue);
                boundsMax = new Vector3(float.MinValue);
                centroidMin = new Vector3(float.MaxValue);
                centroidMax = new Vector3(float.MinValue);
                bool any = false;

                int end = start + count;
                for (int i = start; i < end; i++)
                {
                    int triangleIndex = triangleIndices[i];
                    int baseIndex = triangleIndex * 3;
                    if ((uint)(baseIndex + 2) >= (uint)indices.Length)
                    {
                        continue;
                    }

                    int i0 = (int)indices[baseIndex];
                    int i1 = (int)indices[baseIndex + 1];
                    int i2 = (int)indices[baseIndex + 2];
                    if ((uint)i0 >= positions.Length || (uint)i1 >= positions.Length || (uint)i2 >= positions.Length)
                    {
                        continue;
                    }

                    Vector3 p0 = positions[i0];
                    Vector3 p1 = positions[i1];
                    Vector3 p2 = positions[i2];
                    Vector3 triangleMin = Vector3.Min(Vector3.Min(p0, p1), p2);
                    Vector3 triangleMax = Vector3.Max(Vector3.Max(p0, p1), p2);
                    Vector3 centroid = (p0 + p1 + p2) * (1f / 3f);

                    boundsMin = Vector3.Min(boundsMin, triangleMin);
                    boundsMax = Vector3.Max(boundsMax, triangleMax);
                    centroidMin = Vector3.Min(centroidMin, centroid);
                    centroidMax = Vector3.Max(centroidMax, centroid);
                    any = true;
                }

                if (!any)
                {
                    boundsMin = Vector3.Zero;
                    boundsMax = Vector3.Zero;
                    centroidMin = Vector3.Zero;
                    centroidMax = Vector3.Zero;
                }
            }

            private static int PartitionTrianglesByAxis(
                int[] triangleIndices,
                int start,
                int count,
                Vector3[] positions,
                uint[] indices,
                int axis,
                float splitValue)
            {
                int i = start;
                int j = start + count - 1;
                while (i <= j)
                {
                    float centroid = GetTriangleCentroidAxis(triangleIndices[i], positions, indices, axis);
                    if (centroid <= splitValue)
                    {
                        i++;
                        continue;
                    }

                    (triangleIndices[i], triangleIndices[j]) = (triangleIndices[j], triangleIndices[i]);
                    j--;
                }

                return i;
            }

            private static float GetTriangleCentroidAxis(
                int triangleIndex,
                Vector3[] positions,
                uint[] indices,
                int axis)
            {
                int baseIndex = triangleIndex * 3;
                if ((uint)(baseIndex + 2) >= (uint)indices.Length)
                {
                    return 0f;
                }

                int i0 = (int)indices[baseIndex];
                int i1 = (int)indices[baseIndex + 1];
                int i2 = (int)indices[baseIndex + 2];
                if ((uint)i0 >= positions.Length || (uint)i1 >= positions.Length || (uint)i2 >= positions.Length)
                {
                    return 0f;
                }

                Vector3 centroid = (positions[i0] + positions[i1] + positions[i2]) * (1f / 3f);
                return axis switch
                {
                    0 => centroid.X,
                    1 => centroid.Y,
                    _ => centroid.Z
                };
            }

            private readonly struct BvhNode
            {
                private BvhNode(
                    Vector3 boundsMin,
                    Vector3 boundsMax,
                    int leftChildIndex,
                    int rightChildIndex,
                    int triangleStart,
                    int triangleCount)
                {
                    BoundsMin = boundsMin;
                    BoundsMax = boundsMax;
                    LeftChildIndex = leftChildIndex;
                    RightChildIndex = rightChildIndex;
                    TriangleStart = triangleStart;
                    TriangleCount = triangleCount;
                }

                public Vector3 BoundsMin { get; }
                public Vector3 BoundsMax { get; }
                public int LeftChildIndex { get; }
                public int RightChildIndex { get; }
                public int TriangleStart { get; }
                public int TriangleCount { get; }
                public bool IsLeaf => TriangleCount > 0;

                public static BvhNode CreateLeaf(Vector3 boundsMin, Vector3 boundsMax, int triangleStart, int triangleCount)
                {
                    return new BvhNode(boundsMin, boundsMax, -1, -1, triangleStart, triangleCount);
                }

                public static BvhNode CreateInternal(Vector3 boundsMin, Vector3 boundsMax, int leftChildIndex, int rightChildIndex)
                {
                    return new BvhNode(boundsMin, boundsMax, leftChildIndex, rightChildIndex, 0, 0);
                }
            }
        }

        private sealed class MetalMeshGpuResources : IDisposable
        {
            private bool _disposed;

            public required IMTLBuffer VertexBuffer { get; init; }
            public required IMTLBuffer IndexBuffer { get; init; }
            public required int IndexCount { get; init; }
            public required MTLIndexType IndexType { get; init; }
            public required float ReferenceRadius { get; init; }
            public required Vector3[] Positions { get; init; }
            public required uint[] Indices { get; init; }
            public required Vector3 BoundsMin { get; init; }
            public required Vector3 BoundsMax { get; init; }
            public required CpuTriangleBvh Bvh { get; init; }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                VertexBuffer.Dispose();
                IndexBuffer.Dispose();
            }
        }

        private enum InteractionMode
        {
            None,
            PanView,
            OrbitView
        }
    }
}
