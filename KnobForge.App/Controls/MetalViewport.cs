using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using KnobForge.Core;
using KnobForge.Core.Scene;
using KnobForge.Rendering;
using KnobForge.Rendering.GPU;
using SkiaSharp;

namespace KnobForge.App.Controls
{
    public readonly struct MTLRenderCommandEncoder
    {
        public IntPtr Handle { get; }

        internal MTLRenderCommandEncoder(IntPtr handle)
        {
            Handle = handle;
        }
    }

    public sealed class MetalViewport : NativeControlHost
    {
        public enum PaintHitMode
        {
            Idle,
            MeshHit,
            Fallback
        }

        public readonly struct PaintHudSnapshot
        {
            public PaintHudSnapshot(
                bool paintEnabled,
                bool isPainting,
                PaintChannel channel,
                PaintBrushType brushType,
                ScratchAbrasionType abrasionType,
                float activeSizePx,
                float activeOpacity,
                float liveScratchDepth,
                bool optionDepthRampActive,
                PaintHitMode hitMode)
            {
                PaintEnabled = paintEnabled;
                IsPainting = isPainting;
                Channel = channel;
                BrushType = brushType;
                AbrasionType = abrasionType;
                ActiveSizePx = activeSizePx;
                ActiveOpacity = activeOpacity;
                LiveScratchDepth = liveScratchDepth;
                OptionDepthRampActive = optionDepthRampActive;
                HitMode = hitMode;
            }

            public bool PaintEnabled { get; }
            public bool IsPainting { get; }
            public PaintChannel Channel { get; }
            public PaintBrushType BrushType { get; }
            public ScratchAbrasionType AbrasionType { get; }
            public float ActiveSizePx { get; }
            public float ActiveOpacity { get; }
            public float LiveScratchDepth { get; }
            public bool OptionDepthRampActive { get; }
            public PaintHitMode HitMode { get; }
        }

        public readonly struct LightGizmoSnapshot
        {
            public LightGizmoSnapshot(
                Point positionDip,
                Point originDip,
                Point directionTipDip,
                bool hasDirectionTip,
                byte colorR,
                byte colorG,
                byte colorB,
                bool isSelected,
                double radiusDip,
                double selectedRingRadiusDip,
                double directionTipRadiusDip,
                byte fillAlpha,
                byte lineAlpha)
            {
                PositionDip = positionDip;
                OriginDip = originDip;
                DirectionTipDip = directionTipDip;
                HasDirectionTip = hasDirectionTip;
                ColorR = colorR;
                ColorG = colorG;
                ColorB = colorB;
                IsSelected = isSelected;
                RadiusDip = radiusDip;
                SelectedRingRadiusDip = selectedRingRadiusDip;
                DirectionTipRadiusDip = directionTipRadiusDip;
                FillAlpha = fillAlpha;
                LineAlpha = lineAlpha;
            }

            public Point PositionDip { get; }
            public Point OriginDip { get; }
            public Point DirectionTipDip { get; }
            public bool HasDirectionTip { get; }
            public byte ColorR { get; }
            public byte ColorG { get; }
            public byte ColorB { get; }
            public bool IsSelected { get; }
            public double RadiusDip { get; }
            public double SelectedRingRadiusDip { get; }
            public double DirectionTipRadiusDip { get; }
            public byte FillAlpha { get; }
            public byte LineAlpha { get; }
        }

        private const int MaxGpuLights = 8;
        private const nuint DepthPixelFormat = 252; // MTLPixelFormatDepth32Float
        private const nuint NormalMapPixelFormat = 70; // MTLPixelFormatRGBA8Unorm
        private const nuint PaintMaskPixelFormat = 70; // MTLPixelFormatRGBA8Unorm
        private const nuint PaintMaskTextureUsage = 5; // MTLTextureUsageShaderRead | MTLTextureUsageRenderTarget
        private const nuint MTLPrimitiveTypeTriangle = 3;
        private const nuint MTLLoadActionLoad = 1; // MTLoadActionLoad
        private const nuint MTLLoadActionClear = 2;
        private const nuint MTLStoreActionStore = 1;
        private const nuint MTLBlendFactorZero = 0;
        private const nuint MTLBlendFactorOne = 1;
        private const nuint MTLBlendFactorSourceAlpha = 4;
        private const nuint MTLBlendFactorOneMinusSourceAlpha = 5;
        private const nuint MTLColorWriteMaskNone = 0;
        private const nuint MTLColorWriteMaskAlpha = 1;
        private const nuint MTLColorWriteMaskBlue = 2;
        private const nuint MTLColorWriteMaskGreen = 4;
        private const nuint MTLColorWriteMaskRed = 8;
        private const nuint MTLColorWriteMaskAll = 15;
        private const int MaxPendingPaintStamps = 8192;
        private const int MaxShadowPassLights = 4;
        private const int SpiralNormalMapSize = 1024;

        private IntPtr _nativeView;
        private IntPtr _metalLayer;
        private IntPtr _depthTexture;
        private IntPtr _spiralNormalTexture;
        private IntPtr _paintMaskTexture;
        private IntPtr _paintColorTexture;
        private IntPtr _paintStampLibrary;
        private IntPtr _paintStampVertexFunction;
        private IntPtr _paintStampFragmentFunction;
        private IntPtr _paintStampPipelineRust;
        private IntPtr _paintStampPipelineWear;
        private IntPtr _paintStampPipelineGunk;
        private IntPtr _paintStampPipelineScratch;
        private IntPtr _paintStampPipelineErase;
        private IntPtr _paintStampPipelineColor;
        private IntPtr _lightGizmoLibrary;
        private IntPtr _lightGizmoVertexFunction;
        private IntPtr _lightGizmoFragmentFunction;
        private IntPtr _lightGizmoPipeline;
        private nuint _depthTextureWidth;
        private nuint _depthTextureHeight;
        private MetalRendererContext? _context;
        private DispatcherTimer? _renderTimer;
        private MeshShapeKey _meshShapeKey;
        private CollarShapeKey _collarShapeKey;
        private SpiralNormalMapKey _spiralNormalMapKey;
        private MetalMeshGpuResources? _meshResources;
        private MetalMeshGpuResources? _collarResources;
        private bool _viewportCollarStateLogged;
        private bool _offscreenCollarStateLogged;
        private int _paintMaskTextureVersion = -1;
        private bool _paintColorTextureNeedsClear = true;

        private KnobProject? _project;
        private bool _dirty = true;

        private float _orbitYawDeg = 30f;
        private float _orbitPitchDeg = -20f;
        private float _zoom = 1.0f;
        private Vector2 _panPx = Vector2.Zero;
        private bool _isOrbiting;
        private bool _isPanning;
        private bool _isPainting;
        private Point _lastPointer;
        private Point _lastPaintPointer;
        private Point _scratchVirtualPointer;
        private bool _scratchVirtualPointerInitialized;
        private float _scratchCurrentDepth;
        private uint _paintStrokeSeed;
        private KeyModifiers _lastKnownModifiers;
        private bool _optionDepthRampActive;
        private PaintHitMode _lastPaintHitMode = PaintHitMode.Idle;
        private readonly List<PaintStampCommand> _pendingPaintStampCommands = new();
        private readonly List<LightGizmoSnapshot> _lightGizmoSnapshots = new(16);
        private readonly List<ShadowPassConfig> _resolvedShadowPasses = new(MaxShadowPassLights);
        private readonly List<ShadowLightContribution> _shadowLightContributions = new(8);
        private OrientationDebug _orientation = new()
        {
            InvertX = true,
            InvertY = true,
            InvertZ = true,
            FlipCamera180 = true
        };
        private bool _gizmoInvertX;
        private bool _gizmoInvertY = true;
        private bool _gizmoInvertZ;
        private bool _invertImportedCollarOrbit;
        private bool _invertKnobFrontFaceWinding = true;
        private bool _invertImportedStlFrontFaceWinding = true;
        private ContextMenu? _debugContextMenu;

        public KnobProject? Project
        {
            get => _project;
            set
            {
                if (ReferenceEquals(_project, value))
                {
                    return;
                }

                _project = value;
                _meshResources = null;
                _collarResources = null;
                _meshShapeKey = default;
                _collarShapeKey = default;
                ReleaseSpiralNormalTexture();
                _spiralNormalMapKey = default;
                ReleasePaintMaskTexture();
                ReleasePaintColorTexture();
                ReleasePaintStampResources();
                ReleaseLightGizmoResources();
                _paintMaskTextureVersion = -1;
                _paintColorTextureNeedsClear = true;
                _pendingPaintStampCommands.Clear();
                _viewportCollarStateLogged = false;
                _offscreenCollarStateLogged = false;
                InvalidateGpu();
                PublishPaintHudSnapshot();
            }
        }

        public OrientationDebug CurrentOrientation => _orientation;
        public ViewportCameraState CurrentCameraState =>
            new(_orbitYawDeg, _orbitPitchDeg, _zoom, new SKPoint(_panPx.X, _panPx.Y));
        public bool CanRenderOffscreen =>
            OperatingSystem.IsMacOS() &&
            _context is not null &&
            _metalLayer != IntPtr.Zero &&
            _project is not null;
        public event Action<PaintHudSnapshot>? PaintHudUpdated;
        public event Action? ViewportFrameRendered;

        public MetalViewport()
        {
            Focusable = true;
            IsHitTestVisible = true;
        }

        public void RefreshPaintHud()
        {
            PublishPaintHudSnapshot();
        }

