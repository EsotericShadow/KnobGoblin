using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace KnobForge.App.Controls
{
    public sealed partial class MetalViewport
    {
        private static class ObjCClasses
        {
            public static readonly IntPtr NSString = ObjC.objc_getClass("NSString");
            public static readonly IntPtr NSView = ObjC.objc_getClass("NSView");
            public static readonly IntPtr CAMetalLayer = ObjC.objc_getClass("CAMetalLayer");
            public static readonly IntPtr MTLRenderPassDescriptor = ObjC.objc_getClass("MTLRenderPassDescriptor");
            public static readonly IntPtr MTLTextureDescriptor = ObjC.objc_getClass("MTLTextureDescriptor");
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
            public static readonly IntPtr SetLayer = ObjC.sel_registerName("setLayer:");
            public static readonly IntPtr SetWantsLayer = ObjC.sel_registerName("setWantsLayer:");
            public static readonly IntPtr SetFrame = ObjC.sel_registerName("setFrame:");
            public static readonly IntPtr SetDevice = ObjC.sel_registerName("setDevice:");
            public static readonly IntPtr SetPixelFormat = ObjC.sel_registerName("setPixelFormat:");
            public static readonly IntPtr SetFramebufferOnly = ObjC.sel_registerName("setFramebufferOnly:");
            public static readonly IntPtr SetContentsScale = ObjC.sel_registerName("setContentsScale:");
            public static readonly IntPtr SetDrawableSize = ObjC.sel_registerName("setDrawableSize:");
            public static readonly IntPtr NextDrawable = ObjC.sel_registerName("nextDrawable");
            public static readonly IntPtr Texture = ObjC.sel_registerName("texture");
            public static readonly IntPtr Width = ObjC.sel_registerName("width");
            public static readonly IntPtr Height = ObjC.sel_registerName("height");
            public static readonly IntPtr RenderPassDescriptor = ObjC.sel_registerName("renderPassDescriptor");
            public static readonly IntPtr ColorAttachments = ObjC.sel_registerName("colorAttachments");
            public static readonly IntPtr DepthAttachment = ObjC.sel_registerName("depthAttachment");
            public static readonly IntPtr NewLibraryWithSourceOptionsError = ObjC.sel_registerName("newLibraryWithSource:options:error:");
            public static readonly IntPtr NewFunctionWithName = ObjC.sel_registerName("newFunctionWithName:");
            public static readonly IntPtr ObjectAtIndexedSubscript = ObjC.sel_registerName("objectAtIndexedSubscript:");
            public static readonly IntPtr SetTexture = ObjC.sel_registerName("setTexture:");
            public static readonly IntPtr SetLoadAction = ObjC.sel_registerName("setLoadAction:");
            public static readonly IntPtr SetStoreAction = ObjC.sel_registerName("setStoreAction:");
            public static readonly IntPtr SetClearColor = ObjC.sel_registerName("setClearColor:");
            public static readonly IntPtr SetClearDepth = ObjC.sel_registerName("setClearDepth:");
            public static readonly IntPtr Texture2DDescriptorWithPixelFormatWidthHeightMipmapped =
                ObjC.sel_registerName("texture2DDescriptorWithPixelFormat:width:height:mipmapped:");
            public static readonly IntPtr SetUsage = ObjC.sel_registerName("setUsage:");
            public static readonly IntPtr SetStorageMode = ObjC.sel_registerName("setStorageMode:");
            public static readonly IntPtr NewTextureWithDescriptor = ObjC.sel_registerName("newTextureWithDescriptor:");
            public static readonly IntPtr RenderCommandEncoderWithDescriptor = ObjC.sel_registerName("renderCommandEncoderWithDescriptor:");
            public static readonly IntPtr SetVertexFunction = ObjC.sel_registerName("setVertexFunction:");
            public static readonly IntPtr SetFragmentFunction = ObjC.sel_registerName("setFragmentFunction:");
            public static readonly IntPtr SetRenderPipelineState = ObjC.sel_registerName("setRenderPipelineState:");
            public static readonly IntPtr SetBlendingEnabled = ObjC.sel_registerName("setBlendingEnabled:");
            public static readonly IntPtr SetRgbBlendOperation = ObjC.sel_registerName("setRgbBlendOperation:");
            public static readonly IntPtr SetAlphaBlendOperation = ObjC.sel_registerName("setAlphaBlendOperation:");
            public static readonly IntPtr SetSourceRGBBlendFactor = ObjC.sel_registerName("setSourceRGBBlendFactor:");
            public static readonly IntPtr SetDestinationRGBBlendFactor = ObjC.sel_registerName("setDestinationRGBBlendFactor:");
            public static readonly IntPtr SetSourceAlphaBlendFactor = ObjC.sel_registerName("setSourceAlphaBlendFactor:");
            public static readonly IntPtr SetDestinationAlphaBlendFactor = ObjC.sel_registerName("setDestinationAlphaBlendFactor:");
            public static readonly IntPtr SetWriteMask = ObjC.sel_registerName("setWriteMask:");
            public static readonly IntPtr SetDepthAttachmentPixelFormat = ObjC.sel_registerName("setDepthAttachmentPixelFormat:");
            public static readonly IntPtr SetDepthCompareFunction = ObjC.sel_registerName("setDepthCompareFunction:");
            public static readonly IntPtr SetDepthWriteEnabled = ObjC.sel_registerName("setDepthWriteEnabled:");
            public static readonly IntPtr NewDepthStencilStateWithDescriptor = ObjC.sel_registerName("newDepthStencilStateWithDescriptor:");
            public static readonly IntPtr SetDepthStencilState = ObjC.sel_registerName("setDepthStencilState:");
            public static readonly IntPtr SetCullMode = ObjC.sel_registerName("setCullMode:");
            public static readonly IntPtr NewRenderPipelineStateWithDescriptorError = ObjC.sel_registerName("newRenderPipelineStateWithDescriptor:error:");
            public static readonly IntPtr SetVertexBufferOffsetAtIndex = ObjC.sel_registerName("setVertexBuffer:offset:atIndex:");
            public static readonly IntPtr SetVertexTextureAtIndex = ObjC.sel_registerName("setVertexTexture:atIndex:");
            public static readonly IntPtr SetVertexBytesLengthAtIndex = ObjC.sel_registerName("setVertexBytes:length:atIndex:");
            public static readonly IntPtr SetFragmentBytesLengthAtIndex = ObjC.sel_registerName("setFragmentBytes:length:atIndex:");
            public static readonly IntPtr SetFragmentTextureAtIndex = ObjC.sel_registerName("setFragmentTexture:atIndex:");
            public static readonly IntPtr ReplaceRegionMipmapLevelWithBytesBytesPerRow =
                ObjC.sel_registerName("replaceRegion:mipmapLevel:withBytes:bytesPerRow:");
            public static readonly IntPtr GetBytesBytesPerRowFromRegionMipmapLevel =
                ObjC.sel_registerName("getBytes:bytesPerRow:fromRegion:mipmapLevel:");
            public static readonly IntPtr DrawPrimitivesVertexStartVertexCount =
                ObjC.sel_registerName("drawPrimitives:vertexStart:vertexCount:");
            public static readonly IntPtr DrawIndexedPrimitivesIndexCountIndexTypeIndexBufferIndexBufferOffset =
                ObjC.sel_registerName("drawIndexedPrimitives:indexCount:indexType:indexBuffer:indexBufferOffset:");
            public static readonly IntPtr BlitCommandEncoder = ObjC.sel_registerName("blitCommandEncoder");
            public static readonly IntPtr GenerateMipmapsForTexture = ObjC.sel_registerName("generateMipmapsForTexture:");
            public static readonly IntPtr EndEncoding = ObjC.sel_registerName("endEncoding");
            public static readonly IntPtr PresentDrawable = ObjC.sel_registerName("presentDrawable:");
            public static readonly IntPtr Commit = ObjC.sel_registerName("commit");
            public static readonly IntPtr WaitUntilCompleted = ObjC.sel_registerName("waitUntilCompleted");
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
            public static extern IntPtr IntPtr_objc_msgSend_UInt_UInt_UInt_Bool(
                IntPtr receiver,
                IntPtr selector,
                nuint arg1,
                nuint arg2,
                nuint arg3,
                bool arg4);

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
            public static extern nuint UInt_objc_msgSend(IntPtr receiver, IntPtr selector);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            public static extern void Void_objc_msgSend(IntPtr receiver, IntPtr selector);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            public static extern void Void_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg1);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            public static extern void Void_objc_msgSend_IntPtr_UInt(
                IntPtr receiver,
                IntPtr selector,
                IntPtr arg1,
                nuint arg2);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            public static extern void Void_objc_msgSend_UInt(IntPtr receiver, IntPtr selector, nuint arg1);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            public static extern void Void_objc_msgSend_Bool(IntPtr receiver, IntPtr selector, bool arg1);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            public static extern void Void_objc_msgSend_Double(IntPtr receiver, IntPtr selector, double arg1);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            public static extern void Void_objc_msgSend_IntPtr_UInt_UInt(
                IntPtr receiver,
                IntPtr selector,
                IntPtr arg1,
                nuint arg2,
                nuint arg3);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            public static extern void Void_objc_msgSend_UInt_UInt_UInt(
                IntPtr receiver,
                IntPtr selector,
                nuint arg1,
                nuint arg2,
                nuint arg3);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            public static extern void Void_objc_msgSend_UInt_UInt_UInt_IntPtr_UInt(
                IntPtr receiver,
                IntPtr selector,
                nuint arg1,
                nuint arg2,
                nuint arg3,
                IntPtr arg4,
                nuint arg5);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            public static extern void Void_objc_msgSend_MTLRegion_UInt_IntPtr_UInt(
                IntPtr receiver,
                IntPtr selector,
                MTLRegion arg1,
                nuint arg2,
                IntPtr arg3,
                nuint arg4);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            public static extern void Void_objc_msgSend_IntPtr_UInt_MTLRegion_UInt(
                IntPtr receiver,
                IntPtr selector,
                IntPtr arg1,
                nuint arg2,
                MTLRegion arg3,
                nuint arg4);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            public static extern void Void_objc_msgSend_MTLClearColor(
                IntPtr receiver,
                IntPtr selector,
                MTLClearColor arg1);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            public static extern void Void_objc_msgSend_CGRect(
                IntPtr receiver,
                IntPtr selector,
                CGRect arg1);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            public static extern void Void_objc_msgSend_CGSize(
                IntPtr receiver,
                IntPtr selector,
                CGSize arg1);
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct CGSize
        {
            public readonly double Width;
            public readonly double Height;

            public CGSize(double width, double height)
            {
                Width = width;
                Height = height;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct CGPoint
        {
            public readonly double X;
            public readonly double Y;

            public CGPoint(double x, double y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct CGRect
        {
            public readonly CGPoint Origin;
            public readonly CGSize Size;

            public CGRect(double x, double y, double width, double height)
            {
                Origin = new CGPoint(x, y);
                Size = new CGSize(width, height);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct MTLClearColor
        {
            public readonly double Red;
            public readonly double Green;
            public readonly double Blue;
            public readonly double Alpha;

            public MTLClearColor(double red, double green, double blue, double alpha)
            {
                Red = red;
                Green = green;
                Blue = blue;
                Alpha = alpha;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct MTLOrigin
        {
            public readonly nuint X;
            public readonly nuint Y;
            public readonly nuint Z;

            public MTLOrigin(nuint x, nuint y, nuint z)
            {
                X = x;
                Y = y;
                Z = z;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct MTLSize
        {
            public readonly nuint Width;
            public readonly nuint Height;
            public readonly nuint Depth;

            public MTLSize(nuint width, nuint height, nuint depth)
            {
                Width = width;
                Height = height;
                Depth = depth;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct MTLRegion
        {
            public readonly MTLOrigin Origin;
            public readonly MTLSize Size;

            public MTLRegion(MTLOrigin origin, MTLSize size)
            {
                Origin = origin;
                Size = size;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PaintStampUniform
        {
            public Vector4 CenterRadiusOpacity;
            public Vector4 Params0;
            public Vector4 Params1;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PaintPickUniform
        {
            public Vector4 CameraPosAndReferenceRadius;
            public Vector4 RightAndScaleX;
            public Vector4 UpAndScaleY;
            public Vector4 ForwardAndScaleZ;
            public Vector4 ProjectionOffsetsAndObjectId;
            public Vector4 ModelRotationCosSin;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LightGizmoOverlayLight
        {
            public Vector4 PositionAndOrigin;
            public Vector4 DirectionTipAndRadii;
            public Vector4 TipRadiusAndAlpha;
            public Vector4 Color;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LightGizmoOverlayUniform
        {
            public Vector4 ViewportAndCount;
            public LightGizmoOverlayLight Light0;
            public LightGizmoOverlayLight Light1;
            public LightGizmoOverlayLight Light2;
            public LightGizmoOverlayLight Light3;
            public LightGizmoOverlayLight Light4;
            public LightGizmoOverlayLight Light5;
            public LightGizmoOverlayLight Light6;
            public LightGizmoOverlayLight Light7;
        }

    }
}
