using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using KnobForge.Core;
using KnobForge.Core.Scene;
using SkiaSharp;

namespace KnobForge.Rendering
{
    public sealed class OrientationDebug
    {
        public bool InvertX { get; set; }
        public bool InvertY { get; set; }
        public bool InvertZ { get; set; }
        public bool FlipCamera180 { get; set; }
    }

    public sealed class PreviewRenderer
    {
        private const bool ForceSecondLightNoAttenuation = false;
        private const int SpiralNormalMapSize = 1024;

        private readonly KnobProject _project;
        private readonly SKPaint _triPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _gizmoLinePaint = new() { IsAntialias = true, IsStroke = true, StrokeWidth = 1f };
        private readonly SKPaint _gizmoFillPaint = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
        private readonly SKPaint _gizmoRingPaint = new()
        {
            IsAntialias = true,
            IsStroke = true,
            StrokeWidth = 2f,
            Color = new SKColor(255, 230, 90)
        };
        private readonly SKPaint _directionPaint = new()
        {
            IsAntialias = true,
            IsStroke = true,
            StrokeWidth = 2f
        };
        private readonly SKPaint _gizmoTextPaint = new()
        {
            IsAntialias = true,
            Color = new SKColor(230, 236, 245, 220)
        };
        private readonly SKFont _gizmoFont = new() { Size = 11f };

        private readonly List<TriangleDraw> _drawBuffer = new(8192);
        private readonly OrientationDebug _orientation = new()
        {
            InvertX = true,
            InvertY = true,
            InvertZ = true,
            FlipCamera180 = true
        };
        private MeshCache? _meshCache;
        private SpiralNormalMap? _spiralNormalMap;
        private SpiralNormalMapKey _spiralNormalMapKey;
        private float _currentShadeZoom = 1f;

        public PreviewRenderer(KnobProject project)
        {
            _project = project;
        }

        public OrientationDebug Orientation => _orientation;

        public void ResetOrientationDebug()
        {
            _orientation.InvertX = true;
            _orientation.InvertY = true;
            _orientation.InvertZ = true;
            _orientation.FlipCamera180 = true;
        }

        public void Render(SKCanvas canvas, float width, float height, float zoom, SKPoint panPx, float orbitYawDeg, float orbitPitchDeg)
        {
            canvas.Clear(new SKColor(30, 30, 30));
            int renderWidth = Math.Max(1, (int)MathF.Round(width));
            int renderHeight = Math.Max(1, (int)MathF.Round(height));
            if (renderWidth <= 1 || renderHeight <= 1)
            {
                return;
            }

            GetCameraBasis(orbitYawDeg, orbitPitchDeg, out Vector3 right, out Vector3 up, out Vector3 forward);
            float referenceRadius = GetMaxModelReferenceRadius();
            float camDist = referenceRadius * 6f;
            Vector3 cameraPos = -forward * camDist;
            var camera = new Camera(cameraPos, forward, right, up, zoom, panPx);

            RenderWithCamera(canvas, renderWidth, renderHeight, 0f, camera);

            float centerX = renderWidth * 0.5f + panPx.X;
            float centerY = renderHeight * 0.5f + panPx.Y;
            DrawLightGizmos(canvas, right, up, centerX, centerY, zoom, cameraPos, forward, referenceRadius);
        }

        public void RenderWithCamera(
            SKCanvas canvas,
            int width,
            int height,
            float modelRotationRadians,
            Camera camera)
        {
            if (width <= 1 || height <= 1)
            {
                return;
            }

            float centerX = width * 0.5f + camera.PanPx.X;
            float centerY = height * 0.5f + camera.PanPx.Y;
            Vector3 right = camera.Right;
            Vector3 up = camera.Up;
            Vector3 forward = camera.Forward;
            float zoom = camera.Zoom;
            _currentShadeZoom = MathF.Max(1e-3f, zoom);

            var modelNodes = _project.SceneRoot.Children
                .OfType<ModelNode>()
                .ToList();
            if (modelNodes.Count == 0)
            {
                return;
            }

            var models = new List<(ModelNode Node, MeshCache Mesh)>(modelNodes.Count);
            foreach (var modelNode in modelNodes)
            {
                MeshCache mesh = GetOrBuildMesh(modelNode);
                models.Add((modelNode, mesh));
            }

            Vector3 cullViewDir = -forward;
            Vector3 cameraPos = camera.Position;

            _drawBuffer.Clear();
            foreach (var entry in models)
            {
                var modelNode = entry.Node;
                var materialNode = modelNode.Children
                    .OfType<MaterialNode>()
                    .FirstOrDefault();
                var mesh = entry.Mesh;
                float knobAngle = modelNode.RotationRadians + modelRotationRadians;
                float cosA = MathF.Cos(knobAngle);
                float sinA = MathF.Sin(knobAngle);
                float topScale = Math.Clamp(modelNode.TopRadiusScale, 0.30f, 1.30f);
                float topRadius = MathF.Max(1f, mesh.ReferenceRadius * topScale);
                float turns = MathF.Max(1f, modelNode.SpiralTurns);
                float spacingPx = (topRadius / turns) * _currentShadeZoom;
                float geometryKeep = SmoothStep(0.20f, 0.90f, spacingPx);
                SpiralNormalMap? spiralNormalMap = GetOrBuildSpiralNormalMap(
                    mesh.ReferenceRadius,
                    topScale,
                    modelNode.SpiralRidgeHeight,
                    modelNode.SpiralRidgeWidth,
                    modelNode.SpiralTurns);

                for (int i = 0; i < mesh.Indices.Length; i += 3)
                {
                    int i0 = mesh.Indices[i];
                    int i1 = mesh.Indices[i + 1];
                    int i2 = mesh.Indices[i + 2];

                    Vector3 v0 = RotateAroundZ(mesh.Positions[i0], cosA, sinA);
                    Vector3 v1 = RotateAroundZ(mesh.Positions[i1], cosA, sinA);
                    Vector3 v2 = RotateAroundZ(mesh.Positions[i2], cosA, sinA);

                    // Backface culling uses face normal
                    Vector3 faceNormal = Vector3.Normalize(Vector3.Cross(v2 - v0, v1 - v0));
                    float facing = Vector3.Dot(faceNormal, cullViewDir);
                    if (facing <= 0f)
                    {
                        continue;
                    }

                    // Rotate vertex normals for per-vertex shading
                    Vector3 n0 = RotateAroundZ(mesh.Normals[i0], cosA, sinA);
                    Vector3 n1 = RotateAroundZ(mesh.Normals[i1], cosA, sinA);
                    Vector3 n2 = RotateAroundZ(mesh.Normals[i2], cosA, sinA);

                    float indicatorProtect0 = ComputeIndicatorFlattenProtect(v0, topRadius, modelNode, cosA, sinA);
                    float indicatorProtect1 = ComputeIndicatorFlattenProtect(v1, topRadius, modelNode, cosA, sinA);
                    float indicatorProtect2 = ComputeIndicatorFlattenProtect(v2, topRadius, modelNode, cosA, sinA);

                    ApplyGeometryLodFlatten(ref v0, ref n0, mesh.FrontZ, geometryKeep, indicatorProtect0);
                    ApplyGeometryLodFlatten(ref v1, ref n1, mesh.FrontZ, geometryKeep, indicatorProtect1);
                    ApplyGeometryLodFlatten(ref v2, ref n2, mesh.FrontZ, geometryKeep, indicatorProtect2);

                    SKPoint p0 = Project(v0, right, up, centerX, centerY, zoom);
                    SKPoint p1 = Project(v1, right, up, centerX, centerY, zoom);
                    SKPoint p2 = Project(v2, right, up, centerX, centerY, zoom);

                    float area2 = (p1.X - p0.X) * (p2.Y - p0.Y) - (p1.Y - p0.Y) * (p2.X - p0.X);
                    if (MathF.Abs(area2) < 0.01f)
                    {
                        continue;
                    }

                    Vector3 vd0 = Vector3.Normalize(cameraPos - v0);
                    Vector3 vd1 = Vector3.Normalize(cameraPos - v1);
                    Vector3 vd2 = Vector3.Normalize(cameraPos - v2);

                    SKColor c0 = ShadeFace(
                        v0,
                        n0,
                        vd0,
                        mesh.ReferenceRadius,
                        topRadius,
                        modelNode,
                        spiralNormalMap,
                        materialNode,
                        cosA,
                        sinA);
                    SKColor c1 = ShadeFace(
                        v1,
                        n1,
                        vd1,
                        mesh.ReferenceRadius,
                        topRadius,
                        modelNode,
                        spiralNormalMap,
                        materialNode,
                        cosA,
                        sinA);
                    SKColor c2 = ShadeFace(
                        v2,
                        n2,
                        vd2,
                        mesh.ReferenceRadius,
                        topRadius,
                        modelNode,
                        spiralNormalMap,
                        materialNode,
                        cosA,
                        sinA);
                    float depth = (Vector3.Dot(v0, forward) + Vector3.Dot(v1, forward) + Vector3.Dot(v2, forward)) / 3f;

                    _drawBuffer.Add(new TriangleDraw
                    {
                        P0 = p0,
                        P1 = p1,
                        P2 = p2,
                        C0 = c0,
                        C1 = c1,
                        C2 = c2,
                        Depth = depth,
                    });
                }
            }

            _drawBuffer.Sort(static (a, b) => b.Depth.CompareTo(a.Depth));

            foreach (var tri in _drawBuffer)
            {
                using var verts = SKVertices.CreateCopy(
                    SKVertexMode.Triangles,
                    new[] { tri.P0, tri.P1, tri.P2 },
                    null,
                    new[] { tri.C0, tri.C1, tri.C2 },
                    null);
                canvas.DrawVertices(verts, SKBlendMode.Dst, _triPaint);
            }

        }

