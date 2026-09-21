namespace PoteHunter;

using System.Text.Json;

public enum UpkeepBuff { None, Encourage, HardenSkin }

public readonly record struct BuffSlotRef(string CharacterIdentity, string Zone, int PageBase, string SlotKey, int SkillId)
{
    public string Scope => $"{CharacterIdentity}\u001f{Zone}\u001f{PageBase}";
}

public sealed record BuffPolicyInput(
    string CharacterIdentity,
    string Zone,
    int PageBase,
    HotbarSnapshot Hotbar,
    DateTimeOffset Now,
    bool Enabled,
    IReadOnlyDictionary<UpkeepBuff, double> DurationsSeconds);

public sealed record BuffDecision(
    BuffSlotRef Slot,
    UpkeepBuff Skill,
    bool ShouldCast,
    string Status,
    DateTimeOffset? EstimatedExpiry,
    DateTimeOffset? RetryAt);

/// <summary>Pure bookkeeping for the two instant, self/area buffs. It never reads input or casts.</summary>
public sealed class BuffUpkeepPolicy
{
    private const double ActivationWindowSeconds = 2;
    private const double FailedBackoffSeconds = 10;
    private readonly Dictionary<BuffSlotRef, Tracker> _trackers = new();
    private readonly Dictionary<(string Scope, string SlotKey), int> _assignments = new();
    private string? _lastScope;

    private sealed class Tracker
    {
        public uint LastCooldown;
        public DateTimeOffset? AttemptAt;
        public bool Pending;
        public bool ActivationEligible;
        public DateTimeOffset? EstimatedExpiry;
        public DateTimeOffset? RetryAt;
    }

    public IReadOnlyList<BuffDecision> Evaluate(BuffPolicyInput input)
    {
        var found = new List<BuffDecision>();
        var scope = $"{input.CharacterIdentity}\u001f{input.Zone}\u001f{input.PageBase}";
        // A new identity/zone/page is a new tracking scope. Stopping and starting
        // without changing this scope intentionally retains the estimates.
        if (_lastScope is not null && !string.Equals(_lastScope, scope, StringComparison.Ordinal))
        {
            foreach (var key in _trackers.Keys.Where(x => x.Scope != scope).ToArray()) _trackers.Remove(key);
            foreach (var assignment in _assignments.Keys.Where(x => x.Scope != scope).ToArray()) _assignments.Remove(assignment);
        }
        _lastScope = scope;
        foreach (var slot in input.Hotbar.Slots)
        {
            if (slot.Kind==SlotKind.Skill && Recognize(slot.Name) != UpkeepBuff.None) continue;
            if (!_assignments.TryGetValue((scope, slot.Key), out var oldId)) continue;
            foreach (var old in _trackers.Keys.Where(x => x.Scope == scope && x.SlotKey == slot.Key && x.SkillId == oldId).ToArray()) _trackers.Remove(old);
            _assignments.Remove((scope, slot.Key));
        }
        foreach (var slot in input.Hotbar.Slots)
        {
            var skill = Recognize(slot.Name);
            if (slot.Kind!=SlotKind.Skill || skill == UpkeepBuff.None) continue;
            var slotRef = new BuffSlotRef(input.CharacterIdentity, input.Zone, input.PageBase, slot.Key, slot.Id);
            var location = (scope, slot.Key);
            if (_assignments.TryGetValue(location, out var priorId) && priorId != slot.Id)
                foreach (var old in _trackers.Keys.Where(x => x.CharacterIdentity == input.CharacterIdentity && x.Zone == input.Zone && x.PageBase == input.PageBase && x.SlotKey == slot.Key && x.SkillId != slot.Id).ToArray()) _trackers.Remove(old);
            _assignments[location] = slot.Id;
            if (!_trackers.TryGetValue(slotRef, out var tracker)) _trackers[slotRef] = tracker = new Tracker();

            if (tracker.Pending && tracker.AttemptAt is { } pendingAt && (input.Now - pendingAt).TotalSeconds > ActivationWindowSeconds)
            {
                tracker.Pending = false;
                tracker.ActivationEligible = false;
                tracker.RetryAt = pendingAt.AddSeconds(FailedBackoffSeconds);
            }
            var attempt = tracker.AttemptAt;
            var transitioned = tracker.LastCooldown == 0 && slot.RemainingCooldown > 0 && tracker.ActivationEligible && attempt.HasValue && (input.Now - attempt.Value).TotalSeconds <= ActivationWindowSeconds && (input.Now - attempt.Value).TotalSeconds >= -0.1;
            if (transitioned)
            {
                tracker.Pending = false;
                tracker.ActivationEligible = false;
                if (TryDuration(input.DurationsSeconds, skill, out var duration)) tracker.EstimatedExpiry = attempt!.Value.AddSeconds(duration);
            }
            tracker.LastCooldown = slot.RemainingCooldown;

            var retryAt = tracker.RetryAt;
            var expiry = tracker.EstimatedExpiry;
            var activeEstimate = expiry.HasValue && input.Now < expiry.Value;
            string status;
            bool cast = false;
            if (!input.Enabled) status = "disabled";
            else if (!TryDuration(input.DurationsSeconds, skill, out _)) status = "unknown duration";
            else if (tracker.Pending) status = "pending activation";
            else if (activeEstimate) status = $"estimated buff: {(expiry!.Value-input.Now).TotalSeconds:F1}s left; skill cooldown: {slot.RemainingCooldown/1000.0:F1}s";
            else if (slot.Locked || slot.RemainingCooldown > 0) status = $"buff expiry unknown/estimated elapsed; skill {(slot.Locked?"locked":"cooling down")}: {Math.Max(slot.RemainingCooldown,Math.Max(0,slot.LockRemaining))/1000.0:F1}s";
            else if (retryAt is { } retry && input.Now < retry) status = $"activation not confirmed; retry in {(retry-input.Now).TotalSeconds:F1}s";
            else if (!slot.Ready) status = "not ready";
            else { cast = true; status = "due (estimated)"; }
            found.Add(new BuffDecision(slotRef, skill, cast, status, tracker.EstimatedExpiry, retryAt));
        }
        return found;
    }

