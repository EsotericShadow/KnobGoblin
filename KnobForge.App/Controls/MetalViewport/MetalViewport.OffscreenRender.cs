using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using KnobForge.Core;
using KnobForge.Core.Scene;
using KnobForge.Rendering;
using KnobForge.Rendering.GPU;
using SkiaSharp;

namespace KnobForge.App.Controls
{
    public sealed partial class MetalViewport
    {
        public bool TryRenderFrameToBitmap(int widthPx, int heightPx, ViewportCameraState cameraState, out SKBitmap? bitmap)
        {
            bitmap = null;
            if (!CanRenderOffscreen || _context is null || _project is null)
            {
                return false;
            }

            ModelNode? modelNode = _project.SceneRoot.Children.OfType<ModelNode>().FirstOrDefault();
            if (modelNode is null)
            {
                return false;
            }

            int width = Math.Max(1, widthPx);
            int height = Math.Max(1, heightPx);

            float savedYaw = _orbitYawDeg;
            float savedPitch = _orbitPitchDeg;
            float savedZoom = _zoom;
            Vector2 savedPan = _panPx;

            IntPtr colorTexture = IntPtr.Zero;
            IntPtr depthTexture = IntPtr.Zero;

            try
            {
                _orbitYawDeg = cameraState.OrbitYawDeg;
                _orbitPitchDeg = cameraState.OrbitPitchDeg;
                _zoom = cameraState.Zoom;
                _panPx = new Vector2(cameraState.PanPx.X, cameraState.PanPx.Y);

                RefreshMeshResources(_project, modelNode);
                CollarNode? collarNode = modelNode.Children.OfType<CollarNode>().FirstOrDefault();
                if (_meshResources == null ||
                    _meshResources.VertexBuffer.Handle == IntPtr.Zero ||
                    _meshResources.IndexBuffer.Handle == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr colorDescriptor = ObjC.IntPtr_objc_msgSend_UInt_UInt_UInt_Bool(
                    ObjCClasses.MTLTextureDescriptor,
                    Selectors.Texture2DDescriptorWithPixelFormatWidthHeightMipmapped,
                    (nuint)MetalRendererContext.DefaultColorFormat,
                    (nuint)width,
                    (nuint)height,
                    false);
                if (colorDescriptor == IntPtr.Zero)
                {
                    return false;
                }

                ObjC.Void_objc_msgSend_UInt(colorDescriptor, Selectors.SetUsage, 4); // MTLTextureUsageRenderTarget
                ObjC.Void_objc_msgSend_UInt(colorDescriptor, Selectors.SetStorageMode, 0); // MTLStorageModeShared
                colorTexture = ObjC.IntPtr_objc_msgSend_IntPtr(_context.Device.Handle, Selectors.NewTextureWithDescriptor, colorDescriptor);
                if (colorTexture == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr depthDescriptor = ObjC.IntPtr_objc_msgSend_UInt_UInt_UInt_Bool(
                    ObjCClasses.MTLTextureDescriptor,
                    Selectors.Texture2DDescriptorWithPixelFormatWidthHeightMipmapped,
                    DepthPixelFormat,
                    (nuint)width,
                    (nuint)height,
                    false);
                if (depthDescriptor == IntPtr.Zero)
                {
                    return false;
                }

                ObjC.Void_objc_msgSend_UInt(depthDescriptor, Selectors.SetUsage, 4); // MTLTextureUsageRenderTarget
                depthTexture = ObjC.IntPtr_objc_msgSend_IntPtr(_context.Device.Handle, Selectors.NewTextureWithDescriptor, depthDescriptor);
                if (depthTexture == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr passDescriptor = ObjC.IntPtr_objc_msgSend(ObjCClasses.MTLRenderPassDescriptor, Selectors.RenderPassDescriptor);
                if (passDescriptor == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr colorAttachments = ObjC.IntPtr_objc_msgSend(passDescriptor, Selectors.ColorAttachments);
                IntPtr colorAttachment = ObjC.IntPtr_objc_msgSend_UInt(colorAttachments, Selectors.ObjectAtIndexedSubscript, 0);
                if (colorAttachment == IntPtr.Zero)
                {
                    return false;
                }

                ObjC.Void_objc_msgSend_IntPtr(colorAttachment, Selectors.SetTexture, colorTexture);
                ObjC.Void_objc_msgSend_UInt(colorAttachment, Selectors.SetLoadAction, 2); // MTLLoadActionClear
                ObjC.Void_objc_msgSend_UInt(colorAttachment, Selectors.SetStoreAction, 1); // MTLStoreActionStore
                // Offscreen export should preserve transparency for PNG alpha compositing (e.g., shadows).
                ObjC.Void_objc_msgSend_MTLClearColor(colorAttachment, Selectors.SetClearColor, new MTLClearColor(0d, 0d, 0d, 0d));

                IntPtr depthAttachment = ObjC.IntPtr_objc_msgSend(passDescriptor, Selectors.DepthAttachment);
                if (depthAttachment != IntPtr.Zero)
                {
                    ObjC.Void_objc_msgSend_IntPtr(depthAttachment, Selectors.SetTexture, depthTexture);
                    ObjC.Void_objc_msgSend_UInt(depthAttachment, Selectors.SetLoadAction, 2); // MTLLoadActionClear
                    ObjC.Void_objc_msgSend_UInt(depthAttachment, Selectors.SetStoreAction, 0); // MTLStoreActionDontCare
                    ObjC.Void_objc_msgSend_Double(depthAttachment, Selectors.SetClearDepth, 1.0);
                }

                IntPtr commandBuffer = _context.CreateCommandBuffer().Handle;
                if (commandBuffer == IntPtr.Zero)
                {
                    return false;
                }

                bool drawCollar = _collarResources != null && _collarResources.VertexBuffer.Handle != IntPtr.Zero && _collarResources.IndexBuffer.Handle != IntPtr.Zero && collarNode is { Enabled: true };
                if (!_offscreenCollarStateLogged)
                {
                    _offscreenCollarStateLogged = true;
                    LogCollarState("offscreen", collarNode, _collarResources);
                }
                float sceneReferenceRadius = drawCollar
                    ? MathF.Max(_meshResources.ReferenceRadius, _collarResources!.ReferenceRadius)
                    : _meshResources.ReferenceRadius;
                GpuUniforms knobUniforms = BuildUniformsForPixels(_project, modelNode, sceneReferenceRadius, width, height);
                GpuUniforms collarUniforms = drawCollar
                    ? BuildCollarUniforms(knobUniforms, collarNode!)
                    : default;
                EnsurePaintMaskTexture(_project);
                EnsurePaintColorTexture(_project);
                ApplyPendingPaintStamps(commandBuffer);

                IntPtr encoderPtr = ObjC.IntPtr_objc_msgSend_IntPtr(commandBuffer, Selectors.RenderCommandEncoderWithDescriptor, passDescriptor);
                if (encoderPtr == IntPtr.Zero)
                {
                    return false;
                }

                MetalPipelineManager pipelineManager = MetalPipelineManager.Instance;
                pipelineManager.UsePipeline(new MTLRenderCommandEncoderHandle(encoderPtr));
                if (drawCollar && _invertImportedCollarOrbit && IsImportedCollarPreset(collarNode))
                {
                    collarUniforms.ModelRotationCosSin.Y = -collarUniforms.ModelRotationCosSin.Y;
                }
                ObjC.Void_objc_msgSend_IntPtr_UInt(
                    encoderPtr,
                    Selectors.SetVertexTextureAtIndex,
                    _paintMaskTexture,
                    1);
                ObjC.Void_objc_msgSend_IntPtr_UInt(
                    encoderPtr,
                    Selectors.SetFragmentTextureAtIndex,
                    _spiralNormalTexture,
                    0);
                ObjC.Void_objc_msgSend_IntPtr_UInt(
                    encoderPtr,
                    Selectors.SetFragmentTextureAtIndex,
                    _paintMaskTexture,
                    1);
                ObjC.Void_objc_msgSend_IntPtr_UInt(
                    encoderPtr,
                    Selectors.SetFragmentTextureAtIndex,
                    _paintColorTexture,
                    2);
                GetCameraBasis(out Vector3 right, out Vector3 up, out Vector3 forward);
                bool frontFacingClockwiseBase = ResolveFrontFacingClockwise(right, up, forward);
                bool frontFacingClockwiseKnob = _invertKnobFrontFaceWinding
                    ? !frontFacingClockwiseBase
                    : frontFacingClockwiseBase;
                bool frontFacingClockwiseCollar = frontFacingClockwiseBase;
                if (drawCollar &&
                    IsImportedCollarPreset(collarNode) &&
                    _invertImportedStlFrontFaceWinding)
                {
                    frontFacingClockwiseCollar = !frontFacingClockwiseCollar;
                }
                IReadOnlyList<ShadowPassConfig> shadowConfigs = ResolveShadowPassConfigs(_project, right, up, forward, width, height);

                bool collarDrawExecuted = false;
                if (drawCollar)
                {
                    MetalPipelineManager.SetFrontFacingWinding(
                        new MTLRenderCommandEncoderHandle(encoderPtr),
                        frontFacingClockwiseCollar);
                    ObjC.Void_objc_msgSend_IntPtr_UInt_UInt(
                        encoderPtr,
                        Selectors.SetVertexBufferOffsetAtIndex,
                        _collarResources!.VertexBuffer.Handle,
                        0,
                        0);
                    UploadUniforms(encoderPtr, collarUniforms);
                    ObjC.Void_objc_msgSend_UInt_UInt_UInt_IntPtr_UInt(
                        encoderPtr,
                        Selectors.DrawIndexedPrimitivesIndexCountIndexTypeIndexBufferIndexBufferOffset,
                        3, // MTLPrimitiveTypeTriangle
                        (nuint)_collarResources.IndexCount,
                        (nuint)_collarResources.IndexType,
                        _collarResources.IndexBuffer.Handle,
                        0);
                    collarDrawExecuted = true;
                }

                if (collarNode is { Enabled: true } &&
                    _collarResources != null &&
                    _collarResources.VertexBuffer.Handle != IntPtr.Zero &&
                    _collarResources.IndexBuffer.Handle != IntPtr.Zero &&
                    !collarDrawExecuted)
                {
                    throw new InvalidOperationException("Collar was enabled with valid GPU resources but was not drawn in offscreen render.");
                }

                MetalPipelineManager.SetFrontFacingWinding(
                    new MTLRenderCommandEncoderHandle(encoderPtr),
                    frontFacingClockwiseKnob);
                ObjC.Void_objc_msgSend_IntPtr_UInt_UInt(
                    encoderPtr,
                    Selectors.SetVertexBufferOffsetAtIndex,
                    _meshResources.VertexBuffer.Handle,
                    0,
                    0);
                UploadUniforms(encoderPtr, knobUniforms);
                ObjC.Void_objc_msgSend_UInt_UInt_UInt_IntPtr_UInt(
                    encoderPtr,
                    Selectors.DrawIndexedPrimitivesIndexCountIndexTypeIndexBufferIndexBufferOffset,
                    3, // MTLPrimitiveTypeTriangle
                    (nuint)_meshResources.IndexCount,
                    (nuint)_meshResources.IndexType,
                    _meshResources.IndexBuffer.Handle,
                    0);

                if (shadowConfigs.Count > 0)
                {
                    pipelineManager.UseDepthReadOnlyState(new MTLRenderCommandEncoderHandle(encoderPtr));
                    for (int shadowIndex = 0; shadowIndex < shadowConfigs.Count; shadowIndex++)
                    {
                        ShadowPassConfig shadowConfig = shadowConfigs[shadowIndex];
                        if (!shadowConfig.Enabled)
                        {
                            continue;
                        }

                        if (drawCollar)
                        {
                            MetalPipelineManager.SetFrontFacingWinding(
                                new MTLRenderCommandEncoderHandle(encoderPtr),
                                frontFacingClockwiseCollar);
                            RenderShadowPasses(encoderPtr, collarUniforms, shadowConfig, _collarResources!);
                        }

                        MetalPipelineManager.SetFrontFacingWinding(
                            new MTLRenderCommandEncoderHandle(encoderPtr),
                            frontFacingClockwiseKnob);
                        RenderShadowPasses(encoderPtr, knobUniforms, shadowConfig, _meshResources);
                    }
                    pipelineManager.UseDepthWriteState(new MTLRenderCommandEncoderHandle(encoderPtr));
                }

                ObjC.Void_objc_msgSend(encoderPtr, Selectors.EndEncoding);
                ObjC.Void_objc_msgSend(commandBuffer, Selectors.Commit);
                ObjC.Void_objc_msgSend(commandBuffer, Selectors.WaitUntilCompleted);

                int bytesPerRow = width * 4;
                int byteCount = bytesPerRow * height;
                byte[] pixelBytes = new byte[byteCount];
                GCHandle pinned = GCHandle.Alloc(pixelBytes, GCHandleType.Pinned);
                try
                {
                    MTLRegion region = new(
                        new MTLOrigin(0, 0, 0),
                        new MTLSize((nuint)width, (nuint)height, 1));
                    ObjC.Void_objc_msgSend_IntPtr_UInt_MTLRegion_UInt(
                        colorTexture,
                        Selectors.GetBytesBytesPerRowFromRegionMipmapLevel,
                        pinned.AddrOfPinnedObject(),
                        (nuint)bytesPerRow,
                        region,
                        0);
                }
                finally
                {
                    pinned.Free();
                }

                // GPU blending writes premultiplied color/alpha; keep Premul to preserve soft shadow alpha on export.
                bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
                IntPtr destination = bitmap.GetPixels();
                Marshal.Copy(pixelBytes, 0, destination, byteCount);
                return true;
            }
            finally
            {
                _orbitYawDeg = savedYaw;
                _orbitPitchDeg = savedPitch;
                _zoom = savedZoom;
                _panPx = savedPan;

                if (depthTexture != IntPtr.Zero)
                {
                    ObjC.Void_objc_msgSend(depthTexture, Selectors.Release);
                }

                if (colorTexture != IntPtr.Zero)
                {
                    ObjC.Void_objc_msgSend(colorTexture, Selectors.Release);
                }
            }
        }

    }
}
