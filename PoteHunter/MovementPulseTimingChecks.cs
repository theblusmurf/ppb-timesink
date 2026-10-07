using System.Collections.Concurrent;
using System.Diagnostics;

namespace PoteHunter;

internal static class MovementPulseTimingChecks
{
    static void Require(bool passed,string message)
    {if(!passed)throw new InvalidOperationException("Movement pulse ownership: "+message);}

    internal static async Task Run()
    {
        CheckDeadlines();

        // Do not pump this owner context until after key-up was observed. A
        // captured UI continuation used to retain W until that pump resumed.
        var original=SynchronizationContext.Current;var owner=new DeferredOwner();
        using var released=new ManualResetEventSlim();int postflight=0,ends=0;
        var clock=Stopwatch.StartNew();Task<long> delayedOwner;
        try
        {
            SynchronizationContext.SetSynchronizationContext(owner);
            delayedOwner=MovementPulseTiming.RunOwnedAsync(16,
                ()=>Require(SynchronizationContext.Current==owner,"preflight left its owner context"),
                ()=>Require(SynchronizationContext.Current==null,"key-down inherited the UI context"),
                ()=>{ends++;released.Set();},()=>{},
                ()=>{Require(SynchronizationContext.Current==owner && released.IsSet,"postflight ran before join or off owner");postflight++;},
                default);
        }
        finally {SynchronizationContext.SetSynchronizationContext(original);}
        Require(released.Wait(TimeSpan.FromSeconds(2)),"blocked owner delayed key-up");
        Require(!delayedOwner.IsCompleted && postflight==0 && ends==1,"key-up waited for owner postflight");
        Require(SpinWait.SpinUntil(()=>owner.HasPending,TimeSpan.FromSeconds(2)),"joined worker did not queue owner postflight");
        owner.Drain();await delayedOwner;
        Require(postflight==1 && ends==1,"owner continuation duplicated release or skipped postflight");

        // A stopped queued worker must never begin input, and must relinquish
        // ownership so the next pulse can be admitted.
        using(var startupCancellation=new CancellationTokenSource())
        {
            int startupBegins=0,startupEnds=0;bool startupStopped=false;
            try
            {
                await MovementPulseTiming.RunOwnedAsync(16,()=>{},()=>startupBegins++,()=>startupEnds++,()=>{},()=>{},
                    startupCancellation.Token,work=>{startupCancellation.Cancel();return Task.Run(work);});
            }
            catch(OperationCanceledException){startupStopped=true;}
            Require(startupStopped && startupBegins==0 && startupEnds==0,"canceled queued worker began or released unowned input");
        }

        // Exercise the conventional timer without relying on a particular
        // machine's clock resolution. Only synthetic callbacks run here.
        using(var fallback=MovementPulseDeadlineWait.Create(default,conventional:true))
        {
            int fallbackEnds=0;
            long fallbackHeld=MovementPulseTiming.RunDeadline(16,()=>{},()=>fallbackEnds++,()=>{},
                fallback.Wait,Stopwatch.GetTimestamp,Stopwatch.Frequency,default);
            Require(!fallback.IsHighResolution && fallbackEnds==1 && fallbackHeld>=16,
                "conventional timer fallback skipped its deadline or cleanup");
        }

        using(var nativeCancellation=new CancellationTokenSource())
        {
            int nativeEnds=0,nativePost=0;bool nativeStopped=false;
            try
            {
                await MovementPulseTiming.RunOwnedAsync(60,()=>{},()=>{},()=>nativeEnds++,
                    ()=>nativeCancellation.Cancel(),()=>nativePost++,nativeCancellation.Token);
            }
            catch(OperationCanceledException){nativeStopped=true;}
            Require(nativeStopped && nativeEnds==1 && nativePost==0,
                "native timer cancellation bypassed release or ran postflight");
        }
        using(var eventOwner=new CancellationTokenSource())
        {
            var cancelEvent=eventOwner.Token.WaitHandle;
            using(var localTimer=MovementPulseDeadlineWait.Create(eventOwner.Token))
                Require(!cancelEvent.SafeWaitHandle.IsClosed,"timer construction closed its owner's cancellation event");
            eventOwner.Cancel();
            Require(!cancelEvent.SafeWaitHandle.IsClosed && cancelEvent.WaitOne(0),
                "timer disposal took ownership of the token's cancellation event");
        }

        // Overlap/nesting must reject without running the new owner's callbacks
        // or ending the already active owner's held input.
        var worker=new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        int firstEnds=0,rejectedCallbacks=0;long fakeClock=0;
        var first=MovementPulseTiming.RunOwnedAsync(16,()=>{},()=>{},()=>firstEnds++,()=>{},()=>{},
            (_,_)=>Task.CompletedTask,()=>fakeClock,default,_=>worker.Task);
        bool overlap=false;
        try {await MovementPulseTiming.RunOwnedAsync(16,()=>rejectedCallbacks++,()=>rejectedCallbacks++,()=>rejectedCallbacks++,
            ()=>{},()=>{},(_,_)=>Task.CompletedTask,()=>0,default);}
        catch(InvalidOperationException){overlap=true;}
        Require(overlap && rejectedCallbacks==0 && firstEnds==0,"overlap changed the active owner's input");
        worker.SetResult(16);await first;
        bool nested=false;fakeClock=0;
        await MovementPulseTiming.RunOwnedAsync(16,()=>
        {
            try {MovementPulseTiming.RunOwnedAsync(16,()=>rejectedCallbacks++,()=>{},()=>{},()=>{},()=>{},
                (_,_)=>Task.CompletedTask,()=>0,default).GetAwaiter().GetResult();}
            catch(InvalidOperationException){nested=true;}
        },()=>{},()=>{},()=>{},()=>{},(ms,_)=>{fakeClock+=ms;return Task.CompletedTask;},()=>fakeClock,default);
        Require(nested && rejectedCallbacks==0,"nested pulse preflight acquired input");

        // Wait failure also joins cleanup and releases the ownership latch.
        fakeClock=0;int failedEnds=0;bool waitFailed=false;
        try
        {
            await MovementPulseTiming.RunOwnedAsync(16,()=>{},()=>{},()=>failedEnds++,()=>{},()=>{},
                (_,_)=>throw new TimeoutException("timer failure"),()=>fakeClock,default);
        }
        catch(TimeoutException){waitFailed=true;}
        Require(waitFailed && failedEnds==1,"failed wait skipped owned cleanup");
        await MovementPulseTiming.RunOwnedAsync(16,()=>{},()=>{},()=>{},()=>{},()=>{},
            (ms,_)=>{fakeClock+=ms;return Task.CompletedTask;},()=>fakeClock,default);

        foreach(bool failBegin in new[]{false,true})
        {
            int cleanup=0,post=0;bool failed=false;
            try {await MovementPulseTiming.RunOwnedAsync(16,()=>{},()=>{if(failBegin)throw new InvalidOperationException("down rejected");},
                ()=>cleanup++,()=>throw new OperationCanceledException("focus changed"),()=>post++,
                Task.Delay,()=>clock.ElapsedMilliseconds,default);}
            catch(Exception e) when(e is InvalidOperationException or OperationCanceledException){failed=true;}
            Require(failed && cleanup==1 && post==0,"failed startup/guard did not release exactly once");
        }

        var trace=new PulseInputTrace();bool nestedTrace=false;
        using(trace.Enter())
        {
            for(int i=0;i<67;i++)Require(PulseInputTrace.TryRecord("synthetic input",new{Index=i}),"worker trace was not buffered");
            try {using var ignored=trace.Enter();}
            catch(InvalidOperationException){nestedTrace=true;}
        }
        var entries=new List<(string Stage,DateTime Time)>();
        trace.Flush((stage,_,time)=>entries.Add((stage,time)));trace.Flush((stage,_,time)=>entries.Add((stage,time)));
        Require(nestedTrace && entries.Count==65 && entries[^1].Stage=="pulse input trace overflow" &&
            entries.Zip(entries.Skip(1),(a,b)=>a.Time<=b.Time).All(v=>v) && !PulseInputTrace.IsActive,
            "pulse trace lost its bound, timestamps, scope or single flush");
    }

