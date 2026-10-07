namespace PoteHunter;

public static class SkillTargeting
{
    public static bool IsAreaOrLine(HotbarSlot skill)
    {
        if (skill.SkillTarget == SkillTargetKind.EnemyLine) return true;
        var text = (skill.Name + " " + skill.Description).ToLowerInvariant();
        return new[] { "area", "aoe", "line", "cone", "radius", "nearby", "surround" }.Any(text.Contains);
    }

    public static Entity? Choose(HotbarSlot skill, Entity current, IEnumerable<Entity> engaged, IReadOnlyDictionary<uint, Health> health,
        Vec player, double packRadius, bool centerArea, bool retargetSingle, bool prioritizeGamekeeper = false)
    {
        // The encounter roster has already passed monster/target protection checks.
        // Callers supply actual attack reach, which may be narrower than the
        // five-target pack-count radius. Do not select a distant pack member
        // that basic swings immediately replace with an in-range target.
        var effectiveRange = double.IsFinite(packRadius) ? Math.Max(0, packRadius) : 0;
        // The current target has already passed the caller's identity and
        // protection checks. Do not let skill ranking displace an active,
        // verified Gamekeeper with ordinary engaged enemies. This exception
        // retains only that current target; it never adds fresh candidates.
        if (prioritizeGamekeeper && Targeting.IsGamekeeper(current) &&
            health.TryGetValue(current.Id, out var currentHealth) && currentHealth.Known && !currentHealth.Dead)
            // A valid keeper can be inside the existing combat hysteresis but
            // outside the narrower new-target admission range. In that case
            // offer no retarget rather than entering ordinary ranking, which
            // would immediately be preempted back to the same keeper.
            return current.Position.Finite && player.Finite &&
                (current.Position - player).Length <= effectiveRange ? current : null;
        var candidates = engaged.Where(e => e.Position.Finite)
            .Where(e => player.Finite && (e.Position - player).Length <= effectiveRange)
            .Where(e => health.TryGetValue(e.Id, out var hp) && hp.Known && !hp.Dead).ToArray();
        if (candidates.Length == 0) return null;
        if (IsAreaOrLine(skill))
        {
            if (!centerArea) return current;
            var clusterRadius=Math.Min(2.5,Math.Max(1.5,effectiveRange));
            return candidates.OrderByDescending(e => candidates.Count(other => (other.Position - e.Position).Length <= clusterRadius))
                .ThenByDescending(e => health[e.Id].Current)
                .ThenByDescending(e => (double)health[e.Id].Current / Math.Max(1, health[e.Id].Maximum))
                .ThenByDescending(e => e.Id == current.Id).ThenBy(e => (e.Position - player).Length).ThenBy(e => e.Id).First();
        }
        if (!retargetSingle) return current;
        // Single-target skills should keep pressure on the toughest engaged
        // enemy that is actually in range.  Prefer current health, then the
        // health percentage when two targets have the same current amount.
        return candidates.OrderByDescending(e => health[e.Id].Current)
            .ThenByDescending(e => (double)health[e.Id].Current / Math.Max(1, health[e.Id].Maximum))
            .ThenByDescending(e => e.Id == current.Id)
            .ThenBy(e => (e.Position - player).Length).ThenBy(e => e.Id).First();
    }

