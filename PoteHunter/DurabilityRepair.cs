namespace PoteHunter;

public sealed record DurableEquipment(string Slot, string Identity, int Current, int Maximum)
{
    public decimal? Percent => Maximum > 0 && Current >= 0 && Current <= Maximum
        ? Current * 100m / Maximum : null;
}

// Known is the reader's assertion that the relevant equipped set is complete.
// Empty/non-durable slots must be positively identified by the reader; an
// unreadable occupied slot makes the whole reading unknown, not a shorter list.
public sealed record DurabilityReading(bool Known, string Context,
    IReadOnlyList<DurableEquipment> Items, DateTime ObservedUtc, string Status)
{
    IReadOnlyList<DurableEquipment> items = Freeze(Items);
    public IReadOnlyList<DurableEquipment> Items
    {
        get => items;
        init => items = Freeze(value);
    }
    static IReadOnlyList<DurableEquipment> Freeze(IReadOnlyList<DurableEquipment>? value) =>
        Array.AsReadOnly(value?.ToArray() ?? []);

    public decimal? LowestPercent => TryValidate(out var minimum, out _) ? minimum : null;
    public decimal? MinimumPercent => LowestPercent;

    internal bool TryValidate(out decimal minimum, out string reason)
    {
        minimum = 0;
        if (!Known)
        {
            reason = string.IsNullOrWhiteSpace(Status) ? "Equipment durability is unavailable." : Status;
            return false;
        }
        if (string.IsNullOrWhiteSpace(Context))
        { reason = "Equipment durability has no verified character/client context."; return false; }
        if (ObservedUtc.Kind != DateTimeKind.Utc || ObservedUtc == DateTime.MinValue)
        { reason = "Equipment durability has no valid UTC observation time."; return false; }
        if (Items.Count == 0)
        { reason = "No known durable equipment is available."; return false; }
        var slots = new HashSet<string>(StringComparer.Ordinal);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        minimum = 100m;
        foreach (var item in Items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Slot) || string.IsNullOrWhiteSpace(item.Identity))
            { reason = "An equipped item has no verified slot or identity."; return false; }
            if (!slots.Add(item.Slot) || !identities.Add(item.Identity))
            { reason = "The equipped-item reading contains duplicate slots or identities."; return false; }
            if (item.Percent is not decimal percent)
            { reason = "An equipped item has invalid current or maximum durability."; return false; }
            minimum = Math.Min(minimum, percent);
        }
        reason = "";
        return true;
    }
}

// Combat itself is not a blocker. The owning loop still serializes repair with
// skills, recovery and travel so inventory input cannot overlap another action.
internal readonly record struct DurabilityActivity(bool InputAllowed, bool KnownLiving,
    bool RepairInProgress, bool RecoveryPending, bool TravelPending, bool BusyInput);

// Keep this object for the form/client session, not inside one Hunt invocation.
// A stop/F8 restart and unknown readings intentionally do not clear attempts.
internal sealed class DurabilityRepairPolicy
{
    internal static readonly TimeSpan MaximumAge = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan MaximumFutureSkew = TimeSpan.FromMilliseconds(250);
    sealed class Episode
    {
        public DurabilityReading? Latest;
        public bool Attempted;
    }
    readonly Dictionary<string, Episode> episodes = new(StringComparer.Ordinal);
    public string LastStatus { get; private set; } = "Waiting for a complete equipment durability reading.";

    public static bool CanRepairDuringCombat(DurabilityActivity activity) =>
        CanRepairDuringCombat(activity, out _);

    public static bool CanRepairDuringCombat(DurabilityActivity activity, out string reason)
    {
        if (!activity.InputAllowed)
        { reason = "Repair is waiting for verified game input access."; return false; }
        if (!activity.KnownLiving)
        { reason = "Repair requires a known living character."; return false; }
        if (activity.RepairInProgress)
        { reason = "An equipment repair is already in progress."; return false; }
        if (activity.RecoveryPending)
        { reason = "Repair is waiting for death recovery to finish."; return false; }
        if (activity.TravelPending)
        { reason = "Repair is waiting for travel to finish."; return false; }
        if (activity.BusyInput)
        { reason = "Repair is waiting for the current input action to finish."; return false; }
        reason = "";
        return true;
    }

