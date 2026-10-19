namespace PoteHunter;

public sealed partial class HunterForm
{
    async Task RunMerchantRepairAsync(CancellationToken token,Action? additionalValidation,Func<Task>? verifyDurability)
    {
        var options=activeGuardOptions??CurrentOptions();
        if(options.GroupMode||options.HealerMode)throw new InvalidOperationException("Merchant repair trips require solo hunt mode.");
        var profile=MerchantRepairProfile.Load();var ui=profile.ControlsFor(world.ClientHash,RepairScreen.Bounds(world).Size);
        var drive=movement??throw new InvalidOperationException("Calibrate movement with F6 before testing a merchant repair trip.");
        var departure=world.LocalPlayer();int zone=world.ActiveZone(),pid=world.Pid;nint window=world.Window;string client=world.ClientHash;
        var equipment=world.ReadDurability();
        if(!equipment.TryValidate(out _,out var reason))throw new InvalidOperationException("Merchant repair blocked: "+reason);
        if(equipment.Items.All(item=>item.Current==item.Maximum)){repairStatus.Text="Equipment is already fully repaired; merchant trip skipped.";return;}
        var outbound=profile.Plan(navigation.SavedRoutes,departure,zone,(double)options.RouteCorridorRadius);
        if((departure.Position-profile.Route.RevivalOrigin).Length<=3 && Math.Abs(departure.Height-profile.OriginHeight)>=2)
            throw new RouteUnavailableException("The Hoys route start is on a different floor.");
        var back=new SavedTravel(outbound.Points.Reverse().Append(departure.Position).ToArray(),departure.Position);
        var previousPreflight=Input.Preflight;var previousAdvance=drive.CanAdvance;
        bool previousOwner=navigationInputOwned;double previousRate=drive.TurnSpeedDegreesPerSecond;
        using var bounded=CancellationTokenSource.CreateLinkedTokenSource(token);bounded.CancelAfter(TimeSpan.FromMinutes(15));var tripToken=bounded.Token;
        void ValidateTrip()
        {
            tripToken.ThrowIfCancellationRequested();var current=world.LocalPlayer();var hp=world.TargetHealth(current.Id);
            if(hp.Dead&&working&&options.AutoReviveAfterDeath){ObserveDeath(hp);throw new DeathRecoveryRequiredException();}
            if(!world.ConnectionVerified||world.Pid!=pid||world.Window!=window||world.ClientHash!=client||world.ActiveZone()!=zone||
                !LocalCharacter.Same(departure,current)||!hp.Known||hp.Dead)
                throw new InvalidOperationException("Merchant repair stopped because the client, character, map or health changed.");
            additionalValidation?.Invoke();
        }
        void ValidateShop()
        {
            ValidateTrip();var body=world.LocalPlayer();
            if((body.Position-profile.Route.Anchor).Length>.5||Math.Abs(body.Height-profile.Route.Height)>=2)
                throw new InvalidOperationException("The character moved away from the saved Hoys interaction point. No merchant click was sent.");
        }
        repairInProgress=true;navigationInputOwned=true;
        Input.PickupHoldProvider=null;ReleaseCombatPickup();drive.StopApproach();Input.Release();drive.ResetTurnResponse();
        try
        {
            drive.TurnSpeedDegreesPerSecond=(double)options.TurnSpeedDegreesPerSecond;
            drive.CanAdvance=(from,to)=>Avoidance.BlockedSegment(from,to,avoidZones)==null&&navigation.CanAdvance(from,to,avoidZones);
            Input.Preflight=()=>{ValidateTrip();previousPreflight?.Invoke();};
            TraceLog.Record("merchant repair trip started",new{Merchant="Hoys",Departure=departure.Position,Destination=profile.Route.Anchor,Waypoints=outbound.Points.Length});
            await FollowMerchantRepairPath(drive,outbound,profile.Route.Height,profile.Route.Heading,"travelling to Hoys",ValidateTrip,tripToken);
            ValidateShop();message=repairStatus.Text="Hoys · opening General Store and repairing equipment";
            await MerchantRepair.Run(new LiveMerchantRepair(world,ui,ValidateShop),ui.Confirm!=null,tripToken);
            if(verifyDurability!=null)await verifyDurability();
            TraceLog.Record("merchant repair durability verified",new{Merchant="Hoys",Position=world.PlayerPosition(),PaidActionRepeated=false});
            await FollowMerchantRepairPath(drive,back,departure.Height,departure.Heading,"returning from Hoys",ValidateTrip,tripToken);
            repairStatus.Text="Hoys repair verified; departure point and facing restored.";
            TraceLog.Record("merchant repair trip completed",new{Merchant="Hoys",Position=world.PlayerPosition(),Departure=departure.Position,DurabilityVerified=true});
        }
        finally
        {
            drive.StopApproach();Input.Release();drive.ResetTurnResponse();drive.CanAdvance=previousAdvance;
            drive.TurnSpeedDegreesPerSecond=previousRate;Input.Preflight=previousPreflight;
            navigationInputOwned=previousOwner;repairInProgress=false;
        }
    }