    public static void SelfTest()
    {
        var a = new Entity(1, 0x80000001, "Mimic", new(0, 0), 0);
        var b = new Entity(2, 0x80000002, "Mimic", new(.5, 0), 0);
        var c = new Entity(3, 0x80000003, "Mimic", new(6, 0), 0);
        var hp = new Dictionary<uint, Health> { [a.Id] = new(80,100), [b.Id] = new(20,100), [c.Id] = new(90,100) };
        var area = new HotbarSlot("1",SlotKind.Skill,1,"Cone",0,0,false,0,Description:"area cone",SkillTarget:SkillTargetKind.EnemyLine);
        var chosen=Choose(area,c,[a,b,c],hp,new(),2,true,true);
        if (chosen?.Id != a.Id && chosen?.Id != b.Id) throw new Exception($"Area skill did not choose dense pack anchor ({chosen?.Id})");
        var single = area with { SkillTarget=SkillTargetKind.Enemy, Name="Strike", Description="" };
        if (Choose(single,a,[a,b,c],hp,new(),2,false,true)?.Id != a.Id) throw new Exception("Single target did not prefer the highest-health target in range");
        if (Choose(single,c,[a,b,c],hp,new(),2,false,true)?.Id == c.Id) throw new Exception("Single target selected an engaged target outside the configured range");

        var nearby = a with { Position = new(.5, 0) };
        var distantPack = new[] { b with { Position = new(3.5, 0) }, c with { Position = new(4, 0) } };
        var pack = new[] { nearby, distantPack[0], distantPack[1] };
        if (Choose(area,nearby,pack,hp,new(),6,true,true)?.Id != c.Id ||
            Choose(area,nearby,pack,hp,new(),2,true,true)?.Id != nearby.Id ||
            Choose(single,nearby,pack,hp,new(),2,false,true)?.Id != nearby.Id)
            throw new Exception("A distant dense/high-health pack escaped the supplied melee range");

        var keeper = new Entity(100, 0x80001752, "Gamekeeper", new(1, 0), 0, Generation: 1, Model: "MON_SnowGun2.GCMDS");
        hp[keeper.Id] = new(1,100);
        // A current priority target need not have joined the ordinary encounter
        // roster yet. Retention must not depend on the roster being nonempty.
        if (Choose(single,keeper,[a,b],hp,new(),2,false,true,true) != keeper ||
            Choose(area,keeper,[a,b],hp,new(),2,true,true,true) != keeper ||
            Choose(single,keeper,[],hp,new(),2,false,true,true) != keeper)
            throw new Exception("Valid current Gamekeeper was displaced before its skill cast");
        if (Choose(single,keeper,[a,b],hp,new(),2,false,true,false)?.Id != a.Id ||
            Choose(area,keeper,[a,b],hp,new(),2,true,true,false)?.Id != a.Id)
            throw new Exception("Disabled Gamekeeper skill priority changed ordinary ranking");
        hp[keeper.Id] = new(0,100);
        if (Choose(single,keeper,[a,b],hp,new(),2,false,true,true)?.Id != a.Id)
            throw new Exception("Dead Gamekeeper retained skill priority");
        hp.Remove(keeper.Id);
        if (Choose(single,keeper,[a,b],hp,new(),2,false,true,true)?.Id != a.Id)
            throw new Exception("Unknown-health Gamekeeper retained skill priority");
        hp[keeper.Id] = new(1,100);
        if (Choose(single,keeper with { Position = new(3,0) },[a,b],hp,new(),2,false,true,true) != null ||
            Choose(single,keeper with { Position = new(double.NaN,0) },[a,b],hp,new(),2,false,true,true) != null ||
            Choose(single,keeper with { Model = "MON_mimic.GCMDS" },[a,b],hp,new(),2,false,true,true)?.Id != a.Id ||
            Choose(single,keeper,[a,b],hp,new(double.NaN,0),2,false,true,true) != null)
            throw new Exception("Invalid identity, position or range retained Gamekeeper skill priority");
        if (Choose(single,keeper with { Position = new(2.6,0) },[a,b],hp,new(),2.5,false,true,true) != null ||
            Choose(area,keeper with { Position = new(20,0) },[a,b],hp,new(),9,true,true,true) != null)
            throw new Exception("Combat hysteresis or ranged keeper focus fell through to ordinary skill ranking");
        hp[a.Id] = new(50,100); hp[b.Id] = new(51,100);
        if (Choose(single,a,[a,b],hp,new(),2,false,true,true)?.Id != b.Id)
            throw new Exception("Ordinary highest-health skill ranking acquired a margin or dwell");
        if (Choose(single,c with { Position = new(.25,0) },[a,b],hp,new(),2,false,true,true)?.Id != b.Id)
            throw new Exception("Skill priority added an ordinary current target outside the protected roster");
        SkillTargetReservation.SelfTest(single,a,b,c);
    }
}

// This reservation covers only a short pre-cast retarget/aim sequence. Every
// caller must still recheck live HP, range, focus, identity and skill gates,
// clear before any cast attempt, and allow self-healing to preempt it.
public sealed class SkillTargetReservation
{
    public const long LifetimeMilliseconds = 3000;

    readonly record struct SkillIdentity(string Key, SlotKind Kind, int Id, string Name, SkillTargetKind SkillTarget)
    {
        public static SkillIdentity Of(HotbarSlot slot) => new(slot.Key,slot.Kind,slot.Id,slot.Name,slot.SkillTarget);
    }

    readonly record struct TargetIdentity(uint Id, uint Generation, long Address, string Name, string Model)
    {
        public static TargetIdentity Of(Entity target) => new(target.Id,target.Generation,target.Address,target.Name,target.Model);
    }

    SkillIdentity skill;
    TargetIdentity targetIdentity;
    long reservedAt;
    long lastObserved;
    bool active;

