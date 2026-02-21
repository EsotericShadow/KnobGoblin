using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using KnobForge.Core.Scene;
using KnobForge.Rendering.GPU;
using SkiaSharp;

namespace KnobForge.App.Controls
{
    public sealed partial class MetalViewport
    {
        private bool TryMapPointerToPaintUvGpu(
            Point pointerDip,
            ModelNode modelNode,
            CollarNode? collarNode,
            bool drawCollar,
            float referenceRadius,
            out Vector2 uv)
        {
            uv = default;
            if (!OperatingSystem.IsMacOS() || _context is null || _project is null)
            {
                return false;
            }

            if (!EnsurePaintPickMapRendered(modelNode, collarNode, drawCollar, referenceRadius))
            {
                return false;
            }

            int width = (int)_paintPickTextureWidth;
            int height = (int)_paintPickTextureHeight;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            SKPoint screenPoint = DipToScreen(pointerDip);
            int sampleX = Math.Clamp((int)MathF.Round(screenPoint.X), 0, width - 1);
            int sampleY = Math.Clamp((int)MathF.Round(screenPoint.Y), 0, height - 1);

            if (TryReadPaintPickUv(sampleX, sampleY, out uv))
            {
                uv = ApplyBrushUvAxisInversion(uv);
                return true;
            }

            int flippedY = (height - 1) - sampleY;
            if (flippedY != sampleY && TryReadPaintPickUv(sampleX, flippedY, out uv))
            {
                uv = ApplyBrushUvAxisInversion(uv);
                return true;
            }

            return false;
        }

        private bool EnsurePaintPickMapRendered(
            ModelNode modelNode,
            CollarNode? collarNode,
            bool drawCollar,
            float referenceRadius)
        {
            if (_context is null || _project is null || _meshResources is null)
            {
                return false;
            }

            nuint width = (nuint)MathF.Max(1f, GetViewportWidthPx());
            nuint height = (nuint)MathF.Max(1f, GetViewportHeightPx());
            if (width == 0 || height == 0)
            {
                return false;
            }

            if (!EnsurePaintPickResources() || !EnsurePaintPickTargets(width, height))
            {
                return false;
            }

            if (!_paintPickMapDirty)
            {
                return true;
            }

            GpuUniforms knobUniforms = BuildUniformsForPixels(_project, modelNode, referenceRadius, width, height);
            PaintPickUniform knobPickUniform = BuildPaintPickUniform(knobUniforms, objectId: 1f / 255f);

            PaintPickUniform collarPickUniform = default;
            bool drawCollarPick =
                drawCollar &&
                _collarResources is not null &&
                _collarResources.VertexBuffer.Handle != IntPtr.Zero &&
                _collarResources.IndexBuffer.Handle != IntPtr.Zero &&
                collarNode is { Enabled: true };
            if (drawCollarPick && collarNode is not null)
            {
                GpuUniforms collarUniforms = BuildCollarUniforms(knobUniforms, collarNode);
                if (_invertImportedCollarOrbit && IsImportedCollarPreset(collarNode))
                {
                    collarUniforms.ModelRotationCosSin.Y = -collarUniforms.ModelRotationCosSin.Y;
                }

                collarPickUniform = BuildPaintPickUniform(collarUniforms, objectId: 2f / 255f);
            }

            IntPtr passDescriptor = ObjC.IntPtr_objc_msgSend(ObjCClasses.MTLRenderPassDescriptor, Selectors.RenderPassDescriptor);
            if (passDescriptor == IntPtr.Zero)
            {
                return false;
            }

            IntPtr colorAttachments = ObjC.IntPtr_objc_msgSend(passDescriptor, Selectors.ColorAttachments);
            IntPtr colorAttachment0 = ObjC.IntPtr_objc_msgSend_UInt(colorAttachments, Selectors.ObjectAtIndexedSubscript, 0);
            if (colorAttachment0 == IntPtr.Zero)
            {
                return false;
            }

            ObjC.Void_objc_msgSend_IntPtr(colorAttachment0, Selectors.SetTexture, _paintPickTexture);
            ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetLoadAction, MTLLoadActionClear);
            ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetStoreAction, MTLStoreActionStore);
            ObjC.Void_objc_msgSend_MTLClearColor(colorAttachment0, Selectors.SetClearColor, new MTLClearColor(0d, 0d, 0d, 0d));

