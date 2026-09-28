namespace PoteHunter;

public sealed record TightGatherPlan(Vec Destination,int Close,int Front,int Total);
public enum GatherAction { Move, Wait, Attack, GiveUp }

public static class TightGathering
{
    public const double DefaultRadius=.5, Arrival=.08;
    // Start the swing once five members, or every member of a smaller roster,
    // are close and forward. The roster itself has no artificial size cap.
    public const int SwingCloseTargetCount=5;
    public const long MoveMilliseconds=1800, SettleMilliseconds=1200, MaximumMilliseconds=3000;
    public const long SuccessCooldown=6000, FailedCooldown=15000;
    public static int CloseCount(Vec position,IReadOnlyList<Entity> enemies,double radius=DefaultRadius)=>enemies.Count(e=>
        e.Position.Finite && (e.Position-position).Length<=radius);
    public static int FrontCount(Vec position,Entity target,IReadOnlyList<Entity> enemies,double radius=DefaultRadius)=>
        CombatPositioning.FrontCount(position,target.Position,enemies,radius);
    public static bool Ready(Vec position,Entity target,IReadOnlyList<Entity> enemies)=>
        Ready(position,target,enemies,DefaultRadius);
    public static bool Ready(Vec position,Entity target,IReadOnlyList<Entity> enemies,double radius)=>
        enemies.Count>=2 &&
        CloseCount(position,enemies,radius)>=Math.Min(SwingCloseTargetCount,enemies.Count) &&
        FrontCount(position,target,enemies,radius)>=Math.Min(SwingCloseTargetCount,enemies.Count);

    // Keep the initial roster throughout an attempt. A creature leaving the
    // observation radius is not a successful gather or a confirmed death.
    public static Entity[]? ObserveRoster(IReadOnlyList<Entity> original,IEnumerable<Entity> scene,
        IReadOnlyDictionary<uint,Health> health,Func<Entity,Health,bool> permitted)
    {
        var byId=scene.GroupBy(e=>e.Id).ToDictionary(g=>g.Key,g=>g.ToArray());
        var result=new List<Entity>();
        foreach(var prior in original)
        {
            // Require the original identity even when the current ID's HP is
            // dead; a reused ID cannot silently remove an original member.
            if(!byId.TryGetValue(prior.Id,out var matches) || matches.Any(e=>e.Generation!=prior.Generation || e.Address!=prior.Address))return null;
            var current=matches[0];
            if(current.Name!=prior.Name || current.Model!=prior.Model || !current.Monster || !current.Position.Finite)return null;
            var hp=health.GetValueOrDefault(prior.Id);
            if(!hp.Known)return null;
            if(hp.Dead)continue;
            if(!permitted(current,hp))return null;
            result.Add(current);
        }
        return result.ToArray();
    }

    public static TightGatherPlan? Choose(Vec position,Entity target,IReadOnlyList<Entity> enemies,
        double attackReach,Func<Vec,Vec,bool> safe,double radius=DefaultRadius)
    {
        if(!position.Finite || !target.Position.Finite || enemies.Count<2 || enemies.Any(e=>!e.Position.Finite) ||
            !double.IsFinite(attackReach) || attackReach<=0 ||
            !enemies.Any(e=>e.Id==target.Id && e.Generation==target.Generation && e.Address==target.Address))return null;
        TightGatherPlan? best=null;double bestCost=double.MaxValue;
        void Consider(Vec goal)
        {
            double travel=(goal-position).Length,reach=(goal-target.Position).Length;
            if(!goal.Finite || travel>CombatPositioning.MaximumStep || reach>attackReach || !safe(position,goal))return;
            int close=CloseCount(goal,enemies,radius),front=FrontCount(goal,target,enemies,radius);
            double cost=enemies.Average(e=>(e.Position-goal).Length)+travel*.15;
            // Prefer the largest close forward group, then close bodies, then
            // a central holding point. The session can release the swing once
            // five (or all of a smaller roster) are ready. Meaningful
            // cost hysteresis limits jitter.
            if(best==null || front>best.Front || front==best.Front && close>best.Close ||
                front==best.Front && close==best.Close && cost<bestCost-.10)
            {best=new(goal,close,front,enemies.Count);bestCost=cost;}
        }
        Consider(position); // Waiting in place is a valid bounded gather attempt.
        var center=new Vec(enemies.Average(e=>e.Position.X),enemies.Average(e=>e.Position.Y));
        Consider(center);
        // Circles around bodies and pairwise radius intersections
        // find close groups without confusing pairwise spacing with player radius.
        foreach(var enemy in enemies)
        {
            for(int i=0;i<32;i++)
            {
                Vec offset=new(Math.Cos(i*Math.PI/16),Math.Sin(i*Math.PI/16));
                Consider(enemy.Position+offset*(radius*.8));Consider(enemy.Position+offset*radius);
            }
        }
        for(int i=0;i<enemies.Count;i++)for(int j=i+1;j<enemies.Count;j++)
        {
            Vec delta=enemies[j].Position-enemies[i].Position;double distance=delta.Length;
            if(distance<.0001 || distance>radius*2)continue;
            Vec mid=(enemies[i].Position+enemies[j].Position)/2;
            double height=Math.Sqrt(Math.Max(0,radius*radius-distance*distance/4));
            Vec normal=new(-delta.Y/distance,delta.X/distance);
            Consider(mid+normal*height);Consider(mid-normal*height);Consider(mid);
        }
        return best;
    }
}