        public IReadOnlyList<LightGizmoSnapshot> GetLightGizmoSnapshots()
        {
            _lightGizmoSnapshots.Clear();
            if (_project is null || _project.Lights.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
            {
                return _lightGizmoSnapshots;
            }

            GetCameraBasis(out Vector3 right, out Vector3 up, out Vector3 forward);

            ModelNode? modelNode = _project.SceneRoot.Children.OfType<ModelNode>().FirstOrDefault();
            float referenceRadius = MathF.Max(1f, modelNode?.Radius ?? 220f);
            if (_meshResources is not null)
            {
                referenceRadius = MathF.Max(referenceRadius, _meshResources.ReferenceRadius);
            }

            if (_collarResources is not null)
            {
                referenceRadius = MathF.Max(referenceRadius, _collarResources.ReferenceRadius);
            }

            float cameraDistance = MathF.Max(1f, referenceRadius * 6f);
            Vector3 cameraPos = -forward * cameraDistance;
            Vector3 viewOrigin = -cameraPos;
            float referenceDepth = Vector3.Dot(viewOrigin, forward);
            float depthRange = MathF.Max(1f, referenceRadius * 2f);
            float renderScale = MathF.Max(1e-4f, GetRenderScale());
            float centerX = (float)(Bounds.Width * 0.5) + _panPx.X;
            float centerY = (float)(Bounds.Height * 0.5) + _panPx.Y;
            float zoomDip = _zoom / renderScale;

            for (int i = 0; i < _project.Lights.Count; i++)
            {
                KnobLight light = _project.Lights[i];
                Vector3 lightPos = ApplyGizmoDisplayOrientation(
                    ApplyLightOrientation(new Vector3(light.X, light.Y, light.Z)));
                Vector3 viewLight = lightPos - cameraPos;
                Point gizmoPoint = new(
                    centerX + (Vector3.Dot(viewLight, right) * zoomDip),
                    centerY - (Vector3.Dot(viewLight, up) * zoomDip));
                Point originPoint = new(
                    centerX + (Vector3.Dot(viewOrigin, right) * zoomDip),
                    centerY - (Vector3.Dot(viewOrigin, up) * zoomDip));

                float depth = Vector3.Dot(viewLight, forward);
                float depthOffset = (depth - referenceDepth) / depthRange;
                float nearFactor = (1f - Math.Clamp(depthOffset, -1f, 1f)) * 0.5f;
                double radiusDip = 4d + (nearFactor * 5f);
                byte fillAlpha = (byte)(110 + (nearFactor * 145f));
                byte lineAlpha = (byte)(70 + (nearFactor * 120f));
                bool isSelected = i == _project.SelectedLightIndex;
                double selectedRingRadiusDip = Math.Max(radiusDip + 4d, 10d);

                bool hasDirectionTip = false;
                Point directionTipPoint = default;
                double directionTipRadiusDip = 2.5d;
                if (light.Type == LightType.Directional)
                {
                    Vector3 lightDir = ApplyGizmoDisplayOrientation(ApplyLightOrientation(GetDirectionalVector(light)));
                    if (lightDir.LengthSquared() > 1e-8f)
                    {
                        lightDir = Vector3.Normalize(lightDir);
                        directionTipPoint = new Point(
                            gizmoPoint.X + ((Vector3.Dot(lightDir, right) * 20f) / renderScale),
                            gizmoPoint.Y - ((Vector3.Dot(lightDir, up) * 20f) / renderScale));
                        hasDirectionTip = true;
                    }
                }

                _lightGizmoSnapshots.Add(new LightGizmoSnapshot(
                    positionDip: gizmoPoint,
                    originDip: originPoint,
                    directionTipDip: directionTipPoint,
                    hasDirectionTip: hasDirectionTip,
                    colorR: light.Color.Red,
                    colorG: light.Color.Green,
                    colorB: light.Color.Blue,
                    isSelected: isSelected,
                    radiusDip: radiusDip,
                    selectedRingRadiusDip: selectedRingRadiusDip,
                    directionTipRadiusDip: directionTipRadiusDip,
                    fillAlpha: fillAlpha,
                    lineAlpha: lineAlpha));
            }

            return _lightGizmoSnapshots;
        }

        private void PublishPaintHudSnapshot()
        {
            Action<PaintHudSnapshot>? handler = PaintHudUpdated;
            if (handler is null)
            {
                return;
            }

            if (_project is null)
            {
                handler(new PaintHudSnapshot(
                    paintEnabled: false,
                    isPainting: false,
                    channel: PaintChannel.Rust,
                    brushType: PaintBrushType.Spray,
                    abrasionType: ScratchAbrasionType.Needle,
                    activeSizePx: 0f,
                    activeOpacity: 0f,
                    liveScratchDepth: 0f,
                    optionDepthRampActive: false,
                    hitMode: PaintHitMode.Idle));
                return;
            }

            bool scratchChannel = _project.BrushChannel == PaintChannel.Scratch;
            float activeSizePx = scratchChannel
                ? Math.Clamp(_project.ScratchWidthPx, 1f, 320f)
                : Math.Clamp(_project.BrushSizePx, 1f, 320f);
            float liveScratchDepth = scratchChannel
                ? Math.Clamp(_isPainting ? _scratchCurrentDepth : _project.ScratchDepth, 0f, 1f)
                : 0f;

            handler(new PaintHudSnapshot(
                paintEnabled: _project.BrushPaintingEnabled,
                isPainting: _isPainting,
                channel: _project.BrushChannel,
                brushType: _project.BrushType,
                abrasionType: _project.ScratchAbrasionType,
                activeSizePx: activeSizePx,
                activeOpacity: Math.Clamp(_project.BrushOpacity, 0f, 1f),
                liveScratchDepth: liveScratchDepth,
                optionDepthRampActive: _optionDepthRampActive,
                hitMode: _lastPaintHitMode));
        }

        public void InvalidateGpu()
        {
            _dirty = true;
            if (_metalLayer != IntPtr.Zero)
            {
                Render(_ => { });
            }
        }

        public void ResetCamera()
        {
            _orbitYawDeg = 30f;
            _orbitPitchDeg = -20f;
            _zoom = 1.0f;
            _panPx = Vector2.Zero;
            InvalidateGpu();
        }

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

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            if (!OperatingSystem.IsMacOS())
            {
                return base.CreateNativeControlCore(parent);
            }

            _context = MetalRendererContext.Instance;
            IntPtr device = _context.Device.Handle;
            if (device == IntPtr.Zero)
            {
                return base.CreateNativeControlCore(parent);
            }

            _nativeView = ObjC.IntPtr_objc_msgSend(ObjC.IntPtr_objc_msgSend(ObjCClasses.NSView, Selectors.Alloc), Selectors.Init);
            _metalLayer = ObjC.IntPtr_objc_msgSend(ObjC.IntPtr_objc_msgSend(ObjCClasses.CAMetalLayer, Selectors.Alloc), Selectors.Init);

            ObjC.Void_objc_msgSend_IntPtr(_metalLayer, Selectors.SetDevice, device);
            ObjC.Void_objc_msgSend_UInt(_metalLayer, Selectors.SetPixelFormat, (nuint)MetalRendererContext.DefaultColorFormat);
            ObjC.Void_objc_msgSend_Bool(_metalLayer, Selectors.SetFramebufferOnly, false);
            ObjC.Void_objc_msgSend_Double(_metalLayer, Selectors.SetContentsScale, GetRenderScale());

            ObjC.Void_objc_msgSend_Bool(_nativeView, Selectors.SetWantsLayer, true);
            ObjC.Void_objc_msgSend_IntPtr(_nativeView, Selectors.SetLayer, _metalLayer);

            UpdateDrawableSize(Bounds.Size);
            StartRenderLoop();
            InvalidateGpu();

            return new PlatformHandle(_nativeView, "NSView");
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            if (OperatingSystem.IsMacOS())
            {
                StopRenderLoop();

                if (_metalLayer != IntPtr.Zero)
                {
                    ObjC.Void_objc_msgSend(_metalLayer, Selectors.Release);
                    _metalLayer = IntPtr.Zero;
                }

                if (_nativeView != IntPtr.Zero)
                {
                    ObjC.Void_objc_msgSend(_nativeView, Selectors.Release);
                    _nativeView = IntPtr.Zero;
                }

                if (_depthTexture != IntPtr.Zero)
                {
                    ObjC.Void_objc_msgSend(_depthTexture, Selectors.Release);
                    _depthTexture = IntPtr.Zero;
                    _depthTextureWidth = 0;
                    _depthTextureHeight = 0;
                }

                ReleaseSpiralNormalTexture();
                _spiralNormalMapKey = default;
                ReleasePaintMaskTexture();
                ReleasePaintColorTexture();
                ReleasePaintStampResources();
                ReleaseLightGizmoResources();
                _paintMaskTextureVersion = -1;
                _paintColorTextureNeedsClear = true;
                _pendingPaintStampCommands.Clear();

                _meshResources = null;
                _meshShapeKey = default;
                _context = null;
            }

            base.DestroyNativeControlCore(control);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Size arranged = base.ArrangeOverride(finalSize);
            UpdateDrawableSize(finalSize);
            _dirty = true;
            return arranged;
        }

        public void Render(Action<MTLRenderCommandEncoder> encode)
        {
            if (!OperatingSystem.IsMacOS() || _context is null || _metalLayer == IntPtr.Zero)
            {
                return;
            }

            IntPtr drawable = ObjC.IntPtr_objc_msgSend(_metalLayer, Selectors.NextDrawable);
            if (drawable == IntPtr.Zero)
            {
                return;
            }

            IntPtr texture = ObjC.IntPtr_objc_msgSend(drawable, Selectors.Texture);
            if (texture == IntPtr.Zero)
            {
                return;
            }

            nuint drawableWidth = ObjC.UInt_objc_msgSend(texture, Selectors.Width);
            nuint drawableHeight = ObjC.UInt_objc_msgSend(texture, Selectors.Height);
            EnsureDepthTexture(drawableWidth, drawableHeight);

            IntPtr passDescriptor = ObjC.IntPtr_objc_msgSend(ObjCClasses.MTLRenderPassDescriptor, Selectors.RenderPassDescriptor);
            IntPtr colorAttachments = ObjC.IntPtr_objc_msgSend(passDescriptor, Selectors.ColorAttachments);
            IntPtr attachment = ObjC.IntPtr_objc_msgSend_UInt(colorAttachments, Selectors.ObjectAtIndexedSubscript, 0);
            if (attachment == IntPtr.Zero)
            {
                return;
            }

            ObjC.Void_objc_msgSend_IntPtr(attachment, Selectors.SetTexture, texture);
            ObjC.Void_objc_msgSend_UInt(attachment, Selectors.SetLoadAction, 2); // MTLLoadActionClear
            ObjC.Void_objc_msgSend_UInt(attachment, Selectors.SetStoreAction, 1); // MTLStoreActionStore
            ObjC.Void_objc_msgSend_MTLClearColor(attachment, Selectors.SetClearColor, new MTLClearColor(0.07d, 0.07d, 0.09d, 1d));

            if (_depthTexture != IntPtr.Zero)
            {
                IntPtr depthAttachment = ObjC.IntPtr_objc_msgSend(passDescriptor, Selectors.DepthAttachment);
                if (depthAttachment != IntPtr.Zero)
                {
                    ObjC.Void_objc_msgSend_IntPtr(depthAttachment, Selectors.SetTexture, _depthTexture);
                    ObjC.Void_objc_msgSend_UInt(depthAttachment, Selectors.SetLoadAction, 2); // MTLLoadActionClear
                    ObjC.Void_objc_msgSend_UInt(depthAttachment, Selectors.SetStoreAction, 0); // MTLStoreActionDontCare
                    ObjC.Void_objc_msgSend_Double(depthAttachment, Selectors.SetClearDepth, 1.0);
                }
            }

            IntPtr commandBuffer = _context.CreateCommandBuffer().Handle;
            if (commandBuffer == IntPtr.Zero)
            {
                return;
            }

            ModelNode? modelNode = _project?.SceneRoot.Children.OfType<ModelNode>().FirstOrDefault();
            RefreshMeshResources(_project, modelNode);
            CollarNode? collarNode = modelNode?.Children.OfType<CollarNode>().FirstOrDefault();

            if (_meshResources != null && _meshResources.VertexBuffer.Handle != IntPtr.Zero && _meshResources.IndexBuffer.Handle != IntPtr.Zero)
            {
                bool drawCollar = _collarResources != null && _collarResources.VertexBuffer.Handle != IntPtr.Zero && _collarResources.IndexBuffer.Handle != IntPtr.Zero && collarNode is { Enabled: true };
                if (!_viewportCollarStateLogged)
                {
                    _viewportCollarStateLogged = true;
                    LogCollarState("viewport", collarNode, _collarResources);
                }
                float sceneReferenceRadius = drawCollar
                    ? MathF.Max(_meshResources.ReferenceRadius, _collarResources!.ReferenceRadius)
                    : _meshResources.ReferenceRadius;
                GpuUniforms knobUniforms = BuildUniforms(_project, modelNode, sceneReferenceRadius, Bounds.Size);
                GpuUniforms collarUniforms = drawCollar
                    ? BuildCollarUniforms(knobUniforms, collarNode!)
                    : default;
                EnsurePaintMaskTexture(_project);
                EnsurePaintColorTexture(_project);
                ApplyPendingPaintStamps(commandBuffer);
                if (drawCollar && _invertImportedCollarOrbit && IsImportedCollarPreset(collarNode))
                {
                    collarUniforms.ModelRotationCosSin.Y = -collarUniforms.ModelRotationCosSin.Y;
                }

                IntPtr encoderPtr = ObjC.IntPtr_objc_msgSend_IntPtr(commandBuffer, Selectors.RenderCommandEncoderWithDescriptor, passDescriptor);
                if (encoderPtr != IntPtr.Zero)
                {
                    MetalPipelineManager pipelineManager = MetalPipelineManager.Instance;
                    pipelineManager.UsePipeline(new MTLRenderCommandEncoderHandle(encoderPtr));

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

                    Size viewportDip = Bounds.Size;
                    float renderScale = GetRenderScale();
                    float viewportWidthPx = MathF.Max(1f, (float)viewportDip.Width * renderScale);
                    float viewportHeightPx = MathF.Max(1f, (float)viewportDip.Height * renderScale);
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
                    IReadOnlyList<ShadowPassConfig> shadowConfigs = ResolveShadowPassConfigs(_project, right, up, forward, viewportWidthPx, viewportHeightPx);

                    encode(new MTLRenderCommandEncoder(encoderPtr));

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
                }
            }

            RenderLightGizmoOverlayPass(commandBuffer, texture, drawableWidth, drawableHeight);
            ObjC.Void_objc_msgSend_IntPtr(commandBuffer, Selectors.PresentDrawable, drawable);
            ObjC.Void_objc_msgSend(commandBuffer, Selectors.Commit);
            _dirty = false;
            ViewportFrameRendered?.Invoke();
        }

