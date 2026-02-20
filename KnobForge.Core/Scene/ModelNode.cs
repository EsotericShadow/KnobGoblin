using System;
using System.Numerics;

namespace KnobForge.Core.Scene
{
    public enum GripType
    {
        None,
        VerticalFlutes,
        DiamondKnurl,
        SquareKnurl,
        HexKnurl
    }

    public enum GripStyle
    {
        BrutalIndustrial,
        BoutiqueSynthPremium,
        VintageBakeliteEra,
        ModernEurorackCoarse
    }

    public enum BodyStyle
    {
        Straight,
        Waisted,
        Barrel,
        Stepped
    }

    public enum MountPreset
    {
        Custom,
        SetScrewQuarterInch,
        PushOnSixMmSpline,
        DShaftSixMm,
        FineSpline24,
        ColletSixMm
    }

    public enum IndicatorShape
    {
        Bar,
        Tapered,
        Capsule,
        Needle,
        Triangle,
        Diamond,
        Dot
    }

    public enum IndicatorRelief
    {
        Inset,
        Extrude
    }

    public enum IndicatorProfile
    {
        Straight,
        Rounded,
        Convex,
        Concave
    }

    public enum ReferenceKnobStyle
    {
        Custom,
        BossFlutedPedal,
        MxrHeptagonPedal,
        EhxSmoothDomePedal,
        SslChannelStrip,
        SslMonitorLarge,
        StratTeleBell,
        GibsonSpeed,
        BrushedAluminumPremium
    }

    public sealed class ModelNode : SceneNode
    {
        private float _spiralRidgeHeight = 19.89f;
        private float _spiralRidgeWidth = 18.92f;
        private float _spiralRidgeHeightVariance = 0.15f;
        private float _spiralRidgeWidthVariance = 0.12f;
        private float _spiralHeightVarianceThreshold = 0.45f;
        private float _spiralWidthVarianceThreshold = 0.45f;
        private float _spiralTurns = 150.0f;
        private float _crownProfile = 0f;
        private float _bevelCurve = 1f;
        private float _bodyTaper = 0f;
        private float _bodyBulge = 0f;
        private float _gripStart = 0.15f;
        private float _gripHeight = 0.55f;
        private float _gripDensity = 60f;
        private float _gripPitch = 6f;
        private float _gripDepth = 1.2f;
        private float _gripWidth = 1.2f;
        private float _gripSharpness = 1.0f;
        private float _boreRadiusRatio = 0f;
        private float _boreDepthRatio = 0f;
        private float _collarWidthRatio = 0f;
        private float _collarHeightRatio = 0f;
        private float _indicatorGrooveDepthRatio = 0f;
        private float _indicatorGrooveWidthDegrees = 7f;
        private float _indicatorRadiusRatio = 0.70f;
        private float _indicatorLengthRatio = 0.22f;
        private bool _indicatorEnabled = true;
        private float _indicatorWidthRatio = 0.06f;
        private float _indicatorLengthRatioTop = 0.28f;
        private float _indicatorPositionRatio = 0.46f;
        private float _indicatorThicknessRatio = 0.012f;
        private float _indicatorRoundness = 0f;
        private Vector3 _indicatorColor = new(0.97f, 0.96f, 0.92f);
        private float _indicatorColorBlend = 1f;
        private bool _indicatorCadWallsEnabled = true;

        public float Radius { get; set; } = 220f;
        public float Height { get; set; } = 120f;
        public float Bevel { get; set; } = 18f;
        public float TopRadiusScale { get; set; } = 0.86f;
        public int RadialSegments { get; set; } = 180;
        public float RotationRadians { get; set; } = 0f;
        public float SpiralRidgeHeight
        {
            get => _spiralRidgeHeight;
            set => _spiralRidgeHeight = Math.Clamp(value, 0f, 24f);
        }
        public float SpiralRidgeWidth
        {
            get => _spiralRidgeWidth;
            set => _spiralRidgeWidth = Math.Clamp(value, 0.4f, 30f);
        }
        public float SpiralRidgeHeightVariance
        {
            get => _spiralRidgeHeightVariance;
            set => _spiralRidgeHeightVariance = Math.Clamp(value, 0f, 1f);
        }
        public float SpiralRidgeWidthVariance
        {
            get => _spiralRidgeWidthVariance;
            set => _spiralRidgeWidthVariance = Math.Clamp(value, 0f, 1f);
        }
        public float SpiralHeightVarianceThreshold
        {
            get => _spiralHeightVarianceThreshold;
            set => _spiralHeightVarianceThreshold = Math.Clamp(value, 0f, 1f);
        }
        public float SpiralWidthVarianceThreshold
        {
            get => _spiralWidthVarianceThreshold;
            set => _spiralWidthVarianceThreshold = Math.Clamp(value, 0f, 1f);
        }
        public float SpiralTurns
        {
            get => _spiralTurns;
            set => _spiralTurns = Math.Clamp(value, 1f, 600f);
        }

        public float CrownProfile
        {
            get => _crownProfile;
            set => _crownProfile = Math.Clamp(value, -1f, 1f);
        }

        public float BevelCurve
        {
            get => _bevelCurve;
            set => _bevelCurve = Math.Clamp(value, 0.4f, 3f);
        }

