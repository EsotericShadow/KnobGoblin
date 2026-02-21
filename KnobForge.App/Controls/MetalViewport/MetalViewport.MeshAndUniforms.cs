using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using KnobForge.Core;
using KnobForge.Core.Scene;
using KnobForge.Rendering.GPU;

namespace KnobForge.App.Controls
{
    public sealed partial class MetalViewport
    {
        private void ClearMeshResources()
        {
            ReplaceMeshResources(ref _meshResources, null);
            ReplaceMeshResources(ref _collarResources, null);
            _paintPickMapDirty = true;
        }

        private void ReplaceMeshResources(ref MetalMeshGpuResources? target, MetalMeshGpuResources? replacement)
        {
            if (ReferenceEquals(target, replacement))
            {
                return;
            }

            target?.Dispose();
            target = replacement;
            _paintPickMapDirty = true;
        }

        private void RefreshMeshResources(KnobProject? project, ModelNode? modelNode)
        {
            if (_context is null || project is null || modelNode is null)
            {
                ClearMeshResources();
                _collarShapeKey = default;
                return;
            }

            CollarNode? collarNode = modelNode.Children.OfType<CollarNode>().FirstOrDefault();
            CollarShapeKey nextCollarKey = BuildCollarShapeKey(modelNode, collarNode);
            bool collarEnabled = collarNode is { Enabled: true } && collarNode.Preset != CollarPreset.None;
            bool collarShapeChanged = !nextCollarKey.Equals(_collarShapeKey);
            if (!collarEnabled)
            {
                ReplaceMeshResources(ref _collarResources, null);
                _collarShapeKey = default;
            }
            else if (collarShapeChanged || _collarResources == null)
            {
                _collarShapeKey = nextCollarKey;
                MetalMeshGpuResources? nextCollarResources = null;
                CollarMesh? collarMesh = CollarMeshBuilder.TryBuildFromProject(project);
                if (collarMesh is null || collarMesh.Vertices.Length == 0 || collarMesh.Indices.Length == 0)
                {
                    Console.WriteLine(
                        $"[MetalViewport] Collar mesh build failed. enabled={collarEnabled}, preset={collarNode?.Preset}, pathSegments={collarNode?.PathSegments ?? 0}, crossSegments={collarNode?.CrossSegments ?? 0}, importPath={collarNode?.ImportedMeshPath ?? "<none>"}");
                }
                else
                {
                    nextCollarResources = CreateGpuResources(collarMesh.Vertices, collarMesh.Indices, collarMesh.ReferenceRadius);
                }

                ReplaceMeshResources(ref _collarResources, nextCollarResources);
            }

            MeshShapeKey nextKey = new(
                MathF.Round(modelNode.Radius, 3),
                MathF.Round(modelNode.Height, 3),
                MathF.Round(modelNode.Bevel, 3),
                MathF.Round(modelNode.TopRadiusScale, 3),
                modelNode.RadialSegments,
                MathF.Round(modelNode.CrownProfile, 4),
                MathF.Round(modelNode.BevelCurve, 4),
                MathF.Round(modelNode.BodyTaper, 4),
                MathF.Round(modelNode.BodyBulge, 4),
                MathF.Round(modelNode.SpiralRidgeHeight, 3),
                MathF.Round(modelNode.SpiralRidgeWidth, 3),
                MathF.Round(modelNode.SpiralRidgeHeightVariance, 3),
                MathF.Round(modelNode.SpiralRidgeWidthVariance, 3),
                MathF.Round(modelNode.SpiralHeightVarianceThreshold, 3),
                MathF.Round(modelNode.SpiralWidthVarianceThreshold, 3),
                MathF.Round(modelNode.SpiralTurns, 3),
                (int)modelNode.GripType,
                MathF.Round(modelNode.GripStart, 4),
                MathF.Round(modelNode.GripHeight, 4),
                MathF.Round(modelNode.GripDensity, 3),
                MathF.Round(modelNode.GripPitch, 3),
                MathF.Round(modelNode.GripDepth, 3),
                MathF.Round(modelNode.GripWidth, 4),
                MathF.Round(modelNode.GripSharpness, 3),
                modelNode.IndicatorEnabled ? 1 : 0,
                (int)modelNode.IndicatorShape,
                (int)modelNode.IndicatorRelief,
                (int)modelNode.IndicatorProfile,
                MathF.Round(modelNode.IndicatorWidthRatio, 4),
                MathF.Round(modelNode.IndicatorLengthRatioTop, 4),
                MathF.Round(modelNode.IndicatorPositionRatio, 4),
                MathF.Round(modelNode.IndicatorThicknessRatio, 4),
                MathF.Round(modelNode.IndicatorRoundness, 4),
                modelNode.IndicatorCadWallsEnabled ? 1 : 0);

            if (_meshResources != null && nextKey.Equals(_meshShapeKey))
            {
                EnsureSpiralNormalTexture(modelNode, _meshResources.ReferenceRadius);
                return;
            }

            MetalMesh? mesh = MetalMeshBuilder.TryBuildFromProject(project);
            if (mesh is null || mesh.Vertices.Length == 0 || mesh.Indices.Length == 0)
            {
                ReplaceMeshResources(ref _meshResources, null);
                _meshShapeKey = default;
                return;
            }

            MetalMeshGpuResources? nextMeshResources = CreateGpuResources(mesh.Vertices, mesh.Indices, mesh.ReferenceRadius);
            ReplaceMeshResources(ref _meshResources, nextMeshResources);
            if (_meshResources == null)
            {
                _meshShapeKey = default;
                return;
            }

            _meshShapeKey = nextKey;
            EnsureSpiralNormalTexture(modelNode, mesh.ReferenceRadius);
        }

