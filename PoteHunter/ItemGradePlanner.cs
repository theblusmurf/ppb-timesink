namespace PoteHunter;

internal enum ItemGemGroup { Armor, Shield, Weapon, Caster }

/// <summary>One gem option for closing a stat gap: the gem's name and how many of that tier are needed.</summary>
internal sealed record ItemGemOption(string Name, int Count);

/// <summary>
/// What the stat reaches at +10 with every socket filled: the cheapest gem tier that gets it to AAA with five sockets and with
/// seven (a Lucky item's two innate sockets plus the five from upgrading), or null when even Perfect gems fall short.
/// </summary>
internal sealed record ItemStatProjection(string? FiveSlots, int FiveCount, string? SevenSlots, int SevenCount, bool AtPlusTen = false)
{
    /// <summary>James's rule of thumb: five regular gems on a +10 item are the cheap path, so such stats are highlighted.</summary>
    internal bool FiveRegular => FiveSlots == ItemGradePlanner.RegularTierName;
    /// <summary>
    /// The gem counts assume every socket is free: the client exposes only two socket words and the rest are not mapped yet
    /// (docs/atlas/items.md), so gems already socketed are neither seen nor subtracted. An item already at +10 is worded as
    /// "AAA with", since no further upgrade is in the picture.
    /// </summary>
    internal string Describe()
    {
        string head = AtPlusTen ? "AAA with " : "AAA at +10 with ";
        return FiveSlots == null && SevenSlots == null ? (AtPlusTen ? "not AAA even with 7 Perfect" : "not AAA at +10 even with 7 Perfect")
            : FiveSlots == null ? head + SevenCount + " " + SevenSlots + " (7 slots only)"
            : head + FiveCount + " " + FiveSlots + (SevenSlots == FiveSlots ? "" : " or " + SevenCount + " " + SevenSlots + " (7 slots)");
    }
}

/// <summary>One stat line of a graded item: the live value, its grade, and what the target grade still needs.</summary>
internal sealed record ItemStatPlan(string Stat, int Value, ItemGrade? Grade, ItemGrade? Target, int? Needed, string? GemName, IReadOnlyList<ItemGemOption>? Gems,
    ItemStatProjection? Projection = null)
{
    internal string Label => ItemGradeTable.DisplayName(Stat);
    /// <summary>True when the table lists this stat for the item, so a grade and a gap could be computed.</summary>
    internal bool Graded => Grade.HasValue;
}

/// <summary>FixedTarget is null for the automatic rule: a stat at AAA shows the gap to S, every other stat the gap to AAA.</summary>
internal sealed record ItemGradePlan(ItemGradeProfile? Profile, string Name, int PrototypeId, ItemGrade? FixedTarget,
    IReadOnlyList<ItemStatPlan> Stats, string ItemGradeLabel)
{
    internal string TargetLabel => FixedTarget?.ToString() ?? "AAA, then S";
}

/// <summary>Grades each stat of an item against the bundled table and counts the gems that close the gap to a target grade.</summary>
internal static class ItemGradePlanner
{
    // Gem bonus per tier (Fragment, Small, plain, Great, Flawless, Perfect) in tooltip units.
    sealed record Gem(string Name, int[] Bonus);
    static readonly (int Tier, string Prefix)[] ShownTiers = [(5, "Perfect "), (4, "Flawless "), (3, "Great "), (2, "")];
    internal const string RegularTierName = "regular";
    /// <summary>Cheapest first: the projection names the first tier whose full set of gems reaches AAA.</summary>
    static readonly (int Tier, string Name)[] ProjectionTiers = [(2, RegularTierName), (3, "Great"), (4, "Flawless"), (5, "Perfect")];
    /// <summary>
    /// Upgrade levels +1 to +5 each add one point to one stat in turn, so every stat ends up one point higher by +5 (James's
    /// account); +6 to +10 add sockets instead. Which stat each level feeds is not known, so only a +0 item is credited the
    /// point (a +1..+4 item may already carry it in some stats); erring low keeps the AAA promise honest.
    /// </summary>
    internal const int StatLevels = 5;
    internal static readonly int[] ProjectedSlots = [5, 7];