        public float GetMaxModelReferenceRadius()
        {
            float maxReferenceRadius = 1f;
            foreach (var modelNode in _project.SceneRoot.Children.OfType<ModelNode>())
            {
                MeshCache mesh = GetOrBuildMesh(modelNode);
                float candidate = mesh.ReferenceRadius;
                if (candidate > maxReferenceRadius)
                {
                    maxReferenceRadius = candidate;
                }
            }

            return maxReferenceRadius;
        }

        private void DrawLightGizmos(
            SKCanvas canvas,
            Vector3 right,
            Vector3 up,
            float centerX,
            float centerY,
            float zoom,
            Vector3 cameraPos,
            Vector3 forward,
            float referenceRadius)
        {
            float depthRange = MathF.Max(1f, referenceRadius * 2f);
            Vector3 viewOrigin = -cameraPos;
            float referenceDepth = Vector3.Dot(viewOrigin, forward);

            for (int i = 0; i < _project.Lights.Count; i++)
            {
                var light = _project.Lights[i];
                Vector3 lightPos = ApplyGizmoOrientation(new Vector3(light.X, light.Y, light.Z));
                Vector3 viewLight = lightPos - cameraPos;
                SKPoint g = new(
                    centerX + Vector3.Dot(viewLight, right) * zoom,
                    centerY - Vector3.Dot(viewLight, up) * zoom);
                SKPoint origin = new(
                    centerX + Vector3.Dot(viewOrigin, right) * zoom,
                    centerY - Vector3.Dot(viewOrigin, up) * zoom);
                bool isSelected = i == _project.SelectedLightIndex;

                float depth = Vector3.Dot(viewLight, forward);
                float depthOffset = (depth - referenceDepth) / depthRange;
                float nearFactor = (1f - Math.Clamp(depthOffset, -1f, 1f)) * 0.5f;
                float gizmoRadius = 4f + (nearFactor * 5f);
                byte gizmoAlpha = (byte)(110 + (nearFactor * 145f));
                byte lineAlpha = (byte)(70 + (nearFactor * 120f));
                byte textAlpha = (byte)(120 + (nearFactor * 120f));

                _gizmoLinePaint.Color = WithAlpha(light.Color, lineAlpha);
                _gizmoFillPaint.Color = WithAlpha(light.Color, gizmoAlpha);
                _directionPaint.Color = WithAlpha(light.Color, gizmoAlpha);
                _gizmoTextPaint.Color = new SKColor(230, 236, 245, textAlpha);

                canvas.DrawLine(g, origin, _gizmoLinePaint);
                canvas.DrawCircle(g, gizmoRadius, _gizmoFillPaint);

                string zLabel = lightPos.Z >= 0f ? "Z+" : "Z-";
                canvas.DrawText(
                    zLabel,
                    g.X + gizmoRadius + 4f,
                    g.Y - gizmoRadius - 2f,
                    SKTextAlign.Left,
                    _gizmoFont,
                    _gizmoTextPaint);

                if (light.Type == LightType.Directional)
                {
                    Vector3 lightDir = GetDirectionalVector(light);
                    lightDir = ApplyGizmoOrientation(lightDir);
                    SKPoint d2 = new(
                        g.X + (lightDir.X * right.X + lightDir.Y * right.Y + lightDir.Z * right.Z) * 20f,
                        g.Y - (lightDir.X * up.X + lightDir.Y * up.Y + lightDir.Z * up.Z) * 20f);
                    canvas.DrawLine(g, d2, _directionPaint);
                    canvas.DrawCircle(d2, 2.5f, _directionPaint);
                }

                if (isSelected)
                {
                    canvas.DrawCircle(g, 10f, _gizmoRingPaint);
                }
            }
        }

