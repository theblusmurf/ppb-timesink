using System.Text.Json;

namespace PoteHunter;

internal static class ArrivalPulseGeometryChecks
{
    static void Require(bool value,string reason)
    {if(!value)throw new InvalidOperationException("Arrival pulse geometry: "+reason);}

    internal static async Task Run()
    {
        // The recorded .100 goal settled .273 units away after a correctly
        // timed 16.710ms hold. A near wall/leash must reserve physical motion,
        // rather than accepting only the smaller requested goal segment.
        const double observed=.273;
        Vec before=new(),forward=new(1,0),target=new(2.512,0);
        var motion=new ArrivalMotion();double initialSpeed=motion.Speed;
        int duration=motion.PulseMilliseconds(.100,.025);
        motion.ObservePulse(observed,16.710);
        double envelope=motion.PulseClearance(duration);
        Require(duration==16 && motion.Speed==initialSpeed && envelope>=observed+.025,
            "a short physical quantum trained velocity, shortened input or reserved only the goal");
        bool Wall(Vec from,Vec to)=>to.X<=.200;
        Require(Wall(before,before+forward*.100) && !ArrivalPulseGeometry.PathClear(before,forward,envelope,Wall),
            "the old clipped .100 guard and full .273-or-larger motion envelope were not distinguished");
        bool Leash(Vec from,Vec to)=>(to-new Vec()).Length<=1.5;
        var edge=new Vec(1.3,0);
        Require(Leash(edge,edge+forward*.100) && !ArrivalPulseGeometry.PathClear(edge,forward,envelope,Leash),
            "a tiny goal admitted a full physical pulse beyond the anchor leash");
        Require(ArrivalPulseGeometry.PathClear(before,forward,envelope,Leash) &&
            (target-(before+forward*observed)).Length<=2.5 && (before+forward*observed).Length<1.5,
            "a clear short assist failed verified actual range entry inside the original leash");

        // Aim can change the position. Use its fresh origin to test the whole
        // envelope, rather than retaining a stale pre-facing collision result.
        bool ShiftedWall(Vec from,Vec to)=>to.X<=.450;
        Require(ArrivalPulseGeometry.PathClear(before,forward,envelope,ShiftedWall) &&
            !ArrivalPulseGeometry.PathClear(new(.15,0),forward,envelope,ShiftedWall),
            "post-aim geometry inherited a stale clearance result");
        foreach(var origin in new[]{before,new Vec(1000,-5000)})
        for(int i=0;i<628;i++)
        {
            double angle=i*.01;var direction=new Vec(Math.Cos(angle),Math.Sin(angle));
            Vec checkedFrom=default,checkedTo=default;
            Require(ArrivalPulseGeometry.PathClear(origin,direction,envelope,(from,to)=>
                {checkedFrom=from;checkedTo=to;return true;}) && checkedFrom==origin &&
                Math.Abs((checkedTo-origin).Length-envelope)<1e-8,
                "translated or rotated reservation changed the full physical step");
        }
        Require(!ArrivalPulseGeometry.PathClear(new(double.NaN,0),forward,envelope,(_,_)=>true) &&
            !ArrivalPulseGeometry.PathClear(before,new(2,0),envelope,(_,_)=>true) &&
            !ArrivalPulseGeometry.PathClear(before,forward,double.NaN,(_,_)=>true),
            "invalid current position, direction or displacement admitted a correction");

        for(int requested=16;requested<=60;requested++)
        {
            int aligned=ArrivalPulseGeometry.FrameDuration(requested);
            Require(aligned is 16 or 32 or 48 or 60 && aligned<=requested && aligned>=16,
                "sub-frame correction duration was rounded upward or escaped its existing bounds");
        }
        Require(ArrivalPulseGeometry.FrameDuration(31)==16 && ArrivalPulseGeometry.FrameDuration(47)==32 &&
            ArrivalPulseGeometry.FrameDuration(59)==48 && ArrivalPulseGeometry.FrameDuration(60)==60,
            "intermediate holds retained an avoidable additional client frame");
        foreach(int invalid in new[]{0,15,61,int.MaxValue})
        {
            bool rejected=false;
            try{ArrivalPulseGeometry.FrameDuration(invalid);}catch(ArgumentOutOfRangeException){rejected=true;}
            Require(rejected,"invalid correction duration did not fail before input");
            rejected=false;
            try{motion.PulseClearance(invalid);}catch(ArgumentOutOfRangeException){rejected=true;}
            Require(rejected,"invalid physical-path duration was silently accepted");
        }
        foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,-1d})
        {
            bool rejected=false;
            try{motion.PulseMilliseconds(invalid,.025);}catch(ArgumentOutOfRangeException){rejected=true;}
            Require(rejected,"unavailable requested geometry admitted a correction");
            rejected=false;
            try{motion.PulseMilliseconds(.100,invalid);}catch(ArgumentOutOfRangeException){rejected=true;}
            Require(rejected,"invalid arrival tolerance admitted a correction");
        }
        var shortModel=new ArrivalMotion();double originalEnvelope=shortModel.PulseClearance(16);
        foreach(double invalid in new[]{0,-1,.003,double.NaN,double.PositiveInfinity,3.01})
            shortModel.ObservePulse(invalid,16.5);
        Require(shortModel.PulseClearance(16)==originalEnvelope && shortModel.Speed==initialSpeed,
            "no-response, jitter or invalid motion contaminated settled displacement");
        shortModel.ObservePulse(.55,16.5);
        Require(shortModel.PulseClearance(16)>=.575 && shortModel.Speed==initialSpeed,
            "a larger short settled displacement was ignored or treated as a new velocity");
        for(int i=0;i<5;i++)shortModel.ObservePulse(.273,16.5);
        Require(shortModel.PulseClearance(16)<.575 && shortModel.PulseClearance(16)>=.298,
            "bounded recent quantum evidence either retained an obsolete sample forever or forgot the physical step");
        Require(ArrivalPulseGeometry.ForwardObservation(before,new(observed,0),forward) &&
            !ArrivalPulseGeometry.ForwardObservation(before,new(-observed,0),forward) &&
            !ArrivalPulseGeometry.ForwardObservation(before,new(0,observed),forward) &&
            !ArrivalPulseGeometry.ForwardObservation(before,new(.003,.003),forward),
            "changed heading, reverse/lateral movement or jitter trained forward pulse motion");

        long clock=0;int reads=0,delays=0,pulses=1;
        Task Delay(int milliseconds,CancellationToken token)
        {token.ThrowIfCancellationRequested();Require(pulses==1,"another pulse was queued before the observation completed");
            if(milliseconds>0)delays++;clock+=milliseconds;return Task.CompletedTask;}
        Vec DelayedPosition(){reads++;return clock<160?before:new Vec(observed,0);}
        var delayed=await ArrivalPulseSettling.ObserveAsync(before,before,DelayedPosition,Delay,()=>clock,default);
        Require(delayed.Settled && delayed.Position==new Vec(observed,0) && clock==240 && reads==4,
            "zero immediate displacement or delayed publication completed before a cumulative quiet window");
        // Immediate published motion retains the existing 120ms fast path.
        clock=0;reads=0;delays=0;
        var immediate=await ArrivalPulseSettling.ObserveAsync(before,new(observed,0),()=>
            {reads++;return new(observed,0);},Delay,()=>clock,default);
        Require(immediate.Settled && clock==120 && reads==1 && delays==1,
            "already published stable movement acquired an unnecessary extra observation wait");
        clock=0;reads=0;delays=0;
        var noResponse=await ArrivalPulseSettling.ObserveAsync(before,before,()=>{reads++;return before;},Delay,()=>clock,default);
        Require(noResponse.Settled && noResponse.Position==before && clock==200 && reads==3,
            "zero displacement immediately authorized another tiny tap or waited without a bound");
        // Each change is smaller than .025, but two samples together are not.
        // A continuously moving read cannot reset the fixed deadline or become
        // a learned collision obstacle: the caller receives a distinct failure.
        clock=0;reads=0;delays=0;
        Vec ChangingPosition(){reads++;return new(clock*.0006,0);}
        var changing=await ArrivalPulseSettling.ObserveAsync(before,before,ChangingPosition,Delay,()=>clock,default);
        Require(!changing.Settled && clock==ArrivalPulseSettling.MaximumMilliseconds &&
            reads==1+(ArrivalPulseSettling.MaximumMilliseconds-ArrivalPulseSettling.InitialMilliseconds)/ArrivalPulseSettling.SampleMilliseconds,
            "cumulative motion masqueraded as jitter or renewed its original deadline");

        // Reported route-start failures ended at328-375ms under the old320ms
        // stop. Replay synchronous position latency while keeping the original
        // released-input owner and safety check; no replacement pulse is sent.
        foreach(int completion in new[]{328,333,350,364,375})
        {
            clock=0;reads=0;int postReadGuards=0;
            Vec SlowStablePosition(){reads++;clock+=completion-120;return new(observed,0);}
            Task GuardedDelay(int ms,CancellationToken token)
            {token.ThrowIfCancellationRequested();Require(pulses==1,"late observation queued another pulse");
                if(ms==0)postReadGuards++;clock+=ms;return Task.CompletedTask;}
            var stable=await ArrivalPulseSettling.ObserveAsync(before,new(observed,0),SlowStablePosition,GuardedDelay,()=>clock,default);
            Require(stable.Settled && stable.ElapsedMilliseconds==completion && stable.GraceUsed &&
                stable.Status=="DelayedQuiet" && reads==1 && postReadGuards==1 &&
                stable.MaximumReadMilliseconds==completion-120 && stable.GraceReads==1,
                "a reported stable late observation still stopped instead of using bounded guarded grace");
        }
        clock=0;reads=0;
        Vec DelayedGracePosition(){reads++;clock+=205;return reads==1?before:new Vec(observed,0);}
        var graceDelayed=await ArrivalPulseSettling.ObserveAsync(before,before,DelayedGracePosition,Delay,()=>clock,default);
        // First zero snapshot at325ms is valid quiet evidence after200ms; the
        // min read age still applies, without fabricating additional movement.
        Require(graceDelayed.Settled && graceDelayed.Position==before && clock==325 && graceDelayed.GraceUsed,
            "delayed zero-result observation changed the minimum age or demanded a new pulse");

        // Motion first published after the normal window needs an actual later
        // position read confirming80ms cumulative quiet before input can resume.
        clock=0;reads=0;
        Vec LatePublishedPosition(){reads++;clock+=reads==1?205:45;return new(observed,0);}
        var latePublished=await ArrivalPulseSettling.ObserveAsync(before,before,LatePublishedPosition,Delay,()=>clock,default);
        Require(latePublished.Settled && clock==410 && reads==2 && latePublished.QuietMilliseconds==85,
            "late motion completed from one changed read instead of a fresh quiet confirmation");

        // Start is captured before the initial released-position read. Its375ms
        // latency consumes the budget, but never earns quiet time for itself.
        clock=375;reads=0;
        var initialReadSlow=await ArrivalPulseSettling.ObserveAsync(before,new(observed,0),()=>
            {reads++;return new(observed,0);},Delay,()=>clock,default,startedAt:0);
        Require(initialReadSlow.Settled && clock==455 && reads==3 && initialReadSlow.QuietMilliseconds==80,
            "unobserved initial read latency masqueraded as confirmed quiet motion");
        clock=951;reads=0;
        var consumedInitialBudget=await ArrivalPulseSettling.ObserveAsync(before,new(observed,0),()=>
            {reads++;return new(observed,0);},Delay,()=>clock,default,startedAt:0);
        Require(!consumedInitialBudget.Settled && clock==1000 && consumedInitialBudget.QuietMilliseconds==49,
            "an initial late read renewed the hard budget instead of retaining its original deadline");

        // A post-read synchronous guard consumes deadline time but cannot turn
        // one changed or too-early zero snapshot into a quiet position interval.
        clock=0;reads=0;int slowPostGuards=0;
        Task SlowPostGuard(int ms,CancellationToken token)
        {token.ThrowIfCancellationRequested();clock+=ms;if(ms==0){slowPostGuards++;clock+=100;}return Task.CompletedTask;}
        var changedWithGuard=await ArrivalPulseSettling.ObserveAsync(before,before,()=>
            {reads++;return new(observed,0);},SlowPostGuard,()=>clock,default);
        Require(changedWithGuard.Settled && reads==2 && clock==360 && changedWithGuard.PositionReadElapsedMilliseconds==260 &&
            changedWithGuard.QuietMilliseconds==140 && slowPostGuards==2,
            "unobserved post-read guard time made a single changed snapshot look quiet");
        clock=0;reads=0;slowPostGuards=0;
        var zeroWithGuard=await ArrivalPulseSettling.ObserveAsync(before,before,()=>{reads++;return before;},SlowPostGuard,()=>clock,default);
        Require(zeroWithGuard.Settled && reads==2 && zeroWithGuard.PositionReadElapsedMilliseconds>=200,
            "post-read guard latency bypassed the200ms zero-result observation minimum");

        int lateDelays=0;clock=0;reads=0;
        Task BeyondHardGuard(int ms,CancellationToken token)
        {token.ThrowIfCancellationRequested();lateDelays++;clock+=ms+1100;return Task.CompletedTask;}
        var lateGuard=await ArrivalPulseSettling.ObserveAsync(before,new(observed,0),()=>
            {reads++;return new(observed,0);},BeyondHardGuard,()=>clock,default);
        Require(!lateGuard.Settled && lateDelays==1 && reads==0 && lateGuard.Status=="GuardDeadline",
            "a guard beyond the hard deadline authorized a position read or renewed waits");
        clock=0;reads=0;
        var stableTooLate=await ArrivalPulseSettling.ObserveAsync(before,new(observed,0),()=>
            {reads++;clock+=900;return new(observed,0);},Delay,()=>clock,default);
        Require(!stableTooLate.Settled && stableTooLate.ElapsedMilliseconds==1020 && reads==1 &&
            stableTooLate.Status=="ReadDeadline" && stableTooLate.QuietDisplacement==0,
            "a stable read beyond the hard deadline learned motion or falsely claimed continued drift");

        clock=0;int exactDelays=0;
        Task ExactBoundaryDelay(int ms,CancellationToken token)
        {token.ThrowIfCancellationRequested();if(ms>0)clock+=++exactDelays==1?920:80;return Task.CompletedTask;}
        var exact=await ArrivalPulseSettling.ObserveAsync(before,before,()=>new(observed,0),ExactBoundaryDelay,()=>clock,default);
        Require(exact.Settled && exact.ElapsedMilliseconds==1000 && exactDelays==2,
            "an actually quiet read exactly at the original deadline was rejected or given a new deadline");

        foreach(string reason in new[]{"Game lost focus.","F9 stop key pressed.","Known death detected.","Character identity changed."})
        {
            clock=0;reads=0;bool readChangedSafety=false,interrupted=false;int safetyChecks=0;
            Task ChangingSafetyDelay(int ms,CancellationToken token)
            {token.ThrowIfCancellationRequested();clock+=ms;safetyChecks++;
                if(ms==0 && readChangedSafety)throw new OperationCanceledException(reason);return Task.CompletedTask;}
            try{await ArrivalPulseSettling.ObserveAsync(before,new(observed,0),()=>
                {reads++;readChangedSafety=true;return new(observed,0);},ChangingSafetyDelay,()=>clock,default);}
            catch(OperationCanceledException ex){interrupted=ex.Message==reason;}
            Require(interrupted && reads==1 && safetyChecks==2 && pulses==1,
                "a post-read focus/stop/death/identity failure was consumed or admitted settled success/new input");
        }
        clock=0;reads=0;int postCancelGuards=0;bool cancelledDuringRead=false;
        using(var duringReadCancellation=new CancellationTokenSource())
        {
            Task CountingGuard(int ms,CancellationToken token)
            {token.ThrowIfCancellationRequested();clock+=ms;if(ms==0)postCancelGuards++;return Task.CompletedTask;}
            try{await ArrivalPulseSettling.ObserveAsync(before,new(observed,0),()=>
                {reads++;duringReadCancellation.Cancel();return new(observed,0);},CountingGuard,()=>clock,duringReadCancellation.Token);}
            catch(OperationCanceledException){cancelledDuringRead=true;}
        }
        Require(cancelledDuringRead && reads==1 && postCancelGuards==0,
            "cancellation during a synchronous read reached safety/learning instead of propagating immediately");

        // Compose the actual released-motion observer with the urgent runner.
        // Earlier aim/preflight work has already spent1500ms of that runner's
        // original2s budget; a valid520ms settlement must not grant more time.
        var urgent=new SurvivalCombatHandoff();int urgentCasts=0,urgentStops=0,urgentMoves=0;
        var urgentTarget=new Entity(100,0x80001755,"Lv. 1 Pulkhan",new(2.593,0),10,Generation:1);
        var urgentSample=new SurvivalCombatHandoff.Observation(before,before,new(6992,16610),urgentTarget,
            new(9188,9188),2.5,true,true,true,7);
        clock=0;reads=0;bool forwardReleased=true;
        Task UrgentDelay(int ms,CancellationToken token)
        {token.ThrowIfCancellationRequested();Require(forwardReleased,"urgent settling held W or issued a second pulse");
            clock+=ms;return Task.CompletedTask;}
        var urgentOutcome=await urgent.RunAsync(()=>urgentSample,async (_,ct)=>
        {
            urgentMoves++;clock+=1500;
            var result=await ArrivalPulseSettling.ObserveAsync(before,before,()=>
                {reads++;clock+=180;return new(observed,0);},UrgentDelay,()=>clock,ct);
            Require(result.Settled && result.ElapsedMilliseconds==520,"composed late-settlement fixture failed");
            urgentSample=urgentSample with{Position=result.Position};
        },()=>{urgentStops++;forwardReleased=true;},(_,_)=>{urgentCasts++;return Task.CompletedTask;},
            UrgentDelay,()=>clock,default);
        Require(urgentOutcome==SurvivalHandoffOutcome.DurationExpired && urgentMoves==1 && urgentCasts==0 &&
            urgentStops>=2 && urgent.CorrectionConsumed && clock>=2000 && forwardReleased,
            "settling grace renewed the urgent deadline, lost correction/return ownership, or cast after the original bound");
        clock=0;reads=0;urgentCasts=urgentStops=0;urgent=new();bool urgentReadChanged=false,urgentFocusPropagated=false;
        urgentSample=urgentSample with{Position=before};
        Task UrgentSafetyDelay(int ms,CancellationToken token)
        {token.ThrowIfCancellationRequested();clock+=ms;if(ms==0&&urgentReadChanged)
            throw new OperationCanceledException("Game lost focus during settling read.");return Task.CompletedTask;}
        try
        {
            await urgent.RunAsync(()=>urgentSample,async (_,ct)=>
            {
                await ArrivalPulseSettling.ObserveAsync(before,new(observed,0),()=>
                    {reads++;urgentReadChanged=true;return new(observed,0);},UrgentSafetyDelay,()=>clock,ct);
            },()=>urgentStops++,(_,_)=>{urgentCasts++;return Task.CompletedTask;},UrgentSafetyDelay,()=>clock,default);
        }
        catch(OperationCanceledException ex){urgentFocusPropagated=ex.Message=="Game lost focus during settling read.";}
        Require(urgentFocusPropagated && reads==1 && urgentStops==1 && urgentCasts==0 && urgent.CorrectionConsumed,
            "composed urgent runner swallowed post-read safety failure or rearmed correction/cast ownership");

        foreach(bool focusLoss in new[]{false,true})
        {
            clock=0;reads=0;bool interrupted=false;using var cancelled=new CancellationTokenSource();
            Task InterruptDelay(int ms,CancellationToken token)
            {clock+=ms;if(focusLoss)throw new OperationCanceledException("Game lost focus.");
                cancelled.Cancel();token.ThrowIfCancellationRequested();return Task.CompletedTask;}
            try{await ArrivalPulseSettling.ObserveAsync(before,before,()=>{reads++;return before;},InterruptDelay,()=>clock,cancelled.Token);}
            catch(OperationCanceledException){interrupted=true;}
            Require(interrupted && reads==0,"focus/cancellation was consumed or a post-cancel snapshot admitted another pulse");
        }
        clock=0;bool unknownRejected=false;
        try{await ArrivalPulseSettling.ObserveAsync(before,before,()=>new(double.NaN,0),Delay,()=>clock,default);}
        catch(InvalidOperationException){unknownRejected=true;}
        Require(unknownRejected,"unknown post-release geometry was marked settled");

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"arrival-pulse-geometry-checks.json"),JsonSerializer.Serialize(new
        {Passed=true,RecordedGoal=.100,RecordedSettledDisplacement=observed,RecordedHeldMilliseconds=16.710,
            MinimumPulseMilliseconds=16,MaximumPulseMilliseconds=60,WholePhysicalPathReserved=true,
            ShortDisplacementSeparateFromVelocity=true,FreshPostAimGeometry=true,DelayedNoMotionObservedBeforeRepeat=true,
            CumulativeQuietMilliseconds=ArrivalPulseSettling.QuietMilliseconds,
            NormalObservationWindowMilliseconds=ArrivalPulseSettling.NormalMilliseconds,
            FixedObservationWindowMilliseconds=ArrivalPulseSettling.MaximumMilliseconds,
            StableReported328To375MillisecondsAccepted=true,PostReadSafetyAndCancellationRechecked=true,
            InitialReadAndGuardConsumeOriginalBudget=true,GuardTimeDoesNotFabricateQuiet=true,
            SharedUrgentRunnerRetainsOriginalDurationAndCorrection=true,NoSpeculativeCollisionObstacle=true}));
    }
}
