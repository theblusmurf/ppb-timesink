namespace PoteHunter;

public sealed class HuntExcursion
{
    public Vec Anchor { get; }
    public double HuntRadius { get; }
    public double OutsideSearchRadius { get; }
    public bool OutsideTrip { get; private set; }
    public bool ReturnPending { get; private set; }
    public double MovementBoundary { get; private set; }

    public HuntExcursion(Vec anchor,double huntRadius)
    {
        if(!anchor.Finite)throw new ArgumentOutOfRangeException(nameof(anchor));
        if(!double.IsFinite(huntRadius) || huntRadius<=0 || !double.IsFinite(huntRadius*2))
            throw new ArgumentOutOfRangeException(nameof(huntRadius));
        Anchor=anchor;HuntRadius=huntRadius;OutsideSearchRadius=huntRadius*2;MovementBoundary=huntRadius;
    }

    public void BeginOutsideTrip(Entity target,double nearbyRadius,double meleeRange)
    {
        ArgumentNullException.ThrowIfNull(target);
        if(OutsideTrip || ReturnPending)throw new InvalidOperationException("Finish the current excursion and return before starting another.");
        double distance=(target.Position-Anchor).Length;
        if(!target.Targetable || !target.Position.Finite || !double.IsFinite(distance) || distance<=HuntRadius || distance>OutsideSearchRadius)
            throw new ArgumentOutOfRangeException(nameof(target),"An excursion target must be outside the original hunt area and inside the bounded search area.");
        double boundary=Targeting.CompletionRadius(OutsideSearchRadius,nearbyRadius,meleeRange);
        MovementBoundary=boundary;OutsideTrip=true;ReturnPending=true;
    }

    // The caller also waits for its locked target and pending pickup to finish.
    public bool CanReturn(bool hasEngaged)=>OutsideTrip && ReturnPending && !hasEngaged;

    public bool TryCompleteReturn(Vec position,bool hasEngaged)
    {
        if(!CanReturn(hasEngaged) || !position.Finite || (position-Anchor).Length>HuntRadius)return false;
        OutsideTrip=false;ReturnPending=false;MovementBoundary=HuntRadius;return true;
    }
}

public static class HuntingArea
{
    // A matching creature with unknown HP or a protection conflict is still a
    // reason to stay inside. Only confirmed-dead matches clear the inside area.
    public static bool HasApprovedInside(IEnumerable<Entity> entities,IReadOnlyDictionary<uint,Health> health,
        HuntExcursion excursion,Func<Entity,bool> matchesRequested)=>
        entities.Any(entity=>Candidate(entity,matchesRequested) && (entity.Position-excursion.Anchor).Length<=excursion.HuntRadius &&
            !health.GetValueOrDefault(entity.Id).Dead);

    public static Entity? Choose(IEnumerable<Entity> entities,IReadOnlyDictionary<uint,Health> health,Vec position,
        HuntExcursion excursion,Func<Entity,bool> matchesRequested,Func<Entity,Health,bool> permitted,Func<Entity,int>? priorityRank=null,bool prioritizeBreakables=true)
    {
        ArgumentNullException.ThrowIfNull(entities);ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(excursion);ArgumentNullException.ThrowIfNull(matchesRequested);ArgumentNullException.ThrowIfNull(permitted);
        if(!position.Finite || excursion.OutsideTrip || excursion.ReturnPending)return null;
        var matching=entities.Where(entity=>Candidate(entity,matchesRequested) && !health.GetValueOrDefault(entity.Id).Dead).ToArray();
        if(!prioritizeBreakables && matching.Any(entity=>!entity.PriorityLootObject))matching=matching.Where(entity=>!entity.PriorityLootObject).ToArray();
        bool Ready(Entity entity)
        {
            var hp=health.GetValueOrDefault(entity.Id);
            return hp.Known && !hp.Dead && permitted(entity,hp);
        }
        var inside=matching.Where(entity=>(entity.Position-excursion.Anchor).Length<=excursion.HuntRadius).ToArray();
        if(inside.Length>0)
            return inside.Where(Ready).OrderByDescending(entity=>priorityRank?.Invoke(entity)??0)
                .ThenBy(entity=>(entity.Position-position).Length).ThenBy(entity=>entity.Id).FirstOrDefault();
        return matching.Where(entity=>(entity.Position-excursion.Anchor).Length<=excursion.OutsideSearchRadius && Ready(entity))
            .OrderBy(entity=>(entity.Position-position).Length).ThenBy(entity=>entity.Id).FirstOrDefault();
    }

    static bool Candidate(Entity entity,Func<Entity,bool> matchesRequested)=>
        entity!=null && entity.Targetable && entity.Position.Finite && matchesRequested(entity);

