using System;
using System.IO;
using System.Runtime.InteropServices;

namespace KnobForge.Rendering.GPU;

public sealed partial class MetalPipelineManager
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
}
