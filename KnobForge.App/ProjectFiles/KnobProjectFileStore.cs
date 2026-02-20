using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KnobForge.App.ProjectFiles
{
    public static class KnobProjectFileStore
    {
        public const string FileExtension = ".knob";
        public const string FormatId = "knobforge.project.v1";
        private const int MaxRecentProjects = 64;
        private static readonly JsonSerializerOptions EnvelopeJsonOptions = new()
        {
            WriteIndented = true
        };

        private static readonly JsonSerializerOptions RecentJsonOptions = new()
        {
            WriteIndented = true
        };

        public static string GetDefaultProjectsDirectory()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrWhiteSpace(desktop))
            {
                return Path.Combine(desktop, "KnobForge", "Projects");
            }

            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(documents))
            {
                return Path.Combine(documents, "KnobForge", "Projects");
            }

            return Path.Combine(Directory.GetCurrentDirectory(), "Projects");
        }

        public static string EnsureDefaultProjectsDirectory()
        {
            string path = GetDefaultProjectsDirectory();
            Directory.CreateDirectory(path);
            return path;
        }

        public static bool TryLoadEnvelope(string path, out KnobProjectFileEnvelope? envelope, out string error)
        {
            envelope = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Project path is empty.";
                return false;
            }

            if (!File.Exists(path))
            {
                error = $"Project file not found: {path}";
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);
                envelope = JsonSerializer.Deserialize<KnobProjectFileEnvelope>(json, EnvelopeJsonOptions);
                if (envelope == null)
                {
                    error = "Project file is empty.";
                    return false;
                }

                if (!string.Equals(envelope.Format, FormatId, StringComparison.Ordinal))
                {
                    error = $"Unsupported project format: {envelope.Format}";
                    envelope = null;
                    return false;
                }

                if (string.IsNullOrWhiteSpace(envelope.SnapshotJson))
                {
                    error = "Project snapshot data is missing.";
                    envelope = null;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to read project file: {ex.Message}";
                return false;
            }
        }

        public static bool TrySaveEnvelope(string path, KnobProjectFileEnvelope envelope, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Project path is empty.";
                return false;
            }

            if (envelope == null)
            {
                error = "Project envelope is missing.";
                return false;
            }

            try
            {
                string directory = Path.GetDirectoryName(path) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                envelope.Format = FormatId;
                envelope.SavedUtc = DateTime.UtcNow;
                string json = JsonSerializer.Serialize(envelope, EnvelopeJsonOptions);
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Failed to save project file: {ex.Message}";
                return false;
            }
        }

        public static IReadOnlyList<KnobProjectLauncherEntry> GetLauncherEntries(int maxCount = 36)
        {
            var result = new List<KnobProjectLauncherEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in LoadRecentProjectPaths())
            {
                if (!TryAddLauncherEntry(path, result, seen))
                {
                    continue;
                }

                if (result.Count >= maxCount)
                {
                    return result;
                }
            }

            string projectsDirectory = EnsureDefaultProjectsDirectory();
            string[] files = Directory.Exists(projectsDirectory)
                ? Directory.GetFiles(projectsDirectory, $"*{FileExtension}", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();
            foreach (string path in files.OrderByDescending(File.GetLastWriteTimeUtc))
            {
                if (!TryAddLauncherEntry(path, result, seen))
                {
                    continue;
                }

                if (result.Count >= maxCount)
                {
                    break;
                }
            }

            return result;
        }

        public static void MarkRecentProject(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                return;
            }

            var paths = LoadRecentProjectPaths()
                .Where(existing => !string.Equals(existing, fullPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            paths.Insert(0, fullPath);
            if (paths.Count > MaxRecentProjects)
            {
                paths.RemoveRange(MaxRecentProjects, paths.Count - MaxRecentProjects);
            }

            SaveRecentProjectPaths(paths);
        }

        public static string BuildDefaultProjectPath(string displayName)
        {
            string safeName = SanitizeFileName(displayName);
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "Untitled";
            }

            string root = EnsureDefaultProjectsDirectory();
            string candidate = Path.Combine(root, safeName + FileExtension);
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            int suffix = 2;
            while (true)
            {
                string withSuffix = Path.Combine(root, $"{safeName} {suffix}{FileExtension}");
                if (!File.Exists(withSuffix))
                {
                    return withSuffix;
                }

                suffix++;
            }
        }

        public static Bitmap? TryDecodeThumbnail(string? base64Png)
        {
            if (string.IsNullOrWhiteSpace(base64Png))
            {
                return null;
            }

            try
            {
                byte[] bytes = Convert.FromBase64String(base64Png);
                using var stream = new MemoryStream(bytes);
                return new Bitmap(stream);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryAddLauncherEntry(
            string path,
            List<KnobProjectLauncherEntry> destination,
            HashSet<string> seen)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                return false;
            }

            if (!seen.Add(fullPath) || !File.Exists(fullPath))
            {
                return false;
            }

            if (!TryLoadEnvelope(fullPath, out KnobProjectFileEnvelope? envelope, out _))
            {
                return false;
            }

            string displayName = envelope?.DisplayName ?? string.Empty;
            destination.Add(new KnobProjectLauncherEntry
            {
                FilePath = fullPath,
                DisplayName = string.IsNullOrWhiteSpace(displayName)
                    ? Path.GetFileNameWithoutExtension(fullPath)
                    : displayName,
                SavedUtc = envelope?.SavedUtc ?? DateTime.MinValue,
                ThumbnailPngBase64 = envelope?.ThumbnailPngBase64
            });
            return true;
        }

        private static List<string> LoadRecentProjectPaths()
        {
            string manifestPath = GetRecentProjectsManifestPath();
            if (!File.Exists(manifestPath))
            {
                return new List<string>();
            }

            try
            {
                string json = File.ReadAllText(manifestPath);
                RecentProjectManifest? manifest = JsonSerializer.Deserialize<RecentProjectManifest>(json, RecentJsonOptions);
                if (manifest?.Paths == null)
                {
                    return new List<string>();
                }

                return manifest.Paths
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static void SaveRecentProjectPaths(IReadOnlyList<string> paths)
        {
            try
            {
                string manifestPath = GetRecentProjectsManifestPath();
                string directory = Path.GetDirectoryName(manifestPath) ?? EnsureDefaultProjectsDirectory();
                Directory.CreateDirectory(directory);
                var manifest = new RecentProjectManifest
                {
                    Paths = paths.ToList()
                };
                string json = JsonSerializer.Serialize(manifest, RecentJsonOptions);
                File.WriteAllText(manifestPath, json);
            }
            catch
            {
                // best effort only
            }
        }

        private static string GetRecentProjectsManifestPath()
        {
            return Path.Combine(EnsureDefaultProjectsDirectory(), "recent-projects.json");
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            char[] cleaned = value
                .Trim()
                .Select(ch => invalid.Contains(ch) ? '_' : ch)
                .ToArray();
            return new string(cleaned).Trim();
        }

        private sealed class RecentProjectManifest
        {
            public List<string> Paths { get; set; } = new();
        }
    }

    public sealed class KnobProjectFileEnvelope
    {
        public string Format { get; set; } = KnobProjectFileStore.FormatId;
        public string DisplayName { get; set; } = "Untitled";
        public DateTime SavedUtc { get; set; } = DateTime.UtcNow;
        public string SnapshotJson { get; set; } = string.Empty;
        public string? PaintStateJson { get; set; }
        public string? ViewportStateJson { get; set; }
        public string? ThumbnailPngBase64 { get; set; }
    }

    public sealed class KnobProjectLauncherEntry
    {
        public string FilePath { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public DateTime SavedUtc { get; set; }
        public string? ThumbnailPngBase64 { get; set; }
    }
}
