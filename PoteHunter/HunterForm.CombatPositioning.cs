namespace PoteHunter;

public sealed partial class HunterForm
{
    readonly PositioningCadence positioningCadence=new();
    readonly SideStepCadence sideStepCadence=new();

    async Task<bool> TryCombatSideStep(Movement drive,Entity target,Vec anchor,Options options,double boundary,double attackReach,CancellationToken token)
    {
        if(options.Ranged || !options.ContinuousCombatPositioning || !Input.BasicAttackHeld ||
            !sideStepCadence.TryCheck(Environment.TickCount64))return false;
        RefreshGuardScene();Vec position=world.PlayerPosition();
        Entity[] Candidates(Vec point)=>CombatPositioning.Eligible(entities,world.HealthSnapshot(),point,(double)options.NearbyEnemyRadius,
            e=>TargetIdentity(e)==TargetIdentity(target) || encounter.IsEngaged(e) || courtesy.StartedHere(e),
            (e,hp)=>options.GroupMode ? GroupCandidateReason(e,hp,point,options)==null : TargetGuardReason(e,hp,point,options)==null);
        var enemies=Candidates(position);
        bool Safe(Vec destination)=>destination.Finite && (destination-position).Length<=1.1 &&
            (destination-anchor).Length<=boundary && (destination-target.Position).Length<=attackReach &&
            (!options.GroupMode || groupDecision.Tank is Entity tank && (destination-tank.Position).Length<=(double)options.GroupFollowDistance) &&
            navigation.CanAdvance(position,destination,avoidZones) && drive.CanAdvance?.Invoke(position,destination)==true;
        var plan=CombatPositioning.ChooseSideStep(position,target,enemies,(double)options.NearbyEnemyRadius,SideStepCadence.Distance,Safe);
        if(plan==null){sideStepCadence.Finish(Environment.TickCount64,false);return false;}
        var key=plan.Left ? Keys.A : Keys.D;
        try
        {
            message=$"Side-stepping { (plan.Left ? "left" : "right") } to keep {plan.After}/{enemies.Length} targets in the attack cone.";
            TraceLog.Record("combat side-step",new {target.Id,plan.Left,plan.Before,plan.After,plan.Destination});
            // Two short pulses keep the lateral correction smooth while the
            // attack remains held, avoiding a single abrupt strafe burst.
            Input.Hold(key,true,token);await Input.Delay(32,token);
            Input.Hold(key,false,default);await Input.Delay(14,token);
            Input.Hold(key,true,token);await Input.Delay(24,token);
            return true;
        }
        finally
        {
            Input.Hold(key,false,default);sideStepCadence.Finish(Environment.TickCount64,true);
        }
    }

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
        ReleaseCombatPickup();drive.StopApproach();
        // Keep the basic swing held while the body makes a small formation
        // correction. The correction uses S/A/D instead of turning and walking
        // forward, so the attack cone remains aimed at the selected target.
        Input.HoldMouse(false,true,token);
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
            int missingTargetReads=0;
            while(Environment.TickCount64-started<CombatPositioning.BurstMilliseconds)
            {
                token.ThrowIfCancellationRequested();RefreshGuardScene();
                var current=world.Find(target.Id);
                // Memory snapshots can briefly omit a creature while movement
                // keys are being processed. Do not turn one missed read into a
                // lost target lock.
                if(current==null)
                {
                    if(++missingTargetReads>5)break;
                    await Input.Delay(35,token);continue;
                }
                missingTargetReads=0;
                if(TargetIdentity(current)!=TargetIdentity(target) || current.Name!=target.Name || current.Model!=target.Model || !current.Targetable)break;
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
                Keys correction=CombatPositioning.CorrectionKey(position,plan.Destination,world.PlayerHeading());
                Input.Hold(correction,true,token);
                Input.HoldMouse(false,true,token);
                await Input.Delay(48,token);
                Input.Hold(correction,false,default);
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
            drive.StopApproach();
            Input.Hold(Keys.S,false,default);Input.Hold(Keys.A,false,default);Input.Hold(Keys.D,false,default);
            drive.CanAdvance=previousAdvance;drive.ResetTurnResponse();
            positioningCadence.Finished(Environment.TickCount64);
            TraceLog.Record("combat positioning finished",new {target.Id});
        }
        return true; // Re-read the target and aim normally before any attack.
    }
}

