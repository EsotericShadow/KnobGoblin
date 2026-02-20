using System;
using System.Numerics;

namespace KnobForge.Core.Scene
{
    public enum MaterialRegionTarget
    {
        WholeKnob = 0,
        TopCap = 1,
        Bevel = 2,
        Side = 3
    }

    public sealed class MaterialNode : SceneNode
    {
        private float _metallic = 1f;
        private float _roughness = 0.04f;
        private float _pearlescence = 0f;
        private float _rustAmount = 0f;
        private float _wearAmount = 0f;
        private float _gunkAmount = 0f;
        private float _radialBrushStrength = 0.65f;
        private float _radialBrushDensity = 280.5f;
        private float _surfaceCharacter = 1f;
        private bool _partMaterialsEnabled;
        private float _topMetallic = 1f;
        private float _topRoughness = 0.04f;
        private float _bevelMetallic = 1f;
        private float _bevelRoughness = 0.04f;
        private float _sideMetallic = 1f;
        private float _sideRoughness = 0.04f;

        public Vector3 BaseColor { get; set; } = new Vector3(0.55f, 0.16f, 0.16f);
        public Vector3 TopBaseColor { get; set; } = new Vector3(0.55f, 0.16f, 0.16f);
        public Vector3 BevelBaseColor { get; set; } = new Vector3(0.55f, 0.16f, 0.16f);
        public Vector3 SideBaseColor { get; set; } = new Vector3(0.55f, 0.16f, 0.16f);
        public bool PartMaterialsEnabled
        {
            get => _partMaterialsEnabled;
            set => _partMaterialsEnabled = value;
        }
        public float TopMetallic
        {
            get => _topMetallic;
            set => _topMetallic = Math.Clamp(value, 0f, 1f);
        }
        public float TopRoughness
        {
            get => _topRoughness;
            set => _topRoughness = Math.Clamp(value, 0.04f, 1f);
        }
        public float BevelMetallic
        {
            get => _bevelMetallic;
            set => _bevelMetallic = Math.Clamp(value, 0f, 1f);
        }
        public float BevelRoughness
        {
            get => _bevelRoughness;
            set => _bevelRoughness = Math.Clamp(value, 0.04f, 1f);
        }
        public float SideMetallic
        {
            get => _sideMetallic;
            set => _sideMetallic = Math.Clamp(value, 0f, 1f);
        }
        public float SideRoughness
        {
            get => _sideRoughness;
            set => _sideRoughness = Math.Clamp(value, 0.04f, 1f);
        }
        public float Metallic
        {
            get => _metallic;
            set => _metallic = Math.Clamp(value, 0f, 1f);
        }
        public float Roughness
        {
            get => _roughness;
            set => _roughness = Math.Clamp(value, 0.04f, 1f);
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
        public float RadialBrushStrength
        {
            get => _radialBrushStrength;
            set => _radialBrushStrength = Math.Clamp(value, 0f, 1f);
        }
        public float RadialBrushDensity
        {
            get => _radialBrushDensity;
            set => _radialBrushDensity = Math.Clamp(value, 4f, 320f);
        }
        public float SurfaceCharacter
        {
            get => _surfaceCharacter;
            set => _surfaceCharacter = Math.Clamp(value, 0f, 1f);
        }
        public float SpecularPower { get; set; } = 64f;
        public float DiffuseStrength { get; set; } = 1.0f;
        public float SpecularStrength { get; set; } = 1.0f;

        public MaterialNode(string name = "Material")
            : base(name)
        {
        }

        public void SyncPartMaterialsFromGlobal()
        {
            TopBaseColor = BaseColor;
            BevelBaseColor = BaseColor;
            SideBaseColor = BaseColor;
            TopMetallic = Metallic;
            BevelMetallic = Metallic;
            SideMetallic = Metallic;
            TopRoughness = Roughness;
            BevelRoughness = Roughness;
            SideRoughness = Roughness;
        }
    }
}
