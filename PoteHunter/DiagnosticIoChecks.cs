using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PoteHunter;

internal static class DiagnosticIoChecks
{
    static void Require(bool value, string message)
    {
        if (!value) throw new Exception("Diagnostic IO: " + message);
    }

    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "PoteHunter-diagnostic-io-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var diagnostics = DiagnosticIo.BeginChecks(Path.Combine(root, "diagnostic-failures.jsonl"));
            AtomicReplacement(root);
            AppendRecovery(root);
            UnwritableDestinations(root);
            ProgrammingErrorsPropagate(root);
            ConnectionWording();
            ChestPersistenceRecovery(root);
            ChestSaveFailureRecovery(root);
            ChestBlockedLoadRecovery(root);
            InputCallbacksRemainIndependent(root);
            AuditSinkFailure(root);
            BoundedMetadata(root);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "diagnostic-io-checks.json"),
                JsonSerializer.Serialize(new
                {
                    Passed = true,
                    HardwareInputEmitted = false,
                    RealWindowsFileSharing = OperatingSystem.IsWindows(),
                    Checks = new[]
                    {
                        "reader sharing read/write but denying delete blocks atomic replacement without changing original JSON",
                        "bounded failed attempts leave no temporary files and the next unlocked write recovers",
                        "delete-sharing reader permits replacement and retains its own original complete snapshot",
                        "exclusive append lock preserves existing history and unlock permits later append",
                        "directory destination, blocked parent and read-only destination expose nonfatal diagnostic failures",
                        "invalid path programming errors propagate rather than become diagnostic IO failures",
                        "automatic signature discovery never claims the client changed",
                        "denied chest persistence preserves in-memory sightings and later retries without a new sighting",
                        "blocked chest save retains old disk history and persists old plus new sightings after unlock",
                        "blocked initial chest load merges unread older sightings with newer same-ID observations before saving",
                        "real trace append failure leaves synthetic input callbacks running; required input failure propagates",
                        "an independently locked error-audit sink cannot throw or erase failure metadata",
                        "diagnostic metadata retains original exception code/stage and bounded warning/history counts"
                    }
                }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            string fullRoot = Path.GetFullPath(root);
            string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(fullRoot).StartsWith("PoteHunter-diagnostic-io-check-", StringComparison.Ordinal))
                throw new InvalidOperationException("Diagnostic IO check cleanup escaped its owned temporary directory.");
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }

    static void AtomicReplacement(string root)
    {
        string folder = Path.Combine(root, "atomic");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "live-status.json");
        const string before = "{\"Sequence\":1,\"Working\":true,\"HP\":15853}";
        const string after = "{\"Sequence\":2,\"Working\":true,\"HP\":16610}";
        Require(DiagnosticIo.TryAtomicWrite("check snapshot initial", path, before), "initial diagnostic snapshot failed");
        if (OperatingSystem.IsWindows())
        {
            using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var elapsed = Stopwatch.StartNew();
                var baseline = DiagnosticIo.Snapshot();
                for (int attempt = 0; attempt < 3; attempt++)
                    Require(!DiagnosticIo.TryAtomicWrite("check snapshot replacement blocked", path, after),
                        "a reader denying delete did not block replacement");
                Require(elapsed.Elapsed < TimeSpan.FromSeconds(3), "blocked diagnostic writes waited beyond a bounded tick-sized attempt");
                var failed = DiagnosticIo.Snapshot();
                Require(failed.FailureCount - baseline.FailureCount == 3 &&
                    failed.RetryCount - baseline.RetryCount <= 6 &&
                    failed.CleanupFailureCount == baseline.CleanupFailureCount,
                    "failed replacements retried unboundedly, lost failure accounting or failed temporary cleanup");
                Require(File.ReadAllText(path) == before, "blocked replacement corrupted the old snapshot");
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                Require(json.RootElement.GetProperty("Sequence").GetInt32() == 1, "old snapshot became partial or invalid JSON");
                Require(Directory.GetFiles(folder).SequenceEqual(new[] { path }), "failed replacements left temporary files");
                AssertFailure("check snapshot replacement blocked", path);
            }
            Require(DiagnosticIo.TryAtomicWrite("check snapshot replacement blocked", path, after),
                "next diagnostic write did not recover after the reader closed");
            Require(File.ReadAllText(path) == after, "recovered snapshot was not atomically replaced");
            Require(!DiagnosticIo.Snapshot().ActiveWarnings.Any(failure =>
                failure.Operation == "check snapshot replacement blocked" && failure.Path == Path.GetFullPath(path)),
                "a successful later snapshot retained its stale persistence warning");

            using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                Require(DiagnosticIo.TryAtomicWrite("check snapshot delete sharing", path, before),
                    "reader granting FileShare.ReadWrite|Delete (7) prevented replacement: " +
                    JsonSerializer.Serialize(DiagnosticIo.Snapshot().LastFailure));
                Require(File.ReadAllText(path) == before, "delete-sharing replacement did not expose the new snapshot");
                using var original = new StreamReader(reader, Encoding.UTF8, true, 1024, leaveOpen: true);
                Require(original.ReadToEnd() == after, "replacement altered the old reader's complete snapshot");
            }
        }
        else
        {
            Require(DiagnosticIo.TryAtomicWrite("check snapshot replacement", path, after), "ordinary replacement failed");
            Require(File.ReadAllText(path) == after, "ordinary replacement content mismatch");
        }
        Require(Directory.GetFiles(folder).SequenceEqual(new[] { path }), "successful replacement left temporary files");
    }

    static void AppendRecovery(string root)
    {
        string path = Path.Combine(root, "trace.jsonl");
        const string before = "{\"Stage\":\"before\"}\n";
        const string after = "{\"Stage\":\"after\"}\n";
        Require(DiagnosticIo.TryAppend("check trace append", path, before), "initial trace append failed");
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Require(!DiagnosticIo.TryAppend("check trace append", path, after), "exclusive trace reader did not block append");
            using var original = new StreamReader(reader, Encoding.UTF8, true, 1024, leaveOpen: true);
            Require(original.ReadToEnd() == before, "failed trace append changed its original history");
            AssertFailure("check trace append", path);
        }
        Require(DiagnosticIo.TryAppend("check trace append", path, after), "trace append did not recover after unlock");
        Require(File.ReadAllText(path) == before + after, "append recovery truncated or duplicated history");
        foreach (string line in File.ReadAllLines(path))
            using (JsonDocument.Parse(line)) { }
    }

    static void UnwritableDestinations(string root)
    {
        string directory = Path.Combine(root, "directory-destination");
        Directory.CreateDirectory(directory);
        Require(!DiagnosticIo.TryAtomicWrite("check directory snapshot", directory, "{}"), "directory accepted an atomic file replacement");
        AssertFailure("check directory snapshot", directory);
        Require(!DiagnosticIo.TryAppend("check directory append", directory, "{}\n"), "directory accepted a trace append");
        AssertFailure("check directory append", directory);
        Require(Directory.GetFiles(root).All(file => !Path.GetFileName(file).StartsWith("directory-destination", StringComparison.Ordinal)),
            "directory write failure left temporary files");

        string parent = Path.Combine(root, "blocked-parent");
        File.WriteAllText(parent, "ordinary file, never a directory");
        string child = Path.Combine(parent, "snapshot.json");
        Require(!DiagnosticIo.TryAtomicWrite("check blocked parent", child, "{}"), "file parent accepted a nested diagnostic path");
        AssertFailure("check blocked parent", child);
        Require(File.ReadAllText(parent) == "ordinary file, never a directory", "denied parent was modified");

        if (OperatingSystem.IsWindows())
        {
            string readOnly = Path.Combine(root, "read-only.json");
            File.WriteAllText(readOnly, "{\"Original\":true}");
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            try
            {
                Require(!DiagnosticIo.TryAtomicWrite("check read-only snapshot", readOnly, "{\"Original\":false}"),
                    "read-only diagnostic destination was overwritten");
                AssertFailure("check read-only snapshot", readOnly);
                Require(File.ReadAllText(readOnly) == "{\"Original\":true}", "read-only failure changed the original");
            }
            finally { File.SetAttributes(readOnly, FileAttributes.Normal); }
            Require(DiagnosticIo.TryAtomicWrite("check read-only snapshot", readOnly, "{\"Original\":false}"),
                "read-only failure did not recover after permissions changed");
        }
    }

    static void ProgrammingErrorsPropagate(string root)
    {
        string malformed = Path.Combine(root, "invalid\0path.json");
        bool atomicRejected = false, appendRejected = false;
        try { DiagnosticIo.TryAtomicWrite("check invalid atomic argument", malformed, "{}"); }
        catch (ArgumentException) { atomicRejected = true; }
        try { DiagnosticIo.TryAppend("check invalid append argument", malformed, "{}\n"); }
        catch (ArgumentException) { appendRejected = true; }
        Require(atomicRejected && appendRejected, "non-IO programming errors were silently treated as optional diagnostics");
    }

    static void ConnectionWording()
    {
        string automatic = HunterForm.ConnectionStatus(true), known = HunterForm.ConnectionStatus(false);
        Require(automatic.Contains("verified automatic client layout", StringComparison.Ordinal) &&
            !automatic.Contains("updated", StringComparison.OrdinalIgnoreCase) &&
            !automatic.Contains("changed", StringComparison.OrdinalIgnoreCase) &&
            known.StartsWith("Connected. ", StringComparison.Ordinal) &&
            automatic.EndsWith("Press F8 or Start to calibrate and hunt.", StringComparison.Ordinal) &&
            known.EndsWith("Press F8 or Start to calibrate and hunt.", StringComparison.Ordinal),
            "reconnecting through automatic discovery falsely reported a changed client or omitted calibration");
    }

    static void ChestPersistenceRecovery(string root)
    {
        string path = Path.Combine(root, "chests.json");
        Directory.CreateDirectory(path);
        long now = 0;
        var catalog = new ChestCatalog(path, () => now);
        var chest = new Entity(1, 0x80000bc0, "Treasure Box", new(4, 5), 0, Model: "MON_luckybag.GCMDS");
        catalog.Observe(12, [chest]);
        Require(catalog.ForZone(12).Single().Position == chest.Position,
            "optional persistence failure discarded the in-memory chest sighting");
        Directory.Delete(path);
        now += 2100;
        catalog.Observe(12, []);
        Require(File.Exists(path), "an unchanged later scene did not retry pending chest persistence");
        Require(new ChestCatalog(path).ForZone(12).Single().Position == chest.Position,
            "recovered chest persistence did not retain the original sighting");
    }

    static void InputCallbacksRemainIndependent(string root)
    {
        string path = Path.Combine(root, "callback-trace.jsonl");
        File.WriteAllText(path, "original\n");
        var emitted = new List<bool>();
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var held = new HeldInputs<bool>((_, down) =>
            {
                DiagnosticIo.TryAppend("check input trace callback", path, "event\n");
                emitted.Add(down); // offline callback only; no Input/native API
            });
            held.Set(false, true);
            held.ReleaseAll();
            Require(emitted.SequenceEqual(new[] { true, false }),
                "an optional trace failure prevented an independent input/release callback");
            AssertFailure("check input trace callback", path);
        }
        Require(File.ReadAllText(path) == "original\n", "callback trace lock changed prior history");
        var requiredFailure = new InvalidOperationException("required input fixture failure");
        var broken = new HeldInputs<bool>((_, _) => throw requiredFailure);
        bool stopped = false;
        try { broken.Set(false, true); }
        catch (InvalidOperationException error) when (ReferenceEquals(error, requiredFailure)) { stopped = true; }
        Require(stopped, "a required input callback fault was swallowed after diagnostic IO failure");
    }

    static void ChestSaveFailureRecovery(string root)
    {
        string path = Path.Combine(root, "chest-save-lock.json");
        var older = new TrackedChest(12, 10, new(2, 3), "older sighting", DateTime.UnixEpoch, DateTime.UnixEpoch);
        string original = JsonSerializer.Serialize(new[] { older });
        File.WriteAllText(path, original);
        long now = 0;
        var catalog = new ChestCatalog(path, () => now);
        var observed = new Entity(11, 0x80000bc0, "Treasure Box", new(8, 9), 0, Model: "MON_luckybag.GCMDS");
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            catalog.Observe(12, [observed]);
            Require(catalog.ForZone(12).Count == 2 && catalog.ForZone(12).Any(chest =>
                chest.Id == observed.Id && chest.Position == observed.Position),
                "blocked chest save lost an old or newly observed in-memory sighting");
            Require(File.ReadAllText(path) == original, "blocked chest save overwrote its old disk history");
            AssertFailure("save chest sightings", path);
        }
        now += 2100;
        catalog.Observe(12, []);
        var restored = new ChestCatalog(path).ForZone(12);
        Require(restored.Count == 2 && restored.Contains(older) &&
            restored.Any(chest => chest.Id == observed.Id && chest.Position == observed.Position),
            "later unlocked save failed to persist both old and new chest sightings");
    }

    static void ChestBlockedLoadRecovery(string root)
    {
        string path = Path.Combine(root, "chest-load-lock.json");
        var fresh = new Entity(21, 0x80000bc0, "Treasure Box", new(8, 9), 0, Model: "MON_luckybag.GCMDS");
        var unseen = new TrackedChest(12, 20, new(2, 3), "unseen older sighting", DateTime.UnixEpoch, DateTime.UnixEpoch);
        var oldVisible = new TrackedChest(12, fresh.Id, new(4, 5), "old visible sighting", DateTime.UnixEpoch, DateTime.UnixEpoch);
        string original = JsonSerializer.Serialize(new[] { unseen, oldVisible });
        File.WriteAllText(path, original);
        long now = 0;
        ChestCatalog catalog;
        using (var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            catalog = new ChestCatalog(path, () => now);
            catalog.Observe(12, [fresh]);
            Require(catalog.ForZone(12).Single().Position == fresh.Position,
                "blocked initial load discarded a new in-memory sighting");
            now += 2100;
            catalog.Observe(12, []); // still locked: a due retry must never save over unread history
            using var originalReader = new StreamReader(reader, Encoding.UTF8, true, 1024, leaveOpen: true);
            Require(originalReader.ReadToEnd() == original, "blocked initial load overwrote unread old sightings");
            AssertFailure("load chest sightings", path);
        }
        now += 2100;
        catalog.Observe(12, []);
        var merged = catalog.ForZone(12);
        Require(merged.Count == 2 && merged.Contains(unseen), "recovered load failed to merge an unseen old sighting");
        var updated = merged.Single(chest => chest.Id == fresh.Id);
        Require(updated.Position == fresh.Position && updated.Label == Targeting.ChestLabel(fresh) &&
            updated.FirstSeenUtc == DateTime.UnixEpoch && updated.LastSeenUtc > DateTime.UnixEpoch,
            "recovered load replaced the newer same-ID observation or lost its earlier first-seen time");
        var restored = new ChestCatalog(path).ForZone(12);
        Require(restored.Count == 2 && restored.Contains(unseen) &&
            restored.Single(chest => chest.Id == fresh.Id) == updated,
            "merged chest history was not persisted after load recovery");
    }

    static void BoundedMetadata(string root)
    {
        const string operation = "check original exception metadata";
        string path = Path.Combine(root, "metadata.json");
        var original = new IOException("original IO fixture message");
        original.Data["TickOperation"] = "Reading player health fixture";
        DiagnosticIo.RecordFailure(operation, path, original);
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(DiagnosticIo.Snapshot())))
        {
            var failure = json.RootElement.GetProperty("LastFailure");
            Require(failure.GetProperty("Operation").GetString() == operation &&
                failure.GetProperty("Path").GetString() == Path.GetFullPath(path) &&
                failure.GetProperty("HResult").GetInt32() == original.HResult &&
                failure.GetProperty("Error").GetString() == original.Message &&
                failure.GetProperty("TickOperation").GetString() == "Reading player health fixture",
                "recorded IO fault lost original operation/path/code/error/stage");
        }
        for (int index = 0; index < 40; index++)
            DiagnosticIo.RecordFailure("check bounded history " + index, Path.Combine(root, index + ".json"),
                new IOException("bounded fixture " + index));
        using var bounded = JsonDocument.Parse(JsonSerializer.Serialize(DiagnosticIo.Snapshot()));
        Require(bounded.RootElement.GetProperty("RecentFailures").GetArrayLength() <= 24 &&
            bounded.RootElement.GetProperty("ActiveWarnings").GetArrayLength() <= 16,
            "repeated optional diagnostics grew unbounded history or visible warnings");
        Require(bounded.RootElement.GetProperty("FailureCount").GetInt64() >= 40,
            "bounded history incorrectly discarded total failure accounting");
    }

    static void AuditSinkFailure(string root)
    {
        string audit = Path.Combine(root, "locked-error-audit.jsonl");
        File.WriteAllText(audit, "original audit\n");
        using var scope = DiagnosticIo.BeginChecks(audit);
        string blocked = Path.Combine(root, "audit-fixture-directory");
        Directory.CreateDirectory(blocked);
        using (var lockFile = new FileStream(audit, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Require(!DiagnosticIo.TryAtomicWrite("check locked audit", blocked, "{}"),
                "unwritable diagnostic fixture unexpectedly succeeded");
            AssertFailure("check locked audit", blocked);
            Require(SpinWait.SpinUntil(()=>DiagnosticIo.Snapshot().AuditWriteFailureCount>0,2000),
                "error sink failure was not bounded/nonfatal or did not retain its own failure count");
            using var original = new StreamReader(lockFile, Encoding.UTF8, true, 1024, leaveOpen: true);
            Require(original.ReadToEnd() == "original audit\n", "failed error audit changed previous history");
        }
        DiagnosticIo.RecordFailure("check audit recovery", blocked, new IOException("later audit fixture"));
        Require(DiagnosticPersistence.Drain(TimeSpan.FromSeconds(5)) && DiagnosticIo.Snapshot().AuditRecordCount == 2 &&
            File.ReadAllText(audit).StartsWith("original audit\n", StringComparison.Ordinal),
            "later unlocked audit did not recover while preserving its existing history");
    }

    static void AssertFailure(string operation, string path)
    {
        // The public diagnostic snapshot is also the live-status contract. Parse
        // its serialized form so the check verifies the exported metadata.
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(DiagnosticIo.Snapshot()));
        Require(ContainsFailure(json.RootElement, operation, Path.GetFullPath(path)),
            "exported failure omitted exact operation/path/type/HResult/error for " + operation);
        Require(!string.IsNullOrWhiteSpace(DiagnosticIo.StatusSummary), "diagnostic failure did not expose a visible status summary");
    }

    static bool ContainsFailure(JsonElement node, string operation, string path)
    {
        if (node.ValueKind == JsonValueKind.Array)
            return node.EnumerateArray().Any(item => ContainsFailure(item, operation, path));
        if (node.ValueKind != JsonValueKind.Object) return false;
        if (node.TryGetProperty("Operation", out var op) && op.GetString() == operation &&
            node.TryGetProperty("Path", out var location) && location.GetString() == path)
        {
            bool type = node.TryGetProperty("ExceptionType", out var exception) &&
                exception.GetString() is "System.IO.IOException" or "System.UnauthorizedAccessException";
            bool code = node.TryGetProperty("HResult", out var hresult) && hresult.TryGetInt32(out int result) && result != 0;
            bool error = node.TryGetProperty("Error", out var message) && !string.IsNullOrWhiteSpace(message.GetString());
            return type && code && error;
        }
        return node.EnumerateObject().Any(property => ContainsFailure(property.Value, operation, path));
    }
}
