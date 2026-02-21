using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using KnobForge.Core;
using KnobForge.Core.Scene;
using SkiaSharp;


namespace KnobForge.Rendering
{
    public sealed partial class PreviewRenderer
    {
        private MeshCache GetOrBuildMesh(ModelNode modelNode)
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

            MeshKey key = new(
                radius,
                height,
                bevel,
                topScale,
                radialSegments,
                crownProfile,
                bevelCurve,
                bodyTaper,
                bodyBulge,
                spiralHeight,
                spiralWidth,
                spiralTurns,
                gripType,
                gripStart,
                gripHeight,
                gripDensity,
                gripPitch,
                gripDepth,
                gripWidth,
                gripSharpness,
                indicatorEnabled,
                indicatorShape,
                indicatorRelief,
                indicatorProfile,
                indicatorWidthRatio,
                indicatorLengthRatio,
                indicatorPositionRatio,
                indicatorThicknessRatio,
                indicatorRoundness,
                indicatorCadWallsEnabled);
            if (_meshCache != null && _meshCache.Key.Equals(key))
            {
                return _meshCache;
            }

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
            var indices = new List<int>(radialSegments * (ringCount - 1) * 6 + radialSegments * frontCapRings * 6 + radialSegments * 6);

            // Side vertices
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

            // Side triangles
            for (int ring = 0; ring < ringCount - 1; ring++)
            {
                int a0 = ring * radialSegments;
                int b0 = (ring + 1) * radialSegments;
                for (int s = 0; s < radialSegments; s++)
                {
                    int sn = (s + 1) % radialSegments;
                    int i00 = a0 + s;
                    int i01 = a0 + sn;
                    int i10 = b0 + s;
                    int i11 = b0 + sn;

                    indices.Add(i00);
                    indices.Add(i01);
                    indices.Add(i10);

                    indices.Add(i01);
                    indices.Add(i11);
                    indices.Add(i10);
                }
            }

            // Front cap (+Z)
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
                indices.Add(frontCenter);
                indices.Add(frontRingStart + s);
                indices.Add(frontRingStart + sn);
            }

            for (int ring = 1; ring < frontCapRings; ring++)
            {
                int innerStart = frontRingStart + ((ring - 1) * radialSegments);
                int outerStart = frontRingStart + (ring * radialSegments);
                for (int s = 0; s < radialSegments; s++)
                {
                    int sn = (s + 1) % radialSegments;
                    int i0 = innerStart + s;
                    int i1 = outerStart + s;
                    int i2 = outerStart + sn;
                    int i3 = innerStart + sn;

                    indices.Add(i0);
                    indices.Add(i1);
                    indices.Add(i2);

                    indices.Add(i0);
                    indices.Add(i2);
                    indices.Add(i3);
                }
            }

            // Back cap (-Z)
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
                indices.Add(backCenter);
                indices.Add(backRingStart + sn);
                indices.Add(backRingStart + s);
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

            _meshCache = new MeshCache
            {
                Key = key,
                Positions = positionList.ToArray(),
                Normals = normalList.ToArray(),
                Indices = indices.ToArray(),
                FrontZ = zFront,
                ReferenceRadius = radius
            };

            return _meshCache;
        }
    }
}
