using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using KnobForge.Core;
using KnobForge.Core.Scene;

namespace KnobForge.Rendering.GPU;

public static partial class OuroborosCollarMeshBuilder
{
    private static float ComputeBodyScaleRelief(
        int ring,
        int slice,
        int pathSegments,
        int crossSegments,
        int biteIndex,
        float localRadius)
    {
        float u = ((float)ring / Math.Max(1, pathSegments)) * MathF.Max(24f, pathSegments * 0.42f);
        float parityShift = (((int)MathF.Floor(u)) & 1) == 0 ? 0f : 0.5f;
        float v = ((float)slice / Math.Max(1, crossSegments)) * MathF.Max(10f, crossSegments * 0.95f) + parityShift;

        float fu = Fract(u) - 0.5f;
        float fv = Fract(v) - 0.5f;
        float nx = fu / 0.55f;
        float ny = fv / 0.42f;
        float d = MathF.Sqrt((nx * nx) + (ny * ny));
        float cell = Math.Clamp(1f - d, 0f, 1f);
        cell = cell * cell * (3f - (2f * cell));

        float seamU = 1f - SmoothStep(0.42f, 0.50f, MathF.Abs(fu));
        float seamV = 1f - SmoothStep(0.36f, 0.50f, MathF.Abs(fv));
        float seam = MathF.Max(seamU, seamV);

        int ringDelta = MinWrappedDistance(ring, biteIndex, pathSegments);
        float headFade = 1f - SmoothStep(0f, TotalReplacedRings * 1.2f, ringDelta);
        float bodyMask = 1f - headFade;

        float phi = ((float)slice / Math.Max(1, crossSegments)) * MathF.PI * 2f;
        float bellyMask = 0.68f + (0.32f * MathF.Abs(MathF.Sin(phi)));

        float bump = cell * 0.055f;
        float groove = seam * 0.022f;
        return localRadius * bodyMask * bellyMask * (bump - groove);
    }

    private static float Fract(float x)
    {
        return x - MathF.Floor(x);
    }

    private static int MinWrappedDistance(int a, int b, int count)
    {
        int d = Math.Abs(a - b);
        return Math.Min(d, Math.Max(0, count - d));
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

    private static float WrapSignedRadians(float radians)
    {
        float twoPi = MathF.PI * 2f;
        float r = radians % twoPi;
        if (r > MathF.PI)
        {
            r -= twoPi;
        }
        else if (r < -MathF.PI)
        {
            r += twoPi;
        }

        return r;
    }

    private static float Wrap01(float value)
    {
        float wrapped = value - MathF.Floor(value);
        if (wrapped < 0f)
        {
            wrapped += 1f;
        }

        return wrapped;
    }
}
