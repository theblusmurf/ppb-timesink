namespace PoteHunter;

public sealed partial class HunterForm
{
    readonly PositioningCadence positioningCadence=new();

    async Task<bool> TryCombatPositioning(Movement drive,Entity target,Vec anchor,Options options,double boundary,double attackReach,CancellationToken token)
    {
        if(!options.ContinuousCombatPositioning || target.PriorityLootObject || !positioningCadence.TryCheck(Environment.TickCount64))return false;
        RefreshGuardScene();
        Vec position=world.PlayerPosition();
        Entity[] Candidates(Vec point)=>CombatPositioning.Eligible(entities,world.HealthSnapshot(),point,(double)options.NearbyEnemyRadius,
            e=>TargetIdentity(e)==TargetIdentity(target) || encounter.IsEngaged(e) || courtesy.StartedHere(e),
            (e,hp)=>options.GroupMode ? GroupCandidateReason(e,hp,point,options)==null : TargetGuardReason(e,hp,point,options)==null);
        bool Safe(Vec from,Vec to)=>to.Finite && (to-anchor).Length<=boundary &&
            (to-target.Position).Length<=attackReach &&
            (!options.GroupMode || groupDecision.Tank is Entity tank && (to-tank.Position).Length<=(double)options.GroupFollowDistance) &&
            navigation.CanAdvance(from,to,avoidZones) && drive.CanAdvance?.Invoke(from,to)==true;
        bool Healthy()
        {
            var hp=world.TargetHealth(guardSelfId);
            return hp.Known && !hp.Dead && (decimal)hp.Current*100/hp.Maximum>options.HealBelowPercent &&
                retreatRecovery==null && !healingRestPending && !defenseRepositioning;
        }
        if(!Healthy())return false;
        var enemies=Candidates(position);
        var plan=CombatPositioning.Choose(position,target,enemies,attackReach,(double)options.NearbyEnemyRadius,Safe);
        if(plan==null)return false;
        ReleaseCombatPickup();drive.StopApproach();Input.HoldMouse(false,false,token);
        TraceLog.Record("combat positioning started",new {target.Id,plan.Destination,plan.Before,plan.After,plan.Total});
        long started=Environment.TickCount64;
        var previousAdvance=drive.CanAdvance;
        try
        {
            // Restrict the actual heading's next step, not just the intended
            // destination, to the same safe corridor and attack reach.
            drive.CanAdvance=(from,to)=>to.Finite && (to-anchor).Length<=boundary &&
                (to-target.Position).Length<=attackReach &&
                (!options.GroupMode || groupDecision.Tank is Entity tank && (to-tank.Position).Length<=(double)options.GroupFollowDistance) &&
                navigation.CanAdvance(from,to,avoidZones) && previousAdvance?.Invoke(from,to)==true;
            while(Environment.TickCount64-started<CombatPositioning.BurstMilliseconds)
            {
                token.ThrowIfCancellationRequested();RefreshGuardScene();
                var current=world.Find(target.Id);
                if(current==null || TargetIdentity(current)!=TargetIdentity(target) || current.Name!=target.Name || current.Model!=target.Model || !current.Targetable)break;
                target=current;position=world.PlayerPosition();
                var hp=world.TargetHealth(target.Id);
                if(!hp.Known || hp.Dead || !Healthy() || TargetGuardReason(target,hp,position,options)!=null)break;
                enemies=Candidates(position);
                if(enemies.Length<2 || !Safe(position,plan.Destination))break;
                var refreshedPlan=CombatPositioning.Choose(position,target,enemies,attackReach,(double)options.NearbyEnemyRadius,Safe);
                if(refreshedPlan==null)break;
                plan=refreshedPlan;
                int front=CombatPositioning.FrontCount(position,target.Position,enemies,(double)options.NearbyEnemyRadius);
                if(front>enemies.Length/2 || (plan.Destination-position).Length<=.25)break;
                if(CombatPositioning.FrontCount(plan.Destination,target.Position,enemies,(double)options.NearbyEnemyRadius)<=front)break;
                message=$"Positioning: {front}/{enemies.Length} engaged enemies in front";
                await drive.Approach(world,position,plan.Destination-position,token,watchTurns:true);
            }
        }
        catch(MovementBlockedException ex)
        {
            navigation.RecordBlock(ex.Position,ex.Direction,world.LocalPlayer().Height);
            TraceLog.Record("combat positioning blocked",new {ex.Position});
        }
        catch(TurnUnresponsiveException)
        {
            // Optional repositioning must not turn into an endless recovery
            // loop. Normal target aiming retains its own recovery handling.
            TraceLog.Record("combat positioning turn unavailable",new {target.Id});
        }
        finally
        {
            drive.StopApproach();drive.CanAdvance=previousAdvance;drive.ResetTurnResponse();
            positioningCadence.Finished(Environment.TickCount64);
            TraceLog.Record("combat positioning finished",new {target.Id});
        }
        return true; // Re-read the target and aim normally before any attack.
    }
}

