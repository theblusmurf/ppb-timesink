using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PoteHunter;

/// <summary>Local application/hunt history. HP confirms deaths and revivals; sent input never does.</summary>
internal sealed class HuntingSessionLog : IDisposable
{
    internal static HuntingSessionLog? Current { get; set; }
    static readonly string[] Items = ["Silvin", "Mithril", "Iternium", "Fehu", "Gold", "Gems"];
    static readonly HashSet<string> RecoveryStages = new(StringComparer.Ordinal)
    {
        "character death detected", "character revived", "revival popup opening click", "visual revive confirmed",
        "revival click deferred", "revive key input", "revive fallback input", "revival setup/test cancelled",
        "revival setup/test stopped", "revival test completed", "repair inventory key requested", "repair inventory key sent",
        "repair inventory located", "repair recognition changed", "repair recognition timed out",
        "repair control clicked", "repair sequence completed", "repair stopped", "repair setup/test cancelled",
        "repair blocked saved-route return", "recovery destination selected", "recovery spots occupied",
        "saved hunt anchor reached", "saved hunt anchor return retry", "saved hunt anchor return blocked",
        "fault death recovery completed", "movement fault death watch started", "hunt stopped", "hunt failed",
        "healer stopped", "healer failed", "stop requested"
    };
    readonly object gate = new();
    readonly Func<DateTimeOffset> clock;
    readonly DateTimeOffset applicationStarted;
    readonly string applicationId = Guid.NewGuid().ToString("N");
    readonly string header;
    string lootSessionId = Guid.NewGuid().ToString("N");
    bool disposed, baseline, wasDead, healthGap;
    string? observationGap;
    int processId, zone, deaths, revivals;
    Entity? character;
    Health health;
    HuntContext? hunt;
    PendingDeath? pending, lastRecovery;
    LootTrackerSnapshot? loot;
    public string FilePath { get; }
    public string? LastError { get; private set; }

    sealed class HuntContext(string mode, string targets, DateTimeOffset started)
    {
        public readonly string Id = Guid.NewGuid().ToString("N");
        public readonly string Mode = mode, Targets = targets;
        public readonly DateTimeOffset Started = started;
        public int Deaths, Revivals;
        public DateTimeOffset? Ended;
    }
    sealed record PendingDeath(string Id, HuntContext? Hunt, bool ObservedDeath);

    public HuntingSessionLog(string root, Func<DateTimeOffset>? clock = null)
    {
        this.clock = clock ?? (() => DateTimeOffset.Now);
        applicationStarted = this.clock();
        FilePath = Path.Combine(root, applicationStarted.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + applicationId[..8], "events.csv");
        var columns = new List<string> { "TimeLocal", "TimeUtc", "ApplicationId", "HuntId", "Event", "ProcessId", "CharacterId", "Character", "Zone", "Mode", "Targets", "HP", "MaximumHP", "DeathId", "Cause", "Deaths", "Revivals", "HuntDeaths", "HuntRevivals", "ApplicationElapsedSeconds", "HuntElapsedSeconds", "LootSessionId" };
        columns.AddRange(Items); columns.AddRange(Items.Select(name => name + "PerHour"));
        columns.AddRange(["WalletKnown", "WalletCurrent", "WalletBaseline", "WalletNet", "DetectedGoldEstimate", "LootElapsedSeconds", "RateElapsedSeconds", "SourceKills", "SourceDrops", "Details"]);
        header = string.Join(',', columns);
        Write("Application started", new { DeathCause = "Unknown: no verified attacker or killer field" }, null);
    }

    public void Observe(int processId, Entity self, int zone, Health hp)
    {
        lock (gate)
        {
            if (disposed) return;
            if (character != null && (this.processId != processId || this.zone != zone || !RecoveryRouting.SameCharacter(character, self)))
            {
                Write("Character observation changed", new { PreviousProcess = this.processId, NewProcess = processId, PreviousZone = this.zone, NewZone = zone }, hunt);
                ResetHealth();
            }
            this.processId = processId; character = self; this.zone = zone; health = hp;
            if (!hp.Known)
            {
                if (!healthGap) Write("Health unavailable", new { PendingDeathPreserved = pending != null }, hunt);
                healthGap = true; return;
            }
            healthGap = false; observationGap = null;
            if (!baseline)
            {
                baseline = true; wasDead = hp.Dead;
                if (hp.Dead) pending = new(Guid.NewGuid().ToString("N"), hunt, false);
                Write(hp.Dead ? "Already dead on connection" : "Connected health baseline", new { CountedDeath = false }, hunt, pending?.Id);
                return;
            }
            if (hp.Dead == wasDead) return;
            wasDead = hp.Dead;
            if (hp.Dead)
            {
                lastRecovery = null;
                deaths++; if (hunt != null) hunt.Deaths++;
                pending = new(Guid.NewGuid().ToString("N"), hunt, true);
                Write("Death observed", new { Confirmation = "Readable HP reached zero or below", CountedDeath = true }, hunt, pending.Id);
            }
            else if (pending is { } death)
            {
                revivals++; if (death.Hunt != null) death.Hunt.Revivals++;
                Write("Revival confirmed", new { Confirmation = "Readable positive HP after known dead HP", death.ObservedDeath }, death.Hunt, death.Id);
                lastRecovery = death;
                pending = null;
            }
        }
    }

