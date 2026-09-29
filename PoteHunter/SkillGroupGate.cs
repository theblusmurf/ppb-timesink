namespace PoteHunter;

/// <summary>
/// Describes whether the live engaged pack is large and wounded enough for an
/// offensive skill.  The gate is deliberately independent from cooldown and
/// mana checks: those checks still decide whether a selected skill can start.
/// </summary>
public readonly record struct SkillGroupStatus(int InRangeTargets, decimal HighestHealthPercent, bool Ready)
{
    public const int MinimumTargets = 5;
    public const decimal MaximumHighestHealthPercent = 80m;
    public const double DefaultRange = 6d;
    public const long DelayMilliseconds = 5000;
}

public static class SkillGroupGate
{
    public static SkillGroupStatus Evaluate(IEnumerable<Entity> engaged, Entity current,
        IReadOnlyDictionary<uint, Health> health, Vec player, double range = SkillGroupStatus.DefaultRange)
    {
        ArgumentNullException.ThrowIfNull(engaged);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(health);
        if (!player.Finite || !double.IsFinite(range) || range < 0)
            throw new ArgumentOutOfRangeException(nameof(range));

        // Include the locked target even when the encounter roster has not yet
        // published the current snapshot.  Group by id so a refreshed entity
        // cannot count twice during one combat pass.
        var members = engaged.Append(current).Where(e => e.Position.Finite)
            .GroupBy(e => e.Id).Select(group => group.Last())
            .Select(entity => (Entity: entity, Health: health.GetValueOrDefault(entity.Id)))
            .Where(pair => pair.Health.Known && !pair.Health.Dead &&
                (pair.Entity.Position - player).Length <= range)
            .ToArray();

        int count = members.Length;
        decimal highest = count == 0 ? 100m : members.Max(pair =>
            (decimal)pair.Health.Current * 100m / Math.Max(1, pair.Health.Maximum));
        return new SkillGroupStatus(count, highest,
            count >= SkillGroupStatus.MinimumTargets && highest < SkillGroupStatus.MaximumHighestHealthPercent);
    }

    public static void SelfTest()
    {
        var player = new Vec();
        var targets = Enumerable.Range(1, 5)
            .Select(index => new Entity((uint)index, 0x80000000u + (uint)index, "Mimic", new(index * .5, 0), 0))
            .ToArray();
        var health = targets.ToDictionary(entity => entity.Id, _ => new Health(70, 100));
        var ready = Evaluate(targets, targets[0], health, player);
        if (!ready.Ready || ready.InRangeTargets != 5 || ready.HighestHealthPercent != 70)
            throw new Exception("Five wounded targets should open the skill group gate.");
        if (Evaluate(targets.Take(4), targets[0], health, player).Ready)
            throw new Exception("The skill group gate opened with fewer than five targets.");
        var atThreshold = health.ToDictionary(pair => pair.Key, _ => new Health(80, 100));
        if (Evaluate(targets, targets[0], atThreshold, player).Ready)
            throw new Exception("The skill group gate opened at the 80 percent threshold.");
        var outOfRange = targets.ToDictionary(entity => entity.Id, entity => entity with { Position = new(20, 0) });
        if (Evaluate(outOfRange.Values, outOfRange.Values.First(), outOfRange.ToDictionary(pair => pair.Key, _ => new Health(70, 100)), player).Ready)
            throw new Exception("The skill group gate counted targets outside the configured range.");
    }
}
