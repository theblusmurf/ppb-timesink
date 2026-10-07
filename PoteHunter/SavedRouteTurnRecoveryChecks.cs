using System.Text.Json;

namespace PoteHunter;

internal static class SavedRouteTurnRecoveryChecks
{
    public static async Task Run()
    {
        static void Require(bool condition,string message)
        {if(!condition)throw new Exception("Saved route turn recovery: "+message);}
        static RecoveryPath Path()=>new(new Vec[]{new(10,0),new(20,0)},new(20,0));
        var path=Path();var recovery=new SavedRouteTurnRecovery();
        long clock=0;int releases=0,resets=0,reads=0,started=0,guards=0;
        bool released=false;var order=new List<string>();
        void Release(){releases++;released=true;order.Add("release");}
        void Reset(){Require(released,"reset preceded release");resets++;order.Add("reset");}
        Task Delay(int ms,CancellationToken token)
        {token.ThrowIfCancellationRequested();Require(released,"retry delay held forward/attack");
            clock+=ms;if(ms==0)guards++;order.Add("delay"+ms);return Task.CompletedTask;}
        (Vec Position,double Heading) Read()
        {Require(released,"retry observation preceded release");reads++;order.Add("read");return(new(.5,0),.5);}
        void Started(SavedRouteTurnRetry retry)
        {Require(released && resets==1 && retry.Reason=="RetryStarted","diagnostic preceded release/reset");started++;order.Add("start");}
        recovery.ObserveWaypoint(path,clock);clock=1500;
        var retry=await recovery.RetryAsync(path,Release,Reset,Delay,Read,()=>clock,default,Started);
        Require(retry.Ready && retry.ElapsedMilliseconds==1620 && retry.Position==new Vec(.5,0) &&
            retry.ActualHeading==.5 && releases==1 && resets==1 && reads==1 && started==1 && guards==1 &&
            string.Join(",",order)=="release,reset,start,delay120,read,delay0",
            "first measured turn fault did not use one released, freshly guarded same-waypoint retry");
        // Position progress and its changing aim delta remain the same owner.
        // They must neither refill the retry nor restart its fixed allowance.
        foreach(double x in new[]{.5,2.0,5.0})
        {
            clock+=100;
            Require(path.Next(new(x,0))==new Vec(10,0) && path.Index==0,"fixture unexpectedly advanced checkpoint");
            recovery.ObserveWaypoint(path,clock);
        }
        released=false;
        retry=await recovery.RetryAsync(path,Release,Reset,Delay,Read,()=>clock,default);
        Require(!retry.Ready && retry.Reason=="AlreadyRetried" && releases==2 && resets==1 && reads==1,
            "same-waypoint progress, moving aim or a second fault refilled retries or read/sent after rejection");
        Require(path.Next(new(10,0))==new Vec(20,0) && path.Index==1,"next-checkpoint fixture failed");
        recovery.ObserveWaypoint(path,clock);released=false;
        retry=await recovery.RetryAsync(path,Release,Reset,Delay,Read,()=>clock,default);
        Require(retry.Ready && retry.WaypointIndex==1 && retry.ElapsedMilliseconds==120 && resets==2,
            "a confirmed checkpoint advance failed to grant its own single retry");

        foreach(long faultAt in new[]{19999L,20000L,21000L})
        {
            path=Path();recovery=new();clock=0;recovery.ObserveWaypoint(path,clock);clock=faultAt;
            releases=resets=reads=0;released=false;
            retry=await recovery.RetryAsync(path,Release,Reset,Delay,Read,()=>clock,default);
            Require(!retry.Ready && retry.Reason=="WaypointDeadline" && releases==1 && reads==0 &&
                resets==(faultAt<20000?1:0),"expired initial/released-delay allowance admitted retry or renewed deadline");
        }
        path=Path();recovery=new();clock=0;recovery.ObserveWaypoint(path,clock);clock=19879;
        retry=await recovery.RetryAsync(path,Release,Reset,Delay,Read,()=>clock,default);
        Require(retry.Ready && retry.ElapsedMilliseconds==19999,"last in-budget fresh observation was rejected");
        path=Path();recovery=new();clock=0;recovery.ObserveWaypoint(path,clock);clock=19800;reads=0;
        (Vec Position,double Heading) LateRead(){reads++;clock+=81;return(new(.5,0),.5);}
        retry=await recovery.RetryAsync(path,Release,Reset,Delay,LateRead,()=>clock,default);
        Require(!retry.Ready && retry.Reason=="WaypointDeadline" && retry.ElapsedMilliseconds==20001 && reads==1,
            "synchronous fresh read latency renewed the fixed waypoint allowance");

        // Exercise safety failures through the exact runner with default tokens;
        // cancellation alone must not be confused with all safety admission.
        foreach(var safety in new Exception[]{new OperationCanceledException("Game lost focus."),
            new OperationCanceledException("F9 stop."),new InvalidOperationException("Game window identity changed."),
            new DeathRecoveryRequiredException()})
        foreach(bool afterRead in new[]{false,true})
        {
            path=Path();recovery=new();clock=0;recovery.ObserveWaypoint(path,clock);clock=1500;
            releases=resets=reads=0;released=false;bool sawSame=false;
            Task UnsafeDelay(int ms,CancellationToken token)
            {Require(released,"safety check preceded release");clock+=ms;if(afterRead?ms==0:ms==120)throw safety;
                return Task.CompletedTask;}
            try{await recovery.RetryAsync(path,Release,Reset,UnsafeDelay,Read,()=>clock,default);}
            catch(Exception failure){sawSame=ReferenceEquals(failure,safety);}
            Require(sawSame && releases==1 && resets==1 && reads==(afterRead?1:0),
                "focus/F9/death/identity failure was swallowed, replaced or admitted retry");
            retry=await recovery.RetryAsync(path,Release,Reset,Delay,Read,()=>clock,default);
            Require(!retry.Ready && retry.Reason=="AlreadyRetried","safety failure rearmed the used retry");
        }
        foreach(bool cancelDuringRead in new[]{false,true})
        {
            path=Path();recovery=new();clock=0;recovery.ObserveWaypoint(path,clock);clock=1500;
            releases=resets=reads=guards=0;released=false;bool cancelled=false;
            using var cancellation=new CancellationTokenSource();
            if(!cancelDuringRead)cancellation.Cancel();
            (Vec Position,double Heading) CancelRead()
            {reads++;cancellation.Cancel();return(new(.5,0),.5);}
            try{await recovery.RetryAsync(path,Release,Reset,Delay,CancelRead,()=>clock,cancellation.Token);}
            catch(OperationCanceledException){cancelled=true;}
            Require(cancelled && releases==1 && resets==(cancelDuringRead?1:0) &&
                reads==(cancelDuringRead?1:0) && guards==0,"cancelled retry failed to release or performed post-cancel admission");
        }
        path=Path();recovery=new();clock=0;recovery.ObserveWaypoint(path,clock);clock=1500;
        bool unknownRejected=false;
        try{await recovery.RetryAsync(path,Release,Reset,Delay,()=> (new Vec(double.NaN,0),.5),()=>clock,default);}
        catch(InvalidOperationException){unknownRejected=true;}
        Require(unknownRejected,"unknown fresh geometry was marked ready");
        var differentPath=Path();
        retry=await recovery.RetryAsync(differentPath,Release,Reset,Delay,Read,()=>clock,default);
        Require(!retry.Ready && retry.Reason=="WaypointChanged","unobserved replacement path inherited permission");
        recovery.ObserveWaypoint(differentPath,clock);
        retry=await recovery.RetryAsync(differentPath,Release,Reset,Delay,Read,()=>clock,default);
        Require(retry.Ready,"explicit newly planned path failed to receive bounded ownership");

        File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"saved-route-turn-recovery-checks.json"),
            JsonSerializer.Serialize(new{Passed=true,HardwareInputEmitted=false,MaximumRetriesPerWaypoint=1,
                FixedWaypointAllowanceMilliseconds=SavedRouteTurnRecovery.WaypointAllowanceMilliseconds,
                ReleasedSettleMilliseconds=SavedRouteTurnRecovery.SettleMilliseconds,
                ProgressAndMovingAimDoNotRefill=true,FreshPostReadSafetyRequired=true,
                ReadLatencyConsumesOriginalDeadline=true,FocusStopDeathIdentityAndCancellationPropagate=true,
                SharedStartupDeathReturnAndFallbackIntegration=true}));
    }
}
