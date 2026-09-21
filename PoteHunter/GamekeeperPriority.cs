namespace PoteHunter;

public static class GamekeeperPriority
{
    static bool Same(Entity a,Entity b)=>a.Id==b.Id && a.Generation==b.Generation && a.Address==b.Address;

    // Callers apply route and target protections before passing candidates. The
    // response area is independent of an ordinary encounter or pending return.
    // Supply a bounded retainedRadius only for an already engaged Gamekeeper;
    // every fresh candidate remains inside the configured response area.
    public static Entity? Choose(IEnumerable<Entity> candidates,IReadOnlyDictionary<uint,Health> health,
        Vec position,Vec anchor,double huntRadius,double gamekeeperRadius,bool enabled,Entity? retained=null,double? retainedRadius=null)
    {
        ArgumentNullException.ThrowIfNull(candidates);ArgumentNullException.ThrowIfNull(health);
        if(!enabled || !position.Finite || !anchor.Finite || !double.IsFinite(huntRadius) || huntRadius<=0 ||
            !double.IsFinite(gamekeeperRadius) || gamekeeperRadius<0 ||
            retainedRadius is double extension && (!double.IsFinite(extension) || extension<0))return null;
        double radius=Targeting.ResponseRadius(huntRadius,gamekeeperRadius);
        double completionRadius=Math.Max(radius,retainedRadius??radius);
        var observed=candidates.Where(entity=>entity.Targetable && Targeting.IsGamekeeper(entity)).ToArray();
        // A reused UID must not borrow health or the retained lock from another life.
        var ambiguous=observed.GroupBy(entity=>entity.Id)
            .Where(group=>group.Select(entity=>(entity.Generation,entity.Address)).Distinct().Skip(1).Any())
            .Select(group=>group.Key).ToHashSet();
        var ready=observed.Where(entity=>!ambiguous.Contains(entity.Id) && entity.Position.Finite &&
            (entity.Position-anchor).Length<=(retained!=null && Same(entity,retained) ? completionRadius : radius) &&
            health.GetValueOrDefault(entity.Id) is {Known:true,Dead:false}).ToArray();
        return (retained==null ? null : ready.FirstOrDefault(entity=>Same(entity,retained))) ??
            ready.OrderBy(entity=>(entity.Position-position).Length).ThenBy(entity=>entity.Id).FirstOrDefault();
    }

    public static bool ShouldYield(Entity? locked,Entity? priority)=>
        priority!=null && (locked==null || !Same(locked,priority));

    // Preserve every ordinary engagement while temporarily serving Gamekeeper.
    public static Entity? ChooseFirst(Encounter encounter,Entity? priority,Func<Entity?> chooseFresh)=>
        priority ?? Targeting.ChooseEngagedFirst(encounter,chooseFresh);