    static bool ValidThreshold(decimal threshold) => threshold is >= 1 and <= 99;
    static bool Fresh(DurabilityReading? reading, DateTime now, out decimal minimum, out string reason)
    {
        minimum = 0;
        if (reading is null)
        { reason = "Equipment durability is unavailable."; return false; }
        if (!reading.TryValidate(out minimum, out reason)) return false;
        if (now.Kind != DateTimeKind.Utc)
        { reason = "Durability decisions require a UTC clock."; return false; }
        var age = now - reading.ObservedUtc;
        if (age > MaximumAge)
        { reason = "Equipment durability is stale."; return false; }
        if (age < -MaximumFutureSkew)
        { reason = "Equipment durability has a future observation time."; return false; }
        return true;
    }

    static bool SameItems(DurabilityReading a, DurabilityReading b) =>
        a.Items.OrderBy(item => item.Slot, StringComparer.Ordinal)
            .SequenceEqual(b.Items.OrderBy(item => item.Slot, StringComparer.Ordinal));

    bool Accept(DurabilityReading reading, decimal threshold, DateTime now,
        out Episode episode, out decimal minimum)
    {
        episode = null!; minimum = 0;
        if (!ValidThreshold(threshold))
        { LastStatus = "Choose a durability repair threshold from 1 to 99 percent."; return false; }
        if (!Fresh(reading, now, out minimum, out var reason))
        { LastStatus = reason; return false; }
        if (!episodes.TryGetValue(reading.Context, out episode!))
            episodes.Add(reading.Context, episode = new Episode());
        if (episode.Latest is { } latest)
        {
            if (reading.ObservedUtc < latest.ObservedUtc ||
                reading.ObservedUtc == latest.ObservedUtc && !SameItems(reading, latest))
            { LastStatus = "Equipment durability arrived out of order or changed within one observation."; return false; }
        }
        bool newer = episode.Latest is null || reading.ObservedUtc > episode.Latest.ObservedUtc;
        episode.Latest = reading;
        // A replayed high sample cannot unlock an attempt recorded on that sample.
        if (newer && minimum > threshold) episode.Attempted = false;
        LastStatus = minimum > threshold
            ? $"Lowest equipped durability is {minimum:0.##}%; above the repair threshold."
            : $"Lowest equipped durability is {minimum:0.##}%; waiting for a safe repair opportunity.";
        return true;
    }

    // Observe can rearm a recovered set without claiming an input opportunity.
    public bool Observe(DurabilityReading reading, decimal threshold, DateTime now) =>
        Accept(reading, threshold, now, out _, out _);

    public bool TryBegin(DurabilityReading reading, decimal threshold, DateTime now)
    {
        if (!Accept(reading, threshold, now, out var episode, out var minimum) || minimum > threshold)
            return false;
        if (episode.Attempted)
        {
            LastStatus = "Repair was already attempted for this low-durability episode; waiting for verified recovery.";
            return false;
        }
        episode.Attempted = true; // Claim before any inventory key or paid confirmation.
        LastStatus = $"Repair requested at {minimum:0.##}% equipped durability.";
        return true;
    }

    // A manual or revival repair can consume the same episode. This is bookkeeping,
    // not permission to send input; its caller still owns the complete repair guards.
    public bool RecordAttempt(DurabilityReading reading, decimal threshold, DateTime now)
    {
        if (!Accept(reading, threshold, now, out var episode, out _)) return false;
        episode.Attempted = true;
        LastStatus = "Repair attempt recorded; waiting for verified durability recovery.";
        return true;
    }

    // Identity comparison only; callers separately enforce freshness and health.
    public static bool SameEquipment(DurabilityReading before, DurabilityReading after)
    {
        if (before is null || after is null || !before.TryValidate(out _, out _) || !after.TryValidate(out _, out _) ||
            !string.Equals(before.Context, after.Context, StringComparison.Ordinal) || before.Items.Count != after.Items.Count)
            return false;
        var previous = before.Items.ToDictionary(item => item.Slot, StringComparer.Ordinal);
        return after.Items.All(item => previous.TryGetValue(item.Slot, out var original) &&
            string.Equals(item.Identity, original.Identity, StringComparison.Ordinal));
    }

    public static bool VerifyImprovement(DurabilityReading before, DurabilityReading after,
        decimal threshold, DateTime now) => VerifyImprovement(before, after, threshold, now, out _);

    public static bool VerifyImprovement(DurabilityReading before, DurabilityReading after,
        decimal threshold, DateTime now, out string reason)
        => VerifyImprovementCore(before, after, threshold, now, false, out reason);

