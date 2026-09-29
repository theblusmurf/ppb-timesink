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
        Vec player, double packRadius, bool centerArea, bool retargetSingle)
    {
        // The encounter roster has already passed monster/target protection checks.
        // Keep skill target changes inside the same nearby-enemy boundary used
        // by the five-target skill gate.  A target outside that boundary may
        // remain the locked swing target, but it must not pull a skill toward a
        // distant engaged enemy and make the character chase it.
        var effectiveRange = double.IsFinite(packRadius) ? Math.Max(0, packRadius) : 0;
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
    }
}
