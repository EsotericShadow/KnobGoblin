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
            _project.PaintCoatMetallic = snapshot.PaintCoatMetallic;
            _project.PaintCoatRoughness = snapshot.PaintCoatRoughness;
            _project.ClearCoatAmount = snapshot.ClearCoatAmount;
            _project.ClearCoatRoughness = snapshot.ClearCoatRoughness;
            _project.AnisotropyAngleDegrees = snapshot.AnisotropyAngleDegrees;
            _project.PaintColor = new(snapshot.PaintColorX, snapshot.PaintColorY, snapshot.PaintColorZ);
            _project.ScratchWidthPx = snapshot.ScratchWidthPx;
            _project.ScratchDepth = snapshot.ScratchDepth;
            _project.ScratchDragResistance = snapshot.ScratchDragResistance;
            _project.ScratchDepthRamp = snapshot.ScratchDepthRamp;
            _project.ScratchExposeColor = new(
                snapshot.ScratchExposeColorX,
                snapshot.ScratchExposeColorY,
                snapshot.ScratchExposeColorZ);
            _project.ScratchExposeMetallic = snapshot.ScratchExposeMetallic;
            _project.ScratchExposeRoughness = snapshot.ScratchExposeRoughness;
            _project.SpiralNormalInfluenceEnabled = snapshot.SpiralNormalInfluenceEnabled;
            _project.SpiralNormalLodFadeStart = snapshot.SpiralNormalLodFadeStart;
            _project.SpiralNormalLodFadeEnd = snapshot.SpiralNormalLodFadeEnd;
            _project.SpiralRoughnessLodBoost = snapshot.SpiralRoughnessLodBoost;

            if (_metalViewport != null)
            {
                _metalViewport.RestorePaintHistoryRevision(snapshot.PaintHistoryRevision);
                _metalViewport.SetActivePaintLayer(snapshot.ActivePaintLayerIndex);
                _metalViewport.SetFocusedPaintLayer(snapshot.FocusedPaintLayerIndex);
                RefreshPaintLayerListFromViewport(preferActiveSelection: true);
            }

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
                ImportedMirrorX = collar.ImportedMirrorX,
                ImportedMirrorY = collar.ImportedMirrorY,
                ImportedMirrorZ = collar.ImportedMirrorZ,
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
            collar.ImportedMirrorX = snapshot.ImportedMirrorX;
            collar.ImportedMirrorY = snapshot.ImportedMirrorY;
            collar.ImportedMirrorZ = snapshot.ImportedMirrorZ;
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

        private bool TryAdoptSceneSelectionFromInspectorContext()
        {
            if (_updatingUi || _inspectorTabControl == null || _inspectorTabControl.SelectedItem is not TabItem selectedTab)
            {
                return false;
            }

            SceneNode? desiredNode = null;
            if (ReferenceEquals(selectedTab, _lightingTabItem))
            {
                desiredNode = ResolveSelectedLightSceneNode();
            }
            else if (ReferenceEquals(selectedTab, _modelTabItem) || ReferenceEquals(selectedTab, _brushTabItem))
            {
                desiredNode = ResolvePreferredModelSceneNode();
            }

            if (desiredNode == null || _project.SelectedNode?.Id == desiredNode.Id)
            {
                return false;
            }

            _project.SetSelectedNode(desiredNode);
            if (desiredNode is LightNode lightNode)
            {
                int index = _project.Lights.FindIndex(light => ReferenceEquals(light, lightNode.Light));
                if (index >= 0)
                {
                    _project.SetSelectedLightIndex(index);
                }
            }

            return true;
        }

        private SceneNode? ResolveSelectedLightSceneNode()
        {
            int selectedIndex = _project.SelectedLightIndex;
            if (selectedIndex < 0 || selectedIndex >= _project.Lights.Count)
            {
                return null;
            }

            KnobLight selectedLight = _project.Lights[selectedIndex];
            return _project.SceneRoot.Children
                .OfType<LightNode>()
                .FirstOrDefault(node => ReferenceEquals(node.Light, selectedLight));
        }

        private SceneNode? ResolvePreferredModelSceneNode()
        {
            if (_project.SelectedNode is ModelNode ||
                _project.SelectedNode is MaterialNode ||
                _project.SelectedNode is CollarNode)
            {
                return _project.SelectedNode;
            }

            return GetModelNode();
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

            RefreshInspectorFromProject(InspectorRefreshTabPolicy.FollowSceneSelection);
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

    }
}
