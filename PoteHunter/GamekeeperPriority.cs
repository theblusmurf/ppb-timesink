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

    // Only the verified physical endpoint may release saved-route ownership.
    // The caller supplies a freshly protected priority candidate and renews
    // character, zone, floor, completed repair and destination occupancy checks.
    // Saved facing may then wait until the Gamekeeper return completes.
    public static bool ShouldHandOffAtAnchor(bool farmOnArrival,bool prioritize,bool contextVerified,
        Vec position,Vec anchor,Health playerHealth,Entity? priority,Health priorityHealth)=>
        farmOnArrival && prioritize && contextVerified && position.Finite && anchor.Finite &&
        (position-anchor).Length<=NearbyLootPickup.AnchorArrivalTolerance &&
        playerHealth is {Known:true,Dead:false} && priorityHealth is {Known:true,Dead:false} &&
        priority is {Monster:true,Targetable:true,PriorityLootObject:false} &&
        priority.Position.Finite && Targeting.IsGamekeeper(priority);

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

        AnchorHandoffChecks(keeper,ordinary);
        AnchorReturnDefenseCadence.Checks();
    }

    static void AnchorHandoffChecks(Entity keeper,Entity ordinary)
    {
        Vec anchor=new(20,30);var living=new Health(100,100);
        bool HandOff(Vec position,bool farming=true,bool enabled=true,bool verified=true,
            Health? selfHealth=null,Entity? target=null,Health? targetHealth=null)=>
            ShouldHandOffAtAnchor(farming,enabled,verified,position,anchor,selfHealth??living,
                target??keeper,targetHealth??living);
        if(!HandOff(anchor) || !HandOff(anchor+new Vec(.5,0)) || !HandOff(anchor+new Vec(0,-.5)) ||
            HandOff(anchor+new Vec(.500001,0)) || HandOff(anchor+new Vec(1.5,0)))
            throw new Exception("Gamekeeper handoff did not require actual half-unit anchor arrival.");
        // A protected keeper can be outside swing range: arrival must hand it
        // to the existing priority pursuit rather than renew farm-target defense.
        if((keeper.Position-anchor).Length<=Targeting.MeleeAttackRange || !HandOff(anchor,target:keeper))
            throw new Exception("A response-range Gamekeeper was ignored merely because it was outside melee reach.");
        if(HandOff(anchor,farming:false) || HandOff(anchor,enabled:false) || HandOff(anchor,verified:false) ||
            HandOff(new(double.NaN,30)) || HandOff(new(20,double.PositiveInfinity)) ||
            ShouldHandOffAtAnchor(true,true,true,anchor,new(double.NaN,30),living,keeper,living) ||
            ShouldHandOffAtAnchor(true,true,true,anchor,new(20,double.NegativeInfinity),living,keeper,living) ||
            ShouldHandOffAtAnchor(true,true,true,anchor,anchor,living,null,living))
            throw new Exception("Gamekeeper handoff bypassed disabled farming/priority, protected context or finite endpoint checks.");
        foreach(var unreadableOrDead in new[]{default(Health),new Health(0,100),new Health(-1,100)})
            if(HandOff(anchor,selfHealth:unreadableOrDead) || HandOff(anchor,targetHealth:unreadableOrDead))
                throw new Exception("Gamekeeper handoff accepted unknown or dead player/priority health.");
        foreach(var invalid in new[]{ordinary,keeper with{Id=0x40001752},keeper with{Model="MON_mimic.GCMDS"},
            keeper with{Position=new(double.NaN,0)},keeper with{Position=new(0,double.PositiveInfinity)}})
            if(HandOff(anchor,target:invalid))
                throw new Exception("Gamekeeper handoff accepted an unrelated or malformed target.");

        // Keep completed repair and ordinary engagements while the character
        // is near, but not at, its physical endpoint. After real arrival a
        // protected keeper owns target selection without replaying repair.
        var recovery=new DeathRecoveryState();recovery.Observe(new(0,100),0);
        recovery.Observe(living,10);long episode=recovery.Episode;
        if(!recovery.MarkPostRevivalPrepared(episode) || !recovery.MarkRepairCompleted(episode) ||
            HandOff(anchor+new Vec(.6,0)) || !recovery.Pending || !recovery.RepairCompleted)
            throw new Exception("Gamekeeper arrival handoff completed recovery before the actual endpoint or lost repair progress.");
        var encounter=new Encounter();encounter.MarkAttack(ordinary,living);
        if(!HandOff(anchor) || ChooseFirst(encounter,keeper,()=>ordinary)!=keeper || !encounter.IsEngaged(ordinary))
            throw new Exception("An engaged farm target blocked the arrived Gamekeeper or was discarded at handoff.");
        recovery.Reset();
        if(recovery.Pending || recovery.MarkRepairCompleted(episode))
            throw new Exception("Completed anchor handoff retained the old recovery or reused its repair episode.");
    }
}