    public static bool VerifyCombatImprovement(DurabilityReading before, DurabilityReading after,
        decimal threshold, DateTime now) => VerifyCombatImprovement(before, after, threshold, now, out _);

    public static bool VerifyCombatImprovement(DurabilityReading before, DurabilityReading after,
        decimal threshold, DateTime now, out string reason)
        => VerifyImprovementCore(before, after, threshold, now, true, out reason);

    static bool VerifyImprovementCore(DurabilityReading before, DurabilityReading after,
        decimal threshold, DateTime now, bool allowConcurrentWear, out string reason)
    {
        if (!ValidThreshold(threshold))
        { reason = "Choose a durability repair threshold from 1 to 99 percent."; return false; }
        if (before is null || !before.TryValidate(out var originalMinimum, out reason))
        { reason = "The original equipment durability reading is incomplete or invalid."; return false; }
        if (!Fresh(after, now, out var minimum, out reason)) return false;
        // The original sample was admitted immediately before the repair. It can
        // legitimately be older than two seconds after the bounded UI sequence.
        if (after.ObservedUtc <= before.ObservedUtc)
        { reason = "A new durability reading after repair is required."; return false; }
        if (!string.Equals(before.Context, after.Context, StringComparison.Ordinal) || before.Items.Count != after.Items.Count)
        { reason = "The character/client context or equipped set changed during repair."; return false; }
        var previous = before.Items.ToDictionary(item => item.Slot, StringComparer.Ordinal);
        bool improved = false;
        foreach (var current in after.Items)
        {
            if (!previous.TryGetValue(current.Slot, out var original) ||
                !string.Equals(original.Identity, current.Identity, StringComparison.Ordinal) || original.Maximum != current.Maximum)
            { reason = "An equipped item, slot or maximum durability changed during repair."; return false; }
            // An originally healthy item can wear while a different low item is
            // repaired. Low items themselves still need a nondecreasing reading.
            if (current.Current < original.Current &&
                (!allowConcurrentWear || original.Percent <= threshold))
            { reason = "Equipment durability fell during repair; improvement was not confirmed."; return false; }
            improved |= current.Current > original.Current;
        }
        if (!improved)
        { reason = "Equipment durability did not improve after the repair sequence."; return false; }
        if (minimum <= threshold)
        { reason = "Equipment durability remains at or below the repair threshold."; return false; }
        if (allowConcurrentWear && minimum <= originalMinimum)
        { reason = "The lowest equipped durability did not improve during combat repair."; return false; }
        reason = $"Equipment durability improved; the lowest equipped item is now {minimum:0.##}%.";
        return true;
    }

