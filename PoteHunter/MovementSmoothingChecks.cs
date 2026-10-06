using System.Text.Json;

namespace PoteHunter;

internal static class MovementSmoothingChecks
{
    static void Require(bool value,string message)
    { if(!value)throw new InvalidOperationException("Movement smoothing: "+message); }

    public static async Task Run()
    {
        foreach(double sensitivity in new[]{-.004,.004,-.0007,.0007})
        foreach(double initial in new[]{-.1,.1,-.5,.5,-Math.PI,Math.PI})
        foreach(bool walking in new[]{false,true})
        foreach(int latency in new[]{20,60,100,160})
        foreach(int portions in new[]{1,4})
        foreach(double initialHeading in new[]{0.0,Math.PI-.01,-Math.PI+.01})
        {
            var steering=new SmoothSteering();var pending=new PriorityQueue<double,long>();
            double error=initial,heading=initialHeading;long now;
            int deadline=!walking && portions==1 && latency<=100?4000:8000;
            for(now=0;now<deadline && (Math.Abs(error)>.035 || pending.Count>0);now+=20)
            {
                while(pending.TryPeek(out double turn,out long at) && at<=now)
                {
                    pending.Dequeue();error-=turn;
                    heading=Math.Atan2(Math.Sin(heading-turn),Math.Cos(heading-turn));
                }
                Require(Math.Sign(error)==Math.Sign(initial) || Math.Abs(error)<=.035,
                    "delayed or partial feedback overshot the target beyond facing tolerance");
                int pixels=steering.Next(error,heading,sensitivity,walking,now);
                Require(Math.Abs(pixels)<=(walking?64:112),"turn exceeded the existing pixel bound");
                if(pixels!=0)
                {
                    Require(Math.Sign(pixels*sensitivity)==Math.Sign(error),"turn corrected away from the target");
                    Require(Math.Abs(pixels*sensitivity)<=(walking?.226:.301),"turn failed the elapsed-time angular cap");
                    for(int part=0;part<portions;part++)pending.Enqueue(pixels*sensitivity/portions,now+latency+part*20);
                }
            }
            Require(Math.Abs(error)<=.035 && now<deadline,"delayed heading did not converge for either sensitivity sign");
        }
        int stationaryQuarterTurn=QuarterTurn(false),walkingQuarterTurn=QuarterTurn(true);
        Require(stationaryQuarterTurn<=440 && walkingQuarterTurn<=480,
            "responsive 90-degree turning regressed to the previous slower ramp");
        var held=new SmoothSteering();
        Require(held.Next(1,0,.004,false,0)!=0 && held.Next(1,0,.004,false,20)==0 && held.Next(1,0,.004,false,60)==0,
            "new turns stacked before the client reported the previous heading");
        var partial=new SmoothSteering();int first=partial.Next(1,0,.004,false,0);
        double observed=first*.004*.25,remaining=1-observed;
        int second=partial.Next(remaining,-observed,.004,false,80);
        Require(second!=0 && first*.004*.75+second*.004<=remaining*.70,
            "partial feedback allowed pending turns beyond the remaining angular budget");
        var lost=new SmoothSteering();long lastLostSend=0;double lostOutstanding=0;
        for(int at=0;at<=200;at+=20)
        {
            int sent=lost.Next(.04,0,.004,false,at);
            if(sent!=0){lastLostSend=at;lostOutstanding+=sent*.004;}
            Require(lostOutstanding<=.04*.70+1e-9,"unobserved turns exceeded their reserved angle");
        }
        Require(lost.Next(.04,0,.004,false,lastLostSend+219)==0 &&
            lost.Next(.04,0,.004,false,lastLostSend+220)!=0,
            "a dropped turn did not respect the bounded retry interval after the latest command");
        var reversed=new SmoothSteering();int old=reversed.Next(.08,0,.004,false,0);
        Require(reversed.Next(.02,0,.004,false,40)==0 && reversed.Next(-.08,0,.004,false,80)==0 &&
            reversed.Next(-.08,-old*.004,.004,false,120)<0,
            "deadzone/reversed goals discarded an outstanding turn before feedback");
        foreach(double initialHeading in new[]{Math.PI-.01,-Math.PI+.01})
        {
            var wrapped=new SmoothSteering();int sent=wrapped.Next(-.1,initialHeading,.004,false,0);
            double responded=Math.Atan2(Math.Sin(initialHeading-sent*.004),Math.Cos(initialHeading-sent*.004));
            Require(wrapped.Next(.1,responded,.004,false,40)>0,
                "fully observed wraparound feedback left a residual budget blocking reversal");
            wrapped.Reset();
            Require(wrapped.Next(.1,2.8,.004,false,60)>0,"reset fabricated feedback from an old nonzero heading");
        }
        var response=new TurnResponse();bool stalled=false;
        for(int now=0;now<=1600;now+=20)
        {
            int sent=held.Next(1,0,.004,false,now);
            stalled|=response.Observe(default,0,sent,now,awaitingResponse:true);
        }
        Require(stalled,"cadence waiting hid an unresponsive turn");
        var unsent=new TurnResponse();
        Require(!unsent.Observe(default,0,0,0,true) && !unsent.Observe(default,0,0,10000,true),
            "waiting without any sent input fabricated a stall");
        var restoredResponse=new TurnResponse();restoredResponse.Observe(default,0,1,0);restoredResponse.Reset();
        Require(!restoredResponse.Observe(default,0,1,10000,true) && !restoredResponse.Observe(default,0,0,11499,true) &&
            restoredResponse.Observe(default,0,0,11500,true),"new facing goal inherited an idle timer or waits hid a real stall");
        var quantized=new ArrivalMotion();double originalSpeed=quantized.Speed;
        foreach(double moved in new[]{.0032626,0,.326145,.341727})quantized.ObservePulse(moved,9);
        Require(quantized.Speed==originalSpeed && quantized.PulseMilliseconds(.334,.15)>=16,
            "recorded sub-frame steps trained velocity or created another tiny tap");
        quantized.ObservePulse(.9201,54);
        Require(quantized.Speed<=originalSpeed*1.0625+1e-9,"one settling jump changed speed too abruptly");

        long facingClock=10000;int resetCount=0,faceCalls=0,retries=0;
        Task FacingDelay(int ms,CancellationToken ct){ct.ThrowIfCancellationRequested();facingClock+=ms;return Task.CompletedTask;}
        int observations=await FacingRestore.RunAsync(_=>Task.FromResult(++faceCalls>110),()=>resetCount++,()=>{},FacingDelay,
            ()=>facingClock,()=>new TurnUnresponsiveException(default,default),default,3,_=>retries++);
        Require(observations==111 && retries==1 && resetCount>=3,"facing used an iteration cap or failed to retry a bounded goal");
        facingClock=0;resetCount=0;faceCalls=0;bool facingStopped=false;
        try {await FacingRestore.RunAsync(_=>{faceCalls++;return Task.FromResult(false);},()=>resetCount++,()=>{},FacingDelay,
            ()=>facingClock,()=>new TurnUnresponsiveException(default,default),default,3);}
        catch(TurnUnresponsiveException){facingStopped=true;}
        Require(facingStopped && facingClock<=6500 && resetCount==5,"unresponsive facing retry was unbounded");
        using(var facingCancel=new CancellationTokenSource())
        {
            facingCancel.Cancel();bool interrupted=false;
            try {await FacingRestore.RunAsync(_=>throw new Exception("Cancelled facing emitted input"),()=>{},()=>{},FacingDelay,
                ()=>facingClock,()=>new TurnUnresponsiveException(default,default),facingCancel.Token,3);}
            catch(OperationCanceledException){interrupted=true;}
            Require(interrupted,"facing retry swallowed a deliberate cancellation");
        }

        Require(FacingRestore.TimeoutMilliseconds(30)==8000 && FacingRestore.TimeoutMilliseconds(165)==3091 &&
            FacingRestore.TimeoutMilliseconds(360)==2500,"saved-facing deadline conflicts with the configured turn cap");
        foreach(double invalidCap in new[]{double.NaN,double.PositiveInfinity,29d,361d})
        {
            bool rejected=false;try{FacingRestore.TimeoutMilliseconds(invalidCap);}catch(ArgumentOutOfRangeException){rejected=true;}
            Require(rejected,"invalid turn cap produced a saved-facing deadline");
        }
        // A measured, responsive half-turn at a deliberately slow cap must be
        // allowed to finish. Simulate reader latency and 500ms delayed motion;
        // the real no-progress detector still watches each measured heading.
        foreach(double cap in new[]{30d,60d,165d})
        {
            facingClock=0;var progressiveResponse=new TurnResponse();int timeout=FacingRestore.TimeoutMilliseconds(cap);
            var failures=new List<FacingRestoreFailure>();
            bool prepared=false;
            await FacingRestore.RunAsync(_=>
            {
                Require(prepared,"saved-facing emitted correction before its movement reset");
                facingClock+=100;
                double observed=Math.Min(Math.PI,Math.Max(0,facingClock-500)*cap*Math.PI/180/1000);
                if(Math.PI-observed<=.035)return Task.FromResult(true);
                if(progressiveResponse.Observe(default,observed,1,facingClock,true))
                    throw new TurnUnresponsiveException(default,default);
                return Task.FromResult(false);
            },()=>{prepared=true;progressiveResponse.Reset();},()=>{},FacingDelay,()=>facingClock,
                ()=>new TurnUnresponsiveException(default,default),default,
                timeoutMilliseconds:timeout,failureObserved:failures.Add);
            Require(failures.Count==0 && facingClock<=timeout && (cap!=30 || facingClock>6000),
                "responsive low-cap saved facing was treated as an unresponsive two-second turn");
        }
        // A healthy but never-completed goal and a real no-progress fault have
        // distinct diagnostics. The longer goal allowance never extends the
        // 1500ms no-response watchdog or consumes cancellation/focus failures.
        facingClock=0;var facingFailures=new List<FacingRestoreFailure>();
        var deadlineException=new TurnUnresponsiveException(default,default,"Saved-facing deadline exceeded.");
        Exception? observedFailure=null;
        try
        {
            await FacingRestore.RunAsync(_=>{facingClock+=100;return Task.FromResult(false);},()=>{},()=>{},
                FacingDelay,()=>facingClock,()=>new TurnUnresponsiveException(default,default),default,
                timeoutMilliseconds:FacingRestore.TimeoutMilliseconds(30),failureObserved:facingFailures.Add,
                deadlineFailure:()=>deadlineException);
        }
        catch(TurnUnresponsiveException ex){observedFailure=ex;}
        Require(ReferenceEquals(observedFailure,deadlineException) && facingFailures.Count==1 &&
            facingFailures[0] is {Reason:"Deadline",Attempt:1,TimeoutMilliseconds:8000} &&
            facingClock>=8000 && facingClock<=8120,"saved-facing deadline did not preserve its reason or hard bound");
        facingClock=0;facingFailures.Clear();var noProgressResponse=new TurnResponse();
        var noProgressException=new TurnUnresponsiveException(default,default);int deadlineFactories=0;
        try
        {
            await FacingRestore.RunAsync(_=>
            {
                if(noProgressResponse.Observe(default,0,1,facingClock,true))throw noProgressException;
                return Task.FromResult(false);
            },noProgressResponse.Reset,()=>{},FacingDelay,()=>facingClock,
                ()=>new TurnUnresponsiveException(default,default),default,
                timeoutMilliseconds:FacingRestore.TimeoutMilliseconds(30),failureObserved:facingFailures.Add,
                deadlineFailure:()=>{deadlineFactories++;return deadlineException;});
        }
        catch(TurnUnresponsiveException ex){observedFailure=ex;}
        Require(ReferenceEquals(observedFailure,noProgressException) && deadlineFactories==0 &&
            observedFailure.Message=="The game did not respond to sustained turning input." &&
            facingFailures.Count==1 && facingFailures[0] is {Reason:"NoProgress",ElapsedMilliseconds:1500},
            "speed-derived deadline hid or relabelled a real unresponsive turn");
        foreach(bool focusFailure in new[]{false,true})
        {
            facingClock=0;facingFailures.Clear();int stopCalls=0;bool interrupted=false;
            using var interruptedFacing=new CancellationTokenSource();
            try
            {
                await FacingRestore.RunAsync(ct=>
                {
                    if(focusFailure)throw new OperationCanceledException("Game lost focus.");
                    interruptedFacing.Cancel();ct.ThrowIfCancellationRequested();return Task.FromResult(false);
                },()=>{},()=>stopCalls++,FacingDelay,()=>facingClock,
                    ()=>new TurnUnresponsiveException(default,default),interruptedFacing.Token,3,
                    timeoutMilliseconds:8000,failureObserved:facingFailures.Add);
            }
            catch(OperationCanceledException){interrupted=true;}
            Require(interrupted && facingFailures.Count==0 && stopCalls==0,
                "extended saved-facing retries swallowed cancellation or a lost-focus guard");
        }

        // Reproduce a fast approach that would cross a .15-unit anchor during
        // one held-W tick; braking followed by observed, settled corrections
        // converges without enlarging the acceptance radius.
        foreach(double speed in new[]{.004,.009,.014})
        foreach(double initial in new[]{.16,.456,.718,1.5,3.0})
        {
            var motion=new ArrivalMotion();double position=initial;int steps=0;
            while(Math.Abs(position)>.15 && steps++<40)
            {
                int pulse=motion.PulseMilliseconds(Math.Abs(position),.15);
                double moved=speed*Math.Ceiling(pulse/16.0)*16;
                position-=Math.Sign(position)*moved;
                motion.ObservePulse(moved,pulse);
            }
            Require(Math.Abs(position)<=.15 && steps<40,"frame-quantized final approach failed to settle");
        }
        Require(!AnchorArrival.Settled(new(.1,0),new(.2443,0),default,.15),"post-arrival drift from the recorded failure was accepted");
        Require(!AnchorArrival.Settled(new(.1,0),new(.14,0),default,.15),"moving position was accepted before settling");

        Vec pos=new(.10,0);long clock=0;int moves=0,faces=0,stops=0;bool drift=true;
        async Task Delay(int ms,CancellationToken token) {token.ThrowIfCancellationRequested();clock+=ms;await Task.CompletedTask;}
        bool success=await AnchorArrival.ReturnAsync(()=>pos,default,.15,
            _=>{moves++;pos=new(.04,0);return Task.CompletedTask;},()=>stops++,
            _=>{faces++;if(drift){pos=new(.2443,0);drift=false;}return Task.CompletedTask;},Delay,()=>clock,default);
        Require(success && moves==1 && faces==2 && stops>=4 && pos.X==.04,
            "facing-induced drift did not force a fresh settled arrival");
        clock=0;stops=0;pos=new(1,0);
        bool failed=await AnchorArrival.ReturnAsync(()=>pos,default,.15,_=>Task.CompletedTask,()=>stops++,
            _=>throw new Exception("Facing must not run before arrival"),Delay,()=>clock,default);
        Require(!failed && clock>=15000 && stops>0,"blocked return did not stop within its deadline");
        using var cancel=new CancellationTokenSource();cancel.Cancel();bool cancelled=false;stops=0;
        try {await AnchorArrival.ReturnAsync(()=>pos,default,.15,_=>Task.CompletedTask,()=>stops++,
            _=>Task.CompletedTask,Delay,()=>clock,cancel.Token);}
        catch(OperationCanceledException){cancelled=true;}
        Require(cancelled && stops>0,"cancelled return did not release movement");
        bool rejected=false;stops=0;
        try {await AnchorArrival.ReturnAsync(()=>pos,default,.15,_=>throw new OperationCanceledException("focus lost"),()=>stops++,
            _=>Task.CompletedTask,Delay,()=>clock,default);}
        catch(OperationCanceledException){rejected=true;}
        Require(rejected && stops>0,"focus/preflight rejection did not release movement");

        var pressure=new CombatPressure();pressure.Observe(new(100,100),0);
        for(int attempt=1;attempt<=20;attempt++)
        {
            pressure.Observe(new(100-attempt,100),attempt*200);
            Require(!HealingRest.ReadyAfterDamage(pressure.LastDamageAt,attempt*200+100),
                "repeated damage allowed immediate rest re-entry");
        }
        Require(!HealingRest.ReadyAfterDamage(pressure.LastDamageAt,6999) &&
            HealingRest.ReadyAfterDamage(pressure.LastDamageAt,7000) &&
            !HealingRest.ReadyAfterDamage(pressure.LastDamageAt,3999),"shared quiet period boundary failed");

        // Staying anchored must still allow a permitted, living in-range
        // defensive candidate; it must not broaden the target family/range.
        var permitted=new Entity(100,0x80000001,"Lv. 1 Pulkhan",new(2,0),0,Generation:1);
        var distant=permitted with {Id=0x80000002,Address=101,Position=new(2.51,0)};
        var other=permitted with {Id=0x80000003,Address=102,Name="Lv. 1 Other monster",Position=new(1,0)};
        var health=new Dictionary<uint,Health>{{permitted.Id,new(100,100)},{distant.Id,new(100,100)},{other.Id,new(100,100)}};
        Require(CombatPressure.ChooseDefense([other,distant,permitted],health,default,2.5,(e,h)=>Targeting.MatchesName(e.Name,"Pulkhan"))==permitted,
            "stationary defense ignored the family or melee boundary");
        Require(CombatPressure.ChooseDefense([permitted],health,default,2.5,(_,_)=>false)==null,
            "stationary defense bypassed a protection refusal");

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"movement-smoothing-checks.json"),JsonSerializer.Serialize(new
        {
            Passed=true,HardwareInputEmitted=false,StationaryQuarterTurnMilliseconds=stationaryQuarterTurn,
            WalkingQuarterTurnMilliseconds=walkingQuarterTurn,
            Checks=new[]{"delayed and partial heading response while walking/stationary with both calibration signs",
                "no overshoot beyond facing tolerance","responsive 90-degree turning","pending commanded-angle budget",
                "deadzone/reversal retains pending feedback",
                "bounded lost-command retry","time-bounded turn size","unresponsive turn deadline",
                "unsent turns cannot start stall timing","new facing goal clears idle timer","bounded post-loot facing retry and cancellation",
                "slow-cap progressive saved facing and delayed feedback","speed-derived bounded facing deadline",
                "saved-facing deadline distinguished from real no-progress watchdog","extended facing cancellation and focus guards",
                "recorded sub-frame steps excluded from speed training","frame-sized pulse and filtered speed gain",
                "frame-quantized precise arrival","recorded post-facing drift rejected","settle then face then recheck",
                "blocked/cancelled/focus-lost return releases movement","persistent damage quiet period","stationary defense family/range/protection gates"}
        },new JsonSerializerOptions{WriteIndented=true}));
    }

    static int QuarterTurn(bool walking)
    {
        var steering=new SmoothSteering();double error=Math.PI/2,heading=0;
        for(int now=0;now<2000;now+=20)
        {
            if(Math.Abs(error)<=.035)return now;
            int pixels=steering.Next(error,heading,.004,walking,now);
            double turn=pixels*.004;error-=turn;heading-=turn;
        }
        throw new InvalidOperationException("Movement smoothing: 90-degree turn did not converge");
    }
}