        private MetalMeshGpuResources? CreateGpuResources(MetalVertex[] vertices, uint[] indices, float referenceRadius)
        {
            if (_context is null)
            {
                return null;
            }

            IMTLBuffer vertexBuffer = _context.CreateBuffer<MetalVertex>(vertices);
            IMTLBuffer indexBuffer = _context.CreateBuffer<uint>(indices);
            if (vertexBuffer.Handle == IntPtr.Zero || indexBuffer.Handle == IntPtr.Zero)
            {
                vertexBuffer.Dispose();
                indexBuffer.Dispose();
                return null;
            }

            var positions = new Vector3[vertices.Length];
            Vector3 boundsMin = new(float.MaxValue);
            Vector3 boundsMax = new(float.MinValue);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = vertices[i].Position;
                positions[i] = p;
                boundsMin = Vector3.Min(boundsMin, p);
                boundsMax = Vector3.Max(boundsMax, p);
            }

            uint[] indicesCopy = indices.ToArray();
            CpuTriangleBvh bvh = CpuTriangleBvh.Build(positions, indicesCopy);

            return new MetalMeshGpuResources
            {
                VertexBuffer = vertexBuffer,
                IndexBuffer = indexBuffer,
                IndexCount = indices.Length,
                IndexType = MTLIndexType.UInt32,
                ReferenceRadius = referenceRadius,
                Positions = positions,
                Indices = indicesCopy,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax,
                Bvh = bvh
            };
        }

        private static bool IsImportedCollarPreset(CollarNode? collarNode)
        {
            return collarNode is not null && CollarNode.IsImportedMeshPreset(collarNode.Preset);
        }

        private static string ResolveImportedMeshPath(CollarNode collarNode)
        {
            return CollarNode.ResolveImportedMeshPath(collarNode.Preset, collarNode.ImportedMeshPath);
        }