        private SKColor ShadeFace(
            Vector3 centroid,
            Vector3 normal,
            Vector3 viewDir,
            float referenceRadius,
            float topRadius,
            ModelNode modelNode,
            SpiralNormalMap? spiralNormalMap,
            MaterialNode? materialNode,
            float modelCos,
            float modelSin)
        {
            _project.EnsureSelection();

            Vector3 baseColor = materialNode?.BaseColor ?? new Vector3(0.55f, 0.16f, 0.16f);
            float metallic = Math.Clamp(materialNode?.Metallic ?? 0f, 0f, 1f);
            float roughness = Math.Clamp(materialNode?.Roughness ?? 0.5f, 0.04f, 1f);
            float pearlescence = Math.Clamp(materialNode?.Pearlescence ?? 0f, 0f, 1f);
            float rustAmount = Math.Clamp(materialNode?.RustAmount ?? 0f, 0f, 1f);
            float wearAmount = Math.Clamp(materialNode?.WearAmount ?? 0f, 0f, 1f);
            float gunkAmount = Math.Clamp(materialNode?.GunkAmount ?? 0f, 0f, 1f);
            float shininess = 4f + ((128f - 4f) * (1f - roughness));
            float brushStrength = Math.Clamp(materialNode?.RadialBrushStrength ?? 0f, 0f, 1f);
            float brushDensity = MathF.Max(1f, materialNode?.RadialBrushDensity ?? 56f);
            float brushDensityFactor = Math.Clamp((brushDensity - 4f) / 316f, 0f, 1f);
            float surfaceCharacter = Math.Clamp(materialNode?.SurfaceCharacter ?? 0f, 0f, 1f);
            Vector3 ambientColor = new Vector3(0.03f, 0.03f, 0.03f);
            float diffuseStrength = 1.0f;
            float specularStrength = 1.0f;

            if (materialNode != null)
            {
                diffuseStrength = materialNode.DiffuseStrength;
                specularStrength = materialNode.SpecularStrength;
            }

            Vector3 shadingNormal = normal;
            if (Vector3.Dot(shadingNormal, viewDir) < 0f)
            {
                shadingNormal = -shadingNormal;
            }
            float microInfluence = _project.SpiralNormalInfluenceEnabled ? 1f : 0f;
            float fadeStart = MathF.Max(0.1f, _project.SpiralNormalLodFadeStart);
            float fadeEnd = MathF.Max(fadeStart + 1e-3f, _project.SpiralNormalLodFadeEnd);
            float roughnessLodBoostFactor = MathF.Max(0f, _project.SpiralRoughnessLodBoost);

            Vector3 topTangent = new(1f, 0f, 0f);
            topTangent -= shadingNormal * Vector3.Dot(shadingNormal, topTangent);
            if (topTangent.LengthSquared() <= 1e-8f)
            {
                topTangent = new Vector3(0f, 1f, 0f);
                topTangent -= shadingNormal * Vector3.Dot(shadingNormal, topTangent);
            }

            if (topTangent.LengthSquared() <= 1e-8f)
            {
                topTangent = Vector3.UnitX;
            }
            else
            {
                topTangent = Vector3.Normalize(topTangent);
            }

            Vector3 topBitangent = Vector3.Normalize(Vector3.Cross(shadingNormal, topTangent));
            float localX = (centroid.X * modelCos) + (centroid.Y * modelSin);
            float localY = (-centroid.X * modelSin) + (centroid.Y * modelCos);
            float indicatorMask = ComputeIndicatorMask(
                localX,
                localY,
                topRadius,
                modelNode.IndicatorEnabled,
                modelNode.IndicatorShape,
                modelNode.IndicatorWidthRatio,
                modelNode.IndicatorLengthRatioTop,
                modelNode.IndicatorPositionRatio,
                modelNode.IndicatorRoundness);
            indicatorMask *= SmoothStep(0.55f, 0.95f, MathF.Abs(shadingNormal.Z));
            float topMask = Math.Clamp((MathF.Abs(shadingNormal.Z) - 0.55f) / 0.40f, 0f, 1f);
            topMask = MathF.Pow(topMask, 1.6f + ((0.8f - 1.6f) * surfaceCharacter));
            topMask *= 1f - Math.Clamp(indicatorMask, 0f, 1f);
            Vector2 uv = new(
                (centroid.X / MathF.Max(topRadius * 2f, 1e-4f)) + 0.5f,
                (centroid.Y / MathF.Max(topRadius * 2f, 1e-4f)) + 0.5f);
            bool uvInside = uv.X >= 0f && uv.X <= 1f && uv.Y >= 0f && uv.Y <= 1f;
            float uvFootprint = 1f / MathF.Max(1e-5f, topRadius * 2f * _currentShadeZoom);
            float texelsPerPixel = 1f / MathF.Max(uvFootprint * SpiralNormalMapSize, 1e-5f);
            float microDetailVisibility = SmoothStep(fadeStart, fadeEnd, texelsPerPixel);

            if (spiralNormalMap != null && uvInside && topMask > 0f)
            {
                Vector3 mapNormal = SampleSpiralNormalBilinear(spiralNormalMap, uv.X, uv.Y);
                Vector3 microNormal =
                    (topTangent * mapNormal.X) +
                    (topBitangent * mapNormal.Y) +
                    (shadingNormal * mapNormal.Z);
                if (microNormal.LengthSquared() > 1e-8f)
                {
                    microNormal = Vector3.Normalize(microNormal);
                    float densityInfluence = Lerp(0.65f, 1.35f, brushDensityFactor);
                    float microBlend = brushStrength * densityInfluence * topMask * microDetailVisibility * microInfluence;
                    microBlend = Math.Clamp(microBlend, 0f, 1f);
                    shadingNormal = Vector3.Normalize(Vector3.Lerp(shadingNormal, microNormal, microBlend));
                }
            }

            // Fade out unresolved high-frequency cap detail to avoid distance moire.
            float capFlatten = (1f - microDetailVisibility) * topMask * microInfluence * 0.35f;
            shadingNormal = Vector3.Normalize(Vector3.Lerp(shadingNormal, Vector3.UnitZ, Math.Clamp(capFlatten, 0f, 1f)));

            float roughnessLodBoost = (1f - microDetailVisibility) * roughnessLodBoostFactor * (0.35f + (0.65f * surfaceCharacter)) * microInfluence;
            roughness = Math.Clamp(roughness + roughnessLodBoost, 0.04f, 1f);
            float indicatorBlend = Math.Clamp(modelNode.IndicatorColorBlend * indicatorMask, 0f, 1f);
            baseColor = Vector3.Lerp(baseColor, modelNode.IndicatorColor, indicatorBlend);

            // Literal paint-mask weathering (R=rust, G=wear, B=gunk) in local object space.
            float paintU = (localX / MathF.Max(referenceRadius * 2f, 1e-4f)) + 0.5f;
            float paintV = (localY / MathF.Max(referenceRadius * 2f, 1e-4f)) + 0.5f;
            Vector4 paintSample = Vector4.Zero;
            if (paintU >= 0f && paintU <= 1f && paintV >= 0f && paintV <= 1f)
            {
                paintSample = _project.SamplePaintMaskBilinear(paintU, paintV);
            }

            float brushDarkness = Math.Clamp(_project.BrushDarkness, 0f, 1f);
            Vector3 scratchExposeColor = new(
                Math.Clamp(_project.ScratchExposeColor.X, 0f, 1f),
                Math.Clamp(_project.ScratchExposeColor.Y, 0f, 1f),
                Math.Clamp(_project.ScratchExposeColor.Z, 0f, 1f));
            float darknessGain = Lerp(0.45f, 1.45f, brushDarkness);
            float rustRaw = Math.Clamp(paintSample.X, 0f, 1f);
            float wearRaw = Math.Clamp(paintSample.Y, 0f, 1f);
            float gunkRaw = Math.Clamp(paintSample.Z, 0f, 1f);
            float scratchRaw = Math.Clamp(paintSample.W, 0f, 1f);

            float rustNoiseA = ValueNoise2D((paintU * 192f) + 11.3f, (paintV * 217f) + 6.7f);
            float rustNoiseB = ValueNoise2D((paintU * 67f) + 41.1f, (paintV * 59f) + 13.5f);
            float rustSplotch = SmoothStep(0.32f, 0.90f, (rustNoiseA * 0.72f) + (rustNoiseB * 0.58f));
            float rustStrength = Lerp(0.30f, 1.00f, rustAmount);
            float wearStrength = Lerp(0.15f, 0.70f, wearAmount);
            float gunkStrength = Lerp(0.35f, 1.20f, gunkAmount);
            float scratchStrength = Lerp(0.30f, 1.00f, wearAmount);
            float rustMask = Math.Clamp(rustRaw * rustSplotch * darknessGain * rustStrength, 0f, 1f);
            float wearMask = Math.Clamp(wearRaw * Lerp(0.30f, 0.80f, brushDarkness) * wearStrength, 0f, 1f);
            float gunkMask = Math.Clamp(gunkRaw * Lerp(0.55f, 1.65f, brushDarkness) * gunkStrength, 0f, 1f);
            float scratchMask = Math.Clamp(scratchRaw * Lerp(0.45f, 1.00f, brushDarkness) * scratchStrength, 0f, 1f);

            float rustHue = ValueNoise2D((paintU * 103f) + 3.1f, (paintV * 97f) + 17.2f);
            Vector3 rustDark = new(0.23f, 0.08f, 0.04f);
            Vector3 rustMid = new(0.46f, 0.17f, 0.07f);
            Vector3 rustOrange = new(0.71f, 0.29f, 0.09f);
            Vector3 rustColor = Vector3.Lerp(
                Vector3.Lerp(rustDark, rustMid, Math.Clamp(rustHue * 1.25f, 0f, 1f)),
                rustOrange,
                Math.Clamp((rustHue - 0.35f) / 0.65f, 0f, 1f));
            Vector3 gunkColor = new(0.02f, 0.02f, 0.018f);
            Vector3 wearColor = Vector3.Lerp(baseColor, new Vector3(0.80f, 0.79f, 0.76f), 0.45f);

            baseColor = Vector3.Lerp(baseColor, rustColor, Math.Clamp(rustMask * 0.88f, 0f, 1f));
            baseColor = Vector3.Lerp(baseColor, gunkColor, Math.Clamp(gunkMask * 0.96f, 0f, 1f));
            baseColor = Vector3.Lerp(baseColor, wearColor, Math.Clamp(wearMask * 0.24f, 0f, 1f));
            float grimeDarken = Math.Clamp((rustMask * 0.18f + gunkMask * 0.55f) * (0.25f + (0.75f * brushDarkness)), 0f, 0.85f);
            baseColor *= 1f - grimeDarken;
            baseColor = Vector3.Lerp(baseColor, scratchExposeColor, Math.Clamp(scratchMask, 0f, 1f));

            roughness = Math.Clamp(
                roughness +
                (rustMask * 0.34f) +
                (gunkMask * 0.62f) -
                (wearMask * 0.05f) -
                (scratchMask * 0.14f),
                0.04f,
                1f);
            metallic = Math.Clamp(
                metallic -
                (rustMask * 0.62f) -
                (gunkMask * 0.30f) +
                (scratchMask * 0.10f),
                0f,
                1f);
            shininess = 4f + ((128f - 4f) * (1f - roughness));

            Vector3 radial = new(-centroid.Y, centroid.X, 0f);
            Vector3 tangent = radial.LengthSquared() > 1e-8f ? Vector3.Normalize(radial) : Vector3.UnitX;
            tangent -= shadingNormal * Vector3.Dot(shadingNormal, tangent);
            if (tangent.LengthSquared() <= 1e-8f)
            {
                tangent = Vector3.Cross(Vector3.UnitZ, shadingNormal);
                tangent = tangent.LengthSquared() > 1e-8f ? Vector3.Normalize(tangent) : Vector3.UnitX;
            }
            else
            {
                tangent = Vector3.Normalize(tangent);
            }

            Vector3 bitangent = Vector3.Normalize(Vector3.Cross(shadingNormal, tangent));
            float anisotropy = Math.Clamp(
                brushStrength * topMask * (0.35f + (0.65f * surfaceCharacter)) * Lerp(0.8f, 1.2f, brushDensityFactor),
                0f,
                0.95f);
            float alpha = MathF.Max(0.02f, roughness * roughness);
            float alphaT = MathF.Max(0.02f, alpha * (1f - anisotropy));
            float alphaB = MathF.Max(0.02f, alpha * (1f + anisotropy));

            float NdotV = MathF.Max(0f, Vector3.Dot(shadingNormal, viewDir));
            float maxBase = MathF.Max(1e-6f, MathF.Max(baseColor.X, MathF.Max(baseColor.Y, baseColor.Z)));
            Vector3 metalSpecColor = baseColor / maxBase;
            Vector3 F0 = Vector3.Lerp(new Vector3(0.04f), metalSpecColor, metallic);
            Vector3 fresnelView = F0 + (Vector3.One - F0) * MathF.Pow(1f - NdotV, 5f);

            Vector3 accum = Hadamard(baseColor, ambientColor) * (1f - metallic);
            for (int i = 0; i < _project.Lights.Count; i++)
            {
                var light = _project.Lights[i];
                Vector3 lightColor = new(light.Color.Red / 255f, light.Color.Green / 255f, light.Color.Blue / 255f);

                Vector3 L;
                float attenuation;
                if (light.Type == LightType.Directional)
                {
                    L = Vector3.Normalize(ApplyGizmoOrientation(GetDirectionalVector(light)));
                    attenuation = 1f;
                }
                else
                {
                    Vector3 lightPos = ApplyGizmoOrientation(new Vector3(light.X, light.Y, light.Z));
                    Vector3 delta = lightPos - centroid;
                    float dist = MathF.Max(1e-4f, delta.Length());
                    L = delta / dist;
                    float distNorm = dist / MathF.Max(1f, referenceRadius * 2f);
                    attenuation = 1f / (1f + MathF.Max(0f, light.Falloff) * distNorm * distNorm);
                }

                if (ForceSecondLightNoAttenuation && i == 1)
                {
                    attenuation = 1f;
                }

                float NdotL = MathF.Max(0f, Vector3.Dot(shadingNormal, L));
                float diffuseFactor = NdotL;
                float effectiveDiffuse = diffuseFactor * (1f - metallic * 0.92f);
                Vector3 hRaw = L + viewDir;
                Vector3 halfVec = hRaw.LengthSquared() > 1e-8f ? Vector3.Normalize(hRaw) : viewDir;
                float NdotH = MathF.Max(0f, Vector3.Dot(shadingNormal, halfVec));
                float VdotH = MathF.Max(0f, Vector3.Dot(viewDir, halfVec));
                float TdotH = Vector3.Dot(tangent, halfVec);
                float BdotH = Vector3.Dot(bitangent, halfVec);
                float rawSpec = MathF.Pow(NdotH, shininess);
                float rawSpecBase = rawSpec;

                float dDenom = ((TdotH * TdotH) / (alphaT * alphaT)) +
                               ((BdotH * BdotH) / (alphaB * alphaB)) +
                               (NdotH * NdotH);
                float D = 1f / ((MathF.PI * alphaT * alphaB * dDenom * dDenom) + 1e-6f);

                float k = ((roughness + 1f) * (roughness + 1f)) / 8f;
                float Gv = NdotV / ((NdotV * (1f - k)) + k);
                float Gl = NdotL / ((NdotL * (1f - k)) + k);
                float G = Gv * Gl;
                Vector3 F = F0 + (Vector3.One - F0) * MathF.Pow(1f - VdotH, 5f);
                Vector3 specBrdf = F * ((D * G) / MathF.Max(4f * NdotV * NdotL, 1e-4f));

                ApplyModeShaping(_project.Mode, light, ref effectiveDiffuse, ref rawSpec);

                float intensity = MathF.Max(0f, light.Intensity) * attenuation;
                effectiveDiffuse *= intensity;
                float specShapeScale = rawSpecBase > 1e-5f ? (rawSpec / rawSpecBase) : 1f;
                float metalSpecBoost = 1f + ((2f - 1f) * metallic);
                float artisticSpecBoost = 0.55f + 0.45f * MathF.Max(0f, light.SpecularBoost);
                Vector3 specularTerm = specBrdf * NdotL;
                specularTerm *= specularStrength * intensity * metalSpecBoost * MathF.Max(0f, specShapeScale) * artisticSpecBoost;

                accum += Hadamard(baseColor, lightColor) * (effectiveDiffuse * diffuseStrength);
                accum += Hadamard(lightColor, specularTerm);
            }

            Vector3 envTop = _project.EnvironmentTopColor;
            Vector3 envBottom = _project.EnvironmentBottomColor;
            float envIntensity = MathF.Max(0f, _project.EnvironmentIntensity);
            float envRoughMix = Math.Clamp(_project.EnvironmentRoughnessMix, 0f, 1f);

            Vector3 reflection = Vector3.Reflect(-viewDir, shadingNormal);
            float hemi = Math.Clamp(reflection.Y * 0.5f + 0.5f, 0f, 1f);
            Vector3 envBase = envBottom + ((envTop - envBottom) * hemi);
            float horizonBand = MathF.Exp(-MathF.Abs(reflection.Y) * 12f);
            float skyHotspot = MathF.Pow(Math.Clamp((reflection.Z * 0.5f) + 0.5f, 0f, 1f), 24f) * hemi;
            Vector3 horizonColor = Vector3.Lerp(envTop, Vector3.One, 0.35f);
            Vector3 envColor = envBase + (horizonColor * (0.40f * horizonBand)) + (Vector3.One * (0.25f * skyHotspot));
            float envSpecWeight = 0.20f + (1.15f * metallic);
            Vector3 chromeTint = Vector3.Lerp(metalSpecColor, Vector3.One, 0.35f);
            Vector3 specTint = Vector3.Lerp(Vector3.One, chromeTint, metallic);
            Vector3 envSpecular = Hadamard(Hadamard(envColor, fresnelView), specTint) * envSpecWeight;
            float envDiffuseWeight = MathF.Max(0f, 1f - metallic);
            Vector3 envDiffuse = Hadamard(baseColor, envColor) * envDiffuseWeight;
            float envDiffuseEnergy = 0.35f;
            float roughEnergy = 1.12f + ((0.45f - 1.12f) * (roughness * envRoughMix));
            float anisotropicEnergy = 1f + ((1.35f - 1f) * anisotropy);
            float envBrush = Lerp(1f, 1.08f, brushStrength * topMask * (0.35f + (0.65f * surfaceCharacter)));
            accum += envDiffuse * (envIntensity * envDiffuseEnergy);
            accum += envSpecular * (envIntensity * roughEnergy * anisotropicEnergy * envBrush);

            if (pearlescence > 1e-4f)
            {
                float pearlEdge = MathF.Pow(1f - NdotV, 1.35f);
                Vector3 rv = reflection + viewDir;
                Vector3 rvn = rv.LengthSquared() > 1e-8f ? Vector3.Normalize(rv) : viewDir;
                float pearlPhase = Math.Clamp((Vector3.Dot(rvn, new Vector3(0.23f, 0.67f, 0.71f)) * 0.5f) + 0.5f, 0f, 1f);
                Vector3 pearlTint = new(
                    0.5f + (0.5f * MathF.Cos((MathF.Tau * (pearlPhase + 0.00f)))),
                    0.5f + (0.5f * MathF.Cos((MathF.Tau * (pearlPhase + 0.33f)))),
                    0.5f + (0.5f * MathF.Cos((MathF.Tau * (pearlPhase + 0.67f)))));
                float pearlStrength = pearlescence * (0.15f + (0.85f * pearlEdge));
                accum += pearlTint * pearlStrength * (0.20f + (0.80f * envIntensity));
            }

            accum = accum / (Vector3.One + accum);
            accum = Clamp01(accum);
            return new SKColor((byte)(accum.X * 255f), (byte)(accum.Y * 255f), (byte)(accum.Z * 255f), 255);
        }

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

