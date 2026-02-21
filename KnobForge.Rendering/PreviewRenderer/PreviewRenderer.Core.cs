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

    }
}