        private static CollarShapeKey BuildCollarShapeKey(ModelNode modelNode, CollarNode? collarNode)
        {
            if (collarNode is null)
            {
                return default;
            }

            string importedMeshPath = ResolveImportedMeshPath(collarNode);
            long importedFileTicks = 0;
            if (!string.IsNullOrWhiteSpace(importedMeshPath) && File.Exists(importedMeshPath))
            {
                importedFileTicks = File.GetLastWriteTimeUtc(importedMeshPath).Ticks;
            }

            return new CollarShapeKey(
                collarNode.Enabled ? 1 : 0,
                (int)collarNode.Preset,
                MathF.Round(modelNode.Radius, 3),
                MathF.Round(modelNode.Height, 3),
                MathF.Round(collarNode.InnerRadiusRatio, 4),
                MathF.Round(collarNode.GapToKnobRatio, 4),
                MathF.Round(collarNode.ElevationRatio, 4),
                MathF.Round(collarNode.OverallRotationRadians, 4),
                MathF.Round(collarNode.BiteAngleRadians, 4),
                MathF.Round(collarNode.BodyRadiusRatio, 4),
                MathF.Round(collarNode.BodyEllipseYScale, 4),
                MathF.Round(collarNode.NeckTaper, 4),
                MathF.Round(collarNode.TailTaper, 4),
                MathF.Round(collarNode.MassBias, 4),
                MathF.Round(collarNode.TailUnderlap, 4),
                MathF.Round(collarNode.HeadScale, 4),
                MathF.Round(collarNode.JawBulge, 4),
                collarNode.UvSeamFollowBite ? 1 : 0,
                MathF.Round(collarNode.UvSeamOffset, 4),
                collarNode.PathSegments,
                collarNode.CrossSegments,
                MathF.Round(collarNode.ImportedScale, 4),
                MathF.Round(collarNode.ImportedBodyLengthScale, 4),
                MathF.Round(collarNode.ImportedBodyThicknessScale, 4),
                MathF.Round(collarNode.ImportedHeadLengthScale, 4),
                MathF.Round(collarNode.ImportedHeadThicknessScale, 4),
                MathF.Round(collarNode.ImportedRotationRadians, 4),
                collarNode.ImportedMirrorX ? 1 : 0,
                collarNode.ImportedMirrorY ? 1 : 0,
                collarNode.ImportedMirrorZ ? 1 : 0,
                MathF.Round(collarNode.ImportedOffsetXRatio, 4),
                MathF.Round(collarNode.ImportedOffsetYRatio, 4),
                MathF.Round(collarNode.ImportedInflateRatio, 4),
                importedMeshPath,
                importedFileTicks);
        }

        private GpuUniforms BuildUniforms(KnobProject? project, ModelNode? modelNode, float referenceRadius, Size viewportDip)
        {
            float renderScale = GetRenderScale();
            float viewportWidthPx = MathF.Max(1f, (float)viewportDip.Width * renderScale);
            float viewportHeightPx = MathF.Max(1f, (float)viewportDip.Height * renderScale);
            return BuildUniformsForPixels(project, modelNode, referenceRadius, viewportWidthPx, viewportHeightPx);
        }