    public static void SelfTest()
    {
        var keeper=new Entity(100,0x80001752,"Gamekeeper",new(45,0),0,Generation:1,Model:"MON_SnowGun2.GCMDS");
        var nearerKeeper=keeper with {Address=200,Id=0x80011752,Position=new(30,0)};
        var ordinary=new Entity(300,0x80000001,"Lv. 1 Gorgon",new(2,0),0,Generation:1);
        var add=ordinary with {Address=400,Id=0x80000002,Name="Lv. 1 Wolf",Position=new(3,0)};
        Entity[] scene=[keeper,nearerKeeper,ordinary,add];
        var health=scene.ToDictionary(entity=>entity.Id,_=>new Health(100,100));
        Vec anchor=new(0,0);
        Entity? Choose(IEnumerable<Entity> candidates,bool enabled=true,Entity? retained=null,double huntRadius=20,double responseRadius=60,double? retainedRadius=null)=>
            GamekeeperPriority.Choose(candidates,health,anchor,anchor,huntRadius,responseRadius,enabled,retained,retainedRadius);
        if(Choose(scene)?.Id!=nearerKeeper.Id || Choose(scene,false)!=null || Choose([ordinary,add])!=null ||
            Choose([keeper],responseRadius:40)!=null || Choose([keeper],huntRadius:50,responseRadius:10)?.Id!=keeper.Id)
            throw new Exception("Gamekeeper priority ignored its setting, identity, or bounded response area.");
        if(Choose(scene,retained:keeper)?.Id!=keeper.Id || ShouldYield(keeper,Choose(scene,retained:keeper)) ||
            !ShouldYield(ordinary,Choose(scene)) || !ShouldYield(null,Choose(scene)) || ShouldYield(ordinary,null))
            throw new Exception("Gamekeeper priority abandoned its existing Gamekeeper or failed to interrupt an ordinary target.");
        var moved=keeper with {Position=new(44,1)};
        if(ShouldYield(keeper,moved) || Choose([moved,nearerKeeper],retained:keeper)!=moved)
            throw new Exception("A position update broke the retained Gamekeeper identity.");
        var reused=keeper with {Generation=2,Address=101};
        if(!ShouldYield(keeper,reused) || Choose([reused,nearerKeeper],retained:keeper)?.Id!=nearerKeeper.Id ||
            Choose([keeper,reused])!=null)
            throw new Exception("A reused or ambiguous Gamekeeper identity inherited an old lock.");
        foreach(var invalid in new[]{keeper with {Position=new(double.NaN,0)},keeper with {Position=new(60.1,0)},
            keeper with {Model="MON_mimic.GCMDS"},keeper with {Id=0x40001752}})
            if(Choose([invalid])!=null)throw new Exception("Gamekeeper priority accepted an invalid or outside candidate.");
        if(Choose([keeper with {Position=new(60,0)}])==null || Choose([keeper],responseRadius:double.PositiveInfinity)!=null ||
            Choose([keeper],huntRadius:double.NaN)!=null ||
            GamekeeperPriority.Choose(scene,health,new(double.NaN,0),anchor,20,60,true)!=null)
            throw new Exception("Gamekeeper response boundary was inconsistent or unbounded.");
        var sidestepped=keeper with {Position=new(60.1,0)};
        if(Choose([sidestepped,nearerKeeper],retained:keeper,retainedRadius:65)?.Id!=keeper.Id ||
            Choose([sidestepped],retainedRadius:65)!=null ||
            Choose([nearerKeeper with {Position=new(60.1,0)}],retained:keeper,retainedRadius:65)!=null ||
            Choose([keeper with {Position=new(65,0)}],retained:keeper,retainedRadius:65)==null ||
            Choose([keeper with {Position=new(65.1,0)}],retained:keeper,retainedRadius:65)!=null)
            throw new Exception("An engaged Gamekeeper lost its completion allowance or expanded fresh target selection.");
        foreach(double invalid in new[]{-1d,double.NaN,double.PositiveInfinity})
            if(Choose([keeper],retained:keeper,retainedRadius:invalid)!=null)
                throw new Exception("An invalid retained Gamekeeper completion radius was accepted.");
        health[keeper.Id]=new(0,100);
        if(Choose([keeper])!=null || Choose(scene,retained:keeper)?.Id!=nearerKeeper.Id)
            throw new Exception("A confirmed-dead Gamekeeper retained priority.");
        health.Remove(keeper.Id);
        if(Choose([keeper])!=null)throw new Exception("A Gamekeeper with unknown health retained priority.");
        health[keeper.Id]=new(100,100);

        var encounter=new Encounter();encounter.MarkAttack(ordinary,health[ordinary.Id]);encounter.MarkAttack(add,health[add.Id]);
        encounter.Observe(scene,health,anchor,6,(_,_)=>true,(_,hp)=>hp.Known && !hp.Dead,clearNearby:false);
        int freshCalls=0;
        Entity? Fresh(){freshCalls++;return ordinary;}
        if(encounter.EngagedCount!=2 || ChooseFirst(encounter,Choose([keeper]),Fresh)?.Id!=keeper.Id || freshCalls!=0 ||
            !encounter.IsEngaged(ordinary) || !encounter.IsEngaged(add))
            throw new Exception("Existing engagements blocked Gamekeeper priority or were discarded during the interruption.");
        if(ChooseFirst(encounter,Choose([sidestepped],retained:keeper,retainedRadius:65),Fresh)?.Id!=keeper.Id || freshCalls!=0)
            throw new Exception("A Gamekeeper sidestep yielded to old engagements before the completion boundary.");

        // Replay the real recovery state machine: priority ends an existing sit
        // at ten percent HP, while the encounter retains both ordinary enemies.
        var lowHealth=new Health(10,100);var resting=new RestReading(RestPosture.Resting);
        var standing=new RestReading(RestPosture.Standing);var recovery=new HealingRest(lowHealth,0);
        if(recovery.Evaluate(lowHealth,resting,false,false,3000).Action!=HealingRestAction.Wait)
            throw new Exception("The priority recovery replay did not begin with an active rest.");
        var priority=Choose([keeper]);
        if(recovery.Evaluate(lowHealth,resting,encounter.HasEngaged || priority!=null,false,3100).Action!=HealingRestAction.Stand ||
            recovery.Evaluate(lowHealth,standing,encounter.HasEngaged || priority!=null,false,3200).Action!=HealingRestAction.Interrupted ||
            ChooseFirst(encounter,priority,Fresh)?.Id!=keeper.Id || encounter.EngagedCount!=2 || freshCalls!=0)
            throw new Exception("Low-health rest blocked Gamekeeper, waited for full health, or discarded engaged enemies.");
        // Priority alone must interrupt recovery, even before any monster has
        // engaged. Turning the checkbox off keeps an otherwise quiet rest going.
        var priorityOnlyRecovery=new HealingRest(lowHealth,0);var disabledRecovery=new HealingRest(lowHealth,0);
        if(priorityOnlyRecovery.Evaluate(lowHealth,resting,Choose([keeper])!=null,false,3000).Action!=HealingRestAction.Stand ||
            disabledRecovery.Evaluate(lowHealth,resting,Choose([keeper],enabled:false)!=null,false,3000).Action!=HealingRestAction.Wait ||
            disabledRecovery.Interrupted)
            throw new Exception("Recovery ignored enabled Gamekeeper priority or was cancelled with the checkbox disabled.");

        health[keeper.Id]=new(0,100);
        if(ChooseFirst(encounter,Choose([keeper]),Fresh)==null || freshCalls!=0 || encounter.EngagedCount!=2)
            throw new Exception("Gamekeeper completion skipped surviving engaged monsters and selected a fresh target.");
        var emptyBar=new HotbarSnapshot(0,[]);
        if(HealingRest.MayStart(lowHealth,emptyBar,encounter.HasEngaged,false,encounter.Active,0,false,false,75))
            throw new Exception("Gamekeeper death allowed resting before surviving engaged monsters were finished.");
        health[ordinary.Id]=new(0,100);health[add.Id]=new(0,100);
        encounter.Observe(scene,health,anchor,6,(_,_)=>true,(_,hp)=>hp.Known && !hp.Dead,clearNearby:false);
        if(encounter.HasEngaged)throw new Exception("Confirmed deaths failed to finish the priority replay's ordinary engagements.");
        encounter.Reset(); // The caller has completed the ordinary encounter cleanup.
        if(!HealingRest.MayStart(lowHealth,emptyBar,encounter.HasEngaged,false,encounter.Active,0,false,false,75) ||
            new HealingRest(lowHealth,0).Evaluate(lowHealth,standing,Choose([keeper])!=null,false,3000).Action!=HealingRestAction.Sit)
            throw new Exception("Normal low-health recovery did not resume after Gamekeeper and remaining engagements were finished.");
        if(ChooseFirst(encounter,null,Fresh)!=ordinary || freshCalls!=1)
            throw new Exception("Ordinary hunting did not resume after priority and engagements cleared.");
    }
}
