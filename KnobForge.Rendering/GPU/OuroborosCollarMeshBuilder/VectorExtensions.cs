using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace KnobForge.Rendering.GPU;

internal static class VectorExtensions
{
    public static Vector3 XYZ(this Vector4 value)
    {
        return new Vector3(value.X, value.Y, value.Z);
    }
}
