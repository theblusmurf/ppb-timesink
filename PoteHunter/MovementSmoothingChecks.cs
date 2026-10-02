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
        foreach(int latency in new[]{20,60,100})
        {
            var steering=new SmoothSteering();var pending=new Queue<(long At,double Turn)>();
            double error=initial,heading=0;long now;
            for(now=0;now<4000 && (Math.Abs(error)>.035 || pending.Count>0);now+=20)
            {
                while(pending.TryPeek(out var p) && p.At<=now) {pending.Dequeue();error-=p.Turn;heading+=p.Turn;}
                int pixels=steering.Next(error,heading,sensitivity,false,now);
                Require(Math.Abs(pixels)<=112,"turn exceeded the existing pixel bound");
                if(pixels!=0)
                {
                    Require(Math.Sign(pixels*sensitivity)==Math.Sign(error),"turn corrected away from the target");
                    Require(Math.Abs(pixels*sensitivity)<=.251,"turn failed the elapsed-time angular cap");
                    pending.Enqueue((now+latency,pixels*sensitivity));
                }
            }
            Require(Math.Abs(error)<=.035 && now<4000,"delayed heading did not converge for either sensitivity sign");
        }
        var held=new SmoothSteering();
        Require(held.Next(1,0,.004,false,0)!=0 && held.Next(1,0,.004,false,20)==0 && held.Next(1,0,.004,false,60)==0,
            "new turns stacked before the client reported the previous heading");
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
            Passed=true,HardwareInputEmitted=false,
            Checks=new[]{"delayed heading response and both calibration signs","time-bounded turn size","unresponsive turn deadline",
                "unsent turns cannot start stall timing","new facing goal clears idle timer","bounded post-loot facing retry and cancellation",
                "recorded sub-frame steps excluded from speed training","frame-sized pulse and filtered speed gain",
                "frame-quantized precise arrival","recorded post-facing drift rejected","settle then face then recheck",
                "blocked/cancelled/focus-lost return releases movement","persistent damage quiet period","stationary defense family/range/protection gates"}
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