        private static Vector3 RotateAroundZ(Vector3 v, float cosA, float sinA)
        {
            return new Vector3(v.X * cosA - v.Y * sinA, v.X * sinA + v.Y * cosA, v.Z);
        }

        private static SKPoint Project(Vector3 world, Vector3 right, Vector3 up, float centerX, float centerY, float zoom)
        {
            float sx = centerX + Vector3.Dot(world, right) * zoom;
            float sy = centerY - Vector3.Dot(world, up) * zoom;
            return new SKPoint(sx, sy);
        }

        private void GetCameraBasis(float yawDeg, float pitchDeg, out Vector3 right, out Vector3 up, out Vector3 forward)
        {
            float yaw = yawDeg * (MathF.PI / 180f);
            float pitch = Math.Clamp(pitchDeg, -85f, 85f) * (MathF.PI / 180f);

            forward = Vector3.Normalize(new Vector3(
                MathF.Sin(yaw) * MathF.Cos(pitch),
                MathF.Sin(pitch),
                -MathF.Cos(yaw) * MathF.Cos(pitch)));

            Vector3 worldUp = Vector3.UnitY;
            right = Vector3.Cross(worldUp, forward);
            if (right.LengthSquared() < 1e-6f)
            {
                right = Vector3.UnitX;
            }
            else
            {
                right = Vector3.Normalize(right);
            }

            up = Vector3.Normalize(Vector3.Cross(forward, right));

            if (_orientation.FlipCamera180)
            {
                forward = -forward;
                right = -right;
            }
        }

