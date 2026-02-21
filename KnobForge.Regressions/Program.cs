using KnobForge.App.ProjectFiles;
using System.Text.Json;

internal static class Program
{
    private static int Main(string[] args)
    {
        var failures = new List<string>();
        string root = Path.Combine(Path.GetTempPath(), "knobforge-regressions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            RunTest("Envelope round-trip preserves payload", failures, () => EnvelopeRoundTripPreservesPayload(root));
            RunTest("Load rejects unsupported format", failures, () => LoadRejectsUnsupportedFormat(root));
            RunTest("Load rejects missing snapshot", failures, () => LoadRejectsMissingSnapshot(root));
            RunTest("Loaded snapshot exposes required sections", failures, () => LoadedSnapshotExposesRequiredSections(root));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort temp cleanup.
            }
        }

        if (failures.Count == 0)
        {
            Console.WriteLine("PASS: all save/load regressions passed.");
            return 0;
        }

        Console.Error.WriteLine($"FAIL: {failures.Count} regression(s) failed.");
        foreach (string failure in failures)
        {
            Console.Error.WriteLine($" - {failure}");
        }

        return 1;
    }

    private static void RunTest(string name, List<string> failures, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"[PASS] {name}");
        }
        catch (Exception ex)
        {
            failures.Add($"{name}: {ex.Message}");
            Console.Error.WriteLine($"[FAIL] {name}: {ex.Message}");
        }
    }

    private static void EnvelopeRoundTripPreservesPayload(string root)
    {
        string path = Path.Combine(root, "roundtrip.knob");
        string snapshotJson = BuildSnapshotJson();
        var source = new KnobProjectFileEnvelope
        {
            DisplayName = "Regression Fixture",
            SnapshotJson = snapshotJson,
            PaintStateJson = "{\"paint\":true}",
            ViewportStateJson = "{\"camera\":true}",
            ThumbnailPngBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 })
        };

        if (!KnobProjectFileStore.TrySaveEnvelope(path, source, out string saveError))
        {
            throw new InvalidOperationException($"save failed: {saveError}");
        }

        if (!KnobProjectFileStore.TryLoadEnvelope(path, out KnobProjectFileEnvelope? loaded, out string loadError) || loaded is null)
        {
            throw new InvalidOperationException($"load failed: {loadError}");
        }

        AssertEqual(KnobProjectFileStore.FormatId, loaded.Format, "format");
        AssertEqual(source.DisplayName, loaded.DisplayName, "display name");
        AssertEqual(source.SnapshotJson, loaded.SnapshotJson, "snapshot json");
        AssertEqual(source.PaintStateJson, loaded.PaintStateJson, "paint state");
        AssertEqual(source.ViewportStateJson, loaded.ViewportStateJson, "viewport state");
        AssertEqual(source.ThumbnailPngBase64, loaded.ThumbnailPngBase64, "thumbnail");
    }

    private static void LoadRejectsUnsupportedFormat(string root)
    {
        string path = Path.Combine(root, "invalid-format.knob");
        string json = """
                      {
                        "Format": "knobforge.project.v999",
                        "DisplayName": "Invalid",
                        "SavedUtc": "2026-01-01T00:00:00Z",
                        "SnapshotJson": "{}"
                      }
                      """;
        File.WriteAllText(path, json);

        if (KnobProjectFileStore.TryLoadEnvelope(path, out _, out string error))
        {
            throw new InvalidOperationException("load unexpectedly succeeded for unsupported format.");
        }

        if (!error.Contains("Unsupported project format", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"unexpected error: {error}");
        }
    }

    private static void LoadRejectsMissingSnapshot(string root)
    {
        string path = Path.Combine(root, "missing-snapshot.knob");
        string json = """
                      {
                        "Format": "knobforge.project.v1",
                        "DisplayName": "Missing Snapshot",
                        "SavedUtc": "2026-01-01T00:00:00Z",
                        "SnapshotJson": ""
                      }
                      """;
        File.WriteAllText(path, json);

        if (KnobProjectFileStore.TryLoadEnvelope(path, out _, out string error))
        {
            throw new InvalidOperationException("load unexpectedly succeeded for missing snapshot.");
        }

        if (!error.Contains("snapshot data is missing", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"unexpected error: {error}");
        }
    }

    private static void LoadedSnapshotExposesRequiredSections(string root)
    {
        string path = Path.Combine(root, "sections.knob");
        var envelope = new KnobProjectFileEnvelope
        {
            DisplayName = "Section Coverage",
            SnapshotJson = BuildSnapshotJson()
        };
        if (!KnobProjectFileStore.TrySaveEnvelope(path, envelope, out string saveError))
        {
            throw new InvalidOperationException($"save failed: {saveError}");
        }

        if (!KnobProjectFileStore.TryLoadEnvelope(path, out KnobProjectFileEnvelope? loaded, out string loadError) || loaded is null)
        {
            throw new InvalidOperationException($"load failed: {loadError}");
        }

        using JsonDocument doc = JsonDocument.Parse(loaded.SnapshotJson);
        AssertHasProperty(doc.RootElement, "Lighting");
        AssertHasProperty(doc.RootElement, "Environment");
        AssertHasProperty(doc.RootElement, "Shadows");
        AssertHasProperty(doc.RootElement, "Collar");

        JsonElement collar = doc.RootElement.GetProperty("Collar");
        AssertHasProperty(collar, "ImportedMirrorX");
        AssertHasProperty(collar, "ImportedMirrorY");
        AssertHasProperty(collar, "ImportedMirrorZ");
    }

    private static string BuildSnapshotJson()
    {
        var snapshot = new
        {
            Lighting = new
            {
                Mode = "Studio",
                Lights = new[]
                {
                    new { Name = "Key", Type = "Point", X = -0.4f, Y = 0.8f, Z = 1.1f, Intensity = 1.2f }
                }
            },
            Environment = new
            {
                Intensity = 0.62f,
                RoughnessMix = 0.35f
            },
            Shadows = new
            {
                Enabled = true,
                Strength = 0.68f,
                Softness = 0.34f
            },
            Collar = new
            {
                Enabled = true,
                Preset = "ImportedStl",
                ImportedMeshPath = "/tmp/collar_models/dragon.glb",
                ImportedScale = 1.09f,
                ImportedMirrorX = true,
                ImportedMirrorY = false,
                ImportedMirrorZ = true
            }
        };

        return JsonSerializer.Serialize(snapshot);
    }

    private static void AssertHasProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out _))
        {
            throw new InvalidOperationException($"missing required property '{name}'.");
        }
    }

    private static void AssertEqual(string? expected, string? actual, string fieldName)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{fieldName} mismatch.");
        }
    }
}
