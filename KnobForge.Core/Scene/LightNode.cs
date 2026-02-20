using KnobForge.Core;
using System.Numerics;

namespace KnobForge.Core.Scene
{
    public sealed class LightNode : SceneNode
    {
        public KnobLight Light { get; }
        public Vector3 Position
        {
            get => new Vector3(Light.X, Light.Y, Light.Z);
            set
            {
                Light.X = value.X;
                Light.Y = value.Y;
                Light.Z = value.Z;
            }
        }

        public LightNode(KnobLight light)
            : base(light.Name)
        {
            Light = light;
        }
    }
}
