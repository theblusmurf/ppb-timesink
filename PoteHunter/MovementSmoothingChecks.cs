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
        // Replay a small fixed saved-facing goal with slow quantized measured
        // movement. Total required movement is less than the old universal
        // 0.02-radian watchdog threshold. The actual controller, response
        // detector and unchanged facing timeout must agree on progress.
        long fineClock=0;var fineSteering=new SmoothSteering();var fineRate=new TurnRateBudget();
        var fineResponse=new TurnResponse();var fineFailures=new List<FacingRestoreFailure>();
        const double fineStart=.04634;int fineSent=0;
        await FacingRestore.RunAsync(_=>
        {
            fineClock+=100;
            double measured=Math.Floor(fineClock/400.0)*.0024,error=fineStart-measured;
            if(error<=.035)return Task.FromResult(true);
            int pixels=fineSteering.Next(error,-measured,.0024,false,fineClock,fineRate,165);
            if(pixels!=0)fineSent++;
            if(fineResponse.ObserveGoal(default,-measured,pixels,fineClock,"saved-facing",error,.035))
                throw new TurnUnresponsiveException(default,default,observation:fineResponse.Observation);
            return Task.FromResult(false);
        },()=>{fineResponse.Reset();fineSteering.Reset();},()=>{},(ms,ct)=>
            {ct.ThrowIfCancellationRequested();fineClock+=ms;return Task.CompletedTask;},()=>fineClock,
            ()=>new TurnUnresponsiveException(default,default),default,
            timeoutMilliseconds:FacingRestore.TimeoutMilliseconds(165),failureObserved:fineFailures.Add);
        Require(fineClock>=2000 && fineClock<3091 && fineSent>0 && fineFailures.Count==0,
            "recorded-sized quantized facing could not finish under its strict acceptance and absolute timeout");
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
            bool invalidCapRejected=false;try{FacingRestore.TimeoutMilliseconds(invalidCap);}catch(ArgumentOutOfRangeException){invalidCapRejected=true;}
            Require(invalidCapRejected,"invalid turn cap produced a saved-facing deadline");
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
                deadlineFailure:()=>{deadlineFactories++;return deadlineException;},errorRadians:()=>1);
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

        // Replay a 16ms correction with a slow 46ms scene read. The read must
        // execute with W already up, rather than turning it into the recorded
        // 62ms pulse that crossed the anchor by about .78 map units.
        long pulseClock=0;bool forwardHeld=false;int fullPulseChecks=0,pulseEnds=0;
        long measuredPulseHeld=await MovementPulseTiming.RunAsync(16,()=>{pulseClock+=100;fullPulseChecks++;forwardHeld=true;},
            ()=>{forwardHeld=false;pulseEnds++;},()=>Require(forwardHeld,"pulse safety ran after its input owner ended"),
            ()=>{Require(!forwardHeld,"slow postflight ran while W remained held");pulseClock+=46;fullPulseChecks++;},
            (ms,ct)=>{ct.ThrowIfCancellationRequested();pulseClock+=ms;return Task.CompletedTask;},()=>pulseClock,default);
        Require(measuredPulseHeld==16 && pulseClock==162 && fullPulseChecks==2 && pulseEnds==1 && !forwardHeld,
            "slow full preflight extended the movement pulse or measured pre/post reads as held time");
        foreach(bool focusFailure in new[]{false,true})
        {
            pulseClock=0;forwardHeld=false;int postChecks=0;bool ended=false,interrupted=false;
            using var pulseCancellation=new CancellationTokenSource();
            try
            {
                await MovementPulseTiming.RunAsync(16,()=>forwardHeld=true,()=>{forwardHeld=false;ended=true;},
                    ()=>{if(focusFailure)throw new OperationCanceledException("Game lost focus.");},()=>postChecks++,
                    (ms,ct)=>{pulseClock+=ms;pulseCancellation.Cancel();ct.ThrowIfCancellationRequested();return Task.CompletedTask;},
                    ()=>pulseClock,pulseCancellation.Token);
            }
            catch(OperationCanceledException){interrupted=true;}
            Require(interrupted && ended && !forwardHeld && postChecks==0,
                "short movement pulse swallowed stop/focus or left W held on cancellation");
        }
        foreach(int invalidPulse in new[]{0,15,61,int.MaxValue})
        {
            bool rejectedPulse=false;
            try {await MovementPulseTiming.RunAsync(invalidPulse,()=>throw new Exception("Invalid pulse emitted input"),
                ()=>{},()=>{},()=>{},(ms,ct)=>Task.CompletedTask,()=>0,default);}
            catch(ArgumentOutOfRangeException){rejectedPulse=true;}
            Require(rejectedPulse,"movement pulse admitted duration outside its short correction bounds");
        }
        await MovementPulseTimingChecks.Run();
        await Input.CheckForwardPulseInput();
        var completePulse=new ArrivalMotion();double priorPulseSpeed=completePulse.Speed;
        completePulse.ObservePulse(.7814072341773657,62);
        Require(completePulse.Speed>=priorPulseSpeed && completePulse.Speed<=priorPulseSpeed*1.0625,
            "recorded total displacement was ignored or changed arrival speed without filtering");
        var delayedPulse=new ArrivalMotion();delayedPulse.ObservePulse(.26012016453944425,32);
        Require(delayedPulse.Speed<priorPulseSpeed,
            "all-after-release recorded motion was ignored instead of training the complete pulse");

        // Measured feedback at the observed reader cadence must use elapsed
        // time without exceeding the rate/pixel/remaining-angle bounds.
        foreach(int cadence in new[]{80,120})
        foreach(double sensitivity in new[]{-.004,.004})
        {
            var slowReaderSteering=new SmoothSteering();var rate=new TurnRateBudget();
            double readerRemaining=Math.PI,heading=0;long at=0;
            for(;at<3091 && readerRemaining>.035;at+=cadence)
            {
                int pixels=slowReaderSteering.Next(readerRemaining,heading,sensitivity,false,at,rate,165);
                double correction=pixels*sensitivity;
                Require(correction>=0 && correction<=.301 && Math.Abs(pixels)<=112,
                    "slow-reader steering exceeded packet bounds or corrected away from the goal");
                readerRemaining-=correction;heading-=correction;
                Require(readerRemaining>=-.035,"slow-reader steering overshot its remaining-angle reservation");
            }
            Require(readerRemaining<=.035 && at<=3091,"responsive 165-degree cap could not finish a half-turn at the recorded reader cadence");
        }

        // A low cap with slow but freshly observed feedback takes longer than
        // the nominal allowance. Keep the original watchdog and grant time
        // only when measured error beats the previous best by a useful amount.
        facingClock=0;var slowCapSteering=new SmoothSteering();var slowCapRate=new TurnRateBudget();
        var slowCapResponse=new TurnResponse();double measuredRemaining=Math.PI,measuredHeading=0;
        facingFailures.Clear();
        await FacingRestore.RunAsync(_=>
        {
            facingClock+=100;
            if(measuredRemaining<=.035)return Task.FromResult(true);
            int pixels=slowCapSteering.Next(measuredRemaining,measuredHeading,.004,false,facingClock,slowCapRate,30);
            if(slowCapResponse.Observe(default,measuredHeading,pixels,facingClock,true))
                throw new TurnUnresponsiveException(default,default);
            double correction=pixels*.004;measuredHeading-=correction;measuredRemaining-=correction;
            return Task.FromResult(false);
        },slowCapResponse.Reset,()=>{},FacingDelay,()=>facingClock,()=>new TurnUnresponsiveException(default,default),
            default,timeoutMilliseconds:FacingRestore.TimeoutMilliseconds(30),failureObserved:facingFailures.Add,
            errorRadians:()=>measuredRemaining);
        Require(facingClock>8000 && facingClock<=20000 && facingFailures.Count==0 && measuredRemaining<=.035,
            "fresh low-cap progress could not complete without bypassing measured direction or the watchdog");
        foreach(bool oscillating in new[]{false,true})
        {
            facingClock=0;facingFailures.Clear();int polls=0;double measuredError=1;
            try
            {
                await FacingRestore.RunAsync(_=>
                {
                    facingClock+=100;polls++;
                    measuredError=oscillating?(polls%2==0?1:.98):1-(polls%2==0?.004:0);
                    return Task.FromResult(false);
                },()=>{},()=>{},FacingDelay,()=>facingClock,()=>new TurnUnresponsiveException(default,default),default,
                    timeoutMilliseconds:8000,failureObserved:facingFailures.Add,errorRadians:()=>measuredError);
            }
            catch(TurnUnresponsiveException){ }
            Require(facingFailures.Count==1 && facingFailures[0].Reason=="Deadline" && facingClock<=8300 &&
                facingFailures[0].ProgressExtensions==(oscillating?1:0),
                "unchanged/noisy/oscillating error repeatedly renewed the facing deadline");
        }
        facingClock=0;facingFailures.Clear();double slowlyMeasuredError=1;int progressResets=0;
        try
        {
            await FacingRestore.RunAsync(_=>
            {
                facingClock+=100;slowlyMeasuredError-=.002;
                return Task.FromResult(false);
            },()=>progressResets++,()=>{},FacingDelay,()=>facingClock,
                ()=>new TurnUnresponsiveException(default,default),default,3,timeoutMilliseconds:8000,
                failureObserved:facingFailures.Add,errorRadians:()=>slowlyMeasuredError);
        }
        catch(TurnUnresponsiveException){ }
        Require(facingFailures.Count==1 && facingFailures[0] is {Reason:"Deadline",ProgressExtensions:>0} &&
            facingClock>=20000 && facingClock<=20120 && progressResets==1,
            "fresh progress exceeded the hard total bound or restarted its bound on retry");
        foreach(double missingError in new[]{double.NaN,double.PositiveInfinity})
        {
            bool invalidFacingError=false;int erroneousTurns=0;
            try {await FacingRestore.RunAsync(_=>{erroneousTurns++;return Task.FromResult(false);},()=>{},()=>{},
                FacingDelay,()=>0,()=>new TurnUnresponsiveException(default,default),default,errorRadians:()=>missingError);}
            catch(InvalidOperationException){invalidFacingError=true;}
            Require(invalidFacingError && erroneousTurns==0,"unavailable measured direction admitted facing input or progress");
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

        // Arrived characters must finish before another borrowed farm-defense
        // slice. The old ordering renewed defense forever at the endpoint.
        clock=0;stops=0;pos=new(.10,0);int defenses=0;faces=0;
        bool defendedReturn=await AnchorArrival.ReturnAsync(()=>pos,default,.15,
            _=>throw new Exception("Defense moved a settled character"),()=>stops++,
            _=>{faces++;return Task.CompletedTask;},Delay,()=>clock,default,
            _=>{defenses++;clock+=1000;return Task.FromResult(true);});
        Require(defendedReturn && defenses==0 && faces==1 && clock==240,
            "a continuous farm spawn postponed confirmed endpoint arrival");
        clock=0;stops=0;faces=0;moves=0;defenses=0;pos=new(1,0);
        bool progressedUnderDefense=await AnchorArrival.ReturnAsync(()=>pos,default,.15,
            _=>{moves++;pos-=new Vec(.2,0);clock+=50;return Task.CompletedTask;},()=>stops++,
            _=>{faces++;return Task.CompletedTask;},Delay,()=>clock,default,
            _=>{defenses++;clock+=1000;return Task.FromResult(true);});
        Require(progressedUnderDefense && defenses==1 && moves==5 && faces==1 && pos.Length<=.15,
            "continuous nearby targets starved the movement opportunity or bypassed settled arrival");
        clock=0;stops=0;faces=0;moves=0;pos=new(1,0);
        bool endlessDefense=await AnchorArrival.ReturnAsync(()=>pos,default,.15,
            _=>{moves++;return Task.CompletedTask;},()=>stops++,
            _=>{faces++;return Task.CompletedTask;},Delay,()=>clock,default,
            _=>{clock+=1000;return Task.FromResult(true);});
        Require(!endlessDefense && clock>=15000 && clock<=121020 && faces==0 && stops>0 && moves>0,
            "defense extension lost its hard bound, starved approach or claimed unconfirmed arrival");
        clock=0;stops=0;pos=new(1,0);bool defenseCancelled=false;
        try {await AnchorArrival.ReturnAsync(()=>pos,default,.15,_=>Task.CompletedTask,()=>stops++,
            _=>Task.CompletedTask,Delay,()=>clock,default,_=>throw new OperationCanceledException("Defense stopped"));}
        catch(OperationCanceledException){defenseCancelled=true;}
        Require(defenseCancelled && stops>0,"cancelled defense retained movement ownership");

        // A route retry shares the completed engagement's correction window.
        // Recreating cadence here would attack again before a supported 30°/s
        // half-turn (six seconds plus measured feedback) can reach the anchor.
        var sharedReturnCadence=new AnchorReturnDefenseCadence();
        clock=0;stops=faces=moves=defenses=0;pos=new(.868,0);
        bool sharedReturnFailed=false;long sharedDefenseEndedAt=-1;
        Task<bool> SharedReturnDefense(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();defenses++;
            Require(defenses==1,"a fresh engagement stole the previous slow-turn correction window on retry");
            clock+=1000;sharedDefenseEndedAt=clock;return Task.FromResult(true);
        }
        Task SharedReturnDelay(int milliseconds,CancellationToken token)
        {
            Require(milliseconds is 20 or 120,"anchor return exceeded its bounded poll/settle intervals");
            return Delay(milliseconds,token);
        }
        try
        {
            await AnchorArrival.ReturnAsync(()=>pos,default,.5,
                _=>{moves++;throw new AnchorReturnException("Synthetic arrival correction needs a retry");},
                ()=>stops++,_=>{faces++;return Task.CompletedTask;},SharedReturnDelay,()=>clock,default,
                SharedReturnDefense,defenseSchedule:sharedReturnCadence,turnSpeedDegreesPerSecond:30);
        }
        catch(AnchorReturnException){sharedReturnFailed=true;}
        Require(sharedReturnFailed && defenses==1 && moves==1 && faces==0 && stops>=2 &&
            sharedDefenseEndedAt==1000 && clock==1020 && pos.Length>.5,
            "the interrupted correction lost its completed defense clock, ownership, or pending physical arrival");

        long sharedRetryStartedAt=clock;int retryApproaches=0;
        bool sharedRetryArrived=await AnchorArrival.ReturnAsync(()=>pos,default,.5,
            token=>
            {
                token.ThrowIfCancellationRequested();retryApproaches++;moves++;
                Require(clock<sharedDefenseEndedAt+8000 && defenses==1,
                    "a retry restarted defense or exceeded the prior slow-turn correction opportunity");
                clock+=500; // Synthetic measured 30°/s turn feedback, without hardware input.
                if(clock-sharedRetryStartedAt>=6000)pos=new(.1,0);
                return Task.CompletedTask;
            },()=>stops++,token=>{token.ThrowIfCancellationRequested();faces++;return Task.CompletedTask;},
            SharedReturnDelay,()=>clock,default,SharedReturnDefense,
            defenseSchedule:sharedReturnCadence,turnSpeedDegreesPerSecond:30);
        Require(sharedRetryArrived && defenses==1 && retryApproaches==12 && moves==13 && faces==1 &&
            clock==7500 && clock-sharedRetryStartedAt>=6000 && clock<sharedDefenseEndedAt+8000 &&
            clock-sharedRetryStartedAt<15000 && pos.Length<=.5,
            "shared retry could not complete its slow half-turn, settle, restore facing, and confirm actual arrival");

        clock=sharedDefenseEndedAt+7999;
        Require(!sharedReturnCadence.TryBegin(clock),
            "separate return invocations shortened the original eight-second correction deadline");
        clock=sharedDefenseEndedAt+8000;
        long sharedSettledStartedAt=clock;
        bool sharedSettledReturn=await AnchorArrival.ReturnAsync(()=>pos,default,.5,
            _=>throw new Exception("An arrived retry must not restart movement"),()=>stops++,
            _=>{faces++;return Task.CompletedTask;},SharedReturnDelay,()=>clock,default,
            _=>throw new Exception("An arrived retry must bypass an eligible defense opportunity"),
            defenseSchedule:sharedReturnCadence,turnSpeedDegreesPerSecond:30);
        Require(sharedSettledReturn && defenses==1 && faces==2 && clock==9240 && clock-sharedSettledStartedAt==240,
            "a settled arrival failed to bypass shared defense or its two bounded settlement observations");
        Require(sharedReturnCadence.TryBegin(clock),
            "settled arrival consumed or renewed the expired shared defense opportunity");
        sharedReturnCadence.Complete(clock,30);

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
                "small saved-facing controller goal reaches strict tolerance with quantized measured progress",
                "slow-cap progressive saved facing and delayed feedback","speed-derived bounded facing deadline",
                "saved-facing deadline distinguished from real no-progress watchdog","extended facing cancellation and focus guards",
                "movement key-up precedes slow postflight","short pulse focus/cancellation/duration gates",
                "UI-blocked owner cannot delay worker key-up","serialized nested/overlapping pulse rejection",
                "paired injected pulse packets and release races","bounded timestamp-preserving pulse trace",
                "recorded complete delayed pulse displacement","measured slow-reader turn cadence bounded convergence",
                "freshly reduced low-cap facing error extends soft allowance","stale/noisy/oscillating facing cannot renew deadline",
                "progress-aware facing hard total bound and unavailable-error guard",
                "recorded sub-frame steps excluded from speed training","frame-sized pulse and filtered speed gain",
                "frame-quantized precise arrival","recorded post-facing drift rejected","settle then face then recheck",
                "blocked/cancelled/focus-lost return releases movement","anchor arrival precedes defense; bounded engagements leave approach time",
                "shared return retries retain eight-second slow-turn correction opportunity",
                "slow retry settles actual anchor before new defense; exact correction clock boundaries",
                "persistent damage quiet period","stationary defense family/range/protection gates"}
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