            IntPtr depthAttachment = ObjC.IntPtr_objc_msgSend(passDescriptor, Selectors.DepthAttachment);
            if (depthAttachment != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend_IntPtr(depthAttachment, Selectors.SetTexture, _paintPickDepthTexture);
                ObjC.Void_objc_msgSend_UInt(depthAttachment, Selectors.SetLoadAction, MTLLoadActionClear);
                ObjC.Void_objc_msgSend_UInt(depthAttachment, Selectors.SetStoreAction, 0); // MTLStoreActionDontCare
                ObjC.Void_objc_msgSend_Double(depthAttachment, Selectors.SetClearDepth, 1.0);
            }

            IntPtr commandBuffer = _context.CreateCommandBuffer().Handle;
            if (commandBuffer == IntPtr.Zero)
            {
                return false;
            }

            IntPtr encoderPtr = ObjC.IntPtr_objc_msgSend_IntPtr(commandBuffer, Selectors.RenderCommandEncoderWithDescriptor, passDescriptor);
            if (encoderPtr == IntPtr.Zero)
            {
                return false;
            }

            var encoderHandle = new MTLRenderCommandEncoderHandle(encoderPtr);
            ObjC.Void_objc_msgSend_IntPtr(encoderPtr, Selectors.SetRenderPipelineState, _paintPickPipeline);
            ObjC.Void_objc_msgSend_IntPtr(encoderPtr, Selectors.SetDepthStencilState, _paintPickDepthStencilState);
            MetalPipelineManager.SetBackfaceCulling(encoderHandle, true);

            GetCameraBasis(out Vector3 right, out Vector3 up, out Vector3 forward);
            bool frontFacingClockwiseBase = ResolveFrontFacingClockwise(right, up, forward);
            bool frontFacingClockwiseKnob = _invertKnobFrontFaceWinding
                ? !frontFacingClockwiseBase
                : frontFacingClockwiseBase;
            bool frontFacingClockwiseCollar = frontFacingClockwiseBase;
            if (drawCollarPick && collarNode is not null && IsImportedCollarPreset(collarNode) && _invertImportedStlFrontFaceWinding)
            {
                frontFacingClockwiseCollar = !frontFacingClockwiseCollar;
            }

            if (drawCollarPick && _collarResources is not null)
            {
                MetalPipelineManager.SetFrontFacingWinding(encoderHandle, frontFacingClockwiseCollar);
                ObjC.Void_objc_msgSend_IntPtr_UInt_UInt(
                    encoderPtr,
                    Selectors.SetVertexBufferOffsetAtIndex,
                    _collarResources.VertexBuffer.Handle,
                    0,
                    0);
                UploadPaintPickUniform(encoderPtr, collarPickUniform);
                ObjC.Void_objc_msgSend_UInt_UInt_UInt_IntPtr_UInt(
                    encoderPtr,
                    Selectors.DrawIndexedPrimitivesIndexCountIndexTypeIndexBufferIndexBufferOffset,
                    MTLPrimitiveTypeTriangle,
                    (nuint)_collarResources.IndexCount,
                    (nuint)_collarResources.IndexType,
                    _collarResources.IndexBuffer.Handle,
                    0);
            }

            MetalPipelineManager.SetFrontFacingWinding(encoderHandle, frontFacingClockwiseKnob);
            ObjC.Void_objc_msgSend_IntPtr_UInt_UInt(
                encoderPtr,
                Selectors.SetVertexBufferOffsetAtIndex,
                _meshResources.VertexBuffer.Handle,
                0,
                0);
            UploadPaintPickUniform(encoderPtr, knobPickUniform);
            ObjC.Void_objc_msgSend_UInt_UInt_UInt_IntPtr_UInt(
                encoderPtr,
                Selectors.DrawIndexedPrimitivesIndexCountIndexTypeIndexBufferIndexBufferOffset,
                MTLPrimitiveTypeTriangle,
                (nuint)_meshResources.IndexCount,
                (nuint)_meshResources.IndexType,
                _meshResources.IndexBuffer.Handle,
                0);

