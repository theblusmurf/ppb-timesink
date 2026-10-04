namespace PoteHunter;

public readonly record struct SentinelPlayerIdentity(uint Id, uint Generation, long Address, string Model)
{
    public static SentinelPlayerIdentity Of(Entity entity) => new(entity.Id, entity.Generation, entity.Address, entity.Model?.ToUpperInvariant() ?? "");
    internal bool Valid => Id != 0 && (Id & 0xf0000000) == 0 && Address >= 0 && PlayerRecognition.Faction(Model) != PlayerFaction.Unknown;
}

public readonly record struct SentinelAlertContext(string ConnectionKey, SentinelPlayerIdentity Local, int Zone);
public readonly record struct SentinelPlayerObservation(SentinelPlayerIdentity Identity, string Name, double Distance, PlayerRelation Relation, bool ConfirmedDead = false);
public sealed record SentinelAlertInput(bool Enabled, bool Connected, bool Fresh, bool LocalAlive, SentinelAlertContext Context,
    IReadOnlyList<SentinelPlayerObservation> Players, bool SoundEnabled, int Volume, bool SoundAllowed = true,
    double Range = SentinelAlertPolicy.EntryRadius);
public sealed record SentinelAlertFrame(IReadOnlyList<SentinelPlayerObservation> CurrentEnemies,
    IReadOnlyList<SentinelPlayerObservation> Arrivals, bool PlaySound, int Volume, bool Baseline, string Status)
{
    internal static SentinelAlertFrame Empty(string status) => new(Array.AsReadOnly(Array.Empty<SentinelPlayerObservation>()),
        Array.AsReadOnly(Array.Empty<SentinelPlayerObservation>()), false, 0, false, status);
}

// Observational lifecycle only. No combat, input, route or client operations.
public sealed class SentinelAlertPolicy
{
    public const double EntryRadius = 25;
    public const double ExitRadius = 28;
    public const double MinimumRange = 1;
    public const double MaximumRange = 100;
    public static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan FreshGapBaseline = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan SoundSpacing = TimeSpan.FromSeconds(3);
    readonly Dictionary<SentinelPlayerIdentity, Presence> tracked = new();
    SentinelAlertContext? context;
    DateTime? lastUpdate, lastFresh, lastSound;
    double? currentRange;
    bool baselinePending = true;
    sealed class Presence
    {
        internal bool Inside;
        internal DateTime Seen;
        internal DateTime? ExitSince;
    }

    public void Reset()
    {
        tracked.Clear(); context = null; lastUpdate = null; lastFresh = null; currentRange = null; baselinePending = true;
    }

    public SentinelAlertFrame Update(SentinelAlertInput input, DateTime utcNow)
    {
        // A reset retains the global cooldown, but an adjusted wall clock must
        // not leave that cooldown permanently anchored to a future timestamp.
        if (lastSound is { } futureSound && futureSound > utcNow) lastSound = utcNow;
        if (!input.Enabled || !input.Connected)
        {
            Reset();
            return SentinelAlertFrame.Empty(input.Enabled ? "Disconnected" : "Sentinel disabled");
        }
        // A changed session, character/body identity, map, or backwards clock cannot
        // inherit detections from an unrelated observation stream.
        bool backwards = lastUpdate is { } previous && utcNow < previous;
        if (context != input.Context || backwards)
        {
            Reset(); context = input.Context;
            if (backwards) lastSound = utcNow;
        }
        lastUpdate = utcNow;
        double range = NormalizeRange(input.Range), exitRange = range + (ExitRadius - EntryRadius);
        if (currentRange != range)
        {
            BaselineAgain(); currentRange = range;
        }
        if (!input.Fresh || !input.LocalAlive || !input.Context.Local.Valid)
        {
            if (lastFresh is { } fresh && utcNow - fresh >= FreshGapBaseline) BaselineAgain();
            return SentinelAlertFrame.Empty(!input.LocalAlive ? "Awaiting living player" : "Awaiting fresh player reading");
        }
        if (lastFresh is { } priorFresh && utcNow - priorFresh >= FreshGapBaseline) BaselineAgain();
        lastFresh = utcNow;
        if (ZoneCombatRules.For(input.Context.Zone).Rule != ZoneCombatRule.PvP)
        {
            BaselineAgain();
            return SentinelAlertFrame.Empty("Enemy alerts suppressed: PvP zone unverified or non-PvP");
        }

        // If one UID denotes two simultaneous object generations, neither object
        // can establish a reliable arrival until the accepted snapshot resolves it.
        var players = input.Players.GroupBy(p => p.Identity.Id)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single()).Where(Eligible)
            .OrderBy(p => p.Distance).ThenBy(p => p.Identity.Id).ToArray();
        var seen = players.Select(p => p.Identity).ToHashSet();
        var arrivals = new List<SentinelPlayerObservation>();
        bool baseline = baselinePending;
        foreach (var presence in tracked.Values)
            if (presence.Inside && presence.ExitSince is { } exit && utcNow - exit >= ExitGrace) presence.Inside = false;
        foreach (var player in players)
        {
            if (!tracked.TryGetValue(player.Identity, out var presence)) tracked.Add(player.Identity, presence = new());
            presence.Seen = utcNow;
            if (player.Distance <= range)
            {
                if (!presence.Inside && !baseline) arrivals.Add(player);
                presence.Inside = true; presence.ExitSince = null;
            }
            else if (player.Distance <= exitRange && presence.Inside) presence.ExitSince = null;
            else MarkExit(presence, utcNow);
        }
        foreach (var pair in tracked)
            if (!seen.Contains(pair.Key)) MarkExit(pair.Value, utcNow);
        foreach (var key in tracked.Where(pair => !pair.Value.Inside && utcNow - pair.Value.Seen >= TimeSpan.FromSeconds(30)).Select(pair => pair.Key).ToArray())
            tracked.Remove(key);
        baselinePending = false;
        var current = Array.AsReadOnly(players.Where(p => p.Distance <= range).ToArray());
        var incoming = Array.AsReadOnly(arrivals.ToArray());
        int volume = Math.Clamp(input.Volume, 0, 100);
        bool sound = arrivals.Count > 0 && input.SoundEnabled && input.SoundAllowed && volume > 0 &&
            (lastSound == null || utcNow - lastSound.Value >= SoundSpacing);
        if (sound) lastSound = utcNow;
        // Muted, background and cooldown arrivals are consumed, never queued for
        // unexpected playback on unmute/focus return. Simultaneous arrivals are one clip.
        return new(current, incoming, sound, volume, baseline,
            baseline ? "Current players established; arrival sound armed" : current.Count == 0 ? $"No opposing players within {range:0.##} map units" : $"{current.Count} opposing player(s) within {range:0.##} map units");

        bool Eligible(SentinelPlayerObservation player) => player.Identity.Valid && player.Identity.Id != input.Context.Local.Id &&
            player.Relation == PlayerRelation.Enemy && !player.ConfirmedDead && double.IsFinite(player.Distance) && player.Distance >= 0 &&
            PlayerRecognition.Faction(player.Identity.Model) != PlayerRecognition.Faction(input.Context.Local.Model);
    }

    public static double NormalizeRange(double range) => double.IsFinite(range) ? Math.Clamp(range, MinimumRange, MaximumRange) : EntryRadius;
    void BaselineAgain() { tracked.Clear(); baselinePending = true; }
    static void MarkExit(Presence presence, DateTime now)
    {
        if (!presence.Inside) return;
        presence.ExitSince ??= now;
        if (now - presence.ExitSince.Value >= ExitGrace) presence.Inside = false;
    }
}