        private GpuUniforms BuildUniformsForPixels(
            KnobProject? project,
            ModelNode? modelNode,
            float referenceRadius,
            float viewportWidthPx,
            float viewportHeightPx)
        {
            GetCameraBasis(out Vector3 right, out Vector3 up, out Vector3 forward);

            float scaleX = (2f * _zoom) / MathF.Max(1f, viewportWidthPx);
            float scaleY = (2f * _zoom) / MathF.Max(1f, viewportHeightPx);
            float scaleZ = scaleX;
            float offsetX = (2f * _panPx.X) / MathF.Max(1f, viewportWidthPx);
            float offsetY = (-2f * _panPx.Y) / MathF.Max(1f, viewportHeightPx);

            float radius = MathF.Max(1f, referenceRadius);
            Vector3 cameraPos = -forward * (radius * 6f);

            MaterialNode? materialNode = modelNode?.Children.OfType<MaterialNode>().FirstOrDefault();
            Vector3 baseColor = materialNode?.BaseColor ?? new Vector3(0.55f, 0.16f, 0.16f);
            float metallic = Math.Clamp(materialNode?.Metallic ?? 0f, 0f, 1f);
            float roughness = Math.Clamp(materialNode?.Roughness ?? 0.5f, 0.04f, 1f);
            float pearlescence = Math.Clamp(materialNode?.Pearlescence ?? 0f, 0f, 1f);
            float rustAmount = Math.Clamp(materialNode?.RustAmount ?? 0f, 0f, 1f);
            float wearAmount = Math.Clamp(materialNode?.WearAmount ?? 0f, 0f, 1f);
            float gunkAmount = Math.Clamp(materialNode?.GunkAmount ?? 0f, 0f, 1f);
            float diffuseStrength = materialNode?.DiffuseStrength ?? 1f;
            float specularStrength = materialNode?.SpecularStrength ?? 1f;
            float brushStrength = Math.Clamp(materialNode?.RadialBrushStrength ?? 0f, 0f, 1f);
            float brushDensity = MathF.Max(1f, materialNode?.RadialBrushDensity ?? 56f);
            float surfaceCharacter = Math.Clamp(materialNode?.SurfaceCharacter ?? 0f, 0f, 1f);
            bool partMaterialsEnabled = materialNode?.PartMaterialsEnabled ?? false;
            Vector3 topBaseColor = materialNode?.TopBaseColor ?? baseColor;
            Vector3 bevelBaseColor = materialNode?.BevelBaseColor ?? baseColor;
            Vector3 sideBaseColor = materialNode?.SideBaseColor ?? baseColor;
            float topMetallic = partMaterialsEnabled
                ? Math.Clamp(materialNode?.TopMetallic ?? metallic, 0f, 1f)
                : metallic;
            float bevelMetallic = partMaterialsEnabled
                ? Math.Clamp(materialNode?.BevelMetallic ?? metallic, 0f, 1f)
                : metallic;
            float sideMetallic = partMaterialsEnabled
                ? Math.Clamp(materialNode?.SideMetallic ?? metallic, 0f, 1f)
                : metallic;
            float topRoughness = partMaterialsEnabled
                ? Math.Clamp(materialNode?.TopRoughness ?? roughness, 0.04f, 1f)
                : roughness;
            float bevelRoughness = partMaterialsEnabled
                ? Math.Clamp(materialNode?.BevelRoughness ?? roughness, 0.04f, 1f)
                : roughness;
            float sideRoughness = partMaterialsEnabled
                ? Math.Clamp(materialNode?.SideRoughness ?? roughness, 0.04f, 1f)
                : roughness;
            float indicatorEnabled = modelNode?.IndicatorEnabled == true ? 1f : 0f;
            float indicatorShape = (float)(modelNode?.IndicatorShape ?? IndicatorShape.Bar);
            float indicatorWidth = modelNode?.IndicatorWidthRatio ?? 0.06f;
            float indicatorLength = modelNode?.IndicatorLengthRatioTop ?? 0.28f;
            float indicatorPosition = modelNode?.IndicatorPositionRatio ?? 0.46f;
            float indicatorRoundness = modelNode?.IndicatorRoundness ?? 0f;
            Vector3 indicatorColor = modelNode?.IndicatorColor ?? new Vector3(0.97f, 0.96f, 0.92f);
            float indicatorColorBlend = modelNode?.IndicatorColorBlend ?? 1f;
            float turns = MathF.Max(1f, modelNode?.SpiralTurns ?? 220f);

            float modelRotationRadians = modelNode?.RotationRadians ?? 0f;
            float modelCos = MathF.Cos(modelRotationRadians);
            float modelSin = MathF.Sin(modelRotationRadians);
            float topScale = Math.Clamp(modelNode?.TopRadiusScale ?? 0.86f, 0.30f, 1.30f);
            float knobBaseRadius = MathF.Max(1f, modelNode?.Radius ?? radius);
            float knobTopRadius = knobBaseRadius * topScale;
            float spacingPx = (knobTopRadius / turns) * _zoom;
            float geometryKeep = SmoothStep(0.20f, 0.90f, spacingPx);
            float frontZ = (modelNode?.Height ?? (radius * 2f)) * 0.5f;

            GpuUniforms uniforms = default;
            uniforms.CameraPosAndReferenceRadius = new Vector4(cameraPos, radius);
            uniforms.RightAndScaleX = new Vector4(right, scaleX);
            uniforms.UpAndScaleY = new Vector4(up, scaleY);
            uniforms.ForwardAndScaleZ = new Vector4(forward, scaleZ);
            uniforms.ProjectionOffsetsAndLightCount = new Vector4(offsetX, offsetY, 0f, 0f);
            uniforms.MaterialBaseColorAndMetallic = new Vector4(baseColor, metallic);
            uniforms.MaterialRoughnessDiffuseSpecMode = new Vector4(roughness, diffuseStrength, specularStrength, (float)(project?.Mode ?? LightingMode.Both));
            uniforms.MaterialPartTopColorAndMetallic = new Vector4(topBaseColor, topMetallic);
            uniforms.MaterialPartBevelColorAndMetallic = new Vector4(bevelBaseColor, bevelMetallic);
            uniforms.MaterialPartSideColorAndMetallic = new Vector4(sideBaseColor, sideMetallic);
            uniforms.MaterialPartRoughnessAndEnable = new Vector4(
                topRoughness,
                bevelRoughness,
                sideRoughness,
                partMaterialsEnabled ? 1f : 0f);
            uniforms.MaterialSurfaceBrushParams = new Vector4(brushStrength, brushDensity, surfaceCharacter, geometryKeep);
            uniforms.WeatherParams = new Vector4(rustAmount, wearAmount, gunkAmount, Math.Clamp(project?.BrushDarkness ?? 0.58f, 0f, 1f));
            Vector3 scratchExposeColor = project?.ScratchExposeColor ?? new Vector3(0.88f, 0.88f, 0.90f);
            float scratchExposeMetallic = Math.Clamp(project?.ScratchExposeMetallic ?? 0.92f, 0f, 1f);
            uniforms.ScratchExposeColorAndStrength = new Vector4(scratchExposeColor, scratchExposeMetallic);
            uniforms.AdvancedMaterialParams = new Vector4(
                Math.Clamp(project?.ScratchExposeRoughness ?? 0.20f, 0.04f, 1f),
                Math.Clamp(project?.ClearCoatAmount ?? 0f, 0f, 1f),
                Math.Clamp(project?.ClearCoatRoughness ?? 0.18f, 0.04f, 1f),
                (project?.AnisotropyAngleDegrees ?? 0f) * (MathF.PI / 180f));
            uniforms.IndicatorParams0 = new Vector4(indicatorEnabled, indicatorShape, indicatorWidth, indicatorLength);
            // Keep top-cap/indicator normalization stable even when scene bounds grow (e.g. collar enabled).
            uniforms.IndicatorParams1 = new Vector4(indicatorRoundness, indicatorPosition, knobTopRadius, pearlescence);
            uniforms.IndicatorColorAndBlend = new Vector4(indicatorColor, indicatorColorBlend);
            if (project != null)
            {
                uniforms.MicroDetailParams = new Vector4(
                    project.SpiralNormalInfluenceEnabled ? 1f : 0f,
                    project.SpiralNormalLodFadeStart,
                    project.SpiralNormalLodFadeEnd,
                    project.SpiralRoughnessLodBoost);
            }
            else
            {
                uniforms.MicroDetailParams = new Vector4(1f, 0.55f, 2.4f, 0.20f);
            }

            if (project != null)
            {
                Vector3 envTop = project.EnvironmentTopColor;
                Vector3 envBottom = project.EnvironmentBottomColor;
                float envIntensity = MathF.Max(0f, project.EnvironmentIntensity);
                float envRoughMix = Math.Clamp(project.EnvironmentRoughnessMix, 0f, 1f);
                uniforms.EnvironmentTopColorAndIntensity = new Vector4(envTop, envIntensity);
                uniforms.EnvironmentBottomColorAndRoughnessMix = new Vector4(envBottom, envRoughMix);
            }
            else
            {
                uniforms.EnvironmentTopColorAndIntensity = new Vector4(0.12f, 0.12f, 0.13f, 1f);
                uniforms.EnvironmentBottomColorAndRoughnessMix = new Vector4(0.02f, 0.02f, 0.02f, 1f);
            }

            uniforms.ModelRotationCosSin = new Vector4(modelCos, modelSin, topScale, frontZ);
            uniforms.ShadowParams = Vector4.Zero;
            uniforms.ShadowColorAndOpacity = Vector4.Zero;
            uniforms.DebugBasisParams = new Vector4(
                (float)(project?.BasisDebug ?? BasisDebugMode.Off),
                Math.Clamp(project?.ScratchDepth ?? 0.30f, 0f, 1f),
                Math.Clamp(project?.PaintCoatMetallic ?? 0.02f, 0f, 1f),
                Math.Clamp(project?.PaintCoatRoughness ?? 0.56f, 0.04f, 1f));

            if (project != null)
            {
                int lightCount = Math.Min(project.Lights.Count, MaxGpuLights);
                uniforms.ProjectionOffsetsAndLightCount.Z = lightCount;

                for (int i = 0; i < lightCount; i++)
                {
                    KnobLight light = project.Lights[i];
                    Vector3 lightPos = ApplyLightOrientation(new Vector3(light.X, light.Y, light.Z));
                    Vector3 lightDir = ApplyLightOrientation(GetDirectionalVector(light));
                    if (lightDir.LengthSquared() > 1e-8f)
                    {
                        lightDir = Vector3.Normalize(lightDir);
                    }
                    else
                    {
                        lightDir = Vector3.UnitZ;
                    }

                    GpuLight packed = new()
                    {
                        PositionType = new Vector4(
                            lightPos,
                            light.Type == LightType.Directional ? 1f : 0f),
                        Direction = new Vector4(lightDir, 0f),
                        ColorIntensity = new Vector4(
                            light.Color.Red / 255f,
                            light.Color.Green / 255f,
                            light.Color.Blue / 255f,
                            MathF.Max(0f, light.Intensity)),
                        Params0 = new Vector4(
                            MathF.Max(0f, light.Falloff),
                            MathF.Max(0f, light.DiffuseBoost),
                            MathF.Max(0f, light.SpecularBoost),
                            MathF.Max(1f, light.SpecularPower))
                    };

                    SetGpuLight(ref uniforms, i, packed);
                }
            }

            return uniforms;
        }

