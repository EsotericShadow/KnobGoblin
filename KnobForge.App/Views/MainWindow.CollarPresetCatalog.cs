using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
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
        private const string CollarThumbnailsFolderName = "collar_thumbnails";
        private static readonly string[] UpsideDownImportedCollarTokens =
        {
            "artnouveauvines",
            "greekkeymeandre",
            "indentedindices",
            "laurelwreath",
            "wavecollar"
        };
        private FileSystemWatcher? _collarModelsWatcher;
        private string? _collarModelsWatcherDirectory;
        private DispatcherTimer? _collarModelsWatcherDebounceTimer;
        private bool _collarModelsWatcherDisposed;

        private void RebuildCollarPresetOptions()
        {
            if (_collarPresetCombo == null)
            {
                return;
            }

            CollarPresetOption previousSelection = ResolveSelectedCollarPresetOption();
            string previousResolvedPath = previousSelection.ResolveImportedMeshPath(_collarMeshPathTextBox?.Text);
            string? libraryDirectory = ResolveCollarModelsDirectory();
            string? thumbnailsDirectory = ResolveCollarThumbnailsDirectory(libraryDirectory);
            ConfigureCollarModelsWatcher(libraryDirectory);

            DisposeCollarPresetOptionThumbnails(_collarPresetOptions);
            _collarPresetOptions.Clear();
            _collarPresetOptions.Add(CollarPresetOption.CreateGroupLabel("Built-in"));
            _collarPresetOptions.Add(new CollarPresetOption(CollarPreset.None, "  None"));
            _collarPresetOptions.Add(new CollarPresetOption(CollarPreset.SnakeOuroboros, "  Snake Ouroboros (Procedural)"));
            _collarPresetOptions.Add(new CollarPresetOption(
                CollarPreset.MeshyOuroborosRing,
                "  Ouroboros Ring (Generated)",
                CollarNode.ResolveImportedMeshPath(CollarPreset.MeshyOuroborosRing, null),
                LoadCollarThumbnail(CollarNode.ResolveImportedMeshPath(CollarPreset.MeshyOuroborosRing, null), thumbnailsDirectory)));
            _collarPresetOptions.Add(new CollarPresetOption(
                CollarPreset.MeshyOuroborosRingTextured,
                "  Ouroboros Ring (Textured)",
                CollarNode.ResolveImportedMeshPath(CollarPreset.MeshyOuroborosRingTextured, null),
                LoadCollarThumbnail(CollarNode.ResolveImportedMeshPath(CollarPreset.MeshyOuroborosRingTextured, null), thumbnailsDirectory)));

            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CollarPresetOption option in _collarPresetOptions)
            {
                if (!string.IsNullOrWhiteSpace(option.FixedImportedMeshPath))
                {
                    seenPaths.Add(NormalizePathForCompare(option.FixedImportedMeshPath));
                }
            }

            List<string> discoveredModels = EnumerateDiscoveredCollarModelPaths(libraryDirectory).ToList();
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
                (bool mirrorX, bool mirrorY, bool mirrorZ)? mirrorDefaults = ResolveImportedMirrorDefaults(modelPath);
                _collarPresetOptions.Add(new CollarPresetOption(
                    CollarPreset.ImportedStl,
                    $"  {BuildCollarModelDisplayName(modelPath)}",
                    modelPath,
                    LoadCollarThumbnail(modelPath, thumbnailsDirectory),
                    defaultMirrorX: mirrorDefaults?.mirrorX,
                    defaultMirrorY: mirrorDefaults?.mirrorY,
                    defaultMirrorZ: mirrorDefaults?.mirrorZ,
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

        private void InitializeCollarLibraryHotReload()
        {
            _collarModelsWatcherDisposed = false;
            ConfigureCollarModelsWatcher(ResolveCollarModelsDirectory());
        }

        private void DisposeCollarLibraryHotReload()
        {
            _collarModelsWatcherDisposed = true;

            if (_collarModelsWatcherDebounceTimer != null)
            {
                _collarModelsWatcherDebounceTimer.Stop();
                _collarModelsWatcherDebounceTimer = null;
            }

            DisposeCollarModelsWatcher();
            DisposeCollarPresetOptionThumbnails(_collarPresetOptions);
        }

        private void RefreshCollarLibraryPreservingSelection(bool fromWatcher)
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

            if (_collarModelsWatcherDisposed)
            {
                return;
            }

            if (fromWatcher)
            {
                SetCollarMeshPathStatus(
                    _discoveredCollarLibraryCount > 0
                        ? $"Path status: auto-refreshed ({_discoveredCollarLibraryCount} model{(_discoveredCollarLibraryCount == 1 ? string.Empty : "s")} found)."
                        : "Path status: auto-refreshed (no models found in collar_models).",
                    _discoveredCollarLibraryCount > 0 ? Brushes.LightGreen : Brushes.LightGray);
                return;
            }

            SetCollarMeshPathStatus(
                _discoveredCollarLibraryCount > 0
                    ? $"Path status: library refreshed ({_discoveredCollarLibraryCount} model{(_discoveredCollarLibraryCount == 1 ? string.Empty : "s")} found)."
                    : "Path status: library refreshed (no models found in collar_models).",
                _discoveredCollarLibraryCount > 0 ? Brushes.LightGreen : Brushes.LightGray);
        }

        private void ConfigureCollarModelsWatcher(string? directory)
        {
            if (_collarModelsWatcherDisposed)
            {
                return;
            }

            string normalized = NormalizePathForCompare(directory);
            if (!string.IsNullOrWhiteSpace(normalized) &&
                string.Equals(_collarModelsWatcherDirectory, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            DisposeCollarModelsWatcher();
            _collarModelsWatcherDirectory = string.Empty;

            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            try
            {
                _collarModelsWatcher = new FileSystemWatcher(directory)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.Size,
                    Filter = "*.*",
                    EnableRaisingEvents = true
                };

                _collarModelsWatcher.Created += OnCollarModelsWatcherChanged;
                _collarModelsWatcher.Changed += OnCollarModelsWatcherChanged;
                _collarModelsWatcher.Deleted += OnCollarModelsWatcherChanged;
                _collarModelsWatcher.Renamed += OnCollarModelsWatcherRenamed;
                _collarModelsWatcherDirectory = normalized;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CollarPresetCatalog] Failed to watch '{directory}': {ex.Message}");
                DisposeCollarModelsWatcher();
            }
        }

        private void DisposeCollarModelsWatcher()
        {
            if (_collarModelsWatcher != null)
            {
                _collarModelsWatcher.EnableRaisingEvents = false;
                _collarModelsWatcher.Created -= OnCollarModelsWatcherChanged;
                _collarModelsWatcher.Changed -= OnCollarModelsWatcherChanged;
                _collarModelsWatcher.Deleted -= OnCollarModelsWatcherChanged;
                _collarModelsWatcher.Renamed -= OnCollarModelsWatcherRenamed;
                _collarModelsWatcher.Dispose();
                _collarModelsWatcher = null;
            }
        }

        private void OnCollarModelsWatcherChanged(object sender, FileSystemEventArgs e)
        {
            if (!IsSupportedCollarAssetPath(e.FullPath))
            {
                return;
            }

            QueueCollarLibraryAutoRefresh();
        }

        private void OnCollarModelsWatcherRenamed(object sender, RenamedEventArgs e)
        {
            if (!IsSupportedCollarAssetPath(e.OldFullPath) &&
                !IsSupportedCollarAssetPath(e.FullPath))
            {
                return;
            }

            QueueCollarLibraryAutoRefresh();
        }

        private void QueueCollarLibraryAutoRefresh()
        {
            if (_collarModelsWatcherDisposed)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (_collarModelsWatcherDisposed)
                {
                    return;
                }

                _collarModelsWatcherDebounceTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) =>
                {
                    if (_collarModelsWatcherDebounceTimer != null)
                    {
                        _collarModelsWatcherDebounceTimer.Stop();
                    }

                    RefreshCollarLibraryPreservingSelection(fromWatcher: true);
                });
                _collarModelsWatcherDebounceTimer.Stop();
                _collarModelsWatcherDebounceTimer.Start();
            }, DispatcherPriority.Background);
        }

        private static bool IsSupportedCollarAssetPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".stl", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!IsSupportedThumbnailExtension(path))
            {
                return false;
            }

            string? directoryName = Path.GetFileName(Path.GetDirectoryName(path));
            return string.Equals(directoryName, CollarThumbnailsFolderName, StringComparison.OrdinalIgnoreCase);
        }

        private void OnRefreshCollarLibraryButtonClicked(object? sender, RoutedEventArgs e)
        {
            RefreshCollarLibraryPreservingSelection(fromWatcher: false);
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

        private static IEnumerable<string> EnumerateDiscoveredCollarModelPaths(string? directory = null)
        {
            directory ??= ResolveCollarModelsDirectory();
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

        private static string? ResolveCollarThumbnailsDirectory(string? modelsDirectory)
        {
            if (string.IsNullOrWhiteSpace(modelsDirectory))
            {
                return null;
            }

            string directory = Path.Combine(modelsDirectory, CollarThumbnailsFolderName);
            return Directory.Exists(directory) ? directory : null;
        }

        private static bool IsSupportedThumbnailExtension(string path)
        {
            return path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildLookupToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new string(value
                .Trim()
                .ToLowerInvariant()
                .Where(ch => char.IsLetterOrDigit(ch))
                .ToArray());
        }

        private static (bool mirrorX, bool mirrorY, bool mirrorZ)? ResolveImportedMirrorDefaults(string? modelPath)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                return null;
            }

            string token = BuildLookupToken(Path.GetFileNameWithoutExtension(modelPath));
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            foreach (string upsideDownToken in UpsideDownImportedCollarTokens)
            {
                if (token.Contains(upsideDownToken, StringComparison.Ordinal))
                {
                    return (mirrorX: false, mirrorY: false, mirrorZ: true);
                }
            }

            return null;
        }

        private static string? ResolveThumbnailPathForModel(string modelPath, string? thumbnailsDirectory)
        {
            if (string.IsNullOrWhiteSpace(thumbnailsDirectory) || !Directory.Exists(thumbnailsDirectory))
            {
                return null;
            }

            string modelName = Path.GetFileNameWithoutExtension(modelPath);
            if (string.IsNullOrWhiteSpace(modelName))
            {
                return null;
            }

            string[] directCandidates =
            {
                Path.Combine(thumbnailsDirectory, modelName + ".png"),
                Path.Combine(thumbnailsDirectory, modelName + ".jpg"),
                Path.Combine(thumbnailsDirectory, modelName + ".jpeg"),
                Path.Combine(thumbnailsDirectory, modelName + ".bmp"),
                Path.Combine(thumbnailsDirectory, modelName + ".webp")
            };

            foreach (string candidate in directCandidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            string modelToken = BuildLookupToken(modelName);
            if (string.IsNullOrWhiteSpace(modelToken))
            {
                return null;
            }

            try
            {
                HashSet<string> modelLookupTokens = BuildThumbnailLookupTokens(modelName);
                if (modelLookupTokens.Count == 0)
                {
                    return null;
                }

                string? containsMatch = null;
                foreach (string path in Directory.GetFiles(thumbnailsDirectory))
                {
                    if (!IsSupportedThumbnailExtension(path))
                    {
                        continue;
                    }

                    string thumbToken = BuildLookupToken(Path.GetFileNameWithoutExtension(path));
                    if (string.IsNullOrWhiteSpace(thumbToken))
                    {
                        continue;
                    }

                    if (modelLookupTokens.Contains(thumbToken))
                    {
                        return path;
                    }

                    foreach (string token in modelLookupTokens)
                    {
                        if (token.Contains(thumbToken, StringComparison.Ordinal) ||
                            thumbToken.Contains(token, StringComparison.Ordinal))
                        {
                            containsMatch ??= path;
                            break;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(containsMatch))
                {
                    return containsMatch;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CollarPresetCatalog] Failed to scan thumbnails in '{thumbnailsDirectory}': {ex.Message}");
            }

            return null;
        }

        private static HashSet<string> BuildThumbnailLookupTokens(string modelName)
        {
            var tokens = new HashSet<string>(StringComparer.Ordinal);

            void AddToken(string? value)
            {
                string token = BuildLookupToken(value ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    tokens.Add(token);
                }
            }

            AddToken(modelName);

            string trimmed = modelName;
            if (trimmed.StartsWith("Meshy_AI_", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed["Meshy_AI_".Length..];
                AddToken(trimmed);
            }

            if (trimmed.EndsWith("_generate", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[..^"_generate".Length];
                AddToken(trimmed);
            }

            int lastUnderscore = trimmed.LastIndexOf('_');
            if (lastUnderscore > 0 && lastUnderscore < trimmed.Length - 1)
            {
                string tail = trimmed[(lastUnderscore + 1)..];
                bool numericTail = tail.All(char.IsDigit);
                if (numericTail)
                {
                    string withoutNumericTail = trimmed[..lastUnderscore];
                    AddToken(withoutNumericTail);
                }
            }

            return tokens;
        }

        private static Bitmap? LoadCollarThumbnail(string? modelPath, string? thumbnailsDirectory)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                return null;
            }

            string? thumbnailPath = ResolveThumbnailPathForModel(modelPath, thumbnailsDirectory);
            if (string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath))
            {
                return null;
            }

            try
            {
                using FileStream stream = File.OpenRead(thumbnailPath);
                return new Bitmap(stream);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CollarPresetCatalog] Failed to load thumbnail '{thumbnailPath}': {ex.Message}");
                return null;
            }
        }

        private static void DisposeCollarPresetOptionThumbnails(IEnumerable<CollarPresetOption> options)
        {
            foreach (CollarPresetOption option in options)
            {
                option.DisposeThumbnail();
            }
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
                Bitmap? thumbnail = null,
                bool? defaultMirrorX = null,
                bool? defaultMirrorY = null,
                bool? defaultMirrorZ = null,
                bool allowsCustomPathEntry = false,
                bool isSelectable = true)
            {
                Preset = preset;
                DisplayName = displayName;
                FixedImportedMeshPath = string.IsNullOrWhiteSpace(fixedImportedMeshPath)
                    ? null
                    : fixedImportedMeshPath;
                Thumbnail = thumbnail;
                DefaultMirrorX = defaultMirrorX;
                DefaultMirrorY = defaultMirrorY;
                DefaultMirrorZ = defaultMirrorZ;
                AllowsCustomPathEntry = allowsCustomPathEntry;
                IsSelectable = isSelectable;
            }

            public CollarPreset Preset { get; }

            public string DisplayName { get; }

            public string? FixedImportedMeshPath { get; }

            public Bitmap? Thumbnail { get; }

            public bool HasThumbnail => Thumbnail != null;

            public bool? DefaultMirrorX { get; }

            public bool? DefaultMirrorY { get; }

            public bool? DefaultMirrorZ { get; }

            public bool AllowsCustomPathEntry { get; }

            public bool IsSelectable { get; }

            public static CollarPresetOption CreateGroupLabel(string displayName)
            {
                return new CollarPresetOption(
                    CollarPreset.None,
                    displayName,
                    fixedImportedMeshPath: null,
                    thumbnail: null,
                    allowsCustomPathEntry: false,
                    isSelectable: false);
            }

            public void DisposeThumbnail()
            {
                Thumbnail?.Dispose();
            }

            public bool TryGetImportedMirrorDefaults(out bool mirrorX, out bool mirrorY, out bool mirrorZ)
            {
                if (DefaultMirrorX.HasValue && DefaultMirrorY.HasValue && DefaultMirrorZ.HasValue)
                {
                    mirrorX = DefaultMirrorX.Value;
                    mirrorY = DefaultMirrorY.Value;
                    mirrorZ = DefaultMirrorZ.Value;
                    return true;
                }

                mirrorX = false;
                mirrorY = false;
                mirrorZ = false;
                return false;
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
