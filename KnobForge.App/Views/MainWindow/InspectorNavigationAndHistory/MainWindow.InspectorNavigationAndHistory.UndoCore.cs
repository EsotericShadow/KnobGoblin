using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using KnobForge.Core;
using KnobForge.Core.Scene;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private const int MaxUndoSnapshots = 64;
        private static readonly JsonSerializerOptions UndoFingerprintJsonOptions = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly List<InspectorUndoSnapshot> _undoSnapshots = new();
        private readonly List<InspectorUndoSnapshot> _redoSnapshots = new();
        private InspectorUndoSnapshot? _currentUndoSnapshot;
        private string _currentUndoFingerprint = string.Empty;
        private bool _undoRedoInitialized;
        private bool _applyingUndoRedo;

        private void InitializeUndoRedoSupport()
        {
            AddHandler(InputElement.KeyDownEvent, OnUndoRedoKeyDown, RoutingStrategies.Tunnel);
            InitializeUndoRedoHistory(resetStacks: true);
            UpdateUndoRedoButtonState();
        }

        private void OnUndoRedoKeyDown(object? sender, KeyEventArgs e)
        {
            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
            {
                return;
            }

            bool commandDown = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
            if (commandDown && e.Key == Key.Z)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    ExecuteRedo();
                }
                else
                {
                    ExecuteUndo();
                }

                e.Handled = true;
                return;
            }

            if (e.Key == Key.Y && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                ExecuteRedo();
                e.Handled = true;
            }
        }

        private void ExecuteUndo()
        {
            if (_undoSnapshots.Count == 0)
            {
                return;
            }

            InspectorUndoSnapshot currentSnapshot = CaptureInspectorUndoSnapshot();
            InspectorUndoSnapshot targetSnapshot = _undoSnapshots[^1];
            _undoSnapshots.RemoveAt(_undoSnapshots.Count - 1);
            _redoSnapshots.Add(currentSnapshot);
            ApplyUndoSnapshot(targetSnapshot);
        }

        private void ExecuteRedo()
        {
            if (_redoSnapshots.Count == 0)
            {
                return;
            }

            InspectorUndoSnapshot currentSnapshot = CaptureInspectorUndoSnapshot();
            InspectorUndoSnapshot targetSnapshot = _redoSnapshots[^1];
            _redoSnapshots.RemoveAt(_redoSnapshots.Count - 1);
            _undoSnapshots.Add(currentSnapshot);
            ApplyUndoSnapshot(targetSnapshot);
        }

        private void ApplyUndoSnapshot(InspectorUndoSnapshot snapshot)
        {
            _applyingUndoRedo = true;
            try
            {
                ApplyInspectorUndoSnapshot(snapshot);
                NotifyProjectStateChanged();
            }
            finally
            {
                _applyingUndoRedo = false;
            }

            InitializeUndoRedoHistory(resetStacks: false);
            UpdateUndoRedoButtonState();
        }

        private void CaptureUndoSnapshotIfChanged()
        {
            if (_applyingUndoRedo)
            {
                return;
            }

            InspectorUndoSnapshot snapshot = CaptureInspectorUndoSnapshot();
            string fingerprint = ComputeUndoFingerprint(snapshot);

            if (!_undoRedoInitialized || _currentUndoSnapshot == null)
            {
                _currentUndoSnapshot = snapshot;
                _currentUndoFingerprint = fingerprint;
                _undoRedoInitialized = true;
                UpdateUndoRedoButtonState();
                return;
            }

            if (string.Equals(fingerprint, _currentUndoFingerprint, StringComparison.Ordinal))
            {
                UpdateUndoRedoButtonState();
                return;
            }

            _undoSnapshots.Add(_currentUndoSnapshot);
            if (_undoSnapshots.Count > MaxUndoSnapshots)
            {
                _undoSnapshots.RemoveAt(0);
            }

            _redoSnapshots.Clear();
            _currentUndoSnapshot = snapshot;
            _currentUndoFingerprint = fingerprint;
            UpdateUndoRedoButtonState();
        }

        private void InitializeUndoRedoHistory(bool resetStacks)
        {
            if (resetStacks)
            {
                _undoSnapshots.Clear();
                _redoSnapshots.Clear();
            }

            InspectorUndoSnapshot snapshot = CaptureInspectorUndoSnapshot();
            _currentUndoSnapshot = snapshot;
            _currentUndoFingerprint = ComputeUndoFingerprint(snapshot);
            _undoRedoInitialized = true;
        }

        private void UpdateUndoRedoButtonState()
        {
            if (_undoButton != null)
            {
                _undoButton.IsEnabled = _undoSnapshots.Count > 0;
            }

            if (_redoButton != null)
            {
                _redoButton.IsEnabled = _redoSnapshots.Count > 0;
            }
        }

        private static string ComputeUndoFingerprint(InspectorUndoSnapshot snapshot)
        {
            return JsonSerializer.Serialize(snapshot, UndoFingerprintJsonOptions);
        }

        private InspectorUndoSnapshot CaptureInspectorUndoSnapshot()
        {
            ModelNode? model = GetModelNode();
            MaterialNode? material = model?.Children.OfType<MaterialNode>().FirstOrDefault();
            CollarNode? collar = model?.Children.OfType<CollarNode>().FirstOrDefault();

            UserReferenceProfileSnapshot? modelMaterialSnapshot = null;
            if (model != null && material != null)
            {
                modelMaterialSnapshot = CaptureUserReferenceProfileSnapshot(_project, model, material);
            }

            return new InspectorUndoSnapshot
            {
                Mode = _project.Mode,
                BasisDebug = _project.BasisDebug,
                EnvironmentTopColorX = _project.EnvironmentTopColor.X,
                EnvironmentTopColorY = _project.EnvironmentTopColor.Y,
                EnvironmentTopColorZ = _project.EnvironmentTopColor.Z,
                EnvironmentBottomColorX = _project.EnvironmentBottomColor.X,
                EnvironmentBottomColorY = _project.EnvironmentBottomColor.Y,
                EnvironmentBottomColorZ = _project.EnvironmentBottomColor.Z,
                EnvironmentIntensity = _project.EnvironmentIntensity,
                EnvironmentRoughnessMix = _project.EnvironmentRoughnessMix,
                ShadowsEnabled = _project.ShadowsEnabled,
                ShadowMode = _project.ShadowMode,
                ShadowStrength = _project.ShadowStrength,
                ShadowSoftness = _project.ShadowSoftness,
                ShadowDistance = _project.ShadowDistance,
                ShadowScale = _project.ShadowScale,
                ShadowQuality = _project.ShadowQuality,
                ShadowGray = _project.ShadowGray,
                ShadowDiffuseInfluence = _project.ShadowDiffuseInfluence,
                PaintHistoryRevision = _metalViewport?.PaintHistoryRevision ?? 0,
                ActivePaintLayerIndex = _metalViewport?.ActivePaintLayerIndex ?? 0,
                FocusedPaintLayerIndex = _metalViewport?.FocusedPaintLayerIndex ?? -1,
                BrushPaintingEnabled = _project.BrushPaintingEnabled,
                BrushType = _project.BrushType,
                BrushChannel = _project.BrushChannel,
                ScratchAbrasionType = _project.ScratchAbrasionType,
                BrushSizePx = _project.BrushSizePx,
                BrushOpacity = _project.BrushOpacity,
                BrushSpread = _project.BrushSpread,
                BrushDarkness = _project.BrushDarkness,
                PaintCoatMetallic = _project.PaintCoatMetallic,
                PaintCoatRoughness = _project.PaintCoatRoughness,
                ClearCoatAmount = _project.ClearCoatAmount,
                ClearCoatRoughness = _project.ClearCoatRoughness,
                AnisotropyAngleDegrees = _project.AnisotropyAngleDegrees,
                PaintColorX = _project.PaintColor.X,
                PaintColorY = _project.PaintColor.Y,
                PaintColorZ = _project.PaintColor.Z,
                ScratchWidthPx = _project.ScratchWidthPx,
                ScratchDepth = _project.ScratchDepth,
                ScratchDragResistance = _project.ScratchDragResistance,
                ScratchDepthRamp = _project.ScratchDepthRamp,
                ScratchExposeColorX = _project.ScratchExposeColor.X,
                ScratchExposeColorY = _project.ScratchExposeColor.Y,
                ScratchExposeColorZ = _project.ScratchExposeColor.Z,
                ScratchExposeMetallic = _project.ScratchExposeMetallic,
                ScratchExposeRoughness = _project.ScratchExposeRoughness,
                SpiralNormalInfluenceEnabled = _project.SpiralNormalInfluenceEnabled,
                SpiralNormalLodFadeStart = _project.SpiralNormalLodFadeStart,
                SpiralNormalLodFadeEnd = _project.SpiralNormalLodFadeEnd,
                SpiralRoughnessLodBoost = _project.SpiralRoughnessLodBoost,
                Lights = _project.Lights.Select(CaptureLightState).ToList(),
                SelectedLightIndex = _project.SelectedLightIndex,
                HasModelMaterialSnapshot = modelMaterialSnapshot != null,
                ModelMaterialSnapshot = modelMaterialSnapshot != null ? CloneSnapshot(modelMaterialSnapshot) : null,
                ModelReferenceStyle = model?.ReferenceStyle ?? ReferenceKnobStyle.Custom,
                SelectedUserReferenceProfileName = _selectedUserReferenceProfileName,
                CollarSnapshot = collar != null ? CaptureCollarStateSnapshot(collar) : null,
                Selection = CaptureSceneSelectionSnapshot(_project.SelectedNode)
            };
        }


    }
}
