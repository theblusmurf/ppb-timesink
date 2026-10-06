using System.Collections.Concurrent;
using System.Diagnostics;

namespace PoteHunter;

internal static class MovementPulseTimingChecks
{
    static void Require(bool passed,string message)
    {if(!passed)throw new InvalidOperationException("Movement pulse ownership: "+message);}

    internal static async Task Run()
    {
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
                Task.Delay,()=>clock.ElapsedMilliseconds,default);
        }
        finally {SynchronizationContext.SetSynchronizationContext(original);}
        Require(released.Wait(TimeSpan.FromSeconds(2)),"blocked owner delayed key-up");
        Require(!delayedOwner.IsCompleted && postflight==0 && ends==1,"key-up waited for owner postflight");
        Require(SpinWait.SpinUntil(()=>owner.HasPending,TimeSpan.FromSeconds(2)),"joined worker did not queue owner postflight");
        owner.Drain();await delayedOwner;
        Require(postflight==1 && ends==1,"owner continuation duplicated release or skipped postflight");

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
