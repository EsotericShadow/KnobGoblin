using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using KnobForge.Core;
using KnobForge.Core.Scene;
using KnobForge.Rendering.GPU;

namespace KnobForge.App.Controls
{
    public sealed partial class MetalViewport
    {
        private void StartRenderLoop()
        {
            if (!OperatingSystem.IsMacOS())
            {
                return;
            }

            if (_renderTimer is null)
            {
                _renderTimer = new DispatcherTimer(
                    TimeSpan.FromMilliseconds(16),
                    DispatcherPriority.Render,
                    OnRenderTimerTick);
            }

            _renderTimer.Start();
        }

        private void StopRenderLoop()
        {
            if (_renderTimer is null)
            {
                return;
            }

            _renderTimer.Stop();
            _renderTimer = null;
        }

        private void OnRenderTimerTick(object? sender, EventArgs e)
        {
            if (!IsVisible || _metalLayer == IntPtr.Zero || !_dirty)
            {
                return;
            }

            Render(_ => { });
        }

        private void UpdateDrawableSize(Size size)
        {
            if (!OperatingSystem.IsMacOS() || _metalLayer == IntPtr.Zero || _nativeView == IntPtr.Zero)
            {
                return;
            }

            double width = Math.Max(1d, size.Width);
            double height = Math.Max(1d, size.Height);
            double scale = GetRenderScale();

            ObjC.Void_objc_msgSend_CGRect(_nativeView, Selectors.SetFrame, new CGRect(0d, 0d, width, height));
            ObjC.Void_objc_msgSend_CGRect(_metalLayer, Selectors.SetFrame, new CGRect(0d, 0d, width, height));
            ObjC.Void_objc_msgSend_CGSize(_metalLayer, Selectors.SetDrawableSize, new CGSize(width * scale, height * scale));
            ObjC.Void_objc_msgSend_Double(_metalLayer, Selectors.SetContentsScale, scale);
        }

        private void EnsureDepthTexture(nuint width, nuint height)
        {
            if (_context is null || width == 0 || height == 0)
            {
                return;
            }

            if (_depthTexture != IntPtr.Zero && _depthTextureWidth == width && _depthTextureHeight == height)
            {
                return;
            }

            if (_depthTexture != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_depthTexture, Selectors.Release);
                _depthTexture = IntPtr.Zero;
                _depthTextureWidth = 0;
                _depthTextureHeight = 0;
            }

            IntPtr descriptor = ObjC.IntPtr_objc_msgSend_UInt_UInt_UInt_Bool(
                ObjCClasses.MTLTextureDescriptor,
                Selectors.Texture2DDescriptorWithPixelFormatWidthHeightMipmapped,
                DepthPixelFormat,
                width,
                height,
                false);
            if (descriptor == IntPtr.Zero)
            {
                return;
            }

            ObjC.Void_objc_msgSend_UInt(descriptor, Selectors.SetUsage, 4); // MTLTextureUsageRenderTarget
            IntPtr texture = ObjC.IntPtr_objc_msgSend_IntPtr(_context.Device.Handle, Selectors.NewTextureWithDescriptor, descriptor);
            if (texture == IntPtr.Zero)
            {
                return;
            }

            _depthTexture = texture;
            _depthTextureWidth = width;
            _depthTextureHeight = height;
        }