        private Vector3 ApplyGizmoOrientation(Vector3 value)
        {
            if (_orientation.InvertX)
            {
                value.X = -value.X;
            }

            if (_orientation.InvertY)
            {
                value.Y = -value.Y;
            }

            if (_orientation.InvertZ)
            {
                value.Z = -value.Z;
            }

            return value;
        }

        private static Vector3 GetDirectionalVector(KnobLight light)
        {
            float z = light.Z / 300f;
            Vector3 dir = new(MathF.Cos(light.DirectionRadians), MathF.Sin(light.DirectionRadians), z);
            if (dir.LengthSquared() < 1e-6f)
            {
                return Vector3.UnitZ;
            }

            return Vector3.Normalize(dir);
        }

        private static void ApplyGeometryLodFlatten(
            ref Vector3 pos,
            ref Vector3 normal,
            float frontZ,
            float geometryKeep,
            float indicatorProtect)
        {
            float topMask = SmoothStep(0.60f, 0.98f, normal.Z);
            float flatten = (1f - geometryKeep) * topMask * (1f - Math.Clamp(indicatorProtect, 0f, 1f));
            if (flatten <= 1e-4f)
            {
                return;
            }

            pos.Z = Lerp(pos.Z, frontZ, flatten);
            Vector3 adjusted = Vector3.Lerp(normal, Vector3.UnitZ, flatten);
            normal = adjusted.LengthSquared() > 1e-8f ? Vector3.Normalize(adjusted) : Vector3.UnitZ;
        }

        private static float ComputeIndicatorFlattenProtect(
            Vector3 worldPos,
            float topRadius,
            ModelNode modelNode,
            float modelCos,
            float modelSin)
        {
            float localX = (worldPos.X * modelCos) + (worldPos.Y * modelSin);
            float localY = (-worldPos.X * modelSin) + (worldPos.Y * modelCos);
            return ComputeIndicatorMask(
                localX,
                localY,
                topRadius,
                modelNode.IndicatorEnabled,
                modelNode.IndicatorShape,
                modelNode.IndicatorWidthRatio,
                modelNode.IndicatorLengthRatioTop,
                modelNode.IndicatorPositionRatio,
                modelNode.IndicatorRoundness);
        }

        private SpiralNormalMap GetOrBuildSpiralNormalMap(
            float referenceRadius,
            float topScale,
            float spiralHeight,
            float spiralWidth,
            float spiralTurns)
        {
            SpiralNormalMapKey key = new(
                MathF.Round(referenceRadius, 3),
                MathF.Round(topScale, 4),
                MathF.Round(spiralHeight, 4),
                MathF.Round(spiralWidth, 4),
                MathF.Round(spiralTurns, 4));

            if (_spiralNormalMap != null && key.Equals(_spiralNormalMapKey))
            {
                return _spiralNormalMap;
            }

            float topRadius = MathF.Max(1e-4f, referenceRadius * topScale);
            Vector3[] normals = BuildSpiralNormalMapNormals(
                SpiralNormalMapSize,
                topRadius,
                spiralHeight,
                spiralWidth,
                spiralTurns);

            _spiralNormalMap = new SpiralNormalMap
            {
                Size = SpiralNormalMapSize,
                Normals = normals
            };
            _spiralNormalMapKey = key;
            return _spiralNormalMap;
        }

        private static Vector3[] BuildSpiralNormalMapNormals(
            int size,
            float topRadius,
            float spiralHeight,
            float spiralWidth,
            float spiralTurns)
        {
            int clampedSize = Math.Clamp(size, 128, 4096);
            var normals = new Vector3[clampedSize * clampedSize];
            float invSizeMinusOne = 1f / MathF.Max(1, clampedSize - 1);
            float epsilon = (2f * topRadius) * invSizeMinusOne;

            for (int y = 0; y < clampedSize; y++)
            {
                float v = (y * invSizeMinusOne * 2f) - 1f;
                float py = v * topRadius;
                for (int x = 0; x < clampedSize; x++)
                {
                    float u = (x * invSizeMinusOne * 2f) - 1f;
                    float px = u * topRadius;
                    int idx = (y * clampedSize) + x;
                    float radialDistance = MathF.Sqrt((px * px) + (py * py));
                    if (radialDistance > topRadius)
                    {
                        normals[idx] = Vector3.UnitZ;
                        continue;
                    }

                    float xL = px - epsilon;
                    float xR = px + epsilon;
                    float yD = py - epsilon;
                    float yU = py + epsilon;

                    float hL = ComputeSpiralRidgeOffset(xL, py, MathF.Sqrt((xL * xL) + (py * py)), topRadius, spiralHeight, spiralWidth, spiralTurns);
                    float hR = ComputeSpiralRidgeOffset(xR, py, MathF.Sqrt((xR * xR) + (py * py)), topRadius, spiralHeight, spiralWidth, spiralTurns);
                    float hD = ComputeSpiralRidgeOffset(px, yD, MathF.Sqrt((px * px) + (yD * yD)), topRadius, spiralHeight, spiralWidth, spiralTurns);
                    float hU = ComputeSpiralRidgeOffset(px, yU, MathF.Sqrt((px * px) + (yU * yU)), topRadius, spiralHeight, spiralWidth, spiralTurns);

                    float dhdx = (hR - hL) / MathF.Max(1e-6f, 2f * epsilon);
                    float dhdy = (hU - hD) / MathF.Max(1e-6f, 2f * epsilon);
                    Vector3 n = Vector3.Normalize(new Vector3(-dhdx, -dhdy, 1f));
                    if (float.IsNaN(n.X) || float.IsNaN(n.Y) || float.IsNaN(n.Z))
                    {
                        n = Vector3.UnitZ;
                    }

                    normals[idx] = n;
                }
            }

            return normals;
        }

