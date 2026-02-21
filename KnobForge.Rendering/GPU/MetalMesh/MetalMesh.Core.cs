using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using KnobForge.Core;
using KnobForge.Core.Scene;

namespace KnobForge.Rendering.GPU;

[StructLayout(LayoutKind.Sequential)]
public readonly struct MetalVertex
{
    public Vector3 Position { get; init; }
    public Vector3 Normal { get; init; }
    public Vector4 Tangent { get; init; }
}

public sealed class MetalMesh
{
    public MetalVertex[] Vertices { get; init; } = Array.Empty<MetalVertex>();

    public uint[] Indices { get; init; } = Array.Empty<uint>();

    public float ReferenceRadius { get; init; }
}

public static partial class MetalMeshBuilder
{
    public static MetalMesh? TryBuildFromProject(KnobProject? project)
    {
        if (project is null)
        {
            return null;
        }

        ModelNode? modelNode = project.SceneRoot.Children
            .OfType<ModelNode>()
            .FirstOrDefault();

        return modelNode is null
            ? null
            : BuildFromModel(modelNode);
    }

    public static MetalMesh BuildFromModel(ModelNode modelNode)
    {
        int radialSegments = Math.Clamp(modelNode.RadialSegments, 12, 180);
        float radius = MathF.Max(20f, modelNode.Radius);
        float height = MathF.Max(20f, modelNode.Height);
        float bevel = Math.Clamp(modelNode.Bevel, 0f, MathF.Min(radius * 0.45f, height * 0.45f));
        float topScale = Math.Clamp(modelNode.TopRadiusScale, 0.30f, 1.30f);
        float crownProfile = modelNode.CrownProfile;
        float bevelCurve = modelNode.BevelCurve;
        float bodyTaper = modelNode.BodyTaper;
        float bodyBulge = modelNode.BodyBulge;
        float spiralHeight = modelNode.SpiralRidgeHeight;
        float spiralWidth = modelNode.SpiralRidgeWidth;
        float spiralTurns = modelNode.SpiralTurns;
        GripType gripType = modelNode.GripType;
        float gripStart = modelNode.GripStart;
        float gripHeight = modelNode.GripHeight;
        float gripDensity = modelNode.GripDensity;
        float gripPitch = modelNode.GripPitch;
        float gripDepth = modelNode.GripDepth;
        float gripWidth = modelNode.GripWidth;
        float gripSharpness = modelNode.GripSharpness;
        bool indicatorEnabled = modelNode.IndicatorEnabled;
        IndicatorShape indicatorShape = modelNode.IndicatorShape;
        IndicatorRelief indicatorRelief = modelNode.IndicatorRelief;
        IndicatorProfile indicatorProfile = modelNode.IndicatorProfile;
        float indicatorWidthRatio = modelNode.IndicatorWidthRatio;
        float indicatorLengthRatio = modelNode.IndicatorLengthRatioTop;
        float indicatorPositionRatio = modelNode.IndicatorPositionRatio;
        float indicatorThicknessRatio = modelNode.IndicatorThicknessRatio;
        float indicatorRoundness = modelNode.IndicatorRoundness;
        bool indicatorCadWallsEnabled = modelNode.IndicatorCadWallsEnabled;
        float lockedGripDensity = QuantizeDensity(gripDensity, radialSegments);

        float zBack = -height * 0.5f;
        float zFront = height * 0.5f;
        float topRadius = radius * topScale;
        float sideTopRadius = MathF.Max(topRadius, radius * (1f - bodyTaper));

        Vector2 backInner = new(radius * 0.97f, zBack);
        Vector2 sideStart = new(radius, zBack + bevel * 0.35f);
        Vector2 sideEnd = new(sideTopRadius, zFront - bevel);
        Vector2 top = new(topRadius, zFront);
        int sideDetailSegments = Math.Clamp((int)MathF.Round(radialSegments * 0.75f), 24, 160);
        int chamferSegments = 6;
        var profile = new List<Vector2>(sideDetailSegments + chamferSegments + 3)
        {
            backInner,
            sideStart
        };
        for (int i = 1; i < sideDetailSegments; i++)
        {
            float t = (float)i / sideDetailSegments;
            float r = ComputeBodyRadius(t, sideStart.X, sideEnd.X, bodyBulge);
            float z = sideStart.Y + ((sideEnd.Y - sideStart.Y) * t);
            profile.Add(new Vector2(r, z));
        }
        profile.Add(sideEnd);
        for (int i = 1; i <= chamferSegments; i++)
        {
            float t = (float)i / chamferSegments;
            float shaped = MathF.Pow(t, MathF.Max(0.4f, bevelCurve));
            float r = sideEnd.X + ((top.X - sideEnd.X) * shaped);
            float z = sideEnd.Y + ((top.Y - sideEnd.Y) * t);
            profile.Add(new Vector2(r, z));
        }
        profile.Add(top);

        int ringCount = profile.Count;
        int sideVertexCount = ringCount * radialSegments;
        int frontCapRings = Math.Clamp(radialSegments * 3, 36, 360);
        int frontCapVertexCount = (frontCapRings * radialSegments) + 1;
        int capRingCount = radialSegments;
        int totalVertices = sideVertexCount + frontCapVertexCount + capRingCount + 1;

        var positions = new Vector3[totalVertices];
        var normals = new Vector3[totalVertices];
        var indices = new List<uint>(radialSegments * (ringCount - 1) * 6 + radialSegments * frontCapRings * 6 + radialSegments * 6);

        for (int i = 0; i < ringCount; i++)
        {
            float baseRadius = profile[i].X;
            float z = profile[i].Y;
            int ringStart = i * radialSegments;
            for (int s = 0; s < radialSegments; s++)
            {
                float t = (float)s / radialSegments;
                float angle = t * MathF.PI * 2f;
                float c = MathF.Cos(angle);
                float si = MathF.Sin(angle);
                float gripOffset = ComputeGripOffset(
                    gripType,
                    angle,
                    z,
                    sideStart.Y,
                    sideEnd.Y,
                    gripStart,
                    gripHeight,
                    lockedGripDensity,
                    gripPitch,
                    gripDepth,
                    gripWidth,
                    gripSharpness);
                float r = baseRadius + gripOffset;

                int vi = ringStart + s;
                positions[vi] = new Vector3(r * c, r * si, z);
                normals[vi] = Vector3.UnitX;
            }
        }
        RecomputeSideNormals(positions, normals, ringCount, radialSegments);

        for (int ring = 0; ring < ringCount - 1; ring++)
        {
            int a0 = ring * radialSegments;
            int b0 = (ring + 1) * radialSegments;
            for (int s = 0; s < radialSegments; s++)
            {
                int sn = (s + 1) % radialSegments;
                uint i00 = (uint)(a0 + s);
                uint i01 = (uint)(a0 + sn);
                uint i10 = (uint)(b0 + s);
                uint i11 = (uint)(b0 + sn);

                indices.Add(i00);
                indices.Add(i01);
                indices.Add(i10);

                indices.Add(i01);
                indices.Add(i11);
                indices.Add(i10);
            }
        }

        int frontRingStart = sideVertexCount;
        int frontCenter = frontRingStart + (frontCapRings * radialSegments);
        for (int ring = 1; ring <= frontCapRings; ring++)
        {
            float rNorm = (float)ring / frontCapRings;
            float ringRadius = topRadius * rNorm;
            int ringStart = frontRingStart + ((ring - 1) * radialSegments);
            for (int s = 0; s < radialSegments; s++)
            {
                float t = (float)s / radialSegments;
                float angle = t * MathF.PI * 2f;
                float c = MathF.Cos(angle);
                float si = MathF.Sin(angle);
                float x = ringRadius * c;
                    float y = ringRadius * si;
                    float z = zFront + ComputeCrownOffset(rNorm, crownProfile, radius, height) + ComputeSpiralRidgeOffset(
                        x,
                        y,
                        ringRadius,
                        topRadius,
                        spiralHeight,
                        spiralWidth,
                        spiralTurns) + ComputeIndicatorOffset(
                        x,
                        y,
                        topRadius,
                        indicatorEnabled,
                        indicatorShape,
                        indicatorRelief,
                        indicatorProfile,
                        indicatorWidthRatio,
                        indicatorLengthRatio,
                        indicatorPositionRatio,
                        indicatorThicknessRatio,
                        indicatorRoundness);

                positions[ringStart + s] = new Vector3(x, y, z);
                normals[ringStart + s] = Vector3.UnitZ;
            }
        }

        positions[frontCenter] = new Vector3(0f, 0f, zFront + ComputeCrownOffset(0f, crownProfile, radius, height) + ComputeIndicatorOffset(
            0f,
            0f,
            topRadius,
            indicatorEnabled,
            indicatorShape,
            indicatorRelief,
            indicatorProfile,
            indicatorWidthRatio,
            indicatorLengthRatio,
            indicatorPositionRatio,
            indicatorThicknessRatio,
            indicatorRoundness));
        normals[frontCenter] = Vector3.UnitZ;

        for (int ring = 1; ring <= frontCapRings; ring++)
        {
            int ringStart = frontRingStart + ((ring - 1) * radialSegments);
            for (int s = 0; s < radialSegments; s++)
            {
                int sn = (s + 1) % radialSegments;
                int prevTan = (s + radialSegments - 1) % radialSegments;
                int nextTan = sn;

                int prevRadIdx = ring > 1 ? (ringStart - radialSegments + s) : frontCenter;
                int nextRadIdx = ring < frontCapRings ? (ringStart + radialSegments + s) : (ringStart + s);
                int prevTanIdx = ringStart + prevTan;
                int nextTanIdx = ringStart + nextTan;

                Vector3 dRad = positions[nextRadIdx] - positions[prevRadIdx];
                Vector3 dTan = positions[nextTanIdx] - positions[prevTanIdx];
                Vector3 n = Vector3.Cross(dRad, dTan);
                if (n.LengthSquared() < 1e-8f)
                {
                    n = Vector3.UnitZ;
                }
                else
                {
                    n = Vector3.Normalize(n);
                    if (n.Z < 0f)
                    {
                        n = -n;
                    }
                }

                normals[ringStart + s] = n;
            }
        }

        for (int s = 0; s < radialSegments; s++)
        {
            int sn = (s + 1) % radialSegments;
            indices.Add((uint)frontCenter);
            indices.Add((uint)(frontRingStart + s));
            indices.Add((uint)(frontRingStart + sn));
        }

        for (int ring = 1; ring < frontCapRings; ring++)
        {
            int innerStart = frontRingStart + ((ring - 1) * radialSegments);
            int outerStart = frontRingStart + (ring * radialSegments);
            for (int s = 0; s < radialSegments; s++)
            {
                int sn = (s + 1) % radialSegments;
                uint i0 = (uint)(innerStart + s);
                uint i1 = (uint)(outerStart + s);
                uint i2 = (uint)(outerStart + sn);
                uint i3 = (uint)(innerStart + sn);

                indices.Add(i0);
                indices.Add(i1);
                indices.Add(i2);

                indices.Add(i0);
                indices.Add(i2);
                indices.Add(i3);
            }
        }

        int backRingStart = frontCenter + 1;
        int backCenter = backRingStart + capRingCount;
        float backRadius = profile[0].X;
        for (int s = 0; s < radialSegments; s++)
        {
            float t = (float)s / radialSegments;
            float angle = t * MathF.PI * 2f;
            float c = MathF.Cos(angle);
            float si = MathF.Sin(angle);
            positions[backRingStart + s] = new Vector3(backRadius * c, backRadius * si, zBack);
            normals[backRingStart + s] = -Vector3.UnitZ;
        }

        positions[backCenter] = new Vector3(0f, 0f, zBack);
        normals[backCenter] = -Vector3.UnitZ;
        for (int s = 0; s < radialSegments; s++)
        {
            int sn = (s + 1) % radialSegments;
            indices.Add((uint)backCenter);
            indices.Add((uint)(backRingStart + sn));
            indices.Add((uint)(backRingStart + s));
        }

        var positionList = new List<Vector3>(positions);
        var normalList = new List<Vector3>(normals);
        if (indicatorEnabled &&
            indicatorCadWallsEnabled &&
            indicatorProfile == IndicatorProfile.Straight &&
            indicatorThicknessRatio > 1e-6f)
        {
            AppendIndicatorHardWalls(
                positionList,
                normalList,
                indices,
                topRadius,
                zFront,
                crownProfile,
                radius,
                height,
                spiralHeight,
                spiralWidth,
                spiralTurns,
                indicatorShape,
                indicatorRelief,
                indicatorWidthRatio,
                indicatorLengthRatio,
                indicatorPositionRatio,
                indicatorThicknessRatio);
        }

        var vertices = new MetalVertex[positionList.Count];
        for (int i = 0; i < positionList.Count; i++)
        {
            Vector3 tangent = ComputeDefaultTangent(positionList[i], normalList[i]);
            vertices[i] = new MetalVertex
            {
                Position = positionList[i],
                Normal = normalList[i],
                Tangent = new Vector4(tangent, 1f)
            };
        }

        return new MetalMesh
        {
            Vertices = vertices,
            Indices = indices.ToArray(),
            ReferenceRadius = radius
        };
    }