    internal static int RemainingUpgradeBonus(int upgradeLevel) => upgradeLevel <= 0 ? 1 : 0;

    static ItemStatProjection? Project(ItemStatBreakpoints breakpoints, Gem? gem, int value, int upgradeLevel)
    {
        if (gem == null || breakpoints.Minimum(ItemGrade.AAA) is not int minimum) return null;
        int upgraded = value + RemainingUpgradeBonus(upgradeLevel);
        // Cheapest tier whose full set of gems reaches AAA, and the exact number of that tier actually needed.
        (string? Name, int Count) Tier(int slots)
        {
            foreach (var (tier, name) in ProjectionTiers)
                if (upgraded + slots * gem.Bonus[tier] >= minimum) return (name, Math.Max(0, (int)Math.Ceiling((minimum - upgraded) / (double)gem.Bonus[tier])));
            return (null, 0);
        }
        var five = Tier(ProjectedSlots[0]); var seven = Tier(ProjectedSlots[1]);
        return new(five.Name, five.Count, seven.Name, seven.Count, upgradeLevel >= 10);
    }
    static readonly Dictionary<ItemGemGroup, Dictionary<string, Gem>> Gems = new()
    {
        [ItemGemGroup.Armor] = new()
        {
            ["DEF"] = new("BlackMoon", [1, 2, 4, 8, 12, 16]), ["HP REG"] = new("Ruby", [1, 2, 6, 12, 18, 24]),
            ["MR"] = new("Sapphire", [1, 2, 5, 10, 15, 20]), ["EVAS"] = new("Diamond", [1, 2, 6, 12, 18, 24]),
            ["MAX HP"] = new("Emerald", [10, 20, 50, 100, 150, 200]),
        },
        [ItemGemGroup.Weapon] = new()
        {
            ["ACC"] = new("BlackMoon", [1, 2, 6, 12, 18, 24]), ["MIN"] = new("Ruby", [1, 2, 6, 12, 16, 24]),
            ["MAX"] = new("Ruby", [1, 2, 6, 12, 16, 24]), ["MAX MP"] = new("Sapphire", [10, 20, 50, 100, 150, 200]),
            ["CRI"] = new("Diamond", [1, 2, 6, 12, 18, 24]), ["MP REG"] = new("Emerald", [1, 2, 5, 10, 15, 20]),
        },
        [ItemGemGroup.Shield] = new()
        {
            ["MP REG"] = new("BlackMoon", [1, 2, 5, 10, 15, 20]), ["HP REG"] = new("Ruby", [1, 2, 6, 12, 16, 24]),
            ["MAX MP"] = new("Sapphire", [10, 20, 50, 100, 150, 200]), ["BLOCK"] = new("Diamond", [1, 2, 5, 10, 15, 20]),
            ["MAX HP"] = new("Emerald", [10, 20, 50, 100, 150, 200]),
        },
        [ItemGemGroup.Caster] = new()
        {
            ["ACC"] = new("BlackMoon", [1, 2, 6, 12, 18, 24]), ["MIN"] = new("Ruby", [1, 2, 6, 12, 16, 24]),
            ["MAX"] = new("Ruby", [1, 2, 6, 12, 16, 24]), ["MAGIC"] = new("Sapphire", [1, 2, 6, 12, 18, 24]),
            ["CRI"] = new("Diamond", [1, 2, 6, 12, 18, 24]), ["MP REG"] = new("Emerald", [1, 2, 5, 10, 15, 20]),
        },
    };