        private static Vector3 SampleSpiralNormalBilinear(SpiralNormalMap map, float u, float v)
        {
            float uc = Math.Clamp(u, 0f, 1f);
            float vc = Math.Clamp(v, 0f, 1f);
            float x = uc * (map.Size - 1);
            float y = vc * (map.Size - 1);
            int x0 = (int)MathF.Floor(x);
            int y0 = (int)MathF.Floor(y);
            int x1 = Math.Min(x0 + 1, map.Size - 1);
            int y1 = Math.Min(y0 + 1, map.Size - 1);
            float tx = x - x0;
            float ty = y - y0;

            Vector3 n00 = map.Normals[(y0 * map.Size) + x0];
            Vector3 n10 = map.Normals[(y0 * map.Size) + x1];
            Vector3 n01 = map.Normals[(y1 * map.Size) + x0];
            Vector3 n11 = map.Normals[(y1 * map.Size) + x1];
            Vector3 nx0 = Vector3.Lerp(n00, n10, tx);
            Vector3 nx1 = Vector3.Lerp(n01, n11, tx);
            Vector3 n = Vector3.Lerp(nx0, nx1, ty);
            return n.LengthSquared() > 1e-8f ? Vector3.Normalize(n) : Vector3.UnitZ;
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

        private static float Lerp(float a, float b, float t)
        {
            return a + ((b - a) * t);
        }

        private static void ApplyModeShaping(LightingMode mode, KnobLight light, ref float diffuse, ref float spec)
        {
            switch (mode)
            {
                case LightingMode.Realistic:
                    break;
                case LightingMode.Artistic:
                    diffuse = MathF.Pow(diffuse, 0.80f) * MathF.Max(0f, light.DiffuseBoost);
                    spec = MathF.Pow(spec, 0.65f) * MathF.Max(0f, light.SpecularBoost);
                    break;
                case LightingMode.Both:
                    diffuse = MathF.Pow(diffuse, 0.90f) * (0.65f + 0.35f * MathF.Max(0f, light.DiffuseBoost));
                    spec = MathF.Pow(spec, 0.78f) * (0.65f + 0.35f * MathF.Max(0f, light.SpecularBoost));
                    break;
            }
        }

        private static SKColor WithAlpha(SKColor color, byte alpha)
        {
            return new SKColor(color.Red, color.Green, color.Blue, alpha);
        }

        private static Vector2 Normalize2(Vector2 v)
        {
            float len = v.Length();
            if (len < 1e-6f)
            {
                return new Vector2(1f, 0f);
            }

            return v / len;
        }

        private static float ComputeSpiralRidgeOffset(
            float x,
            float y,
            float radialDistance,
            float topRadius,
            float ridgeHeight,
            float ridgeWidth,
            float spiralTurns)
        {
            if (ridgeHeight <= 0f || topRadius <= 1e-6f || spiralTurns <= 1e-6f)
            {
                return 0f;
            }

            float rNorm = Math.Clamp(radialDistance / topRadius, 0f, 1f);
            float theta = MathF.Atan2(y, x);
            if (theta < 0f)
            {
                theta += MathF.PI * 2f;
            }

            float thetaNorm = theta / (MathF.PI * 2f);
            float ringCount = MathF.Max(1f, spiralTurns);
            float phaseNoise = ValueNoise2D((rNorm * 60f) + 17.2f, (thetaNorm * 40f) + 9.7f);
            float phaseJitter = (phaseNoise - 0.5f) * 0.15f;
            float phase = (rNorm * ringCount) + phaseJitter;
            float nearest = MathF.Round(phase);
            float absDist = MathF.Abs((phase - nearest) / ringCount);

            float microHeight = ridgeHeight * 0.075f;
            float widthNoise = ValueNoise2D((rNorm * 80f) + 2.3f, (thetaNorm * 48f) + 4.1f);
            float widthJitter = 1f + ((widthNoise - 0.5f) * 0.08f);
            float widthNorm = MathF.Max(1e-6f, (ridgeWidth * 1.25f * widthJitter) / MathF.Max(topRadius, 1e-4f));
            float halfWidth = widthNorm * 0.5f;
            if (absDist >= halfWidth)
            {
                return 0f;
            }

            float t = absDist / halfWidth;
            float vProfile = 1f - t;
            vProfile *= vProfile;
            vProfile *= vProfile;
            float heightNoise = ValueNoise2D((rNorm * 96f) + 13.9f, (thetaNorm * 56f) + 5.8f);
            float heightJitter = 1f + ((heightNoise - 0.5f) * 0.04f);
            float edgeT = Math.Clamp((rNorm - 0.975f) / 0.025f, 0f, 1f);
            float edgeFade = 1f - (edgeT * edgeT * (3f - (2f * edgeT)));
            return -microHeight * heightJitter * vProfile * edgeFade;
        }

        private static float ComputeBodyRadius(float t, float radiusStart, float radiusEnd, float bodyBulge)
        {
            float baseRadius = radiusStart + ((radiusEnd - radiusStart) * t);
            float arch = 1f - MathF.Pow((2f * t) - 1f, 2f);
            float bulgeScale = radiusStart * bodyBulge * 0.22f;
            return MathF.Max(1f, baseRadius + (bulgeScale * arch));
        }

        private static float ComputeIndicatorOffset(
            float x,
            float y,
            float topRadius,
            bool enabled,
            IndicatorShape shape,
            IndicatorRelief relief,
            IndicatorProfile profile,
            float widthRatio,
            float lengthRatio,
            float positionRatio,
            float thicknessRatio,
            float roundness)
        {
            if (!enabled || thicknessRatio <= 1e-6f || topRadius <= 1e-6f)
            {
                return 0f;
            }

            Vector2 p = new(x / topRadius, y / topRadius);
            float t = p.Y;
            float start = Math.Clamp(positionRatio, 0.05f, 0.90f);
            float end = Math.Clamp(start + lengthRatio, start + 1e-4f, 0.98f);
            float halfWidth = MathF.Max(0.001f, widthRatio * 0.5f);
            float along = (t - start) / MathF.Max(1e-4f, end - start);
            float v;
            float edgeDistance;
            if (shape == IndicatorShape.Dot)
            {
                float dotRadius = halfWidth;
                float centerY = end - MathF.Min(dotRadius * 0.35f, (end - start) * 0.25f);
                float dx = p.X;
                float dy = p.Y - centerY;
                v = MathF.Sqrt((dx * dx) + (dy * dy)) / MathF.Max(dotRadius, 1e-6f);
                edgeDistance = v;
            }
            else
            {
                if (t < start || t > end)
                {
                    return 0f;
                }

                float localHalfWidth = halfWidth;
                if (shape == IndicatorShape.Tapered)
                {
                    localHalfWidth *= MathF.Max(0.20f, 1f - (along * 0.80f));
                }
                else if (shape == IndicatorShape.Needle)
                {
                    localHalfWidth *= MathF.Max(0.06f, 1f - (along * 0.94f));
                }
                else if (shape == IndicatorShape.Triangle)
                {
                    localHalfWidth *= MathF.Max(0.02f, 1f - along);
                }

                if (shape == IndicatorShape.Diamond)
                {
                    float qx = MathF.Abs(p.X) / MathF.Max(halfWidth, 1e-6f);
                    float qy = MathF.Abs((along * 2f) - 1f);
                    v = qx + qy;
                    edgeDistance = v;
                }
                else
                {
                    v = MathF.Abs(p.X) / MathF.Max(localHalfWidth, 1e-6f);
                    edgeDistance = v;
                }
            }

            if (edgeDistance >= 1f)
            {
                return 0f;
            }

            float edgeMask;
            if (profile == IndicatorProfile.Straight)
            {
                // Straight profile should remain hard-edged, independent from roundness feathering.
                edgeMask = 1f;
            }
            else if (roundness <= 1e-4f)
            {
                edgeMask = 1f;
            }
            else
            {
                float feather = Math.Clamp(roundness, 0f, 1f) * 0.45f;
                edgeMask = 1f - SmoothStep(1f - feather, 1f, edgeDistance);
            }

            float capMask = 1f;
            if (shape == IndicatorShape.Capsule)
            {
                float endDistance = MathF.Min(along, 1f - along);
                capMask = SmoothStep(0f, 0.22f, endDistance);
            }
            else if (shape == IndicatorShape.Dot || shape == IndicatorShape.Diamond)
            {
                capMask = 1f;
            }

            float profileMask = profile switch
            {
                IndicatorProfile.Straight => 1f,
                IndicatorProfile.Rounded => 1f - (edgeDistance * edgeDistance),
                IndicatorProfile.Convex => MathF.Sqrt(MathF.Max(0f, 1f - (edgeDistance * edgeDistance))),
                IndicatorProfile.Concave => MathF.Pow(MathF.Max(0f, 1f - edgeDistance), 2f),
                _ => 1f
            };

            float sign = relief == IndicatorRelief.Inset ? -1f : 1f;
            float amplitude = thicknessRatio * topRadius;
            return sign * amplitude * edgeMask * capMask * profileMask;
        }

        private static void AppendIndicatorHardWalls(
            List<Vector3> positions,
            List<Vector3> normals,
            List<int> indices,
            float topRadius,
            float zFront,
            float crownProfile,
            float radius,
            float height,
            float spiralHeight,
            float spiralWidth,
            float spiralTurns,
            IndicatorShape shape,
            IndicatorRelief relief,
            float widthRatio,
            float lengthRatio,
            float positionRatio,
            float thicknessRatio)
        {
            List<Vector2> contour = BuildIndicatorContour(shape, widthRatio, lengthRatio, positionRatio);
            if (contour.Count < 3)
            {
                return;
            }

            EnsureCounterClockwise(contour);

            float signedAmplitude = (relief == IndicatorRelief.Inset ? -1f : 1f) * (thicknessRatio * topRadius);
            for (int i = 0; i < contour.Count; i++)
            {
                Vector2 c0 = contour[i] * topRadius;
                Vector2 c1 = contour[(i + 1) % contour.Count] * topRadius;
                Vector2 edge2 = c1 - c0;
                if (edge2.LengthSquared() <= 1e-10f)
                {
                    continue;
                }

                edge2 = Normalize2(edge2);
                Vector3 outward = Vector3.Normalize(new Vector3(edge2.Y, -edge2.X, 0f));

                float r0 = c0.Length();
                float r1 = c1.Length();
                float zOuter0 = zFront +
                                ComputeCrownOffset(Math.Clamp(r0 / MathF.Max(topRadius, 1e-6f), 0f, 1f), crownProfile, radius, height) +
                                ComputeSpiralRidgeOffset(c0.X, c0.Y, r0, topRadius, spiralHeight, spiralWidth, spiralTurns);
                float zOuter1 = zFront +
                                ComputeCrownOffset(Math.Clamp(r1 / MathF.Max(topRadius, 1e-6f), 0f, 1f), crownProfile, radius, height) +
                                ComputeSpiralRidgeOffset(c1.X, c1.Y, r1, topRadius, spiralHeight, spiralWidth, spiralTurns);

                Vector3 v0 = new(c0.X, c0.Y, zOuter0);
                Vector3 v1 = new(c1.X, c1.Y, zOuter1);
                Vector3 v2 = new(c1.X, c1.Y, zOuter1 + signedAmplitude);
                Vector3 v3 = new(c0.X, c0.Y, zOuter0 + signedAmplitude);

                int baseIndex = positions.Count;
                positions.Add(v0);
                positions.Add(v1);
                positions.Add(v2);
                positions.Add(v3);
                normals.Add(outward);
                normals.Add(outward);
                normals.Add(outward);
                normals.Add(outward);

                Vector3 faceNormal = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
                bool aligned = Vector3.Dot(faceNormal, outward) >= 0f;
                if (aligned)
                {
                    indices.Add(baseIndex + 0);
                    indices.Add(baseIndex + 1);
                    indices.Add(baseIndex + 2);
                    indices.Add(baseIndex + 0);
                    indices.Add(baseIndex + 2);
                    indices.Add(baseIndex + 3);
                }
                else
                {
                    indices.Add(baseIndex + 0);
                    indices.Add(baseIndex + 2);
                    indices.Add(baseIndex + 1);
                    indices.Add(baseIndex + 0);
                    indices.Add(baseIndex + 3);
                    indices.Add(baseIndex + 2);
                }
            }
        }

        private static List<Vector2> BuildIndicatorContour(
            IndicatorShape shape,
            float widthRatio,
            float lengthRatio,
            float positionRatio)
        {
            float start = Math.Clamp(positionRatio, 0.05f, 0.90f);
            float end = Math.Clamp(start + lengthRatio, start + 1e-4f, 0.98f);
            float halfWidth = MathF.Max(0.001f, widthRatio * 0.5f);
            var contour = new List<Vector2>(64);

            switch (shape)
            {
                case IndicatorShape.Dot:
                {
                    float centerY = end - MathF.Min(halfWidth * 0.35f, (end - start) * 0.25f);
                    const int segments = 28;
                    for (int i = 0; i < segments; i++)
                    {
                        float a = i * (MathF.Tau / segments);
                        contour.Add(new Vector2(MathF.Cos(a) * halfWidth, centerY + MathF.Sin(a) * halfWidth));
                    }

                    break;
                }
                case IndicatorShape.Diamond:
                {
                    float centerY = (start + end) * 0.5f;
                    contour.Add(new Vector2(0f, start));
                    contour.Add(new Vector2(halfWidth, centerY));
                    contour.Add(new Vector2(0f, end));
                    contour.Add(new Vector2(-halfWidth, centerY));
                    break;
                }
                case IndicatorShape.Capsule:
                {
                    float radius = halfWidth;
                    float y0 = start + radius;
                    float y1 = end - radius;
                    if (y1 <= y0 + 1e-5f)
                    {
                        float centerY = (start + end) * 0.5f;
                        const int segments = 28;
                        for (int i = 0; i < segments; i++)
                        {
                            float a = i * (MathF.Tau / segments);
                            contour.Add(new Vector2(MathF.Cos(a) * radius, centerY + MathF.Sin(a) * radius));
                        }
                        break;
                    }

                    const int arcSegments = 14;
                    contour.Add(new Vector2(radius, y0));
                    contour.Add(new Vector2(radius, y1));
                    for (int i = 1; i <= arcSegments; i++)
                    {
                        float a = (MathF.PI * i) / (arcSegments + 1);
                        contour.Add(new Vector2(MathF.Cos(a) * radius, y1 + MathF.Sin(a) * radius));
                    }

                    contour.Add(new Vector2(-radius, y1));
                    contour.Add(new Vector2(-radius, y0));
                    for (int i = 1; i <= arcSegments; i++)
                    {
                        float a = MathF.PI + ((MathF.PI * i) / (arcSegments + 1));
                        contour.Add(new Vector2(MathF.Cos(a) * radius, y0 + MathF.Sin(a) * radius));
                    }

                    break;
                }
                default:
                {
                    const int samples = 24;
                    for (int i = 0; i <= samples; i++)
                    {
                        float t = i / (float)samples;
                        float y = Lerp(start, end, t);
                        float along = (y - start) / MathF.Max(1e-5f, end - start);
                        float hw = halfWidth;
                        if (shape == IndicatorShape.Tapered)
                        {
                            hw *= MathF.Max(0.20f, 1f - (along * 0.80f));
                        }
                        else if (shape == IndicatorShape.Needle)
                        {
                            hw *= MathF.Max(0.06f, 1f - (along * 0.94f));
                        }
                        else if (shape == IndicatorShape.Triangle)
                        {
                            hw *= MathF.Max(0.02f, 1f - along);
                        }

                        contour.Add(new Vector2(hw, y));
                    }

                    for (int i = samples; i >= 0; i--)
                    {
                        float t = i / (float)samples;
                        float y = Lerp(start, end, t);
                        float along = (y - start) / MathF.Max(1e-5f, end - start);
                        float hw = halfWidth;
                        if (shape == IndicatorShape.Tapered)
                        {
                            hw *= MathF.Max(0.20f, 1f - (along * 0.80f));
                        }
                        else if (shape == IndicatorShape.Needle)
                        {
                            hw *= MathF.Max(0.06f, 1f - (along * 0.94f));
                        }
                        else if (shape == IndicatorShape.Triangle)
                        {
                            hw *= MathF.Max(0.02f, 1f - along);
                        }

                        contour.Add(new Vector2(-hw, y));
                    }

                    break;
                }
            }

            return contour;
        }

        private static void EnsureCounterClockwise(List<Vector2> points)
        {
            double area2 = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % points.Count];
                area2 += (a.X * b.Y) - (b.X * a.Y);
            }