        private void RefreshMeshResources(KnobProject? project, ModelNode? modelNode)
        {
            if (_context is null || project is null || modelNode is null)
            {
                _meshResources = null;
                _collarResources = null;
                _collarShapeKey = default;
                return;
            }

            CollarNode? collarNode = modelNode.Children.OfType<CollarNode>().FirstOrDefault();
            CollarShapeKey nextCollarKey = BuildCollarShapeKey(modelNode, collarNode);
            bool collarEnabled = collarNode is { Enabled: true } && collarNode.Preset != CollarPreset.None;
            bool collarShapeChanged = !nextCollarKey.Equals(_collarShapeKey);
            if (!collarEnabled)
            {
                _collarResources = null;
                _collarShapeKey = default;
            }
            else if (collarShapeChanged || _collarResources == null)
            {
                _collarShapeKey = nextCollarKey;
                CollarMesh? collarMesh = CollarMeshBuilder.TryBuildFromProject(project);
                if (collarMesh is null || collarMesh.Vertices.Length == 0 || collarMesh.Indices.Length == 0)
                {
                    Console.WriteLine(
                        $"[MetalViewport] Collar mesh build failed. enabled={collarEnabled}, preset={collarNode?.Preset}, pathSegments={collarNode?.PathSegments ?? 0}, crossSegments={collarNode?.CrossSegments ?? 0}, importPath={collarNode?.ImportedMeshPath ?? "<none>"}");
                    _collarResources = null;
                }
                else
                {
                    _collarResources = CreateGpuResources(collarMesh.Vertices, collarMesh.Indices, collarMesh.ReferenceRadius);
                }
            }

            MeshShapeKey nextKey = new(
                MathF.Round(modelNode.Radius, 3),
                MathF.Round(modelNode.Height, 3),
                MathF.Round(modelNode.Bevel, 3),
                MathF.Round(modelNode.TopRadiusScale, 3),
                modelNode.RadialSegments,
                MathF.Round(modelNode.CrownProfile, 4),
                MathF.Round(modelNode.BevelCurve, 4),
                MathF.Round(modelNode.BodyTaper, 4),
                MathF.Round(modelNode.BodyBulge, 4),
                MathF.Round(modelNode.SpiralRidgeHeight, 3),
                MathF.Round(modelNode.SpiralRidgeWidth, 3),
                MathF.Round(modelNode.SpiralRidgeHeightVariance, 3),
                MathF.Round(modelNode.SpiralRidgeWidthVariance, 3),
                MathF.Round(modelNode.SpiralHeightVarianceThreshold, 3),
                MathF.Round(modelNode.SpiralWidthVarianceThreshold, 3),
                MathF.Round(modelNode.SpiralTurns, 3),
                (int)modelNode.GripType,
                MathF.Round(modelNode.GripStart, 4),
                MathF.Round(modelNode.GripHeight, 4),
                MathF.Round(modelNode.GripDensity, 3),
                MathF.Round(modelNode.GripPitch, 3),
                MathF.Round(modelNode.GripDepth, 3),
                MathF.Round(modelNode.GripWidth, 4),
                MathF.Round(modelNode.GripSharpness, 3),
                modelNode.IndicatorEnabled ? 1 : 0,
                (int)modelNode.IndicatorShape,
                (int)modelNode.IndicatorRelief,
                (int)modelNode.IndicatorProfile,
                MathF.Round(modelNode.IndicatorWidthRatio, 4),
                MathF.Round(modelNode.IndicatorLengthRatioTop, 4),
                MathF.Round(modelNode.IndicatorPositionRatio, 4),
                MathF.Round(modelNode.IndicatorThicknessRatio, 4),
                MathF.Round(modelNode.IndicatorRoundness, 4),
                modelNode.IndicatorCadWallsEnabled ? 1 : 0);

            if (_meshResources != null && nextKey.Equals(_meshShapeKey))
            {
                EnsureSpiralNormalTexture(modelNode, _meshResources.ReferenceRadius);
                return;
            }

            MetalMesh? mesh = MetalMeshBuilder.TryBuildFromProject(project);
            if (mesh is null || mesh.Vertices.Length == 0 || mesh.Indices.Length == 0)
            {
                _meshResources = null;
                _meshShapeKey = default;
                return;
            }

            _meshResources = CreateGpuResources(mesh.Vertices, mesh.Indices, mesh.ReferenceRadius);
            if (_meshResources == null)
            {
                _meshShapeKey = default;
                return;
            }

            _meshShapeKey = nextKey;
            EnsureSpiralNormalTexture(modelNode, mesh.ReferenceRadius);
        }

        private MetalMeshGpuResources? CreateGpuResources(MetalVertex[] vertices, uint[] indices, float referenceRadius)
        {
            if (_context is null)
            {
                return null;
            }

            IMTLBuffer vertexBuffer = _context.CreateBuffer<MetalVertex>(vertices);
            IMTLBuffer indexBuffer = _context.CreateBuffer<uint>(indices);
            if (vertexBuffer.Handle == IntPtr.Zero || indexBuffer.Handle == IntPtr.Zero)
            {
                return null;
            }

            var positions = new Vector3[vertices.Length];
            Vector3 boundsMin = new(float.MaxValue);
            Vector3 boundsMax = new(float.MinValue);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = vertices[i].Position;
                positions[i] = p;
                boundsMin = Vector3.Min(boundsMin, p);
                boundsMax = Vector3.Max(boundsMax, p);
            }

            return new MetalMeshGpuResources
            {
                VertexBuffer = vertexBuffer,
                IndexBuffer = indexBuffer,
                IndexCount = indices.Length,
                IndexType = MTLIndexType.UInt32,
                ReferenceRadius = referenceRadius,
                Positions = positions,
                Indices = indices.ToArray(),
                BoundsMin = boundsMin,
                BoundsMax = boundsMax
            };
        }

        private static bool IsImportedCollarPreset(CollarNode? collarNode)
        {
            return collarNode is not null && CollarNode.IsImportedMeshPreset(collarNode.Preset);
        }

        private static string ResolveImportedMeshPath(CollarNode collarNode)
        {
            return CollarNode.ResolveImportedMeshPath(collarNode.Preset, collarNode.ImportedMeshPath);
        }

        private static CollarShapeKey BuildCollarShapeKey(ModelNode modelNode, CollarNode? collarNode)
        {
            if (collarNode is null)
            {
                return default;
            }

            string importedMeshPath = ResolveImportedMeshPath(collarNode);
            long importedFileTicks = 0;
            if (!string.IsNullOrWhiteSpace(importedMeshPath) && File.Exists(importedMeshPath))
            {
                importedFileTicks = File.GetLastWriteTimeUtc(importedMeshPath).Ticks;
            }

            return new CollarShapeKey(
                collarNode.Enabled ? 1 : 0,
                (int)collarNode.Preset,
                MathF.Round(modelNode.Radius, 3),
                MathF.Round(modelNode.Height, 3),
                MathF.Round(collarNode.InnerRadiusRatio, 4),
                MathF.Round(collarNode.GapToKnobRatio, 4),
                MathF.Round(collarNode.ElevationRatio, 4),
                MathF.Round(collarNode.OverallRotationRadians, 4),
                MathF.Round(collarNode.BiteAngleRadians, 4),
                MathF.Round(collarNode.BodyRadiusRatio, 4),
                MathF.Round(collarNode.BodyEllipseYScale, 4),
                MathF.Round(collarNode.NeckTaper, 4),
                MathF.Round(collarNode.TailTaper, 4),
                MathF.Round(collarNode.MassBias, 4),
                MathF.Round(collarNode.TailUnderlap, 4),
                MathF.Round(collarNode.HeadScale, 4),
                MathF.Round(collarNode.JawBulge, 4),
                collarNode.UvSeamFollowBite ? 1 : 0,
                MathF.Round(collarNode.UvSeamOffset, 4),
                collarNode.PathSegments,
                collarNode.CrossSegments,
                MathF.Round(collarNode.ImportedScale, 4),
                MathF.Round(collarNode.ImportedBodyLengthScale, 4),
                MathF.Round(collarNode.ImportedBodyThicknessScale, 4),
                MathF.Round(collarNode.ImportedHeadLengthScale, 4),
                MathF.Round(collarNode.ImportedHeadThicknessScale, 4),
                MathF.Round(collarNode.ImportedRotationRadians, 4),
                MathF.Round(collarNode.ImportedOffsetXRatio, 4),
                MathF.Round(collarNode.ImportedOffsetYRatio, 4),
                MathF.Round(collarNode.ImportedInflateRatio, 4),
                importedMeshPath,
                importedFileTicks);
        }

        private GpuUniforms BuildUniforms(KnobProject? project, ModelNode? modelNode, float referenceRadius, Size viewportDip)
        {
            float renderScale = GetRenderScale();
            float viewportWidthPx = MathF.Max(1f, (float)viewportDip.Width * renderScale);
            float viewportHeightPx = MathF.Max(1f, (float)viewportDip.Height * renderScale);
            return BuildUniformsForPixels(project, modelNode, referenceRadius, viewportWidthPx, viewportHeightPx);
        }

