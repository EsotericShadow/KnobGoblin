using System;
using System.IO;
using System.Numerics;

namespace KnobForge.Core.Scene
{
    public enum CollarPreset
    {
        None,
        SnakeOuroboros,
        ImportedStl,
        MeshyOuroborosRing,
        MeshyOuroborosRingTextured
    }

    public sealed class CollarNode : SceneNode
    {
        private const string MeshyOuroborosRingFileName = "Meshy_AI_ouroboros_ring_0220001100_generate.glb";
        private const string MeshyOuroborosRingTexturedFileName = "Meshy_AI_ouroboros_ring_0220001505_texture.glb";
        private float _innerRadiusRatio = 1.03f;
        private float _gapToKnobRatio = 0.025f;
        private float _elevationRatio = 0f;
        private float _overallRotationRadians = 0f;
        private float _biteAngleRadians = MathF.PI * 0.34f;
        private float _bodyRadiusRatio = 0.115f;
        private float _bodyEllipseYScale = 0.86f;
        private float _neckTaper = 0.22f;
        private float _tailTaper = 0.34f;
        private float _massBias = 0.18f;
        private float _tailUnderlap = 0.22f;
        private float _headScale = 1.0f;
        private float _jawBulge = 0.24f;
        private int _pathSegments = 320;
        private int _crossSegments = 26;
        private float _uvSeamOffset = 0f;
        private Vector3 _baseColor = new(0.74f, 0.74f, 0.70f);
        private float _metallic = 0.96f;
        private float _roughness = 0.32f;
        private float _pearlescence = 0f;
        private float _rustAmount = 0f;
        private float _wearAmount = 0f;
        private float _gunkAmount = 0f;
        private float _normalStrength = 1.0f;
        private float _heightStrength = 0.45f;
        private float _scaleDensity = 0.65f;
        private float _scaleRelief = 0.38f;
        private string _importedMeshPath = string.Empty;
        private float _importedScale = 1.0f;
        private float _importedRotationRadians = 0f;
        private bool _importedMirrorX = false;
        private bool _importedMirrorY = false;
        private bool _importedMirrorZ = false;
        private float _importedHeadAngleOffsetRadians = 0f;
        private float _importedOffsetXRatio = 0f;
        private float _importedOffsetYRatio = 0f;
        private float _importedInflateRatio = 0f;
        private float _importedBodyLengthScale = 1.0f;
        private float _importedBodyThicknessScale = 1.0f;
        private float _importedHeadLengthScale = 1.0f;
        private float _importedHeadThicknessScale = 1.0f;

        public CollarNode(string name = "SnakeOuroborosCollar")
            : base(name)
        {
        }

        public bool Enabled { get; set; } = false;

        public CollarPreset Preset { get; set; } = CollarPreset.SnakeOuroboros;

        public static bool IsImportedMeshPreset(CollarPreset preset)
        {
            return preset == CollarPreset.ImportedStl ||
                   preset == CollarPreset.MeshyOuroborosRing ||
                   preset == CollarPreset.MeshyOuroborosRingTextured;
        }

        public static string ResolveImportedMeshPath(CollarPreset preset, string? customPath)
        {
            return preset switch
            {
                CollarPreset.MeshyOuroborosRing => Path.Combine(GetDesktopKnobForgeDirectory(), MeshyOuroborosRingFileName),
                CollarPreset.MeshyOuroborosRingTextured => Path.Combine(GetDesktopKnobForgeDirectory(), MeshyOuroborosRingTexturedFileName),
                CollarPreset.ImportedStl => customPath?.Trim() ?? string.Empty,
                _ => string.Empty
            };
        }

        private static string GetDesktopKnobForgeDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "KnobForge");
        }

        public float InnerRadiusRatio
        {
            get => _innerRadiusRatio;
            set => _innerRadiusRatio = Math.Clamp(value, 0.65f, 1.90f);
        }

        public float GapToKnobRatio
        {
            get => _gapToKnobRatio;
            set => _gapToKnobRatio = Math.Clamp(value, 0f, 0.40f);
        }

        public float ElevationRatio
        {
            get => _elevationRatio;
            set => _elevationRatio = Math.Clamp(value, -1.50f, 1.50f);
        }

        public float OverallRotationRadians
        {
            get => _overallRotationRadians;
            set => _overallRotationRadians = value;
        }

        public float BiteAngleRadians
        {
            get => _biteAngleRadians;
            set => _biteAngleRadians = value;
        }

        public float BodyRadiusRatio
        {
            get => _bodyRadiusRatio;
            set => _bodyRadiusRatio = Math.Clamp(value, 0.03f, 0.35f);
        }

        public float BodyEllipseYScale
        {
            get => _bodyEllipseYScale;
            set => _bodyEllipseYScale = Math.Clamp(value, 0.45f, 1.35f);
        }

        public float NeckTaper
        {
            get => _neckTaper;
            set => _neckTaper = Math.Clamp(value, 0f, 0.95f);
        }

        public float TailTaper
        {
            get => _tailTaper;
            set => _tailTaper = Math.Clamp(value, 0f, 0.95f);
        }

        public float MassBias
        {
            get => _massBias;
            set => _massBias = Math.Clamp(value, -1f, 1f);
        }

        public float TailUnderlap
        {
            get => _tailUnderlap;
            set => _tailUnderlap = Math.Clamp(value, 0f, 1f);
        }

        public float HeadScale
        {
            get => _headScale;
            set => _headScale = Math.Clamp(value, 0.40f, 2.20f);
        }

        public float JawBulge
        {
            get => _jawBulge;
            set => _jawBulge = Math.Clamp(value, 0f, 1f);
        }

        // When enabled, keep the UV seam anchored to the bite region by default.
        public bool UvSeamFollowBite { get; set; } = true;

        public float UvSeamOffset
        {
            get => _uvSeamOffset;
            set => _uvSeamOffset = Math.Clamp(value, 0f, 1f);
        }

        public int PathSegments
        {
            get => _pathSegments;
            set => _pathSegments = Math.Clamp(value, 64, 2048);
        }

        public int CrossSegments
        {
            get => _crossSegments;
            set => _crossSegments = Math.Clamp(value, 8, 256);
        }

        public Vector3 BaseColor
        {
            get => _baseColor;
            set => _baseColor = new Vector3(
                Math.Clamp(value.X, 0f, 1f),
                Math.Clamp(value.Y, 0f, 1f),
                Math.Clamp(value.Z, 0f, 1f));
        }

        public float Metallic
        {
            get => _metallic;
            set => _metallic = Math.Clamp(value, 0f, 1f);
        }

        public float Roughness
        {
            get => _roughness;
            set => _roughness = Math.Clamp(value, 0.02f, 1f);
        }

        public float Pearlescence
        {
            get => _pearlescence;
            set => _pearlescence = Math.Clamp(value, 0f, 1f);
        }

        public float RustAmount
        {
            get => _rustAmount;
            set => _rustAmount = Math.Clamp(value, 0f, 1f);
        }

        public float WearAmount
        {
            get => _wearAmount;
            set => _wearAmount = Math.Clamp(value, 0f, 1f);
        }

        public float GunkAmount
        {
            get => _gunkAmount;
            set => _gunkAmount = Math.Clamp(value, 0f, 1f);
        }

        public float NormalStrength
        {
            get => _normalStrength;
            set => _normalStrength = Math.Clamp(value, 0f, 3f);
        }

        public float HeightStrength
        {
            get => _heightStrength;
            set => _heightStrength = Math.Clamp(value, 0f, 2f);
        }

        public float ScaleDensity
        {
            get => _scaleDensity;
            set => _scaleDensity = Math.Clamp(value, 0f, 2f);
        }

        public float ScaleRelief
        {
            get => _scaleRelief;
            set => _scaleRelief = Math.Clamp(value, 0f, 2f);
        }

        public string ImportedMeshPath
        {
            get => _importedMeshPath;
            set => _importedMeshPath = value?.Trim() ?? string.Empty;
        }

        public float ImportedScale
        {
            get => _importedScale;
            set => _importedScale = Math.Clamp(value, 0.05f, 8f);
        }

        public float ImportedRotationRadians
        {
            get => _importedRotationRadians;
            set => _importedRotationRadians = value;
        }

        public bool ImportedMirrorX
        {
            get => _importedMirrorX;
            set => _importedMirrorX = value;
        }

        public bool ImportedMirrorY
        {
            get => _importedMirrorY;
            set => _importedMirrorY = value;
        }

        public bool ImportedMirrorZ
        {
            get => _importedMirrorZ;
            set => _importedMirrorZ = value;
        }

        // Local head-region angular offset used for imported STL deformation masks.
        // This is independent from ImportedRotationRadians (global mesh rotation).
        public float ImportedHeadAngleOffsetRadians
        {
            get => _importedHeadAngleOffsetRadians;
            set => _importedHeadAngleOffsetRadians = value;
        }

        // In-plane imported mesh offset relative to knob radius.
        public float ImportedOffsetXRatio
        {
            get => _importedOffsetXRatio;
            set => _importedOffsetXRatio = Math.Clamp(value, -2f, 2f);
        }

        // In-plane imported mesh offset relative to knob radius.
        public float ImportedOffsetYRatio
        {
            get => _importedOffsetYRatio;
            set => _importedOffsetYRatio = Math.Clamp(value, -2f, 2f);
        }

        public float ImportedInflateRatio
        {
            get => _importedInflateRatio;
            set => _importedInflateRatio = Math.Clamp(value, -0.35f, 0.35f);
        }

        // Scales ring circumference/body sweep while preserving head anchor.
        public float ImportedBodyLengthScale
        {
            get => _importedBodyLengthScale;
            set => _importedBodyLengthScale = Math.Clamp(value, 0.6f, 2.4f);
        }

        // Scales body cross-section thickness while preserving head anchor.
        public float ImportedBodyThicknessScale
        {
            get => _importedBodyThicknessScale;
            set => _importedBodyThicknessScale = Math.Clamp(value, 0.5f, 2.5f);
        }

        // Scales the head projection along the ring while preserving head anchor alignment.
        public float ImportedHeadLengthScale
        {
            get => _importedHeadLengthScale;
            set => _importedHeadLengthScale = Math.Clamp(value, 0.5f, 2.5f);
        }

        // Scales local head volume/thickness independent from body thickness.
        public float ImportedHeadThicknessScale
        {
            get => _importedHeadThicknessScale;
            set => _importedHeadThicknessScale = Math.Clamp(value, 0.5f, 2.8f);
        }
    }
}