    internal static ItemGemGroup GemGroupFor(string category)
    {
        if (category.Contains("Armor", StringComparison.Ordinal) || category.Contains("Helm", StringComparison.Ordinal)) return ItemGemGroup.Armor;
        if (category.Contains("Shield", StringComparison.Ordinal)) return ItemGemGroup.Shield;
        if (category.Contains("Staff", StringComparison.Ordinal) || category.Contains("Scythe", StringComparison.Ordinal) || category.Contains("Hammer", StringComparison.Ordinal)) return ItemGemGroup.Caster;
        return ItemGemGroup.Weapon;
    }

    /// <summary>values: stat label to live tooltip value, in the table's stat labels. Unknown stats are listed ungraded.</summary>
    /// <summary>James's rule for the automatic target: at AAA aim for S, otherwise aim for AAA.</summary>
    internal static ItemGrade AutomaticTarget(ItemGrade grade) => grade >= ItemGrade.AAA ? ItemGrade.S : ItemGrade.AAA;

    /// <param name="upgradeLevel">The item's current upgrade level (+0..+10), for the +10 projection.</param>
    internal static ItemGradePlan Plan(ItemGradeProfile? profile, string name, int prototypeId,
        IReadOnlyDictionary<string, int> values, ItemGrade? fixedTarget, int upgradeLevel = 0)
    {
        var gems = profile == null ? null : Gems[profile.GemGroup];
        var stats = new List<ItemStatPlan>();
        foreach (var (stat, value) in values.OrderBy(pair => ItemGradeTable.StatRank(pair.Key)).ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var breakpoints = profile?.Stats.GetValueOrDefault(stat);
            if (breakpoints == null) { stats.Add(new(stat, value, null, null, null, null, null)); continue; }
            var gem = gems?.GetValueOrDefault(stat);
            var grade = breakpoints.GradeOf(value);
            var target = fixedTarget ?? AutomaticTarget(grade);
            int? needed = breakpoints.Minimum(target) is int minimum ? Math.Max(0, minimum - value) : null;
            IReadOnlyList<ItemGemOption>? options = null;
            if (gem != null && needed is int gap)
                options = gap == 0 ? [] : ShownTiers.Select(tier => new ItemGemOption(tier.Prefix + gem.Name,
                    checked((int)Math.Ceiling(gap / (double)gem.Bonus[tier.Tier])))).ToArray();
            stats.Add(new(stat, value, grade, target, needed, gem?.Name, options, grade < ItemGrade.AAA ? Project(breakpoints, gem, value, upgradeLevel) : null));
        }
        // Ruby raises minimum and maximum damage together, but each row shows its own count so the exact gap stays readable.
        var grades = stats.Where(s => s.Grade.HasValue).Select(s => s.Grade!.Value).ToArray();
        string label = "?";
        if (grades.Length > 0)
        {
            var best = grades.Max();
            int ties = grades.Count(g => g == best) - 1;
            label = best + (ties > 0 ? $"(+{ties})" : "");
        }
        return new(profile, name, prototypeId, fixedTarget, stats, label);
    }

    internal static string Describe(ItemGradePlan plan)
    {
        var lines = new List<string>();
        lines.Add(plan.Profile is { } it
            ? $"{it.Name}  (#{it.Id}, {it.Requirement} {it.RequirementStat})  item grade {plan.ItemGradeLabel}"
            : $"No grade data for: {plan.Name} (#{plan.PrototypeId})");
        foreach (var s in plan.Stats)
        {
            string tail = s.Needed switch
            {
                null => "",
                0 => $"at {s.Target} or better",
                int need => $"+{need} to {s.Target}" + (s.Gems is { Count: > 0 } ? "  ->  " + string.Join(" / ", s.Gems.Select(g => $"{g.Name} x{g.Count}"))
                    : s.Gems == null ? "  (no gem for this stat)" : "")
            };
            lines.Add($"  {s.Label,-12} {s.Value,6}  {(s.Grade?.ToString() ?? "?"),-3}  {tail}" + (s.Projection is { } p ? "  [" + p.Describe() + "]" : ""));
        }
        return string.Join(Environment.NewLine, lines);
    }

