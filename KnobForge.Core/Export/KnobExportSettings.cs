namespace KnobForge.Core.Export
{
    public enum SpritesheetLayout
    {
        Horizontal,
        Grid
    }

    public enum ExportFilterPreset
    {
        None,
        Neutral,
        Cinematic,
        HighContrast,
        Matte
    }

    public sealed class KnobExportSettings
    {
        public int FrameCount { get; set; } = 360;
        public int Resolution { get; set; } = 512;
        public int SupersampleScale { get; set; } = 2;
        public SpritesheetLayout SpritesheetLayout { get; set; } = SpritesheetLayout.Horizontal;
        public float Padding { get; set; } = 0f;
        public ExportFilterPreset FilterPreset { get; set; } = ExportFilterPreset.None;
        public bool ExportIndividualFrames { get; set; } = true;
        public bool ExportSpritesheet { get; set; }
        public float CameraDistanceScale { get; set; } = 6f;
    }
}
