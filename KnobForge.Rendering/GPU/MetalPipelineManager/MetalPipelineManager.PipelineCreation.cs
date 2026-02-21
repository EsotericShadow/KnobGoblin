using System;
using System.IO;
using System.Runtime.InteropServices;

namespace KnobForge.Rendering.GPU;

public sealed partial class MetalPipelineManager
{
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
}