    public static void SelfTest()
    {
        static Entity Mob(uint id,string name,double x)=>new(id*100,0x80000000u|id,"Lv. 1 "+name,new Vec(x,0),0,Generation:1);
        var inside=Mob(1,"Naga",18);var outside=Mob(2,"Naga",25);var nextOutside=Mob(3,"Naga",35);
        var keeper=Mob(5970,"Gamekeeper",30) with {Model="MON_SnowGun2.GCMDS"};
        var excluded=Mob(4,"Wolf",1);var dead=Mob(5,"Naga",2);var unknown=Mob(6,"Naga",3);
        var tooFar=Mob(7,"Naga",40.1);var add=Mob(8,"Naga",26);
        Entity[] scene=[inside,outside,nextOutside,keeper,excluded,dead,tooFar];
        var health=scene.Append(add).ToDictionary(entity=>entity.Id,_=>new Health(100,100));health[dead.Id]=new(0,100);
        bool Matches(Entity entity)=>entity.Name.Contains("Naga",StringComparison.Ordinal) || Targeting.IsGamekeeper(entity);
        bool Permitted(Entity entity,Health hp)=>hp.Known && !hp.Dead;
        int Priority(Entity entity)=>Targeting.PriorityRank(entity,true);
        var area=new HuntExcursion(new Vec(0,0),20);Vec player=area.Anchor;
        Entity? Choose(IEnumerable<Entity> candidates)=>HuntingArea.Choose(candidates,health,player,area,Matches,Permitted,Priority);
        if(area.OutsideSearchRadius!=40 || area.MovementBoundary!=20 || Choose(scene)?.Id!=inside.Id)
            throw new Exception("An outside priority target displaced an approved target in the original hunt area.");
        var insideKeeper=keeper with {Position=new Vec(19,0)};
        if(Choose([inside,insideKeeper])?.Id!=keeper.Id)throw new Exception("Inside-target priority was lost.");
        if(!HasApprovedInside([unknown],health,area,Matches) || Choose([unknown,outside])!=null ||
            HuntingArea.Choose([inside,outside],health,player,area,Matches,(entity,hp)=>entity.Id!=inside.Id,Priority)!=null)
            throw new Exception("Unknown HP or a protected matching inside monster authorized an outside departure.");
        health[inside.Id]=new(0,100);
        if(HasApprovedInside([inside,dead,excluded],health,area,Matches) || Choose(scene)?.Id!=outside.Id)
            throw new Exception("Confirmed-dead or excluded inside creatures blocked a legitimate nearest outside target.");
        if(Choose([tooFar,excluded,dead])!=null)throw new Exception("Outside fallback selected an excluded or beyond-limit monster.");

        area.BeginOutsideTrip(outside,6,1);
        if(!area.OutsideTrip || !area.ReturnPending || area.MovementBoundary!=46 || Choose(scene)!=null)
            throw new Exception("An outside trip did not latch its boundary and require return before fresh selection.");
        var fight=new Encounter();fight.MarkAttack(outside,health[outside.Id]);fight.MarkAttack(add,health[add.Id]);
        player=new Vec(25,0);health[outside.Id]=new(0,100);health[add.Id]=new(80,100);
        fight.Observe([outside,add],health,player,6,(_,_)=>false,
            (entity,hp)=>hp.Known && !hp.Dead && (entity.Position-area.Anchor).Length<=area.MovementBoundary,clearNearby:false);
        if(area.CanReturn(fight.HasEngaged) || area.TryCompleteReturn(new Vec(19,0),fight.HasEngaged) ||
            Targeting.ChooseEngagedFirst(fight,()=>Choose(scene))?.Id!=add.Id)
            throw new Exception("An excursion returned or selected a fresh target before finishing a surviving engaged add.");
        health[add.Id]=new(0,100);
        fight.Observe([outside,add],health,player,6,(_,_)=>false,(_,_)=>true,clearNearby:false);
        if(!area.CanReturn(fight.HasEngaged) || area.TryCompleteReturn(player,false) || Choose([nextOutside])!=null)
            throw new Exception("A second outside pull started before reentering the original hunt area.");
        bool rejected=false;
        try {area.BeginOutsideTrip(nextOutside,6,1);}catch(InvalidOperationException){rejected=true;}
        if(!rejected)throw new Exception("A new trip replaced an excursion with a pending return.");
        player=new Vec(19,0);
        if(!area.TryCompleteReturn(player,false) || area.Anchor!=new Vec(0,0) || area.OutsideTrip || area.ReturnPending || area.MovementBoundary!=20)
            throw new Exception("Returning recentered the original anchor or left stale excursion state.");
        var newInside=Mob(9,"Naga",1);health[newInside.Id]=new(100,100);
        if(Choose([newInside,nextOutside])?.Id!=newInside.Id)
            throw new Exception("After return, a nearer outside target displaced an approved inside target.");
        if(Choose([nextOutside])?.Id!=nextOutside.Id)throw new Exception("Outside search did not resume after a completed return and empty inside area.");
        foreach(double radius in new[]{0d,-1d,double.NaN,double.PositiveInfinity,double.MaxValue})
        {
            rejected=false;try {_=new HuntExcursion(new Vec(0,0),radius);}catch(ArgumentOutOfRangeException){rejected=true;}
            if(!rejected)throw new Exception("An invalid or unbounded excursion radius was accepted.");
        }
        rejected=false;try {area.BeginOutsideTrip(tooFar,6,1);}catch(ArgumentOutOfRangeException){rejected=true;}
        if(!rejected || area.OutsideTrip)throw new Exception("A beyond-limit target mutated excursion state.");
    }
}
