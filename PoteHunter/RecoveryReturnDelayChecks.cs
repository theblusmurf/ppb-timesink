using System.Text.Json;

namespace PoteHunter;

internal static class RecoveryReturnDelayChecks
{
    static void Require(bool condition,string message)
    {if(!condition)throw new Exception("Recovery return delay: "+message);}

    sealed class Fixture
    {
        internal readonly DeathRecoveryState State=new();
        internal long Now=1000;
        internal int Released,Checks,Waited,Slices;
        internal Action? OnValidate,OnDelay;
        internal Fixture(bool repaired=true)
        {
            State.Observe(new(0,100),0);State.Observe(new(100,100),100);
            State.MarkPostRevivalPrepared(State.Episode);
            if(repaired)State.MarkRepairCompleted(State.Episode);
        }
        internal Task Run(bool enabled=true,int seconds=2,CancellationToken token=default)=>
            RecoveryReturnDelay.Run(State,State.Episode,enabled,seconds,()=>Now,
                ()=>{Checks++;OnValidate?.Invoke();},()=>Released++,
                (milliseconds,ct)=>
                {
                    ct.ThrowIfCancellationRequested();Require(milliseconds is >0 and <=100,"wait slice exceeded its bound");
                    Now+=milliseconds;Waited+=milliseconds;Slices++;OnDelay?.Invoke();return Task.CompletedTask;
                },remaining=>Require(remaining>0,"countdown displayed a completed wait"),token);
    }

    static async Task Rejected<T>(Func<Task> run,string message) where T:Exception
    {
        bool failed=false;try{await run();}catch(T){failed=true;}Require(failed,message);
    }

    internal static async Task Run()
    {
        var beforeRepair=new Fixture(false);
        await Rejected<DeathRecoveryRequiredException>(()=>beforeRepair.Run(),"delay started before repair/skip-repair completion");
        Require(beforeRepair.Waited==0 && beforeRepair.Released==2 && !beforeRepair.State.ReturnDelayCompleted,"rejected early wait changed state/input ownership");
        var state=beforeRepair.State;
        Require(!state.MarkReturnDelayCompleted(state.Episode),"unstarted delay was marked complete");

        foreach(var configuration in new[]{(Enabled:false,Seconds:10),(Enabled:true,Seconds:0),(Enabled:true,Seconds:-10)})
        {
            var disabled=new Fixture();await disabled.Run(configuration.Enabled,configuration.Seconds);
            Require(disabled.Waited==0 && disabled.State.ReturnDelayCompleted && disabled.Released==2,"disabled/zero delay waited or was not retained");
        }
        var full=new Fixture();await full.Run();long deadline=full.State.ReturnDelayReadyAt!.Value;
        Require(full.Waited==2000 && full.Slices==20 && full.State.ReturnDelayCompleted && full.Released==2,"configured time was not fully waited with released input");
        full.Now+=500;full.State.Observe(default,full.Now);await full.Run(seconds:99);
        Require(full.Waited==2000 && full.State.ReturnDelayReadyAt==deadline,"completed route retry/read gap repeated or extended the wait");
        long completedEpisode=full.State.Episode;full.State.Observe(new(0,100),full.Now);
        Require(!full.State.ReturnDelayCompleted && full.State.ReturnDelayReadyAt==null && !full.State.MarkReturnDelayCompleted(completedEpisode),
            "a new death retained a completed earlier wait");

        var bounded=new Fixture();await bounded.Run(seconds:999);
        Require(bounded.Waited==600000 && bounded.State.ReturnDelayCompleted,"upper seconds bound was not enforced");
        var interrupted=new Fixture();bool failedOnce=false;
        interrupted.OnDelay=()=>{if(!failedOnce){failedOnce=true;throw new InvalidOperationException("health reading unavailable");}};
        await Rejected<InvalidOperationException>(()=>interrupted.Run(),"unknown health exception was swallowed");
        long originalDeadline=interrupted.State.ReturnDelayReadyAt!.Value;
        Require(!interrupted.State.ReturnDelayCompleted && interrupted.Released==2,"interrupted wait marked complete or failed to release input");
        interrupted.OnDelay=null;interrupted.Now+=500;await interrupted.Run(seconds:500);
        Require(interrupted.State.ReturnDelayReadyAt==originalDeadline && interrupted.Now==originalDeadline && interrupted.Waited==1500,
            "retry restarted the delay instead of retaining the original episode deadline");

        foreach(string reason in new[]{"F9", "focus lost", "character changed", "window changed", "health unavailable"})
        {
            var stopped=new Fixture();stopped.OnValidate=()=>throw new OperationCanceledException(reason);
            await Rejected<OperationCanceledException>(()=>stopped.Run(),"safety interruption was swallowed: "+reason);
            Require(stopped.Waited==0 && stopped.Released==2 && !stopped.State.ReturnDelayCompleted,"safety stop completed delay or retained input: "+reason);
        }
        using(var cancellation=new CancellationTokenSource())
        {
            var cancelled=new Fixture();cancelled.OnDelay=()=>cancellation.Cancel();
            await Rejected<OperationCanceledException>(()=>cancelled.Run(token:cancellation.Token),"token cancellation was ignored inside wait");
            Require(cancelled.Waited==100 && cancelled.Released==2 && !cancelled.State.ReturnDelayCompleted,"cancellation completed delay or omitted release");
        }
        var redied=new Fixture();long oldEpisode=redied.State.Episode;
        redied.OnDelay=()=>redied.State.Observe(new(0,100),redied.Now);
        await Rejected<DeathRecoveryRequiredException>(()=>redied.Run(),"new death continued the old wait");
        Require(redied.State.Episode!=oldEpisode && !redied.State.ReturnDelayCompleted && redied.State.ReturnDelayReadyAt==null &&
            !redied.State.MarkReturnDelayCompleted(oldEpisode) && redied.Released==2,"new death retained or completed the old wait");
        redied.OnDelay=null;redied.State.Observe(new(100,100),redied.Now);redied.State.MarkPostRevivalPrepared(redied.State.Episode);
        redied.State.MarkRepairCompleted(redied.State.Episode);int waitedBefore=redied.Waited;await redied.Run();
        Require(redied.Waited-waitedBefore==2000,"new death did not receive its own complete wait");
        redied.State.Reset();Require(!redied.State.ReturnDelayCompleted && redied.State.ReturnDelayReadyAt==null,"reset retained delay state");

        var backwards=new Fixture();backwards.OnDelay=()=>backwards.Now-=200;
        await Rejected<InvalidOperationException>(()=>backwards.Run(),"backwards clock extended an active wait");
        Require(backwards.Released==2 && !backwards.State.ReturnDelayCompleted,"clock failure completed wait or retained input");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"recovery-return-delay-checks.json"),JsonSerializer.Serialize(new
        {
            Passed=true,HardwareInputEmitted=false,GameInputUsed=false,SyntheticClock=true,
            Checks=new[]{"after repair/skip-repair only","disabled/zero immediate completion","configured full wait and bounded slices",
                "six hundred second maximum","one deadline per episode across retries/read gaps","completed wait retained across route retries",
                "focus/stop/identity/health exceptions and cancellation release","new death invalidates waiting/completed state",
                "stale asynchronous completion rejected","reset clears delay","backwards clock stops"}
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
