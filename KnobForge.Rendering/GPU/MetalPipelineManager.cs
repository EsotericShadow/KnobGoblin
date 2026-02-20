using System;
using System.IO;
using System.Runtime.InteropServices;

namespace KnobForge.Rendering.GPU;

public sealed class MetalPipelineManager
{
    private static readonly Lazy<MetalPipelineManager> InstanceFactory =
        new(static () => new MetalPipelineManager(MetalRendererContext.Instance));

    private const string VertexFunctionName = "vertex_main";
    private const string FragmentFunctionName = "fragment_main";
    private const nuint DepthPixelFormat = 252; // MTLPixelFormatDepth32Float

    private readonly MetalRendererContext _context;
    private readonly IMTLLibrary _library;
    private readonly IMTLFunction _vertexFunction;
    private readonly IMTLFunction _fragmentFunction;
    private readonly IMTLRenderPipelineState _defaultPipeline;
    private readonly IntPtr _defaultDepthStencilState;
    private readonly IntPtr _shadowDepthStencilState;

    public static MetalPipelineManager Instance => InstanceFactory.Value;

    public MetalPipelineManager()
        : this(MetalRendererContext.Instance)
    {
    }

    public MetalPipelineManager(MetalRendererContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));

        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Metal pipeline manager is only supported on macOS.");
        }

        IntPtr device = _context.Device.Handle;
        if (device == IntPtr.Zero)
        {
            throw new InvalidOperationException("Metal device is not available.");
        }

        bool loadedFromFile = TryLoadShaderSource(out string shaderSource, out string shaderPath);
        if (!loadedFromFile)
        {
            LogError($"Shader file not found. Falling back to embedded source. Searched base: {AppContext.BaseDirectory}");
        }

        IntPtr libraryPtr = CreateShaderLibrary(device, shaderSource);
        if (libraryPtr == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to compile Metal shader library. Source: {shaderPath}");
        }

        _library = new MTLLibraryHandle(libraryPtr);

        IntPtr vertexFunctionPtr = CreateFunction(libraryPtr, VertexFunctionName);
        IntPtr fragmentFunctionPtr = CreateFunction(libraryPtr, FragmentFunctionName);
        if (vertexFunctionPtr == IntPtr.Zero || fragmentFunctionPtr == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Failed to load shader functions '{VertexFunctionName}'/'{FragmentFunctionName}' from '{shaderPath}'.");
        }

        _vertexFunction = new MTLFunctionHandle(vertexFunctionPtr);
        _fragmentFunction = new MTLFunctionHandle(fragmentFunctionPtr);

        IntPtr pipelineStatePtr = CreateRenderPipelineState(
            device,
            _vertexFunction.Handle,
            _fragmentFunction.Handle);

        if (pipelineStatePtr == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create default Metal render pipeline state.");
        }

        _defaultPipeline = new MTLRenderPipelineStateHandle(pipelineStatePtr);

        _defaultDepthStencilState = CreateDepthStencilState(device, depthWriteEnabled: true);
        if (_defaultDepthStencilState == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create default Metal depth stencil state.");
        }

        _shadowDepthStencilState = CreateDepthStencilState(device, depthWriteEnabled: false);
        if (_shadowDepthStencilState == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create shadow Metal depth stencil state.");
        }
    }

    public IMTLRenderPipelineState GetDefaultPipeline()
    {
        return _defaultPipeline;
    }

    public void UsePipeline(IMTLRenderCommandEncoder encoder)
    {
        if (encoder is null)
        {
            throw new ArgumentNullException(nameof(encoder));
        }

        if (encoder.Handle == IntPtr.Zero || _defaultPipeline.Handle == IntPtr.Zero)
        {
            return;
        }

        ObjC.Void_objc_msgSend_IntPtr(encoder.Handle, Selectors.SetRenderPipelineState, _defaultPipeline.Handle);
        UseDepthWriteState(encoder);

        SetBackfaceCulling(encoder, true);
        SetFrontFacingWinding(encoder, clockwise: true);
    }

    public void UseDepthWriteState(IMTLRenderCommandEncoder encoder)
    {
        SetDepthStencilState(encoder, _defaultDepthStencilState);
    }

    public void UseDepthReadOnlyState(IMTLRenderCommandEncoder encoder)
    {
        IntPtr depthStencilState = _shadowDepthStencilState != IntPtr.Zero
            ? _shadowDepthStencilState
            : _defaultDepthStencilState;
        SetDepthStencilState(encoder, depthStencilState);
    }

    public static void SetBackfaceCulling(IMTLRenderCommandEncoder encoder, bool enabled)
    {
        if (encoder is null || encoder.Handle == IntPtr.Zero)
        {
            return;
        }

        ObjC.Void_objc_msgSend_UInt(
            encoder.Handle,
            Selectors.SetCullMode,
            enabled ? (nuint)2 : (nuint)0); // MTLCullModeBack / MTLCullModeNone
    }

    public static void SetFrontFacingWinding(IMTLRenderCommandEncoder encoder, bool clockwise)
    {
        if (encoder is null || encoder.Handle == IntPtr.Zero)
        {
            return;
        }

        ObjC.Void_objc_msgSend_UInt(
            encoder.Handle,
            Selectors.SetFrontFacingWinding,
            clockwise ? (nuint)0 : (nuint)1); // MTLWindingClockwise / MTLWindingCounterClockwise
    }

    private static IntPtr CreateShaderLibrary(IntPtr device, string shaderSource)
    {
        IntPtr sourceString = ToNSString(shaderSource);
        IntPtr error;
        IntPtr library = ObjC.IntPtr_objc_msgSend_IntPtr_IntPtr_outIntPtr(
            device,
            Selectors.NewLibraryWithSourceOptionsError,
            sourceString,
            IntPtr.Zero,
            out error);

        if (library == IntPtr.Zero)
        {
            LogError($"newLibraryWithSource failed: {DescribeNSError(error)}");
        }

        return library;
    }

    private static IntPtr CreateFunction(IntPtr library, string functionName)
    {
        IntPtr functionNameString = ToNSString(functionName);
        IntPtr function = ObjC.IntPtr_objc_msgSend_IntPtr(library, Selectors.NewFunctionWithName, functionNameString);
        if (function == IntPtr.Zero)
        {
            LogError($"newFunctionWithName failed for '{functionName}'.");
        }

        return function;
    }

    private static IntPtr CreateRenderPipelineState(IntPtr device, IntPtr vertexFunction, IntPtr fragmentFunction)
    {
        IntPtr descriptor = ObjC.IntPtr_objc_msgSend(
            ObjC.IntPtr_objc_msgSend(ObjCClasses.MTLRenderPipelineDescriptor, Selectors.Alloc),
            Selectors.Init);

        if (descriptor == IntPtr.Zero)
        {
            LogError("Failed to allocate MTLRenderPipelineDescriptor.");
            return IntPtr.Zero;
        }

        ObjC.Void_objc_msgSend_IntPtr(descriptor, Selectors.SetVertexFunction, vertexFunction);
        ObjC.Void_objc_msgSend_IntPtr(descriptor, Selectors.SetFragmentFunction, fragmentFunction);

        IntPtr colorAttachments = ObjC.IntPtr_objc_msgSend(descriptor, Selectors.ColorAttachments);
        IntPtr colorAttachment0 = ObjC.IntPtr_objc_msgSend_UInt(colorAttachments, Selectors.ObjectAtIndexedSubscript, 0);
        if (colorAttachment0 == IntPtr.Zero)
        {
            LogError("Failed to get color attachment 0 from pipeline descriptor.");
            return IntPtr.Zero;
        }

        ObjC.Void_objc_msgSend_UInt(
            colorAttachment0,
            Selectors.SetPixelFormat,
            (nuint)MetalRendererContext.DefaultColorFormat);
        ObjC.Void_objc_msgSend_Bool(colorAttachment0, Selectors.SetBlendingEnabled, true);
        ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetRgbBlendOperation, 0); // MTLBlendOperationAdd
        ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetAlphaBlendOperation, 0); // MTLBlendOperationAdd
        ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetSourceRGBBlendFactor, 4); // MTLBlendFactorSourceAlpha
        // Preserve exported transparent-shadow coverage: alpha channel should be srcA + dstA*(1-srcA),
        // not srcA*srcA + dstA*(1-srcA).
        ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetSourceAlphaBlendFactor, 1); // MTLBlendFactorOne
        ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetDestinationRGBBlendFactor, 5); // MTLBlendFactorOneMinusSourceAlpha
        ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetDestinationAlphaBlendFactor, 5); // MTLBlendFactorOneMinusSourceAlpha
        ObjC.Void_objc_msgSend_UInt(
            descriptor,
            Selectors.SetDepthAttachmentPixelFormat,
            DepthPixelFormat);

        IntPtr error;
        IntPtr pipelineState = ObjC.IntPtr_objc_msgSend_IntPtr_outIntPtr(
            device,
            Selectors.NewRenderPipelineStateWithDescriptorError,
            descriptor,
            out error);

        if (pipelineState == IntPtr.Zero)
        {
            LogError($"newRenderPipelineStateWithDescriptor failed: {DescribeNSError(error)}");
        }

        ObjC.Void_objc_msgSend(descriptor, Selectors.Release);
        return pipelineState;
    }

    private static void SetDepthStencilState(IMTLRenderCommandEncoder encoder, IntPtr depthStencilState)
    {
        if (encoder is null || encoder.Handle == IntPtr.Zero || depthStencilState == IntPtr.Zero)
        {
            return;
        }

        ObjC.Void_objc_msgSend_IntPtr(encoder.Handle, Selectors.SetDepthStencilState, depthStencilState);
    }

    private static IntPtr CreateDepthStencilState(IntPtr device, bool depthWriteEnabled)
    {
        IntPtr descriptor = ObjC.IntPtr_objc_msgSend(
            ObjC.IntPtr_objc_msgSend(ObjCClasses.MTLDepthStencilDescriptor, Selectors.Alloc),
            Selectors.Init);
        if (descriptor == IntPtr.Zero)
        {
            LogError("Failed to allocate MTLDepthStencilDescriptor.");
            return IntPtr.Zero;
        }

        ObjC.Void_objc_msgSend_UInt(descriptor, Selectors.SetDepthCompareFunction, 3); // MTLCompareFunctionLessEqual
        ObjC.Void_objc_msgSend_Bool(descriptor, Selectors.SetDepthWriteEnabled, depthWriteEnabled);

        IntPtr depthStencilState = ObjC.IntPtr_objc_msgSend_IntPtr(
            device,
            Selectors.NewDepthStencilStateWithDescriptor,
            descriptor);
        if (depthStencilState == IntPtr.Zero)
        {
            LogError("newDepthStencilStateWithDescriptor failed.");
        }

        ObjC.Void_objc_msgSend(descriptor, Selectors.Release);
        return depthStencilState;
    }

    private static IntPtr ToNSString(string value)
    {
        IntPtr utf8Ptr = Marshal.StringToHGlobalAnsi(value);
        try
        {
            return ObjC.IntPtr_objc_msgSend_IntPtr(ObjCClasses.NSString, Selectors.StringWithUTF8String, utf8Ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(utf8Ptr);
        }
    }

    private static string DescribeNSError(IntPtr error)
    {
        if (error == IntPtr.Zero)
        {
            return "unknown error";
        }

        IntPtr description = ObjC.IntPtr_objc_msgSend(error, Selectors.LocalizedDescription);
        if (description == IntPtr.Zero)
        {
            return $"NSError(0x{error.ToInt64():X})";
        }

        IntPtr utf8 = ObjC.IntPtr_objc_msgSend(description, Selectors.UTF8String);
        if (utf8 == IntPtr.Zero)
        {
            return $"NSError(0x{error.ToInt64():X})";
        }

        return Marshal.PtrToStringAnsi(utf8) ?? $"NSError(0x{error.ToInt64():X})";
    }

    private static bool TryLoadShaderSource(out string source, out string sourcePath)
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "Shaders", "KnobShaders.metal"),
            Path.Combine(AppContext.BaseDirectory, "KnobShaders.metal"),
            Path.Combine(Directory.GetCurrentDirectory(), "Shaders", "KnobShaders.metal"),
            Path.Combine(Directory.GetCurrentDirectory(), "KnobForge.Rendering", "Shaders", "KnobShaders.metal")
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                source = File.ReadAllText(candidate);
                sourcePath = candidate;
                return true;
            }
        }

        source = FallbackShaderSource;
        sourcePath = "<embedded-fallback>";
        return false;
    }

    private static void LogError(string message)
    {
        Console.Error.WriteLine($"[MetalPipelineManager] {message}");
    }

    private sealed class MTLLibraryHandle : IMTLLibrary
    {
        public IntPtr Handle { get; }

        public MTLLibraryHandle(IntPtr handle)
        {
            Handle = handle;
        }
    }

    private sealed class MTLFunctionHandle : IMTLFunction
    {
        public IntPtr Handle { get; }

        public MTLFunctionHandle(IntPtr handle)
        {
            Handle = handle;
        }
    }

    private sealed class MTLRenderPipelineStateHandle : IMTLRenderPipelineState
    {
        public IntPtr Handle { get; }

        public MTLRenderPipelineStateHandle(IntPtr handle)
        {
            Handle = handle;
        }
    }

    private static class ObjCClasses
    {
        public static readonly IntPtr NSString = ObjC.objc_getClass("NSString");
        public static readonly IntPtr MTLRenderPipelineDescriptor = ObjC.objc_getClass("MTLRenderPipelineDescriptor");
        public static readonly IntPtr MTLDepthStencilDescriptor = ObjC.objc_getClass("MTLDepthStencilDescriptor");
    }

    private static class Selectors
    {
        public static readonly IntPtr Alloc = ObjC.sel_registerName("alloc");
        public static readonly IntPtr Init = ObjC.sel_registerName("init");
        public static readonly IntPtr Release = ObjC.sel_registerName("release");
        public static readonly IntPtr UTF8String = ObjC.sel_registerName("UTF8String");
        public static readonly IntPtr LocalizedDescription = ObjC.sel_registerName("localizedDescription");
        public static readonly IntPtr StringWithUTF8String = ObjC.sel_registerName("stringWithUTF8String:");
        public static readonly IntPtr NewLibraryWithSourceOptionsError = ObjC.sel_registerName("newLibraryWithSource:options:error:");
        public static readonly IntPtr NewFunctionWithName = ObjC.sel_registerName("newFunctionWithName:");
        public static readonly IntPtr SetVertexFunction = ObjC.sel_registerName("setVertexFunction:");
        public static readonly IntPtr SetFragmentFunction = ObjC.sel_registerName("setFragmentFunction:");
        public static readonly IntPtr SetDepthAttachmentPixelFormat = ObjC.sel_registerName("setDepthAttachmentPixelFormat:");
        public static readonly IntPtr ColorAttachments = ObjC.sel_registerName("colorAttachments");
        public static readonly IntPtr ObjectAtIndexedSubscript = ObjC.sel_registerName("objectAtIndexedSubscript:");
        public static readonly IntPtr SetPixelFormat = ObjC.sel_registerName("setPixelFormat:");
        public static readonly IntPtr SetBlendingEnabled = ObjC.sel_registerName("setBlendingEnabled:");
        public static readonly IntPtr SetRgbBlendOperation = ObjC.sel_registerName("setRgbBlendOperation:");
        public static readonly IntPtr SetAlphaBlendOperation = ObjC.sel_registerName("setAlphaBlendOperation:");
        public static readonly IntPtr SetSourceRGBBlendFactor = ObjC.sel_registerName("setSourceRGBBlendFactor:");
        public static readonly IntPtr SetSourceAlphaBlendFactor = ObjC.sel_registerName("setSourceAlphaBlendFactor:");
        public static readonly IntPtr SetDestinationRGBBlendFactor = ObjC.sel_registerName("setDestinationRGBBlendFactor:");
        public static readonly IntPtr SetDestinationAlphaBlendFactor = ObjC.sel_registerName("setDestinationAlphaBlendFactor:");
        public static readonly IntPtr NewRenderPipelineStateWithDescriptorError = ObjC.sel_registerName("newRenderPipelineStateWithDescriptor:error:");
        public static readonly IntPtr SetRenderPipelineState = ObjC.sel_registerName("setRenderPipelineState:");
        public static readonly IntPtr SetDepthCompareFunction = ObjC.sel_registerName("setDepthCompareFunction:");
        public static readonly IntPtr SetDepthWriteEnabled = ObjC.sel_registerName("setDepthWriteEnabled:");
        public static readonly IntPtr NewDepthStencilStateWithDescriptor = ObjC.sel_registerName("newDepthStencilStateWithDescriptor:");
        public static readonly IntPtr SetDepthStencilState = ObjC.sel_registerName("setDepthStencilState:");
        public static readonly IntPtr SetCullMode = ObjC.sel_registerName("setCullMode:");
        public static readonly IntPtr SetFrontFacingWinding = ObjC.sel_registerName("setFrontFacingWinding:");
    }

    private static class ObjC
    {
        [DllImport("/usr/lib/libobjc.A.dylib")]
        public static extern IntPtr objc_getClass(string name);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        public static extern IntPtr sel_registerName(string name);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr IntPtr_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr IntPtr_objc_msgSend_UInt(IntPtr receiver, IntPtr selector, nuint index);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr IntPtr_objc_msgSend_IntPtr_outIntPtr(
            IntPtr receiver,
            IntPtr selector,
            IntPtr arg1,
            out IntPtr arg2);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr IntPtr_objc_msgSend_IntPtr_IntPtr_outIntPtr(
            IntPtr receiver,
            IntPtr selector,
            IntPtr arg1,
            IntPtr arg2,
            out IntPtr arg3);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern void Void_objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern void Void_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern void Void_objc_msgSend_UInt(IntPtr receiver, IntPtr selector, nuint arg1);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern void Void_objc_msgSend_Bool(IntPtr receiver, IntPtr selector, bool arg1);
    }

    private const string FallbackShaderSource = @"
