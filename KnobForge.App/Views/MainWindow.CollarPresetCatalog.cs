using Avalonia.Interactivity;
using Avalonia.Media;
using KnobForge.Core.Scene;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KnobForge.App.Views
{
    public partial class MainWindow
    {
        private const string CollarModelsFolderName = "collar_models";

        private void RebuildCollarPresetOptions()
        {
            if (_collarPresetCombo == null)
            {
                return;
            }

            CollarPresetOption previousSelection = ResolveSelectedCollarPresetOption();
            string previousResolvedPath = previousSelection.ResolveImportedMeshPath(_collarMeshPathTextBox?.Text);

            _collarPresetOptions.Clear();
            _collarPresetOptions.Add(CollarPresetOption.CreateGroupLabel("Built-in"));
            _collarPresetOptions.Add(new CollarPresetOption(CollarPreset.None, "  None"));
            _collarPresetOptions.Add(new CollarPresetOption(CollarPreset.SnakeOuroboros, "  Snake Ouroboros (Procedural)"));
            _collarPresetOptions.Add(new CollarPresetOption(
                CollarPreset.MeshyOuroborosRing,
                "  Ouroboros Ring (Generated)",
                CollarNode.ResolveImportedMeshPath(CollarPreset.MeshyOuroborosRing, null)));
            _collarPresetOptions.Add(new CollarPresetOption(
                CollarPreset.MeshyOuroborosRingTextured,
                "  Ouroboros Ring (Textured)",
                CollarNode.ResolveImportedMeshPath(CollarPreset.MeshyOuroborosRingTextured, null)));

            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CollarPresetOption option in _collarPresetOptions)
            {
                if (!string.IsNullOrWhiteSpace(option.FixedImportedMeshPath))
                {
                    seenPaths.Add(NormalizePathForCompare(option.FixedImportedMeshPath));
                }
            }

            List<string> discoveredModels = EnumerateDiscoveredCollarModelPaths().ToList();
            if (discoveredModels.Count > 0)
            {
                _collarPresetOptions.Add(CollarPresetOption.CreateGroupLabel("Library (collar_models)"));
            }

            int addedLibraryOptions = 0;
            foreach (string modelPath in discoveredModels)
            {
                string normalizedPath = NormalizePathForCompare(modelPath);
                if (seenPaths.Contains(normalizedPath))
                {
                    continue;
                }

                seenPaths.Add(normalizedPath);
                _collarPresetOptions.Add(new CollarPresetOption(
                    CollarPreset.ImportedStl,
                    $"  {BuildCollarModelDisplayName(modelPath)}",
                    modelPath,
                    allowsCustomPathEntry: false));
                addedLibraryOptions++;
            }
            _discoveredCollarLibraryCount = addedLibraryOptions;

            _collarPresetOptions.Add(CollarPresetOption.CreateGroupLabel("Custom Import"));
            _collarPresetOptions.Add(new CollarPresetOption(
                CollarPreset.ImportedStl,
                "  Custom Imported (.glb/.stl)",
                null,
                allowsCustomPathEntry: true));

            _collarPresetCombo.ItemsSource = _collarPresetOptions.ToList();
            CollarPresetOption selected = ResolveCollarPresetOptionForState(previousSelection.Preset, previousResolvedPath);
            _collarPresetCombo.SelectedItem = selected;
            _lastSelectableCollarPresetOption = selected;
        }

        private void OnRefreshCollarLibraryButtonClicked(object? sender, RoutedEventArgs e)
        {
            CollarPresetOption previouslySelected = ResolveSelectedCollarPresetOption();
            string previousResolvedPath = previouslySelected.ResolveImportedMeshPath(_collarMeshPathTextBox?.Text);

            RebuildCollarPresetOptions();

            CollarPresetOption selected = ResolveCollarPresetOptionForState(previouslySelected.Preset, previousResolvedPath);
            if (_collarPresetCombo != null && !ReferenceEquals(_collarPresetCombo.SelectedItem, selected))
            {
                _collarPresetCombo.SelectedItem = selected;
            }
            _lastSelectableCollarPresetOption = selected;

            bool hasModel = GetModelNode() != null;
            UpdateCollarControlEnablement(hasModel, selected.Preset);
            SetCollarMeshPathStatus(
                _discoveredCollarLibraryCount > 0
                    ? $"Path status: library refreshed ({_discoveredCollarLibraryCount} model{(_discoveredCollarLibraryCount == 1 ? string.Empty : "s")} found)."
                    : "Path status: library refreshed (no models found in collar_models).",
                _discoveredCollarLibraryCount > 0 ? Brushes.LightGreen : Brushes.LightGray);
        }

        private CollarPresetOption ResolveSelectedCollarPresetOption()
        {
            if (_collarPresetCombo?.SelectedItem is CollarPresetOption option && option.IsSelectable)
            {
                _lastSelectableCollarPresetOption = option;
                return option;
            }

            if (_lastSelectableCollarPresetOption != null)
            {
                return _lastSelectableCollarPresetOption;
            }

            return GetFirstSelectableCollarPresetOption();
        }

        private CollarPresetOption ResolveCollarPresetOptionForState(CollarPreset preset, string? importedMeshPath)
        {
            if (_collarPresetOptions.Count == 0)
            {
                return new CollarPresetOption(CollarPreset.None, "None");
            }

            if (preset == CollarPreset.ImportedStl)
            {
                string normalizedResolvedPath = NormalizePathForCompare(CollarNode.ResolveImportedMeshPath(preset, importedMeshPath));
                if (!string.IsNullOrWhiteSpace(normalizedResolvedPath))
                {
                    CollarPresetOption? matchedLibraryOption = _collarPresetOptions.FirstOrDefault(option =>
                        option.IsSelectable &&
                        option.Preset == CollarPreset.ImportedStl &&
                        !option.AllowsCustomPathEntry &&
                        PathsEqual(option.FixedImportedMeshPath, normalizedResolvedPath));
                    if (matchedLibraryOption != null)
                    {
                        return matchedLibraryOption;
                    }
                }

                CollarPresetOption? customImported = _collarPresetOptions.FirstOrDefault(option =>
                    option.IsSelectable &&
                    option.Preset == CollarPreset.ImportedStl &&
                    option.AllowsCustomPathEntry);
                if (customImported != null)
                {
                    return customImported;
                }
            }

            CollarPresetOption? presetMatch = _collarPresetOptions.FirstOrDefault(option =>
                option.IsSelectable &&
                option.Preset == preset);
            if (presetMatch != null)
            {
                return presetMatch;
            }

            return GetFirstSelectableCollarPresetOption();
        }

        private CollarPresetOption GetFirstSelectableCollarPresetOption()
        {
            CollarPresetOption? firstSelectable = _collarPresetOptions.FirstOrDefault(option => option.IsSelectable);
            if (firstSelectable != null)
            {
                return firstSelectable;
            }

            return new CollarPresetOption(CollarPreset.None, "None");
        }

        private static IEnumerable<string> EnumerateDiscoveredCollarModelPaths()
        {
            string? directory = ResolveCollarModelsDirectory();
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                yield break;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(directory)
                    .Where(path =>
                        path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".stl", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CollarPresetCatalog] Failed to enumerate '{directory}': {ex.Message}");
                yield break;
            }

            foreach (string path in files)
            {
                yield return path;
            }
        }

        private static string? ResolveCollarModelsDirectory()
        {
            var candidates = new List<string>();
            string currentDirectory = Environment.CurrentDirectory;
            if (!string.IsNullOrWhiteSpace(currentDirectory))
            {
                candidates.Add(Path.Combine(currentDirectory, CollarModelsFolderName));
            }

            string baseDirectory = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDirectory))
            {
                string? probe = baseDirectory;
                for (int i = 0; i < 8 && !string.IsNullOrWhiteSpace(probe); i++)
                {
                    candidates.Add(Path.Combine(probe, CollarModelsFolderName));
                    probe = Directory.GetParent(probe)?.FullName;
                }
            }

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrWhiteSpace(desktop))
            {
                candidates.Add(Path.Combine(desktop, "KnobForge", CollarModelsFolderName));
            }

            foreach (string candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string BuildCollarModelDisplayName(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.StartsWith("Meshy_AI_", StringComparison.OrdinalIgnoreCase))
            {
                name = name["Meshy_AI_".Length..];
            }

            if (name.EndsWith("_generate", StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^"_generate".Length];
            }

            name = name.Replace('_', ' ');
            return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static string NormalizePathForCompare(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string trimmed = path.Trim();
            try
            {
                return Path.GetFullPath(trimmed).Replace('\\', '/');
            }
            catch
            {
                return trimmed.Replace('\\', '/');
            }
        }

        private static bool PathsEqual(string? left, string? right)
        {
            return string.Equals(
                NormalizePathForCompare(left),
                NormalizePathForCompare(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private sealed class CollarPresetOption
        {
            public CollarPresetOption(
                CollarPreset preset,
                string displayName,
                string? fixedImportedMeshPath = null,
                bool allowsCustomPathEntry = false,
                bool isSelectable = true)
            {
                Preset = preset;
                DisplayName = displayName;
                FixedImportedMeshPath = string.IsNullOrWhiteSpace(fixedImportedMeshPath)
                    ? null
                    : fixedImportedMeshPath;
                AllowsCustomPathEntry = allowsCustomPathEntry;
                IsSelectable = isSelectable;
            }

            public CollarPreset Preset { get; }

            public string DisplayName { get; }

            public string? FixedImportedMeshPath { get; }

            public bool AllowsCustomPathEntry { get; }

            public bool IsSelectable { get; }

            public static CollarPresetOption CreateGroupLabel(string displayName)
            {
                return new CollarPresetOption(
                    CollarPreset.None,
                    displayName,
                    fixedImportedMeshPath: null,
                    allowsCustomPathEntry: false,
                    isSelectable: false);
            }

            public string ResolveImportedMeshPath(string? customPath)
            {
                if (!IsSelectable || !CollarNode.IsImportedMeshPreset(Preset))
                {
                    return string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(FixedImportedMeshPath))
                {
                    return FixedImportedMeshPath;
                }

                return CollarNode.ResolveImportedMeshPath(Preset, customPath);
            }

            public override string ToString()
            {
                return DisplayName;
            }
        }
    }
}