        private void EnsureSpiralNormalTexture(ModelNode modelNode, float referenceRadius)
        {
            if (_context is null || _context.Device.Handle == IntPtr.Zero)
            {
                return;
            }

            float topScale = Math.Clamp(modelNode.TopRadiusScale, 0.30f, 1.30f);
            SpiralNormalMapKey nextKey = new(
                MathF.Round(referenceRadius, 3),
                MathF.Round(topScale, 4),
                MathF.Round(modelNode.SpiralRidgeHeight, 4),
                MathF.Round(modelNode.SpiralRidgeWidth, 4),
                MathF.Round(modelNode.SpiralTurns, 4));

            if (_spiralNormalTexture != IntPtr.Zero && nextKey.Equals(_spiralNormalMapKey))
            {
                return;
            }

            ReleaseSpiralNormalTexture();

            byte[] pixelBytes = BuildSpiralNormalMapRgba8(
                SpiralNormalMapSize,
                referenceRadius,
                topScale,
                modelNode.SpiralRidgeHeight,
                modelNode.SpiralRidgeWidth,
                modelNode.SpiralTurns);

            IntPtr descriptor = ObjC.IntPtr_objc_msgSend_UInt_UInt_UInt_Bool(
                ObjCClasses.MTLTextureDescriptor,
                Selectors.Texture2DDescriptorWithPixelFormatWidthHeightMipmapped,
                NormalMapPixelFormat,
                (nuint)SpiralNormalMapSize,
                (nuint)SpiralNormalMapSize,
                true);
            if (descriptor == IntPtr.Zero)
            {
                return;
            }

            ObjC.Void_objc_msgSend_UInt(descriptor, Selectors.SetUsage, 1); // MTLTextureUsageShaderRead
            IntPtr texture = ObjC.IntPtr_objc_msgSend_IntPtr(_context.Device.Handle, Selectors.NewTextureWithDescriptor, descriptor);
            if (texture == IntPtr.Zero)
            {
                return;
            }

            GCHandle pinned = GCHandle.Alloc(pixelBytes, GCHandleType.Pinned);
            try
            {
                MTLRegion region = new MTLRegion(
                    new MTLOrigin(0, 0, 0),
                    new MTLSize((nuint)SpiralNormalMapSize, (nuint)SpiralNormalMapSize, 1));
                ObjC.Void_objc_msgSend_MTLRegion_UInt_IntPtr_UInt(
                    texture,
                    Selectors.ReplaceRegionMipmapLevelWithBytesBytesPerRow,
                    region,
                    0,
                    pinned.AddrOfPinnedObject(),
                    (nuint)(SpiralNormalMapSize * 4));
            }
            finally
            {
                pinned.Free();
            }

            IntPtr commandBuffer = _context.CreateCommandBuffer().Handle;
            if (commandBuffer != IntPtr.Zero)
            {
                IntPtr blitEncoder = ObjC.IntPtr_objc_msgSend(commandBuffer, Selectors.BlitCommandEncoder);
                if (blitEncoder != IntPtr.Zero)
                {
                    ObjC.Void_objc_msgSend_IntPtr(blitEncoder, Selectors.GenerateMipmapsForTexture, texture);
                    ObjC.Void_objc_msgSend(blitEncoder, Selectors.EndEncoding);
                }

                ObjC.Void_objc_msgSend(commandBuffer, Selectors.Commit);
                ObjC.Void_objc_msgSend(commandBuffer, Selectors.WaitUntilCompleted);
            }

            _spiralNormalTexture = texture;
            _spiralNormalMapKey = nextKey;
        }