#include <metal_stdlib>
using namespace metal;

#define MAX_LIGHTS 8

struct MetalVertex
{
    packed_float3 position;
    packed_float3 normal;
    packed_float4 tangent;
};

struct GpuLight
{
    float4 positionType;
    float4 direction;
    float4 colorIntensity;
    float4 params0;
};

struct GpuUniforms
{
    float4 cameraPosAndReferenceRadius;
    float4 rightAndScaleX;
    float4 upAndScaleY;
    float4 forwardAndScaleZ;
    float4 projectionOffsetsAndLightCount;
    float4 materialBaseColorAndMetallic;
    float4 materialRoughnessDiffuseSpecMode;
    float4 materialPartTopColorAndMetallic;
    float4 materialPartBevelColorAndMetallic;
    float4 materialPartSideColorAndMetallic;
    float4 materialPartRoughnessAndEnable;
    float4 materialSurfaceBrushParams;
    float4 weatherParams;
    float4 scratchExposeColorAndStrength;
    float4 indicatorParams0;
    float4 indicatorParams1;
    float4 indicatorColorAndBlend;
    float4 microDetailParams;
    float4 environmentTopColorAndIntensity;
    float4 environmentBottomColorAndRoughnessMix;
    float4 modelRotationCosSin;
    float4 shadowParams;
    float4 shadowColorAndOpacity;
    float4 debugBasisParams;
    GpuLight lights[MAX_LIGHTS];
};

