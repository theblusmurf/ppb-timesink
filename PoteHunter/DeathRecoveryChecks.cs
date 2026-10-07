using System.Text.Json;

namespace PoteHunter;

internal static class DeathRecoveryChecks
{
    public static void Run()
    {
        var faultWatch=new FaultDeathWatch();var fault=new TurnUnresponsiveException(default,default);
        foreach(var failure in new Exception[]{new OperationCanceledException("Escape"),new UnauthorizedAccessException(),new InvalidOperationException("Identity changed"),new RouteUnavailableException("Invalid route")})
            if(faultWatch.TryBegin(failure,true,false,false,0))throw new Exception("An explicit stop or unknown fault enabled automatic recovery.");
        if(faultWatch.TryBegin(fault,false,false,false,0) || faultWatch.TryBegin(fault,true,true,false,0) || faultWatch.TryBegin(fault,true,false,true,0))
            throw new Exception("Disabled/group/cancelled run began a fault death watch.");
        if(!faultWatch.TryBegin(fault,true,false,false,1000) || faultWatch.TryBegin(fault,true,false,false,1100) ||
            faultWatch.Expired(120999,false) || !faultWatch.Expired(121000,false) || faultWatch.Expired(121000,true) ||
            !faultWatch.Expired(601000,true) || faultWatch.RecoveryRemaining(601000)!=0)
            throw new Exception("Fault watch refreshed itself or exceeded its bounded wait/recovery deadline.");
        faultWatch.Reset();
        if(faultWatch.Active || faultWatch.Reason!=null || faultWatch.Expired(999999,true))throw new Exception("Explicit stop retained fault recovery intent.");
        var interruptedRecovery=new DeathRecoveryState();int deathLogs=0;
        void OnDeath(Health hp){if(interruptedRecovery.Observe(hp,1000))deathLogs++;}
        foreach(var hp in new[]{new Health(1,100),default})
            DeathRecoveryState.InterruptIfDead(hp,true,OnDeath);
        DeathRecoveryState.InterruptIfDead(new(0,100),false,OnDeath);
        if(interruptedRecovery.Pending||deathLogs!=0)throw new Exception("Unknown/living HP or disabled revival incorrectly began recovery.");
        for(int i=0;i<2;i++)
        {
            bool interrupted=false;
            try{DeathRecoveryState.InterruptIfDead(new(0,15370),true,OnDeath);}
            catch(DeathRecoveryRequiredException){interrupted=true;}
            if(!interrupted||!interruptedRecovery.Pending||deathLogs!=1)
                throw new Exception("Zero HP during an activity did not enter the shared death recovery exactly once.");
        }
        var recovery=new DeathRecoveryState();
        if(!recovery.Observe(new(0,100),1000) || recovery.Observe(new(0,100),1100) || !recovery.Pending)
            throw new Exception("Death must begin recovery and log only once.");
        recovery.Observe(default,1200);
        if(!recovery.Pending || recovery.ReadyAt(10)!=11000 || recovery.ReadyAt(0)!=1500)
            throw new Exception("Unreadable health changed the pending revive delay.");
        if(recovery.ReadyAt(0,true)!=3000 || recovery.ReadyAt(10,true)!=11000)
            throw new Exception("Visual revival countdown did not use the two-second minimum or respect a longer saved delay.");
        recovery.Observe(new(100,100),1600);
        if(!recovery.Pending)throw new Exception("Manual revival incorrectly bypassed return to the anchor.");
        if(!recovery.Observe(new(0,100),2000) || recovery.ReadyAt(5)!=7000)
            throw new Exception("Death on the return route did not restart recovery.");
        recovery.Reset();
        if(recovery.Pending || !recovery.Observe(new(0,100),3000))throw new Exception("A later death was not detected after arrival.");
        CompletedRecoveryPhases();

        var anchor=new Vec(0,0);
        var path=new RecoveryPath([new(8,0),new(4,0)],anchor);
        for(int tick=0;tick<100;tick++)
            if(path.Next(new(10,0))!=new Vec(8,0))throw new Exception("Recovery skipped a waypoint without reaching it.");
        if(path.Next(new(8,0))!=new Vec(4,0) || path.Next(new(4,0))!=anchor || path.Next(new(.51,0))!=anchor || path.Next(new(.49,0))!=null)
            throw new Exception("Recovery resumed before reaching the final anchor.");

        var merge=new RecoveryPath([new(.83,0),new(.83,2),new(.83,4)],new(.83,6));
        if(merge.Next(new(0,0),(_,_)=>true)!=new Vec(.83,2))
            throw new Exception("A short lateral route entry still forced a sideways turn.");
        var blockedMerge=new RecoveryPath([new(.83,0),new(.83,2),new(.83,4)],new(.83,6));
        if(blockedMerge.Next(new(0,0),(_,_)=>false)!=new Vec(.83,0))
            throw new Exception("Lookahead bypassed a blocked segment.");
        var corner=new RecoveryPath([new(0,0),new(0,2),new(2,2)],new(4,2));
        if(corner.Next(new(.83,0),(_,_)=>true)!=new Vec(0,0))
            throw new Exception("Route entry cut a sharp upcoming corner.");
        var passed=new RecoveryPath([new(0,0),new(0,2),new(0,4)],new(0,6));
        passed.Next(new(0,0));
        if(passed.Next(new(.8,2.2),(_,_)=>true)!=new Vec(0,4) || passed.Index!=2 ||
            passed.Next(new(0,0),(_,_)=>true)!=new Vec(0,4))
            throw new Exception("A locally passed straight waypoint caused backtracking or regressed route progress.");
        var final=new RecoveryPath([new(0,0)],new(0,2));
        if(final.Next(new(0,1.2),(_,_)=>true)!=new Vec(0,2) || !final.Final ||
            final.Next(new(0,1.49),(_,_)=>true)!=new Vec(0,2) || final.Next(new(0,1.51),(_,_)=>true)!=null)
            throw new Exception("Lookahead loosened final anchor arrival.");
        var remotePoint=new RecoveryPath([new(0,0),new(0,2)],new(0,4));
        if(remotePoint.Next(new(5,0),(_,_)=>true)!=new Vec(0,0))
            throw new Exception("Lookahead skipped a distant required connector.");
        RecoveryArrivalBraking();
        RecoverySteering();
        RecoveryObservedPassage();

        var self=new Entity(100,1,"Farmer",new(80,0),10,Model:"PC_MAN.GCMDS");
        var other=new Entity(200,2,"Neighbor",anchor,10,Model:"PC_MAN.GCMDS");
        var npc=other with{Id=0x40000003,Model="NPC_guard.GCMDS"};
        if(!RecoveryRouting.SameCharacter(self,self with{Address=999,Generation=2}) ||
            RecoveryRouting.SameCharacter(self,self with{Name="Other"}) || RecoveryRouting.SameCharacter(self,self with{Id=3}))
            throw new Exception("Revival identity validation accepted another character or rejected a recreated body.");
        var primary=new SavedNavigationRoute(5,anchor,1,[anchor,new(4,0),new(8,0)],DateTime.UtcNow,"Farmer",10,5,2,true);
        var alt1=primary with{Anchor=new(20,0),Points=[new(20,0),new(20,4),new(20,8)],FarmOnArrival=false};
        var alt2=primary with{Anchor=new(40,0),Points=[new(40,0),new(40,4),new(40,8)]};
        SavedNavigationRoute?[] routes=[primary,alt1,alt2];
        if(RecoveryRouting.SavedReturnProblem(primary,5,"Farmer",10,anchor)!=null ||
            RecoveryRouting.SavedReturnProblem(alt1,5,"Farmer",10,alt1.Anchor)!=null ||
            RecoveryRouting.SavedReturnProblem(null,5,"Farmer",10,anchor)==null ||
            RecoveryRouting.SavedReturnProblem(primary with{Points=[anchor]},5,"Farmer",10,anchor)==null ||
            RecoveryRouting.SavedReturnProblem(primary,6,"Farmer",10,anchor)==null ||
            RecoveryRouting.SavedReturnProblem(primary,5,"Other",10,anchor)==null ||
            RecoveryRouting.SavedReturnProblem(primary,5,"Farmer",20,anchor)==null ||
            RecoveryRouting.SavedReturnProblem(primary,5,"Farmer",10,new(40,0))==null)
            throw new Exception("Combined revival/return accepted a missing or incompatible recorded route.");
        var fallback=new RecoveryFallbackCycle(0);fallback.Reject(0);
        int Choose(IEnumerable<Entity> entities)=>fallback.Select(routes.Length,
            slot=>routes[slot] is {} route && RecoveryTravel.Recorded(route) && RecoveryRouting.Compatible(route,5,"Farmer",10),
            slot=>RecoveryRouting.Occupied(routes[slot]!.Anchor,10,5,entities,1));
        routes[1]=alt1 with{Points=[alt1.Anchor]};
        if(Choose([])!=2)throw new Exception("Automatic revival selected a spot-only alternative without a return route.");
        routes[1]=alt1;
        if(!RecoveryRouting.Occupied(anchor,10,5,[self,other],1) ||
            RecoveryRouting.Occupied(anchor,10,5,[self with{Position=anchor},npc,other with{Height=20}],1) || Choose([other])!=1)
            throw new Exception("Occupied primary did not select the first free alternative on the farming floor.");
        if(Choose([other,other with{Id=3,Position=alt1.Anchor}])!=2)
            throw new Exception("Occupied alternative one did not fall back to alternative two.");
        if(Choose([other with{Position=alt1.Anchor},other with{Id=3,Position=alt2.Anchor}])!=-1)
            throw new Exception("Recovery selected an occupied destination.");
        fallback.Reject(1);
        if(Choose([])!=2)throw new Exception("An occupied unloaded route was selected again during the same return.");
        fallback.Reject(2);
        if(Choose([])!=-1)throw new Exception("Recovery oscillated between rejected destinations.");
        fallback.Restart();fallback.Reject(0);routes[1]=alt1 with{Zone=6};routes[2]=alt2 with{Character="Other"};
        if(Choose([])!=-1 || RecoveryRouting.Compatible(alt1 with{Height=20},5,"Farmer",10))
            throw new Exception("Recovery accepted a different zone, character, or farming floor.");

        string file=Path.Combine(Path.GetTempPath(),"PoteHunter-recovery-"+Guid.NewGuid().ToString("N")+".json");
        try
        {
            var navigation=new Navigation();
            navigation.Observe("saved",new(8,0),10);navigation.BeginRecording(new(8,0));
            navigation.Observe("saved",new(4,0),10);navigation.Observe("saved",anchor,10);
            if(!navigation.SaveCurrentRoute(5,anchor,1,0,file,new("Farmer",10,5,2,true,false)))throw new Exception("Recovery fixture route was not saved.");
            navigation.EndRecording();
            byte[] savedBytes=File.ReadAllBytes(file);
            // Stationary hunting and a respawn clear the observed trail but
            // must not replace the user's recorded revival-to-farm route.
            navigation.Observe("hunt",anchor,10);navigation.Observe("hunt",new(1,0),10);
            navigation.Observe("respawn",new(8,0),10);
            if(!navigation.TryGetRouteToSavedAnchor(5,new(8,0),0,out var points) || points.Count!=2 || points[0]!=new Vec(8,0) || points[1]!=new Vec(4,0))
                throw new Exception("Saved route was lost or reversed after a death transition.");
            var returning=new RecoveryPath(points,anchor);
            if(returning.Next(new(8,0))!=new Vec(4,0) || returning.Next(new(4,0))!=anchor || returning.Next(anchor)!=null ||
                !savedBytes.SequenceEqual(File.ReadAllBytes(file)))
                throw new Exception("Recovery overwrote the saved route or failed to reach its anchor.");
        }
        finally {if(File.Exists(file))File.Delete(file);if(File.Exists(file+".tmp"))File.Delete(file+".tmp");}
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"death-recovery-checks.json"),JsonSerializer.Serialize(new{
            Passed=true,HardwareInputEmitted=false,Checks=new[]{"bounded movement-fault death watch","explicit stop/unknown failure/group/disabled revival never enables fault recovery","watch cannot refresh its deadline","zero HP interrupts combat/rest into recovery","unknown HP is not death","disabled revival respected","one log per death","configured delay survives unreadable HP","manual revival still returns","death during return restarts recovery",
                "successful repair survives living pending-return retries without repeated preparation or repair","new death clears completed recovery phases","unreadable HP preserves completed phases without creating a death","stale episode completions cannot complete a newer death or stopped recovery","failed repair remains incomplete",
                "waypoints require actual arrival","final anchor tolerance","duplicate plan destinations retain final braking","straight route samples keep continuous movement","sharp corners and short final anchor adjustments brake before capture",
                "translated dense recorded curve brakes on cumulative curvature","bounded independent steering goal without checkpoint advancement","blocked or unverified steering remains at the checkpoint","steering corridor rejects a connector shortcut","sharp bends, U-turns and nearby loop endpoints cannot be shortcut","rotation/translation invariant steering","exact final anchor retained by steering",
                "body recreation with identity validation","occupied primary and alternatives","character/map/floor compatibility",
                "no fallback oscillation","recorded route required for combined revival/return","spot-only alternatives excluded during recovery","saved route preserved through hunting and respawn"}
        },new JsonSerializerOptions{WriteIndented=true}));
    }

    static void RecoveryArrivalBraking()
    {
        // The observed startup return already contained this destination.
        // Appending it again formerly left Final=false at 1.535 units, keeping
        // W held instead of engaging arrival braking.
        var anchor=new Vec(0,0); // Translate the observed geometry; do not retain a user's farming coordinates.
        var duplicate=new RecoveryPath([anchor,anchor],anchor);
        if(duplicate.Next(new(1.524375,.1803125))!=anchor || !duplicate.Final || duplicate.ArrivalTolerance!=.5 ||
            duplicate.Next(anchor)!=null || duplicate.ArrivalTolerance!=0)
            throw new Exception("A repeated plan destination disabled final arrival braking or retained a completed route.");
        var straight=new RecoveryPath([new(0,0),new(0,3),new(0,6),new(0,9)],new(0,9));
        if(straight.Next(new(0,0),(_,_)=>false)!=new Vec(0,3) || straight.ArrivalTolerance!=0 ||
            straight.Next(new(0,3),(_,_)=>false)!=new Vec(0,6) || straight.ArrivalTolerance!=0 ||
            straight.Next(new(0,6),(_,_)=>false)!=new Vec(0,9) || !straight.Final || straight.ArrivalTolerance!=.5)
            throw new Exception("Straight travel stopped at every sample or lost final arrival braking.");
        var corner=new RecoveryPath([new(0,0),new(0,2),new(2,2)],new(6,2));
        if(corner.Next(new(0,0),(_,_)=>true)!=new Vec(0,2) || corner.ArrivalTolerance!=.6 ||
            corner.Next(new(0,1.39),(_,_)=>true)!=new Vec(0,2) || corner.ArrivalTolerance!=.6)
            throw new Exception("A required sharp corner had no braking before waypoint capture.");
        var preciseAnchor=new Vec(.9,12);
        var adjustment=new RecoveryPath([new(0,6),new(0,12)],preciseAnchor);
        if(adjustment.Next(new(0,6),(_,_)=>false)!=new Vec(0,12) || adjustment.Final || adjustment.ArrivalTolerance!=.6 ||
            adjustment.Next(new(0,12),(_,_)=>false)!=preciseAnchor || !adjustment.Final || adjustment.ArrivalTolerance!=.5 ||
            adjustment.Next(new(.39,12))!=preciseAnchor || adjustment.Next(new(.4,12))!=null)
            throw new Exception("A near-end activation-anchor adjustment bypassed braking or loosened its final tolerance.");
    }

    static void CompletedRecoveryPhases()
    {
        var state=new DeathRecoveryState();
        if(state.MarkPostRevivalPrepared(state.Episode) || state.MarkRepairCompleted(state.Episode))
            throw new Exception("A nonpending recovery accepted completed revival or repair.");
        state.Observe(new(0,100),1000);long firstEpisode=state.Episode;
        if(state.PostRevivalPrepared || state.RepairCompleted || state.MarkPostRevivalPrepared(firstEpisode) ||
            state.MarkRepairCompleted(firstEpisode))
            throw new Exception("Known dead HP completed a revival or repair phase.");
        state.Observe(new(100,100),2000);
        if(state.MarkRepairCompleted(firstEpisode))throw new Exception("Repair completed before the revived character was prepared.");

        int preparations=0,repairs=0;
        void ResumeLivingReturn()
        {
            if(!state.PostRevivalPrepared)
            {
                preparations++;
                if(!state.MarkPostRevivalPrepared(state.Episode))throw new Exception("Living revival preparation was rejected.");
            }
            if(!state.RepairCompleted)
            {
                repairs++;
                if(!state.MarkRepairCompleted(state.Episode))throw new Exception("Successful repair was not retained.");
            }
        }
        ResumeLivingReturn();
        // Reproduce the failure: final-facing stalls after a successful repair,
        // while the same living death episode is still awaiting anchor arrival.
        for(int retry=0;retry<3;retry++)
        {
            state.Observe(new(57,100),3000+retry*100);
            ResumeLivingReturn();
        }
        if(preparations!=1 || repairs!=1 || !state.Pending || state.Episode!=firstEpisode ||
            !state.PostRevivalPrepared || !state.RepairCompleted)
            throw new Exception("A living pending-return retry repeated successful recovery phases.");
        state.Observe(default,4000);
        if(!state.Pending || state.Episode!=firstEpisode || !state.PostRevivalPrepared || !state.RepairCompleted)
            throw new Exception("Unreadable HP discarded completed phases or started another recovery episode.");
        if(!state.Observe(new(0,100),5000) || state.Episode==firstEpisode || state.PostRevivalPrepared || state.RepairCompleted)
            throw new Exception("Death on the return route retained an earlier repair.");
        long nextEpisode=state.Episode;
        state.Observe(new(0,100),5001);
        if(state.Episode!=nextEpisode || state.ReadyAt(3,true)!=8000)
            throw new Exception("Repeated dead HP restarted the episode or death delay.");
        state.Observe(new(100,100),6000);
        if(state.MarkPostRevivalPrepared(firstEpisode) || state.MarkRepairCompleted(firstEpisode))
            throw new Exception("An old asynchronous completion advanced a newer death episode.");
        ResumeLivingReturn();
        if(preparations!=2 || repairs!=2 || !state.PostRevivalPrepared || !state.RepairCompleted)
            throw new Exception("A second real death did not get exactly one new repair cycle.");

        state.Reset();long stoppedEpisode=state.Episode;
        if(state.Pending || state.PostRevivalPrepared || state.RepairCompleted ||
            state.MarkPostRevivalPrepared(nextEpisode) || state.MarkRepairCompleted(nextEpisode))
            throw new Exception("Explicit stop retained or accepted completed recovery phases.");
        state.Observe(new(0,100),7000);state.Observe(new(100,100),8000);
        if(state.Episode==stoppedEpisode || state.MarkPostRevivalPrepared(nextEpisode))
            throw new Exception("A new run reused a stale death episode.");
        if(!state.MarkPostRevivalPrepared(state.Episode) || state.RepairCompleted)
            throw new Exception("Preparation falsely declared an unperformed or failed repair successful.");
        state.Observe(default,8100);state.Observe(new(100,100),8200);
        if(!state.Pending || !state.PostRevivalPrepared || state.RepairCompleted)
            throw new Exception("An uncompleted repair became successful while waiting for readable health.");
    }

    static void RecoverySteering()
    {
        // Translate the recorded multi-bend geometry to a neutral origin. The
        // individually mild bends after the first sharp corner still accumulate
        // enough curvature to produce the observed alternating body turns.
        Vec[] curve=[new(-1.0440625,.29078125),new(0,0),new(1.8171875,.61921875),
            new(3.485,1.29046875),new(5.0521875,2.41421875),new(6.111875,3.95703125),
            new(6.784375,5.53390625),new(7.080625,7.6403125),new(7.1078125,9.161875)];
        var curved=new RecoveryPath(curve,new(7.1,13));
        curved.Next(curve[0],(_,_)=>false);
        if(curved.Next(curve[1],(_,_)=>true)!=curve[2] || curved.ArrivalTolerance!=0 ||
            curved.UpcomingCurvatureDegrees<35 || curved.UpcomingCurvatureDegrees>40)
            throw new Exception("A gentle cumulative curve still forced precision stops at its recorded samples.");
        Vec curveAim=curved.SteeringGoal(curve[1],(_,_)=>true);
        double curveLookahead=RecoveryPath.SteeringLookaheadUnits/(1+curved.UpcomingCurvatureDegrees/90);
        if(curveAim==curve[2] || (curveAim-curve[1]).Length>curveLookahead+.0001 || curved.Index!=2)
            throw new Exception("A cumulative curve lost bounded forward lookahead or advanced its checkpoint while steering.");
        Vec nearCurve=curve[2]+new Vec(-.8,0);
        if(curved.Next(nearCurve,(_,_)=>true)!=curve[2] || curved.Index!=2)
            throw new Exception("Steering changed the conservative checkpoint capture of a cumulative curve.");
        for(int sample=2;sample<curve.Length-1;sample++)
        {
            curved.Next(curve[sample],(_,_)=>false);
            if(curved.ArrivalTolerance!=0)
                throw new Exception("Consecutive gentle curve samples still alternated precision stop/aim/pulse movement.");
        }

        Vec[] mild=[new(0,0),new(1.5,0),new(3,.15),new(4.5,.45),new(6,.9)];
        var path=new RecoveryPath(mild,new(8,1.3));
        Vec current=new(0,0);
        if(path.Next(current,(_,_)=>false)!=mild[1] || path.ArrivalTolerance!=0)
            throw new Exception("A mild recorded curve lost continuous travel.");
        int checkpointIndex=path.Index;Vec aim=path.SteeringGoal(current,(_,_)=>true);
        if(aim.X<=mild[1].X || (aim-current).Length>RecoveryPath.SteeringLookaheadUnits+.0001 ||
            path.Index!=checkpointIndex || path.Next(current,(_,_)=>false)!=mild[1])
            throw new Exception("Steering failed to look ahead locally, advanced the checkpoint, or escaped its distance bound.");
        if(path.SteeringGoal(current)!=mild[1] || path.SteeringGoal(current,(_,_)=>false)!=mild[1] ||
            path.SteeringGoal(current,(from,to)=>!(from.X>=mild[1].X && to.X>from.X))!=mild[1])
            throw new Exception("A blocked or unverified original segment allowed a steering shortcut.");
        if(path.SteeringGoal(new(0,-1),(_,_)=>true)!=mild[1] ||
            path.SteeringGoal(new(-5,0),(_,_)=>true)!=mild[1])
            throw new Exception("Steering escaped the local connector corridor or skipped a distant required connector.");

        var corner=new RecoveryPath([new(0,0),new(0,2),new(2,2)],new(6,2));
        corner.Next(new(0,0),(_,_)=>false);
        if(corner.SteeringGoal(new(0,0),(_,_)=>true)!=new Vec(0,2) || corner.Index!=1)
            throw new Exception("Steering cut a required right-angle corner.");
        var hairpin=new RecoveryPath([new(0,0),new(2,0),new(2,.2),new(0,.2)],new(-2,.2));
        hairpin.Next(new(0,0),(_,_)=>false);
        if(hairpin.SteeringGoal(new(0,0),(_,_)=>true)!=new Vec(2,0) ||
            hairpin.Next(new(.2,.2),(_,_)=>true)!=new Vec(2,0) || hairpin.Index!=1)
            throw new Exception("A U-turn or nearby return leg became a forward shortcut.");

        // Moving the entire fixture must move its steering point identically;
        // no direction-specific heuristic or personal map coordinates apply.
        const double rotation=.73;Vec translation=new(20,-7);
        Vec Transform(Vec value)=>Movement.Rotate(value,rotation)+translation;
        var transformed=new RecoveryPath(mild.Select(Transform),Transform(new(8,1.3)));
        transformed.Next(Transform(current),(_,_)=>false);
        if((transformed.SteeringGoal(Transform(current),(_,_)=>true)-Transform(aim)).Length>.000001)
            throw new Exception("Route steering depends on map origin or orientation.");
        if(transformed.SteeringGoal(Transform(new(0,-1)),(_,_)=>true)!=Transform(mild[1]) ||
            transformed.SteeringGoal(Transform(new(-5,0)),(_,_)=>true)!=Transform(mild[1]))
            throw new Exception("Adaptive lookahead bypassed a rotated off-route or distant connector.");
        var initialConnector=new RecoveryPath(mild.Skip(1),new(8,1.3));
        initialConnector.Next(current,(_,_)=>false);
        if(initialConnector.SteeringGoal(current,(_,_)=>true)!=mild[1])
            throw new Exception("Steering looked beyond an unvisited initial route connector.");

        var final=new RecoveryPath([new(0,0),new(0,3)],new(0,6));
        final.Next(new(0,0),(_,_)=>false);
        final.Next(new(0,3),(_,_)=>false);
        if(!final.Final || final.ArrivalTolerance!=.5 ||
            final.SteeringGoal(new(0,3),(_,_)=>true)!=new Vec(0,6) ||
            final.Next(new(0,5.49),(_,_)=>true)!=new Vec(0,6) || final.Next(new(0,5.51),(_,_)=>true)!=null)
            throw new Exception("Steering loosened the exact final anchor envelope.");
    }

    static void RecoveryObservedPassage()
    {
        Vec[] straight=[new(0,0),new(0,3),new(0,7),new(0,11)];
        foreach(double overshoot in new[]{1.3,1.5,2.0})
        foreach(double angle in new[]{0.0,.73,-1.2,Math.PI})
        {
            Vec Transform(Vec value)=>Movement.Rotate(value,angle)+new Vec(20,-7);
            var path=new RecoveryPath(straight.Select(Transform),Transform(straight[^1]));
            if(path.Next(Transform(straight[0]),(_,_)=>false)!=Transform(straight[1]))
                throw new Exception("Observed-passage fixture did not retain its first unvisited checkpoint.");
            Vec current=Transform(new(0,3+overshoot));
            Vec? next=path.Next(current,(_,_)=>true);
            double error=Movement.Angle(Transform(new(0,4+overshoot))-current,path.SteeringGoal(current,(_,_)=>true)-current);
            if(next!=Transform(straight[2]) || path.Index!=2 || Math.Abs(error)>.001)
                throw new Exception("A delayed straight-route sample commanded a backwards turn after crossing its checkpoint.");
            path.Next(Transform(new(0,0)),(_,_)=>false);
            if(path.Index!=2)throw new Exception("Moving backwards regressed an observed route checkpoint.");
        }

        RecoveryPath NewStraight()=>new(straight,straight[^1]);
        void Unpassed(Vec before,Vec current,Func<Vec,Vec,bool>? clear,string reason)
        {
            var path=NewStraight();path.Next(straight[0]);path.Next(before);
            if(path.Next(current,clear)!=straight[1] || path.Index!=1)
                throw new Exception("Observed route passage bypassed "+reason+".");
        }
        Unpassed(new(0,0),new(0,4.5),null,"unknown clearance");
        Unpassed(new(0,0),new(0,4.5),(_,_)=>false,"blocked clearance");
        Unpassed(new(0,0),new(0,4.5),(from,to)=>from!=new Vec(0,0) || to!=new Vec(0,4.5),"the traversed segment");
        Unpassed(new(0,0),new(0,4.5),(from,to)=>from!=straight[1] || to!=new Vec(0,4.5),"the passed checkpoint connector");
        Unpassed(new(0,0),new(0,4.5),(from,to)=>from!=new Vec(0,4.5) || to!=straight[2],"the onward connector");
        Unpassed(new(.5,0),new(0,4.5),(_,_)=>true,"an off-route previous position");
        Unpassed(new(0,0),new(.5,4.5),(_,_)=>true,"an off-route current position");
        Unpassed(new(.2,4.5),new(-.2,4.5),(_,_)=>true,"a sideways crossing behind the checkpoint");
        Unpassed(new(0,4.5),new(0,1.5),(_,_)=>true,"backwards travel");
        Unpassed(new(0,0),new(0,6),(_,_)=>true,"an overlong jump");
        Unpassed(new(0,-1),new(0,4.5),(_,_)=>true,"a remote prior observation");
        var fresh=NewStraight();
        if(fresh.Next(new(0,4.5),(_,_)=>true)!=straight[0] || fresh.Index!=0)
            throw new Exception("A new route inherited passage history or searched globally for a later checkpoint.");
        var connector=new RecoveryPath(straight.Skip(1),straight[^1]);connector.Next(new(0,0),(_,_)=>false);
        if(connector.Next(new(0,4.5),(_,_)=>true)!=straight[1] || connector.Index!=0)
            throw new Exception("Observed passage skipped an unvisited initial route connector.");

        foreach(Vec[] protectedPath in new[]{
            new Vec[]{new(0,0),new(0,3),new(3,3),new(7,3)},
            new Vec[]{new(0,0),new(0,3),new(0,0),new(0,-4)},
            new Vec[]{new(0,0),new(0,3),new(0,4.5)},
            new Vec[]{new(0,0),new(0,3)}})
        {
            var path=new RecoveryPath(protectedPath,protectedPath[^1]);path.Next(protectedPath[0]);
            if(path.Next(new(0,4.5),(_,_)=>true)!=protectedPath[1] || path.Index!=1)
                throw new Exception("Observed passage skipped a sharp bend, U-turn, short final connector or exact final anchor.");
        }
    }
}
