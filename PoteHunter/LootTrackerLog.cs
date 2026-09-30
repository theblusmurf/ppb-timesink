using System.Globalization;
using System.Text;

namespace PoteHunter;

/// <summary>
/// Appends event snapshots to a CSV file that can be opened while the bot is
/// running in Excel.  A row is written only when the caller observes a death
/// or an explicit tracker reset.
/// </summary>
public sealed class LootTrackerLog
{
    static readonly string[] TrackedNames = ["Silvin", "Mithril", "Iternium", "Fehu", "Gold", "Gems"];
    static readonly string[] SourceNames = ["Mimic", "Tribal", "Pulkhan", "Tower"];
    static readonly string Header = string.Join(",", new[] { "TimestampLocal", "Reason", "Zone", "PendingKills", "SessionElapsed", "RateWindowElapsed" }
        .Concat(TrackedNames.Select(name => name + "Total"))
        .Concat(TrackedNames.Select(name => name + "PerHour"))
        .Concat(SourceNames.Select(name => name + "Kills"))
        .Concat(SourceNames.Select(name => name + "Drops")));
    readonly object gate = new();

    public string FilePath { get; }

    public LootTrackerLog(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A log path is required.", nameof(filePath));
        FilePath = filePath;
    }

    public bool TrySave(LootTrackerSnapshot snapshot, string reason)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        reason = string.IsNullOrWhiteSpace(reason) ? "Event" : reason.Trim();
        lock (gate)
        {
            try
            {
                string? directory = System.IO.Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                using var stream = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                bool writeHeader = stream.Length == 0;
                stream.Seek(0, SeekOrigin.End);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                if (writeHeader) writer.WriteLine(Header);
                writer.WriteLine(Row(snapshot, reason));
                writer.Flush();
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try { TraceLog.Record("loot tracker event log write failed", new { FilePath, Reason = reason, Error = ex.Message }); } catch { }
                return false;
            }
        }
    }

    static string Row(LootTrackerSnapshot snapshot, string reason)
    {
        var totals = snapshot.TrackedLoot.ToDictionary(item => item.Name, item => item.Count, StringComparer.OrdinalIgnoreCase);
        var rates = snapshot.HourlyLoot.ToDictionary(item => item.Name, item => item.PerHour, StringComparer.OrdinalIgnoreCase);
        var sources = snapshot.Sources.ToDictionary(item => item.Source, StringComparer.OrdinalIgnoreCase);
        var values = new List<string>
        {
            DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            reason,
            snapshot.Zone.ToString(CultureInfo.InvariantCulture),
            snapshot.PendingKills.ToString(CultureInfo.InvariantCulture),
            FormatDuration(snapshot.Elapsed),
            FormatDuration(snapshot.RateElapsed)
        };
        values.AddRange(TrackedNames.Select(name => totals.GetValueOrDefault(name).ToString(CultureInfo.InvariantCulture)));
        values.AddRange(TrackedNames.Select(name => rates.GetValueOrDefault(name).ToString("F2", CultureInfo.InvariantCulture)));
        values.AddRange(SourceNames.Select(name => sources.GetValueOrDefault(name)?.Kills.ToString(CultureInfo.InvariantCulture) ?? "0"));
        values.AddRange(SourceNames.Select(name => sources.GetValueOrDefault(name)?.Drops.ToString(CultureInfo.InvariantCulture) ?? "0"));
        return string.Join(',', values.Select(Csv));
    }

    static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    static string FormatDuration(TimeSpan duration)
    {
        duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        return duration.TotalDays >= 1
            ? $"{(int)duration.TotalDays}.{duration:hh\\:mm\\:ss}"
            : duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    public static void SelfTest()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PoteHunter-LootTrackerLog-" + Guid.NewGuid().ToString("N"));
        string path = System.IO.Path.Combine(directory, "loot-session-log.csv");
        try
        {
            var tracker = new LootTracker();
            tracker.ObserveDrops([], 8);
            tracker.RecordKill(new Entity(10, 0x80001753, "Mimic", new(0, 0), 0), new(0, 0), 8);
            tracker.ObserveDrops([
                new GroundItem(1, 1, -2147483551, "Gold", new(0, 0), 0),
                new GroundItem(2, 2, -2147483529, "Gold", new(0, 0), 0)], 8);
            var log = new LootTrackerLog(path);
            if (!log.TrySave(tracker.Snapshot(), "Reset") || !File.Exists(path)) throw new Exception("Event log was not written.");
            string[] lines = File.ReadAllLines(path);
            if (lines.Length != 2 || !lines[0].Contains("TimestampLocal", StringComparison.Ordinal) || !lines[1].Contains("\"Reset\"", StringComparison.Ordinal))
                throw new Exception("Event log header or reason was invalid.");
            int goldColumn = Array.IndexOf(lines[0].Split(','), "GoldTotal");
            if (goldColumn < 0 || lines[1].Split(',')[goldColumn] != "\"216\"")
                throw new Exception("The event log recorded pile counts instead of the gold amount.");
            if (!log.TrySave(tracker.Snapshot(), "Death") || File.ReadAllLines(path).Length != 3)
                throw new Exception("Event log did not append a second event.");
        }
        finally
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch { }
        }
    }
}