        private void ReleaseSpiralNormalTexture()
        {
            if (_spiralNormalTexture != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_spiralNormalTexture, Selectors.Release);
                _spiralNormalTexture = IntPtr.Zero;
            }
        }

        public void DiscardPendingPaintStamps()
        {
            _pendingPaintStampCommands.Clear();
            _activeStrokeCommands.Clear();
        }

        public void RequestClearPaintColorTexture()
        {
            _paintColorTextureNeedsClear = true;
            _dirty = true;
        }

        private void ApplyPendingPaintStamps(IntPtr commandBuffer)
        {
            if (commandBuffer == IntPtr.Zero || _paintMaskTexture == IntPtr.Zero)
            {
                return;
            }

            EnsureDefaultPaintLayer();

            if (_paintColorTexture != IntPtr.Zero && _paintColorTextureNeedsClear)
            {
                ClearTextureToTransparent(commandBuffer, _paintColorTexture);
                GenerateTextureMipmaps(commandBuffer, _paintColorTexture);
                _paintColorTextureNeedsClear = false;
            }

            if (!EnsurePaintStampResources())
            {
                _pendingPaintStampCommands.Clear();
                return;
            }

            bool stampedMask = false;
            bool stampedColor = false;

            if (_paintRebuildRequested)
            {
                ClearTextureToTransparent(commandBuffer, _paintMaskTexture);
                if (_paintColorTexture != IntPtr.Zero)
                {
                    ClearTextureToTransparent(commandBuffer, _paintColorTexture);
                }

                if (_paintHistoryRevision > 0 && _committedPaintStrokes.Count > 0)
                {
                    var replayCommands = BuildReplayPaintCommands(_paintHistoryRevision);
                    if (replayCommands.Count > 0)
                    {
                        stampedMask |= ApplyPaintStampsToTexture(
                            commandBuffer,
                            _paintMaskTexture,
                            includeColorChannel: false,
                            replayCommands);
                        stampedColor |= _paintColorTexture != IntPtr.Zero && ApplyPaintStampsToTexture(
                            commandBuffer,
                            _paintColorTexture,
                            includeColorChannel: true,
                            replayCommands);
                    }
                }

                _paintRebuildRequested = false;
            }

            if (_pendingPaintStampCommands.Count > 0)
            {
                stampedMask |= ApplyPaintStampsToTexture(
                    commandBuffer,
                    _paintMaskTexture,
                    includeColorChannel: false,
                    _pendingPaintStampCommands);
                stampedColor |= _paintColorTexture != IntPtr.Zero && ApplyPaintStampsToTexture(
                    commandBuffer,
                    _paintColorTexture,
                    includeColorChannel: true,
                    _pendingPaintStampCommands);
            }

            if (stampedMask)
            {
                GenerateTextureMipmaps(commandBuffer, _paintMaskTexture);
            }

            if (stampedColor)
            {
                GenerateTextureMipmaps(commandBuffer, _paintColorTexture);
            }

            _pendingPaintStampCommands.Clear();
        }

        private bool ApplyPaintStampsToTexture(
            IntPtr commandBuffer,
            IntPtr targetTexture,
            bool includeColorChannel,
            IReadOnlyList<PaintStampCommand> commands)
        {
            if (commandBuffer == IntPtr.Zero || targetTexture == IntPtr.Zero)
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

            ObjC.Void_objc_msgSend_IntPtr(colorAttachment, Selectors.SetTexture, targetTexture);
            ObjC.Void_objc_msgSend_UInt(colorAttachment, Selectors.SetLoadAction, MTLLoadActionLoad);
            ObjC.Void_objc_msgSend_UInt(colorAttachment, Selectors.SetStoreAction, MTLStoreActionStore);

            IntPtr encoderPtr = ObjC.IntPtr_objc_msgSend_IntPtr(commandBuffer, Selectors.RenderCommandEncoderWithDescriptor, passDescriptor);
            if (encoderPtr == IntPtr.Zero)
            {
                return false;
            }

            bool stampedAny = false;
            IntPtr activePipeline = IntPtr.Zero;
            for (int i = 0; i < commands.Count; i++)
            {
                PaintStampCommand command = ResolvePaintCommandForDisplay(commands[i]);
                bool isColorChannel = command.Channel == PaintChannel.Color;
                if (includeColorChannel)
                {
                    if (!isColorChannel && command.Channel != PaintChannel.Erase)
                    {
                        continue;
                    }
                }
                else if (isColorChannel)
                {
                    continue;
                }

                IntPtr pipeline = ResolvePaintStampPipeline(command.Channel);
                if (pipeline == IntPtr.Zero)
                {
                    continue;
                }

                if (activePipeline != pipeline)
                {
                    ObjC.Void_objc_msgSend_IntPtr(encoderPtr, Selectors.SetRenderPipelineState, pipeline);
                    activePipeline = pipeline;
                }

                PaintStampUniform uniform = BuildPaintStampUniform(command);
                UploadPaintStampUniform(encoderPtr, uniform);
                ObjC.Void_objc_msgSend_UInt_UInt_UInt(
                    encoderPtr,
                    Selectors.DrawPrimitivesVertexStartVertexCount,
                    MTLPrimitiveTypeTriangle,
                    0,
                    3);
                stampedAny = true;
            }

            ObjC.Void_objc_msgSend(encoderPtr, Selectors.EndEncoding);
            return stampedAny;
        }

        private List<PaintStampCommand> BuildReplayPaintCommands(int revision)
        {
            int clampedRevision = Math.Clamp(revision, 0, _committedPaintStrokes.Count);
            int estimatedCount = 0;
            for (int i = 0; i < clampedRevision; i++)
            {
                estimatedCount += _committedPaintStrokes[i].Commands.Length;
            }

            var commands = new List<PaintStampCommand>(Math.Max(0, estimatedCount));
            for (int i = 0; i < clampedRevision; i++)
            {
                PaintStrokeRecord stroke = _committedPaintStrokes[i];
                if (stroke.Commands.Length == 0)
                {
                    continue;
                }

                commands.AddRange(stroke.Commands);
            }

            return commands;
        }

        private PaintStampCommand ResolvePaintCommandForDisplay(PaintStampCommand command)
        {
            if (_focusedPaintLayerIndex < 0 || command.LayerIndex == _focusedPaintLayerIndex)
            {
                return command;
            }

            const float dimFactor = 0.75f;
            Vector3 dimmedColor = new(
                command.PaintColor.X * dimFactor,
                command.PaintColor.Y * dimFactor,
                command.PaintColor.Z * dimFactor);
            return command with
            {
                Opacity = Math.Clamp(command.Opacity * dimFactor, 0f, 1f),
                PaintColor = dimmedColor
            };
        }

        private static void GenerateTextureMipmaps(IntPtr commandBuffer, IntPtr texture)
        {
            if (commandBuffer == IntPtr.Zero || texture == IntPtr.Zero)
            {
                return;
            }

            IntPtr blitEncoder = ObjC.IntPtr_objc_msgSend(commandBuffer, Selectors.BlitCommandEncoder);
            if (blitEncoder != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend_IntPtr(blitEncoder, Selectors.GenerateMipmapsForTexture, texture);
                ObjC.Void_objc_msgSend(blitEncoder, Selectors.EndEncoding);
            }
        }

        private static void ClearTextureToTransparent(IntPtr commandBuffer, IntPtr texture)
        {
            if (commandBuffer == IntPtr.Zero || texture == IntPtr.Zero)
            {
                return;
            }

            IntPtr passDescriptor = ObjC.IntPtr_objc_msgSend(ObjCClasses.MTLRenderPassDescriptor, Selectors.RenderPassDescriptor);
            if (passDescriptor == IntPtr.Zero)
            {
                return;
            }

            IntPtr colorAttachments = ObjC.IntPtr_objc_msgSend(passDescriptor, Selectors.ColorAttachments);
            IntPtr colorAttachment = ObjC.IntPtr_objc_msgSend_UInt(colorAttachments, Selectors.ObjectAtIndexedSubscript, 0);
            if (colorAttachment == IntPtr.Zero)
            {
                return;
            }

            ObjC.Void_objc_msgSend_IntPtr(colorAttachment, Selectors.SetTexture, texture);
            ObjC.Void_objc_msgSend_UInt(colorAttachment, Selectors.SetLoadAction, MTLLoadActionClear);
            ObjC.Void_objc_msgSend_UInt(colorAttachment, Selectors.SetStoreAction, MTLStoreActionStore);
            ObjC.Void_objc_msgSend_MTLClearColor(colorAttachment, Selectors.SetClearColor, new MTLClearColor(0d, 0d, 0d, 0d));

            IntPtr encoderPtr = ObjC.IntPtr_objc_msgSend_IntPtr(commandBuffer, Selectors.RenderCommandEncoderWithDescriptor, passDescriptor);
            if (encoderPtr != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(encoderPtr, Selectors.EndEncoding);
            }
        }

        private void RenderLightGizmoOverlayPass(
            IntPtr commandBuffer,
            IntPtr targetTexture,
            nuint drawableWidth,
            nuint drawableHeight)
        {
            if (commandBuffer == IntPtr.Zero ||
                targetTexture == IntPtr.Zero ||
                _project is null ||
                _project.Lights.Count == 0)
            {
                return;
            }

            if (!EnsureLightGizmoResources())
            {
                return;
            }

            LightGizmoOverlayUniform uniform = BuildLightGizmoOverlayUniform(drawableWidth, drawableHeight);
            int lightCount = (int)MathF.Round(uniform.ViewportAndCount.Z);
            if (lightCount <= 0)
            {
                return;
            }

            IntPtr passDescriptor = ObjC.IntPtr_objc_msgSend(ObjCClasses.MTLRenderPassDescriptor, Selectors.RenderPassDescriptor);
            if (passDescriptor == IntPtr.Zero)
            {
                return;
            }

            IntPtr colorAttachments = ObjC.IntPtr_objc_msgSend(passDescriptor, Selectors.ColorAttachments);
            IntPtr colorAttachment = ObjC.IntPtr_objc_msgSend_UInt(colorAttachments, Selectors.ObjectAtIndexedSubscript, 0);
            if (colorAttachment == IntPtr.Zero)
            {
                return;
            }

            ObjC.Void_objc_msgSend_IntPtr(colorAttachment, Selectors.SetTexture, targetTexture);
            ObjC.Void_objc_msgSend_UInt(colorAttachment, Selectors.SetLoadAction, MTLLoadActionLoad);
            ObjC.Void_objc_msgSend_UInt(colorAttachment, Selectors.SetStoreAction, MTLStoreActionStore);

            IntPtr encoderPtr = ObjC.IntPtr_objc_msgSend_IntPtr(commandBuffer, Selectors.RenderCommandEncoderWithDescriptor, passDescriptor);
            if (encoderPtr == IntPtr.Zero)
            {
                return;
            }

            ObjC.Void_objc_msgSend_IntPtr(encoderPtr, Selectors.SetRenderPipelineState, _lightGizmoPipeline);
            ObjC.Void_objc_msgSend_UInt(encoderPtr, Selectors.SetCullMode, 0); // MTLNone
            UploadLightGizmoOverlayUniform(encoderPtr, uniform);
            ObjC.Void_objc_msgSend_UInt_UInt_UInt(
                encoderPtr,
                Selectors.DrawPrimitivesVertexStartVertexCount,
                MTLPrimitiveTypeTriangle,
                0,
                3);
            ObjC.Void_objc_msgSend(encoderPtr, Selectors.EndEncoding);
        }

        private LightGizmoOverlayUniform BuildLightGizmoOverlayUniform(nuint drawableWidth, nuint drawableHeight)
        {
            float widthPx = MathF.Max(1f, (float)drawableWidth);
            float heightPx = MathF.Max(1f, (float)drawableHeight);
            var uniform = new LightGizmoOverlayUniform
            {
                ViewportAndCount = new Vector4(widthPx, heightPx, 0f, 0f)
            };

            if (_project is null ||
                _project.Lights.Count == 0 ||
                Bounds.Width <= 1e-3 ||
                Bounds.Height <= 1e-3)
            {
                return uniform;
            }

            IReadOnlyList<LightGizmoSnapshot> snapshots = GetLightGizmoSnapshots();
            int lightCount = Math.Min(snapshots.Count, MaxGpuLights);
            if (lightCount <= 0)
            {
                return uniform;
            }

            float scaleX = widthPx / MathF.Max(1e-3f, (float)Bounds.Width);
            float scaleY = heightPx / MathF.Max(1e-3f, (float)Bounds.Height);
            float radiusScale = (scaleX + scaleY) * 0.5f;
            float edgePadding = MathF.Max(6f, 8f * radiusScale);

            for (int i = 0; i < lightCount; i++)
            {
                LightGizmoSnapshot snapshot = snapshots[i];
                Vector2 positionPx = new((float)snapshot.PositionDip.X * scaleX, (float)snapshot.PositionDip.Y * scaleY);
                Vector2 originPx = new((float)snapshot.OriginDip.X * scaleX, (float)snapshot.OriginDip.Y * scaleY);
                Vector2 directionTipPx = new((float)snapshot.DirectionTipDip.X * scaleX, (float)snapshot.DirectionTipDip.Y * scaleY);
                bool onScreen =
                    positionPx.X >= 1f &&
                    positionPx.Y >= 1f &&
                    positionPx.X <= widthPx - 1f &&
                    positionPx.Y <= heightPx - 1f;

                if (!onScreen)
                {
                    positionPx.X = Math.Clamp(positionPx.X, edgePadding, Math.Max(edgePadding, widthPx - edgePadding));
                    positionPx.Y = Math.Clamp(positionPx.Y, edgePadding, Math.Max(edgePadding, heightPx - edgePadding));
                    originPx = positionPx;
                    directionTipPx = positionPx;
                }

                float radiusPx = (float)Math.Max(1.5, snapshot.RadiusDip * radiusScale);
                float selectedRingRadiusPx = (float)Math.Max(radiusPx + 2f, snapshot.SelectedRingRadiusDip * radiusScale);
                float directionTipRadiusPx = (float)Math.Max(1f, snapshot.DirectionTipRadiusDip * radiusScale);
                if (!onScreen)
                {
                    radiusPx = MathF.Max(radiusPx, 5.5f * radiusScale);
                    selectedRingRadiusPx = MathF.Max(selectedRingRadiusPx, 8.5f * radiusScale);
                }

                float hasDirectionTip = onScreen && snapshot.HasDirectionTip ? 1f : 0f;
                var light = new LightGizmoOverlayLight
                {
                    PositionAndOrigin = new Vector4(positionPx.X, positionPx.Y, originPx.X, originPx.Y),
                    DirectionTipAndRadii = new Vector4(directionTipPx.X, directionTipPx.Y, radiusPx, selectedRingRadiusPx),
                    TipRadiusAndAlpha = new Vector4(
                        directionTipRadiusPx,
                        hasDirectionTip,
                        snapshot.FillAlpha / 255f,
                        snapshot.LineAlpha / 255f),
                    Color = new Vector4(
                        snapshot.ColorR / 255f,
                        snapshot.ColorG / 255f,
                        snapshot.ColorB / 255f,
                        snapshot.IsSelected ? 1f : 0f)
                };

                SetLightGizmoOverlayLight(ref uniform, i, light);
            }

            uniform.ViewportAndCount.Z = lightCount;
            return uniform;
        }

        private static void SetLightGizmoOverlayLight(ref LightGizmoOverlayUniform uniforms, int index, in LightGizmoOverlayLight light)
        {
            switch (index)
            {
                case 0:
                    uniforms.Light0 = light;
                    break;
                case 1:
                    uniforms.Light1 = light;
                    break;
                case 2:
                    uniforms.Light2 = light;
                    break;
                case 3:
                    uniforms.Light3 = light;
                    break;
                case 4:
                    uniforms.Light4 = light;
                    break;
                case 5:
                    uniforms.Light5 = light;
                    break;
                case 6:
                    uniforms.Light6 = light;
                    break;
                case 7:
                    uniforms.Light7 = light;
                    break;
            }
        }

        private bool EnsureLightGizmoResources()
        {
            if (_context is null || _context.Device.Handle == IntPtr.Zero)
            {
                return false;
            }

            if (_lightGizmoPipeline != IntPtr.Zero)
            {
                return true;
            }

            ReleaseLightGizmoResources();

            IntPtr device = _context.Device.Handle;
            IntPtr sourceString = ToNSString(LightGizmoOverlayShaderSource);
            IntPtr libraryError;
            IntPtr library = ObjC.IntPtr_objc_msgSend_IntPtr_IntPtr_outIntPtr(
                device,
                Selectors.NewLibraryWithSourceOptionsError,
                sourceString,
                IntPtr.Zero,
                out libraryError);
            if (library == IntPtr.Zero)
            {
                LogPaintStampError($"Failed to compile light gizmo shader: {DescribeNSError(libraryError)}");
                return false;
            }

            IntPtr vertexName = ToNSString("vertex_light_gizmo_overlay");
            IntPtr fragmentName = ToNSString("fragment_light_gizmo_overlay");
            IntPtr vertexFunction = ObjC.IntPtr_objc_msgSend_IntPtr(library, Selectors.NewFunctionWithName, vertexName);
            IntPtr fragmentFunction = ObjC.IntPtr_objc_msgSend_IntPtr(library, Selectors.NewFunctionWithName, fragmentName);
            if (vertexFunction == IntPtr.Zero || fragmentFunction == IntPtr.Zero)
            {
                LogPaintStampError("Light gizmo shader entry points were not found.");
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

            IntPtr pipeline = CreateLightGizmoPipelineState(device, vertexFunction, fragmentFunction);
            if (pipeline == IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(vertexFunction, Selectors.Release);
                ObjC.Void_objc_msgSend(fragmentFunction, Selectors.Release);
                ObjC.Void_objc_msgSend(library, Selectors.Release);
                return false;
            }

            _lightGizmoLibrary = library;
            _lightGizmoVertexFunction = vertexFunction;
            _lightGizmoFragmentFunction = fragmentFunction;
            _lightGizmoPipeline = pipeline;
            return true;
        }

        private static IntPtr CreateLightGizmoPipelineState(IntPtr device, IntPtr vertexFunction, IntPtr fragmentFunction)
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

            ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetPixelFormat, (nuint)MetalRendererContext.DefaultColorFormat);
            ObjC.Void_objc_msgSend_Bool(colorAttachment0, Selectors.SetBlendingEnabled, true);
            ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetRgbBlendOperation, 0);
            ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetAlphaBlendOperation, 0);
            ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetSourceRGBBlendFactor, MTLBlendFactorSourceAlpha);
            ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetDestinationRGBBlendFactor, MTLBlendFactorOneMinusSourceAlpha);
            ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetSourceAlphaBlendFactor, MTLBlendFactorSourceAlpha);
            ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetDestinationAlphaBlendFactor, MTLBlendFactorOneMinusSourceAlpha);
            ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetWriteMask, MTLColorWriteMaskAll);

            IntPtr pipelineError;
            IntPtr pipeline = ObjC.IntPtr_objc_msgSend_IntPtr_outIntPtr(
                device,
                Selectors.NewRenderPipelineStateWithDescriptorError,
                descriptor,
                out pipelineError);
            ObjC.Void_objc_msgSend(descriptor, Selectors.Release);
            if (pipeline == IntPtr.Zero)
            {
                LogPaintStampError($"Failed to create light gizmo pipeline: {DescribeNSError(pipelineError)}");
            }

            return pipeline;
        }

        private static void UploadLightGizmoOverlayUniform(IntPtr encoderPtr, in LightGizmoOverlayUniform uniform)
        {
            int uniformSize = Marshal.SizeOf<LightGizmoOverlayUniform>();
            IntPtr uniformPtr = Marshal.AllocHGlobal(uniformSize);
            try
            {
                Marshal.StructureToPtr(uniform, uniformPtr, false);
                ObjC.Void_objc_msgSend_IntPtr_UInt_UInt(
                    encoderPtr,
                    Selectors.SetFragmentBytesLengthAtIndex,
                    uniformPtr,
                    (nuint)uniformSize,
                    0);
            }
            finally
            {
                Marshal.FreeHGlobal(uniformPtr);
            }
        }

        private void ReleaseLightGizmoResources()
        {
            if (_lightGizmoPipeline != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_lightGizmoPipeline, Selectors.Release);
                _lightGizmoPipeline = IntPtr.Zero;
            }

            if (_lightGizmoVertexFunction != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_lightGizmoVertexFunction, Selectors.Release);
                _lightGizmoVertexFunction = IntPtr.Zero;
            }

            if (_lightGizmoFragmentFunction != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_lightGizmoFragmentFunction, Selectors.Release);
                _lightGizmoFragmentFunction = IntPtr.Zero;
            }

            if (_lightGizmoLibrary != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_lightGizmoLibrary, Selectors.Release);
                _lightGizmoLibrary = IntPtr.Zero;
            }
        }
    }
}