    async Task FollowMerchantRepairPath(Movement drive,SavedTravel plan,double height,double heading,string phase,Action validate,CancellationToken token)
    {
        var path=new RecoveryPath(plan.Points,plan.Destination);var turnRecovery=new SavedRouteTurnRecovery();
        long progressAt=Environment.TickCount64;int progressIndex=-1;double best=double.PositiveInfinity;
        drive.StopApproach();drive.ResetTurnResponse();navigation.BeginGoal(phase);
        while(true)
        {
            await Input.Delay(0,token);validate();var body=world.LocalPlayer();Vec current=body.Position;
            navigation.Observe(world.NavigationContext(body),current,body.Height);RefreshImportedCollisionObstacles();
            Vec? goal=path.Next(current,(from,to)=>Avoidance.BlockedSegment(from,to,avoidZones)==null&&navigation.CanAdvance(from,to,avoidZones));
            if(goal==null)
            {
                drive.StopApproach();Input.Release();await Input.Delay(150,token);validate();var stopped=world.LocalPlayer();
                if(!AnchorArrival.Settled(current,stopped.Position,plan.Destination,.5)||Math.Abs(stopped.Height-height)>=2)
                    throw new RouteUnavailableException("Merchant route arrival did not settle at its saved point and floor.");
                long deadline=Environment.TickCount64+20000;
                while(!await drive.Face(world,Movement.FromClientHeading(heading),token,.035,"merchant-route-facing"))
                {
                    validate();
                    if(Environment.TickCount64>=deadline)throw new RouteUnavailableException("Merchant route facing made no bounded progress; stopped before interaction.");
                    await Input.Delay(20,token);
                }
                validate();
                if((world.PlayerPosition()-plan.Destination).Length>.5)throw new RouteUnavailableException("Merchant route moved while restoring facing.");
                return;
            }
            long now=Environment.TickCount64;double distance=(goal.Value-current).Length;
            if(path.Index!=progressIndex||distance<best-.15){progressIndex=path.Index;best=distance;progressAt=now;}
            else if(now-progressAt>20000)throw new RouteUnavailableException("Merchant travel made no progress toward the saved waypoint.");
            if(Avoidance.BlockedSegment(current,goal.Value,avoidZones)!=null||!navigation.CanAdvance(current,goal.Value,avoidZones))
                throw new RouteUnavailableException("The saved merchant route is blocked; stopped before leaving it.");
            message=$"{phase} · {distance:0.0} to waypoint";turnRecovery.ObserveWaypoint(path,now);
            var obstacles=navigation.Obstacles(avoidZones);
            Vec steering=path.SteeringGoal(current,(from,to)=>Avoidance.BlockedSegment(from,to,avoidZones)==null&&RoutePlanner.SegmentClear(from,to,obstacles),speedUnitsPerMs:drive.RouteSpeedUnitsPerMs);
            try{await drive.Approach(world,current,goal.Value-current,token,watchTurns:true,arrivalTolerance:path.ArrivalTolerance,steeringDelta:steering-current,steeringStatus:path.SteeringStatus);}
            catch(TurnUnresponsiveException)
            {
                var retry=await turnRecovery.RetryAsync(path,()=>{drive.StopApproach();Input.Release();},drive.ResetTurnResponse,
                    Input.Delay,()=>(world.PlayerPosition(),world.PlayerHeading()),()=>Environment.TickCount64,token);
                if(!retry.Ready)throw;
            }
            await Input.Delay(25,token);
        }
    }
}