        private GpuUniforms BuildUniformsForPixels(
            KnobProject? project,
            ModelNode? modelNode,
            float referenceRadius,
            float viewportWidthPx,
            float viewportHeightPx)
        {
            GetCameraBasis(out Vector3 right, out Vector3 up, out Vector3 forward);

            float scaleX = (2f * _zoom) / MathF.Max(1f, viewportWidthPx);
            float scaleY = (2f * _zoom) / MathF.Max(1f, viewportHeightPx);
            float scaleZ = scaleX;
            float offsetX = (2f * _panPx.X) / MathF.Max(1f, viewportWidthPx);
            float offsetY = (-2f * _panPx.Y) / MathF.Max(1f, viewportHeightPx);

            float radius = MathF.Max(1f, referenceRadius);
            Vector3 cameraPos = -forward * (radius * 6f);

            MaterialNode? materialNode = modelNode?.Children.OfType<MaterialNode>().FirstOrDefault();
            Vector3 baseColor = materialNode?.BaseColor ?? new Vector3(0.55f, 0.16f, 0.16f);
            float metallic = Math.Clamp(materialNode?.Metallic ?? 0f, 0f, 1f);
            float roughness = Math.Clamp(materialNode?.Roughness ?? 0.5f, 0.04f, 1f);
            float pearlescence = Math.Clamp(materialNode?.Pearlescence ?? 0f, 0f, 1f);
            float rustAmount = Math.Clamp(materialNode?.RustAmount ?? 0f, 0f, 1f);
            float wearAmount = Math.Clamp(materialNode?.WearAmount ?? 0f, 0f, 1f);
            float gunkAmount = Math.Clamp(materialNode?.GunkAmount ?? 0f, 0f, 1f);
            float diffuseStrength = materialNode?.DiffuseStrength ?? 1f;
            float specularStrength = materialNode?.SpecularStrength ?? 1f;
            float brushStrength = Math.Clamp(materialNode?.RadialBrushStrength ?? 0f, 0f, 1f);
            float brushDensity = MathF.Max(1f, materialNode?.RadialBrushDensity ?? 56f);
            float surfaceCharacter = Math.Clamp(materialNode?.SurfaceCharacter ?? 0f, 0f, 1f);
            bool partMaterialsEnabled = materialNode?.PartMaterialsEnabled ?? false;
            Vector3 topBaseColor = materialNode?.TopBaseColor ?? baseColor;
            Vector3 bevelBaseColor = materialNode?.BevelBaseColor ?? baseColor;
            Vector3 sideBaseColor = materialNode?.SideBaseColor ?? baseColor;
            float topMetallic = partMaterialsEnabled
                ? Math.Clamp(materialNode?.TopMetallic ?? metallic, 0f, 1f)
                : metallic;
            float bevelMetallic = partMaterialsEnabled
                ? Math.Clamp(materialNode?.BevelMetallic ?? metallic, 0f, 1f)
                : metallic;
            float sideMetallic = partMaterialsEnabled
                ? Math.Clamp(materialNode?.SideMetallic ?? metallic, 0f, 1f)
                : metallic;
            float topRoughness = partMaterialsEnabled
                ? Math.Clamp(materialNode?.TopRoughness ?? roughness, 0.04f, 1f)
                : roughness;
            float bevelRoughness = partMaterialsEnabled
                ? Math.Clamp(materialNode?.BevelRoughness ?? roughness, 0.04f, 1f)
                : roughness;
            float sideRoughness = partMaterialsEnabled
                ? Math.Clamp(materialNode?.SideRoughness ?? roughness, 0.04f, 1f)
                : roughness;
            float indicatorEnabled = modelNode?.IndicatorEnabled == true ? 1f : 0f;
            float indicatorShape = (float)(modelNode?.IndicatorShape ?? IndicatorShape.Bar);
            float indicatorWidth = modelNode?.IndicatorWidthRatio ?? 0.06f;
            float indicatorLength = modelNode?.IndicatorLengthRatioTop ?? 0.28f;
            float indicatorPosition = modelNode?.IndicatorPositionRatio ?? 0.46f;
            float indicatorRoundness = modelNode?.IndicatorRoundness ?? 0f;
            Vector3 indicatorColor = modelNode?.IndicatorColor ?? new Vector3(0.97f, 0.96f, 0.92f);
            float indicatorColorBlend = modelNode?.IndicatorColorBlend ?? 1f;
            float turns = MathF.Max(1f, modelNode?.SpiralTurns ?? 220f);

            float modelRotationRadians = modelNode?.RotationRadians ?? 0f;
            float modelCos = MathF.Cos(modelRotationRadians);
            float modelSin = MathF.Sin(modelRotationRadians);
            float topScale = Math.Clamp(modelNode?.TopRadiusScale ?? 0.86f, 0.30f, 1.30f);
            float knobBaseRadius = MathF.Max(1f, modelNode?.Radius ?? radius);
            float knobTopRadius = knobBaseRadius * topScale;
            float spacingPx = (knobTopRadius / turns) * _zoom;
            float geometryKeep = SmoothStep(0.20f, 0.90f, spacingPx);
            float frontZ = (modelNode?.Height ?? (radius * 2f)) * 0.5f;

            GpuUniforms uniforms = default;
            uniforms.CameraPosAndReferenceRadius = new Vector4(cameraPos, radius);
            uniforms.RightAndScaleX = new Vector4(right, scaleX);
            uniforms.UpAndScaleY = new Vector4(up, scaleY);
            uniforms.ForwardAndScaleZ = new Vector4(forward, scaleZ);
            uniforms.ProjectionOffsetsAndLightCount = new Vector4(offsetX, offsetY, 0f, 0f);
            uniforms.MaterialBaseColorAndMetallic = new Vector4(baseColor, metallic);
            uniforms.MaterialRoughnessDiffuseSpecMode = new Vector4(roughness, diffuseStrength, specularStrength, (float)(project?.Mode ?? LightingMode.Both));
            uniforms.MaterialPartTopColorAndMetallic = new Vector4(topBaseColor, topMetallic);
            uniforms.MaterialPartBevelColorAndMetallic = new Vector4(bevelBaseColor, bevelMetallic);
            uniforms.MaterialPartSideColorAndMetallic = new Vector4(sideBaseColor, sideMetallic);
            uniforms.MaterialPartRoughnessAndEnable = new Vector4(
                topRoughness,
                bevelRoughness,
                sideRoughness,
                partMaterialsEnabled ? 1f : 0f);
            uniforms.MaterialSurfaceBrushParams = new Vector4(brushStrength, brushDensity, surfaceCharacter, geometryKeep);
            uniforms.WeatherParams = new Vector4(rustAmount, wearAmount, gunkAmount, Math.Clamp(project?.BrushDarkness ?? 0.58f, 0f, 1f));
            Vector3 scratchExposeColor = project?.ScratchExposeColor ?? new Vector3(0.88f, 0.88f, 0.90f);
            uniforms.ScratchExposeColorAndStrength = new Vector4(scratchExposeColor, 1f);
            uniforms.IndicatorParams0 = new Vector4(indicatorEnabled, indicatorShape, indicatorWidth, indicatorLength);
            // Keep top-cap/indicator normalization stable even when scene bounds grow (e.g. collar enabled).
            uniforms.IndicatorParams1 = new Vector4(indicatorRoundness, indicatorPosition, knobTopRadius, pearlescence);
            uniforms.IndicatorColorAndBlend = new Vector4(indicatorColor, indicatorColorBlend);
            if (project != null)
            {
                uniforms.MicroDetailParams = new Vector4(
                    project.SpiralNormalInfluenceEnabled ? 1f : 0f,
                    project.SpiralNormalLodFadeStart,
                    project.SpiralNormalLodFadeEnd,
                    project.SpiralRoughnessLodBoost);
            }
            else
            {
                uniforms.MicroDetailParams = new Vector4(1f, 0.55f, 2.4f, 0.20f);
            }

            if (project != null)
            {
                Vector3 envTop = project.EnvironmentTopColor;
                Vector3 envBottom = project.EnvironmentBottomColor;
                float envIntensity = MathF.Max(0f, project.EnvironmentIntensity);
                float envRoughMix = Math.Clamp(project.EnvironmentRoughnessMix, 0f, 1f);
                uniforms.EnvironmentTopColorAndIntensity = new Vector4(envTop, envIntensity);
                uniforms.EnvironmentBottomColorAndRoughnessMix = new Vector4(envBottom, envRoughMix);
            }
            else
            {
                uniforms.EnvironmentTopColorAndIntensity = new Vector4(0.12f, 0.12f, 0.13f, 1f);
                uniforms.EnvironmentBottomColorAndRoughnessMix = new Vector4(0.02f, 0.02f, 0.02f, 1f);
            }

            uniforms.ModelRotationCosSin = new Vector4(modelCos, modelSin, topScale, frontZ);
            uniforms.ShadowParams = Vector4.Zero;
            uniforms.ShadowColorAndOpacity = Vector4.Zero;
            uniforms.DebugBasisParams = new Vector4(
                (float)(project?.BasisDebug ?? BasisDebugMode.Off),
                Math.Clamp(project?.ScratchDepth ?? 0.30f, 0f, 1f),
                0f,
                0f);

            if (project != null)
            {
                int lightCount = Math.Min(project.Lights.Count, MaxGpuLights);
                uniforms.ProjectionOffsetsAndLightCount.Z = lightCount;

                for (int i = 0; i < lightCount; i++)
                {
                    KnobLight light = project.Lights[i];
                    Vector3 lightPos = ApplyLightOrientation(new Vector3(light.X, light.Y, light.Z));
                    Vector3 lightDir = ApplyLightOrientation(GetDirectionalVector(light));
                    if (lightDir.LengthSquared() > 1e-8f)
                    {
                        lightDir = Vector3.Normalize(lightDir);
                    }
                    else
                    {
                        lightDir = Vector3.UnitZ;
                    }

                    GpuLight packed = new()
                    {
                        PositionType = new Vector4(
                            lightPos,
                            light.Type == LightType.Directional ? 1f : 0f),
                        Direction = new Vector4(lightDir, 0f),
                        ColorIntensity = new Vector4(
                            light.Color.Red / 255f,
                            light.Color.Green / 255f,
                            light.Color.Blue / 255f,
                            MathF.Max(0f, light.Intensity)),
                        Params0 = new Vector4(
                            MathF.Max(0f, light.Falloff),
                            MathF.Max(0f, light.DiffuseBoost),
                            MathF.Max(0f, light.SpecularBoost),
                            MathF.Max(1f, light.SpecularPower))
                    };

                    SetGpuLight(ref uniforms, i, packed);
                }
            }

            return uniforms;
        }

        private static GpuUniforms BuildCollarUniforms(in GpuUniforms baseUniforms, CollarNode collarNode)
        {
            GpuUniforms uniforms = baseUniforms;
            uniforms.MaterialBaseColorAndMetallic = new Vector4(collarNode.BaseColor, collarNode.Metallic);
            uniforms.MaterialRoughnessDiffuseSpecMode.X = collarNode.Roughness;
            uniforms.MaterialRoughnessDiffuseSpecMode.Y = 1f;
            uniforms.MaterialRoughnessDiffuseSpecMode.Z = 1f;
            uniforms.MaterialPartTopColorAndMetallic = uniforms.MaterialBaseColorAndMetallic;
            uniforms.MaterialPartBevelColorAndMetallic = uniforms.MaterialBaseColorAndMetallic;
            uniforms.MaterialPartSideColorAndMetallic = uniforms.MaterialBaseColorAndMetallic;
            uniforms.MaterialPartRoughnessAndEnable = new Vector4(collarNode.Roughness, collarNode.Roughness, collarNode.Roughness, 0f);
            uniforms.MaterialSurfaceBrushParams = new Vector4(0f, 56f, 0f, 1f);
            uniforms.WeatherParams = new Vector4(
                Math.Clamp(collarNode.RustAmount, 0f, 1f),
                Math.Clamp(collarNode.WearAmount, 0f, 1f),
                Math.Clamp(collarNode.GunkAmount, 0f, 1f),
                baseUniforms.WeatherParams.W);
            uniforms.IndicatorParams0 = Vector4.Zero;
            uniforms.IndicatorParams1 = new Vector4(0f, 0f, 0f, Math.Clamp(collarNode.Pearlescence, 0f, 1f));
            uniforms.IndicatorColorAndBlend = Vector4.Zero;
            uniforms.MicroDetailParams.X = 0f;
            uniforms.MicroDetailParams.W = 0f;
            return uniforms;
        }

        private static void LogCollarState(string pass, CollarNode? collarNode, MetalMeshGpuResources? collarResources)
        {
            _ = pass;
            _ = collarNode;
            _ = collarResources;
        }

        private Vector3 ApplyLightOrientation(Vector3 value)
        {
            if (_orientation.InvertX)
            {
                value.X = -value.X;
            }

            if (_orientation.InvertY)
            {
                value.Y = -value.Y;
            }

            if (_orientation.InvertZ)
            {
                value.Z = -value.Z;
            }

            return value;
        }

        private Vector3 ApplyGizmoDisplayOrientation(Vector3 value)
        {
            if (_gizmoInvertX)
            {
                value.X = -value.X;
            }

            if (_gizmoInvertY)
            {
                value.Y = -value.Y;
            }

            if (_gizmoInvertZ)
            {
                value.Z = -value.Z;
            }

            return value;
        }

        private static Vector3 GetDirectionalVector(KnobLight light)
        {
            float z = light.Z / 300f;
            Vector3 dir = new(MathF.Cos(light.DirectionRadians), MathF.Sin(light.DirectionRadians), z);
            if (dir.LengthSquared() < 1e-6f)
            {
                return Vector3.UnitZ;
            }

            return Vector3.Normalize(dir);
        }

        private static void SetGpuLight(ref GpuUniforms uniforms, int index, in GpuLight light)
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

        private static void UploadUniforms(IntPtr encoderPtr, in GpuUniforms uniforms)
        {
            int uniformSize = Marshal.SizeOf<GpuUniforms>();
            IntPtr uniformPtr = Marshal.AllocHGlobal(uniformSize);
            try
            {
                Marshal.StructureToPtr(uniforms, uniformPtr, false);
                ObjC.Void_objc_msgSend_IntPtr_UInt_UInt(
                    encoderPtr,
                    Selectors.SetVertexBytesLengthAtIndex,
                    uniformPtr,
                    (nuint)uniformSize,
                    1);
                ObjC.Void_objc_msgSend_IntPtr_UInt_UInt(
                    encoderPtr,
                    Selectors.SetFragmentBytesLengthAtIndex,
                    uniformPtr,
                    (nuint)uniformSize,
                    1);
            }
            finally
            {
                Marshal.FreeHGlobal(uniformPtr);
            }
        }

        private static readonly Vector2[] ShadowSampleKernel =
        {
            new(0.0f, 0.0f),
            new(0.285f, -0.192f),
            new(-0.247f, 0.208f),
            new(0.118f, 0.326f),
            new(-0.332f, -0.087f),
            new(0.402f, 0.094f),
            new(-0.116f, -0.375f),
            new(0.046f, 0.462f),
            new(-0.463f, 0.041f),
            new(0.353f, -0.323f),
            new(-0.294f, -0.334f),
            new(0.214f, 0.452f),
            new(-0.027f, -0.497f),
            new(0.492f, -0.028f),
            new(-0.438f, 0.238f),
            new(0.165f, -0.468f)
        };

        private static void RenderShadowPasses(
            IntPtr encoderPtr,
            in GpuUniforms baseUniforms,
            in ShadowPassConfig config,
            MetalMeshGpuResources mesh)
        {
            if (encoderPtr == IntPtr.Zero ||
                mesh.VertexBuffer.Handle == IntPtr.Zero ||
                mesh.IndexBuffer.Handle == IntPtr.Zero ||
                config.Alpha <= 1e-5f)
            {
                return;
            }

            ObjC.Void_objc_msgSend_IntPtr_UInt_UInt(
                encoderPtr,
                Selectors.SetVertexBufferOffsetAtIndex,
                mesh.VertexBuffer.Handle,
                0,
                0);

            int sampleCount = Math.Clamp(config.SampleCount, 1, ShadowSampleKernel.Length);
            const float shadowDepthBiasClip = 0.004f;

            float weightSum = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                Vector2 s = ShadowSampleKernel[i];
                float r2 = (s.X * s.X) + (s.Y * s.Y);
                weightSum += MathF.Exp(-2.5f * r2);
            }
            weightSum = MathF.Max(1e-5f, weightSum);