    private static Vector3 ComputeDefaultTangent(Vector3 position, Vector3 normal)
    {
        Vector3 radial = new(-position.Y, position.X, 0f);
        if (radial.LengthSquared() <= 1e-8f)
        {
            radial = Vector3.UnitX;
        }

        Vector3 tangent = radial - (normal * Vector3.Dot(radial, normal));
        if (tangent.LengthSquared() <= 1e-8f)
        {
            tangent = Vector3.Cross(Vector3.UnitZ, normal);
        }

        if (tangent.LengthSquared() <= 1e-8f)
        {
            tangent = Vector3.UnitX;
        }

        return Vector3.Normalize(tangent);
    }

    private static void RecomputeSideNormals(Vector3[] positions, Vector3[] normals, int ringCount, int radialSegments)
    {
        for (int ring = 0; ring < ringCount; ring++)
        {
            int prevRing = Math.Max(0, ring - 1);
            int nextRing = Math.Min(ringCount - 1, ring + 1);
            int ringStart = ring * radialSegments;
            int prevRingStart = prevRing * radialSegments;
            int nextRingStart = nextRing * radialSegments;
            for (int s = 0; s < radialSegments; s++)
            {
                int prevSeg = (s + radialSegments - 1) % radialSegments;
                int nextSeg = (s + 1) % radialSegments;
                Vector3 dTan = positions[ringStart + nextSeg] - positions[ringStart + prevSeg];
                Vector3 dRing = positions[nextRingStart + s] - positions[prevRingStart + s];
                Vector3 n = Vector3.Cross(dTan, dRing);
                if (n.LengthSquared() < 1e-8f)
                {
                    Vector3 p = positions[ringStart + s];
                    Vector2 xy = new(p.X, p.Y);
                    n = xy.LengthSquared() > 1e-8f
                        ? Vector3.Normalize(new Vector3(xy.X, xy.Y, 0f))
                        : Vector3.UnitX;
                }
                else
                {
                    n = Vector3.Normalize(n);
                }

                Vector3 pos = positions[ringStart + s];
                Vector3 outward = new(pos.X, pos.Y, 0f);
                if (outward.LengthSquared() > 1e-8f && Vector3.Dot(n, outward) < 0f)
                {
                    n = -n;
                }

                normals[ringStart + s] = n;
            }
        }
    }

    private static float QuantizeDensity(float gripDensity, int radialSegments)
    {
        int segments = Math.Max(1, radialSegments);
        int target = Math.Clamp((int)MathF.Round(gripDensity), 1, segments);
        int best = 1;
        int bestDelta = int.MaxValue;
        for (int d = 1; d <= segments; d++)
        {
            if (segments % d != 0)
            {
                continue;
            }

            int delta = Math.Abs(d - target);
            if (delta < bestDelta)
            {
                best = d;
                bestDelta = delta;
            }
        }

        return MathF.Max(1f, best);
    }
}
