using System;
using System.Numerics;
using System.Runtime.InteropServices;
using KnobForge.Core;

namespace KnobForge.App.Controls
{
    public sealed partial class MetalViewport
    {
        private bool EnsurePaintStampResources()
        {
            if (_context is null || _context.Device.Handle == IntPtr.Zero)
            {
                return false;
            }

            if (_paintStampPipelineRust != IntPtr.Zero &&
                _paintStampPipelineWear != IntPtr.Zero &&
                _paintStampPipelineGunk != IntPtr.Zero &&
                _paintStampPipelineScratch != IntPtr.Zero &&
                _paintStampPipelineErase != IntPtr.Zero &&
                _paintStampPipelineColor != IntPtr.Zero)
            {
                return true;
            }

            ReleasePaintStampResources();

            IntPtr device = _context.Device.Handle;
            IntPtr sourceString = ToNSString(PaintMaskStampShaderSource);
            IntPtr libraryError;
            IntPtr library = ObjC.IntPtr_objc_msgSend_IntPtr_IntPtr_outIntPtr(
                device,
                Selectors.NewLibraryWithSourceOptionsError,
                sourceString,
                IntPtr.Zero,
                out libraryError);
            if (library == IntPtr.Zero)
            {
                LogPaintStampError($"Failed to compile paint stamp shader: {DescribeNSError(libraryError)}");
                return false;
            }

            IntPtr vertexName = ToNSString("vertex_paint_stamp");
            IntPtr fragmentName = ToNSString("fragment_paint_stamp");
            IntPtr vertexFunction = ObjC.IntPtr_objc_msgSend_IntPtr(library, Selectors.NewFunctionWithName, vertexName);
            IntPtr fragmentFunction = ObjC.IntPtr_objc_msgSend_IntPtr(library, Selectors.NewFunctionWithName, fragmentName);
            if (vertexFunction == IntPtr.Zero || fragmentFunction == IntPtr.Zero)
            {
                LogPaintStampError("Paint stamp shader entry points were not found.");
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

            IntPtr rustPipeline = CreatePaintStampPipelineState(
                device,
                vertexFunction,
                fragmentFunction,
                MTLColorWriteMaskRed,
                MTLBlendFactorSourceAlpha,
                MTLBlendFactorOneMinusSourceAlpha,
                MTLBlendFactorOne,
                MTLBlendFactorOneMinusSourceAlpha);
            IntPtr wearPipeline = CreatePaintStampPipelineState(
                device,
                vertexFunction,
                fragmentFunction,
                MTLColorWriteMaskGreen,
                MTLBlendFactorSourceAlpha,
                MTLBlendFactorOneMinusSourceAlpha,
                MTLBlendFactorOne,
                MTLBlendFactorOneMinusSourceAlpha);
            IntPtr gunkPipeline = CreatePaintStampPipelineState(
                device,
                vertexFunction,
                fragmentFunction,
                MTLColorWriteMaskBlue,
                MTLBlendFactorSourceAlpha,
                MTLBlendFactorOneMinusSourceAlpha,
                MTLBlendFactorOne,
                MTLBlendFactorOneMinusSourceAlpha);
            IntPtr scratchPipeline = CreatePaintStampPipelineState(
                device,
                vertexFunction,
                fragmentFunction,
                MTLColorWriteMaskAlpha,
                MTLBlendFactorSourceAlpha,
                MTLBlendFactorOneMinusSourceAlpha,
                MTLBlendFactorOne,
                MTLBlendFactorOneMinusSourceAlpha);
            IntPtr erasePipeline = CreatePaintStampPipelineState(
                device,
                vertexFunction,
                fragmentFunction,
                MTLColorWriteMaskAll,
                MTLBlendFactorZero,
                MTLBlendFactorOneMinusSourceAlpha,
                MTLBlendFactorZero,
                MTLBlendFactorOneMinusSourceAlpha);
            IntPtr colorPipeline = CreatePaintStampPipelineState(
                device,
                vertexFunction,
                fragmentFunction,
                MTLColorWriteMaskAll,
                MTLBlendFactorSourceAlpha,
                MTLBlendFactorOneMinusSourceAlpha,
                MTLBlendFactorOne,
                MTLBlendFactorOneMinusSourceAlpha);

            if (rustPipeline == IntPtr.Zero ||
                wearPipeline == IntPtr.Zero ||
                gunkPipeline == IntPtr.Zero ||
                scratchPipeline == IntPtr.Zero ||
                erasePipeline == IntPtr.Zero ||
                colorPipeline == IntPtr.Zero)
            {
                if (rustPipeline != IntPtr.Zero)
                {
                    ObjC.Void_objc_msgSend(rustPipeline, Selectors.Release);
                }

                if (wearPipeline != IntPtr.Zero)
                {
                    ObjC.Void_objc_msgSend(wearPipeline, Selectors.Release);
                }

                if (gunkPipeline != IntPtr.Zero)
                {
                    ObjC.Void_objc_msgSend(gunkPipeline, Selectors.Release);
                }

                if (scratchPipeline != IntPtr.Zero)
                {
                    ObjC.Void_objc_msgSend(scratchPipeline, Selectors.Release);
                }

                if (erasePipeline != IntPtr.Zero)
                {
                    ObjC.Void_objc_msgSend(erasePipeline, Selectors.Release);
                }

                if (colorPipeline != IntPtr.Zero)
                {
                    ObjC.Void_objc_msgSend(colorPipeline, Selectors.Release);
                }

                ObjC.Void_objc_msgSend(vertexFunction, Selectors.Release);
                ObjC.Void_objc_msgSend(fragmentFunction, Selectors.Release);
                ObjC.Void_objc_msgSend(library, Selectors.Release);
                return false;
            }

            _paintStampLibrary = library;
            _paintStampVertexFunction = vertexFunction;
            _paintStampFragmentFunction = fragmentFunction;
            _paintStampPipelineRust = rustPipeline;
            _paintStampPipelineWear = wearPipeline;
            _paintStampPipelineGunk = gunkPipeline;
            _paintStampPipelineScratch = scratchPipeline;
            _paintStampPipelineErase = erasePipeline;
            _paintStampPipelineColor = colorPipeline;
            return true;
        }

        private IntPtr ResolvePaintStampPipeline(PaintChannel channel)
        {
            return channel switch
            {
                PaintChannel.Rust => _paintStampPipelineRust,
                PaintChannel.Wear => _paintStampPipelineWear,
                PaintChannel.Gunk => _paintStampPipelineGunk,
                PaintChannel.Scratch => _paintStampPipelineScratch,
                PaintChannel.Erase => _paintStampPipelineErase,
                PaintChannel.Color => _paintStampPipelineColor,
                _ => IntPtr.Zero
            };
        }

        private static IntPtr CreatePaintStampPipelineState(
            IntPtr device,
            IntPtr vertexFunction,
            IntPtr fragmentFunction,
            nuint writeMask,
            nuint sourceRgbBlendFactor,
            nuint destinationRgbBlendFactor,
            nuint sourceAlphaBlendFactor,
            nuint destinationAlphaBlendFactor)
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
            ObjC.Void_objc_msgSend_Bool(colorAttachment0, Selectors.SetBlendingEnabled, true);
            ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetRgbBlendOperation, 0);
            ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetAlphaBlendOperation, 0);
            ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetSourceRGBBlendFactor, sourceRgbBlendFactor);
            ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetDestinationRGBBlendFactor, destinationRgbBlendFactor);
            ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetSourceAlphaBlendFactor, sourceAlphaBlendFactor);
            ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetDestinationAlphaBlendFactor, destinationAlphaBlendFactor);
            ObjC.Void_objc_msgSend_UInt(colorAttachment0, Selectors.SetWriteMask, writeMask);

            IntPtr pipelineError;
            IntPtr pipeline = ObjC.IntPtr_objc_msgSend_IntPtr_outIntPtr(
                device,
                Selectors.NewRenderPipelineStateWithDescriptorError,
                descriptor,
                out pipelineError);

            ObjC.Void_objc_msgSend(descriptor, Selectors.Release);
            if (pipeline == IntPtr.Zero)
            {
                LogPaintStampError($"Failed to create paint stamp pipeline: {DescribeNSError(pipelineError)}");
            }

            return pipeline;
        }

        private static PaintStampUniform BuildPaintStampUniform(PaintStampCommand command)
        {
            float seed01 = (command.Seed & 0x00FFFFFFu) / 16777215f;
            return new PaintStampUniform
            {
                CenterRadiusOpacity = new Vector4(
                    command.UvCenter.X,
                    command.UvCenter.Y,
                    MathF.Max(command.UvRadius, 1e-6f),
                    Math.Clamp(command.Opacity, 0f, 1f)),
                Params0 = new Vector4(
                    Math.Clamp(command.Spread, 0f, 1f),
                    (float)command.BrushType,
                    (float)command.ScratchAbrasionType,
                    (float)command.Channel),
                Params1 = new Vector4(
                    seed01,
                    Math.Clamp(command.PaintColor.X, 0f, 1f),
                    Math.Clamp(command.PaintColor.Y, 0f, 1f),
                    Math.Clamp(command.PaintColor.Z, 0f, 1f))
            };
        }

        private void UploadPaintStampUniform(IntPtr encoderPtr, in PaintStampUniform uniform)
        {
            if (encoderPtr == IntPtr.Zero)
            {
                return;
            }

            int uniformSize = Marshal.SizeOf<PaintStampUniform>();
            IntPtr uniformPtr = EnsureUniformUploadScratchBuffer(uniformSize, paintStamp: true);
            if (uniformPtr == IntPtr.Zero)
            {
                return;
            }

            Marshal.StructureToPtr(uniform, uniformPtr, false);
            ObjC.Void_objc_msgSend_IntPtr_UInt_UInt(
                encoderPtr,
                Selectors.SetFragmentBytesLengthAtIndex,
                uniformPtr,
                (nuint)uniformSize,
                0);
        }

        private void ReleasePaintStampResources()
        {
            if (_paintStampPipelineRust != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_paintStampPipelineRust, Selectors.Release);
                _paintStampPipelineRust = IntPtr.Zero;
            }

            if (_paintStampPipelineWear != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_paintStampPipelineWear, Selectors.Release);
                _paintStampPipelineWear = IntPtr.Zero;
            }

            if (_paintStampPipelineGunk != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_paintStampPipelineGunk, Selectors.Release);
                _paintStampPipelineGunk = IntPtr.Zero;
            }

            if (_paintStampPipelineScratch != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_paintStampPipelineScratch, Selectors.Release);
                _paintStampPipelineScratch = IntPtr.Zero;
            }

            if (_paintStampPipelineErase != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_paintStampPipelineErase, Selectors.Release);
                _paintStampPipelineErase = IntPtr.Zero;
            }

            if (_paintStampPipelineColor != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_paintStampPipelineColor, Selectors.Release);
                _paintStampPipelineColor = IntPtr.Zero;
            }

            if (_paintStampVertexFunction != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_paintStampVertexFunction, Selectors.Release);
                _paintStampVertexFunction = IntPtr.Zero;
            }

            if (_paintStampFragmentFunction != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_paintStampFragmentFunction, Selectors.Release);
                _paintStampFragmentFunction = IntPtr.Zero;
            }

            if (_paintStampLibrary != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_paintStampLibrary, Selectors.Release);
                _paintStampLibrary = IntPtr.Zero;
            }
        }


        private void EnsurePaintMaskTexture(KnobProject? project)
        {
            if (_context is null || _context.Device.Handle == IntPtr.Zero || project is null)
            {
                return;
            }

            int size = project.PaintMaskSize;
            if (_paintMaskTexture == IntPtr.Zero)
            {
                IntPtr descriptor = ObjC.IntPtr_objc_msgSend_UInt_UInt_UInt_Bool(
                    ObjCClasses.MTLTextureDescriptor,
                    Selectors.Texture2DDescriptorWithPixelFormatWidthHeightMipmapped,
                    PaintMaskPixelFormat,
                    (nuint)size,
                    (nuint)size,
                    true);
                if (descriptor == IntPtr.Zero)
                {
                    return;
                }

                ObjC.Void_objc_msgSend_UInt(descriptor, Selectors.SetUsage, PaintMaskTextureUsage);
                IntPtr texture = ObjC.IntPtr_objc_msgSend_IntPtr(_context.Device.Handle, Selectors.NewTextureWithDescriptor, descriptor);
                if (texture == IntPtr.Zero)
                {
                    return;
                }

                _paintMaskTexture = texture;
                _paintMaskTextureVersion = -1;
            }

            if (_paintMaskTextureVersion == project.PaintMaskVersion)
            {
                return;
            }

            byte[] pixelBytes = project.GetPaintMaskRgba8();
            if (pixelBytes.Length < size * size * 4)
            {
                return;
            }

            GCHandle pinned = GCHandle.Alloc(pixelBytes, GCHandleType.Pinned);
            try
            {
                MTLRegion region = new MTLRegion(
                    new MTLOrigin(0, 0, 0),
                    new MTLSize((nuint)size, (nuint)size, 1));
                ObjC.Void_objc_msgSend_MTLRegion_UInt_IntPtr_UInt(
                    _paintMaskTexture,
                    Selectors.ReplaceRegionMipmapLevelWithBytesBytesPerRow,
                    region,
                    0,
                    pinned.AddrOfPinnedObject(),
                    (nuint)(size * 4));
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
                    ObjC.Void_objc_msgSend_IntPtr(blitEncoder, Selectors.GenerateMipmapsForTexture, _paintMaskTexture);
                    ObjC.Void_objc_msgSend(blitEncoder, Selectors.EndEncoding);
                }

                ObjC.Void_objc_msgSend(commandBuffer, Selectors.Commit);
                ObjC.Void_objc_msgSend(commandBuffer, Selectors.WaitUntilCompleted);
            }

            _paintMaskTextureVersion = project.PaintMaskVersion;
        }

        private void EnsurePaintColorTexture(KnobProject? project)
        {
            if (_context is null || _context.Device.Handle == IntPtr.Zero || project is null)
            {
                return;
            }

            int size = project.PaintMaskSize;
            if (_paintColorTexture != IntPtr.Zero)
            {
                nuint textureWidth = ObjC.UInt_objc_msgSend(_paintColorTexture, Selectors.Width);
                nuint textureHeight = ObjC.UInt_objc_msgSend(_paintColorTexture, Selectors.Height);
                if (textureWidth != (nuint)size || textureHeight != (nuint)size)
                {
                    ReleasePaintColorTexture();
                }
            }

            if (_paintColorTexture != IntPtr.Zero)
            {
                return;
            }

            IntPtr descriptor = ObjC.IntPtr_objc_msgSend_UInt_UInt_UInt_Bool(
                ObjCClasses.MTLTextureDescriptor,
                Selectors.Texture2DDescriptorWithPixelFormatWidthHeightMipmapped,
                PaintMaskPixelFormat,
                (nuint)size,
                (nuint)size,
                true);
            if (descriptor == IntPtr.Zero)
            {
                return;
            }

            ObjC.Void_objc_msgSend_UInt(descriptor, Selectors.SetUsage, PaintMaskTextureUsage);
            IntPtr texture = ObjC.IntPtr_objc_msgSend_IntPtr(_context.Device.Handle, Selectors.NewTextureWithDescriptor, descriptor);
            if (texture == IntPtr.Zero)
            {
                return;
            }

            _paintColorTexture = texture;
            _paintColorTextureNeedsClear = true;
        }

        private void ReleasePaintMaskTexture()
        {
            if (_paintMaskTexture != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_paintMaskTexture, Selectors.Release);
                _paintMaskTexture = IntPtr.Zero;
            }

            _paintMaskTextureVersion = -1;
        }

        private void ReleasePaintColorTexture()
        {
            if (_paintColorTexture != IntPtr.Zero)
            {
                ObjC.Void_objc_msgSend(_paintColorTexture, Selectors.Release);
                _paintColorTexture = IntPtr.Zero;
            }

            _paintColorTextureNeedsClear = true;
        }

        private static byte[] BuildSpiralNormalMapRgba8(
            int size,
            float referenceRadius,
            float topScale,
            float spiralHeight,
            float spiralWidth,
            float spiralTurns)
        {
            int clampedSize = Math.Clamp(size, 128, 4096);
            var pixels = new byte[clampedSize * clampedSize * 4];
            float topRadius = MathF.Max(1e-4f, referenceRadius * topScale);
            float invSizeMinusOne = 1f / MathF.Max(1, clampedSize - 1);
            float epsilon = (2f * topRadius) * invSizeMinusOne;

            for (int y = 0; y < clampedSize; y++)
            {
                float v = (y * invSizeMinusOne) * 2f - 1f;
                float py = v * topRadius;

                for (int x = 0; x < clampedSize; x++)
                {
                    float u = (x * invSizeMinusOne) * 2f - 1f;
                    float px = u * topRadius;
                    int pixelOffset = ((y * clampedSize) + x) * 4;

                    float radialDistance = MathF.Sqrt((px * px) + (py * py));
                    Vector3 normal;
                    if (radialDistance > topRadius)
                    {
                        normal = Vector3.UnitZ;
                    }
                    else
                    {
                        float hL = ComputeSpiralRidgeOffset(
                            px - epsilon,
                            py,
                            MathF.Sqrt(((px - epsilon) * (px - epsilon)) + (py * py)),
                            topRadius,
                            spiralHeight,
                            spiralWidth,
                            spiralTurns);
                        float hR = ComputeSpiralRidgeOffset(
                            px + epsilon,
                            py,
                            MathF.Sqrt(((px + epsilon) * (px + epsilon)) + (py * py)),
                            topRadius,
                            spiralHeight,
                            spiralWidth,
                            spiralTurns);
                        float hD = ComputeSpiralRidgeOffset(
                            px,
                            py - epsilon,
                            MathF.Sqrt((px * px) + ((py - epsilon) * (py - epsilon))),
                            topRadius,
                            spiralHeight,
                            spiralWidth,
                            spiralTurns);
                        float hU = ComputeSpiralRidgeOffset(
                            px,
                            py + epsilon,
                            MathF.Sqrt((px * px) + ((py + epsilon) * (py + epsilon))),
                            topRadius,
                            spiralHeight,
                            spiralWidth,
                            spiralTurns);

                        float dhdx = (hR - hL) / MathF.Max(1e-6f, (2f * epsilon));
                        float dhdy = (hU - hD) / MathF.Max(1e-6f, (2f * epsilon));
                        normal = Vector3.Normalize(new Vector3(-dhdx, -dhdy, 1f));
                        if (float.IsNaN(normal.X) || float.IsNaN(normal.Y) || float.IsNaN(normal.Z))
                        {
                            normal = Vector3.UnitZ;
                        }
                    }

                    Vector3 encoded = (normal * 0.5f) + new Vector3(0.5f);
                    pixels[pixelOffset + 0] = (byte)Math.Clamp((int)MathF.Round(encoded.X * 255f), 0, 255);
                    pixels[pixelOffset + 1] = (byte)Math.Clamp((int)MathF.Round(encoded.Y * 255f), 0, 255);
                    pixels[pixelOffset + 2] = (byte)Math.Clamp((int)MathF.Round(encoded.Z * 255f), 0, 255);
                    pixels[pixelOffset + 3] = 255;
                }
            }

            return pixels;
        }

        private static float ComputeSpiralRidgeOffset(
            float x,
            float y,
            float radialDistance,
            float topRadius,
            float ridgeHeight,
            float ridgeWidth,
            float spiralTurns)
        {
            if (ridgeHeight <= 0f || topRadius <= 1e-6f || spiralTurns <= 1e-6f)
            {
                return 0f;
            }

            float rNorm = Math.Clamp(radialDistance / topRadius, 0f, 1f);
            float theta = MathF.Atan2(y, x);
            if (theta < 0f)
            {
                theta += MathF.PI * 2f;
            }

            float thetaNorm = theta / (MathF.PI * 2f);
            float ringCount = MathF.Max(1f, spiralTurns);
            float phaseNoise = ValueNoise2D((rNorm * 60f) + 17.2f, (thetaNorm * 40f) + 9.7f);
            float phaseJitter = (phaseNoise - 0.5f) * 0.15f;
            float phase = (rNorm * ringCount) + phaseJitter;
            float nearest = MathF.Round(phase);
            float absDist = MathF.Abs((phase - nearest) / ringCount);

            float microHeight = ridgeHeight * 0.075f;
            float widthNoise = ValueNoise2D((rNorm * 80f) + 2.3f, (thetaNorm * 48f) + 4.1f);
            float widthJitter = 1f + ((widthNoise - 0.5f) * 0.08f);
            float widthNorm = MathF.Max(1e-6f, (ridgeWidth * 1.25f * widthJitter) / MathF.Max(topRadius, 1e-4f));
            float halfWidth = widthNorm * 0.5f;
            if (absDist >= halfWidth)
            {
                return 0f;
            }

            float t = absDist / halfWidth;
            float vProfile = 1f - t;
            vProfile *= vProfile;
            vProfile *= vProfile;
            float heightNoise = ValueNoise2D((rNorm * 96f) + 13.9f, (thetaNorm * 56f) + 5.8f);
            float heightJitter = 1f + ((heightNoise - 0.5f) * 0.04f);
            float edgeT = Math.Clamp((rNorm - 0.975f) / 0.025f, 0f, 1f);
            float edgeFade = 1f - (edgeT * edgeT * (3f - (2f * edgeT)));
            return -microHeight * heightJitter * vProfile * edgeFade;
        }

        private static float ValueNoise2D(float x, float y)
        {
            int x0 = (int)MathF.Floor(x);
            int y0 = (int)MathF.Floor(y);
            int x1 = x0 + 1;
            int y1 = y0 + 1;
            float tx = x - x0;
            float ty = y - y0;
            float sx = tx * tx * (3f - (2f * tx));
            float sy = ty * ty * (3f - (2f * ty));

            float n00 = Hash2(x0, y0);
            float n10 = Hash2(x1, y0);
            float n01 = Hash2(x0, y1);
            float n11 = Hash2(x1, y1);
            float nx0 = n00 + ((n10 - n00) * sx);
            float nx1 = n01 + ((n11 - n01) * sx);
            return nx0 + ((nx1 - nx0) * sy);
        }

        private static float Hash2(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393) + (uint)(y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0x00FFFFFF) / 16777215f;
            }
        }
    }
}
