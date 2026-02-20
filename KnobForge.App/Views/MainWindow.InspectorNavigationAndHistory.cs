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
                BrushPaintingEnabled = _project.BrushPaintingEnabled,
                BrushType = _project.BrushType,
                BrushChannel = _project.BrushChannel,
                ScratchAbrasionType = _project.ScratchAbrasionType,
                BrushSizePx = _project.BrushSizePx,
                BrushOpacity = _project.BrushOpacity,
                BrushSpread = _project.BrushSpread,
                BrushDarkness = _project.BrushDarkness,
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

        private void ApplyInspectorUndoSnapshot(InspectorUndoSnapshot snapshot)
        {
            _project.Mode = snapshot.Mode;
            _project.BasisDebug = snapshot.BasisDebug;
            _project.EnvironmentTopColor = new(
                snapshot.EnvironmentTopColorX,
                snapshot.EnvironmentTopColorY,
                snapshot.EnvironmentTopColorZ);
            _project.EnvironmentBottomColor = new(
                snapshot.EnvironmentBottomColorX,
                snapshot.EnvironmentBottomColorY,
                snapshot.EnvironmentBottomColorZ);
            _project.EnvironmentIntensity = snapshot.EnvironmentIntensity;
            _project.EnvironmentRoughnessMix = snapshot.EnvironmentRoughnessMix;

            _project.ShadowsEnabled = snapshot.ShadowsEnabled;
            _project.ShadowMode = snapshot.ShadowMode;
            _project.ShadowStrength = snapshot.ShadowStrength;
            _project.ShadowSoftness = snapshot.ShadowSoftness;
            _project.ShadowDistance = snapshot.ShadowDistance;
            _project.ShadowScale = snapshot.ShadowScale;
            _project.ShadowQuality = snapshot.ShadowQuality;
            _project.ShadowGray = snapshot.ShadowGray;
            _project.ShadowDiffuseInfluence = snapshot.ShadowDiffuseInfluence;

            _project.BrushPaintingEnabled = snapshot.BrushPaintingEnabled;
            _project.BrushType = snapshot.BrushType;
            _project.BrushChannel = snapshot.BrushChannel;
            _project.ScratchAbrasionType = snapshot.ScratchAbrasionType;
            _project.BrushSizePx = snapshot.BrushSizePx;
            _project.BrushOpacity = snapshot.BrushOpacity;
            _project.BrushSpread = snapshot.BrushSpread;
            _project.BrushDarkness = snapshot.BrushDarkness;
            _project.PaintColor = new(snapshot.PaintColorX, snapshot.PaintColorY, snapshot.PaintColorZ);
            _project.ScratchWidthPx = snapshot.ScratchWidthPx;
            _project.ScratchDepth = snapshot.ScratchDepth;
            _project.ScratchDragResistance = snapshot.ScratchDragResistance;
            _project.ScratchDepthRamp = snapshot.ScratchDepthRamp;
            _project.ScratchExposeColor = new(
                snapshot.ScratchExposeColorX,
                snapshot.ScratchExposeColorY,
                snapshot.ScratchExposeColorZ);
            _project.SpiralNormalInfluenceEnabled = snapshot.SpiralNormalInfluenceEnabled;
            _project.SpiralNormalLodFadeStart = snapshot.SpiralNormalLodFadeStart;
            _project.SpiralNormalLodFadeEnd = snapshot.SpiralNormalLodFadeEnd;
            _project.SpiralRoughnessLodBoost = snapshot.SpiralRoughnessLodBoost;

            ApplyLightStates(snapshot.Lights, snapshot.SelectedLightIndex);

            ModelNode model = GetModelNode() ?? CreateModelNode();
            MaterialNode material = model.Children.OfType<MaterialNode>().FirstOrDefault() ?? CreateMaterialNode(model);

            if (snapshot.HasModelMaterialSnapshot && snapshot.ModelMaterialSnapshot != null)
            {
                ApplyUserReferenceProfileSnapshot(_project, model, material, CloneSnapshot(snapshot.ModelMaterialSnapshot));
            }

            model.ReferenceStyle = snapshot.ModelReferenceStyle;
            _selectedUserReferenceProfileName = snapshot.SelectedUserReferenceProfileName;

            if (snapshot.CollarSnapshot != null)
            {
                CollarNode collar = EnsureCollarNode();
                ApplyCollarStateSnapshot(collar, snapshot.CollarSnapshot);
            }
            else
            {
                CollarNode? existingCollar = model.Children.OfType<CollarNode>().FirstOrDefault();
                if (existingCollar != null)
                {
                    model.RemoveChild(existingCollar);
                }
            }

            RebuildReferenceStyleOptions();
            SelectReferenceStyleOptionForModel(model);

            SceneNode selectedNode = ResolveSceneSelectionSnapshot(
                snapshot.Selection,
                model,
                material,
                model.Children.OfType<CollarNode>().FirstOrDefault());
            _project.SetSelectedNode(selectedNode);
        }

        private static LightStateSnapshot CaptureLightState(KnobLight light)
        {
            return new LightStateSnapshot
            {
                Name = light.Name,
                Type = light.Type,
                X = light.X,
                Y = light.Y,
                Z = light.Z,
                DirectionRadians = light.DirectionRadians,
                ColorR = light.Color.Red,
                ColorG = light.Color.Green,
                ColorB = light.Color.Blue,
                ColorA = light.Color.Alpha,
                Intensity = light.Intensity,
                Falloff = light.Falloff,
                DiffuseBoost = light.DiffuseBoost,
                SpecularBoost = light.SpecularBoost,
                SpecularPower = light.SpecularPower
            };
        }

        private void ApplyLightStates(IReadOnlyList<LightStateSnapshot> lights, int selectedLightIndex)
        {
            foreach (LightNode lightNode in _project.SceneRoot.Children.OfType<LightNode>().ToList())
            {
                _project.SceneRoot.RemoveChild(lightNode);
            }

            _project.Lights.Clear();
            foreach (LightStateSnapshot light in lights)
            {
                KnobLight restoredLight = new()
                {
                    Name = light.Name,
                    Type = light.Type,
                    X = light.X,
                    Y = light.Y,
                    Z = light.Z,
                    DirectionRadians = light.DirectionRadians,
                    Color = new SKColor(light.ColorR, light.ColorG, light.ColorB, light.ColorA),
                    Intensity = light.Intensity,
                    Falloff = light.Falloff,
                    DiffuseBoost = light.DiffuseBoost,
                    SpecularBoost = light.SpecularBoost,
                    SpecularPower = light.SpecularPower
                };

                _project.Lights.Add(restoredLight);
                _project.SceneRoot.AddChild(new LightNode(restoredLight));
            }

            if (_project.Lights.Count == 0)
            {
                _project.AddLight();
                selectedLightIndex = _project.SelectedLightIndex;
            }

            int clampedIndex = Math.Clamp(selectedLightIndex, 0, _project.Lights.Count - 1);
            _project.SetSelectedLightIndex(clampedIndex);
        }

        private ModelNode CreateModelNode()
        {
            var model = new ModelNode("KnobModel");
            _project.SceneRoot.AddChild(model);
            return model;
        }

        private static MaterialNode CreateMaterialNode(ModelNode model)
        {
            var material = new MaterialNode("DefaultMaterial");
            model.AddChild(material);
            return material;
        }

        private static CollarStateSnapshot CaptureCollarStateSnapshot(CollarNode collar)
        {
            return new CollarStateSnapshot
            {
                Enabled = collar.Enabled,
                Preset = collar.Preset,
                InnerRadiusRatio = collar.InnerRadiusRatio,
                GapToKnobRatio = collar.GapToKnobRatio,
                ElevationRatio = collar.ElevationRatio,
                OverallRotationRadians = collar.OverallRotationRadians,
                BiteAngleRadians = collar.BiteAngleRadians,
                BodyRadiusRatio = collar.BodyRadiusRatio,
                BodyEllipseYScale = collar.BodyEllipseYScale,
                NeckTaper = collar.NeckTaper,
                TailTaper = collar.TailTaper,
                MassBias = collar.MassBias,
                TailUnderlap = collar.TailUnderlap,
                HeadScale = collar.HeadScale,
                JawBulge = collar.JawBulge,
                UvSeamFollowBite = collar.UvSeamFollowBite,
                UvSeamOffset = collar.UvSeamOffset,
                PathSegments = collar.PathSegments,
                CrossSegments = collar.CrossSegments,
                BaseColorX = collar.BaseColor.X,
                BaseColorY = collar.BaseColor.Y,
                BaseColorZ = collar.BaseColor.Z,
                Metallic = collar.Metallic,
                Roughness = collar.Roughness,
                Pearlescence = collar.Pearlescence,
                RustAmount = collar.RustAmount,
                WearAmount = collar.WearAmount,
                GunkAmount = collar.GunkAmount,
                NormalStrength = collar.NormalStrength,
                HeightStrength = collar.HeightStrength,
                ScaleDensity = collar.ScaleDensity,
                ScaleRelief = collar.ScaleRelief,
                ImportedMeshPath = collar.ImportedMeshPath,
                ImportedScale = collar.ImportedScale,
                ImportedRotationRadians = collar.ImportedRotationRadians,
                ImportedHeadAngleOffsetRadians = collar.ImportedHeadAngleOffsetRadians,
                ImportedOffsetXRatio = collar.ImportedOffsetXRatio,
                ImportedOffsetYRatio = collar.ImportedOffsetYRatio,
                ImportedInflateRatio = collar.ImportedInflateRatio,
                ImportedBodyLengthScale = collar.ImportedBodyLengthScale,
                ImportedBodyThicknessScale = collar.ImportedBodyThicknessScale,
                ImportedHeadLengthScale = collar.ImportedHeadLengthScale,
                ImportedHeadThicknessScale = collar.ImportedHeadThicknessScale
            };
        }

        private static void ApplyCollarStateSnapshot(CollarNode collar, CollarStateSnapshot snapshot)
        {
            collar.Enabled = snapshot.Enabled;
            collar.Preset = snapshot.Preset;
            collar.InnerRadiusRatio = snapshot.InnerRadiusRatio;
            collar.GapToKnobRatio = snapshot.GapToKnobRatio;
            collar.ElevationRatio = snapshot.ElevationRatio;
            collar.OverallRotationRadians = snapshot.OverallRotationRadians;
            collar.BiteAngleRadians = snapshot.BiteAngleRadians;
            collar.BodyRadiusRatio = snapshot.BodyRadiusRatio;
            collar.BodyEllipseYScale = snapshot.BodyEllipseYScale;
            collar.NeckTaper = snapshot.NeckTaper;
            collar.TailTaper = snapshot.TailTaper;
            collar.MassBias = snapshot.MassBias;
            collar.TailUnderlap = snapshot.TailUnderlap;
            collar.HeadScale = snapshot.HeadScale;
            collar.JawBulge = snapshot.JawBulge;
            collar.UvSeamFollowBite = snapshot.UvSeamFollowBite;
            collar.UvSeamOffset = snapshot.UvSeamOffset;
            collar.PathSegments = snapshot.PathSegments;
            collar.CrossSegments = snapshot.CrossSegments;
            collar.BaseColor = new(snapshot.BaseColorX, snapshot.BaseColorY, snapshot.BaseColorZ);
            collar.Metallic = snapshot.Metallic;
            collar.Roughness = snapshot.Roughness;
            collar.Pearlescence = snapshot.Pearlescence;
            collar.RustAmount = snapshot.RustAmount;
            collar.WearAmount = snapshot.WearAmount;
            collar.GunkAmount = snapshot.GunkAmount;
            collar.NormalStrength = snapshot.NormalStrength;
            collar.HeightStrength = snapshot.HeightStrength;
            collar.ScaleDensity = snapshot.ScaleDensity;
            collar.ScaleRelief = snapshot.ScaleRelief;
            collar.ImportedMeshPath = snapshot.ImportedMeshPath;
            collar.ImportedScale = snapshot.ImportedScale;
            collar.ImportedRotationRadians = snapshot.ImportedRotationRadians;
            collar.ImportedHeadAngleOffsetRadians = snapshot.ImportedHeadAngleOffsetRadians;
            collar.ImportedOffsetXRatio = snapshot.ImportedOffsetXRatio;
            collar.ImportedOffsetYRatio = snapshot.ImportedOffsetYRatio;
            collar.ImportedInflateRatio = snapshot.ImportedInflateRatio;
            collar.ImportedBodyLengthScale = snapshot.ImportedBodyLengthScale;
            collar.ImportedBodyThicknessScale = snapshot.ImportedBodyThicknessScale;
            collar.ImportedHeadLengthScale = snapshot.ImportedHeadLengthScale;
            collar.ImportedHeadThicknessScale = snapshot.ImportedHeadThicknessScale;
        }

        private SceneSelectionSnapshot CaptureSceneSelectionSnapshot(SceneNode? node)
        {
            if (node is LightNode lightNode)
            {
                int index = _project.Lights.FindIndex(light => ReferenceEquals(light, lightNode.Light));
                return new SceneSelectionSnapshot
                {
                    Kind = SceneSelectionKind.Light,
                    LightIndex = index
                };
            }

            return node switch
            {
                SceneRootNode => new SceneSelectionSnapshot { Kind = SceneSelectionKind.SceneRoot },
                ModelNode => new SceneSelectionSnapshot { Kind = SceneSelectionKind.Model },
                MaterialNode => new SceneSelectionSnapshot { Kind = SceneSelectionKind.Material },
                CollarNode => new SceneSelectionSnapshot { Kind = SceneSelectionKind.Collar },
                _ => new SceneSelectionSnapshot { Kind = SceneSelectionKind.Unknown }
            };
        }

        private SceneNode ResolveSceneSelectionSnapshot(
            SceneSelectionSnapshot selection,
            ModelNode model,
            MaterialNode material,
            CollarNode? collar)
        {
            if (selection.Kind == SceneSelectionKind.Light &&
                selection.LightIndex >= 0 &&
                selection.LightIndex < _project.Lights.Count)
            {
                KnobLight light = _project.Lights[selection.LightIndex];
                return (SceneNode?)_project.SceneRoot.Children
                    .OfType<LightNode>()
                    .FirstOrDefault(node => ReferenceEquals(node.Light, light)) ?? _project.SceneRoot;
            }

            return selection.Kind switch
            {
                SceneSelectionKind.Model => model,
                SceneSelectionKind.Material => material,
                SceneSelectionKind.Collar when collar != null => collar,
                _ => _project.SceneRoot
            };
        }

        private void SyncSceneListSelectionToProjectNode()
        {
            if (_sceneListBox == null)
            {
                return;
            }

            SceneNode? selectedNode = _project.SelectedNode;
            if (selectedNode == null)
            {
                return;
            }

            SceneNode? match = _sceneNodes.FirstOrDefault(node => node.Id == selectedNode.Id);
            if (match != null && !ReferenceEquals(_sceneListBox.SelectedItem, match))
            {
                _sceneListBox.SelectedItem = match;
            }
        }

        private void SyncInspectorForSelectedSceneNode(SceneNode node)
        {
            bool selectedLightChanged = false;
            if (node is LightNode lightNode)
            {
                int lightIndex = _project.Lights.FindIndex(light => ReferenceEquals(light, lightNode.Light));
                if (lightIndex >= 0 && _project.SetSelectedLightIndex(lightIndex))
                {
                    selectedLightChanged = true;
                }
            }

            SelectInspectorTabForSceneNode(node);
            RefreshInspectorFromProject();
            if (selectedLightChanged)
            {
                _metalViewport?.InvalidateGpu();
            }
        }

        private void SelectInspectorTabForSceneNode(SceneNode? node)
        {
            if (_inspectorTabControl == null)
            {
                return;
            }

            TabItem? target = node switch
            {
                LightNode => _lightingTabItem,
                ModelNode => _modelTabItem,
                MaterialNode => _modelTabItem,
                CollarNode => _modelTabItem,
                SceneRootNode => _modelTabItem,
                _ => _modelTabItem
            };

            if (target != null && !ReferenceEquals(_inspectorTabControl.SelectedItem, target))
            {
                _inspectorTabControl.SelectedItem = target;
            }
        }

        private void SelectInspectorTabForControl(Control control)
        {
            if (_inspectorTabControl == null)
            {
                return;
            }

            TabItem? tab = FindAncestorTabItem(control);
            if (tab != null && !ReferenceEquals(_inspectorTabControl.SelectedItem, tab))
            {
                _inspectorTabControl.SelectedItem = tab;
            }
        }

        private static TabItem? FindAncestorTabItem(Control control)
        {
            Visual? visual = control;
            while (visual != null)
            {
                if (visual is TabItem tabItem)
                {
                    return tabItem;
                }

                visual = visual.GetVisualParent();
            }

            return null;
        }

        private sealed class InspectorUndoSnapshot
        {
            public LightingMode Mode { get; set; }
            public BasisDebugMode BasisDebug { get; set; }
            public float EnvironmentTopColorX { get; set; }
            public float EnvironmentTopColorY { get; set; }
            public float EnvironmentTopColorZ { get; set; }
            public float EnvironmentBottomColorX { get; set; }
            public float EnvironmentBottomColorY { get; set; }
            public float EnvironmentBottomColorZ { get; set; }
            public float EnvironmentIntensity { get; set; }
            public float EnvironmentRoughnessMix { get; set; }
            public bool ShadowsEnabled { get; set; }
            public ShadowLightMode ShadowMode { get; set; } = ShadowLightMode.Weighted;
            public float ShadowStrength { get; set; }
            public float ShadowSoftness { get; set; }
            public float ShadowDistance { get; set; }
            public float ShadowScale { get; set; }
            public float ShadowQuality { get; set; }
            public float ShadowGray { get; set; }
            public float ShadowDiffuseInfluence { get; set; }
            public bool BrushPaintingEnabled { get; set; }
            public PaintBrushType BrushType { get; set; }
            public PaintChannel BrushChannel { get; set; }
            public ScratchAbrasionType ScratchAbrasionType { get; set; }
            public float BrushSizePx { get; set; }
            public float BrushOpacity { get; set; }
            public float BrushSpread { get; set; }
            public float BrushDarkness { get; set; }
            public float PaintColorX { get; set; }
            public float PaintColorY { get; set; }
            public float PaintColorZ { get; set; }
            public float ScratchWidthPx { get; set; }
            public float ScratchDepth { get; set; }
            public float ScratchDragResistance { get; set; }
            public float ScratchDepthRamp { get; set; }
            public float ScratchExposeColorX { get; set; }
            public float ScratchExposeColorY { get; set; }
            public float ScratchExposeColorZ { get; set; }
            public bool SpiralNormalInfluenceEnabled { get; set; }
            public float SpiralNormalLodFadeStart { get; set; }
            public float SpiralNormalLodFadeEnd { get; set; }
            public float SpiralRoughnessLodBoost { get; set; }
            public List<LightStateSnapshot> Lights { get; set; } = new();
            public int SelectedLightIndex { get; set; }
            public bool HasModelMaterialSnapshot { get; set; }
            public UserReferenceProfileSnapshot? ModelMaterialSnapshot { get; set; }
            public ReferenceKnobStyle ModelReferenceStyle { get; set; } = ReferenceKnobStyle.Custom;
            public string? SelectedUserReferenceProfileName { get; set; }
            public CollarStateSnapshot? CollarSnapshot { get; set; }
            public SceneSelectionSnapshot Selection { get; set; } = new();
        }

        private sealed class LightStateSnapshot
        {
            public string Name { get; set; } = "Light";
            public LightType Type { get; set; } = LightType.Point;
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
            public float DirectionRadians { get; set; }
            public byte ColorR { get; set; }
            public byte ColorG { get; set; }
            public byte ColorB { get; set; }
            public byte ColorA { get; set; } = byte.MaxValue;
            public float Intensity { get; set; }
            public float Falloff { get; set; }
            public float DiffuseBoost { get; set; }
            public float SpecularBoost { get; set; }
            public float SpecularPower { get; set; }
        }

        private sealed class CollarStateSnapshot
        {
            public bool Enabled { get; set; }
            public CollarPreset Preset { get; set; }
            public float InnerRadiusRatio { get; set; }
            public float GapToKnobRatio { get; set; }
            public float ElevationRatio { get; set; }
            public float OverallRotationRadians { get; set; }
            public float BiteAngleRadians { get; set; }
            public float BodyRadiusRatio { get; set; }
            public float BodyEllipseYScale { get; set; }
            public float NeckTaper { get; set; }
            public float TailTaper { get; set; }
            public float MassBias { get; set; }
            public float TailUnderlap { get; set; }
            public float HeadScale { get; set; }
            public float JawBulge { get; set; }
            public bool UvSeamFollowBite { get; set; }
            public float UvSeamOffset { get; set; }
            public int PathSegments { get; set; }
            public int CrossSegments { get; set; }
            public float BaseColorX { get; set; }
            public float BaseColorY { get; set; }
            public float BaseColorZ { get; set; }
            public float Metallic { get; set; }
            public float Roughness { get; set; }
            public float Pearlescence { get; set; }
            public float RustAmount { get; set; }
            public float WearAmount { get; set; }
            public float GunkAmount { get; set; }
            public float NormalStrength { get; set; }
            public float HeightStrength { get; set; }
            public float ScaleDensity { get; set; }
            public float ScaleRelief { get; set; }
            public string ImportedMeshPath { get; set; } = string.Empty;
            public float ImportedScale { get; set; }
            public float ImportedRotationRadians { get; set; }
            public float ImportedHeadAngleOffsetRadians { get; set; }
            public float ImportedOffsetXRatio { get; set; }
            public float ImportedOffsetYRatio { get; set; }
            public float ImportedInflateRatio { get; set; }
            public float ImportedBodyLengthScale { get; set; }
            public float ImportedBodyThicknessScale { get; set; }
            public float ImportedHeadLengthScale { get; set; }
            public float ImportedHeadThicknessScale { get; set; }
        }

        private sealed class SceneSelectionSnapshot
        {
            public SceneSelectionKind Kind { get; set; } = SceneSelectionKind.Unknown;
            public int LightIndex { get; set; } = -1;
        }

        private enum SceneSelectionKind
        {
            Unknown = 0,
            SceneRoot = 1,
            Model = 2,
            Material = 3,
            Collar = 4,
            Light = 5
        }
    }
}
