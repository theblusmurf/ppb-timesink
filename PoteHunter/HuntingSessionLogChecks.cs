using System.Text;
using System.Text.Json;

namespace PoteHunter;

internal static class HuntingSessionLogChecks
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "PlayPoteBot-session-log-checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        DateTimeOffset now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        Entity self = new(100, 12, "Test, \"Hero\"\nSecond line", new(4, 5), 1, Generation: 1, Model: "hero");
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        try
        {
            var liveGlobal = HuntingSessionLog.Current;
            using var log = new HuntingSessionLog(root, () => now);
            Require(HuntingSessionLog.Current == liveGlobal, "Creating an offline logger changed the live logger.");
            log.UpdateLoot(Snapshot(true, 6154));
            log.Observe(1, self, 8, new(100, 100));
            log.BeginHunt("Solo", "Mimic, Tribal", 1, self, 8, new(100, 100));
            now = now.AddSeconds(1); log.Observe(1, self, 8, new(0, 100));
            log.Observe(1, self, 8, new(0, 100));
            log.Observe(1, self, 8, default); log.Observe(1, self, 8, default);
            log.EndHunt("Death stopped hunting"); log.EndHunt("Duplicate stop");
            now = now.AddSeconds(1); log.Observe(1, self with { Address = 200, Generation = 2 }, 8, new(50, 100));
            log.RecordRecovery("visual revive confirmed", new { Click = true });
            log.RecordRecovery("repair sequence completed", new { BeforeReturn = true });
            log.RecordRecovery("saved hunt anchor reached", new { AfterDeath = true });
            log.RecordRecovery("revival test completed", new { LivingHPConfirmed = true });
            log.RecordRecovery("client login password", new { Password = "must never appear" });
            var rows = Read(log.FilePath);
            var death = rows.Single(row => row["Event"] == "Death observed");
            var revival = rows.Single(row => row["Event"] == "Revival confirmed");
            Require(death["HuntId"] == revival["HuntId"] && revival["HuntId"].Length > 0, "A manual revival after stop lost the death's hunting session.");
            Require(death["DeathId"] == revival["DeathId"] && revival["Deaths"] == "1" && revival["Revivals"] == "1", "HP events duplicated or unknown HP discarded the pending death.");
            Require(rows.Count(row => row["Event"] == "Health unavailable") == 1 && rows.Count(row => row["Event"] == "Hunt ended") == 1, "Repeated gaps or stops were duplicated.");
            Require(revival["Character"] == self.Name && death["Gold"] == "6154" && death["WalletNet"] == "6154" && death["DetectedGoldEstimate"] == "2602", "CSV escaping or actual wallet net/estimate separation failed.");
            Require(rows.All(row => row["Cause"] == "Unknown") && !File.ReadAllText(log.FilePath).Contains("must never appear"), "Unverified death cause or excluded credentials entered the session log.");
            Require(rows.Single(row => row["Event"] == "Recovery: visual revive confirmed")["Revivals"] == "1", "A sent click was counted as another revival.");
            Require(rows.Where(row => row["Event"] is "Recovery: repair sequence completed" or "Recovery: saved hunt anchor reached" or "Recovery: revival test completed")
                .All(row => row["DeathId"] == death["DeathId"] && row["HuntId"] == death["HuntId"]), "Post-revival repair/return lost the confirmed cycle's identity.");

            log.Observe(1, self, 8, new(0, 100));
            log.ObservationGap("Disconnected"); log.ObservationGap("Disconnected");
            log.Observe(1, self, 8, new(100, 100));
            log.Observe(1, self, 8, new(0, 100));
            log.Observe(2, self with { Name = "Other" }, 8, new(100, 100));
            rows = Read(log.FilePath);
            Require(rows.Count(row => row["Event"] == "Revival confirmed") == 1 && rows.Count(row => row["Event"] == "Observation gap") == 1, "Disconnect or identity change invented a revival.");
            log.RecordLoot(Snapshot(false, 0), "Before loot reset");
            string initialLoot = Read(log.FilePath).Last()["LootSessionId"];
            log.RecordLoot(Snapshot(false, 0), "Timer reset");
            var timerReset = Read(log.FilePath).Last();
            Require(timerReset["LootSessionId"] == initialLoot && timerReset["Gold"] == "" && timerReset["GoldPerHour"] == "" && timerReset["WalletNet"] == "", "Unknown wallet became zero or timer reset changed loot identity.");
            log.RecordLoot(Snapshot(true, 0), "Loot reset");
            Require(Read(log.FilePath).Last()["LootSessionId"] != initialLoot, "Loot reset retained the prior loot run ID.");
            int beforeUpdates = Read(log.FilePath).Count;
            for (int n = 0; n < 20; n++) log.UpdateLoot(Snapshot(true, n));
            Require(Read(log.FilePath).Count == beforeUpdates, "Per-tick loot updates generated CSV snapshot events.");
            log.BeginHunt("Healer", "Party", 2, self with { Name = "Other" }, 8, new(100, 100));
            log.RecordRecovery("repair sequence completed", new { Test = true });
            Require(Read(log.FilePath).Last()["DeathId"] == "", "An unrelated new hunting session inherited a previous death cycle.");
            log.Dispose(); log.Dispose(); log.EndHunt("After disposal");
            rows = Read(log.FilePath);
            Require(rows.Count(row => row["Event"] == "Application ended") == 1 && rows.Count(row => row["Event"] == "Hunt started") == 2 && rows.Count(row => row["Event"] == "Hunt ended") == 2, "Closing the application duplicated or omitted session boundaries.");
            Require(rows.All(row => double.Parse(row["ApplicationElapsedSeconds"], System.Globalization.CultureInfo.InvariantCulture) >= 0), "A zero-duration session produced invalid elapsed time.");
            using var alreadyDead = new HuntingSessionLog(root, () => now);
            alreadyDead.Observe(1, self, 8, new(0, 100)); alreadyDead.Observe(1, self, 8, new(0, 100));
            alreadyDead.Observe(1, self, 8, new(100, 100));
            var baselineRows = Read(alreadyDead.FilePath);
            Require(baselineRows.Count(row => row["Event"] == "Already dead on connection") == 1 && baselineRows.All(row => row["Deaths"] == "0") && baselineRows.Last()["Revivals"] == "1", "Initial dead baseline invented a new death or failed to record subsequent living HP.");

            // An append lock is temporary; once released, existing history and
            // the complete header must survive, and failure must not stop HP tracking.
            using var appendRecovery = new HuntingSessionLog(root, () => now);
            appendRecovery.Observe(1, self, 8, new(100, 100));
            using (var exclusive = File.Open(appendRecovery.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                appendRecovery.Observe(1, self, 8, new(0, 100));
                Require(appendRecovery.LastError != null, "A locked append path did not expose a nonfatal error.");
            }
            appendRecovery.Observe(1, self, 8, new(100, 100));
            Require(appendRecovery.LastError == null && Read(appendRecovery.FilePath).Last()["Revivals"] == "1", "Append failure did not recover cleanly or lost the pending HP transition.");

            using var laterHunt = new HuntingSessionLog(root, () => now);
            laterHunt.BeginHunt("Solo", "Mimic", 1, self, 8, new(100, 100));
            laterHunt.Observe(1, self, 8, new(0, 100)); laterHunt.Observe(1, self, 8, new(100, 100));
            laterHunt.EndHunt("Stopped"); laterHunt.BeginHunt("Solo", "Tribal", 1, self, 8, new(100, 100));
            laterHunt.RecordRecovery("repair sequence completed", new { Test = true });
            var laterRow = Read(laterHunt.FilePath).Last();
            Require(laterRow["DeathId"] == "" && laterRow["Targets"] == "Tribal", "A new hunt was falsely attributed to the previous hunt's confirmed recovery cycle.");

            string denied = Path.Combine(root, "blocked-root"); File.WriteAllText(denied, "existing file");
            using var unavailable = new HuntingSessionLog(denied, () => now);
            unavailable.BeginHunt("Solo", "Mimic", 1, self, 8, new(100, 100)); unavailable.Observe(1, self, 8, new(0, 100));
            Require(unavailable.LastError != null, "An unwritable log path did not expose a nonfatal error.");
            File.Delete(denied); Directory.CreateDirectory(denied);
            unavailable.Observe(1, self, 8, new(100, 100));
            Require(unavailable.LastError == null && Read(unavailable.FilePath).Single()["Event"] == "Revival confirmed", "A recovered constructor failure created a headerless CSV.");
            HuntingSessionLog.Current = unavailable;
            try { unavailable.Dispose(); Require(HuntingSessionLog.Current == null, "Disposal retained the live global logger reference."); }
            finally { HuntingSessionLog.Current = liveGlobal; }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "hunting-session-log-checks.json"), JsonSerializer.Serialize(new { Passed = true, Checks = new[] { "death/positive-HP revival deduplication", "unknown HP preserves pending death", "existing dead baseline", "manual revival retains stopped hunt", "post-revival repair/return cycle attribution", "new hunt clears former recovery attribution", "allocation/generation recreation", "disconnect/identity gaps", "recovery input is not revival", "explicit hunt/healer lifecycle and close", "CSV quotes/commas/newlines", "actual wallet net separate from drop estimate", "unknown wallet remains blank", "loot/timer reset IDs", "quiet per-tick loot update", "zero-duration elapsed", "nonfatal constructor/append errors and header recovery", "disposal clears owned global logger" } }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { Directory.Delete(root, true); }
    }

    static LootTrackerSnapshot Snapshot(bool known, long net) => new(8, 0, [new("Mimic", 3, 4, [])], [], [new("Gold", net), new("Silvin", 2)], DateTime.UtcNow, TimeSpan.FromMinutes(3), DateTime.UtcNow, TimeSpan.FromMinutes(2), [new("Gold", net * 30d), new("Silvin", 60)])
    { Wallet = new(known, "Test", known ? 51471 + net : null, 51471, known ? net : null, DateTime.UtcNow, known ? "Verified wallet" : "Unavailable"), DetectedGoldEstimate = 2602 };

    // A real CSV parser exercises embedded newlines and doubled quotes, not line splitting.
    static List<Dictionary<string, string>> Read(string path)
    {
        string text = File.ReadAllText(path);
        var records = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder(); bool quoted = false;
        for (int n = 0; n < text.Length; n++)
        {
            char c = text[n];
            if (c == '"') { if (quoted && n + 1 < text.Length && text[n + 1] == '"') { field.Append('"'); n++; } else quoted = !quoted; }
            else if (c == ',' && !quoted) { row.Add(field.ToString()); field.Clear(); }
            else if ((c == '\r' || c == '\n') && !quoted)
            {
                if (c == '\r' && n + 1 < text.Length && text[n + 1] == '\n') n++;
                row.Add(field.ToString()); field.Clear(); records.Add(row); row = [];
            }
            else field.Append(c);
        }
        if (quoted || field.Length > 0 || row.Count > 0) throw new Exception("Session CSV contains an incomplete record.");
        string[] header = records[0].ToArray();
        return records.Skip(1).Select(values => values.Count == header.Length ? header.Select((name, n) => (name, values[n])).ToDictionary(pair => pair.name, pair => pair.Item2) : throw new Exception("Session CSV column mismatch.")).ToList();
    }
}