public sealed class TightGatherSession(long started,Vec destination,double radius=TightGathering.DefaultRadius)
{
    long? waitingSince;
    public void SetDestination(Vec value)
    {
        if(!value.Finite)return;
        destination=value;
        waitingSince=null;
    }
    public GatherAction Next(long now,Vec position,Entity target,IReadOnlyList<Entity> enemies)
    {
        if(now<started || !position.Finite || enemies.Count<2)return GatherAction.GiveUp;
        if(TightGathering.Ready(position,target,enemies,radius))return GatherAction.Attack;
        if(now-started>=TightGathering.MaximumMilliseconds)return GatherAction.GiveUp;
        if(waitingSince==null && now-started<TightGathering.MoveMilliseconds && (destination-position).Length>TightGathering.Arrival)return GatherAction.Move;
        waitingSince ??= now;
        return now-waitingSince.Value>=TightGathering.SettleMilliseconds ? GatherAction.GiveUp : GatherAction.Wait;
    }
}

public sealed class TightGatherCadence
{
    long due;
    public bool TryCheck(long now){if(now<due)return false;due=now+750;return true;}
    public void Finish(long now,bool success)=>due=now+(success?TightGathering.SuccessCooldown:TightGathering.FailedCooldown);
    public void Reset()=>due=0;
}

public sealed partial class HunterForm
{
    readonly TightGatherCadence gatherCadence=new();
    readonly CheckBox tightGathering=new(){Text="Group engaged targets",AutoSize=true,Checked=true};
    readonly NumericUpDown gatherRadius=Number(.5m,4,1);
    string gatherStatus="Ready";

