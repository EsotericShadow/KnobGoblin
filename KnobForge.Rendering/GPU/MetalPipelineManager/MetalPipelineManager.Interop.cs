using System;
using System.Runtime.InteropServices;

namespace KnobForge.Rendering.GPU;

public sealed partial class MetalPipelineManager
{
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
}