struct VertexOut
{
    float4 position [[position]];
    float3 worldPos;
    float3 worldNormal;
    float4 worldTangentSign;
};

static inline float3 Hadamard(float3 a, float3 b)
{
    return a * b;
}

static inline float3 Clamp01(float3 value)
{
    return clamp(value, 0.0, 1.0);
}

static inline float Hash21(float2 p)
{
    p = fract(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

static inline float ValueNoise2(float2 p)
{
    float2 i = floor(p);
    float2 f = fract(p);
    float a = Hash21(i);
    float b = Hash21(i + float2(1.0, 0.0));
    float c = Hash21(i + float2(0.0, 1.0));
    float d = Hash21(i + float2(1.0, 1.0));
    float2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

static inline void ApplyModeShaping(int mode, float diffuseBoost, float specularBoost, thread float& diffuse, thread float& spec)
{
    if (mode == 1)
    {
        diffuse = pow(diffuse, 0.80) * max(0.0, diffuseBoost);
        spec = pow(spec, 0.65) * max(0.0, specularBoost);
    }
    else if (mode == 2)
    {
        diffuse = pow(diffuse, 0.90) * (0.65 + 0.35 * max(0.0, diffuseBoost));
        spec = pow(spec, 0.78) * (0.65 + 0.35 * max(0.0, specularBoost));
    }
}

static inline float ComputeIndicatorMask(float2 localXY, float topRadius, float4 params0, float4 params1)
{
    if (params0.x < 0.5 || topRadius <= 1e-6)
    {
        return 0.0;
    }

    int shape = int(round(params0.y));
    float widthRatio = params0.z;
    float lengthRatio = params0.w;
    float roundness = clamp(params1.x, 0.0, 1.0);
    float positionRatio = clamp(params1.y, 0.05, 0.90);

    float2 p = localXY / topRadius;
    float t = p.y;
    float start = positionRatio;
    float end = clamp(start + lengthRatio, start + 1e-4, 0.98);
    float halfWidth = max(0.001, widthRatio * 0.5);
    float along = (t - start) / max(1e-4, end - start);
    float edgeDistance = 0.0;

    if (shape == 6)
    {
        float dotRadius = halfWidth;
        float centerY = end - min(dotRadius * 0.35, (end - start) * 0.25);
        float2 d = float2(p.x, p.y - centerY);
        edgeDistance = length(d) / max(dotRadius, 1e-6);
    }
    else
    {
        if (t < start || t > end)
        {
            return 0.0;
        }

        float localHalfWidth = halfWidth;
        if (shape == 1)
        {
            localHalfWidth *= max(0.20, 1.0 - (along * 0.80));
        }
        else if (shape == 3)
        {
            localHalfWidth *= max(0.06, 1.0 - (along * 0.94));
        }
        else if (shape == 4)
        {
            localHalfWidth *= max(0.02, 1.0 - along);
        }

        if (shape == 5)
        {
            float qx = abs(p.x) / max(halfWidth, 1e-6);
            float qy = abs((along * 2.0) - 1.0);
            edgeDistance = qx + qy;
        }
        else
        {
            edgeDistance = abs(p.x) / max(localHalfWidth, 1e-6);
        }
    }

    if (edgeDistance >= 1.0)
    {
        return 0.0;
    }

    float edgeMask;
    if (roundness <= 1e-4)
    {
        edgeMask = 1.0;
    }
    else
    {
        float feather = roundness * 0.45;
        edgeMask = 1.0 - smoothstep(1.0 - feather, 1.0, edgeDistance);
    }

    float capMask = 1.0;
    if (shape == 2)
    {
        float endDistance = min(along, 1.0 - along);
        capMask = smoothstep(0.0, 0.22, endDistance);
    }

    return edgeMask * capMask;
}

vertex VertexOut vertex_main(
    uint vertexId [[vertex_id]],
    const device MetalVertex* vertices [[buffer(0)]],
    constant GpuUniforms& uniforms [[buffer(1)]],
    texture2d<float> paintMask [[texture(1)]])
{
    MetalVertex v = vertices[vertexId];
    constexpr sampler paintSampler(filter::linear, mip_filter::linear, address::clamp_to_edge);
    float cosA = uniforms.modelRotationCosSin.x;
    float sinA = uniforms.modelRotationCosSin.y;

    float3 localPos = float3(v.position);
    float3 localNormal = float3(v.normal);
    float3 localTangent = float3(v.tangent.xyz);
    float tangentSign = v.tangent.w >= 0.0 ? 1.0 : -1.0;
    float3 worldPos = float3(
        localPos.x * cosA - localPos.y * sinA,
        localPos.x * sinA + localPos.y * cosA,
        localPos.z);
    float3 worldNormal = normalize(float3(
        localNormal.x * cosA - localNormal.y * sinA,
        localNormal.x * sinA + localNormal.y * cosA,
        localNormal.z));
    float3 worldTangent = float3(
        localTangent.x * cosA - localTangent.y * sinA,
        localTangent.x * sinA + localTangent.y * cosA,
        localTangent.z);
    worldTangent = worldTangent - worldNormal * dot(worldTangent, worldNormal);
    if (dot(worldTangent, worldTangent) <= 1e-8)
    {
        worldTangent = float3(-worldPos.y, worldPos.x, 0.0);
        worldTangent = worldTangent - worldNormal * dot(worldTangent, worldNormal);
        if (dot(worldTangent, worldTangent) <= 1e-8)
        {
            worldTangent = float3(1.0, 0.0, 0.0) - worldNormal * worldNormal.x;
        }
    }
    worldTangent = normalize(worldTangent);

    float geometryKeep = clamp(uniforms.materialSurfaceBrushParams.w, 0.0, 1.0);
    float topMask = smoothstep(0.60, 0.98, worldNormal.z);
    float topRadiusVertex = uniforms.indicatorParams1.z > 1e-6
        ? uniforms.indicatorParams1.z
        : max(1.0, uniforms.cameraPosAndReferenceRadius.w * max(0.05, uniforms.modelRotationCosSin.z));
    float indicatorMaskVertex = ComputeIndicatorMask(localPos.xy, topRadiusVertex, uniforms.indicatorParams0, uniforms.indicatorParams1);
    float flatten = (1.0 - geometryKeep) * topMask * (1.0 - clamp(indicatorMaskVertex, 0.0, 1.0));
    if (flatten > 1e-4)
    {
        worldPos.z = mix(worldPos.z, uniforms.modelRotationCosSin.w, flatten);
        worldNormal = normalize(mix(worldNormal, float3(0.0, 0.0, 1.0), flatten));
        worldTangent = worldTangent - worldNormal * dot(worldTangent, worldNormal);
        if (dot(worldTangent, worldTangent) <= 1e-8)
        {
            worldTangent = float3(1.0, 0.0, 0.0) - worldNormal * worldNormal.x;
        }
        worldTangent = normalize(worldTangent);
    }

    // Scratch carve displacement (A channel of paint mask) in object-local UV space.
    float scratchDepth = clamp(uniforms.debugBasisParams.y, 0.0, 1.0);
    if (scratchDepth > 1e-4)
    {
        float referenceRadius = max(1.0, uniforms.cameraPosAndReferenceRadius.w);
        float2 paintUv = localPos.xy / max(referenceRadius * 2.0, 1e-4) + 0.5;
        if (all(paintUv >= float2(0.0)) && all(paintUv <= float2(1.0)))
        {
            float scratchMask = paintMask.sample(paintSampler, clamp(paintUv, float2(0.0), float2(1.0)), level(0.0)).w;
            float carveWorld = scratchMask * scratchDepth * referenceRadius * 0.020;
            worldPos -= worldNormal * carveWorld;
        }
    }

    float clipX = dot(worldPos, uniforms.rightAndScaleX.xyz) * uniforms.rightAndScaleX.w + uniforms.projectionOffsetsAndLightCount.x;
    float clipY = dot(worldPos, uniforms.upAndScaleY.xyz) * uniforms.upAndScaleY.w + uniforms.projectionOffsetsAndLightCount.y;
    float cameraDepth = dot(worldPos - uniforms.cameraPosAndReferenceRadius.xyz,
                            uniforms.forwardAndScaleZ.xyz);
    float nearPlane = max(0.05, uniforms.cameraPosAndReferenceRadius.w * 0.5);
    float farPlane = max(nearPlane + 1.0, uniforms.cameraPosAndReferenceRadius.w * 14.0);
    float depth01 = clamp((cameraDepth - nearPlane) / (farPlane - nearPlane), 0.0, 1.0);
    // Metal NDC depth is [0, 1], not [-1, 1].
    float clipZ = clamp(depth01, 0.001, 0.999);

    if (uniforms.shadowParams.x > 0.5)
    {
        clipX = (clipX * uniforms.shadowParams.w) + uniforms.shadowParams.y;
        clipY = (clipY * uniforms.shadowParams.w) + uniforms.shadowParams.z;
        // Nudge projected shadow geometry slightly away from camera so it lands on visible receivers
        // (collar/knob/background) while reducing self-shadow acne.
        float shadowDepthBias = clamp(uniforms.shadowColorAndOpacity.x, 0.0, 0.05);
        clipZ = clamp(clipZ + shadowDepthBias, 0.001, 0.999);
    }

    VertexOut outVertex;
    outVertex.position = float4(clipX, clipY, clipZ, 1.0);
    outVertex.worldPos = worldPos;
    outVertex.worldNormal = worldNormal;
    outVertex.worldTangentSign = float4(worldTangent, tangentSign);
    return outVertex;
}

fragment float4 fragment_main(
    VertexOut inVertex [[stage_in]],
    constant GpuUniforms& uniforms [[buffer(1)]],
    texture2d<float> spiralNormalMap [[texture(0)]],
    texture2d<float> paintMask [[texture(1)]],
    texture2d<float> paintColor [[texture(2)]])
{
    if (uniforms.shadowParams.x > 0.5)
    {
        float shadowAlpha = clamp(uniforms.shadowColorAndOpacity.w, 0.0, 1.0);
        return float4(0.0, 0.0, 0.0, shadowAlpha);
    }

    constexpr sampler normalSampler(filter::linear, mip_filter::linear, address::clamp_to_edge);
    constexpr sampler paintSampler(filter::linear, mip_filter::linear, address::clamp_to_edge);

    float3 normal = normalize(inVertex.worldNormal);
    float3 cameraPos = uniforms.cameraPosAndReferenceRadius.xyz;
    float referenceRadius = max(1.0, uniforms.cameraPosAndReferenceRadius.w);
    float topScale = max(0.05, uniforms.modelRotationCosSin.z);
    float topRadius = uniforms.indicatorParams1.z > 1e-6
        ? uniforms.indicatorParams1.z
        : max(1.0, referenceRadius * topScale);

    float3 baseColor = Clamp01(uniforms.materialBaseColorAndMetallic.xyz);
    float metallic = clamp(uniforms.materialBaseColorAndMetallic.w, 0.0, 1.0);
    float pearlescence = clamp(uniforms.indicatorParams1.w, 0.0, 1.0);
    float roughness = clamp(uniforms.materialRoughnessDiffuseSpecMode.x, 0.04, 1.0);
    float diffuseStrength = uniforms.materialRoughnessDiffuseSpecMode.y;
    float specularStrength = uniforms.materialRoughnessDiffuseSpecMode.z;
    int lightingMode = int(round(uniforms.materialRoughnessDiffuseSpecMode.w));
    float brushStrength = clamp(uniforms.materialSurfaceBrushParams.x, 0.0, 1.0);
    float brushDensity = max(1.0, uniforms.materialSurfaceBrushParams.y);
    float brushDensityFactor = clamp((brushDensity - 4.0) / 316.0, 0.0, 1.0);
    float surfaceCharacter = clamp(uniforms.materialSurfaceBrushParams.z, 0.0, 1.0);
    float rustAmount = clamp(uniforms.weatherParams.x, 0.0, 1.0);
    float wearAmount = clamp(uniforms.weatherParams.y, 0.0, 1.0);
    float gunkAmount = clamp(uniforms.weatherParams.z, 0.0, 1.0);
    float brushDarkness = clamp(uniforms.weatherParams.w, 0.0, 1.0);
    float paintCoatMetallic = clamp(uniforms.debugBasisParams.z, 0.0, 1.0);
    float paintCoatRoughness = clamp(uniforms.debugBasisParams.w, 0.04, 1.0);
    float3 scratchExposeColor = Clamp01(uniforms.scratchExposeColorAndStrength.xyz);
    float scratchExposeStrength = clamp(uniforms.scratchExposeColorAndStrength.w, 0.0, 1.0);
    float microInfluence = clamp(uniforms.microDetailParams.x, 0.0, 1.0);
    float fadeStart = max(0.1, uniforms.microDetailParams.y);
    float fadeEnd = max(fadeStart + 1e-3, uniforms.microDetailParams.z);
    float roughnessLodBoostFactor = max(0.0, uniforms.microDetailParams.w);

    float partMaterialsEnabled = clamp(uniforms.materialPartRoughnessAndEnable.w, 0.0, 1.0);
    if (partMaterialsEnabled > 0.5)
    {
        float halfHeight = max(1e-4, uniforms.modelRotationCosSin.w);
        float localZNorm = clamp((inVertex.worldPos.z / max(halfHeight * 2.0, 1e-4)) + 0.5, 0.0, 1.0);

        float topWeight = smoothstep(0.55, 0.95, normal.z) * smoothstep(0.40, 0.92, localZNorm);
        float sideWeight = smoothstep(0.35, 0.92, 1.0 - abs(normal.z));
        sideWeight *= (1.0 - smoothstep(0.58, 0.96, localZNorm));
        float bevelWeight = max(0.0, 1.0 - topWeight - sideWeight);
        float weightSum = max(1e-4, topWeight + sideWeight + bevelWeight);
        topWeight /= weightSum;
        sideWeight /= weightSum;
        bevelWeight /= weightSum;

        float3 topColor = Clamp01(uniforms.materialPartTopColorAndMetallic.xyz);
        float3 bevelColor = Clamp01(uniforms.materialPartBevelColorAndMetallic.xyz);
        float3 sideColor = Clamp01(uniforms.materialPartSideColorAndMetallic.xyz);
        float topMetallic = clamp(uniforms.materialPartTopColorAndMetallic.w, 0.0, 1.0);
        float bevelMetallic = clamp(uniforms.materialPartBevelColorAndMetallic.w, 0.0, 1.0);
        float sideMetallic = clamp(uniforms.materialPartSideColorAndMetallic.w, 0.0, 1.0);
        float topRoughness = clamp(uniforms.materialPartRoughnessAndEnable.x, 0.04, 1.0);
        float bevelRoughness = clamp(uniforms.materialPartRoughnessAndEnable.y, 0.04, 1.0);
        float sideRoughness = clamp(uniforms.materialPartRoughnessAndEnable.z, 0.04, 1.0);

        baseColor = Clamp01(
            topColor * topWeight +
            bevelColor * bevelWeight +
            sideColor * sideWeight);
        metallic = clamp(
            topMetallic * topWeight +
            bevelMetallic * bevelWeight +
            sideMetallic * sideWeight,
            0.0,
            1.0);
        roughness = clamp(
            topRoughness * topWeight +
            bevelRoughness * bevelWeight +
            sideRoughness * sideWeight,
            0.04,
            1.0);
    }

    float3 ambientColor = float3(0.03, 0.03, 0.03);
    float3 accum = Hadamard(baseColor, ambientColor) * (1.0 - metallic);

    float3 viewRaw = cameraPos - inVertex.worldPos;
    float viewLenSq = dot(viewRaw, viewRaw);
    float3 viewDir = viewLenSq > 1e-8 ? normalize(viewRaw) : float3(0.0, 0.0, 1.0);
    if (dot(normal, viewDir) < 0.0)
    {
        normal = -normal;
    }

    float3 topTangent = float3(1.0, 0.0, 0.0) - normal * normal.x;
    float topTangentLenSq = dot(topTangent, topTangent);
    if (topTangentLenSq <= 1e-8)
    {
        topTangent = float3(0.0, 1.0, 0.0) - normal * normal.y;
        topTangentLenSq = dot(topTangent, topTangent);
    }

    if (topTangentLenSq <= 1e-8)
    {
        topTangent = float3(1.0, 0.0, 0.0);
    }
    else
    {
        topTangent = normalize(topTangent);
    }

    float cosA = uniforms.modelRotationCosSin.x;
    float sinA = uniforms.modelRotationCosSin.y;
    float2 localXY = float2(
        inVertex.worldPos.x * cosA + inVertex.worldPos.y * sinA,
        -inVertex.worldPos.x * sinA + inVertex.worldPos.y * cosA);
    float indicatorMask = ComputeIndicatorMask(localXY, topRadius, uniforms.indicatorParams0, uniforms.indicatorParams1);
    indicatorMask *= smoothstep(0.55, 0.95, abs(normal.z));

    float3 topBitangent = normalize(cross(normal, topTangent));
    float2 uv = inVertex.worldPos.xy / (topRadius * 2.0) + 0.5;
    float2 uvClamped = clamp(uv, float2(0.0), float2(1.0));
    bool uvInside = all(uv >= float2(0.0)) && all(uv <= float2(1.0));
    float topMask = smoothstep(0.55, 0.95, abs(normal.z));
    topMask = pow(topMask, mix(1.6, 0.8, surfaceCharacter));
    topMask *= 1.0 - clamp(indicatorMask, 0.0, 1.0);

    float2 duvDx = dfdx(uv);
    float2 duvDy = dfdy(uv);
    float uvFootprint = max(length(duvDx), length(duvDy));
    float texelsPerPixel = 1.0 / max(uvFootprint * 1024.0, 1e-5);
    float microDetailVisibility = smoothstep(fadeStart, fadeEnd, texelsPerPixel);

    if (uvInside && topMask > 0.0)
    {
        float3 mapNormal = spiralNormalMap.sample(normalSampler, uvClamped).xyz * 2.0 - 1.0;
        float3 microNormal = normalize(
            topTangent * mapNormal.x +
            topBitangent * mapNormal.y +
            normal * mapNormal.z);

        float densityInfluence = mix(0.65, 1.35, brushDensityFactor);
        float microBlend = brushStrength * densityInfluence * topMask * microDetailVisibility * microInfluence;
        normal = normalize(mix(normal, microNormal, clamp(microBlend, 0.0, 1.0)));
    }

    // Fade out unresolved high-frequency cap detail to avoid distance moire.
    float capFlatten = (1.0 - microDetailVisibility) * topMask * microInfluence * 0.35;
    normal = normalize(mix(normal, float3(0.0, 0.0, 1.0), clamp(capFlatten, 0.0, 1.0)));

    float roughnessLodBoost = (1.0 - microDetailVisibility) * roughnessLodBoostFactor * (0.35 + (0.65 * surfaceCharacter)) * microInfluence;
    roughness = clamp(roughness + roughnessLodBoost, 0.04, 1.0);

    float indicatorBlend = clamp(uniforms.indicatorColorAndBlend.w, 0.0, 1.0) * indicatorMask;
    float3 indicatorColor = Clamp01(uniforms.indicatorColorAndBlend.xyz);
    baseColor = mix(baseColor, indicatorColor, clamp(indicatorBlend, 0.0, 1.0));

    // Literal paint-mask weathering (R=rust, G=wear, B=gunk, A=scratch) in local object space.
    float2 paintUv = localXY / max(referenceRadius * 2.0, 1e-4) + 0.5;
    float4 paintSample = float4(0.0);
    float4 colorPaintSample = float4(0.0);
    if (all(paintUv >= float2(0.0)) && all(paintUv <= float2(1.0)))
    {
        paintSample = paintMask.sample(paintSampler, clamp(paintUv, float2(0.0), float2(1.0)));
        colorPaintSample = paintColor.sample(paintSampler, clamp(paintUv, float2(0.0), float2(1.0)));
    }

    float colorPaintMask = clamp(colorPaintSample.w, 0.0, 1.0);
    float3 colorPaintBase = float3(0.0);
    if (colorPaintMask > 1e-5)
    {
        // Color paint texture is stored premultiplied; recover straight RGB before shading.
        colorPaintBase = Clamp01(colorPaintSample.xyz / colorPaintMask);
    }
    baseColor = mix(baseColor, colorPaintBase, colorPaintMask);
    // Make painted color read as a coating layer rather than bare metal.
    float paintCoatBlend = smoothstep(0.0, 0.85, colorPaintMask);
    roughness = mix(roughness, paintCoatRoughness, paintCoatBlend);
    metallic = mix(metallic, paintCoatMetallic, paintCoatBlend);

    float darknessGain = mix(0.45, 1.45, brushDarkness);
    float rustRaw = clamp(paintSample.x, 0.0, 1.0);
    float wearRaw = clamp(paintSample.y, 0.0, 1.0);
    float gunkRaw = clamp(paintSample.z, 0.0, 1.0);
    float scratchRaw = clamp(paintSample.w, 0.0, 1.0);

    float rustNoiseA = ValueNoise2(paintUv * float2(192.0, 217.0) + float2(11.3, 6.7));
    float rustNoiseB = ValueNoise2(paintUv * float2(67.0, 59.0) + float2(41.1, 13.5));
    float rustSplotch = smoothstep(0.32, 0.90, rustNoiseA * 0.72 + rustNoiseB * 0.58);
    float rustStrength = mix(0.30, 1.00, rustAmount);
    float wearStrength = mix(0.15, 0.70, wearAmount);
    float gunkStrength = mix(0.35, 1.20, gunkAmount);
    float scratchStrength = mix(0.30, 1.00, wearAmount);
    float rustMask = clamp(rustRaw * rustSplotch * darknessGain * rustStrength, 0.0, 1.0);
    float wearMask = clamp(wearRaw * mix(0.30, 0.80, brushDarkness) * wearStrength, 0.0, 1.0);
    float gunkMask = clamp(gunkRaw * mix(0.55, 1.65, brushDarkness) * gunkStrength, 0.0, 1.0);
    float scratchMask = clamp(scratchRaw * mix(0.45, 1.00, brushDarkness) * scratchStrength, 0.0, 1.0);

    float rustHue = ValueNoise2(paintUv * float2(103.0, 97.0) + float2(3.1, 17.2));
    float3 rustDark = float3(0.23, 0.08, 0.04);
    float3 rustMid = float3(0.46, 0.17, 0.07);
    float3 rustOrange = float3(0.71, 0.29, 0.09);
    float3 rustColor = mix(
        mix(rustDark, rustMid, clamp(rustHue * 1.25, 0.0, 1.0)),
        rustOrange,
        clamp((rustHue - 0.35) / 0.65, 0.0, 1.0));
    float3 gunkColor = float3(0.02, 0.02, 0.018);
    float3 wearColor = mix(baseColor, float3(0.80, 0.79, 0.76), 0.45);

    baseColor = mix(baseColor, rustColor, clamp(rustMask * 0.88, 0.0, 1.0));
    baseColor = mix(baseColor, gunkColor, clamp(gunkMask * 0.96, 0.0, 1.0));
    baseColor = mix(baseColor, wearColor, clamp(wearMask * 0.24, 0.0, 1.0));
    float grimeDarken = clamp((rustMask * 0.18 + gunkMask * 0.55) * (0.25 + 0.75 * brushDarkness), 0.0, 0.85);
    baseColor *= (1.0 - grimeDarken);
    float scratchReveal = clamp(scratchMask * scratchExposeStrength, 0.0, 1.0);
    baseColor = mix(baseColor, scratchExposeColor, scratchReveal);

    roughness = clamp(roughness + rustMask * 0.34 + gunkMask * 0.62 - wearMask * 0.05 - scratchMask * 0.14, 0.04, 1.0);
    metallic = clamp(metallic - rustMask * 0.62 - gunkMask * 0.30 + scratchMask * 0.10, 0.0, 1.0);

    float shininess = 4.0 + ((128.0 - 4.0) * (1.0 - roughness));

    float tangentSign = inVertex.worldTangentSign.w >= 0.0 ? 1.0 : -1.0;
    float3 tangent = inVertex.worldTangentSign.xyz;
    tangent = tangent - normal * dot(normal, tangent);
    float tangentLenSq = dot(tangent, tangent);
    if (tangentLenSq <= 1e-8)
    {
        float3 radial = float3(-inVertex.worldPos.y, inVertex.worldPos.x, 0.0);
        float radialLenSq = dot(radial, radial);
        tangent = radialLenSq > 1e-8 ? normalize(radial) : float3(1.0, 0.0, 0.0);
        tangent = tangent - normal * dot(normal, tangent);
        tangentLenSq = dot(tangent, tangent);
        if (tangentLenSq <= 1e-8)
        {
            tangent = normalize(cross(float3(0.0, 0.0, 1.0), normal));
            if (dot(tangent, tangent) <= 1e-8)
            {
                tangent = float3(1.0, 0.0, 0.0);
            }
        }
    }
    tangent = normalize(tangent);

    float3 bitangent = normalize(cross(normal, tangent)) * tangentSign;
    if (dot(bitangent, bitangent) <= 1e-8)
    {
        bitangent = normalize(cross(normal, tangent));
    }

    int basisDebugMode = int(round(uniforms.debugBasisParams.x));
    if (basisDebugMode != 0)
    {
        float3 debugColor;
        if (basisDebugMode == 1)
        {
            debugColor = normal * 0.5 + 0.5;
        }
        else if (basisDebugMode == 2)
        {
            debugColor = tangent * 0.5 + 0.5;
        }
        else
        {
            debugColor = bitangent * 0.5 + 0.5;
        }

        return float4(Clamp01(debugColor), 1.0);
    }

    float anisotropy = clamp(
        brushStrength * topMask * (0.35 + 0.65 * surfaceCharacter) * mix(0.8, 1.2, brushDensityFactor),
        0.0,
        0.95);
    float alpha = max(0.02, roughness * roughness);
    float alphaT = max(0.02, alpha * (1.0 - anisotropy));
    float alphaB = max(0.02, alpha * (1.0 + anisotropy));

    float NdotV = max(0.0, dot(normal, viewDir));
    float maxBase = max(1e-6, max(baseColor.x, max(baseColor.y, baseColor.z)));
    float3 metalSpecColor = baseColor / maxBase;
    float3 F0 = mix(float3(0.04), metalSpecColor, metallic);
    float3 fresnelView = F0 + (float3(1.0) - F0) * pow(1.0 - NdotV, 5.0);

    int lightCount = min(MAX_LIGHTS, int(round(uniforms.projectionOffsetsAndLightCount.z)));
    for (int i = 0; i < lightCount; i++)
    {
        GpuLight light = uniforms.lights[i];
        float3 lightColor = Clamp01(light.colorIntensity.xyz);
        float intensity = max(0.0, light.colorIntensity.w);

        float3 L;
            float attenuation;
        if (int(round(light.positionType.w)) == 1)
        {
            float3 directional = light.direction.xyz;
            float dirLenSq = dot(directional, directional);
            L = dirLenSq > 1e-8 ? normalize(directional) : float3(0.0, 0.0, 1.0);
            attenuation = 1.0;
        }
        else
        {
            float3 delta = light.positionType.xyz - inVertex.worldPos;
            float dist = max(1e-4, length(delta));
            L = delta / dist;
            float distNorm = dist / max(1.0, referenceRadius * 2.0);
            attenuation = 1.0 / (1.0 + max(0.0, light.params0.x) * distNorm * distNorm);
        }

        float NdotL = max(0.0, dot(normal, L));
        float diffuseFactor = NdotL;
        // Keep a small artistic diffuse floor so highly metallic assets still respond to direct lights.
        float effectiveDiffuse = diffuseFactor * (1.0 - metallic * 0.92);

        float3 hRaw = L + viewDir;
        float hLenSq = dot(hRaw, hRaw);
        float3 halfVec = hLenSq > 1e-8 ? normalize(hRaw) : viewDir;
        float NdotH = max(0.0, dot(normal, halfVec));
        float VdotH = max(0.0, dot(viewDir, halfVec));
        float TdotH = dot(tangent, halfVec);
        float BdotH = dot(bitangent, halfVec);
        float rawSpec = pow(NdotH, shininess);

        float dDenom = (TdotH * TdotH) / (alphaT * alphaT) +
                       (BdotH * BdotH) / (alphaB * alphaB) +
                       (NdotH * NdotH);
        float D = 1.0 / (3.14159265 * alphaT * alphaB * dDenom * dDenom + 1e-6);

        float k = ((roughness + 1.0) * (roughness + 1.0)) / 8.0;
        float Gv = NdotV / (NdotV * (1.0 - k) + k);
        float Gl = NdotL / (NdotL * (1.0 - k) + k);
        float G = Gv * Gl;
        float3 F = F0 + (float3(1.0) - F0) * pow(1.0 - VdotH, 5.0);
        float3 specBrdf = (D * G) * F / max(4.0 * NdotV * NdotL, 1e-4);

        float shapedDiffuse = effectiveDiffuse;
        float shapedSpec = rawSpec;
        ApplyModeShaping(lightingMode, light.params0.y, light.params0.z, shapedDiffuse, shapedSpec);

        float effectiveIntensity = intensity * attenuation;
        shapedDiffuse *= effectiveIntensity;
        float specShapeScale = rawSpec > 1e-5 ? (shapedSpec / rawSpec) : 1.0;

        float metalSpecBoost = 1.0 + metallic;
        float artisticSpecBoost = 0.55 + 0.45 * max(0.0, light.params0.z);
        float3 specularTerm = specBrdf * NdotL;
        specularTerm *= specularStrength * effectiveIntensity * metalSpecBoost * max(0.0, specShapeScale) * artisticSpecBoost;

        accum += Hadamard(baseColor, lightColor) * (shapedDiffuse * diffuseStrength);
        accum += Hadamard(specularTerm, lightColor);
    }

    float3 envTop = uniforms.environmentTopColorAndIntensity.xyz;
    float envIntensity = max(0.0, uniforms.environmentTopColorAndIntensity.w);
    float3 envBottom = uniforms.environmentBottomColorAndRoughnessMix.xyz;
    float envRoughMix = clamp(uniforms.environmentBottomColorAndRoughnessMix.w, 0.0, 1.0);

    float3 R = reflect(-viewDir, normal);
    float hemi = clamp(R.y * 0.5 + 0.5, 0.0, 1.0);
    float3 envBase = mix(envBottom, envTop, hemi);
    float horizonBand = exp(-abs(R.y) * 12.0);
    float skyHotspot = pow(clamp(R.z * 0.5 + 0.5, 0.0, 1.0), 24.0) * hemi;
    float3 horizonColor = mix(envTop, float3(1.0), 0.35);
    float3 envColor = envBase + horizonColor * (0.40 * horizonBand) + float3(1.0) * (0.25 * skyHotspot);
    float envSpecWeight = 0.20 + 1.15 * metallic;
    float3 chromeTint = mix(metalSpecColor, float3(1.0), 0.35);
    float3 specTint = mix(float3(1.0), chromeTint, metallic);
    float3 envSpecular = envColor * fresnelView * specTint * envSpecWeight;
    float envDiffuseWeight = max(0.0, 1.0 - metallic);
    float3 envDiffuse = Hadamard(baseColor, envColor) * envDiffuseWeight;
    float envDiffuseEnergy = 0.35;
    float roughEnergy = mix(1.12, 0.45, roughness * envRoughMix);
    float anisotropicEnergy = mix(1.0, 1.35, anisotropy);
    float envBrush = mix(1.0, 1.08, brushStrength * topMask * (0.35 + (0.65 * surfaceCharacter)));
    accum += envDiffuse * envIntensity * envDiffuseEnergy;
    accum += envSpecular * envIntensity * roughEnergy * anisotropicEnergy * envBrush;

    if (pearlescence > 1e-4)
    {
        float pearlEdge = pow(1.0 - NdotV, 1.35);
        float pearlPhase = clamp(dot(normalize(R + viewDir), float3(0.23, 0.67, 0.71)) * 0.5 + 0.5, 0.0, 1.0);
        float3 pearlTint = 0.5 + 0.5 * cos(6.2831853 * (pearlPhase + float3(0.00, 0.33, 0.67)));
        float pearlStrength = pearlescence * (0.15 + 0.85 * pearlEdge);
        accum += pearlTint * pearlStrength * (0.20 + 0.80 * envIntensity);
    }

    accum = accum / (float3(1.0) + accum);
    accum = Clamp01(accum);
    return float4(accum, 1.0);
}";
}
