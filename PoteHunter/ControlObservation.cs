using System.Collections.ObjectModel;

namespace PoteHunter;

// This object belongs to one synchronous preflight invocation. It never crosses
// an await, replaces fresh local/locked HP checks or lives across input calls.
internal sealed class ControlHealthPass(Func<string> context, Func<Dictionary<uint, Health>> read,
    Func<long>? milliseconds = null)
{
    internal const int MaximumAgeMilliseconds = 100;
    readonly Func<long> clock = milliseconds ?? (() => Environment.TickCount64);
    IReadOnlyDictionary<uint, Health>? snapshot;
    string? identity;
    long observed;

    public IReadOnlyDictionary<uint, Health> Read()
    {
        string before = context(); long started = clock();
        if (snapshot != null && before == identity && started >= observed && started - observed < MaximumAgeMilliseconds)
            return snapshot;
        var current = read();
        if (context() != before)
            throw new InvalidOperationException("Control health observation context changed; stopped.");
        // A slow read still supplies the caller's ordinary fresh observation,
        // but its start timestamp prevents reuse. Optional caching never adds
        // a new timeout-based whole-hunt stop under scheduling pressure.
        identity = before; observed = started;
        return snapshot = new ReadOnlyDictionary<uint, Health>(new Dictionary<uint, Health>(current));
    }
}

internal static class PriorityHealthRead
{
    // Only actual, unambiguous Gamekeeper bodies request HP. Missing/dead HP
    // remains unknown/dead; every input still renews its selected-target guard.
    public static IReadOnlyDictionary<uint, Health> Read(IReadOnlyList<Entity> candidates, Func<uint, Health> read)
    {
        var result = new Dictionary<uint, Health>();
        foreach (var group in candidates.Where(Targeting.IsGamekeeper).GroupBy(e => e.Id))
        {
            if (group.Select(e => (e.Address, e.Generation)).Distinct().Skip(1).Any()) continue;
            result[group.Key] = read(group.Key);
        }
        return new ReadOnlyDictionary<uint, Health>(result);
    }
}

internal sealed record CoreReadCounts(long BodyPolls, long BroadHealthSnapshots, long TargetHealthCalls,
    long PlayerNameScans, long PlayerNameDisplayReuse, long DurabilityFullScreens, long DurabilityScreenReuse);

public sealed partial class World
{
    long bodyPollCount, healthSnapshotCount, targetHealthReadCount, playerNameScanCount, playerNameReuseCount;

    internal CoreReadCounts ReadCounts => new(Interlocked.Read(ref bodyPollCount), Interlocked.Read(ref healthSnapshotCount),
        Interlocked.Read(ref targetHealthReadCount), Interlocked.Read(ref playerNameScanCount), Interlocked.Read(ref playerNameReuseCount),
        durabilityScreening.FullReads, durabilityScreening.ReusedReads);

    internal string ControlReadContext()
    {
        if (!ConnectionVerified || !ClientProcessAlive) throw new InvalidOperationException("Control observation client is unavailable.");
        var self = LocalPlayer();
        return $"{ClientHash}:{Pid}:{moduleBase:X}:{Pointer(moduleBase + profile.Scene):X8}:" +
            $"{Pointer(moduleBase + profile.UidDataManager):X8}:{Pointer(moduleBase + profile.LocalActor):X8}:" +
            $"{ActiveZone()}:{self.Address:X}:{self.Id:X8}:{self.Generation}:{self.Name}:{self.Model}";
    }
}
