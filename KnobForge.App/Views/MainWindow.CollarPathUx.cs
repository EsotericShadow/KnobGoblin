using Avalonia.Media;
using KnobForge.Core.Scene;
using System;
using System.IO;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private void UpdateCollarMeshPathFeedback(CollarPreset preset, string? customPath, bool? customImportedPresetOverride = null)
        {
            if (_collarResolvedMeshPathText == null || _collarMeshPathStatusText == null)
            {
                return;
            }

            bool importedPreset = CollarNode.IsImportedMeshPreset(preset);
            if (!importedPreset)
            {
                _collarResolvedMeshPathText.Text = "Resolved Source: procedural collar preset (no external mesh).";
                SetCollarMeshPathStatus("Path status: not applicable.", Brushes.LightGray);
                return;
            }

            bool customImportedPreset = customImportedPresetOverride ?? preset == CollarPreset.ImportedStl;
            string resolvedPath = CollarNode.ResolveImportedMeshPath(preset, customPath);
            _collarResolvedMeshPathText.Text = $"Resolved Source: {resolvedPath}";

            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                SetCollarMeshPathStatus(
                    customImportedPreset
                        ? "Path status: required. Enter a .glb or .stl path for custom import."
                        : "Path status: unresolved preset path.",
                    Brushes.IndianRed);
                return;
            }

            bool validExtension =
                resolvedPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ||
                resolvedPath.EndsWith(".stl", StringComparison.OrdinalIgnoreCase);
            bool exists = File.Exists(resolvedPath);
            if (!validExtension)
            {
                SetCollarMeshPathStatus("Path status: unsupported extension (use .glb or .stl).", Brushes.IndianRed);
                return;
            }

            if (exists)
            {
                SetCollarMeshPathStatus(
                    customImportedPreset
                        ? "Path status: file found."
                        : "Path status: file found (preset-managed, read-only).",
                    Brushes.LightGreen);
                return;
            }

            SetCollarMeshPathStatus(
                customImportedPreset
                    ? "Path status: file missing."
                    : "Path status: preset source file missing.",
                Brushes.IndianRed);
        }

        private void SetCollarMeshPathStatus(string text, IBrush foreground)
        {
            if (_collarMeshPathStatusText == null)
            {
                return;
            }

            _collarMeshPathStatusText.Text = text;
            _collarMeshPathStatusText.Foreground = foreground;
        }
    }
}
