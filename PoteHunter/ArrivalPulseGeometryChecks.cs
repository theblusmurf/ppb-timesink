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
            delays++;clock+=milliseconds;return Task.CompletedTask;}
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
        Require(!changing.Settled && clock==ArrivalPulseSettling.MaximumMilliseconds && reads==6,
            "cumulative motion masqueraded as jitter or renewed its original deadline");
        int lateDelays=0;clock=0;
        Task SlowGuardedDelay(int ms,CancellationToken token)
        {token.ThrowIfCancellationRequested();lateDelays++;clock+=ms+350;return Task.CompletedTask;}
        var late=await ArrivalPulseSettling.ObserveAsync(before,before,()=>new(clock*.01,0),SlowGuardedDelay,()=>clock,default);
        Require(!late.Settled && lateDelays==1,
            "slow guard/reader work renewed settling waits after the fixed deadline");
        lateDelays=0;clock=0;
        var stableLate=await ArrivalPulseSettling.ObserveAsync(before,new(observed,0),()=>new(observed,0),
            SlowGuardedDelay,()=>clock,default);
        Require(!stableLate.Settled && stableLate.ElapsedMilliseconds==470 && lateDelays==1,
            "a stable late read learned motion or admitted another correction beyond the fixed deadline");
        clock=0;int exactDelays=0;
        Task ExactBoundaryDelay(int ms,CancellationToken token)
        {token.ThrowIfCancellationRequested();clock+=++exactDelays==1?240:80;return Task.CompletedTask;}
        var exact=await ArrivalPulseSettling.ObserveAsync(before,before,()=>new(observed,0),ExactBoundaryDelay,()=>clock,default);
        Require(exact.Settled && exact.ElapsedMilliseconds==320 && exactDelays==2,
            "an actually quiet read exactly at the original deadline was rejected or given a new deadline");

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
            FixedObservationWindowMilliseconds=ArrivalPulseSettling.MaximumMilliseconds,NoSpeculativeCollisionObstacle=true}));
    }
}