    internal static void CombatSelfTest()
    {
        static void Require(bool condition, string message)
        { if (!condition) throw new Exception("Combat durability repair: " + message); }
        var ready = new DurabilityActivity(true, true, false, false, false, false);
        Require(CanRepairDuringCombat(ready, out var allowedReason) && allowedReason == "",
            "a living engaged character with input access could not repair.");
        // KnownLiving stays true when incoming combat damage lowers HP. No
        // quiet period or threat/engagement count should enter this decision.
        Require(CanRepairDuringCombat(ready with { KnownLiving = true }),
            "living health dropping during combat prevented repair.");
        var blocked = new[]
        {
            ready with { InputAllowed = false }, // stopped, wrong focus, or unverified client
            ready with { KnownLiving = false }, // zero or unreadable HP
            ready with { RepairInProgress = true },
            ready with { RecoveryPending = true },
            ready with { TravelPending = true },
            ready with { BusyInput = true } // casting, ranged tagging, calibration, or pickup input
        };
        foreach (var activity in blocked)
            Require(!CanRepairDuringCombat(activity, out var reason) && !string.IsNullOrWhiteSpace(reason),
                "unknown/dead/focus/recovery/travel/concurrent input state admitted repair.");

        var startedAt = new DateTime(2026, 10, 5, 14, 0, 0, DateTimeKind.Utc);
        var completedAt = startedAt.AddSeconds(6);
        DurableEquipment lowItem = new("Weapon", "weapon-instance", 20, 100);
        DurableEquipment healthy = new("Head", "head-instance", 90, 100);
        DurabilityReading Read(DateTime at, params DurableEquipment[] equipped) =>
            new(true, "verified-client/character", equipped, at, "Verified combat fixture");
        var before = Read(startedAt, lowItem, healthy);
        var after = Read(completedAt, healthy with { Current = 88 }, lowItem with { Current = 98 });
        Require(VerifyCombatImprovement(before, after, 20, completedAt) &&
            !VerifyImprovement(before, after, 20, completedAt),
            "healthy-item wear either rejected a proved combat repair or weakened strict verification.");
        var thresholdPolicy = new DurabilityRepairPolicy();
        Require(CanRepairDuringCombat(ready) && thresholdPolicy.TryBegin(before, 20, startedAt) &&
            VerifyCombatImprovement(before, after, 20, completedAt) &&
            thresholdPolicy.Observe(after, 20, completedAt) &&
            thresholdPolicy.TryBegin(before with { ObservedUtc = completedAt.AddSeconds(1) }, 20, completedAt.AddSeconds(1)),
            "a combat repair did not rearm only after fresh proved recovery.");
        Require(!new DurabilityRepairPolicy().TryBegin(before, 19, startedAt),
            "combat eligibility bypassed the configured low-durability threshold.");

        var rejected = new[]
        {
            after with { Known = false }, after with { Context = "different-character" },
            after with { ObservedUtc = completedAt - MaximumAge - TimeSpan.FromTicks(1) },
            after with { ObservedUtc = completedAt + MaximumFutureSkew + TimeSpan.FromTicks(1) },
            after with { ObservedUtc = startedAt },
            after with { Items = [lowItem with { Current = 98 }] },
            Read(completedAt, lowItem, healthy with { Current = 88 }), // wear alone
            Read(completedAt, lowItem with { Current = 19 }, healthy with { Current = 100 }),
            Read(completedAt, lowItem with { Current = 98 }, healthy with { Current = 20 }), // threshold not cleared
            Read(completedAt, lowItem with { Current = 98, Identity = "replacement" }, healthy),
            Read(completedAt, lowItem with { Current = 98, Slot = "Feet" }, healthy),
            Read(completedAt, lowItem with { Current = 98, Maximum = 200 }, healthy)
        };
        foreach (var reading in rejected)
            Require(!VerifyCombatImprovement(before, reading, 20, completedAt, out var reason) &&
                !string.IsNullOrWhiteSpace(reason),
                "unknown/stale/dead baseline/no improvement/threshold/context/equipment evidence passed verification.");
        // A higher supplied threshold must not turn healthy-item wear into proof
        // when the new group minimum is worse than the original group minimum.
        var healthyBefore = Read(startedAt, lowItem with { Current = 50 }, healthy);
        Require(!VerifyCombatImprovement(healthyBefore,
                Read(completedAt, lowItem with { Current = 98 }, healthy with { Current = 40 }), 20, completedAt) &&
            VerifyCombatImprovement(healthyBefore,
                Read(completedAt, lowItem with { Current = 98 }, healthy with { Current = 51 }), 20, completedAt),
            "combat verification lost the strict minimum-improvement boundary.");
        Require(!VerifyCombatImprovement(before with { Known = false }, after, 20, completedAt) &&
            !VerifyCombatImprovement(before, after, 0, completedAt) &&
            !VerifyCombatImprovement(before, after, 100, completedAt),
            "combat verification invented a baseline or accepted an invalid threshold.");
        // Stopping/restarting and replayed samples must not authorize a second
        // paid confirmation if a previous combat repair has not been verified.
        var failed = new DurabilityRepairPolicy();
        Require(failed.TryBegin(before, 20, startedAt) &&
            !failed.TryBegin(before with { ObservedUtc = startedAt.AddSeconds(1) }, 20, startedAt.AddSeconds(1)) &&
            !failed.Observe(after with { Known = false }, 20, completedAt) &&
            !failed.TryBegin(before with { ObservedUtc = completedAt }, 20, completedAt),
            "restarted combat or unreadable postrepair data repeated a failed repair.");
    }

