namespace PoteHunter;

public static class Targeting
{
    static string ExpectedUnprefixedMonsterModel(uint id) => (id & 0xf0000000) != 0x80000000 ? "" : (id & 0xffff) switch
    {
        5970 => "MON_SnowGun2.GCMDS", // Gamekeeper
        5971 => "MON_mimic.GCMDS",    // Mimic
        5972 => "MON_SsangNom.GCMDS", // Tribal
        5973 => "mon_pulkhan02.GCMDS", // Pulkhan
        5974 => "Mon_Tower.GCMDS",    // Tower
        _ => ""
    };

    public static bool IsKnownUnprefixedMonster(uint id, string model) =>
        ExpectedUnprefixedMonsterModel(id) is { Length: > 0 } expected &&
        // The client has emitted both the bare model name and a resource/path
        // qualified name across builds. Keep the prototype/id pairing strict,
        // while accepting a qualified model that still ends in the verified
        // prototype name.
        !string.IsNullOrWhiteSpace(model) &&
        model.Trim().EndsWith(expected, StringComparison.OrdinalIgnoreCase);

    public static bool MatchesName(string entityName, string nameFilter)
    {
        if (string.IsNullOrWhiteSpace(nameFilter)) return true;
        nameFilter = nameFilter.Trim();
        if (entityName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)) return true;
        string singular = nameFilter;
        if (singular.Length > 3 && singular.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            singular = singular[..^1];
            if (entityName.Contains(singular, StringComparison.OrdinalIgnoreCase)) return true;
        }
        static string NormalizeVariant(string s) => s.Replace("pulkhan", "pulkan", StringComparison.OrdinalIgnoreCase);
        string normEntity = NormalizeVariant(entityName);
        if (normEntity.Contains(NormalizeVariant(nameFilter), StringComparison.OrdinalIgnoreCase) ||
            normEntity.Contains(NormalizeVariant(singular), StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    static string ExpectedPriorityModel(uint id) => (id & 0xf0000000) != 0x80000000 ? "" : (id & 0xffff) switch
    {
        5983 => "NPC_AG_Container.gcmds",
        5984 => "NPC_AG_Barrel.gcmds",
        3007 or 3008 or 3009 => "MON_luckybag.GCMDS",
        _ => ""
    };
    public static bool HasPriorityPrototype(uint id) => ExpectedPriorityModel(id).Length > 0;

    public static bool IsPriorityLootObject(uint id, string model) => HasPriorityPrototype(id) &&
        model.Equals(ExpectedPriorityModel(id), StringComparison.OrdinalIgnoreCase);

    public static string PriorityLabel(uint id) => (id & 0xffff) switch { 5983 => "Unnamed box", 5984 => "Unnamed barrel", _ => "Treasure Box" };

    public static bool IsChest(Entity entity)
    {
        if (entity.Monster && !entity.PriorityLootObject) return false;
        if (entity.Name.Contains("Mimic", StringComparison.OrdinalIgnoreCase)) return false;
        if (entity.PriorityLootObject) return true;
        if ((entity.Id & 0xc0000000) == 0) return false;
        return entity.Model.Contains("keybox", StringComparison.OrdinalIgnoreCase) ||
               entity.Model.Contains("luckybag", StringComparison.OrdinalIgnoreCase) ||
               entity.Model.Contains("container", StringComparison.OrdinalIgnoreCase) ||
               entity.Name.Contains("Treasure", StringComparison.OrdinalIgnoreCase) ||
               entity.Name.Contains("Chest", StringComparison.OrdinalIgnoreCase);
    }

    public static string ChestLabel(Entity entity)
    {
        if (entity.PriorityLootObject) return PriorityLabel(entity.Id);
        string name = entity.DisplayName;
        if (!string.IsNullOrWhiteSpace(name) && !name.Equals("Unnamed object", StringComparison.OrdinalIgnoreCase))
            return name.Trim();
        if (entity.Model.Contains("keybox", StringComparison.OrdinalIgnoreCase)) return "Treasure Chest";
        if (entity.Model.Contains("container", StringComparison.OrdinalIgnoreCase)) return "Box";
        if (entity.Model.Contains("barrel", StringComparison.OrdinalIgnoreCase)) return "Barrel";
        return "Chest";
    }

    public static bool IsGamekeeper(Entity entity) => (entity.Id & 0xffff) == 5970 && IsKnownUnprefixedMonster(entity.Id,entity.Model);
    // These farm targets are stationary melee assignments. They can turn and
    // attack when they enter reach, but the character does not chase them away
    // from the saved hunt point. Gamekeeper priority remains the exception.
    public static bool IsStationaryHuntTargetId(uint id) =>
        (id & 0xffff) is 5971 or 5972 or 5973 or 5974;
    public static bool IsStationaryHuntTarget(Entity entity) =>
        IsStationaryHuntTargetId(entity.Id) && IsKnownUnprefixedMonster(entity.Id,entity.Model);
    public static bool IsStationaryHuntFilter(string? filter)
    {
        if(string.IsNullOrWhiteSpace(filter))return false;
        string normalized=filter.Trim().Replace("pulkhan","pulkan",StringComparison.OrdinalIgnoreCase);
        return normalized.Contains("mimic",StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("tribal",StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("pulkan",StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("tower",StringComparison.OrdinalIgnoreCase);
    }
    public const double StationaryAssistMaximumStep=1.5;
    public static bool StationarySwingInRange(double distance,double range,bool alreadyHeld) =>
        double.IsFinite(distance) && distance>=0 && double.IsFinite(range) && range>0 &&
        distance<=range+(alreadyHeld ? .35 : 0);

    public static bool StationaryAttackNeedsRearm(int health,bool held,long now,ref int previousHealth,ref long damageAt)
    {
        // Regeneration is not evidence that our held swing connected.
        if(previousHealth<0 || health<previousHealth)damageAt=now;
        previousHealth=health;
        return !held || now-damageAt>=2500;
    }

    // Callers supply only protected, owned engaged members. Never pull a fresh
    // target to retain a swing; use living readable HP and the actual range.
    public static Entity? ChooseStationaryEngaged(IEnumerable<Entity> engaged,IReadOnlyDictionary<uint,Health> health,
        Vec position,double range) => !position.Finite || !double.IsFinite(range) || range<=0 ? null :
        engaged.Where(e=>e.Targetable && IsStationaryHuntTargetId(e.Id) && e.Position.Finite &&
            health.GetValueOrDefault(e.Id) is {Known:true,Dead:false} && (e.Position-position).Length<=range)
        .OrderByDescending(e=>health[e.Id].Current).ThenBy(e=>(e.Position-position).Length).ThenBy(e=>e.Id).FirstOrDefault();
    public static bool TryStationaryAssistStep(double targetDistance,double swingWindow,out double step)
    {
        step=0;
        if(!double.IsFinite(targetDistance) || !double.IsFinite(swingWindow) || swingWindow<0 ||
            targetDistance<=swingWindow || targetDistance>swingWindow+StationaryAssistMaximumStep)return false;
        step=Math.Clamp(targetDistance-swingWindow+.05,.1,StationaryAssistMaximumStep);
        return true;
    }
    public static bool ShouldHoldStationaryAnchor(Entity? lockedTarget, Options options, Vec position, Vec anchor, Health targetHealth)
    {
        // Fixed Mimic/Tribal/Pulkhan/Tower assignments must not trigger the
        // generic incoming-damage sidestep while the character is still at the
        // activation point. The damage response can otherwise move the player
        // away from the saved point before the encounter has been registered.
        return !options.GroupMode && lockedTarget is Entity target &&
            IsStationaryHuntTargetId(target.Id) && position.Finite && anchor.Finite &&
            (position-anchor).Length<=1.5 && !targetHealth.Dead;
    }
    public static int PriorityRank(Entity entity, bool prioritizeGamekeeper, bool prioritizeBreakables = true) =>
        prioritizeGamekeeper && IsGamekeeper(entity) ? 2 : prioritizeBreakables && entity.PriorityLootObject ? 1 : 0;
    public static double ResponseRadius(double huntRadius,double gamekeeperRadius) => Math.Max(huntRadius,gamekeeperRadius);
    // Engine-verified attack ranges in bot units (bot positions = engine wire units =
    // internal units / 100). Source: WC+Serv client CharacterControl.cpp -
    // ATTACKABLE_RANGE 150 (melee gate), fLength 1800 (bow/crossbow), +600 for class-8 archers.
    public const double MeleeAttackRange = 1.5;
    public const double BowAttackRange = 18;
    public const double ArcherAttackRange = 24;

    public static double AttackRange(bool ranged, double configured)
    {
        if (!double.IsFinite(configured) || configured < 0) throw new ArgumentOutOfRangeException(nameof(configured));
        return ranged ? Math.Max(configured, BowAttackRange) : configured;
    }

    public static double AttackRange(bool ranged, bool archerClass, double configured)
    {
        if (!double.IsFinite(configured) || configured < 0) throw new ArgumentOutOfRangeException(nameof(configured));
        if (!ranged && archerClass) throw new ArgumentException("The archer profile requires ranged combat.", nameof(archerClass));
        return !ranged ? configured : Math.Max(configured, archerClass ? ArcherAttackRange : BowAttackRange);
    }
    public static double TargetRadius(Entity entity,bool prioritizeGamekeeper,double huntRadius,double gamekeeperRadius) =>
        prioritizeGamekeeper && IsGamekeeper(entity) ? ResponseRadius(huntRadius,gamekeeperRadius) : huntRadius;

    public static double CompletionRadius(double baseRadius,double nearbyRadius,double meleeRange)
    {
        if(!double.IsFinite(baseRadius) || baseRadius<=0) throw new ArgumentOutOfRangeException(nameof(baseRadius));
        if(!double.IsFinite(nearbyRadius) || nearbyRadius<0) throw new ArgumentOutOfRangeException(nameof(nearbyRadius));
        if(!double.IsFinite(meleeRange) || meleeRange<0) throw new ArgumentOutOfRangeException(nameof(meleeRange));
        return baseRadius+Math.Clamp(Math.Max(nearbyRadius,meleeRange+2),3,8);
    }

    public static bool BoundaryStepAllowed(Vec from,Vec to,Vec anchor,double radius)
    {
        if(!from.Finite || !to.Finite || !anchor.Finite || !double.IsFinite(radius) || radius<=0) return false;
        double before=(from-anchor).Length,after=(to-anchor).Length;
        if(!double.IsFinite(before) || !double.IsFinite(after)) return false;
        return after<=radius+1e-9 || before>radius && after<before-1e-9;
    }

    public static Entity? ChooseEngagedFirst(Encounter encounter, Func<Entity?> chooseFresh) =>
        encounter.HasEngaged
            ? encounter.TrackedEngagedCandidates.FirstOrDefault() ?? (encounter.HasUnresolvedEngaged ? null : chooseFresh())
            : chooseFresh();

    // Callers supply candidates that already passed avoidance, route and ownership protections.
    // Emergency monster priority spans the hunt area, even while clearing a smaller encounter.
    public static Entity? ChooseUrgent(IEnumerable<Entity> candidates, IReadOnlyDictionary<uint,Health> health,
        Vec position, Vec anchor, double radius, bool enabled) => enabled ?
        Choose(candidates.Where(IsGamekeeper),health,position,anchor,radius,_=>Threat.Unknown,"",[],prioritizeGamekeeper:true,prioritizeBreakables:false) : null;

    public static Entity? ChooseEncounter(IEnumerable<Entity> protectedCandidates, IEnumerable<Entity> encounterCandidates,
        IReadOnlyDictionary<uint,Health> health, Vec position, Vec anchor, double radius, bool prioritizeGamekeeper, bool prioritizeBreakables = true) =>
        ChooseUrgent(protectedCandidates,health,position,anchor,radius,prioritizeGamekeeper) ??
        encounterCandidates.OrderByDescending(e=>PriorityRank(e,prioritizeGamekeeper,prioritizeBreakables)).FirstOrDefault();

    public static bool Eligible(Entity entity, Health health, Threat difficulty, string nameFilter, IReadOnlyCollection<string> colors, bool prioritizeGamekeeper = false) =>
        health.Known && !health.Dead && (entity.PriorityLootObject ||
        prioritizeGamekeeper && IsGamekeeper(entity) ||
        entity.Monster && MatchesName(entity.Name, nameFilter) && colors.Contains(difficulty.ToString()));

    public static Entity? Choose(IEnumerable<Entity> entities, IReadOnlyDictionary<uint, Health> health, Vec position, Vec anchor,
        double radius, Func<Entity, Threat> difficulty, string nameFilter, IReadOnlyCollection<string> colors, bool priorityOnly = false, bool prioritizeGamekeeper = false, bool prioritizeBreakables = true)
    {
        var candidates=entities.Where(e => e.Targetable && (!priorityOnly || PriorityRank(e,prioritizeGamekeeper,prioritizeBreakables)>0) && e.Position.Finite &&
                (e.Position - anchor).Length <= radius &&
                Eligible(e, health.GetValueOrDefault(e.Id), e.PriorityLootObject ? Threat.Unknown : difficulty(e), nameFilter, colors,prioritizeGamekeeper)).ToArray();
        if(!prioritizeBreakables && candidates.Any(e=>!e.PriorityLootObject))candidates=candidates.Where(e=>!e.PriorityLootObject).ToArray();
        return candidates.OrderByDescending(e => PriorityRank(e,prioritizeGamekeeper,prioritizeBreakables)).ThenBy(e => (e.Position - position).Length).ThenBy(e => e.Id).FirstOrDefault();
    }

    public static int LootHoldMilliseconds(Entity target, decimal configured) => target.PriorityLootObject ? Math.Max(800, (int)configured) : (int)configured;

    public static void CompletionSelfTest()
    {
        if (AttackRange(false, false, 2) != 2 || AttackRange(true, false, 2) != BowAttackRange ||
            AttackRange(true, true, BowAttackRange) != ArcherAttackRange)
            throw new Exception("Ranged class attack ranges were not normalized.");
        bool invalidArcher = false;
        try { AttackRange(false, true, 2); } catch (ArgumentException) { invalidArcher = true; }
        if (!invalidArcher) throw new Exception("An archer profile was accepted without ranged combat.");

        Vec anchor=new(0,0),player=new(19.5,0);
        double finishRadius=CompletionRadius(20,6,1);
        if(finishRadius!=26 || CompletionRadius(20,100,100)!=28 || CompletionRadius(20,0,0)!=23)
            throw new Exception("Engaged completion allowance was not bounded to three through eight units.");
        foreach(var arguments in new[]{(double.NaN,6d,1d),(20d,double.PositiveInfinity,1d),(20d,6d,-1d),(0d,6d,1d)})
        {
            bool rejected=false;
            try {CompletionRadius(arguments.Item1,arguments.Item2,arguments.Item3);} catch(ArgumentOutOfRangeException) {rejected=true;}
            if(!rejected) throw new Exception("Invalid completion radius settings were accepted.");
        }
        var original=new Entity(100,0x80000001,"Lv. 1 Mob",new Vec(19.8,0),0,Generation:1);
        var sidestepped=original with {Position=new Vec(20.2,0)};
        var fresh=original with {Address=200,Id=0x80000002,Position=new Vec(20.2,.2)};
        var health=new Dictionary<uint,Health>{{original.Id,new(90,100)},{fresh.Id,new(100,100)}};
        var encounter=new Encounter();encounter.MarkAttack(original,new(100,100));
        bool FreshEligible(Entity entity,Health hp)=>(entity.Position-anchor).Length<=20 && Eligible(entity,hp,Threat.Green,"Mob",["Green"]);
        bool FinishEligible(Entity entity,Health hp)=>(entity.Position-anchor).Length<=finishRadius && hp.Known && !hp.Dead;
        encounter.Observe([sidestepped,fresh],health,player,6,FreshEligible,FinishEligible,clearNearby:false);
        int freshSelections=0;
        Entity? ChooseFresh(){freshSelections++;return Choose([fresh],health,player,anchor,20,_=>Threat.Green,"Mob",["Green"]);}
        if(ChooseEngagedFirst(encounter,ChooseFresh)?.Id!=original.Id || freshSelections!=0 || !encounter.IsEngaged(sidestepped) || encounter.IsEngaged(fresh))
            throw new Exception("A sidestep across the fresh hunt boundary lost the engagement or allowed a fresh pull.");
        if(ChooseFresh()!=null) throw new Exception("The completion allowance expanded fresh target selection.");
        var escaped=sidestepped with {Position=new Vec(26.1,0)};
        encounter.Observe([escaped,fresh],health,player,6,FreshEligible,FinishEligible,clearNearby:false);
        if(!encounter.HasUnresolvedEngaged || ChooseEngagedFirst(encounter,ChooseFresh)!=null || freshSelections!=1)
            throw new Exception("An enemy beyond the completion limit was chased or silently replaced with a fresh target.");
        health[original.Id]=new(0,100);
        encounter.Observe([escaped,fresh],health,player,6,FreshEligible,FinishEligible,clearNearby:false);
        if(encounter.HasEngaged || !BoundaryStepAllowed(new Vec(26.2,0),new Vec(25.7,0),anchor,26) ||
            !BoundaryStepAllowed(new Vec(26.2,0),anchor,anchor,20) ||
            BoundaryStepAllowed(new Vec(25.8,0),new Vec(26.2,0),anchor,26) ||
            BoundaryStepAllowed(new Vec(26.2,0),new Vec(26.3,0),anchor,26) ||
            BoundaryStepAllowed(new Vec(26.2,0),new Vec(26.2,0),anchor,26) ||
            BoundaryStepAllowed(player,new Vec(double.NaN,0),anchor,26))
            throw new Exception("The completion movement boundary blocked return or permitted outward movement.");

        var fixedTarget=new Entity(301,0x80001753,"Mimic",new Vec(),0,Model:"MON_mimic.GCMDS");
        var fixedOptions=new Options { GroupMode=false };
        if(!ShouldHoldStationaryAnchor(fixedTarget,fixedOptions,new Vec(1,0),anchor,new(40,100)) ||
            ShouldHoldStationaryAnchor(fixedTarget,fixedOptions,new Vec(1.6,0),anchor,new(40,100)) ||
            ShouldHoldStationaryAnchor(fixedTarget with { Id=0x80001760 },fixedOptions,new Vec(1,0),anchor,new(40,100)) ||
            ShouldHoldStationaryAnchor(fixedTarget,fixedOptions,new Vec(1,0),anchor,new(0,100)) ||
            !ShouldHoldStationaryAnchor(fixedTarget,fixedOptions,new Vec(1,0),anchor,new()))
            throw new Exception("Fixed-target damage response did not honor the saved anchor guard.");
        if(ShouldHoldStationaryAnchor(fixedTarget,new Options { GroupMode=true },new Vec(1,0),anchor,new(40,100)))
            throw new Exception("Group mode incorrectly inherited the stationary anchor damage guard.");
        if(!TryStationaryAssistStep(3.5,2.5,out var assistStep) || assistStep>StationaryAssistMaximumStep || assistStep<1.0 ||
            TryStationaryAssistStep(4.01,2.5,out _) || TryStationaryAssistStep(2.5,2.5,out _))
            throw new Exception("Stationary melee assist exceeded its bounded step or admitted an out-of-range target.");
        if(!StationarySwingInRange(2.5,2.5,false) || StationarySwingInRange(2.6,2.5,false) ||
            !StationarySwingInRange(2.8,2.5,true) || StationarySwingInRange(2.86,2.5,true) ||
            StationarySwingInRange(double.NaN,2.5,true) || StationarySwingInRange(1,0,true))
            throw new Exception("Stationary attack entry/release margins were inconsistent.");
        int previousHealth=-1;long damageAt=0;
        if(StationaryAttackNeedsRearm(80,true,1000,ref previousHealth,ref damageAt) ||
            !StationaryAttackNeedsRearm(90,true,3500,ref previousHealth,ref damageAt) ||
            StationaryAttackNeedsRearm(70,true,3600,ref previousHealth,ref damageAt) ||
            StationaryAttackNeedsRearm(75,true,6000,ref previousHealth,ref damageAt) ||
            !StationaryAttackNeedsRearm(76,true,6100,ref previousHealth,ref damageAt) ||
            !StationaryAttackNeedsRearm(76,false,6101,ref previousHealth,ref damageAt))
            throw new Exception("Target regeneration suppressed the stationary no-damage attack watchdog.");
        var close=fixedTarget with{Position=new(1.5,0)};
        var high=close with{Id=0x80011753,Position=new(2,0)};
        var distant=close with{Id=0x80021753,Position=new(3,0)};
        var stationaryHp=new Dictionary<uint,Health>{{close.Id,new(40,100)},{high.Id,new(80,100)},{distant.Id,new(100,100)}};
        if(ChooseStationaryEngaged([close,high,distant],stationaryHp,anchor,2.5)?.Id!=high.Id)
            throw new Exception("Stationary swing did not select the most-health engaged enemy inside range.");
        stationaryHp[high.Id]=new(0,100);stationaryHp[close.Id]=new();
        if(ChooseStationaryEngaged([close,high,distant],stationaryHp,anchor,2.5)!=null)
            throw new Exception("Unknown/dead/out-of-range enemies retained a stationary attack.");
        if(!IsStationaryHuntFilter("Tribal") || !IsStationaryHuntFilter("Pulkhan") ||
            !IsStationaryHuntFilter("Tower") || !IsStationaryHuntFilter("Mimic") ||
            IsStationaryHuntFilter("Green"))
            throw new Exception("Stationary hunt target filters were not classified consistently.");

        // Incidental enemies in an authorized Gamekeeper fight share that fight's
        // completion area; it must not become a wider fresh-monster search.
        var keeper=new Entity(300,0x80001752,"Gamekeeper",new Vec(40,0),0,Generation:2,Model:"MON_SnowGun2.GCMDS");
        var add=fresh with {Position=new Vec(41,0)};
        var mixed=new Encounter();mixed.MarkAttack(keeper,new(100,100));mixed.MarkAttack(add,new(100,100));
        var mixedHp=new Dictionary<uint,Health>{{keeper.Id,new(0,100)},{add.Id,new(80,100)}};
        double shared=CompletionRadius(TargetRadius(keeper,true,20,60),10,1);
        mixed.Observe([keeper,add],mixedHp,new Vec(40,0),10,FreshEligible,
            (entity,hp)=>hp.Known && !hp.Dead && (entity.Position-anchor).Length<=shared,clearNearby:false);
        if(shared!=68 || ChooseEngagedFirst(mixed,()=>null)?.Id!=add.Id ||
            Choose([add],mixedHp,new Vec(40,0),anchor,20,_=>Threat.Green,"Mob",["Green"])!=null)
            throw new Exception("A surviving collateral monster lost its shared completion area or expanded fresh selection.");
    }
}