    public void Reserve(HotbarSlot slot, Entity target, long now)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(target);
        skill = SkillIdentity.Of(slot);
        targetIdentity = TargetIdentity.Of(target);
        reservedAt = lastObserved = now;
        active = true;
    }

    public bool Matches(HotbarSlot slot, Entity current, long now)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(current);
        if (!active) return false;
        // Unsigned elapsed time rejects an otherwise overflowing long span.
        // Matching does not renew the original three-second deadline.
        if (now < lastObserved || unchecked((ulong)(now - reservedAt)) >= (ulong)LifetimeMilliseconds ||
            skill != SkillIdentity.Of(slot) || targetIdentity != TargetIdentity.Of(current))
        {
            Clear();
            return false;
        }
        lastObserved = now;
        return true;
    }

    public void Clear() => active = false;

    internal static void SelfTest(HotbarSlot slot, Entity a, Entity b, Entity c)
    {
        var reservation = new SkillTargetReservation();
        if (reservation.Matches(slot,a,1000)) throw new Exception("Empty skill target reservation matched");
        reservation.Reserve(slot,a,1000);
        var runtimeSlot = slot with { TotalCooldown = 7000, RemainingCooldown = 1, Locked = true, LockRemaining = 1,
            Description = "updated runtime description", ManaCost = 5 };
        var moving = a with { Position = new(1.5,1), Height = 3, Heading = 2 };
        if (!reservation.Matches(runtimeSlot,moving,1200))
            throw new Exception("Moving target or runtime cooldown changes displaced the reserved skill");

        HotbarSlot[] changedSlots = [slot with { Key = "2" }, slot with { Kind = SlotKind.Item }, slot with { Id = slot.Id + 1 },
            slot with { Name = "Other skill" }, slot with { SkillTarget = SkillTargetKind.EnemyLine }];
        foreach (var changed in changedSlots)
        {
            reservation.Reserve(slot,a,1000);
            if (reservation.Matches(changed,a,1001) || reservation.Matches(slot,a,1002))
                throw new Exception("Changed hotbar identity did not clear the skill target reservation");
        }
        Entity[] changedTargets = [b, a with { Id = a.Id + 10 }, a with { Generation = a.Generation + 1 },
            a with { Address = a.Address + 1 }, a with { Name = "Replaced target" }, a with { Model = "Other model" }];
        foreach (var changed in changedTargets)
        {
            reservation.Reserve(slot,a,1000);
            if (reservation.Matches(slot,changed,1001) || reservation.Matches(slot,a,1002))
                throw new Exception("Replaced/different target identity did not clear the skill target reservation");
        }
        reservation.Reserve(slot,a,1000);
        if (!reservation.Matches(slot,a,3999) || reservation.Matches(slot,a,4000) || reservation.Matches(slot,a,1001))
            throw new Exception("Skill target reservation extended or survived its three-second deadline");
        reservation.Reserve(slot,a,1000);
        if (!reservation.Matches(slot,a,2000) || reservation.Matches(slot,a,1999) || reservation.Matches(slot,a,2001))
            throw new Exception("Backward observation clock did not clear the skill target reservation");
        reservation.Reserve(slot,a,1000);
        if (reservation.Matches(slot,a,999) || reservation.Matches(slot,a,1001))
            throw new Exception("Backward reservation clock did not clear the skill target reservation");
        reservation.Reserve(slot,a,long.MinValue);
        if (reservation.Matches(slot,a,long.MaxValue))
            throw new Exception("Overflowing elapsed time retained the skill target reservation");
        reservation.Reserve(slot,a,long.MaxValue - 2000);
        if (!reservation.Matches(slot,a,long.MaxValue) || reservation.Matches(slot,a,long.MinValue))
            throw new Exception("Skill target reservation clock wrap was not guarded");

        // Reproduce A -> B -> C before any cast: two HP snapshots make a new
        // highest-health target available while the first retarget is aiming.
        var targets = new[] { a with { Position = new(.5,0) }, b with { Position = new(1,0) }, c with { Position = new(1.5,0) } };
        var firstHp = new Dictionary<uint,Health> { [a.Id] = new(80,100), [b.Id] = new(90,100), [c.Id] = new(70,100) };
        var nextHp = new Dictionary<uint,Health> { [a.Id] = new(80,100), [b.Id] = new(85,100), [c.Id] = new(95,100) };
        var first = SkillTargeting.Choose(slot,targets[0],targets,firstHp,new(),2,false,true)!;
        var unreservedNext = SkillTargeting.Choose(slot,first,targets,nextHp,new(),2,false,true)!;
        if (first.Id != b.Id || unreservedNext.Id != c.Id)
            throw new Exception("Pre-cast target churn regression scene did not reproduce A -> B -> C");
        reservation.Reserve(slot,first,1000);
        var movedFirst = first with { Position = new(1.25,0) };
        var reservedNext = reservation.Matches(runtimeSlot,movedFirst,1100) ? movedFirst :
            SkillTargeting.Choose(slot,movedFirst,targets,nextHp,new(),2,false,true);
        if (reservedNext?.Id != b.Id)
            throw new Exception("HP changes restarted skill ranking during the reserved pre-cast retarget");
        reservation.Clear(); // Caller clears before attempting either a successful or failed cast.
        if (reservation.Matches(slot,movedFirst,1200) ||
            SkillTargeting.Choose(slot,movedFirst,targets,nextHp,new(),2,false,true)?.Id != c.Id)
            throw new Exception("Cast-attempt clearing retained the old target or changed ordinary ranking");
    }
}