// The caller retains one instance through retries for the same anchor and
// recovery episode. Reserving an engagement prevents overlapping defense;
// completing it allows a whole turn-speed-aware arrival correction before
// another engagement. Reset only when the anchor/episode changes or arrives.
internal sealed class AnchorReturnDefenseCadence
{
    public const int ApproachOpportunityMilliseconds=4000;
    long nextDefenseAt,lastObservation=-1;
    bool defending;

    void ObserveClock(long now)
    {
        if(now<0 || now<lastObservation)throw new InvalidOperationException("Anchor defense clock is unavailable or moved backward.");
        lastObservation=now;
    }
    public bool TryBegin(long now)
    {
        ObserveClock(now);
        if(defending || now<nextDefenseAt)return false;
        defending=true;return true;
    }
    public void Complete(long now,double turnSpeedDegreesPerSecond=90)
    {
        int opportunity=FacingRestore.TimeoutMilliseconds(turnSpeedDegreesPerSecond);
        ObserveClock(now);
        if(!defending)throw new InvalidOperationException("Anchor defense engagement was not reserved.");
        defending=false;
        nextDefenseAt=now>long.MaxValue-opportunity?long.MaxValue:now+opportunity;
    }
    public void Reset(){nextDefenseAt=0;lastObservation=-1;defending=false;}

    internal static void Checks()
    {
        var cadence=new AnchorReturnDefenseCadence();
        if(!cadence.TryBegin(0) || cadence.TryBegin(0) || cadence.TryBegin(50))
            throw new Exception("Repeated anchor-defense requests reserved overlapping engagements.");
        cadence.Complete(1000);int approachUpdates=0;
        for(long now=1000;now<5000;now+=25)
        {
            if(cadence.TryBegin(now))throw new Exception("Continuous nearby targets starved the anchor approach opportunity.");
            approachUpdates++;
        }
        if(approachUpdates!=160 || !cadence.TryBegin(5000) || cadence.TryBegin(5000))
            throw new Exception("Anchor defense did not leave a full 90-degree-per-second correction opportunity.");
        cadence.Complete(6000,30);
        if(cadence.TryBegin(13999) || !cadence.TryBegin(14000))
            throw new Exception("A slow half-turn could not finish before renewed defense.");
        cadence.Complete(14000,360);
        if(cadence.TryBegin(16499) || !cadence.TryBegin(16500))
            throw new Exception("A later engagement reset the rate-aware movement opportunity before it elapsed.");
        cadence.Complete(16500);
        bool rollbackRejected=false,unreservedRejected=false,negativeRejected=false;
        try{cadence.TryBegin(16499);}catch(InvalidOperationException){rollbackRejected=true;}
        try{cadence.Complete(16500);}catch(InvalidOperationException){unreservedRejected=true;}
        try{new AnchorReturnDefenseCadence().TryBegin(-1);}catch(InvalidOperationException){negativeRejected=true;}
        if(!rollbackRejected || !unreservedRejected || !negativeRejected || !new AnchorReturnDefenseCadence().TryBegin(0))
            throw new Exception("Anchor defense accepted an invalid clock/lifecycle or leaked cadence into a fresh return.");
        foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,29.9,360.1})
        {
            var invalidCadence=new AnchorReturnDefenseCadence();invalidCadence.TryBegin(0);bool rejected=false;
            try{invalidCadence.Complete(0,invalid);}catch(ArgumentOutOfRangeException){rejected=true;}
            if(!rejected || invalidCadence.TryBegin(0))throw new Exception("Invalid turn speed completed or reopened a reserved defense.");
        }
        cadence.Reset();
        if(!cadence.TryBegin(0))throw new Exception("Changed anchor/recovery episode retained the old correction deadline.");
        cadence.Reset();
        if(!cadence.TryBegin(1))throw new Exception("Reset retained an overlapping engagement reservation.");
        cadence.Complete(long.MaxValue-1);
        if(cadence.TryBegin(long.MaxValue-1) || !cadence.TryBegin(long.MaxValue))
            throw new Exception("Correction deadline overflow reopened defense before its bound.");
    }
}