    /// <summary>Call immediately when the caller attempts the slot. A failed attempt is backed off.</summary>
    public void RecordCastAttempt(BuffSlotRef slot, DateTimeOffset at, bool accepted)
    {
        if (!_trackers.TryGetValue(slot, out var tracker)) _trackers[slot] = tracker = new Tracker();
        tracker.AttemptAt = at;
        tracker.Pending = accepted;
        tracker.ActivationEligible = accepted;
        if (!accepted) { tracker.RetryAt = at.AddSeconds(FailedBackoffSeconds); tracker.ActivationEligible = false; }
        else tracker.RetryAt = null;
    }

    public void ResetAssignment(string characterIdentity, string zone, int pageBase, string slotKey)
    {
        foreach (var key in _trackers.Keys.Where(x => x.CharacterIdentity == characterIdentity && x.Zone == zone && x.PageBase == pageBase && x.SlotKey == slotKey).ToArray()) _trackers.Remove(key);
        _assignments.Remove(($"{characterIdentity}\u001f{zone}\u001f{pageBase}", slotKey));
    }

    public static UpkeepBuff Recognize(string? name)
    {
        var normalized = string.Join(' ', (name ?? string.Empty).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^Encourage(?:\s+Lv\.\d+)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)) return UpkeepBuff.Encourage;
        if (System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^Harden Skin(?:\s+Lv\.\d+)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)) return UpkeepBuff.HardenSkin;
        return UpkeepBuff.None;
    }

    private static bool TryDuration(IReadOnlyDictionary<UpkeepBuff, double> values, UpkeepBuff skill, out double duration) => values.TryGetValue(skill, out duration) && duration > 0 && double.IsFinite(duration);

    public static string SelfTest(string outputPath)
    {
        var checks = new List<object>();
        static HotbarSnapshot Bar(string name, uint remaining = 0, bool locked = false, int id = 4) => new(0, new[] { new HotbarSlot("4", SlotKind.Skill, id, name, 30, remaining, locked, locked ? 2 : 0) });
        var t = DateTimeOffset.UnixEpoch;
        var durations = new Dictionary<UpkeepBuff, double> { [UpkeepBuff.Encourage] = 60 };
        var p = new BuffUpkeepPolicy(); var input = new BuffPolicyInput("alice", "field", 0, Bar("Encourage"), t, true, durations);
        checks.Add(new { name = "cooldown-not-expiry", pass = p.Evaluate(input)[0].ShouldCast });
        var slot = new BuffSlotRef("alice", "field", 0, "4", 4); p.RecordCastAttempt(slot, t, true);
        checks.Add(new { name = "unknown-duration-no-cast", pass = !new BuffUpkeepPolicy().Evaluate(input with { DurationsSeconds = new Dictionary<UpkeepBuff, double>() })[0].ShouldCast });
        checks.Add(new { name = "pending", pass = p.Evaluate(input with { Now = t.AddSeconds(1) })[0].Status == "pending activation" });
        p.Evaluate(input with { Now = t.AddSeconds(1), Hotbar = Bar("Encourage", 20) });
        checks.Add(new { name = "estimated-after-transition", pass = p.Evaluate(input with { Now = t.AddSeconds(2) })[0].EstimatedExpiry == t.AddSeconds(60) });
        p.RecordCastAttempt(slot, t.AddSeconds(3), false);
        checks.Add(new { name = "failed-retry", pass = !p.Evaluate(input with { Now = t.AddSeconds(4), Hotbar = Bar("Encourage") })[0].ShouldCast });
        checks.Add(new { name = "buff-names", pass = Recognize("Encourage Lv.1") == UpkeepBuff.Encourage && Recognize("Harden Skin Lv.1") == UpkeepBuff.HardenSkin && Recognize("Encouragement") == UpkeepBuff.None });
        checks.Add(new { name = "ready-locked", pass = !p.Evaluate(input with { Now = t.AddSeconds(80), Hotbar = Bar("Encourage", 0, true) })[0].ShouldCast });
        var assignment = new BuffUpkeepPolicy();
        var first = assignment.Evaluate(input)[0];
        assignment.RecordCastAttempt(first.Slot, t, true);
        var changed = assignment.Evaluate(input with { Hotbar = Bar("Harden Skin", 0, false, 5), DurationsSeconds = new Dictionary<UpkeepBuff, double> { [UpkeepBuff.HardenSkin] = 47 } })[0];
        checks.Add(new { name = "assignment-reset", pass = changed.ShouldCast && changed.EstimatedExpiry is null });
        var identityChanged = assignment.Evaluate(input with { CharacterIdentity = "bob" })[0];
        checks.Add(new { name = "identity-reset", pass = identityChanged.ShouldCast });
        checks.Add(new { name = "returning-identity-reset", pass = assignment.Evaluate(input)[0].ShouldCast });
        var timeout=new BuffUpkeepPolicy();var due=timeout.Evaluate(input)[0];timeout.RecordCastAttempt(due.Slot,t,true);
        checks.Add(new{name="failed-activation-backoff",pass=!timeout.Evaluate(input with{Now=t.AddSeconds(3)})[0].ShouldCast});
        checks.Add(new{name="failed-activation-retry",pass=timeout.Evaluate(input with{Now=t.AddSeconds(11)})[0].ShouldCast});
        var item=input with{Hotbar=new(0,[Bar("Encourage").Slots[0] with{Kind=SlotKind.Item}])};
        checks.Add(new{name="item-not-cast",pass=new BuffUpkeepPolicy().Evaluate(item).Count==0});
        var longer=new BuffUpkeepPolicy();var d=longer.Evaluate(input)[0];longer.RecordCastAttempt(d.Slot,t,true);
        longer.Evaluate(input with{Now=t.AddSeconds(1),Hotbar=Bar("Encourage",30000)});
        checks.Add(new{name="ready-before-buff-expires-no-recast",pass=!longer.Evaluate(input with{Now=t.AddSeconds(31)})[0].ShouldCast});
        checks.Add(new{name="expiry-and-ready-recast",pass=longer.Evaluate(input with{Now=t.AddSeconds(61)})[0].ShouldCast});
        checks.Add(new{name="expired-but-cooling-no-recast",pass=!longer.Evaluate(input with{Now=t.AddSeconds(61),Hotbar=Bar("Encourage",1000)})[0].ShouldCast});
        var passed = checks.Count(x => (bool)x.GetType().GetProperty("pass")!.GetValue(x)!);
        var json = JsonSerializer.Serialize(new { passed, total = checks.Count, checks }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outputPath, json);
        if (passed != checks.Count) throw new InvalidOperationException($"Buff self-test failed: {passed}/{checks.Count} checks passed.");
        return json;
    }
}