        private static GpuUniforms BuildCollarUniforms(in GpuUniforms baseUniforms, CollarNode collarNode)
        {
            GpuUniforms uniforms = baseUniforms;
            uniforms.MaterialBaseColorAndMetallic = new Vector4(collarNode.BaseColor, collarNode.Metallic);
            uniforms.MaterialRoughnessDiffuseSpecMode.X = collarNode.Roughness;
            uniforms.MaterialRoughnessDiffuseSpecMode.Y = 1f;
            uniforms.MaterialRoughnessDiffuseSpecMode.Z = 1f;
            uniforms.MaterialPartTopColorAndMetallic = uniforms.MaterialBaseColorAndMetallic;
            uniforms.MaterialPartBevelColorAndMetallic = uniforms.MaterialBaseColorAndMetallic;
            uniforms.MaterialPartSideColorAndMetallic = uniforms.MaterialBaseColorAndMetallic;
            uniforms.MaterialPartRoughnessAndEnable = new Vector4(collarNode.Roughness, collarNode.Roughness, collarNode.Roughness, 0f);
            uniforms.MaterialSurfaceBrushParams = new Vector4(0f, 56f, 0f, 1f);
            uniforms.WeatherParams = new Vector4(
                Math.Clamp(collarNode.RustAmount, 0f, 1f),
                Math.Clamp(collarNode.WearAmount, 0f, 1f),
                Math.Clamp(collarNode.GunkAmount, 0f, 1f),
                baseUniforms.WeatherParams.W);
            uniforms.IndicatorParams0 = Vector4.Zero;
            uniforms.IndicatorParams1 = new Vector4(0f, 0f, 0f, Math.Clamp(collarNode.Pearlescence, 0f, 1f));
            uniforms.IndicatorColorAndBlend = Vector4.Zero;
            uniforms.MicroDetailParams.X = 0f;
            uniforms.MicroDetailParams.W = 0f;
            return uniforms;
        }

