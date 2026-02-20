using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private readonly List<InspectorSearchItem> _inspectorSearchItems = new();

        private void InitializeInspectorUx()
        {
            if (_inspectorSearchTextBox == null)
            {
                return;
            }

            BuildInspectorSearchIndex();
            _inspectorSearchTextBox.KeyDown += OnInspectorSearchTextBoxKeyDown;
            KeyDown += OnInspectorWindowKeyDown;
        }

        private void BuildInspectorSearchIndex()
        {
            _inspectorSearchItems.Clear();

            RegisterSearchItem(_referenceStyleCombo, "Reference Profile Style", "profile", "reference", "preset");
            RegisterSearchItem(_referenceStyleSaveNameTextBox, "Reference Profile Save Name", "profile", "save", "name");
            RegisterSearchItem(_modelRadiusSlider, "Model Radius", "radius", "size", "shape");
            RegisterSearchItem(_modelHeightSlider, "Model Height", "height", "size", "shape");
            RegisterSearchItem(_modelTopScaleSlider, "Top Scale", "top", "scale");
            RegisterSearchItem(_modelBevelSlider, "Bevel", "bevel", "edge");
            RegisterSearchItem(_bevelCurveSlider, "Bevel Curve", "bevel", "curve");
            RegisterSearchItem(_crownProfileSlider, "Crown Profile", "crown", "profile");
            RegisterSearchItem(_modelSegmentsSlider, "Segments", "segments", "radial");
            RegisterSearchItem(_gripDensitySlider, "Grip Density", "grip", "knurl", "density");
            RegisterSearchItem(_gripDepthSlider, "Grip Depth", "grip", "knurl", "depth");

            RegisterSearchItem(_materialRoughnessSlider, "Material Roughness", "rough", "roughness", "surface");
            RegisterSearchItem(_materialMetallicSlider, "Material Metallic", "metal", "metallic");
            RegisterSearchItem(_materialRegionCombo, "Material Region", "material", "part", "top", "bevel", "side");
            RegisterSearchItem(_materialRustSlider, "Material Rust", "rust");
            RegisterSearchItem(_materialWearSlider, "Material Wear", "wear");
            RegisterSearchItem(_materialGunkSlider, "Material Gunk", "gunk");

            RegisterSearchItem(_brushPaintChannelCombo, "Paint Channel", "paint", "channel", "scratch");
            RegisterSearchItem(_brushTypeCombo, "Brush Type", "paint", "brush");
            RegisterSearchItem(_brushPaintColorPicker, "Paint Color", "paint", "color", "picker", "rgb");
            RegisterSearchItem(_brushSizeSlider, "Brush Size", "brush", "size");
            RegisterSearchItem(_brushOpacitySlider, "Brush Opacity", "brush", "opacity");
            RegisterSearchItem(_scratchAbrasionTypeCombo, "Scratch Abrasion Type", "scratch", "abrasion", "tool");
            RegisterSearchItem(_scratchWidthSlider, "Scratch Width", "scratch", "width");
            RegisterSearchItem(_scratchDepthSlider, "Scratch Depth", "scratch", "depth", "carve");
            RegisterSearchItem(_scratchResistanceSlider, "Scratch Drag Resistance", "scratch", "resistance", "drag");
            RegisterSearchItem(_scratchDepthRampSlider, "Scratch Depth Ramp", "scratch", "ramp", "option", "alt");
            RegisterSearchItem(_scratchExposeColorRSlider, "Scratch Exposed Color R", "scratch", "exposed", "silver", "color");
            RegisterSearchItem(_scratchExposeColorGSlider, "Scratch Exposed Color G", "scratch", "exposed", "silver", "color");
            RegisterSearchItem(_scratchExposeColorBSlider, "Scratch Exposed Color B", "scratch", "exposed", "silver", "color");

            RegisterSearchItem(_collarPresetCombo, "Collar Preset", "collar", "snake", "ouroboros");
            RegisterSearchItem(_collarMeshPathTextBox, "Collar Mesh Path", "collar", "mesh", "import", "glb", "stl");
            RegisterSearchItem(_collarScaleSlider, "Collar Imported Scale", "collar", "scale", "import");
            RegisterSearchItem(_collarOffsetXSlider, "Collar Imported Offset X", "collar", "offset", "x");
            RegisterSearchItem(_collarOffsetYSlider, "Collar Imported Offset Y", "collar", "offset", "y");
            RegisterSearchItem(_collarInflateSlider, "Collar Imported Inflate", "collar", "inflate");

            RegisterSearchItem(_envIntensitySlider, "Environment Intensity", "env", "environment", "intensity");
            RegisterSearchItem(_envRoughnessMixSlider, "Environment Roughness Mix", "env", "environment", "roughness");
            RegisterSearchItem(_shadowEnabledCheckBox, "Shadows Enabled", "shadow", "enable");
            RegisterSearchItem(_shadowSoftnessSlider, "Shadow Softness", "shadow", "softness");
            RegisterSearchItem(_shadowQualitySlider, "Shadow Quality", "shadow", "quality");
            RegisterSearchItem(_shadowStrengthSlider, "Shadow Strength", "shadow", "strength");

            RegisterSearchItem(_intensitySlider, "Selected Light Intensity", "light", "intensity");
            RegisterSearchItem(_falloffSlider, "Selected Light Falloff", "light", "falloff");
            RegisterSearchItem(_directionSlider, "Selected Light Direction", "light", "direction");
        }

        private void RegisterSearchItem(Control? control, string displayName, params string[] aliases)
        {
            if (control == null)
            {
                return;
            }

            var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string token in displayName.Split(new[] { ' ', '-', '/', '(', ')', ':', '.' }, StringSplitOptions.RemoveEmptyEntries))
            {
                terms.Add(token);
            }

            foreach (string alias in aliases)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    terms.Add(alias.Trim());
                }
            }

            _inspectorSearchItems.Add(new InspectorSearchItem(displayName, control, terms.ToArray()));
        }

        private void OnInspectorWindowKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.K && e.Key != Key.F)
            {
                return;
            }

            bool commandDown = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
            if (!commandDown || _inspectorSearchTextBox == null)
            {
                return;
            }

            e.Handled = true;
            _inspectorSearchTextBox.Focus();
            _inspectorSearchTextBox.SelectAll();
        }

        private void OnInspectorSearchTextBoxKeyDown(object? sender, KeyEventArgs e)
        {
            if (_inspectorSearchTextBox == null || e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            string query = NormalizeSearchText(_inspectorSearchTextBox.Text ?? string.Empty);
            if (string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            InspectorSearchItem? best = FindBestSearchItem(query);
            if (best == null)
            {
                return;
            }

            JumpToSearchItem(best);
            _inspectorSearchTextBox.SelectAll();
        }

        private InspectorSearchItem? FindBestSearchItem(string query)
        {
            return _inspectorSearchItems
                .Select(item => (Item: item, Score: ScoreSearchItem(item, query)))
                .Where(entry => entry.Score > 0)
                .OrderByDescending(entry => entry.Score)
                .ThenBy(entry => entry.Item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(entry => entry.Item)
                .FirstOrDefault();
        }

        private static string NormalizeSearchText(string value)
        {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static int ScoreSearchItem(InspectorSearchItem item, string query)
        {
            int score = 0;
            if (item.DisplayNameLower.StartsWith(query, StringComparison.Ordinal))
            {
                score += 120;
            }
            else if (item.DisplayNameLower.Contains(query, StringComparison.Ordinal))
            {
                score += 70;
            }

            foreach (string keyword in item.KeywordsLower)
            {
                if (keyword.StartsWith(query, StringComparison.Ordinal))
                {
                    score += 45;
                }
                else if (keyword.Contains(query, StringComparison.Ordinal))
                {
                    score += 20;
                }
            }

            return score;
        }

        private void JumpToSearchItem(InspectorSearchItem item)
        {
            JumpToControl(item.TargetControl);
        }

        private void JumpToControl(Control targetControl)
        {
            SelectInspectorTabForControl(targetControl);
            ExpandAncestorExpanders(targetControl);
            Dispatcher.UIThread.Post(() =>
            {
                targetControl.BringIntoView();
                targetControl.Focus();
                if (targetControl is TextBox textBox)
                {
                    textBox.SelectAll();
                }
            }, DispatcherPriority.Background);
        }

        private static void ExpandAncestorExpanders(Control control)
        {
            Visual? visual = control;
            while (visual != null)
            {
                if (visual is Expander expander)
                {
                    expander.IsExpanded = true;
                }

                visual = visual.GetVisualParent();
            }
        }

        private sealed class InspectorSearchItem
        {
            public InspectorSearchItem(string displayName, Control targetControl, IReadOnlyList<string> keywords)
            {
                DisplayName = displayName;
                TargetControl = targetControl;
                DisplayNameLower = displayName.ToLowerInvariant();
                KeywordsLower = keywords.Select(keyword => keyword.ToLowerInvariant()).Distinct().ToArray();
            }

            public string DisplayName { get; }
            public Control TargetControl { get; }
            public string DisplayNameLower { get; }
            public IReadOnlyList<string> KeywordsLower { get; }
        }
    }
}