    static void CheckDeadlines()
    {
        // The production core uses high-resolution absolute deadlines. Replay
        // slow guards, early wakes, timer oversleep and fractional slices.
        const long frequency=10_000;long now=0;bool held=false;int ends=0,guards=0;
        var waits=new List<long>();
        long measured=MovementPulseTiming.RunDeadline(16,()=>{now=123;held=true;},
            ()=>{held=false;ends++;},()=>{Require(held,"deadline guard ran after release");guards++;now+=3;},
            (ticks,_)=>{waits.Add(ticks);now+=ticks;},()=>now,frequency,default);
        Require(measured==17 && ends==1 && !held && waits.SequenceEqual(new long[]{40,40,40,28}) && guards==5,
            "guard time restarted the deadline or lost its final fractional wait");

        now=0;ends=0;waits.Clear();
        measured=MovementPulseTiming.RunDeadline(16,()=>held=true,()=>{held=false;ends++;},()=>{},
            (ticks,_)=>{waits.Add(ticks);now+=500;},()=>now,frequency,default);
        Require(measured==50 && waits.Count==1 && ends==1 && !held,
            "overslept timer added another wait after the deadline");

        now=0;ends=0;waits.Clear();
        measured=MovementPulseTiming.RunDeadline(16,()=>held=true,()=>{held=false;ends++;},()=>{},
            (ticks,_)=>{waits.Add(ticks);now+=Math.Max(1,ticks/2);},()=>now,frequency,default);
        Require(measured==16 && waits.All(ticks=>ticks>0 && ticks<=40) && waits.Count>4 && ends==1 && !held,
            "early wake released before the deadline or escaped the four-millisecond bound");

        foreach(string failure in new[]{"begin","focus","cancel","wait"})
        {
            now=0;ends=0;int begins=0,waitCount=0;bool failed=false;
            using var cancellation=new CancellationTokenSource();
            try
            {
                MovementPulseTiming.RunDeadline(16,()=>
                {begins++;held=true;if(failure=="begin")throw new InvalidOperationException("rejected down");},
                    ()=>{held=false;ends++;},()=>
                    {if(failure=="focus" && waitCount>0)throw new OperationCanceledException("focus lost");},
                    (ticks,token)=>
                    {
                        waitCount++;now+=ticks;
                        if(failure=="cancel")cancellation.Cancel();
                        token.ThrowIfCancellationRequested();
                        if(failure=="wait")throw new TimeoutException("timer failure");
                    },()=>now,frequency,cancellation.Token);
            }
            catch(Exception e) when(e is OperationCanceledException or InvalidOperationException or TimeoutException){failed=true;}
            Require(failed && begins==1 && ends==1 && !held,
                failure+" did not release the deadline-owned input exactly once");
        }

        // A guard that consumes the remaining interval must release directly.
        now=0;ends=0;int extraWaits=0;
        measured=MovementPulseTiming.RunDeadline(16,()=>{},()=>ends++,()=>now+=160,
            (_,_)=>extraWaits++,()=>now,frequency,default);
        Require(measured==32 && extraWaits==0 && ends==1,"slow guard used a stale remaining duration");
    }

    sealed class DeferredOwner : SynchronizationContext
    {
        readonly ConcurrentQueue<(SendOrPostCallback Callback,object? State)> pending=new();
        internal bool HasPending=>!pending.IsEmpty;
        public override void Post(SendOrPostCallback callback,object? state)=>pending.Enqueue((callback,state));
        internal void Drain()
        {
            var prior=Current;SetSynchronizationContext(this);
            try {while(pending.TryDequeue(out var callback))callback.Callback(callback.State);}
            finally {SetSynchronizationContext(prior);}
        }
    }
}