    public void BeginHunt(string mode, string targets, int processId, Entity self, int zone, Health hp)
    {
        lock (gate)
        {
            if (disposed) return;
            if (hunt != null) EndHuntLocked("Replaced by a new hunting session");
            Observe(processId, self, zone, hp);
            // Repair/return can finish after living HP or Stop, but an unrelated
            // new hunt must not inherit the previous cycle's death attribution.
            lastRecovery = null;
            hunt = new(mode, targets, clock());
            Write("Hunt started", new { Mode = mode, Targets = targets }, hunt);
        }
    }

    public void EndHunt(string reason)
    {
        lock (gate) { if (!disposed) EndHuntLocked(reason); }
    }
    void EndHuntLocked(string reason)
    {
        if (hunt == null) return;
        hunt.Ended = clock();
        Write("Hunt ended", new { Reason = reason }, hunt);
        hunt = null;
    }

    public void ObservationGap(string reason)
    {
        lock (gate)
        {
            if (disposed || observationGap == reason) return;
            Write("Observation gap", new { Reason = reason, PendingDeathCleared = pending != null }, hunt);
            ResetHealth(); health = default; observationGap = reason;
        }
    }
    void ResetHealth() { baseline = false; wasDead = false; healthGap = false; pending = null; lastRecovery = null; }

    public void UpdateLoot(LootTrackerSnapshot snapshot)
    {
        lock (gate) { if (!disposed) loot = snapshot; }
    }
    public void RecordLoot(LootTrackerSnapshot snapshot, string reason)
    {
        lock (gate)
        {
            if (disposed) return;
            loot = snapshot;
            if (reason.Equals("Loot reset", StringComparison.OrdinalIgnoreCase)) lootSessionId = Guid.NewGuid().ToString("N");
            Write("Loot snapshot", new { Reason = reason, WalletStatus = snapshot.Wallet.Status, snapshot.Sources }, hunt, pending?.Id);
        }
    }

    public void RecordRecovery(string stage, object details)
    {
        lock (gate)
        {
            if (disposed || !RecoveryStages.Contains(stage)) return;
            var cycle = pending ?? lastRecovery;
            Write("Recovery: " + stage, details, cycle != null ? cycle.Hunt : hunt, cycle?.Id);
        }
    }

    void Write(string stage, object details, HuntContext? context, string? deathId = null)
    {
        try
        {
            EnsureHeader();
            DateTimeOffset now = clock();
            var row = new List<string>
            {
                now.ToLocalTime().ToString("O", CultureInfo.InvariantCulture), now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), applicationId,
                context?.Id ?? "", stage, character == null ? "" : Number(processId), character == null ? "" : $"0x{character.Id:X8}", character?.Name ?? "",
                character == null ? "" : Number(zone), context?.Mode ?? "", context?.Targets ?? "", health.Known ? Number(health.Current) : "", health.Known ? Number(health.Maximum) : "",
                deathId ?? "", "Unknown", Number(deaths), Number(revivals), context == null ? "" : Number(context.Deaths), context == null ? "" : Number(context.Revivals),
                Seconds(now - applicationStarted), context == null ? "" : Seconds((context.Ended ?? now) - context.Started), lootSessionId
            };
            row.AddRange(Items.Select(name => loot == null || name == "Gold" && !loot.Wallet.Known ? "" : Number(name == "Gold" ? loot.Wallet.Net : loot.TrackedLoot.FirstOrDefault(item => item.Name == name)?.Count ?? 0)));
            row.AddRange(Items.Select(name => loot == null || name == "Gold" && !loot.Wallet.Known ? "" : Number(loot.HourlyLoot.FirstOrDefault(item => item.Name == name)?.PerHour ?? 0)));
            row.AddRange([loot == null ? "" : loot.Wallet.Known.ToString(), Number(loot?.Wallet.Known == true ? loot.Wallet.Current : null), Number(loot?.Wallet.Baseline), Number(loot?.Wallet.Known == true ? loot.Wallet.Net : null), Number(loot?.DetectedGoldEstimate), loot == null ? "" : Seconds(loot.Elapsed), loot == null ? "" : Seconds(loot.RateElapsed), loot == null ? "" : Number(loot.Sources.Sum(source => source.Kills)), loot == null ? "" : Number(loot.Sources.Sum(source => source.Drops)), JsonSerializer.Serialize(details)]);
            File.AppendAllText(FilePath, string.Join(',', row.Select(Escape)) + Environment.NewLine, new UTF8Encoding(false));
            LastError = null;
        }
        catch (Exception ex) when (LogFailure(ex)) { LastError = ex.Message; }
    }
    void EnsureHeader()
    {
        // A temporarily unwritable folder must recover with a complete header;
        // append APIs alone would otherwise silently create a headerless CSV.
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        if (!File.Exists(FilePath) || new FileInfo(FilePath).Length == 0)
        {
            File.WriteAllText(FilePath, header + Environment.NewLine, new UTF8Encoding(false));
            return;
        }
        using var reader = new StreamReader(FilePath, Encoding.UTF8, true);
        if (reader.ReadLine() != header) throw new IOException("Session CSV header is incomplete or changed; the existing file was preserved.");
    }
    static string Number(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    static string Seconds(TimeSpan duration) => Math.Max(0, duration.TotalSeconds).ToString("0.###", CultureInfo.InvariantCulture);
    static string Escape(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    static bool LogFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException or JsonException or InvalidOperationException;

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            EndHuntLocked("Application closed");
            Write("Application ended", new { PendingDeath = pending != null }, null, pending?.Id);
            disposed = true;
            if (ReferenceEquals(Current, this)) Current = null;
        }
    }
}