        private static void LogCollarState(string pass, CollarNode? collarNode, MetalMeshGpuResources? collarResources)
        {
            _ = pass;
            _ = collarNode;
            _ = collarResources;
        }

        private Vector3 ApplyLightOrientation(Vector3 value)
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

        private Vector3 ApplyGizmoDisplayOrientation(Vector3 value)
        {
            if (_gizmoInvertX)
            {
                value.X = -value.X;
            }

            if (_gizmoInvertY)
            {
                value.Y = -value.Y;
            }

            if (_gizmoInvertZ)
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

        private static void SetGpuLight(ref GpuUniforms uniforms, int index, in GpuLight light)
        {
            switch (index)
            {
                case 0:
                    uniforms.Light0 = light;
                    break;
                case 1:
                    uniforms.Light1 = light;
                    break;
                case 2:
                    uniforms.Light2 = light;
                    break;
                case 3:
                    uniforms.Light3 = light;
                    break;
                case 4:
                    uniforms.Light4 = light;
                    break;
                case 5:
                    uniforms.Light5 = light;
                    break;
                case 6:
                    uniforms.Light6 = light;
                    break;
                case 7:
                    uniforms.Light7 = light;
                    break;
            }
        }

        private IntPtr EnsureUniformUploadScratchBuffer(int requiredSize, bool paintStamp)
        {
            if (requiredSize <= 0)
            {
                return IntPtr.Zero;
            }

            ref IntPtr targetBuffer = ref (paintStamp
                ? ref _paintStampUniformUploadScratch
                : ref _gpuUniformUploadScratch);
            ref int targetSize = ref (paintStamp
                ? ref _paintStampUniformUploadScratchSize
                : ref _gpuUniformUploadScratchSize);

            if (targetBuffer != IntPtr.Zero && targetSize >= requiredSize)
            {
                return targetBuffer;
            }

            if (targetBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(targetBuffer);
            }

            targetBuffer = Marshal.AllocHGlobal(requiredSize);
            targetSize = requiredSize;
            return targetBuffer;
        }

        private void ReleaseUniformUploadScratchBuffers()
        {
            if (_gpuUniformUploadScratch != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_gpuUniformUploadScratch);
                _gpuUniformUploadScratch = IntPtr.Zero;
                _gpuUniformUploadScratchSize = 0;
            }

            if (_paintStampUniformUploadScratch != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_paintStampUniformUploadScratch);
                _paintStampUniformUploadScratch = IntPtr.Zero;
                _paintStampUniformUploadScratchSize = 0;
            }
        }

        private void UploadUniforms(IntPtr encoderPtr, in GpuUniforms uniforms)
        {
            if (encoderPtr == IntPtr.Zero)
            {
                return;
            }

            int uniformSize = Marshal.SizeOf<GpuUniforms>();
            IntPtr uniformPtr = EnsureUniformUploadScratchBuffer(uniformSize, paintStamp: false);
            if (uniformPtr == IntPtr.Zero)
            {
                return;
            }

            Marshal.StructureToPtr(uniforms, uniformPtr, false);
            ObjC.Void_objc_msgSend_IntPtr_UInt_UInt(
                encoderPtr,
                Selectors.SetVertexBytesLengthAtIndex,
                uniformPtr,
                (nuint)uniformSize,
                1);
            ObjC.Void_objc_msgSend_IntPtr_UInt_UInt(
                encoderPtr,
                Selectors.SetFragmentBytesLengthAtIndex,
                uniformPtr,
                (nuint)uniformSize,
                1);
        }

        private static readonly Vector2[] ShadowSampleKernel =
        {
            new(0.0f, 0.0f),
            new(0.285f, -0.192f),
            new(-0.247f, 0.208f),
            new(0.118f, 0.326f),
            new(-0.332f, -0.087f),
            new(0.402f, 0.094f),
            new(-0.116f, -0.375f),
            new(0.046f, 0.462f),
            new(-0.463f, 0.041f),
            new(0.353f, -0.323f),
            new(-0.294f, -0.334f),
            new(0.214f, 0.452f),
            new(-0.027f, -0.497f),
            new(0.492f, -0.028f),
            new(-0.438f, 0.238f),
            new(0.165f, -0.468f)
        };

    }
}