    async Task<bool> TryTightGathering(Movement drive,Entity target,Vec anchor,Options options,double boundary,double attackReach,CancellationToken token)
    {
        if(!options.ContinuousCombatPositioning || !options.TightGathering || target.PriorityLootObject ||
            !gatherCadence.TryCheck(Environment.TickCount64))return false;
        RefreshGuardScene();Vec position=world.PlayerPosition();
        bool Permitted(Entity e,Health hp)=>options.GroupMode ? GroupCandidateReason(e,hp,world.PlayerPosition(),options)==null :
            TargetGuardReason(e,hp,world.PlayerPosition(),options)==null;
        bool Healthy()
        {
            var hp=world.TargetHealth(guardSelfId);
            return hp.Known && !hp.Dead && (decimal)hp.Current*100/hp.Maximum>options.HealBelowPercent &&
                retreatRecovery==null && !healingRestPending && !defenseRepositioning;
        }
        if(!Healthy()){gatherStatus="Skipped for healing/recovery";return false;}
        double radius=Math.Clamp((double)options.GatherRadius,.5,4);
        var eligible=CombatPositioning.Eligible(entities,world.HealthSnapshot(),position,(double)options.NearbyEnemyRadius,
            e=>TargetIdentity(e)==TargetIdentity(target) || encounter.IsEngaged(e) || courtesy.StartedHere(e),Permitted);
        var original=CombatPositioning.ConeRoster(eligible,position,target);
        if(original.Length<2){gatherStatus="Needs at least two engaged targets";return false;}
        if(TightGathering.Ready(position,target,original,radius)){gatherStatus=$"All engaged targets within {radius:0.#} and forward; attacking";return false;}
        var previousAdvance=drive.CanAdvance;Vec gatherOrigin=position;
        bool Safe(Vec from,Vec to)=>to.Finite && (to-gatherOrigin).Length<=CombatPositioning.MaximumStep &&
            (to-anchor).Length<=boundary && (to-target.Position).Length<=attackReach &&
            (!options.GroupMode || groupDecision.Tank is Entity tank && (to-tank.Position).Length<=(double)options.GroupFollowDistance) &&
            navigation.CanAdvance(from,to,avoidZones) && previousAdvance?.Invoke(from,to)==true;
        var plan=TightGathering.Choose(position,target,original,attackReach,Safe,radius);
        if(plan==null){gatherStatus="No safe gathering point";gatherCadence.Finish(Environment.TickCount64,false);return false;}
        ReleaseCombatPickup();drive.StopApproach();Input.HoldMouse(false,false,token);
        long started=Environment.TickCount64;var session=new TightGatherSession(started,plan.Destination,radius);bool success=false;
        TraceLog.Record("tight gathering started",new {target.Id,Radius=radius,plan.Destination,Count=original.Length});
        try
        {
            drive.CanAdvance=Safe;
            while(true)
            {
                token.ThrowIfCancellationRequested();RefreshGuardScene();position=world.PlayerPosition();
                var current=world.Find(target.Id);
                if(current==null || TargetIdentity(current)!=TargetIdentity(target) || current.Name!=target.Name || current.Model!=target.Model || !current.Targetable)break;
                target=current;var hp=world.TargetHealth(target.Id);
                if(!hp.Known || hp.Dead || !Healthy() || TargetGuardReason(target,hp,position,options)!=null)break;
                var enemies=TightGathering.ObserveRoster(original,entities,world.HealthSnapshot(),Permitted);
                if(enemies==null)break;
                // Re-plan from the refreshed scene. Engaged creatures keep
                // moving while they pursue the character, so a destination
                // chosen from the initial snapshot can become stale.
                var refreshedPlan=TightGathering.Choose(position,target,enemies,attackReach,Safe,radius);
                if(refreshedPlan==null)break;
                plan=refreshedPlan;session.SetDestination(plan.Destination);
                var action=session.Next(Environment.TickCount64,position,target,enemies);
                if(action==GatherAction.Attack){success=true;break;}
                if(action==GatherAction.GiveUp)break;
                    gatherStatus=$"Gathering: {TightGathering.CloseCount(position,enemies,radius)}/{enemies.Length} within {radius:0.#}";
                message=gatherStatus;
                if(action==GatherAction.Move)
                {
                    if(!Safe(position,plan.Destination))break;
                    await drive.Approach(world,position,plan.Destination-position,token,watchTurns:true);
                }
                else
                {
                    drive.StopApproach();
                    // Stop at a holding point briefly so existing pursuers can
                    // close in. Do not send attacks/skills until this phase ends.
                    await drive.Face(world,target.Position-position,token,.12);
                    await Input.Delay(50,token);
                }
            }
        }
        catch(MovementBlockedException ex){navigation.RecordBlock(ex.Position,ex.Direction,world.LocalPlayer().Height);}
        catch(TurnUnresponsiveException){ /* Optional gathering yields to normal combat. */ }
        finally
        {
            drive.StopApproach();drive.CanAdvance=previousAdvance;drive.ResetTurnResponse();
            gatherCadence.Finish(Environment.TickCount64,success);
            gatherStatus=success?$"All engaged targets within {radius:0.#} and forward; attacking":"Gather ended; resuming normal combat";
            TraceLog.Record("tight gathering finished",new {target.Id,Success=success,Elapsed=Environment.TickCount64-started,Status=gatherStatus});
        }
        return true; // Normal loop revalidates health/identity/range and re-aims.
    }
}