            for (int i = 0; i < sampleCount; i++)
            {
                Vector2 s = ShadowSampleKernel[i];
                float r2 = (s.X * s.X) + (s.Y * s.Y);
                float weight = MathF.Exp(-2.5f * r2) / weightSum;
                float jitterX = s.X * config.SoftRadiusXClip;
                float jitterY = s.Y * config.SoftRadiusYClip;

                GpuUniforms shadowUniforms = baseUniforms;
                shadowUniforms.ShadowParams = new Vector4(
                    1f,
                    config.OffsetXClip + jitterX,
                    config.OffsetYClip + jitterY,
                    config.Scale);
                float darkness = Math.Clamp(1f - config.Gray, 0f, 1f);
                shadowUniforms.ShadowColorAndOpacity = new Vector4(
                    shadowDepthBiasClip,
                    0f,
                    0f,
                    config.Alpha * darkness * weight);

                UploadUniforms(encoderPtr, shadowUniforms);
                ObjC.Void_objc_msgSend_UInt_UInt_UInt_IntPtr_UInt(
                    encoderPtr,
                    Selectors.DrawIndexedPrimitivesIndexCountIndexTypeIndexBufferIndexBufferOffset,
                    3, // MTLPrimitiveTypeTriangle
                    (nuint)mesh.IndexCount,
                    (nuint)mesh.IndexType,
                    mesh.IndexBuffer.Handle,
                    0);
            }
        }

        private IReadOnlyList<ShadowPassConfig> ResolveShadowPassConfigs(
            KnobProject? project,
            Vector3 cameraRight,
            Vector3 cameraUp,
            Vector3 cameraForward,
            float viewportWidthPx,
            float viewportHeightPx)
        {
            _resolvedShadowPasses.Clear();
            _shadowLightContributions.Clear();
            if (project == null || !project.ShadowsEnabled)
            {
                return _resolvedShadowPasses;
            }

            // Keep Selected shadow mode stable even if UI selection briefly leaves the light list.
            project.EnsureSelection();

            switch (project.ShadowMode)
            {
                case ShadowLightMode.Selected:
                {
                    KnobLight? selected = project.SelectedLight;
                    if (selected != null &&
                        TryEvaluateShadowLight(project, selected, cameraRight, cameraUp, cameraForward, out Vector2 shadowVec, out float weight, out float planar))
                    {
                        _shadowLightContributions.Add(new ShadowLightContribution(shadowVec, weight, planar));
                    }

                    break;
                }

                case ShadowLightMode.Dominant:
                {
                    float bestWeight = 0f;
                    Vector2 bestVec = default;
                    float bestPlanar = 0f;
                    for (int i = 0; i < project.Lights.Count; i++)
                    {
                        if (!TryEvaluateShadowLight(project, project.Lights[i], cameraRight, cameraUp, cameraForward, out Vector2 shadowVec, out float weight, out float planar))
                        {
                            continue;
                        }

                        if (weight <= bestWeight)
                        {
                            continue;
                        }

                        bestWeight = weight;
                        bestVec = shadowVec;
                        bestPlanar = planar;
                    }

                    if (bestWeight > 1e-6f && bestVec.LengthSquared() > 1e-8f)
                    {
                        _shadowLightContributions.Add(new ShadowLightContribution(bestVec, bestWeight, bestPlanar));
                    }

                    break;
                }

                default:
                {
                    for (int i = 0; i < project.Lights.Count; i++)
                    {
                        if (!TryEvaluateShadowLight(project, project.Lights[i], cameraRight, cameraUp, cameraForward, out Vector2 shadowVec, out float weight, out float planar))
                        {
                            continue;
                        }

                        _shadowLightContributions.Add(new ShadowLightContribution(shadowVec, weight, planar));
                    }

                    break;
                }
            }

            if (_shadowLightContributions.Count == 0)
            {
                return _resolvedShadowPasses;
            }

            bool allowMultipleLights = project.ShadowMode == ShadowLightMode.Weighted && _shadowLightContributions.Count > 1;
            BuildShadowPassConfigs(project, viewportWidthPx, viewportHeightPx, allowMultipleLights);
            return _resolvedShadowPasses;
        }

        private void BuildShadowPassConfigs(
            KnobProject project,
            float viewportWidthPx,
            float viewportHeightPx,
            bool allowMultipleLights)
        {
            _shadowLightContributions.Sort((a, b) => b.Weight.CompareTo(a.Weight));

            int passCount;
            if (allowMultipleLights)
            {
                int desiredPassCount = 1 + (int)MathF.Round(Math.Clamp(project.ShadowQuality, 0f, 1f) * (MaxShadowPassLights - 1));
                desiredPassCount = Math.Clamp(desiredPassCount, 1, MaxShadowPassLights);
                if (_shadowLightContributions.Count >= 2)
                {
                    desiredPassCount = Math.Max(2, desiredPassCount);
                }

                passCount = Math.Min(desiredPassCount, _shadowLightContributions.Count);
            }
            else
            {
                passCount = 1;
            }

            float totalWeight = 0f;
            for (int i = 0; i < passCount; i++)
            {
                totalWeight += _shadowLightContributions[i].Weight;
            }

            totalWeight = MathF.Max(1e-6f, totalWeight);
            float baseSize = MathF.Max(1f, MathF.Min(viewportWidthPx, viewportHeightPx));
            float clipScaleX = 2f / MathF.Max(1f, viewportWidthPx);
            float clipScaleY = 2f / MathF.Max(1f, viewportHeightPx);
            float distanceUser = MathF.Max(0f, project.ShadowDistance);
            float softness = Math.Clamp(project.ShadowSoftness, 0f, 1f);
            float gray = project.ShadowGray;
            float quality = Math.Clamp(project.ShadowQuality, 0f, 1f);
            int sampleBudget = 1 + (int)MathF.Round(quality * 15f);
            int samplesPerPass = allowMultipleLights
                ? Math.Max(1, (int)MathF.Ceiling(sampleBudget / (float)passCount))
                : sampleBudget;

            float totalPowerNorm = Math.Clamp(totalWeight / 3f, 0.15f, 1.35f);
            float alphaBudget = Math.Clamp((0.08f + (0.26f * totalPowerNorm)) * project.ShadowStrength, 0f, 0.85f);

            for (int i = 0; i < passCount; i++)
            {
                ShadowLightContribution contribution = _shadowLightContributions[i];
                if (contribution.ShadowVec.LengthSquared() <= 1e-8f || contribution.Weight <= 1e-6f)
                {
                    continue;
                }

                Vector2 screenDirection = Vector2.Normalize(contribution.ShadowVec);
                float planar = contribution.Planar;
                float powerNorm = Math.Clamp(contribution.Weight / 3f, 0.15f, 1.35f);
                float weightRatio = Math.Clamp(contribution.Weight / totalWeight, 0f, 1f);
                float spread = allowMultipleLights ? 1f - weightRatio : 0f;
                float offsetMagPx = baseSize * (0.010f + (0.032f * planar)) * powerNorm * distanceUser;
                float scale = project.ShadowScale * (1.0f + (0.035f * planar));
                float softRadiusPx = baseSize * (0.003f + (0.026f * softness * (0.45f + (0.55f * spread))));
                float alpha = allowMultipleLights ? alphaBudget * weightRatio : alphaBudget;
                if (alpha <= 1e-5f)
                {
                    continue;
                }

                float offsetXClip = screenDirection.X * offsetMagPx * clipScaleX;
                float offsetYClip = -screenDirection.Y * offsetMagPx * clipScaleY;
                float softRadiusXClip = softRadiusPx * clipScaleX;
                float softRadiusYClip = softRadiusPx * clipScaleY;

                _resolvedShadowPasses.Add(new ShadowPassConfig(
                    true,
                    offsetXClip,
                    offsetYClip,
                    MathF.Max(0.5f, scale),
                    alpha,
                    gray,
                    softRadiusXClip,
                    softRadiusYClip,
                    samplesPerPass));
            }
        }

        private bool TryEvaluateShadowLight(
            KnobProject project,
            KnobLight light,
            Vector3 cameraRight,
            Vector3 cameraUp,
            Vector3 cameraForward,
            out Vector2 shadowVec,
            out float weight,
            out float planar)
        {
            shadowVec = default;
            weight = 0f;
            planar = 0f;

            float intensity = MathF.Max(0f, light.Intensity);
            if (intensity <= 1e-5f)
            {
                return false;
            }

            Vector3 dir;
            if (light.Type == LightType.Directional)
            {
                dir = ApplyLightOrientation(GetDirectionalVector(light));
                if (dir.LengthSquared() <= 1e-8f)
                {
                    return false;
                }

                dir = Vector3.Normalize(dir);
            }
            else
            {
                Vector3 lightPos = ApplyLightOrientation(new Vector3(light.X, light.Y, light.Z));
                if (lightPos.LengthSquared() <= 1e-8f)
                {
                    return false;
                }

                dir = Vector3.Normalize(lightPos);
            }

            float distNorm = light.Type == LightType.Point
                ? MathF.Max(0.2f, new Vector3(light.X, light.Y, light.Z).Length() / MathF.Max(1f, (_meshResources?.ReferenceRadius ?? 220f) * 2f))
                : 1f;
            float attenuation = light.Type == LightType.Point
                ? 1f / (1f + (MathF.Max(0f, light.Falloff) * distNorm * distNorm))
                : 1f;

            float luminance = ((0.2126f * light.Color.Red) + (0.7152f * light.Color.Green) + (0.0722f * light.Color.Blue)) / 255f;
            float diffuse = MathF.Max(0f, light.DiffuseBoost);
            float diffuseTerm = 0.35f + (0.65f * MathF.Pow(diffuse, MathF.Max(0f, project.ShadowDiffuseInfluence)));
            weight = intensity * attenuation * diffuseTerm * (0.35f + (0.65f * luminance));
            if (weight <= 1e-6f)
            {
                return false;
            }

            float sx = Vector3.Dot(dir, cameraRight);
            float sy = -Vector3.Dot(dir, cameraUp);
            Vector2 projected = new(sx, sy);
            float projectedLen = projected.Length();
            if (projectedLen <= 1e-6f)
            {
                return false;
            }

            float parallaxScale = light.Type == LightType.Point
                ? Math.Clamp(1.15f / MathF.Max(0.35f, distNorm), 0.45f, 1.75f)
                : 1f;
            shadowVec = -projected * parallaxScale;
            float viewIncidence = MathF.Abs(Vector3.Dot(dir, cameraForward));
            planar = MathF.Sqrt(MathF.Max(0f, 1f - (viewIncidence * viewIncidence)));
            return true;
        }

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

            if (_paintColorTexture != IntPtr.Zero && _paintColorTextureNeedsClear)
            {
                ClearTextureToTransparent(commandBuffer, _paintColorTexture);
                GenerateTextureMipmaps(commandBuffer, _paintColorTexture);
                _paintColorTextureNeedsClear = false;
            }

            if (_pendingPaintStampCommands.Count == 0)
            {
                return;
            }

            if (!EnsurePaintStampResources())
            {
                _pendingPaintStampCommands.Clear();
                return;
            }

            bool stampedMask = ApplyPendingPaintStampsToTexture(
                commandBuffer,
                _paintMaskTexture,
                includeColorChannel: false);
            bool stampedColor = _paintColorTexture != IntPtr.Zero && ApplyPendingPaintStampsToTexture(
                commandBuffer,
                _paintColorTexture,
                includeColorChannel: true);

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

        private bool ApplyPendingPaintStampsToTexture(
            IntPtr commandBuffer,
            IntPtr targetTexture,
            bool includeColorChannel)
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
            for (int i = 0; i < _pendingPaintStampCommands.Count; i++)
            {
                PaintStampCommand command = _pendingPaintStampCommands[i];
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

        private static void UploadPaintStampUniform(IntPtr encoderPtr, in PaintStampUniform uniform)
        {
            int uniformSize = Marshal.SizeOf<PaintStampUniform>();
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

        public void HandlePointerPressedFromOverlay(PointerPressedEventArgs e, InputElement overlay)
        {
            PointerPoint point = e.GetCurrentPoint(overlay);
            Point overlayPos = point.Position;
            Point pos = TranslateOverlayPointToViewport(overlay, overlayPos);
            OnForwardedPointerPressed(e, point, pos, overlay);
        }

        public void HandlePointerMovedFromOverlay(PointerEventArgs e, InputElement overlay)
        {
            Point overlayPos = e.GetPosition(overlay);
            Point pos = TranslateOverlayPointToViewport(overlay, overlayPos);
            OnForwardedPointerMoved(e, pos);
        }

        public void HandlePointerReleasedFromOverlay(PointerReleasedEventArgs e, InputElement overlay)
        {
            OnForwardedPointerReleased(e, overlay);
        }

        public void HandlePointerWheelFromOverlay(PointerWheelEventArgs e, InputElement overlay)
        {
            OnForwardedPointerWheel(e);
        }

        private Point TranslateOverlayPointToViewport(InputElement overlay, Point overlayPoint)
        {
            if (overlay is Visual overlayVisual &&
                this is Visual viewportVisual)
            {
                Point? translated = overlayVisual.TranslatePoint(overlayPoint, viewportVisual);
                if (translated.HasValue)
                {
                    return translated.Value;
                }
            }

            return overlayPoint;
        }

        public void HandleKeyDownFromOverlay(KeyEventArgs e)
        {
            _lastKnownModifiers = e.KeyModifiers;
            _optionDepthRampActive = _isPainting &&
                _project?.BrushChannel == PaintChannel.Scratch &&
                _lastKnownModifiers.HasFlag(KeyModifiers.Alt);
            if (e.Key == Key.R)
            {
                ResetCamera();
                e.Handled = true;
            }

            PublishPaintHudSnapshot();
        }

        public void HandleKeyUpFromOverlay(KeyEventArgs e)
        {
            _lastKnownModifiers = e.KeyModifiers;
            _optionDepthRampActive = _isPainting &&
                _project?.BrushChannel == PaintChannel.Scratch &&
                _lastKnownModifiers.HasFlag(KeyModifiers.Alt);
            PublishPaintHudSnapshot();
        }

        private void OnForwardedPointerPressed(PointerPressedEventArgs e, PointerPoint point, Point pos, InputElement overlay)
        {
            Focus();
            _lastPointer = pos;
            bool commandDown = IsCommandDown(e.KeyModifiers);

            if (point.Properties.IsLeftButtonPressed && !commandDown && _project?.BrushPaintingEnabled == true)
            {
                _isPainting = true;
                _lastPaintPointer = pos;
                _scratchVirtualPointer = pos;
                _scratchVirtualPointerInitialized = true;
                _scratchCurrentDepth = Math.Clamp(_project.ScratchDepth, 0f, 1f);
                _lastPaintHitMode = PaintHitMode.Idle;
                _optionDepthRampActive = _project.BrushChannel == PaintChannel.Scratch &&
                    (e.KeyModifiers.HasFlag(KeyModifiers.Alt) || _lastKnownModifiers.HasFlag(KeyModifiers.Alt));
                _paintStrokeSeed++;
                e.Pointer.Capture(overlay);
                StampBrushAtPointer(pos);
                InvalidateGpu();
                PublishPaintHudSnapshot();
                return;
            }

            if (point.Properties.IsLeftButtonPressed && commandDown)
            {
                _isOrbiting = true;
                e.Pointer.Capture(overlay);
                InvalidateGpu();
                PublishPaintHudSnapshot();
                return;
            }

            if (point.Properties.IsMiddleButtonPressed)
            {
                _isPanning = true;
                e.Pointer.Capture(overlay);
                InvalidateGpu();
                PublishPaintHudSnapshot();
                return;
            }

            if (point.Properties.IsRightButtonPressed)
            {
                ShowOrientationContextMenu(point.Position);
                return;
            }
        }

        private void OnForwardedPointerMoved(PointerEventArgs e, Point pos)
        {
            _lastKnownModifiers = e.KeyModifiers;
            Avalonia.Vector delta = pos - _lastPointer;
            _lastPointer = pos;

            if (_isPainting)
            {
                if (_project?.BrushChannel == PaintChannel.Scratch)
                {
                    if (!_scratchVirtualPointerInitialized)
                    {
                        _scratchVirtualPointer = _lastPaintPointer;
                        _scratchVirtualPointerInitialized = true;
                    }

                    float resistance = Math.Clamp(_project.ScratchDragResistance, 0f, 0.98f);
                    float follow = Math.Clamp(1f - resistance, 0.02f, 1f);
                    Point filtered = new(
                        _scratchVirtualPointer.X + ((pos.X - _scratchVirtualPointer.X) * follow),
                        _scratchVirtualPointer.Y + ((pos.Y - _scratchVirtualPointer.Y) * follow));

                    float dx = (float)(filtered.X - _scratchVirtualPointer.X);
                    float dy = (float)(filtered.Y - _scratchVirtualPointer.Y);
                    float deltaDip = MathF.Sqrt((dx * dx) + (dy * dy));
                    float deltaPx = deltaDip * GetRenderScale();
                    bool optionDown = e.KeyModifiers.HasFlag(KeyModifiers.Alt) || _lastKnownModifiers.HasFlag(KeyModifiers.Alt);
                    _optionDepthRampActive = optionDown;
                    float baseDepth = Math.Clamp(_project.ScratchDepth, 0f, 1f);
                    if (optionDown)
                    {
                        _scratchCurrentDepth += deltaPx * _project.ScratchDepthRamp;
                    }
                    else
                    {
                        _scratchCurrentDepth -= deltaPx * (_project.ScratchDepthRamp * 0.35f);
                    }

                    _scratchCurrentDepth = Math.Clamp(_scratchCurrentDepth, baseDepth, 1f);
                    StampBrushStroke(_scratchVirtualPointer, filtered);
                    _scratchVirtualPointer = filtered;
                    _lastPaintPointer = filtered;
                }
                else
                {
                    _optionDepthRampActive = false;
                    StampBrushStroke(_lastPaintPointer, pos);
                    _lastPaintPointer = pos;
                }

                InvalidateGpu();
                PublishPaintHudSnapshot();
            }
            else if (_isOrbiting)
            {
                _orbitYawDeg -= (float)delta.X * 0.4f;
                _orbitPitchDeg += (float)delta.Y * 0.4f;
                _orbitPitchDeg = Math.Clamp(_orbitPitchDeg, -89f, 89f);
                InvalidateGpu();
                PublishPaintHudSnapshot();
            }
            else if (_isPanning)
            {
                _panPx += new Vector2((float)delta.X, (float)delta.Y);
                InvalidateGpu();
                PublishPaintHudSnapshot();
            }
        }

        private void OnForwardedPointerReleased(PointerReleasedEventArgs e, InputElement overlay)
        {
            _isOrbiting = false;
            _isPanning = false;
            _isPainting = false;
            _optionDepthRampActive = false;
            _scratchVirtualPointerInitialized = false;
            if (e.Pointer.Captured == overlay)
            {
                e.Pointer.Capture(null);
            }

            PublishPaintHudSnapshot();
        }

        private void OnForwardedPointerWheel(PointerWheelEventArgs e)
        {
            _zoom *= (float)Math.Pow(1.1, e.Delta.Y);
            _zoom = Math.Clamp(_zoom, 0.2f, 8f);
            InvalidateGpu();
            PublishPaintHudSnapshot();
        }

        private void StampBrushStroke(Point startDip, Point endDip)
        {
            if (_project is null || !_project.BrushPaintingEnabled)
            {
                return;
            }

            SKPoint startPx = DipToScreen(startDip);
            SKPoint endPx = DipToScreen(endDip);
            float dx = endPx.X - startPx.X;
            float dy = endPx.Y - startPx.Y;
            float distancePx = MathF.Sqrt((dx * dx) + (dy * dy));
            bool scratchChannel = _project.BrushChannel == PaintChannel.Scratch;
            float activeSizePx = scratchChannel
                ? _project.ScratchWidthPx
                : _project.BrushSizePx;
            float spacingPx;
            float minSpacingPx = scratchChannel ? 2.5f : 1.5f;
            if (scratchChannel)
            {
                spacingPx = MathF.Max(minSpacingPx, activeSizePx * GetScratchSpacingRatio(_project.ScratchAbrasionType));
            }
            else
            {
                spacingPx = _project.BrushType == PaintBrushType.Stroke
                    ? MathF.Max(minSpacingPx, activeSizePx * 0.18f)
                    : MathF.Max(minSpacingPx, activeSizePx * 0.45f);
            }
            int rawSteps = Math.Max(1, (int)MathF.Ceiling(distancePx / MathF.Max(1e-4f, spacingPx)));
            int steps = Math.Clamp(rawSteps, 1, 96);
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                Point p = new(
                    startDip.X + ((endDip.X - startDip.X) * t),
                    startDip.Y + ((endDip.Y - startDip.Y) * t));
                StampBrushAtPointer(p);
            }
        }

        private void StampBrushAtPointer(Point pointerDip)
        {
            if (_project is null || !_project.BrushPaintingEnabled)
            {
                return;
            }

            if (!TryMapPointerToPaintUv(pointerDip, out Vector2 uv, out float referenceRadius))
            {
                _lastPaintHitMode = PaintHitMode.Idle;
                return;
            }

            bool scratchChannel = _project.BrushChannel == PaintChannel.Scratch;
            float activeSizePx = scratchChannel ? _project.ScratchWidthPx : _project.BrushSizePx;
            float brushRadiusWorld = activeSizePx / MathF.Max(_zoom, 1e-4f);
            float uvRadius = brushRadiusWorld / MathF.Max(2f * referenceRadius, 1e-4f);
            float activeOpacity = _project.BrushOpacity;
            if (scratchChannel)
            {
                float depthFactor = Math.Clamp(_scratchCurrentDepth, 0f, 1f);
                float scratchOpacityBase = MathF.Max(activeOpacity, 0.62f);
                activeOpacity = Math.Clamp(scratchOpacityBase * (0.55f + (0.45f * depthFactor)), 0f, 1f);
            }

            QueuePaintStampCommand(
                uv,
                uvRadius,
                activeOpacity,
                _project.BrushSpread,
                _project.PaintColor,
                _paintStrokeSeed++);
        }

        private void QueuePaintStampCommand(
            Vector2 uvCenter,
            float uvRadius,
            float opacity,
            float spread,
            Vector3 paintColor,
            uint seed)
        {
            if (_project is null)
            {
                return;
            }

            if (_pendingPaintStampCommands.Count >= MaxPendingPaintStamps)
            {
                int dropCount = Math.Max(1, _pendingPaintStampCommands.Count - MaxPendingPaintStamps + 1);
                _pendingPaintStampCommands.RemoveRange(0, dropCount);
            }

            _pendingPaintStampCommands.Add(new PaintStampCommand(
                UvCenter: uvCenter,
                UvRadius: MathF.Max(1e-6f, uvRadius),
                Opacity: Math.Clamp(opacity, 0f, 1f),
                Spread: Math.Clamp(spread, 0f, 1f),
                Channel: _project.BrushChannel,
                BrushType: _project.BrushType,
                ScratchAbrasionType: _project.ScratchAbrasionType,
                PaintColor: new Vector3(
                    Math.Clamp(paintColor.X, 0f, 1f),
                    Math.Clamp(paintColor.Y, 0f, 1f),
                    Math.Clamp(paintColor.Z, 0f, 1f)),
                Seed: seed));
        }

        private static float GetScratchSpacingRatio(ScratchAbrasionType abrasionType)
        {
            return abrasionType switch
            {
                ScratchAbrasionType.Needle => 0.15f,
                ScratchAbrasionType.Chisel => 0.22f,
                ScratchAbrasionType.Burr => 0.34f,
                ScratchAbrasionType.Scuff => 0.40f,
                _ => 0.22f
            };
        }

        private bool TryMapPointerToPaintUv(Point pointerDip, out Vector2 uv, out float referenceRadius)
        {
            uv = default;
            referenceRadius = 1f;
            if (_project is null)
            {
                _lastPaintHitMode = PaintHitMode.Idle;
                return false;
            }

            ModelNode? modelNode = _project.SceneRoot.Children.OfType<ModelNode>().FirstOrDefault();
            if (modelNode is null)
            {
                _lastPaintHitMode = PaintHitMode.Idle;
                return false;
            }

            RefreshMeshResources(_project, modelNode);
            if (_meshResources is null)
            {
                _lastPaintHitMode = PaintHitMode.Idle;
                return false;
            }

            CollarNode? collarNode = modelNode.Children.OfType<CollarNode>().FirstOrDefault();
            bool drawCollar =
                collarNode is { Enabled: true } &&
                _collarResources is not null &&
                _collarResources.VertexBuffer.Handle != IntPtr.Zero &&
                _collarResources.IndexBuffer.Handle != IntPtr.Zero;
            referenceRadius = drawCollar
                ? MathF.Max(_meshResources.ReferenceRadius, _collarResources!.ReferenceRadius)
                : _meshResources.ReferenceRadius;
            referenceRadius = MathF.Max(1f, referenceRadius);

            SKPoint screenPoint = DipToScreen(pointerDip);
            if (!TryBuildPointerRay(screenPoint, referenceRadius, out Vector3 rayOrigin, out Vector3 rayDirection))
            {
                _lastPaintHitMode = PaintHitMode.Idle;
                return false;
            }

            float modelRotation = modelNode.RotationRadians;
            bool hit = false;
            float bestT = float.MaxValue;
            Vector3 bestLocalHit = default;

            if (TryIntersectMeshWithModelRotation(
                    _meshResources,
                    rayOrigin,
                    rayDirection,
                    modelRotation,
                    out Vector3 knobLocalHit,
                    out float knobHitT) &&
                knobHitT < bestT)
            {
                hit = true;
                bestT = knobHitT;
                bestLocalHit = knobLocalHit;
            }

            if (drawCollar && _collarResources is not null)
            {
                float collarRotation = modelRotation;
                if (_invertImportedCollarOrbit && IsImportedCollarPreset(collarNode))
                {
                    collarRotation = -collarRotation;
                }

                if (TryIntersectMeshWithModelRotation(
                        _collarResources,
                        rayOrigin,
                        rayDirection,
                        collarRotation,
                        out Vector3 collarLocalHit,
                        out float collarHitT) &&
                    collarHitT < bestT)
                {
                    hit = true;
                    bestT = collarHitT;
                    bestLocalHit = collarLocalHit;
                }
            }

            if (!hit)
            {
                // Fallback: preserve paint continuity if no triangle hit is found
                // (e.g., sparse/stretched imported meshes or tiny grazing coverage).
                if (!TryScreenToScene(screenPoint, out SKPoint scenePoint))
                {
                    _lastPaintHitMode = PaintHitMode.Idle;
                    return false;
                }

                float cosA = MathF.Cos(modelRotation);
                float sinA = MathF.Sin(modelRotation);
                float localX = (scenePoint.X * cosA) + (scenePoint.Y * sinA);
                float localY = (-scenePoint.X * sinA) + (scenePoint.Y * cosA);
                uv = new Vector2(
                    (localX / (2f * referenceRadius)) + 0.5f,
                    (localY / (2f * referenceRadius)) + 0.5f);
                _lastPaintHitMode = PaintHitMode.Fallback;
                return true;
            }

            uv = new Vector2(
                (bestLocalHit.X / (2f * referenceRadius)) + 0.5f,
                (bestLocalHit.Y / (2f * referenceRadius)) + 0.5f);
            _lastPaintHitMode = PaintHitMode.MeshHit;
            return true;
        }

        private bool TryBuildPointerRay(SKPoint screenPoint, float referenceRadius, out Vector3 rayOrigin, out Vector3 rayDirection)
        {
            rayOrigin = default;
            rayDirection = default;
            if (_zoom <= 1e-6f)
            {
                return false;
            }

            GetCameraBasis(out Vector3 right, out Vector3 up, out Vector3 forward);
            GetScreenCenterPx(out float centerX, out float centerY);
            float viewX = (screenPoint.X - centerX) / _zoom;
            // Paint controls requested with screen-space Y increasing downward (top/bottom non-inverted).
            float viewY = (screenPoint.Y - centerY) / _zoom;
            float radius = MathF.Max(1f, referenceRadius);
            Vector3 cameraPos = -forward * (radius * 6f);
            rayOrigin = cameraPos + (right * viewX) + (up * viewY);
            rayDirection = forward;
            return true;
        }

        private static bool TryIntersectMeshWithModelRotation(
            MetalMeshGpuResources mesh,
            Vector3 rayOriginWorld,
            Vector3 rayDirectionWorld,
            float modelRotationRadians,
            out Vector3 localHitPoint,
            out float hitT)
        {
            localHitPoint = default;
            hitT = float.MaxValue;

            float cosA = MathF.Cos(modelRotationRadians);
            float sinA = MathF.Sin(modelRotationRadians);
            Vector3 rayOriginLocal = RotateToLocalXY(rayOriginWorld, cosA, sinA);
            Vector3 rayDirectionLocal = RotateToLocalXY(rayDirectionWorld, cosA, sinA);

            if (!TryIntersectRayAabb(rayOriginLocal, rayDirectionLocal, mesh.BoundsMin, mesh.BoundsMax))
            {
                return false;
            }

            bool hit = false;
            float bestT = float.MaxValue;
            Vector3 bestPoint = default;
            Vector3[] positions = mesh.Positions;
            uint[] indices = mesh.Indices;
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                int i0 = (int)indices[i];
                int i1 = (int)indices[i + 1];
                int i2 = (int)indices[i + 2];
                if ((uint)i0 >= positions.Length || (uint)i1 >= positions.Length || (uint)i2 >= positions.Length)
                {
                    continue;
                }

                Vector3 p0 = positions[i0];
                Vector3 p1 = positions[i1];
                Vector3 p2 = positions[i2];
                if (!TryIntersectRayTriangle(rayOriginLocal, rayDirectionLocal, p0, p1, p2, out float t))
                {
                    continue;
                }

                if (t <= 1e-5f || t >= bestT)
                {
                    continue;
                }

                hit = true;
                bestT = t;
                bestPoint = rayOriginLocal + (rayDirectionLocal * t);
            }

            if (!hit)
            {
                return false;
            }

            localHitPoint = bestPoint;
            hitT = bestT;
            return true;
        }

        private static Vector3 RotateToLocalXY(Vector3 worldValue, float cosA, float sinA)
        {
            return new Vector3(
                (worldValue.X * cosA) + (worldValue.Y * sinA),
                (-worldValue.X * sinA) + (worldValue.Y * cosA),
                worldValue.Z);
        }

        private static bool TryIntersectRayAabb(Vector3 rayOrigin, Vector3 rayDirection, Vector3 boundsMin, Vector3 boundsMax)
        {
            const float epsilon = 1e-8f;
            float tMin = 0f;
            float tMax = float.MaxValue;

            if (!TryIntersectRayAabbAxis(rayOrigin.X, rayDirection.X, boundsMin.X, boundsMax.X, ref tMin, ref tMax, epsilon) ||
                !TryIntersectRayAabbAxis(rayOrigin.Y, rayDirection.Y, boundsMin.Y, boundsMax.Y, ref tMin, ref tMax, epsilon) ||
                !TryIntersectRayAabbAxis(rayOrigin.Z, rayDirection.Z, boundsMin.Z, boundsMax.Z, ref tMin, ref tMax, epsilon))
            {
                return false;
            }

            return tMax >= tMin && tMax >= 0f;
        }

        private static bool TryIntersectRayAabbAxis(
            float origin,
            float direction,
            float minBound,
            float maxBound,
            ref float tMin,
            ref float tMax,
            float epsilon)
        {
            if (MathF.Abs(direction) <= epsilon)
            {
                return origin >= minBound && origin <= maxBound;
            }

            float inv = 1f / direction;
            float t1 = (minBound - origin) * inv;
            float t2 = (maxBound - origin) * inv;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);
            return tMax >= tMin;
        }

        private static bool TryIntersectRayTriangle(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            Vector3 p0,
            Vector3 p1,
            Vector3 p2,
            out float t)
        {
            t = 0f;
            const float epsilon = 1e-7f;
            Vector3 edge1 = p1 - p0;
            Vector3 edge2 = p2 - p0;
            Vector3 pvec = Vector3.Cross(rayDirection, edge2);
            float det = Vector3.Dot(edge1, pvec);
            if (MathF.Abs(det) < epsilon)
            {
                return false;
            }

            float invDet = 1f / det;
            Vector3 tvec = rayOrigin - p0;
            float u = Vector3.Dot(tvec, pvec) * invDet;
            if (u < 0f || u > 1f)
            {
                return false;
            }

            Vector3 qvec = Vector3.Cross(tvec, edge1);
            float v = Vector3.Dot(rayDirection, qvec) * invDet;
            if (v < 0f || (u + v) > 1f)
            {
                return false;
            }

            t = Vector3.Dot(edge2, qvec) * invDet;
            return t > epsilon;
        }

        private bool IsCommandDown(KeyModifiers eventModifiers)
        {
            if (eventModifiers.HasFlag(KeyModifiers.Meta))
            {
                return true;
            }

            if (_lastKnownModifiers.HasFlag(KeyModifiers.Meta))
            {
                return true;
            }

            TopLevel? topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
            {
                return false;
            }

            PropertyInfo? keyModifiersProperty = topLevel.GetType().GetProperty(
                "KeyModifiers",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (keyModifiersProperty?.GetValue(topLevel) is KeyModifiers topModifiers &&
                topModifiers.HasFlag(KeyModifiers.Meta))
            {
                return true;
            }

            object? platformImpl = topLevel.PlatformImpl;
            if (platformImpl == null)
            {
                return false;
            }

            PropertyInfo? keyboardDeviceProperty = platformImpl.GetType().GetProperty(
                "KeyboardDevice",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object? keyboardDevice = keyboardDeviceProperty?.GetValue(platformImpl);
            if (keyboardDevice == null)
            {
                return false;
            }

            PropertyInfo? modifiersProperty = keyboardDevice.GetType().GetProperty(
                "Modifiers",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (modifiersProperty?.GetValue(keyboardDevice) is KeyModifiers keyboardModifiers &&
                keyboardModifiers.HasFlag(KeyModifiers.Meta))
            {
                return true;
            }

            return false;
        }

        private bool TryScreenToScene(SKPoint screenPoint, out SKPoint scenePoint)
        {
            GetScreenCenterPx(out float centerX, out float centerY);
            GetCameraBasis(out Vector3 right, out Vector3 up, out _);

            float m00 = _zoom * right.X;
            float m01 = _zoom * right.Y;
            float m10 = _zoom * up.X;
            float m11 = _zoom * up.Y;
            float det = m00 * m11 - m01 * m10;
            if (MathF.Abs(det) < 1e-5f)
            {
                scenePoint = SKPoint.Empty;
                return false;
            }

            float dx = screenPoint.X - centerX;
            float dy = screenPoint.Y - centerY;
            float sx = (dx * m11 - m01 * dy) / det;
            float sy = (m00 * dy - dx * m10) / det;
            scenePoint = new SKPoint(sx, sy);
            return true;
        }

        private void GetCameraBasis(out Vector3 right, out Vector3 up, out Vector3 forward)
        {
            float yaw = DegreesToRadians(_orbitYawDeg);
            float pitch = DegreesToRadians(_orbitPitchDeg);
            forward = Vector3.Normalize(new Vector3(
                MathF.Sin(yaw) * MathF.Cos(pitch),
                MathF.Sin(pitch),
                -MathF.Cos(yaw) * MathF.Cos(pitch)));

            Vector3 worldUp = Vector3.UnitY;
            right = Vector3.Cross(worldUp, forward);
            if (right.LengthSquared() < 1e-6f)
            {
                right = Vector3.UnitX;
            }
            else
            {
                right = Vector3.Normalize(right);
            }

            up = Vector3.Normalize(Vector3.Cross(forward, right));

            if (_orientation.InvertX)
            {
                right *= -1f;
            }

            if (_orientation.InvertY)
            {
                up *= -1f;
            }

            if (_orientation.InvertZ)
            {
                forward *= -1f;
            }

            if (_orientation.FlipCamera180)
            {
                forward = -forward;
                right = -right;
            }
        }

        private bool ResolveFrontFacingClockwise(Vector3 right, Vector3 up, Vector3 forward)
        {
            // In this renderer we project manually using camera right/up vectors.
            // If the basis is mirrored, front-face winding must be flipped to keep outward culling stable.
            float handedness = Vector3.Dot(Vector3.Cross(right, up), forward);
            return handedness < 0f;
        }

        private static SKPoint ProjectSceneOffset(SKPoint scene, float zoom, Vector3 right, Vector3 up)
        {
            float x = zoom * (scene.X * right.X + scene.Y * right.Y);
            float y = -zoom * (scene.X * up.X + scene.Y * up.Y);
            return new SKPoint(x, y);
        }

        private void ShowOrientationContextMenu(Point pointerDip)
        {
            _debugContextMenu?.Close();

            var invertXItem = new MenuItem
            {
                Header = "Invert X",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _orientation.InvertX
            };
            invertXItem.Click += (_, _) =>
            {
                _orientation.InvertX = !_orientation.InvertX;
                PrintOrientation();
                InvalidateGpu();
            };

            var invertYItem = new MenuItem
            {
                Header = "Invert Y",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _orientation.InvertY
            };
            invertYItem.Click += (_, _) =>
            {
                _orientation.InvertY = !_orientation.InvertY;
                PrintOrientation();
                InvalidateGpu();
            };

            var invertZItem = new MenuItem
            {
                Header = "Invert Z",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _orientation.InvertZ
            };
            invertZItem.Click += (_, _) =>
            {
                _orientation.InvertZ = !_orientation.InvertZ;
                PrintOrientation();
                InvalidateGpu();
            };

            var gizmoInvertXItem = new MenuItem
            {
                Header = "Gizmo Invert X",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _gizmoInvertX
            };
            gizmoInvertXItem.Click += (_, _) =>
            {
                _gizmoInvertX = !_gizmoInvertX;
                PrintOrientation();
                InvalidateGpu();
            };

            var gizmoInvertYItem = new MenuItem
            {
                Header = "Gizmo Invert Y",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _gizmoInvertY
            };
            gizmoInvertYItem.Click += (_, _) =>
            {
                _gizmoInvertY = !_gizmoInvertY;
                PrintOrientation();
                InvalidateGpu();
            };

            var gizmoInvertZItem = new MenuItem
            {
                Header = "Gizmo Invert Z",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _gizmoInvertZ
            };
            gizmoInvertZItem.Click += (_, _) =>
            {
                _gizmoInvertZ = !_gizmoInvertZ;
                PrintOrientation();
                InvalidateGpu();
            };

            var flipCameraItem = new MenuItem
            {
                Header = "Flip Camera 180°",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _orientation.FlipCamera180
            };
            flipCameraItem.Click += (_, _) =>
            {
                _orientation.FlipCamera180 = !_orientation.FlipCamera180;
                PrintOrientation();
                InvalidateGpu();
            };

            var invertCollarOrbitItem = new MenuItem
            {
                Header = "Invert Collar Orbit (Imported Mesh)",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _invertImportedCollarOrbit
            };
            invertCollarOrbitItem.Click += (_, _) =>
            {
                _invertImportedCollarOrbit = !_invertImportedCollarOrbit;
                PrintOrientation();
                InvalidateGpu();
            };

            var invertKnobFrontFaceWindingItem = new MenuItem
            {
                Header = "Invert Front-Face Winding (Knob)",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _invertKnobFrontFaceWinding
            };
            invertKnobFrontFaceWindingItem.Click += (_, _) =>
            {
                _invertKnobFrontFaceWinding = !_invertKnobFrontFaceWinding;
                PrintOrientation();
                InvalidateGpu();
            };

            var invertImportedStlFrontFaceWindingItem = new MenuItem
            {
                Header = "Invert Front-Face Winding (Imported Mesh Collar)",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _invertImportedStlFrontFaceWinding
            };
            invertImportedStlFrontFaceWindingItem.Click += (_, _) =>
            {
                _invertImportedStlFrontFaceWinding = !_invertImportedStlFrontFaceWinding;
                PrintOrientation();
                InvalidateGpu();
            };

            var resetItem = new MenuItem
            {
                Header = "Reset Orientation"
            };
            resetItem.Click += (_, _) =>
            {
                _orientation = new OrientationDebug
                {
                    InvertX = true,
                    InvertY = true,
                    InvertZ = true,
                    FlipCamera180 = true
                };
                _gizmoInvertX = false;
                _gizmoInvertY = true;
                _gizmoInvertZ = false;
                _invertImportedCollarOrbit = false;
                _invertKnobFrontFaceWinding = true;
                _invertImportedStlFrontFaceWinding = true;
                PrintOrientation();
                InvalidateGpu();
            };

            _debugContextMenu = new ContextMenu
            {
                Items =
                {
                    invertXItem,
                    invertYItem,
                    invertZItem,
                    new Separator(),
                    gizmoInvertXItem,
                    gizmoInvertYItem,
                    gizmoInvertZItem,
                    new Separator(),
                    flipCameraItem,
                    invertCollarOrbitItem,
                    invertKnobFrontFaceWindingItem,
                    invertImportedStlFrontFaceWindingItem,
                    new Separator(),
                    resetItem
                },
                Placement = PlacementMode.Pointer,
                PlacementRect = new Rect(pointerDip, new Size(1, 1))
            };

            _debugContextMenu.Open(this);
        }

        private void PrintOrientation()
        {
            Console.WriteLine("---- Orientation Debug ----");
            Console.WriteLine($"InvertX: {_orientation.InvertX}");
            Console.WriteLine($"InvertY: {_orientation.InvertY}");
            Console.WriteLine($"InvertZ: {_orientation.InvertZ}");
            Console.WriteLine($"GizmoInvertX: {_gizmoInvertX}");
            Console.WriteLine($"GizmoInvertY: {_gizmoInvertY}");
            Console.WriteLine($"GizmoInvertZ: {_gizmoInvertZ}");
            Console.WriteLine($"FlipCamera180: {_orientation.FlipCamera180}");
            Console.WriteLine($"InvertImportedCollarOrbit: {_invertImportedCollarOrbit}");
            Console.WriteLine($"InvertKnobFrontFaceWinding: {_invertKnobFrontFaceWinding}");
            Console.WriteLine($"InvertImportedStlFrontFaceWinding: {_invertImportedStlFrontFaceWinding}");
            Console.WriteLine("---------------------------");
        }

        private float GetRenderScale()
        {
            return (float)(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0);
        }

        private float GetViewportWidthPx()
        {
            return (float)(Bounds.Width * GetRenderScale());
        }

        private float GetViewportHeightPx()
        {
            return (float)(Bounds.Height * GetRenderScale());
        }

        private void GetScreenCenterPx(out float centerX, out float centerY)
        {
            centerX = GetViewportWidthPx() * 0.5f + _panPx.X;
            centerY = GetViewportHeightPx() * 0.5f + _panPx.Y;
        }

        private SKPoint DipToScreen(Point dipPoint)
        {
            float scale = GetRenderScale();
            return new SKPoint((float)dipPoint.X * scale, (float)dipPoint.Y * scale);
        }

        private static float DegreesToRadians(float degrees)
        {
            return degrees * (MathF.PI / 180f);
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            if (edge1 <= edge0)
            {
                return x < edge0 ? 0f : 1f;
            }

            float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
            return t * t * (3f - (2f * t));
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

        private static void LogPaintStampError(string message)
        {
            Console.Error.WriteLine($"[MetalViewport] {message}");
        }

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

        private static class ObjCClasses
        {
            public static readonly IntPtr NSString = ObjC.objc_getClass("NSString");
            public static readonly IntPtr NSView = ObjC.objc_getClass("NSView");
            public static readonly IntPtr CAMetalLayer = ObjC.objc_getClass("CAMetalLayer");
            public static readonly IntPtr MTLRenderPassDescriptor = ObjC.objc_getClass("MTLRenderPassDescriptor");
            public static readonly IntPtr MTLTextureDescriptor = ObjC.objc_getClass("MTLTextureDescriptor");
            public static readonly IntPtr MTLRenderPipelineDescriptor = ObjC.objc_getClass("MTLRenderPipelineDescriptor");
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

        private readonly record struct PaintStampCommand(
            Vector2 UvCenter,
            float UvRadius,
            float Opacity,
            float Spread,
            PaintChannel Channel,
            PaintBrushType BrushType,
            ScratchAbrasionType ScratchAbrasionType,
            Vector3 PaintColor,
            uint Seed);

        [StructLayout(LayoutKind.Sequential)]
        private struct GpuLight
        {
            public Vector4 PositionType;
            public Vector4 Direction;
            public Vector4 ColorIntensity;
            public Vector4 Params0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GpuUniforms
        {
            public Vector4 CameraPosAndReferenceRadius;
            public Vector4 RightAndScaleX;
            public Vector4 UpAndScaleY;
            public Vector4 ForwardAndScaleZ;
            public Vector4 ProjectionOffsetsAndLightCount;
            public Vector4 MaterialBaseColorAndMetallic;
            public Vector4 MaterialRoughnessDiffuseSpecMode;
            public Vector4 MaterialPartTopColorAndMetallic;
            public Vector4 MaterialPartBevelColorAndMetallic;
            public Vector4 MaterialPartSideColorAndMetallic;
            public Vector4 MaterialPartRoughnessAndEnable;
            public Vector4 MaterialSurfaceBrushParams;
            public Vector4 WeatherParams;
            public Vector4 ScratchExposeColorAndStrength;
            public Vector4 IndicatorParams0;
            public Vector4 IndicatorParams1;
            public Vector4 IndicatorColorAndBlend;
            public Vector4 MicroDetailParams;
            public Vector4 EnvironmentTopColorAndIntensity;
            public Vector4 EnvironmentBottomColorAndRoughnessMix;
            public Vector4 ModelRotationCosSin;
            public Vector4 ShadowParams;
            public Vector4 ShadowColorAndOpacity;
            public Vector4 DebugBasisParams;
            public GpuLight Light0;
            public GpuLight Light1;
            public GpuLight Light2;
            public GpuLight Light3;
            public GpuLight Light4;
            public GpuLight Light5;
            public GpuLight Light6;
            public GpuLight Light7;
        }

        private readonly record struct MeshShapeKey(
            float Radius,
            float Height,
            float Bevel,
            float TopScale,
            int Segments,
            float CrownProfile,
            float BevelCurve,
            float BodyTaper,
            float BodyBulge,
            float SpiralHeight,
            float SpiralWidth,
            float SpiralHeightVariance,
            float SpiralWidthVariance,
            float SpiralHeightThreshold,
            float SpiralWidthThreshold,
            float SpiralTurns,
            int GripType,
            float GripStart,
            float GripHeight,
            float GripDensity,
            float GripPitch,
            float GripDepth,
            float GripWidth,
            float GripSharpness,
            int IndicatorEnabled,
            int IndicatorShape,
            int IndicatorRelief,
            int IndicatorProfile,
            float IndicatorWidth,
            float IndicatorLength,
            float IndicatorPosition,
            float IndicatorThickness,
            float IndicatorRoundness,
            int IndicatorCadWallsEnabled);

        private readonly record struct CollarShapeKey(
            int Enabled,
            int Preset,
            float ModelRadius,
            float ModelHeight,
            float InnerRadiusRatio,
            float GapToKnobRatio,
            float ElevationRatio,
            float OverallRotationRadians,
            float BiteAngleRadians,
            float BodyRadiusRatio,
            float BodyEllipseYScale,
            float NeckTaper,
            float TailTaper,
            float MassBias,
            float TailUnderlap,
            float HeadScale,
            float JawBulge,
            int UvSeamFollowBite,
            float UvSeamOffset,
            int PathSegments,
            int CrossSegments,
            float ImportedScale,
            float ImportedBodyLengthScale,
            float ImportedBodyThicknessScale,
            float ImportedHeadLengthScale,
            float ImportedHeadThicknessScale,
            float ImportedRotationRadians,
            float ImportedOffsetXRatio,
            float ImportedOffsetYRatio,
            float ImportedInflateRatio,
            string ImportedMeshPath,
            long ImportedFileTicks);

        private readonly record struct SpiralNormalMapKey(
            float ReferenceRadius,
            float TopScale,
            float SpiralHeight,
            float SpiralWidth,
            float SpiralTurns);

        private readonly record struct ShadowPassConfig(
            bool Enabled,
            float OffsetXClip,
            float OffsetYClip,
            float Scale,
            float Alpha,
            float Gray,
            float SoftRadiusXClip,
            float SoftRadiusYClip,
            int SampleCount);

        private readonly record struct ShadowLightContribution(
            Vector2 ShadowVec,
            float Weight,
            float Planar);

        private sealed class MetalMeshGpuResources
        {
            public required IMTLBuffer VertexBuffer { get; init; }
            public required IMTLBuffer IndexBuffer { get; init; }
            public required int IndexCount { get; init; }
            public required MTLIndexType IndexType { get; init; }
            public required float ReferenceRadius { get; init; }
            public required Vector3[] Positions { get; init; }
            public required uint[] Indices { get; init; }
            public required Vector3 BoundsMin { get; init; }
            public required Vector3 BoundsMax { get; init; }
        }

        private enum InteractionMode
        {
            None,
            PanView,
            OrbitView
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
            public static extern void Void_objc_msgSend_MTLRegion_UInt_IntPtr_UInt(
                IntPtr receiver,
                IntPtr selector,
                MTLRegion region,
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
            public static extern void Void_objc_msgSend_UInt_UInt_UInt_IntPtr_UInt(
                IntPtr receiver,
                IntPtr selector,
                nuint arg1,
                nuint arg2,
                nuint arg3,
                IntPtr arg4,
                nuint arg5);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            public static extern void Void_objc_msgSend_UInt_UInt_UInt(
                IntPtr receiver,
                IntPtr selector,
                nuint arg1,
                nuint arg2,
                nuint arg3);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            public static extern void Void_objc_msgSend_CGSize(IntPtr receiver, IntPtr selector, CGSize size);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            public static extern void Void_objc_msgSend_CGRect(IntPtr receiver, IntPtr selector, CGRect rect);

            [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
            public static extern void Void_objc_msgSend_MTLClearColor(IntPtr receiver, IntPtr selector, MTLClearColor color);
        }
    }
}