            if (area2 < 0.0)
            {
                points.Reverse();
            }
        }

        private static float ComputeIndicatorMask(
            float x,
            float y,
            float topRadius,
            bool enabled,
            IndicatorShape shape,
            float widthRatio,
            float lengthRatio,
            float positionRatio,
            float roundness)
        {
            if (!enabled || topRadius <= 1e-6f)
            {
                return 0f;
            }

            Vector2 p = new(x / topRadius, y / topRadius);
            float t = p.Y;
            float start = Math.Clamp(positionRatio, 0.05f, 0.90f);
            float end = Math.Clamp(start + lengthRatio, start + 1e-4f, 0.98f);
            float halfWidth = MathF.Max(0.001f, widthRatio * 0.5f);
            float along = (t - start) / MathF.Max(1e-4f, end - start);
            float edgeDistance;
            if (shape == IndicatorShape.Dot)
            {
                float dotRadius = halfWidth;
                float centerY = end - MathF.Min(dotRadius * 0.35f, (end - start) * 0.25f);
                float dx = p.X;
                float dy = p.Y - centerY;
                edgeDistance = MathF.Sqrt((dx * dx) + (dy * dy)) / MathF.Max(dotRadius, 1e-6f);
            }
            else
            {
                if (t < start || t > end)
                {
                    return 0f;
                }

                float localHalfWidth = halfWidth;
                if (shape == IndicatorShape.Tapered)
                {
                    localHalfWidth *= MathF.Max(0.20f, 1f - (along * 0.80f));
                }
                else if (shape == IndicatorShape.Needle)
                {
                    localHalfWidth *= MathF.Max(0.06f, 1f - (along * 0.94f));
                }
                else if (shape == IndicatorShape.Triangle)
                {
                    localHalfWidth *= MathF.Max(0.02f, 1f - along);
                }

                if (shape == IndicatorShape.Diamond)
                {
                    float qx = MathF.Abs(p.X) / MathF.Max(halfWidth, 1e-6f);
                    float qy = MathF.Abs((along * 2f) - 1f);
                    edgeDistance = qx + qy;
                }
                else
                {
                    edgeDistance = MathF.Abs(p.X) / MathF.Max(localHalfWidth, 1e-6f);
                }
            }

            if (edgeDistance >= 1f)
            {
                return 0f;
            }

            float edgeMask;
            if (roundness <= 1e-4f)
            {
                edgeMask = 1f;
            }
            else
            {
                float feather = Math.Clamp(roundness, 0f, 1f) * 0.45f;
                edgeMask = 1f - SmoothStep(1f - feather, 1f, edgeDistance);
            }

            float capMask = 1f;
            if (shape == IndicatorShape.Capsule)
            {
                float endDistance = MathF.Min(along, 1f - along);
                capMask = SmoothStep(0f, 0.22f, endDistance);
            }

            return edgeMask * capMask;
        }