            ObjC.Void_objc_msgSend(encoderPtr, Selectors.EndEncoding);
            ObjC.Void_objc_msgSend(commandBuffer, Selectors.Commit);
            ObjC.Void_objc_msgSend(commandBuffer, Selectors.WaitUntilCompleted);
            _paintPickMapDirty = false;
            return true;
        }

        private bool TryReadPaintPickUv(int sampleX, int sampleY, out Vector2 uv)
        {
            uv = default;
            if (_paintPickTexture == IntPtr.Zero)
            {
                return false;
            }

            sampleX = Math.Clamp(sampleX, 0, Math.Max(0, (int)_paintPickTextureWidth - 1));
            sampleY = Math.Clamp(sampleY, 0, Math.Max(0, (int)_paintPickTextureHeight - 1));
            Array.Clear(_paintPickReadbackPixel, 0, _paintPickReadbackPixel.Length);

            GCHandle pinned = GCHandle.Alloc(_paintPickReadbackPixel, GCHandleType.Pinned);
            try
            {
                MTLRegion region = new(
                    new MTLOrigin((nuint)sampleX, (nuint)sampleY, 0),
                    new MTLSize(1, 1, 1));
                ObjC.Void_objc_msgSend_IntPtr_UInt_MTLRegion_UInt(
                    _paintPickTexture,
                    Selectors.GetBytesBytesPerRowFromRegionMipmapLevel,
                    pinned.AddrOfPinnedObject(),
                    4,
                    region,
                    0);
            }
            finally
            {
                pinned.Free();
            }

            byte r = _paintPickReadbackPixel[0];
            byte g = _paintPickReadbackPixel[1];
            byte objectId = _paintPickReadbackPixel[2];
            byte a = _paintPickReadbackPixel[3];
            if (a == 0 || objectId == 0)
            {
                return false;
            }

            uv = new Vector2(r / 255f, g / 255f);
            return true;
        }

        private bool EnsurePaintPickResources()
        {
            if (_context is null || _context.Device.Handle == IntPtr.Zero)
            {
                return false;
            }

            if (_paintPickPipeline != IntPtr.Zero && _paintPickDepthStencilState != IntPtr.Zero)
            {
                return true;
            }

            ReleasePaintPickResources();

            IntPtr device = _context.Device.Handle;
            IntPtr sourceString = ToNSString(PaintPickShaderSource);
            IntPtr libraryError;
            IntPtr library = ObjC.IntPtr_objc_msgSend_IntPtr_IntPtr_outIntPtr(
                device,
                Selectors.NewLibraryWithSourceOptionsError,
                sourceString,
                IntPtr.Zero,
                out libraryError);
            if (library == IntPtr.Zero)
            {
                LogPaintStampError($"Failed to compile paint pick shader: {DescribeNSError(libraryError)}");
                return false;
            }

            IntPtr vertexName = ToNSString("vertex_paint_pick");
            IntPtr fragmentName = ToNSString("fragment_paint_pick");
            IntPtr vertexFunction = ObjC.IntPtr_objc_msgSend_IntPtr(library, Selectors.NewFunctionWithName, vertexName);
            IntPtr fragmentFunction = ObjC.IntPtr_objc_msgSend_IntPtr(library, Selectors.NewFunctionWithName, fragmentName);
            if (vertexFunction == IntPtr.Zero || fragmentFunction == IntPtr.Zero)
            {
                LogPaintStampError("Paint pick shader entry points were not found.");
                if (vertexFunction != IntPtr.Zero)
                {
                    ObjC.Void_objc_msgSend(vertexFunction, Selectors.Release);
                }

                if (fragmentFunction != IntPtr.Zero)
                {
                    ObjC.Void_objc_msgSend(fragmentFunction, Selectors.Release);
                }

                ObjC.Void_objc_msgSend(library, Selectors.Release);
                return false;
            }

            IntPtr pipeline = CreatePaintPickPipelineState(device, vertexFunction, fragmentFunction);
            IntPtr depthStencilState = CreateDepthStencilState(device, depthWriteEnabled: true);
            if (pipeline == IntPtr.Zero || depthStencilState == IntPtr.Zero)
            {
                if (pipeline != IntPtr.Zero)
                {
                    ObjC.Void_objc_msgSend(pipeline, Selectors.Release);
                }

                if (depthStencilState != IntPtr.Zero)
                {
                    ObjC.Void_objc_msgSend(depthStencilState, Selectors.Release);
                }

                ObjC.Void_objc_msgSend(vertexFunction, Selectors.Release);
                ObjC.Void_objc_msgSend(fragmentFunction, Selectors.Release);
                ObjC.Void_objc_msgSend(library, Selectors.Release);
                return false;
            }

            _paintPickLibrary = library;
            _paintPickVertexFunction = vertexFunction;
            _paintPickFragmentFunction = fragmentFunction;
            _paintPickPipeline = pipeline;
            _paintPickDepthStencilState = depthStencilState;
            _paintPickMapDirty = true;
            return true;
        }

        private bool EnsurePaintPickTargets(nuint width, nuint height)
        {
            if (_context is null || _context.Device.Handle == IntPtr.Zero)
            {
                return false;
            }

            if (_paintPickTexture != IntPtr.Zero &&
                _paintPickDepthTexture != IntPtr.Zero &&
                _paintPickTextureWidth == width &&
                _paintPickTextureHeight == height)
            {
                return true;
            }

            ReleasePaintPickTargets();

            IntPtr colorDescriptor = ObjC.IntPtr_objc_msgSend_UInt_UInt_UInt_Bool(
                ObjCClasses.MTLTextureDescriptor,
                Selectors.Texture2DDescriptorWithPixelFormatWidthHeightMipmapped,
                PaintMaskPixelFormat,
                width,
                height,
                false);
            if (colorDescriptor == IntPtr.Zero)
            {
                return false;
            }

            ObjC.Void_objc_msgSend_UInt(colorDescriptor, Selectors.SetUsage, 4); // MTLTextureUsageRenderTarget
            ObjC.Void_objc_msgSend_UInt(colorDescriptor, Selectors.SetStorageMode, 0); // MTLStorageModeShared
            IntPtr colorTexture = ObjC.IntPtr_objc_msgSend_IntPtr(_context.Device.Handle, Selectors.NewTextureWithDescriptor, colorDescriptor);
            if (colorTexture == IntPtr.Zero)
            {
                return false;
            }

            IntPtr depthDescriptor = ObjC.IntPtr_objc_msgSend_UInt_UInt_UInt_Bool(
                ObjCClasses.MTLTextureDescriptor,
                Selectors.Texture2DDescriptorWithPixelFormatWidthHeightMipmapped,
                DepthPixelFormat,
                width,
                height,
                false);
            if (depthDescriptor == IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(colorTexture, Selectors.Release);
                return false;
            }

            ObjC.Void_objc_msgSend_UInt(depthDescriptor, Selectors.SetUsage, 4); // MTLTextureUsageRenderTarget
            IntPtr depthTexture = ObjC.IntPtr_objc_msgSend_IntPtr(_context.Device.Handle, Selectors.NewTextureWithDescriptor, depthDescriptor);
            if (depthTexture == IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(colorTexture, Selectors.Release);
                return false;
            }

            _paintPickTexture = colorTexture;
            _paintPickDepthTexture = depthTexture;
            _paintPickTextureWidth = width;
            _paintPickTextureHeight = height;
            _paintPickMapDirty = true;
            return true;
        }

        private void ReleasePaintPickTargets()
        {
            if (_paintPickDepthTexture != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_paintPickDepthTexture, Selectors.Release);
                _paintPickDepthTexture = IntPtr.Zero;
            }

            if (_paintPickTexture != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_paintPickTexture, Selectors.Release);
                _paintPickTexture = IntPtr.Zero;
            }

            _paintPickTextureWidth = 0;
            _paintPickTextureHeight = 0;
            _paintPickMapDirty = true;
        }

        private void ReleasePaintPickResources()
        {
            ReleasePaintPickTargets();

            if (_paintPickPipeline != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_paintPickPipeline, Selectors.Release);
                _paintPickPipeline = IntPtr.Zero;
            }

            if (_paintPickDepthStencilState != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_paintPickDepthStencilState, Selectors.Release);
                _paintPickDepthStencilState = IntPtr.Zero;
            }

            if (_paintPickVertexFunction != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_paintPickVertexFunction, Selectors.Release);
                _paintPickVertexFunction = IntPtr.Zero;
            }

            if (_paintPickFragmentFunction != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_paintPickFragmentFunction, Selectors.Release);
                _paintPickFragmentFunction = IntPtr.Zero;
            }

            if (_paintPickLibrary != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_paintPickLibrary, Selectors.Release);
                _paintPickLibrary = IntPtr.Zero;
            }

            _paintPickMapDirty = true;
        }

        private static IntPtr CreatePaintPickPipelineState(IntPtr device, IntPtr vertexFunction, IntPtr fragmentFunction)
        {
            IntPtr descriptor = ObjC.IntPtr_objc_msgSend(
                ObjC.IntPtr_objc_msgSend(ObjCClasses.MTLRenderPipelineDescriptor, Selectors.Alloc),
                Selectors.Init);
            if (descriptor == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            ObjC.Void_objc_msgSend_IntPtr(descriptor, Selectors.SetVertexFunction, vertexFunction);
            ObjC.Void_objc_msgSend_IntPtr(descriptor, Selectors.SetFragmentFunction, fragmentFunction);

            IntPtr colorAttachments = ObjC.IntPtr_objc_msgSend(descriptor, Selectors.ColorAttachments);
            IntPtr colorAttachment0 = ObjC.IntPtr_objc_msgSend_UInt(colorAttachments, Selectors.ObjectAtIndexedSubscript, 0);
            if (colorAttachment0 == IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(descriptor, Selectors.Release);
                return IntPtr.Zero;
            }

            ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetPixelFormat, PaintMaskPixelFormat);
            ObjC.Void_objc_msgSend_Bool(colorAttachment0, Selectors.SetBlendingEnabled, false);
            ObjC.Void_objc_msgSend_UInt(descriptor, Selectors.SetDepthAttachmentPixelFormat, DepthPixelFormat);

            IntPtr pipelineError;
            IntPtr pipeline = ObjC.IntPtr_objc_msgSend_IntPtr_outIntPtr(
                device,
                Selectors.NewRenderPipelineStateWithDescriptorError,
                descriptor,
                out pipelineError);
            ObjC.Void_objc_msgSend(descriptor, Selectors.Release);
            if (pipeline == IntPtr.Zero)
            {
                LogPaintStampError($"Failed to create paint pick pipeline: {DescribeNSError(pipelineError)}");
            }

            return pipeline;
        }

        private static IntPtr CreateDepthStencilState(IntPtr device, bool depthWriteEnabled)
        {
            IntPtr descriptor = ObjC.IntPtr_objc_msgSend(
                ObjC.IntPtr_objc_msgSend(ObjCClasses.MTLDepthStencilDescriptor, Selectors.Alloc),
                Selectors.Init);
            if (descriptor == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            ObjC.Void_objc_msgSend_UInt(descriptor, Selectors.SetDepthCompareFunction, 3); // MTLCompareFunctionLessEqual
            ObjC.Void_objc_msgSend_Bool(descriptor, Selectors.SetDepthWriteEnabled, depthWriteEnabled);

            IntPtr depthStencilState = ObjC.IntPtr_objc_msgSend_IntPtr(
                device,
                Selectors.NewDepthStencilStateWithDescriptor,
                descriptor);
            ObjC.Void_objc_msgSend(descriptor, Selectors.Release);
            return depthStencilState;
        }

        private static PaintPickUniform BuildPaintPickUniform(in GpuUniforms uniforms, float objectId)
        {
            return new PaintPickUniform
            {
                CameraPosAndReferenceRadius = uniforms.CameraPosAndReferenceRadius,
                RightAndScaleX = uniforms.RightAndScaleX,
                UpAndScaleY = uniforms.UpAndScaleY,
                ForwardAndScaleZ = uniforms.ForwardAndScaleZ,
                ProjectionOffsetsAndObjectId = new Vector4(
                    uniforms.ProjectionOffsetsAndLightCount.X,
                    uniforms.ProjectionOffsetsAndLightCount.Y,
                    objectId,
                    0f),
                ModelRotationCosSin = uniforms.ModelRotationCosSin
            };
        }

        private void UploadPaintPickUniform(IntPtr encoderPtr, in PaintPickUniform uniform)
        {
            if (encoderPtr == IntPtr.Zero)
            {
                return;
            }

            int uniformSize = Marshal.SizeOf<PaintPickUniform>();
            IntPtr uniformPtr = EnsureUniformUploadScratchBuffer(uniformSize, paintStamp: false);
            if (uniformPtr == IntPtr.Zero)
            {
                return;
            }

            Marshal.StructureToPtr(uniform, uniformPtr, false);
            ObjC.Void_objc_msgSend_IntPtr_UInt_UInt(
                encoderPtr,
                Selectors.SetVertexBytesLengthAtIndex,
                uniformPtr,
                (nuint)uniformSize,
                1);
        }
    }
}
