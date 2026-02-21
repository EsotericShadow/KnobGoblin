namespace KnobForge.App.Controls
{
    public sealed partial class MetalViewport
    {
        private const string PaintPickShaderSource = @"
#include <metal_stdlib>
using namespace metal;

struct MetalVertex
{
    packed_float3 position;
    packed_float3 normal;
    packed_float4 tangent;
};

struct PaintPickUniform
{
    float4 cameraPosAndReferenceRadius;
    float4 rightAndScaleX;
    float4 upAndScaleY;
    float4 forwardAndScaleZ;
    float4 projectionOffsetsAndObjectId;
    float4 modelRotationCosSin;
};

struct PaintPickVertexOut
{
    float4 position [[position]];
    float2 uv;
    float objectId;
};

vertex PaintPickVertexOut vertex_paint_pick(
    uint vertexId [[vertex_id]],
    const device MetalVertex* vertices [[buffer(0)]],
    constant PaintPickUniform& uniforms [[buffer(1)]])
{
    MetalVertex v = vertices[vertexId];
    float3 localPos = float3(v.position);
    float cosA = uniforms.modelRotationCosSin.x;
    float sinA = uniforms.modelRotationCosSin.y;

    float3 worldPos = float3(
        localPos.x * cosA - localPos.y * sinA,
        localPos.x * sinA + localPos.y * cosA,
        localPos.z);

    float clipX = dot(worldPos, uniforms.rightAndScaleX.xyz) * uniforms.rightAndScaleX.w + uniforms.projectionOffsetsAndObjectId.x;
    float clipY = dot(worldPos, uniforms.upAndScaleY.xyz) * uniforms.upAndScaleY.w + uniforms.projectionOffsetsAndObjectId.y;
    float cameraDepth = dot(worldPos - uniforms.cameraPosAndReferenceRadius.xyz, uniforms.forwardAndScaleZ.xyz);
    float nearPlane = max(0.05, uniforms.cameraPosAndReferenceRadius.w * 0.5);
    float farPlane = max(nearPlane + 1.0, uniforms.cameraPosAndReferenceRadius.w * 14.0);
    float depth01 = clamp((cameraDepth - nearPlane) / (farPlane - nearPlane), 0.0, 1.0);
    float clipZ = clamp(depth01, 0.001, 0.999);

    float referenceRadius = max(1.0, uniforms.cameraPosAndReferenceRadius.w);
    float2 uv = localPos.xy / max(referenceRadius * 2.0, 1e-4) + 0.5;

    PaintPickVertexOut outVertex;
    outVertex.position = float4(clipX, clipY, clipZ, 1.0);
    outVertex.uv = uv;
    outVertex.objectId = uniforms.projectionOffsetsAndObjectId.z;
    return outVertex;
}

fragment float4 fragment_paint_pick(PaintPickVertexOut inVertex [[stage_in]])
{
    if (any(inVertex.uv < float2(0.0)) || any(inVertex.uv > float2(1.0)))
    {
        discard_fragment();
    }

    float2 uv = clamp(inVertex.uv, float2(0.0), float2(1.0));
    return float4(uv.x, uv.y, inVertex.objectId, 1.0);
}
";

        private const string PaintMaskStampShaderSource = @"
#include <metal_stdlib>
using namespace metal;

struct PaintStampUniform
{
    float4 centerRadiusOpacity;
    float4 params0;
    float4 params1;
};

struct PaintStampVertexOut
{
    float4 position [[position]];
    float2 uv;
};

static inline float Hash21(float2 p, float seed)
{
    float n = sin(dot(p, float2(127.1, 311.7)) + seed * 31.337);
    return fract(n * 43758.5453);
}

static inline float SmoothInside(float edge0, float edge1, float x)
{
    return 1.0 - smoothstep(edge0, edge1, x);
}

static inline float ComputeCircleWeight(float dist)
{
    if (dist >= 1.0) return 0.0;
    return SmoothInside(0.86, 1.0, dist);
}

static inline float ComputeStrokeWeight(float dist)
{
    if (dist >= 1.0) return 0.0;
    return pow(max(0.0, 1.0 - dist), 0.55);
}

static inline float ComputeSquareWeight(float ax, float ay)
{
    float edge = max(ax, ay);
    if (edge >= 1.0) return 0.0;
    return SmoothInside(0.86, 1.0, edge);
}

static inline float ComputeSprayWeight(float dist, float spread, float seed, float2 uv)
{
    if (dist >= 1.0) return 0.0;
    float noise = Hash21(uv * 4096.0, seed);
    float keepThreshold = 0.90 + ((0.20 - 0.90) * spread);
    if (noise > keepThreshold) return 0.0;
    return (1.0 - dist) * (0.45 + (noise * 0.55));
}

static inline float ComputeSplatWeight(float2 n, float dist, float spread, float seed, float2 uv)
{
    if (dist >= 1.35) return 0.0;
    float radialNoise = Hash21(uv * 3072.0 + float2(13.1, 27.9), seed + 0.17);
    float angularNoise = Hash21(uv * 2048.0 + float2(7.3, 19.7), seed + 0.73);
    float splatBoundary = 1.0 + ((radialNoise - 0.5) * (0.25 + (0.55 * spread)));
    float angularWarp = 1.0 + ((angularNoise - 0.5) * (0.10 + (0.35 * spread)));
    float warpedDist = dist / max(0.3, splatBoundary * angularWarp);
    if (warpedDist >= 1.0) return 0.0;
    float core = 1.0 - warpedDist;
    float lobes = 0.82 + (0.18 * sin((n.x * 5.1) + (n.y * 4.3)));
    return clamp(core * lobes, 0.0, 1.0);
}

static inline float ComputeScratchNeedleWeight(float dist)
{
    if (dist >= 1.0) return 0.0;
    float core = 1.0 - dist;
    return pow(core, 1.35);
}

static inline float ComputeScratchChiselWeight(float dist)
{
    if (dist >= 1.0) return 0.0;
    float plateau = SmoothInside(0.58, 1.0, dist);
    return pow(clamp(plateau, 0.0, 1.0), 0.72);
}

static inline float ComputeScratchBurrWeight(float2 n, float dist, float spread, float seed, float2 uv)
{
    if (dist >= 1.22) return 0.0;
    float radialNoise = Hash21(uv * 5120.0 + float2(5.1, 8.7), seed + 0.29);
    float angularNoise = Hash21(uv * 4096.0 + float2(11.3, 3.9), seed + 0.61);
    float boundary = 0.78 + (radialNoise * (0.24 + (0.34 * spread)));
    float warpedDist = dist / max(0.28, boundary);
    if (warpedDist >= 1.0) return 0.0;
    float core = 1.0 - warpedDist;
    float tooth = 0.68 + (0.32 * sin((n.x * 10.7) + (n.y * 9.3) + (angularNoise * 6.2831853)));
    float micro = 0.35 + (0.65 * angularNoise);
    return clamp(core * tooth * micro, 0.0, 1.0);
}

static inline float ComputeScratchScuffWeight(float dist, float spread, float seed, float2 uv)
{
    if (dist >= 1.0) return 0.0;
    float grain = Hash21(uv * 4096.0 + float2(17.0, 31.0), seed + 0.11);
    float keepThreshold = 0.98 + ((0.42 - 0.98) * spread);
    if (grain > keepThreshold) return 0.0;
    float soft = SmoothInside(0.32, 1.0, dist);
    return soft * (0.55 + (0.45 * grain));
}

static inline float ComputeBrushWeight(int brushType, float2 n, float dist, float spread, float seed, float2 uv)
{
    float ax = abs(n.x);
    float ay = abs(n.y);
    switch (brushType)
    {
        case 1: return ComputeStrokeWeight(dist);
        case 2: return ComputeCircleWeight(dist);
        case 3: return ComputeSquareWeight(ax, ay);
        case 4: return ComputeSplatWeight(n, dist, spread, seed, uv);
        default: return ComputeSprayWeight(dist, spread, seed, uv);
    }
}

static inline float ComputeScratchWeight(int abrasionType, float2 n, float dist, float spread, float seed, float2 uv)
{
    switch (abrasionType)
    {
        case 1: return ComputeScratchChiselWeight(dist);
        case 2: return ComputeScratchBurrWeight(n, dist, spread, seed, uv);
        case 3: return ComputeScratchScuffWeight(dist, spread, seed, uv);
        default: return ComputeScratchNeedleWeight(dist);
    }
}

vertex PaintStampVertexOut vertex_paint_stamp(uint vertexId [[vertex_id]])
{
    float2 clipPositions[3] = {
        float2(-1.0, -1.0),
        float2( 3.0, -1.0),
        float2(-1.0,  3.0)
    };

    PaintStampVertexOut outVertex;
    float2 clip = clipPositions[min(vertexId, 2u)];
    outVertex.position = float4(clip, 0.0, 1.0);
    outVertex.uv = clip * 0.5 + 0.5;
    return outVertex;
}

fragment float4 fragment_paint_stamp(
    PaintStampVertexOut inVertex [[stage_in]],
    constant PaintStampUniform& uniforms [[buffer(0)]])
{
    float2 center = uniforms.centerRadiusOpacity.xy;
    float radius = max(uniforms.centerRadiusOpacity.z, 1e-6);
    float opacity = clamp(uniforms.centerRadiusOpacity.w, 0.0, 1.0);
    float spread = clamp(uniforms.params0.x, 0.0, 1.0);
    int brushType = int(round(uniforms.params0.y));
    int abrasionType = int(round(uniforms.params0.z));
    int channel = int(round(uniforms.params0.w));
    float seed = uniforms.params1.x;
    float3 paintColor = clamp(uniforms.params1.yzw, float3(0.0), float3(1.0));

    float2 n = (inVertex.uv - center) / radius;
    float dist = length(n);
    float weight = (channel == 3)
        ? ComputeScratchWeight(abrasionType, n, dist, spread, seed, inVertex.uv)
        : ComputeBrushWeight(brushType, n, dist, spread, seed, inVertex.uv);
    if (weight <= 1e-6) return float4(0.0, 0.0, 0.0, 0.0);

    float alpha = clamp(opacity * weight, 0.0, 1.0);
    if (channel == 3)
    {
        alpha = clamp(alpha * 1.70, 0.0, 1.0);
    }

    if (channel == 4)
    {
        return float4(0.0, 0.0, 0.0, alpha);
    }

    if (channel == 5)
    {
        return float4(paintColor, alpha);
    }

    return float4(1.0, 1.0, 1.0, alpha);
}
";

        private const string LightGizmoOverlayShaderSource = @"
#include <metal_stdlib>
using namespace metal;

#define MAX_LIGHTS 8

struct GizmoLight
{
    float4 positionAndOrigin;
    float4 directionTipAndRadii;
    float4 tipRadiusAndAlpha;
    float4 color;
};

struct GizmoUniform
{
    float4 viewportAndCount;
    GizmoLight lights[MAX_LIGHTS];
};

struct GizmoVertexOut
{
    float4 position [[position]];
    float2 uv;
};

static inline float SegmentDistance(float2 p, float2 a, float2 b)
{
    float2 ab = b - a;
    float denom = max(dot(ab, ab), 1e-5);
    float t = clamp(dot(p - a, ab) / denom, 0.0, 1.0);
    return distance(p, a + (ab * t));
}

static inline float CircleMask(float2 p, float2 center, float radius, float feather)
{
    float d = distance(p, center);
    float inner = max(0.0, radius - feather);
    return 1.0 - smoothstep(inner, radius + feather, d);
}

static inline float RingMask(float2 p, float2 center, float radius, float thickness, float feather)
{
    float d = distance(p, center);
    float outer = 1.0 - smoothstep(radius + thickness - feather, radius + thickness + feather, d);
    float inner = smoothstep(max(0.0, radius - thickness - feather), max(0.0, radius - thickness + feather), d);
    return clamp(outer * inner, 0.0, 1.0);
}

vertex GizmoVertexOut vertex_light_gizmo_overlay(uint vertexId [[vertex_id]])
{
    float2 clipPositions[3] = {
        float2(-1.0, -1.0),
        float2( 3.0, -1.0),
        float2(-1.0,  3.0)
    };

    GizmoVertexOut outVertex;
    float2 clip = clipPositions[min(vertexId, 2u)];
    outVertex.position = float4(clip, 0.0, 1.0);
    outVertex.uv = clip * 0.5 + 0.5;
    return outVertex;
}

fragment float4 fragment_light_gizmo_overlay(
    GizmoVertexOut inVertex [[stage_in]],
    constant GizmoUniform& uniforms [[buffer(0)]])
{
    float2 viewport = max(float2(1.0, 1.0), uniforms.viewportAndCount.xy);
    float2 p = inVertex.uv * viewport;
    int lightCount = min(MAX_LIGHTS, int(round(uniforms.viewportAndCount.z)));

    float4 outColor = float4(0.0);
    for (int i = 0; i < lightCount; i++)
    {
        GizmoLight light = uniforms.lights[i];
        float2 position = light.positionAndOrigin.xy;
        float2 origin = light.positionAndOrigin.zw;
        float2 directionTip = light.directionTipAndRadii.xy;
        float radius = max(0.5, light.directionTipAndRadii.z);
        float selectedRingRadius = max(radius + 1.0, light.directionTipAndRadii.w);
        float directionTipRadius = max(0.75, light.tipRadiusAndAlpha.x);
        float hasDirectionTip = light.tipRadiusAndAlpha.y;
        float fillAlpha = clamp(light.tipRadiusAndAlpha.z, 0.0, 1.0);
        float lineAlpha = clamp(light.tipRadiusAndAlpha.w, 0.0, 1.0);
        float3 lightColor = clamp(light.color.rgb, 0.0, 1.0);
        float selected = clamp(light.color.a, 0.0, 1.0);

        float line = 1.0 - smoothstep(0.8, 1.9, SegmentDistance(p, origin, position));
        float circle = CircleMask(p, position, radius, 1.3);
        float mainAlpha = max(circle * fillAlpha, line * lineAlpha);

        if (hasDirectionTip > 0.5)
        {
            float tipLine = 1.0 - smoothstep(0.8, 1.9, SegmentDistance(p, position, directionTip));
            float tipDot = CircleMask(p, directionTip, directionTipRadius, 1.0);
            mainAlpha = max(mainAlpha, tipLine * lineAlpha);
            mainAlpha = max(mainAlpha, tipDot * fillAlpha);
        }

        float ring = RingMask(p, position, selectedRingRadius, 1.15, 1.0) * selected;
        float ringAlpha = ring * 0.95;
        float3 ringColor = float3(0.90, 0.93, 0.96);

        float alpha = clamp(mainAlpha + ringAlpha, 0.0, 1.0);
        if (alpha <= 1e-5)
        {
            continue;
        }

        float3 rgb = (lightColor * mainAlpha) + (ringColor * ringAlpha);
        rgb /= max(alpha, 1e-5);

        outColor.rgb = (rgb * alpha) + (outColor.rgb * (1.0 - alpha));
        outColor.a = alpha + (outColor.a * (1.0 - alpha));
    }

    return outColor;
}
";

    }
}