        private static float ComputeCrownOffset(float rNorm, float crownProfile, float radius, float height)
        {
            if (MathF.Abs(crownProfile) <= 1e-5f)
            {
                return 0f;
            }

            float t = 1f - Math.Clamp(rNorm, 0f, 1f);
            float magnitude = MathF.Abs(crownProfile);
            float exponent = 1.6f + ((1f - magnitude) * 1.2f);
            float falloff = MathF.Pow(t, exponent);
            float maxAmplitude = MathF.Min(radius, height) * 0.08f;
            return MathF.Sign(crownProfile) * maxAmplitude * magnitude * falloff;
        }

        private static float ComputeGripOffset(
            GripType gripType,
            float angle,
            float z,
            float sideStartZ,
            float sideEndZ,
            float gripStart,
            float gripHeight,
            float gripDensity,
            float gripPitch,
            float gripDepth,
            float gripWidth,
            float gripSharpness)
        {
            if (gripType == GripType.None || gripDepth <= 1e-6f)
            {
                return 0f;
            }

            float sideSpan = MathF.Max(1e-4f, sideEndZ - sideStartZ);
            float zNorm = Math.Clamp((z - sideStartZ) / sideSpan, 0f, 1f);
            float start = Math.Clamp(gripStart, 0f, 1f);
            float end = Math.Clamp(start + Math.Clamp(gripHeight, 0.05f, 1f), start + 0.001f, 1f);
            if (zNorm < start || zNorm > end)
            {
                return 0f;
            }

            float localZNorm = (zNorm - start) / MathF.Max(1e-4f, end - start);
            float thetaNorm = angle / (MathF.PI * 2f);
            float density = MathF.Max(1f, gripDensity);
            float pitch = MathF.Max(0.2f, gripPitch);
            float u = thetaNorm * density;
            float v = localZNorm * pitch;
            float lineWidth = Math.Clamp(0.02f + (gripWidth * 0.10f), 0.015f, 0.35f);
            float pattern = ComputeKnurlPattern(gripType, u, v, lineWidth);
            float sharpExponent = Math.Clamp(1f + ((Math.Clamp(gripSharpness, 0.5f, 8f) - 1f) * 0.7f), 0.45f, 5f);
            float shape = MathF.Pow(Math.Clamp(pattern, 0f, 1f), sharpExponent);
            float bandFade = SmoothStep(0f, 0.06f, localZNorm) * (1f - SmoothStep(0.94f, 1f, localZNorm));
            float depthWorld = MathF.Max(0f, gripDepth) * sideSpan * 0.016f;
            float depthScale = 0.75f + (MathF.Min(1.5f, gripWidth) * 0.25f);
            return depthWorld * depthScale * shape * bandFade;
        }

        private static float ComputeKnurlPattern(GripType gripType, float u, float v, float width)
        {
            switch (gripType)
            {
                case GripType.VerticalFlutes:
                {
                    return RidgeMask(NearestIntegerDistance(u), width);
                }
                case GripType.DiamondKnurl:
                {
                    float m1 = RidgeMask(NearestIntegerDistance(u + v), width);
                    float m2 = RidgeMask(NearestIntegerDistance(u - v), width);
                    return m1 * m2;
                }
                case GripType.SquareKnurl:
                {
                    float m1 = RidgeMask(NearestIntegerDistance(u), width);
                    float m2 = RidgeMask(NearestIntegerDistance(v), width);
                    return m1 * m2;
                }
                case GripType.HexKnurl:
                {
                    const float cos60 = 0.5f;
                    const float sin60 = 0.8660254f;
                    float m1 = RidgeMask(NearestIntegerDistance(u), width);
                    float m2 = RidgeMask(NearestIntegerDistance((cos60 * u) + (sin60 * v)), width);
                    float m3 = RidgeMask(NearestIntegerDistance((cos60 * u) - (sin60 * v)), width);
                    return ((m1 * m2) + (m2 * m3) + (m3 * m1)) / 3f;
                }
                default:
                    return 0f;
            }
        }

        private static float NearestIntegerDistance(float x)
        {
            return MathF.Abs(Fract(x + 0.5f) - 0.5f);
        }

        private static float RidgeMask(float distance, float width)
        {
            return Math.Clamp(1f - (distance / MathF.Max(width, 1e-4f)), 0f, 1f);
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

        private static float ValueNoise2D(float x, float y)
        {
            int x0 = (int)MathF.Floor(x);
            int y0 = (int)MathF.Floor(y);
            int x1 = x0 + 1;
            int y1 = y0 + 1;
            float tx = x - x0;
            float ty = y - y0;
            float sx = tx * tx * (3f - (2f * tx));
            float sy = ty * ty * (3f - (2f * ty));

            float n00 = Hash2(x0, y0);
            float n10 = Hash2(x1, y0);
            float n01 = Hash2(x0, y1);
            float n11 = Hash2(x1, y1);
            float nx0 = n00 + ((n10 - n00) * sx);
            float nx1 = n01 + ((n11 - n01) * sx);
            return nx0 + ((nx1 - nx0) * sy);
        }

        private static float Hash2(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393) + (uint)(y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0x00FFFFFF) / 16777215f;
            }
        }

        private static float Fract(float x)
        {
            return x - MathF.Floor(x);
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