    internal static void SelfTest()
    {
        static void Require(bool condition, string message)
        { if (!condition) throw new Exception("Durability repair: " + message); }
        var now = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        DurableEquipment head = new("Head", "head-instance-1", 10, 40);
        DurableEquipment weapon = new("Weapon", "weapon-instance-1", 70, 100);
        DurabilityReading Read(DateTime at, params DurableEquipment[] equipped) =>
            new(true, "process/character/context-1", equipped, at, "Verified fixture");
        var low = Read(now, head, weapon);
        Require(low.LowestPercent == 25m && low.MinimumPercent == 25m,
            "minimum used a weighted average or lost the public property alias.");
        Require(Read(now, head with { Current = 1, Maximum = 3 }).LowestPercent == 100m / 3m &&
            Read(now, head with { Current = 0 }).LowestPercent == 0m &&
            Read(now, head with { Current = int.MaxValue, Maximum = int.MaxValue }).LowestPercent == 100m,
            "fractional, broken or large valid item durability was rounded or overflowed.");
        var supplied = new List<DurableEquipment> { head, weapon };
        var frozen = new DurabilityReading(true, low.Context, supplied, now, "Verified fixture");
        supplied[0] = head with { Current = 40 };
        var copied = frozen with { Items = supplied };
        supplied.Clear();
        Require(frozen.LowestPercent == 25m && copied.LowestPercent == 70m,
            "caller mutation changed an admitted or copied equipment snapshot.");

        var invalid = new[]
        {
            low with { Known = false, Status = "One occupied slot was unreadable" },
            low with { Context = " " }, low with { Items = [] },
            low with { ObservedUtc = default }, low with { ObservedUtc = DateTime.SpecifyKind(now, DateTimeKind.Unspecified) },
            Read(now, head with { Slot = "" }), Read(now, head with { Identity = " " }),
            Read(now, head with { Current = -1 }), Read(now, head with { Current = 41 }),
            Read(now, head with { Maximum = 0 }), Read(now, head with { Maximum = -1 }),
            Read(now, head, head with { Identity = "different-instance" }),
            Read(now, head, weapon with { Identity = head.Identity }), Read(now, null!)
        };
        foreach (var reading in invalid)
        {
            var policy = new DurabilityRepairPolicy();
            Require(reading.LowestPercent is null && !policy.TryBegin(reading, 25, now) &&
                !policy.RecordAttempt(reading, 25, now) && policy.TryBegin(low, 25, now),
                "invalid, partial, empty or duplicate equipment admitted input or consumed a valid episode.");
        }
        foreach (decimal threshold in new[] { -1m, 0m, 100m, 101m })
            Require(!new DurabilityRepairPolicy().TryBegin(low, threshold, now), "an invalid threshold admitted repair.");
        Require(!new DurabilityRepairPolicy().TryBegin(low, 24.99m, now) &&
            new DurabilityRepairPolicy().TryBegin(low, 25m, now) &&
            new DurabilityRepairPolicy().TryBegin(Read(now, head with { Current = 0 }), 1, now) &&
            new DurabilityRepairPolicy().TryBegin(Read(now, head with { Current = 39 }), 99, now),
            "the configured inclusive threshold or its supported limits changed.");
        foreach (var reading in new[]
        {
            low with { ObservedUtc = now - MaximumAge - TimeSpan.FromTicks(1) },
            low with { ObservedUtc = now + MaximumFutureSkew + TimeSpan.FromTicks(1) }
        })
        {
            var policy = new DurabilityRepairPolicy();
            Require(!policy.TryBegin(reading, 25, now) && !policy.RecordAttempt(reading, 25, now),
                "stale or future data admitted repair or consumed an episode.");
        }
        Require(new DurabilityRepairPolicy().TryBegin(low with { ObservedUtc = now - MaximumAge }, 25, now) &&
            new DurabilityRepairPolicy().TryBegin(low with { ObservedUtc = now + MaximumFutureSkew }, 25, now),
            "the inclusive freshness boundaries changed.");

        var retained = new DurabilityRepairPolicy();
        Require(retained.Observe(low, 25, now) && retained.TryBegin(low, 25, now),
            "observing a sample prevented the hunt from claiming the same safe opportunity.");
        Require(!retained.TryBegin(low, 25, now) &&
            !retained.Observe(low with { Known = false }, 25, now.AddSeconds(1)) &&
            !retained.TryBegin(low with { ObservedUtc = now.AddSeconds(1) }, 25, now.AddSeconds(1)) &&
            !retained.TryBegin(low with { ObservedUtc = now.AddSeconds(3) }, 25, now.AddSeconds(3)),
            "repeated low data, unknown reads or a caller/F8 restart unlocked a previous attempt.");
        var high = Read(now.AddSeconds(4), head with { Current = 40 }, weapon);
        Require(!retained.Observe(high with { ObservedUtc = now.AddSeconds(2) }, 25, now.AddSeconds(3)) &&
            !retained.Observe(high with { ObservedUtc = now.AddSeconds(3) }, 25, now.AddSeconds(3)),
            "out-of-order or conflicting same-time high data rearmed repair.");
        Require(retained.Observe(high, 25, high.ObservedUtc) &&
            retained.TryBegin(low with { ObservedUtc = now.AddSeconds(5) }, 25, now.AddSeconds(5)),
            "fresh verified recovery did not permit a later low episode.");
        var freshnessLatch = new DurabilityRepairPolicy();
        Require(freshnessLatch.TryBegin(low, 25, now) &&
            !freshnessLatch.Observe(high with { ObservedUtc = now }, 25, now.AddSeconds(3)) &&
            !freshnessLatch.Observe(high, 25, now.AddSeconds(3)) &&
            !freshnessLatch.TryBegin(low with { ObservedUtc = now.AddSeconds(3) }, 25, now.AddSeconds(3)),
            "an unavailable/stale/future recovery sample rearmed an existing low episode.");
        var other = low with { Context = "process/character/context-2", ObservedUtc = now.AddSeconds(6) };
        Require(retained.TryBegin(other, 25, other.ObservedUtc) &&
            !retained.TryBegin(low with { ObservedUtc = now.AddSeconds(7) }, 25, now.AddSeconds(7)),
            "switching contexts forgot an earlier context's consumed episode.");
        var external = new DurabilityRepairPolicy();
        Require(external.RecordAttempt(low, 25, now) && external.RecordAttempt(low, 25, now) &&
            !external.TryBegin(low with { ObservedUtc = now.AddSeconds(1) }, 25, now.AddSeconds(1)),
            "manual/revival bookkeeping failed to suppress an immediate threshold repair.");
        var manualHigh = new DurabilityRepairPolicy();
        Require(manualHigh.RecordAttempt(high, 25, high.ObservedUtc) &&
            manualHigh.Observe(high, 25, high.ObservedUtc) &&
            !manualHigh.TryBegin(low with { ObservedUtc = now.AddSeconds(5) }, 25, now.AddSeconds(5)),
            "replaying the repair's initial high sample rearmed an external attempt.");

        var completedAt = now.AddSeconds(12);
        var repaired = Read(completedAt, weapon, head with { Current = 40 });
        Require(SameEquipment(low, repaired) && SameEquipment(low, Read(completedAt, head with { Current = 0, Maximum = 80 }, weapon)) &&
            !SameEquipment(low, repaired with { Known = false }) &&
            !SameEquipment(low, repaired with { Context = "changed-context" }) &&
            !SameEquipment(low, Read(completedAt, head with { Slot = "Feet" }, weapon)) &&
            !SameEquipment(low, Read(completedAt, head with { Identity = "replacement" }, weapon)) &&
            !SameEquipment(low, Read(completedAt, head)),
            "equipment identity comparison used percentages, lost membership or accepted an unknown set.");
        Require(VerifyImprovement(low, repaired, 25, completedAt),
            "real improvement was rejected because item order changed or the UI sequence exceeded two seconds.");
        var badAfter = new[]
        {
            repaired with { Known = false }, repaired with { Context = "changed-context" },
            repaired with { ObservedUtc = completedAt - MaximumAge - TimeSpan.FromTicks(1) },
            repaired with { ObservedUtc = completedAt + MaximumFutureSkew + TimeSpan.FromTicks(1) },
            repaired with { ObservedUtc = now }, repaired with { Items = [head with { Current = 40 }] },
            Read(completedAt, head, weapon), Read(completedAt, head with { Current = 10 }, weapon with { Current = 100 }),
            Read(completedAt, head with { Current = 40 }, weapon with { Current = 69 }),
            Read(completedAt, head with { Current = 40, Identity = "replacement" }, weapon),
            Read(completedAt, head with { Current = 40, Slot = "Offhand" }, weapon),
            Read(completedAt, head with { Current = 40, Maximum = 80 }, weapon),
            Read(completedAt, head with { Current = 40 }, weapon, new("Feet", "feet-instance", 100, 100))
        };
        foreach (var reading in badAfter)
            Require(!VerifyImprovement(low, reading, 25, completedAt, out var reason) && !string.IsNullOrWhiteSpace(reason),
                "unverified, unchanged, partial, damaged, stale or replaced equipment passed postrepair verification.");
        Require(!VerifyImprovement(low with { Known = false }, repaired, 25, completedAt),
            "postrepair verification invented an original baseline.");
        Require(!VerifyImprovement(low, repaired, 0, completedAt) && !VerifyImprovement(low, repaired, 100, completedAt),
            "postrepair verification accepted an invalid threshold.");
    }
}
