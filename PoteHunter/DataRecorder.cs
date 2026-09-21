using System.Text;
using System.Text.Json;

namespace PoteHunter;

public record ObjectTypeObservation(string Key, uint? PrototypeId, string Name, string Model, string DefinitionName, int? Level, int? Category, bool Priority);

public sealed class DataRecorder
{
    const long MaximumFileBytes = 20L * 1024 * 1024;
    readonly object gate = new();
    readonly string directory;
    readonly string observationsPath;
    readonly string catalogPath;
    readonly string catalogTemporaryPath;
    readonly Dictionary<string, CatalogEntry> catalog = new(StringComparer.Ordinal);
    long nextCaptureAt;
    long retryAt;
    DateTime lastCatalogFlushUtc = DateTime.MinValue;
    bool catalogDirty;
    bool catalogLoaded;

    public string? LastError { get; private set; }

    public DataRecorder(string directory)
    {
        this.directory = Path.GetFullPath(directory);
        observationsPath = Path.Combine(this.directory, "live-observations.jsonl");
        catalogPath = Path.Combine(this.directory, "observed-object-types.json");
        catalogTemporaryPath = catalogPath + ".tmp";
        try
        {
            Directory.CreateDirectory(this.directory);
            LoadCatalog();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            retryAt = Environment.TickCount64 + 5000;
        }
    }

    public bool Capture(object snapshot, IEnumerable<ObjectTypeObservation> observations)
    {
        lock (gate)
        {
            long now = Environment.TickCount64;
            if (now < retryAt || now < nextCaptureAt) return false;
            try
            {
                if (!catalogLoaded) LoadCatalog();
                DateTime seenUtc = DateTime.UtcNow;
                bool newType = UpdateCatalog(observations, seenUtc);
                if (catalogDirty && (newType || seenUtc - lastCatalogFlushUtc >= TimeSpan.FromSeconds(10)))
                    FlushCatalog(seenUtc);

                byte[] line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot) + Environment.NewLine);
                if (line.LongLength > MaximumFileBytes)
                    throw new InvalidOperationException("Observation snapshot exceeds the 20 MiB file limit.");
                RotateIfNeeded(line.LongLength);
                using (var stream = new FileStream(observationsPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                    stream.Write(line);

                nextCaptureAt = Environment.TickCount64 + 1000;
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                retryAt = Environment.TickCount64 + 5000;
                return false;
            }
        }
    }

    void LoadCatalog()
    {
        // A damaged/unreadable existing catalog is preserved until it can be read; never replace it with an empty one.
        if (File.Exists(catalogPath))
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, CatalogEntry>>(File.ReadAllText(catalogPath))
                ?? throw new InvalidOperationException("The existing object catalog could not be read.");
            foreach (var pair in saved) catalog[pair.Key] = pair.Value;
        }
        catalogLoaded = true;
    }

    bool UpdateCatalog(IEnumerable<ObjectTypeObservation> observations, DateTime seenUtc)
    {
        bool newType = false;
        foreach (var observation in observations)
        {
            if (string.IsNullOrWhiteSpace(observation.Key)) continue;
            if (catalog.TryGetValue(observation.Key, out var existing))
            {
                catalog[observation.Key] = existing with
                {
                    PrototypeId = observation.PrototypeId,
                    Name = observation.Name,
                    Model = observation.Model,
                    DefinitionName = observation.DefinitionName,
                    Level = observation.Level,
                    Category = observation.Category,
                    Priority = existing.Priority || observation.Priority,
                    LastSeenUtc = seenUtc,
                    Count = existing.Count + 1
                };
            }
            else
            {
                catalog[observation.Key] = new CatalogEntry(observation.PrototypeId, observation.Name, observation.Model,
                    observation.DefinitionName, observation.Level, observation.Category, observation.Priority, seenUtc, seenUtc, 1);
                newType = true;
            }
            catalogDirty = true;
        }
        return newType;
    }

    void FlushCatalog(DateTime nowUtc)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(catalogTemporaryPath, JsonSerializer.Serialize(catalog, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(catalogTemporaryPath, catalogPath, true);
        catalogDirty = false;
        lastCatalogFlushUtc = nowUtc;
    }

    void RotateIfNeeded(long incomingBytes)
    {
        Directory.CreateDirectory(directory);
        if (!File.Exists(observationsPath) || new FileInfo(observationsPath).Length + incomingBytes <= MaximumFileBytes) return;
        string oldest = observationsPath + ".4";
        if (File.Exists(oldest)) File.Delete(oldest);
        for (int index = 3; index >= 1; index--)
        {
            string source = observationsPath + "." + index;
            if (File.Exists(source)) File.Move(source, observationsPath + "." + (index + 1), true);
        }
        File.Move(observationsPath, observationsPath + ".1", true);
    }

    public static void SelfTest()
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), "PoteHunter-DataRecorder-" + Guid.NewGuid().ToString("N"));
        try
        {
            var observation = new ObjectTypeObservation("5983|npc_ag_container.gcmds", 5983, " ", "NPC_AG_Container.gcmds", "", 300, 11, true);
            var first = new DataRecorder(testDirectory);
            if (!first.Capture(new { Kind = "test", Number = 1 }, [observation])) throw new Exception(first.LastError ?? "First capture was throttled.");
            if (File.ReadLines(Path.Combine(testDirectory, "live-observations.jsonl")).Count() != 1) throw new Exception("Snapshot JSONL was not written exactly once.");
            var second = new DataRecorder(testDirectory);
            if (!second.Capture(new { Kind = "test", Number = 2 }, [observation])) throw new Exception(second.LastError ?? "Second capture was throttled.");
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(testDirectory, "observed-object-types.json")));
            if (document.RootElement.GetProperty(observation.Key).GetProperty("Count").GetInt64() != 2) throw new Exception("Catalog did not persist across recorder instances.");
            if (second.Capture(new { Kind = "throttled" }, [observation])) throw new Exception("Recorder did not throttle repeated frames.");
            using (var oversized = new FileStream(Path.Combine(testDirectory,"live-observations.jsonl"),FileMode.Open,FileAccess.Write)) oversized.SetLength(MaximumFileBytes);
            var third = new DataRecorder(testDirectory);
            if (!third.Capture(new { Kind = "rotation" }, [observation]) || !File.Exists(Path.Combine(testDirectory,"live-observations.jsonl.1"))) throw new Exception("Recorder did not rotate its full log.");
            string catalogFile = Path.Combine(testDirectory,"observed-object-types.json");
            File.WriteAllText(catalogFile,"damaged catalog fixture");
            var damaged = new DataRecorder(testDirectory);
            if (damaged.Capture(new { Kind = "must not overwrite" }, [observation]) || File.ReadAllText(catalogFile) != "damaged catalog fixture") throw new Exception("Damaged existing catalog was overwritten.");
        }
        finally
        {
            // Delete only the recorder's known fixture files, followed by its now-empty unique directory.
            foreach (string name in new[] { "live-observations.jsonl", "live-observations.jsonl.1", "observed-object-types.json", "observed-object-types.json.tmp" })
            {
                string file = Path.Combine(testDirectory,name); if (File.Exists(file)) File.Delete(file);
            }
            if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory);
        }
    }

    sealed record CatalogEntry(uint? PrototypeId, string Name, string Model, string DefinitionName, int? Level, int? Category,
        bool Priority, DateTime FirstSeenUtc, DateTime LastSeenUtc, long Count);
}
