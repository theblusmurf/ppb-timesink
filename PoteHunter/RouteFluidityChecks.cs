using System.Text.Json;

namespace PoteHunter;

// Synthetic geometry and injected clearance only. These checks never open a
// game reader, issue hardware input, or load private runtime routes/evidence.
internal static class RouteFluidityChecks
{
    static readonly Func<Vec,Vec,bool> Clear=(_,_)=>true;
    static readonly Func<Vec,Vec,bool> Blocked=(_,_)=>false;
    static readonly Vec PulseGoal=new(2,0);

    public static void Run()
    {
        RollingCornerProof();
        RequiredCapture();
        SparseAndDenseGeometry();
        ObservedProjectionRecovery();
        ReentryUsesActualArc();
        PulseQualification();
        PulseExclusions();
        PulseOwnershipAndExpiry();
        PulseClearance();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"route-fluidity-checks.json"),
            JsonSerializer.Serialize(new{
                Passed=true,HardwareInputEmitted=false,SyntheticFixturesOnly=true,
                Checks=new[]{
                    "20–35 degree rolling corners require a verified carrot beyond their physical checkpoint",
                    "rolling approval expires at the next observation or a rejected steering decision",
                    "rolling steering cannot bypass mandatory physical corner capture by observed overshoot",
                    "sharp corners, U-turns, entry and final anchors retain original capture/braking",
                    "existing clear short lateral entry merges retain admission; blocked/sharp entries do not",
                    "blocked prefix/chord and off-corridor geometry cannot approve a rolling corner",
                    "sparse straight/future-hard-corner segments retain safe partial lookahead",
                    "dense and rigidly transformed soft-corner polylines retain the same accepted aim",
                    "local work caps do not permit an unverified dense shortcut",
                    "stale projection recovers only at the same physically observed arrival/crossing",
                    "clearance and corridor proofs still gate observed-arrival projection rebasing",
                    "fallback reentry starts at its checkpoint and grows only with measured forward arc",
                    "polling, stationary repetition and backward readings cannot refill reentry lookahead",
                    "two fresh settled actually-held minimum-frame no-motion samples offer32ms once",
                    "unknown/unsettled/out-and-back/lateral/progress/held-time/requested-frame samples excluded",
                    "goal/tolerance/position ownership, whole-streak expiry, clock regression and reset preserved",
                    "actual clearance selection rejects a blocked32ms envelope and independently checks16ms",
                    "both blocked envelopes or invalid direction/displacement admit no pulse"
                }
            },new JsonSerializerOptions{WriteIndented=true}));
    }

    static void Check(bool passed,string reason) {if(!passed)throw new Exception(reason);}
    static void Near(Vec actual,Vec expected,string reason,double tolerance=1e-8)=>
        Check(actual.Finite && (actual-expected).Length<=tolerance,$"{reason}: expected {expected}, actual {actual}.");
    static void Throws<T>(Action action,string reason) where T:Exception
    {
        try{action();}catch(T){return;}
        throw new Exception(reason);
    }
    static RecoveryPath MakePath(Vec[] route)=>new(route[..^1],route[^1]);
    static Vec[] SoftRoute(double degrees)
    {
        Vec corner=new(2,0),outgoing=Movement.Rotate(new(3,0),degrees*Math.PI/180);
        return [new(0,0),corner,corner+outgoing,corner+outgoing*2,corner+outgoing*5];
    }
    static Vec[] Dense(Vec[] route,int subdivisions)
    {
        var result=new List<Vec>{route[0]};
        for(int at=1;at<route.Length;at++)
            for(int step=1;step<=subdivisions;step++)
                result.Add(route[at-1]+(route[at]-route[at-1])*(step/(double)subdivisions));
        return result.ToArray();
    }

    static void RollingCornerProof()
    {
        foreach(double degrees in new[]{25.0,34.0})
        {
            var path=MakePath(SoftRoute(degrees));Vec checkpoint=path.Next(default,Clear)!.Value;int index=path.Index;
            Check(checkpoint==new Vec(2,0) && path.CaptureTolerance==.6 && path.ArrivalTolerance==.6,
                "Soft corner lost mandatory capture before steering proof");
            Vec aim=path.SteeringGoal(default,Clear);
            Check(aim.X>checkpoint.X && aim.Y>0 && path.RollingCornerApproved &&
                path.ArrivalTolerance==0 && path.CaptureTolerance==.6 && path.Index==index,
                "A proved soft-corner carrot failed to retain separate physical capture");
            path.Next(default,Clear);
            Check(!path.RollingCornerApproved && path.ArrivalTolerance==.6 && path.Index==index,
                "A later observation retained an earlier rolling-corner approval");
            path.SteeringGoal(default,Clear);
            Near(path.SteeringGoal(default,Blocked),checkpoint,"Blocked geometry retained rolling steering");
            Check(!path.RollingCornerApproved && path.ArrivalTolerance==.6,
                "A rejected steering decision retained soft-corner approval");

            path.SteeringGoal(default,Clear);
            Vec overshot=checkpoint+Movement.Rotate(new(.8,0),degrees*Math.PI/180);
            Check(path.Next(overshot,Clear)==checkpoint && path.Index==index && path.CaptureTolerance==.6,
                "A rolling carrot authorized an unobserved mandatory-corner crossing");

            var slow=MakePath(SoftRoute(degrees));slow.Next(default,Clear);
            Near(slow.SteeringGoal(default,Clear,.001),checkpoint,
                "A short proof-only aim disabled braking without traversing the soft corner");
            Check(!slow.RollingCornerApproved && slow.ArrivalTolerance==.6,
                "Below-corner speed lookahead removed precise arrival");

            var outgoingBlocked=MakePath(SoftRoute(degrees));outgoingBlocked.Next(default,Clear);
            Near(outgoingBlocked.SteeringGoal(default,(_,to)=>to.Y<=.001),checkpoint,
                "A blocked outgoing corner segment approved rolling travel");
            Check(!outgoingBlocked.RollingCornerApproved,"Blocked outgoing segment retained approval");
        }
        var offRoute=MakePath(SoftRoute(25));Vec target=offRoute.Next(default,Clear)!.Value;
        Near(offRoute.SteeringGoal(new(0,.36),Clear),target,"An off-corridor connector approved a soft corner");
        Check(offRoute.ArrivalTolerance==.6 && !offRoute.RollingCornerApproved,
            "Off-corridor soft-corner steering changed capture policy");
    }

    static void RequiredCapture()
    {
        foreach(double degrees in new[]{36.0,90.0,180.0})
        {
            var path=MakePath(SoftRoute(degrees));Vec checkpoint=path.Next(default,Clear)!.Value;
            Near(path.SteeringGoal(default,Clear),checkpoint,"Sharp corner or U-turn was looked through");
            Check(path.CaptureTolerance==.6 && path.ArrivalTolerance==.6 && !path.RollingCornerApproved,
                "Sharp corner or U-turn lost braking");
        }
        var entry=new RecoveryPath([new(3,0),new(7,0)],new(11,0));
        Vec connector=entry.Next(default,Blocked)!.Value;
        Near(entry.SteeringGoal(default,Clear),connector,"An unvisited initial connector became a rolling aim");
        Check(entry.Index==0 && entry.ArrivalTolerance==.6 && entry.CaptureTolerance==.6,
            "Initial connector lost exact capture");
        foreach(double remaining in new[]{.7,1.0})
        {
            var nearEntry=new RecoveryPath([new(remaining,0),new(5,0)],new(9,0));
            Check(nearEntry.Next(default,Clear)==new Vec(remaining,0) && nearEntry.Index==0 &&
                nearEntry.CaptureTolerance==.6,
                "A clear near-entry connector bypassed mandatory physical capture");
            var nearFinalConnector=new RecoveryPath([new(0,0),new(4,0)],new(5,0));
            nearFinalConnector.Next(default,Clear);
            Check(nearFinalConnector.Next(new(4-remaining,0),Clear)==new Vec(4,0) &&
                nearFinalConnector.Index==1 && nearFinalConnector.CaptureTolerance==.6,
                "A clear near-final connector bypassed mandatory physical capture");
        }
        var lateralMerge=new RecoveryPath([new(.83,0),new(.83,2),new(.83,4)],new(.83,6));
        Check(lateralMerge.Next(default,Clear)==new Vec(.83,2) && lateralMerge.Index==1,
            "A verified clear short lateral entry lost its existing local merge admission");
        var blockedMerge=new RecoveryPath([new(.83,0),new(.83,2),new(.83,4)],new(.83,6));
        Check(blockedMerge.Next(default,Blocked)==new Vec(.83,0) && blockedMerge.Index==0,
            "Short lateral entry admission bypassed a blocked connector");
        var sharpEntry=new RecoveryPath([new(0,0),new(0,2),new(2,2)],new(4,2));
        Check(sharpEntry.Next(new(.83,0),Clear)==new Vec(0,0) && sharpEntry.Index==0,
            "Short lateral entry admission bypassed a sharp upcoming corner");
        var shortMerge=new RecoveryPath([new(0,0)],new(0,2));
        Check(shortMerge.Next(new(0,1.2),Clear)==new Vec(0,2) && shortMerge.Final &&
            shortMerge.CaptureTolerance==.5 && shortMerge.ArrivalTolerance==.5 &&
            shortMerge.Next(new(0,1.49),Clear)==new Vec(0,2) && shortMerge.Next(new(0,1.51),Clear)==null,
            "An admitted short initial merge loosened its final anchor capture");
        var final=new RecoveryPath([new(0,0),new(4,0),new(4,0)],new(4,0));
        Vec anchor=final.Next(default,Clear)!.Value;
        Near(final.SteeringGoal(default,Clear),anchor,"Final anchor was replaced by rolling steering");
        Check(final.Final && final.ArrivalTolerance==.5 && final.CaptureTolerance==.5 &&
            final.Next(new(3.49,0),Clear)==anchor && final.Next(new(3.51,0),Clear)==null,
            "Fluid steering loosened final anchor acceptance");
        var adjustment=new RecoveryPath([new(0,0),new(4,0)],new(5,0));adjustment.Next(default,Blocked);
        Check(adjustment.CaptureTolerance==.6 && adjustment.ArrivalTolerance==.6,
            "Short final connector lost its mandatory braking");
    }

    static void SparseAndDenseGeometry()
    {
        var sparse=new RecoveryPath([new(0,0),new(8,0),new(16,0)],new(24,0));
        sparse.Next(default,Clear);
        Near(sparse.SteeringGoal(default,Clear),new(3.5,0),
            "A sparse straight sample prevented a safe partial physical lookahead");
        var futureCorner=new RecoveryPath([new(0,0),new(2,0),new(8,0),new(8,6)],new(8,12));
        futureCorner.Next(default,Clear);
        Near(futureCorner.SteeringGoal(default,Clear),new(3.5,0),
            "A far hard corner rejected a safe incoming-segment aim");
        var nearbyCorner=new RecoveryPath([new(0,0),new(2,0),new(3,0),new(3,4)],new(3,10));
        nearbyCorner.Next(default,Clear);nearbyCorner.Next(new(1.3,0),Clear);
        Vec beforeCorner=nearbyCorner.SteeringGoal(new(1.3,0),Clear);
        Check(beforeCorner.Y==0 && beforeCorner.X<=3 && !nearbyCorner.RollingCornerApproved,
            "Partial steering looked through an uncaptured future hard corner");

        Vec[] route=SoftRoute(25);var original=MakePath(route);original.Next(default,Clear);
        Vec originalAim=original.SteeringGoal(default,Clear);
        var dense=MakePath(Dense(route,4));dense.Next(default,Clear);
        Near(dense.SteeringGoal(default,Clear),originalAim,"Soft-corner sample density changed the accepted carrot");
        foreach(double radians in new[]{.73,-1.2,Math.PI})
        {
            Vec Transform(Vec value)=>Movement.Rotate(value,radians)+new Vec(20,-7);
            var changed=MakePath(Dense(route,4).Select(Transform).ToArray());changed.Next(Transform(default),Clear);
            Near(changed.SteeringGoal(Transform(default),Clear),Transform(originalAim),
                "A rigid transform changed fluid route geometry");
        }
        Vec[] tiny=Enumerable.Range(0,1001).Select(at=>new Vec(at*.005,0)).ToArray();
        var bounded=MakePath(tiny);Vec checkpoint=bounded.Next(default,Blocked)!.Value;int calls=0,index=bounded.Index;
        Vec aim=bounded.SteeringGoal(default,(_,_)=>{calls++;return true;});
        Check(calls<=256 && bounded.Index==index && !bounded.RollingCornerApproved &&
            aim.X>=checkpoint.X && aim.X<=checkpoint.X+.2,
            "Dense work cap permitted an unproved shortcut or advanced physical progress");
    }

    static RecoveryPath StaleLongRoute()
    {
        var path=new RecoveryPath([new(0,0),new(8,0),new(16,0)],new(24,0));
        path.Next(default,Clear);path.SteeringGoal(default,Clear);
        path.Next(new(5,0),Clear);
        Near(path.SteeringGoal(new(5,0),Clear),new(8,0),
            "Projection rebased without a physical arrival or directional crossing");
        return path;
    }
    static void ObservedProjectionRecovery()
    {
        var path=StaleLongRoute();path.Next(new(7.5,0),Clear);int index=path.Index;
        Near(path.SteeringGoal(new(7.501,0),Clear),new(16,0),
            "A different position reused a prior physical-arrival proof");
        Near(path.SteeringGoal(new(7.5,0),Clear),new(11,0),
            "An ordinary observed arrival did not recover a sparse local projection");
        Check(path.Index==index,"Projection recovery advanced physical route progress");
        path.Next(new(8,0),Clear);Near(path.SteeringGoal(new(8,0),Clear),new(11.5,0),
            "Recovered projection did not retain continuous actual arc progress");
        path.Next(new(12,0),Clear);Near(path.SteeringGoal(new(12,0),Clear),new(15.5,0),
            "Long-segment rebase budget began at its remote vertex rather than the observed projection");

        var blocked=StaleLongRoute();blocked.Next(new(7.5,0),Clear);
        Near(blocked.SteeringGoal(new(7.5,0),(_,to)=>to!=new Vec(8,0)),new(16,0),
            "Observed-arrival rebasing bypassed a blocked original prefix");
        var outside=StaleLongRoute();outside.Next(new(7.6,.36),Clear);
        Near(outside.SteeringGoal(new(7.6,.36),Clear),new(16,0),
            "Proximity arrival bypassed the original steering corridor during rebasing");
    }

    static void ReentryUsesActualArc()
    {
        var path=new RecoveryPath([new(0,0),new(2,0),new(4,0),new(6,0),new(8,0)],new(12,0));
        path.Next(default,Clear);Near(path.SteeringGoal(default,Clear),new(3.5,0),"Initial adaptive aim failed");
        path.Next(new(0,.4),Clear);Near(path.SteeringGoal(new(0,.4),Clear),new(2,0),"Off-corridor steering did not fall back");
        path.Next(new(.1,0),Clear);Near(path.SteeringGoal(new(.1,0),Clear),new(2,0),
            "Reentry immediately jumped back to full lookahead");
        Check(path.SteeringStatus=="Reacquiring","Bounded reentry was not retained as a pending ramp");
        for(int poll=0;poll<100;poll++)
        {
            path.Next(new(.1,0),Clear);
            Near(path.SteeringGoal(new(.1,0),Clear),new(2,0),
                "Stationary polling refilled adaptive lookahead");
        }
        path.Next(new(.2,0),Clear);Near(path.SteeringGoal(new(.2,0),Clear),new(2.2,0),
            "Reentry did not grow only by the measured forward arc");
        path.Next(new(.6,0),Clear);Near(path.SteeringGoal(new(.6,0),Clear),new(3,0),
            "Reentry horizon refilled faster than actual route progress");
        path.Next(new(.5,0),Clear);Near(path.SteeringGoal(new(.5,0),Clear),new(2,0),
            "A backwards reading renewed monotonic route projection");
        path.Next(new(.6,0),Clear);Near(path.SteeringGoal(new(.6,0),Clear),new(2,0),
            "A second dropout renewed the prior full horizon");
    }

    static void NoMotion(ArrivalPulseAdaptation helper,long at,Vec? position=null,string status="NoMovement",double held=17)
    {
        Vec p=position??default;
        helper.Observe(PulseGoal,p,p,p,.6,16,held,true,status,at);
    }
    static ArrivalPulseAdaptation Qualified(long first=1000,long second=1100)
    {
        var helper=new ArrivalPulseAdaptation();NoMotion(helper,first);NoMotion(helper,second);return helper;
    }
    static void PulseQualification()
    {
        var one=new ArrivalPulseAdaptation();NoMotion(one,1000);
        Check(one.Select(PulseGoal,default,.6,16,1001)==16,"One quiet sample offered a longer pulse");
        NoMotion(one,1100,status:"DelayedNoMovement",held:31.999);
        Check(one.Select(PulseGoal,default,.6,16,1101)==32,"Two qualified actually-held taps did not offer32ms");
        for(int cycle=0;cycle<3;cycle++)
        {
            NoMotion(one,1200+cycle*200);NoMotion(one,1300+cycle*200);
            Check(one.Select(PulseGoal,default,.6,16,1301+cycle*200)==16,
                "No-motion repeat cycles rearmed the same quiet-location offer");
        }
        var notMinimum=Qualified();Check(notMinimum.Select(PulseGoal,default,.6,32,1101)==32,
            "A planned longer pulse was altered by minimum-frame adaptation");
        var withinNoise=Qualified();Check(withinNoise.Select(PulseGoal,new(.009,0),.6,16,1101)==32,
            "Small map-unit quiet position noise incorrectly discarded the offer");
    }

    static void PulseExclusions()
    {
        foreach(var invalid in new[]{
            (Settled:false,Status:"NoMovement",Requested:16,Held:17.0,Released:new Vec(),After:new Vec()),
            (Settled:true,Status:"Unknown",Requested:16,Held:17.0,Released:new Vec(),After:new Vec()),
            (Settled:true,Status:"Quiet",Requested:16,Held:17.0,Released:new Vec(),After:new Vec()),
            (Settled:true,Status:"NoMovement",Requested:16,Held:15.999,Released:new Vec(),After:new Vec()),
            (Settled:true,Status:"NoMovement",Requested:16,Held:32.0,Released:new Vec(),After:new Vec()),
            (Settled:true,Status:"NoMovement",Requested:16,Held:double.NaN,Released:new Vec(),After:new Vec()),
            (Settled:true,Status:"NoMovement",Requested:16,Held:double.PositiveInfinity,Released:new Vec(),After:new Vec()),
            (Settled:true,Status:"NoMovement",Requested:32,Held:17.0,Released:new Vec(),After:new Vec()),
            (Settled:true,Status:"NoMovement",Requested:16,Held:17.0,Released:new Vec(.02,0),After:new Vec()),
            (Settled:true,Status:"NoMovement",Requested:16,Held:17.0,Released:new Vec(0,.02),After:new Vec(0,.02)),
            (Settled:true,Status:"NoMovement",Requested:16,Held:17.0,Released:new Vec(.03,0),After:new Vec(.03,0))})
        {
            var helper=new ArrivalPulseAdaptation();NoMotion(helper,1000);
            helper.Observe(PulseGoal,default,invalid.Released,invalid.After,.6,invalid.Requested,
                invalid.Held,invalid.Settled,invalid.Status,1100);
            Check(helper.Select(PulseGoal,invalid.After,.6,16,1101)==16,
                "Unqualified released-motion or held-time evidence offered a longer pulse");
        }
        Throws<InvalidOperationException>(()=>Qualified().Observe(PulseGoal,default,new(double.NaN,0),default,
            .6,16,17,true,"NoMovement",1101),"Unknown released position was admitted");
        Throws<InvalidOperationException>(()=>Qualified().Select(new(double.NaN,0),default,.6,16,1101),
            "Unknown pulse goal was admitted");
        Throws<ArgumentOutOfRangeException>(()=>Qualified().Select(PulseGoal,default,0,16,1101),
            "Invalid arrival tolerance was admitted");
        Throws<ArgumentOutOfRangeException>(()=>Qualified().Select(PulseGoal,default,.6,16,-1),
            "Negative observation clock was admitted");
    }

    static void PulseOwnershipAndExpiry()
    {
        Check(Qualified().Select(new(2.001,0),default,.6,16,1101)==16,"A new goal reused old pulse evidence");
        Check(Qualified().Select(PulseGoal,default,.601,16,1101)==16,"A new tolerance reused old pulse evidence");
        Check(Qualified().Select(PulseGoal,new(.011,0),.6,16,1101)==16,"A changed quiet position reused old pulse evidence");
        var reset=Qualified();reset.Reset();Check(reset.Select(PulseGoal,default,.6,16,1101)==16,
            "Controller reset retained no-motion evidence");
        Check(Qualified(1000,2900).Select(PulseGoal,default,.6,16,3101)==16,
            "Recent second sample renewed a whole-streak age beyond two seconds");
        Check(Qualified(1000,2000).Select(PulseGoal,default,.6,16,3000)==32,
            "The inclusive two-second evidence boundary was rejected");
        Check(Qualified(1000,2000).Select(PulseGoal,default,.6,16,3001)==16,
            "Expired whole-streak evidence offered a pulse");
        var gap=new ArrivalPulseAdaptation();NoMotion(gap,1000);NoMotion(gap,3101);
        Check(gap.Select(PulseGoal,default,.6,16,3102)==16,"A long gap combined two no-motion observations");
        Check(Qualified().Select(PulseGoal,default,.6,16,1099)==16,
            "Clock regression renewed pulse evidence");
        var consumed=Qualified();consumed.Select(PulseGoal,default,.6,16,1101);
        NoMotion(consumed,1200);NoMotion(consumed,1300);
        Check(consumed.Select(PulseGoal,default,.6,16,1250)==16,
            "Clock regression rearmed a consumed same-location offer");
        consumed.Observe(PulseGoal,default,new(.05,0),new(.05,0),.6,16,17,true,"Quiet",1400);
        NoMotion(consumed,1500,new(.05,0));NoMotion(consumed,1600,new(.05,0));
        Check(consumed.Select(PulseGoal,new(.05,0),.6,16,1601)==32,
            "Actual progress did not allow fresh evidence at a new quiet location");
    }

    static void PulseClearance()
    {
        var motion=new ArrivalMotion();Vec forward=new(1,0);
        double smaller=motion.PulseClearance(16),larger=motion.PulseClearance(32);
        Check(larger>smaller,"Adaptive pulse did not reserve its larger displacement envelope");
        var permitted=Qualified();Check(permitted.TrySelectClearance(PulseGoal,default,forward,.6,16,1101,
            motion.PulseClearance,Clear,out int selected,out double step) && selected==32 && step==larger,
            "Actual clearance helper did not admit the independently clear32ms envelope");
        var rejected=Qualified();int calls=0;
        bool ShortOnly(Vec from,Vec to){calls++;return (to-from).Length<=(smaller+larger)/2;}
        Check(rejected.TrySelectClearance(PulseGoal,default,forward,.6,16,1101,motion.PulseClearance,
            ShortOnly,out selected,out step) && selected==16 && step==smaller && calls==2,
            "Blocked32ms envelope bypassed the separately clear ordinary16ms pulse");
        NoMotion(rejected,1200);NoMotion(rejected,1300);
        Check(rejected.Select(PulseGoal,default,.6,16,1301)==16,
            "A clearance-rejected larger pulse rearmed its consumed offer");
        var bothBlocked=Qualified();calls=0;
        Check(!bothBlocked.TrySelectClearance(PulseGoal,default,forward,.6,16,1101,motion.PulseClearance,
            (_,_)=>{calls++;return false;},out selected,out step) && selected==16 && calls==2,
            "Two blocked displacement envelopes admitted a pulse");
        Check(!Qualified().TrySelectClearance(PulseGoal,default,new(2,0),.6,16,1101,
            motion.PulseClearance,Clear,out _,out _),"Nonunit heading admitted an adaptive pulse");
        Check(!Qualified().TrySelectClearance(PulseGoal,default,forward,.6,16,1101,
            _=>double.NaN,Clear,out _,out _),"Unknown displacement envelope admitted a pulse");
        var survival=Qualified();var bounded=SurvivalCombatHandoff.BoundAdvance(Clear,default);
        Check(smaller<=SurvivalCombatHandoff.MaximumCorrection && larger>SurvivalCombatHandoff.MaximumCorrection &&
            survival.TrySelectClearance(PulseGoal,default,forward,.6,16,1101,motion.PulseClearance,
                bounded,out selected,out step) && selected==16 && step==smaller &&
            SurvivalCombatHandoff.MaximumMilliseconds==2000,
            "Adaptive pulse bypassed the complete urgent-survival displacement envelope or changed its time budget");
    }
}