        public float BodyTaper
        {
            get => _bodyTaper;
            set => _bodyTaper = Math.Clamp(value, -0.35f, 0.35f);
        }

        public float BodyBulge
        {
            get => _bodyBulge;
            set => _bodyBulge = Math.Clamp(value, -0.35f, 0.35f);
        }

        public GripType GripType { get; set; } = GripType.None;
        public GripStyle GripStyle { get; set; } = GripStyle.BoutiqueSynthPremium;
        public BodyStyle BodyStyle { get; set; } = BodyStyle.Straight;
        public ReferenceKnobStyle ReferenceStyle { get; set; } = ReferenceKnobStyle.Custom;
        public MountPreset MountPreset { get; set; } = MountPreset.Custom;
        public IndicatorShape IndicatorShape { get; set; } = IndicatorShape.Bar;
        public IndicatorRelief IndicatorRelief { get; set; } = IndicatorRelief.Extrude;
        public IndicatorProfile IndicatorProfile { get; set; } = IndicatorProfile.Straight;

        public float GripStart
        {
            get => _gripStart;
            set => _gripStart = Math.Clamp(value, 0f, 1f);
        }

        public float GripHeight
        {
            get => _gripHeight;
            set => _gripHeight = Math.Clamp(value, 0.05f, 1f);
        }

        public float GripDensity
        {
            get => _gripDensity;
            set => _gripDensity = Math.Clamp(value, 4f, 320f);
        }

        public float GripPitch
        {
            get => _gripPitch;
            set => _gripPitch = Math.Clamp(value, 0.2f, 24f);
        }

        public float GripDepth
        {
            get => _gripDepth;
            set => _gripDepth = Math.Clamp(value, 0f, 20f);
        }

        public float GripWidth
        {
            get => _gripWidth;
            set => _gripWidth = Math.Clamp(value, 0.05f, 3f);
        }

        public float GripSharpness
        {
            get => _gripSharpness;
            set => _gripSharpness = Math.Clamp(value, 0.5f, 8f);
        }

        public float BoreRadiusRatio
        {
            get => _boreRadiusRatio;
            set => _boreRadiusRatio = Math.Clamp(value, 0f, 0.45f);
        }

        public float BoreDepthRatio
        {
            get => _boreDepthRatio;
            set => _boreDepthRatio = Math.Clamp(value, 0f, 0.45f);
        }

        public float CollarWidthRatio
        {
            get => _collarWidthRatio;
            set => _collarWidthRatio = Math.Clamp(value, 0f, 0.20f);
        }

        public float CollarHeightRatio
        {
            get => _collarHeightRatio;
            set => _collarHeightRatio = Math.Clamp(value, 0f, 0.10f);
        }

        public float IndicatorGrooveDepthRatio
        {
            get => _indicatorGrooveDepthRatio;
            set => _indicatorGrooveDepthRatio = Math.Clamp(value, 0f, 0.08f);
        }

        public float IndicatorGrooveWidthDegrees
        {
            get => _indicatorGrooveWidthDegrees;
            set => _indicatorGrooveWidthDegrees = Math.Clamp(value, 1f, 40f);
        }

        public float IndicatorRadiusRatio
        {
            get => _indicatorRadiusRatio;
            set => _indicatorRadiusRatio = Math.Clamp(value, 0.10f, 0.98f);
        }

        public float IndicatorLengthRatio
        {
            get => _indicatorLengthRatio;
            set => _indicatorLengthRatio = Math.Clamp(value, 0.02f, 0.60f);
        }

        public bool IndicatorEnabled
        {
            get => _indicatorEnabled;
            set => _indicatorEnabled = value;
        }

        public float IndicatorWidthRatio
        {
            get => _indicatorWidthRatio;
            set => _indicatorWidthRatio = Math.Clamp(value, 0.005f, 0.35f);
        }

        public float IndicatorLengthRatioTop
        {
            get => _indicatorLengthRatioTop;
            set => _indicatorLengthRatioTop = Math.Clamp(value, 0.05f, 0.80f);
        }

        public float IndicatorPositionRatio
        {
            get => _indicatorPositionRatio;
            set => _indicatorPositionRatio = Math.Clamp(value, 0.05f, 0.90f);
        }

        public float IndicatorThicknessRatio
        {
            get => _indicatorThicknessRatio;
            set => _indicatorThicknessRatio = Math.Clamp(value, 0f, 0.08f);
        }

        public float IndicatorRoundness
        {
            get => _indicatorRoundness;
            set => _indicatorRoundness = Math.Clamp(value, 0f, 1f);
        }

        public Vector3 IndicatorColor
        {
            get => _indicatorColor;
            set => _indicatorColor = new Vector3(
                Math.Clamp(value.X, 0f, 1f),
                Math.Clamp(value.Y, 0f, 1f),
                Math.Clamp(value.Z, 0f, 1f));
        }

        public float IndicatorColorBlend
        {
            get => _indicatorColorBlend;
            set => _indicatorColorBlend = Math.Clamp(value, 0f, 1f);
        }

        public bool IndicatorCadWallsEnabled
        {
            get => _indicatorCadWallsEnabled;
            set => _indicatorCadWallsEnabled = value;
        }

        public ModelNode(string name = "Model")
            : base(name)
        {
        }
    }
}
