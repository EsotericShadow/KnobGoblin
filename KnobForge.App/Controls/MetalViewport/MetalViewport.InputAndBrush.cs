using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using KnobForge.Core;
using KnobForge.Core.Scene;
using KnobForge.Rendering.GPU;
using SkiaSharp;

namespace KnobForge.App.Controls
{
    public sealed partial class MetalViewport
    {
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
            else
            {
                float panStep = GetKeyboardPanStep(e.KeyModifiers);
                Vector2 panDelta = e.Key switch
                {
                    Key.Left => new Vector2(-panStep, 0f),
                    Key.Right => new Vector2(panStep, 0f),
                    Key.Up => new Vector2(0f, -panStep),
                    Key.Down => new Vector2(0f, panStep),
                    _ => Vector2.Zero
                };

                if (panDelta != Vector2.Zero)
                {
                    _panPx += panDelta;
                    InvalidateGpu();
                    e.Handled = true;
                }
            }

            PublishPaintHudSnapshot();
        }

        private static float GetKeyboardPanStep(KeyModifiers modifiers)
        {
            if (modifiers.HasFlag(KeyModifiers.Shift))
            {
                return 64f;
            }

            if (modifiers.HasFlag(KeyModifiers.Alt))
            {
                return 8f;
            }

            return 24f;
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
            if (!overlay.Focus())
            {
                Focus();
            }
            _lastPointer = pos;
            bool commandDown = IsCommandDown(e.KeyModifiers);

            if (point.Properties.IsLeftButtonPressed && !commandDown && _project?.BrushPaintingEnabled == true)
            {
                EnsureDefaultPaintLayer();
                _isPainting = true;
                _lastPaintPointer = pos;
                _activeStrokeLayerIndex = Math.Clamp(_activePaintLayerIndex, 0, Math.Max(0, _paintLayers.Count - 1));
                _activeStrokeCommands.Clear();
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
                    float depthSign = _brushInvertZ ? -1f : 1f;
                    if (optionDown)
                    {
                        _scratchCurrentDepth += deltaPx * _project.ScratchDepthRamp * depthSign;
                    }
                    else
                    {
                        _scratchCurrentDepth -= deltaPx * (_project.ScratchDepthRamp * 0.35f) * depthSign;
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
            bool wasPainting = _isPainting;
            _isOrbiting = false;
            _isPanning = false;
            _isPainting = false;
            _optionDepthRampActive = false;
            _scratchVirtualPointerInitialized = false;
            _activeStrokeLayerIndex = _activePaintLayerIndex;
            if (e.Pointer.Captured == overlay)
            {
                e.Pointer.Capture(null);
            }

            if (wasPainting)
            {
                CommitActivePaintStroke();
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
            long stampStartTimestamp = Stopwatch.GetTimestamp();
            if (_project is null || !_project.BrushPaintingEnabled)
            {
                RecordPaintStampDiagnostics(Stopwatch.GetElapsedTime(stampStartTimestamp).TotalMilliseconds);
                return;
            }

            if (!TryMapPointerToPaintUv(pointerDip, out Vector2 uv, out float referenceRadius))
            {
                _lastPaintHitMode = PaintHitMode.Idle;
                RecordPaintStampDiagnostics(Stopwatch.GetElapsedTime(stampStartTimestamp).TotalMilliseconds);
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
            RecordPaintStampDiagnostics(Stopwatch.GetElapsedTime(stampStartTimestamp).TotalMilliseconds);
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

            EnsureDefaultPaintLayer();
            int targetLayer = Math.Clamp(_isPainting ? _activeStrokeLayerIndex : _activePaintLayerIndex, 0, _paintLayers.Count - 1);
            var command = new PaintStampCommand(
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
                Seed: seed,
                LayerIndex: targetLayer);
            _pendingPaintStampCommands.Add(command);
            if (_isPainting)
            {
                _activeStrokeCommands.Add(command);
            }
        }

        private void CommitActivePaintStroke()
        {
            if (_activeStrokeCommands.Count == 0)
            {
                return;
            }

            if (_paintHistoryRevision < _committedPaintStrokes.Count)
            {
                _committedPaintStrokes.RemoveRange(_paintHistoryRevision, _committedPaintStrokes.Count - _paintHistoryRevision);
            }

            int layerIndex = Math.Clamp(_activeStrokeLayerIndex, 0, Math.Max(0, _paintLayers.Count - 1));
            PaintStampCommand[] commands = _activeStrokeCommands.ToArray();
            _committedPaintStrokes.Add(new PaintStrokeRecord(layerIndex, commands));
            _paintHistoryRevision = _committedPaintStrokes.Count;
            _activeStrokeCommands.Clear();
            RaisePaintHistoryRevisionChanged();
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

        private Vector2 ApplyBrushUvAxisInversion(Vector2 uv)
        {
            if (_brushInvertX)
            {
                uv.X = 1f - uv.X;
            }

            if (_brushInvertY)
            {
                uv.Y = 1f - uv.Y;
            }

            return uv;
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

            if (_meshResources is null ||
                _meshResources.VertexBuffer.Handle == IntPtr.Zero ||
                _meshResources.IndexBuffer.Handle == IntPtr.Zero)
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

            if (TryMapPointerToPaintUvGpu(pointerDip, modelNode, collarNode, drawCollar, referenceRadius, out Vector2 gpuUv))
            {
                uv = gpuUv;
                _lastPaintHitMode = PaintHitMode.MeshHit;
                return true;
            }
            _lastPaintHitMode = PaintHitMode.Idle;
            return false;
        }

        private bool TryMapPointerToPaintUvCpu(
            Point pointerDip,
            ModelNode modelNode,
            CollarNode? collarNode,
            bool drawCollar,
            float referenceRadius,
            out Vector2 uv)
        {
            uv = default;
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
                    _meshResources!,
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
                uv = ApplyBrushUvAxisInversion(uv);
                _lastPaintHitMode = PaintHitMode.Fallback;
                return true;
            }

            uv = new Vector2(
                (bestLocalHit.X / (2f * referenceRadius)) + 0.5f,
                (bestLocalHit.Y / (2f * referenceRadius)) + 0.5f);
            uv = ApplyBrushUvAxisInversion(uv);
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

            if (!TryIntersectRayAabb(rayOriginLocal, rayDirectionLocal, mesh.BoundsMin, mesh.BoundsMax, out _, out _))
            {
                return false;
            }

            if (mesh.Bvh.TryIntersect(
                    rayOriginLocal,
                    rayDirectionLocal,
                    mesh.Positions,
                    mesh.Indices,
                    out localHitPoint,
                    out hitT,
                    out bool bvhTraversalCompleted))
            {
                return true;
            }

            if (bvhTraversalCompleted)
            {
                return false;
            }

            if (!TryIntersectMeshBruteForce(
                    rayOriginLocal,
                    rayDirectionLocal,
                    mesh.Positions,
                    mesh.Indices,
                    out localHitPoint,
                    out hitT))
            {
                return false;
            }

            return true;
        }

        private static bool TryIntersectMeshBruteForce(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            Vector3[] positions,
            uint[] indices,
            out Vector3 localHitPoint,
            out float hitT)
        {
            localHitPoint = default;
            hitT = float.MaxValue;
            bool hit = false;
            float bestT = float.MaxValue;
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
                if (!TryIntersectRayTriangle(rayOrigin, rayDirection, p0, p1, p2, out float t))
                {
                    continue;
                }

                if (t <= 1e-5f || t >= bestT)
                {
                    continue;
                }

                hit = true;
                bestT = t;
            }

            if (!hit)
            {
                return false;
            }

            hitT = bestT;
            localHitPoint = rayOrigin + (rayDirection * bestT);
            return true;
        }

        private static Vector3 RotateToLocalXY(Vector3 worldValue, float cosA, float sinA)
        {
            return new Vector3(
                (worldValue.X * cosA) + (worldValue.Y * sinA),
                (-worldValue.X * sinA) + (worldValue.Y * cosA),
                worldValue.Z);
        }

        private static bool TryIntersectRayAabb(
            Vector3 rayOrigin,
            Vector3 rayDirection,
            Vector3 boundsMin,
            Vector3 boundsMax,
            out float tMin,
            out float tMax)
        {
            const float epsilon = 1e-8f;
            tMin = 0f;
            tMax = float.MaxValue;

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
    }
}