    internal static void SelfTest()
    {
        var helmet = ItemGradeTable.Find(101)!;
        var plan = Plan(helmet, helmet.Name, 101, new Dictionary<string, int> { ["DEF"] = 100, ["MAX HP"] = 2000, ["MR"] = 50, ["ACC"] = 7 }, ItemGrade.AAA);
        if (plan.Stats.Select(s => s.Stat).SequenceEqual(["ACC", "DEF", "MAX HP", "MR"]) == false) throw new Exception("Stat rows are not in tooltip order.");
        var defense = plan.Stats.Single(s => s.Stat == "DEF");
        if (defense.Grade != ItemGrade.AA || defense.Needed != 73 || defense.GemName != "BlackMoon" ||
            defense.Gems == null || !defense.Gems.Select(g => (g.Name, g.Count)).SequenceEqual([("Perfect BlackMoon", 5), ("Flawless BlackMoon", 7), ("Great BlackMoon", 10), ("BlackMoon", 19)]))
            throw new Exception("Armor defense gem plan was computed incorrectly: " + Describe(plan));
        var health = plan.Stats.Single(s => s.Stat == "MAX HP");
        if (health.Grade != ItemGrade.AAA || health.Needed != 0 || health.Gems is not { Count: 0 }) throw new Exception("A stat already at target should need no gems.");
        var resist = plan.Stats.Single(s => s.Stat == "MR");
        if (resist.Grade != ItemGrade.S || resist.Needed != 0) throw new Exception("A stat above target should report S with nothing needed.");
        var accuracy = plan.Stats.Single(s => s.Stat == "ACC");
        if (accuracy.Graded || accuracy.Needed != null || accuracy.Gems != null) throw new Exception("A stat the table lacks must stay ungraded.");
        if (plan.ItemGradeLabel != "S") throw new Exception("Item grade label should be the best stat grade: " + plan.ItemGradeLabel);

        var bow = ItemGradeTable.Find(1301)!;
        var weapon = Plan(bow, bow.Name, 1301, new Dictionary<string, int> { ["MIN"] = 150, ["MAX"] = 100, ["CRI"] = 1 }, ItemGrade.AA);
        var min = weapon.Stats.Single(s => s.Stat == "MIN"); var max = weapon.Stats.Single(s => s.Stat == "MAX");
        if (min.Needed != 24 || max.Needed == null || max.Needed <= min.Needed || min.Gems == null || max.Gems == null ||
            min.Gems[0].Count != 1 || max.Gems[0].Count != (int)Math.Ceiling(max.Needed.Value / 24.0))
            throw new Exception("Each damage row should show its own Ruby count: " + Describe(weapon));
        var tied = Plan(helmet, helmet.Name, 101, new Dictionary<string, int> { ["DEF"] = 100, ["EVAS"] = 24, ["MR"] = 10 }, ItemGrade.AAA);
        if (tied.ItemGradeLabel != "AA(+1)") throw new Exception("Tied stat grades should be counted in the item grade label: " + tied.ItemGradeLabel);

        var unknown = Plan(null, "Mystery", 9, new Dictionary<string, int> { ["DEF"] = 3 }, ItemGrade.S);
        if (unknown.ItemGradeLabel != "?" || unknown.Stats.Single().Graded || !Describe(unknown).StartsWith("No grade data", StringComparison.Ordinal))
            throw new Exception("Items outside the table should be reported without grades.");
        var automatic = Plan(helmet, helmet.Name, 101, new Dictionary<string, int> { ["DEF"] = 100, ["MAX HP"] = 2000, ["MR"] = 50, ["EVAS"] = 10 }, null);
        var autoDefense = automatic.Stats.Single(s => s.Stat == "DEF"); var autoHealth = automatic.Stats.Single(s => s.Stat == "MAX HP");
        if (autoDefense.Target != ItemGrade.AAA || autoDefense.Needed != 73 || autoHealth.Target != ItemGrade.S || autoHealth.Needed != 920 ||
            autoHealth.Gems == null || autoHealth.Gems[0].Count != 5 || automatic.Stats.Single(s => s.Stat == "MR").Needed != 0 ||
            automatic.Stats.Single(s => s.Stat == "EVAS").Target != ItemGrade.AAA || automatic.TargetLabel != "AAA, then S" || plan.TargetLabel != "AAA")
            throw new Exception("The automatic target should aim AAA stats at S and everything else at AAA: " + Describe(automatic));
        var armour = ItemGradeTable.Find(10462)!;
        var topB = Plan(armour, armour.Name, 10462, new Dictionary<string, int> { ["EVAS"] = 84, ["HP REG"] = 63, ["MAX HP"] = 1479, ["DEF"] = 128 }, null);
        var evasion = topB.Stats.Single(s => s.Stat == "EVAS").Projection!; var regen = topB.Stats.Single(s => s.Stat == "HP REG").Projection!;
        var hp = topB.Stats.Single(s => s.Stat == "MAX HP").Projection!; var def = topB.Stats.Single(s => s.Stat == "DEF").Projection!;
        if (evasion.FiveSlots != "Great" || evasion.SevenSlots != RegularTierName || evasion.FiveRegular || regen.FiveSlots != "Great" || regen.SevenSlots != RegularTierName ||
            hp.FiveSlots != "Great" || hp.SevenSlots != "Great" || def.FiveSlots != "Flawless" || def.FiveCount != 4 || def.SevenSlots != "Great")
            throw new Exception("The +10 projection from a top-B Mat Ratsong is wrong: " + Describe(topB));
        if (evasion.Describe() != "AAA at +10 with 4 Great or 7 regular (7 slots)" || hp.Describe() != "AAA at +10 with 5 Great" || evasion.FiveCount != 4 || evasion.SevenCount != 7)
            throw new Exception("Projection wording: " + evasion.Describe());
        var plusTen = Plan(armour, armour.Name, 10462, new Dictionary<string, int> { ["EVAS"] = 100, ["DEF"] = 130 }, null, upgradeLevel: 10);
        if (!plusTen.Stats.Single(s => s.Stat == "EVAS").Projection!.FiveRegular || plusTen.Stats.Single(s => s.Stat == "DEF").Projection!.FiveSlots != "Flawless" ||
            RemainingUpgradeBonus(10) != 0 || RemainingUpgradeBonus(5) != 0 || RemainingUpgradeBonus(3) != 0 || RemainingUpgradeBonus(0) != 1)
            throw new Exception("A +10 item gets no further upgrade points: " + Describe(plusTen));
        string plusTenWording = plusTen.Stats.Single(s => s.Stat == "DEF").Projection!.Describe();
        if (!plusTenWording.StartsWith("AAA with ", StringComparison.Ordinal) || plusTenWording.Contains("+10") || !plusTen.Stats.Single(s => s.Stat == "EVAS").Projection!.AtPlusTen || evasion.AtPlusTen)
            throw new Exception("An item already at +10 is worded without 'at +10': " + plusTenWording);
        if (topB.Stats.Any(s => s.Projection != null && s.Grade >= ItemGrade.AAA) || health.Projection != null) throw new Exception("Stats already at AAA need no projection.");
        var staff = ItemGradeTable.Find(1501)!;
        var caster = Plan(staff, staff.Name, 1501, new Dictionary<string, int> { ["MAGIC"] = 200, ["MAX MP"] = 500 }, ItemGrade.AAA);
        if (caster.Stats.Single(s => s.Stat == "MAGIC").GemName != "Sapphire" || caster.Stats.Single(s => s.Stat == "MAX MP").GemName != null)
            throw new Exception("Caster gems should cover Magic with Sapphire and leave Max Mana without a gem.");
    }
}
