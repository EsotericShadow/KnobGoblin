using KnobForge.Core.Scene;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace KnobForge.Core
{
    public enum LightType
    {
        Point,
        Directional
    }

    public enum LightingMode
    {
        Realistic,
        Artistic,
        Both
    }

    public enum ShadowLightMode
    {
        Selected,
        Dominant,
        Weighted
    }

    public enum BasisDebugMode
    {
        Off = 0,
        Normal = 1,
        Tangent = 2,
        Bitangent = 3
    }

    public enum PaintBrushType
    {
        Spray,
        Stroke,
        Circle,
        Square,
        Splat
    }

    public enum ScratchAbrasionType
    {
        Needle,
        Chisel,
        Burr,
        Scuff
    }

    public enum PaintChannel
    {
        Rust = 0,
        Wear = 1,
        Gunk = 2,
        Scratch = 3,
        Erase = 4,
        Color = 5
    }

    public sealed class KnobLight
    {
        public string Name { get; set; } = "Light";
        public LightType Type { get; set; } = LightType.Point;
        public float X { get; set; } = 0f;
        public float Y { get; set; } = 0f;
        public float Z { get; set; } = 0f;
        public float DirectionRadians { get; set; } = 0f;
        public SKColor Color { get; set; } = SKColors.White;
        public float Intensity { get; set; } = 1.0f;
        public float Falloff { get; set; } = 1.0f;
        public float DiffuseBoost { get; set; } = 1.0f;
        public float SpecularBoost { get; set; } = 1.0f;
        public float SpecularPower { get; set; } = 64f;
    }

    public class KnobProject
    {
        public const int DefaultPaintMaskSize = 1024;
        private float _spiralNormalLodFadeStart = 4.22f;
        private float _spiralNormalLodFadeEnd = 4.23f;
        private float _spiralRoughnessLodBoost = 0.78f;
        private float _shadowStrength = 1.0f;
        private float _shadowSoftness = 0.55f;
        private float _shadowDistance = 1.0f;
        private float _shadowScale = 1.0f;
        private float _shadowQuality = 0.65f;
        private float _shadowGray = 0.14f;
        private float _shadowDiffuseInfluence = 1.0f;
        private bool _brushPaintingEnabled;
        private PaintBrushType _brushType = PaintBrushType.Spray;
        private PaintChannel _brushChannel = PaintChannel.Rust;
        private ScratchAbrasionType _scratchAbrasionType = ScratchAbrasionType.Needle;
        private float _brushSizePx = 32f;
        private float _brushOpacity = 0.50f;
        private float _brushSpread = 0.35f;
        private float _brushDarkness = 0.58f;
        private float _paintCoatMetallic = 0.02f;
        private float _paintCoatRoughness = 0.56f;
        private Vector3 _paintColor = new(0.85f, 0.24f, 0.24f);
        private Vector3 _scratchExposeColor = new(0.88f, 0.88f, 0.90f);
        private float _scratchWidthPx = 20f;
        private float _scratchDepth = 0.45f;
        private float _scratchDragResistance = 0.38f;
        private float _scratchDepthRamp = 0.0015f;
        private readonly byte[] _paintMaskRgba8 = new byte[DefaultPaintMaskSize * DefaultPaintMaskSize * 4];
        private int _paintMaskVersion = 1;

        public SKBitmap BaseTexture { get; private set; }
        public int Width => BaseTexture.Width;
        public int Height => BaseTexture.Height;
        public LightingMode Mode { get; set; } = LightingMode.Both;
        public Vector3 EnvironmentTopColor { get; set; } = new(0.34f, 0.36f, 0.37f);
        public Vector3 EnvironmentBottomColor { get; set; } = new(0f, 0f, 0f);
        public float EnvironmentIntensity { get; set; } = 0.36f;
        public float EnvironmentRoughnessMix { get; set; } = 1.0f;
        public BasisDebugMode BasisDebug { get; set; } = BasisDebugMode.Off;
        public bool ShadowsEnabled { get; set; } = true;
        public ShadowLightMode ShadowMode { get; set; } = ShadowLightMode.Weighted;
        public float ShadowStrength
        {
            get => _shadowStrength;
            set => _shadowStrength = Math.Clamp(value, 0f, 2.5f);
        }
        public float ShadowSoftness
        {
            get => _shadowSoftness;
            set => _shadowSoftness = Math.Clamp(value, 0f, 1f);
        }
        public float ShadowDistance
        {
            get => _shadowDistance;
            set => _shadowDistance = Math.Clamp(value, 0f, 2.5f);
        }
        public float ShadowScale
        {
            get => _shadowScale;
            set => _shadowScale = Math.Clamp(value, 0.7f, 1.6f);
        }
        public float ShadowQuality
        {
            get => _shadowQuality;
            set => _shadowQuality = Math.Clamp(value, 0f, 1f);
        }
        public float ShadowGray
        {
            get => _shadowGray;
            set => _shadowGray = Math.Clamp(value, 0f, 0.6f);
        }
        public float ShadowDiffuseInfluence
        {
            get => _shadowDiffuseInfluence;
            set => _shadowDiffuseInfluence = Math.Clamp(value, 0f, 2f);
        }
        public bool SpiralNormalInfluenceEnabled { get; set; } = true;
        public bool BrushPaintingEnabled
        {
            get => _brushPaintingEnabled;
            set => _brushPaintingEnabled = value;
        }
        public PaintBrushType BrushType
        {
            get => _brushType;
            set => _brushType = value;
        }
        public PaintChannel BrushChannel
        {
            get => _brushChannel;
            set => _brushChannel = value;
        }
        public ScratchAbrasionType ScratchAbrasionType
        {
            get => _scratchAbrasionType;
            set => _scratchAbrasionType = value;
        }
        public float BrushSizePx
        {
            get => _brushSizePx;
            set => _brushSizePx = Math.Clamp(value, 1f, 320f);
        }
        public float BrushOpacity
        {
            get => _brushOpacity;
            set => _brushOpacity = Math.Clamp(value, 0f, 1f);
        }
        public float BrushSpread
        {
            get => _brushSpread;
            set => _brushSpread = Math.Clamp(value, 0f, 1f);
        }
        public float BrushDarkness
        {
            get => _brushDarkness;
            set => _brushDarkness = Math.Clamp(value, 0f, 1f);
        }
        public float PaintCoatMetallic
        {
            get => _paintCoatMetallic;
            set => _paintCoatMetallic = Math.Clamp(value, 0f, 1f);
        }
        public float PaintCoatRoughness
        {
            get => _paintCoatRoughness;
            set => _paintCoatRoughness = Math.Clamp(value, 0.04f, 1f);
        }
        public Vector3 PaintColor
        {
            get => _paintColor;
            set => _paintColor = new Vector3(
                Math.Clamp(value.X, 0f, 1f),
                Math.Clamp(value.Y, 0f, 1f),
                Math.Clamp(value.Z, 0f, 1f));
        }
        public Vector3 ScratchExposeColor
        {
            get => _scratchExposeColor;
            set => _scratchExposeColor = new Vector3(
                Math.Clamp(value.X, 0f, 1f),
                Math.Clamp(value.Y, 0f, 1f),
                Math.Clamp(value.Z, 0f, 1f));
        }
        public float ScratchWidthPx
        {
            get => _scratchWidthPx;
            set => _scratchWidthPx = Math.Clamp(value, 1f, 320f);
        }
        public float ScratchDepth
        {
            get => _scratchDepth;
            set => _scratchDepth = Math.Clamp(value, 0f, 1f);
        }
        public float ScratchDragResistance
        {
            get => _scratchDragResistance;
            set => _scratchDragResistance = Math.Clamp(value, 0f, 0.98f);
        }
        public float ScratchDepthRamp
        {
            get => _scratchDepthRamp;
            set => _scratchDepthRamp = Math.Clamp(value, 0f, 0.02f);
        }
        public int PaintMaskSize => DefaultPaintMaskSize;
        public int PaintMaskVersion => _paintMaskVersion;
        public float SpiralNormalLodFadeStart
        {
            get => _spiralNormalLodFadeStart;
            set
            {
                _spiralNormalLodFadeStart = Math.Clamp(value, 0.1f, 10f);
                if (_spiralNormalLodFadeEnd < _spiralNormalLodFadeStart + 0.01f)
                {
                    _spiralNormalLodFadeEnd = _spiralNormalLodFadeStart + 0.01f;
                }
            }
        }
        public float SpiralNormalLodFadeEnd
        {
            get => _spiralNormalLodFadeEnd;
            set => _spiralNormalLodFadeEnd = Math.Max(SpiralNormalLodFadeStart + 0.01f, Math.Clamp(value, 0.1f, 12f));
        }
        public float SpiralRoughnessLodBoost
        {
            get => _spiralRoughnessLodBoost;
            set => _spiralRoughnessLodBoost = Math.Clamp(value, 0f, 1f);
        }
        public SceneRootNode SceneRoot { get; } = new SceneRootNode();
        public SceneNode? SelectedNode { get; private set; }
        public List<KnobLight> Lights { get; } = new List<KnobLight>();
        public int SelectedLightIndex { get; private set; } = -1;
        public KnobLight? SelectedLight =>
            SelectedLightIndex >= 0 && SelectedLightIndex < Lights.Count
                ? Lights[SelectedLightIndex]
                : null;

        public KnobProject(string? path = null)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                BaseTexture = SKBitmap.Decode(path);
            }
            else
            {
                // Fallback: A Red Circle so you know it's working
                BaseTexture = new SKBitmap(512, 512);
                using (var canvas = new SKCanvas(BaseTexture))
                {
                    canvas.Clear(SKColors.Transparent);
                    var paint = new SKPaint { Color = SKColors.DarkRed, IsAntialias = true };
                    canvas.DrawCircle(256, 256, 200, paint);
                }
            }

            var light1 = AddLight(757f, 761f, -180f);
            light1.Type = LightType.Point;
            light1.Intensity = 2.71f;
            light1.Falloff = 1.0f;
            light1.DiffuseBoost = 1.0f;
            light1.SpecularBoost = 1.0f;
            light1.SpecularPower = 64.0f;

            var light2 = AddLight(-536f, 486f, -233f);
            light2.Type = LightType.Point;
            light2.Intensity = 3.0f;
            light2.Falloff = 0.71f;
            light2.DiffuseBoost = 1.0f;
            light2.SpecularBoost = 1.0f;
            light2.SpecularPower = 64.0f;

            var light3 = AddLight(483f, -568f, -1303f);
            light3.Type = LightType.Point;
            light3.Intensity = 2.27f;
            light3.Falloff = 1.0f;
            light3.DiffuseBoost = 1.0f;
            light3.SpecularBoost = 1.0f;
            light3.SpecularPower = 64.0f;

            var light4 = AddLight(-710f, 791f, -715f);
            light4.Type = LightType.Point;
            light4.Intensity = 1.11f;
            light4.Falloff = 0.30f;
            light4.DiffuseBoost = 1.0f;
            light4.SpecularBoost = 1.0f;
            light4.SpecularPower = 64.0f;

            SetSelectedLightIndex(0);
            var modelNode = new KnobForge.Core.Scene.ModelNode("KnobModel");
            SceneRoot.AddChild(modelNode);
            string meshyRingPath = CollarNode.ResolveImportedMeshPath(CollarPreset.MeshyOuroborosRing, null);
            string meshyRingTexturedPath = CollarNode.ResolveImportedMeshPath(CollarPreset.MeshyOuroborosRingTextured, null);
            string legacyImportedStlPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "KnobForge",
                "ouroboros.stl");

            CollarPreset defaultPreset = CollarPreset.SnakeOuroboros;
            string defaultImportedMeshPath = string.Empty;
            if (File.Exists(meshyRingPath))
            {
                defaultPreset = CollarPreset.MeshyOuroborosRing;
                defaultImportedMeshPath = meshyRingPath;
            }
            else if (File.Exists(meshyRingTexturedPath))
            {
                defaultPreset = CollarPreset.MeshyOuroborosRingTextured;
                defaultImportedMeshPath = meshyRingTexturedPath;
            }
            else if (File.Exists(legacyImportedStlPath))
            {
                defaultPreset = CollarPreset.ImportedStl;
                defaultImportedMeshPath = legacyImportedStlPath;
            }

            bool hasImportedMesh = defaultPreset != CollarPreset.SnakeOuroboros;
            var collarNode = new KnobForge.Core.Scene.CollarNode("SnakeOuroborosCollar")
            {
                Enabled = true,
                Preset = hasImportedMesh ? defaultPreset : CollarPreset.SnakeOuroboros,
                ImportedMeshPath = defaultImportedMeshPath,
                ImportedScale = 1.09f,
                ImportedBodyLengthScale = 1.00f,
                ImportedBodyThicknessScale = 0.79f,
                ImportedHeadLengthScale = 1.00f,
                ImportedHeadThicknessScale = 0.79f,
                ImportedRotationRadians = MathF.PI,
                ImportedOffsetXRatio = -0.13f,
                ImportedOffsetYRatio = 0.24f,
                ImportedInflateRatio = 0.00f,
                BaseColor = new Vector3(0.31f, 0.08f, 0.07f),
                Metallic = 1.00f,
                Roughness = 0.46f,
                Pearlescence = 0.00f
            };
            modelNode.AddChild(collarNode);
            var materialNode = new KnobForge.Core.Scene.MaterialNode("DefaultMaterial");
            modelNode.AddChild(materialNode);
            SetSelectedNode(modelNode);
        }

        public void SetSelectedNode(SceneNode? node)
        {
            SelectedNode = node;
        }

        public KnobLight AddLight(float x = 0f, float y = 0f, float z = 0f)
        {
            var light = new KnobLight
            {
                Name = $"Light {Lights.Count + 1}",
                X = x,
                Y = y,
                Z = z,
                Color = Lights.Count == 0 ? SKColors.White : SKColors.Wheat,
                Intensity = 1.0f,
                Falloff = 1.0f,
                DiffuseBoost = 1.0f,
                SpecularBoost = 1.0f,
                SpecularPower = 64f
            };
            Lights.Add(light);
            SelectedLightIndex = Lights.Count - 1;
            SceneRoot.AddChild(new KnobForge.Core.Scene.LightNode(light));
            return light;
        }

        public bool RemoveSelectedLight()
        {
            if (Lights.Count <= 1 || SelectedLightIndex < 0 || SelectedLightIndex >= Lights.Count)
            {
                return false;
            }

            var removedLight = Lights[SelectedLightIndex];
            Lights.RemoveAt(SelectedLightIndex);
            var nodeToRemove = SceneRoot.Children
                .OfType<KnobForge.Core.Scene.LightNode>()
                .FirstOrDefault(n => n.Light == removedLight);

            if (nodeToRemove != null)
            {
                SceneRoot.RemoveChild(nodeToRemove);
            }
            if (SelectedLightIndex >= Lights.Count)
            {
                SelectedLightIndex = Lights.Count - 1;
            }

            return true;
        }

        public bool SetSelectedLightIndex(int index)
        {
            if (index < 0 || index >= Lights.Count)
            {
                return false;
            }

            SelectedLightIndex = index;
            return true;
        }

        public void EnsureSelection()
        {
            if (Lights.Count == 0)
            {
                SelectedLightIndex = -1;
                return;
            }

            if (SelectedLightIndex < 0 || SelectedLightIndex >= Lights.Count)
            {
                SelectedLightIndex = 0;
            }
        }

        public byte[] GetPaintMaskRgba8()
        {
            return _paintMaskRgba8;
        }

        public void ClearPaintMask()
        {
            Array.Clear(_paintMaskRgba8, 0, _paintMaskRgba8.Length);
            _paintMaskVersion++;
        }

        public Vector4 SamplePaintMaskBilinear(float u, float v)
        {
            float uc = Math.Clamp(u, 0f, 1f);
            float vc = Math.Clamp(v, 0f, 1f);
            int size = DefaultPaintMaskSize;
            float x = uc * (size - 1);
            float y = vc * (size - 1);
            int x0 = (int)MathF.Floor(x);
            int y0 = (int)MathF.Floor(y);
            int x1 = Math.Min(x0 + 1, size - 1);
            int y1 = Math.Min(y0 + 1, size - 1);
            float tx = x - x0;
            float ty = y - y0;

            Vector4 n00 = ReadMaskRgba(x0, y0);
            Vector4 n10 = ReadMaskRgba(x1, y0);
            Vector4 n01 = ReadMaskRgba(x0, y1);
            Vector4 n11 = ReadMaskRgba(x1, y1);
            Vector4 nx0 = Vector4.Lerp(n00, n10, tx);
            Vector4 nx1 = Vector4.Lerp(n01, n11, tx);
            return Vector4.Lerp(nx0, nx1, ty);
        }

        public bool StampPaintMaskUv(
            Vector2 uvCenter,
            float uvRadius,
            PaintBrushType brushType,
            ScratchAbrasionType scratchAbrasionType,
            PaintChannel channel,
            float opacity,
            float spread,
            uint seed)
        {
            if (uvRadius <= 1e-6f || opacity <= 1e-6f)
            {
                return false;
            }

            int size = DefaultPaintMaskSize;
            float radiusPx = MathF.Max(0.5f, uvRadius * size);
            int xMin = Math.Clamp((int)MathF.Floor((uvCenter.X * size) - radiusPx - 1f), 0, size - 1);
            int xMax = Math.Clamp((int)MathF.Ceiling((uvCenter.X * size) + radiusPx + 1f), 0, size - 1);
            int yMin = Math.Clamp((int)MathF.Floor((uvCenter.Y * size) - radiusPx - 1f), 0, size - 1);
            int yMax = Math.Clamp((int)MathF.Ceiling((uvCenter.Y * size) + radiusPx + 1f), 0, size - 1);
            float invRadius = 1f / MathF.Max(1e-6f, uvRadius);

            bool changed = false;
            float spreadClamped = Math.Clamp(spread, 0f, 1f);
            float opacityClamped = Math.Clamp(opacity, 0f, 1f);

            for (int y = yMin; y <= yMax; y++)
            {
                float py = ((y + 0.5f) / size - uvCenter.Y) * invRadius;
                for (int x = xMin; x <= xMax; x++)
                {
                    float px = ((x + 0.5f) / size - uvCenter.X) * invRadius;
                    float weight = channel == PaintChannel.Scratch
                        ? ComputeScratchWeight(scratchAbrasionType, px, py, spreadClamped, seed, x, y)
                        : ComputeBrushWeight(brushType, px, py, spreadClamped, seed, x, y);
                    if (weight <= 1e-6f)
                    {
                        continue;
                    }

                    float alpha = Math.Clamp(opacityClamped * weight, 0f, 1f);
                    if (channel == PaintChannel.Scratch)
                    {
                        alpha = Math.Clamp(alpha * 1.70f, 0f, 1f);
                    }
                    if (alpha <= 1e-6f)
                    {
                        continue;
                    }

                    int idx = ((y * size) + x) * 4;
                    if (channel == PaintChannel.Erase)
                    {
                        changed |= LerpByte(ref _paintMaskRgba8[idx + 0], 0, alpha);
                        changed |= LerpByte(ref _paintMaskRgba8[idx + 1], 0, alpha);
                        changed |= LerpByte(ref _paintMaskRgba8[idx + 2], 0, alpha);
                        changed |= LerpByte(ref _paintMaskRgba8[idx + 3], 0, alpha);
                    }
                    else
                    {
                        if (channel == PaintChannel.Color)
                        {
                            // CPU paint-mask fallback has no dedicated color texture.
                            continue;
                        }

                        int channelIndex = channel switch
                        {
                            PaintChannel.Rust => 0,
                            PaintChannel.Wear => 1,
                            PaintChannel.Gunk => 2,
                            PaintChannel.Scratch => 3,
                            _ => -1
                        };
                        if (channelIndex >= 0)
                        {
                            changed |= LerpByte(ref _paintMaskRgba8[idx + channelIndex], 255, alpha);
                        }
                    }
                }
            }

            if (changed)
            {
                _paintMaskVersion++;
            }

            return changed;
        }

        private Vector4 ReadMaskRgba(int x, int y)
        {
            int idx = ((y * DefaultPaintMaskSize) + x) * 4;
            return new Vector4(
                _paintMaskRgba8[idx + 0] / 255f,
                _paintMaskRgba8[idx + 1] / 255f,
                _paintMaskRgba8[idx + 2] / 255f,
                _paintMaskRgba8[idx + 3] / 255f);
        }

        private static bool LerpByte(ref byte target, byte toValue, float alpha)
        {
            byte before = target;
            float blended = before + ((toValue - before) * alpha);
            byte after = (byte)Math.Clamp((int)MathF.Round(blended), 0, 255);
            target = after;
            return after != before;
        }

        private static float ComputeBrushWeight(
            PaintBrushType brushType,
            float xNorm,
            float yNorm,
            float spread,
            uint seed,
            int px,
            int py)
        {
            float ax = MathF.Abs(xNorm);
            float ay = MathF.Abs(yNorm);
            float dist = MathF.Sqrt((xNorm * xNorm) + (yNorm * yNorm));

            return brushType switch
            {
                PaintBrushType.Square => ComputeSquareWeight(ax, ay),
                PaintBrushType.Spray => ComputeSprayWeight(dist, spread, seed, px, py),
                PaintBrushType.Splat => ComputeSplatWeight(xNorm, yNorm, dist, spread, seed, px, py),
                PaintBrushType.Stroke => ComputeStrokeWeight(dist),
                _ => ComputeCircleWeight(dist)
            };
        }

        private static float ComputeScratchWeight(
            ScratchAbrasionType abrasionType,
            float xNorm,
            float yNorm,
            float spread,
            uint seed,
            int px,
            int py)
        {
            float dist = MathF.Sqrt((xNorm * xNorm) + (yNorm * yNorm));
            return abrasionType switch
            {
                ScratchAbrasionType.Chisel => ComputeScratchChiselWeight(dist),
                ScratchAbrasionType.Burr => ComputeScratchBurrWeight(xNorm, yNorm, dist, spread, seed, px, py),
                ScratchAbrasionType.Scuff => ComputeScratchScuffWeight(dist, spread, seed, px, py),
                _ => ComputeScratchNeedleWeight(dist)
            };
        }

        private static float ComputeCircleWeight(float dist)
        {
            if (dist >= 1f)
            {
                return 0f;
            }

            return 1f - SmoothStep(0.86f, 1f, dist);
        }

        private static float ComputeScratchNeedleWeight(float dist)
        {
            if (dist >= 1f)
            {
                return 0f;
            }

            float core = 1f - dist;
            return MathF.Pow(core, 1.35f);
        }

        private static float ComputeScratchChiselWeight(float dist)
        {
            if (dist >= 1f)
            {
                return 0f;
            }

            float plateau = 1f - SmoothStep(0.58f, 1f, dist);
            return MathF.Pow(Math.Clamp(plateau, 0f, 1f), 0.72f);
        }

        private static float ComputeScratchBurrWeight(
            float xNorm,
            float yNorm,
            float dist,
            float spread,
            uint seed,
            int px,
            int py)
        {
            if (dist >= 1.22f)
            {
                return 0f;
            }

            float radialNoise = Hash01((uint)(px * 5 + 17), (uint)(py * 9 + 29), seed ^ 0xA17F9D3Bu);
            float angularNoise = Hash01((uint)(px * 13 + 43), (uint)(py * 7 + 61), seed ^ 0xD1B54A32u);
            float boundary = 0.78f + (radialNoise * (0.24f + (0.34f * spread)));
            float warpedDist = dist / MathF.Max(0.28f, boundary);
            if (warpedDist >= 1f)
            {
                return 0f;
            }

            float core = 1f - warpedDist;
            float tooth = 0.68f + (0.32f * MathF.Sin((xNorm * 10.7f) + (yNorm * 9.3f) + (angularNoise * 6.28318f)));
            float micro = 0.35f + (0.65f * angularNoise);
            return Math.Clamp(core * tooth * micro, 0f, 1f);
        }

        private static float ComputeScratchScuffWeight(float dist, float spread, uint seed, int px, int py)
        {
            if (dist >= 1f)
            {
                return 0f;
            }

            float grain = Hash01((uint)(px * 3 + 5), (uint)(py * 7 + 11), seed ^ 0x9E3779B9u);
            float keepThreshold = 0.98f + ((0.42f - 0.98f) * spread);
            if (grain > keepThreshold)
            {
                return 0f;
            }

            float soft = 1f - SmoothStep(0.32f, 1f, dist);
            return soft * (0.55f + (0.45f * grain));
        }

        private static float ComputeStrokeWeight(float dist)
        {
            if (dist >= 1f)
            {
                return 0f;
            }

            return MathF.Pow(1f - dist, 0.55f);
        }

        private static float ComputeSquareWeight(float ax, float ay)
        {
            float edge = MathF.Max(ax, ay);
            if (edge >= 1f)
            {
                return 0f;
            }

            return 1f - SmoothStep(0.86f, 1f, edge);
        }

        private static float ComputeSprayWeight(float dist, float spread, uint seed, int px, int py)
        {
            if (dist >= 1f)
            {
                return 0f;
            }

            float noise = Hash01((uint)px, (uint)py, seed);
            float keepThreshold = 0.90f + ((0.20f - 0.90f) * spread);
            if (noise > keepThreshold)
            {
                return 0f;
            }

            return (1f - dist) * (0.45f + (noise * 0.55f));
        }

        private static float ComputeSplatWeight(float xNorm, float yNorm, float dist, float spread, uint seed, int px, int py)
        {
            if (dist >= 1.35f)
            {
                return 0f;
            }

            float radialNoise = Hash01((uint)(px * 7 + 31), (uint)(py * 11 + 19), seed ^ 0xA5A5A5A5u);
            float angularNoise = Hash01((uint)(px * 13 + 23), (uint)(py * 17 + 47), seed ^ 0x3C6EF372u);
            float splatBoundary = 1f + ((radialNoise - 0.5f) * (0.25f + (0.55f * spread)));
            float angularWarp = 1f + ((angularNoise - 0.5f) * (0.10f + (0.35f * spread)));
            float warpedDist = dist / MathF.Max(0.3f, splatBoundary * angularWarp);
            if (warpedDist >= 1f)
            {
                return 0f;
            }

            float core = 1f - warpedDist;
            float lobes = 0.82f + (0.18f * MathF.Sin((xNorm * 5.1f) + (yNorm * 4.3f)));
            return Math.Clamp(core * lobes, 0f, 1f);
        }

        private static float Hash01(uint x, uint y, uint seed)
        {
            unchecked
            {
                uint h = x * 374761393u;
                h += y * 668265263u;
                h ^= seed + 0x9E3779B9u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0x00FFFFFFu) / 16777215f;
            }
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
    }
}
