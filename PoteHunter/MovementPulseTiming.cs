namespace PoteHunter;

// The owner performs all full scene checks. Only the bounded key pulse and
// captured cheap safety guard run on the worker. Awaiting the worker joins it
// before postflight; a delayed UI continuation therefore cannot delay key-up.
internal static class MovementPulseTiming
{
    static int ownedPulse;

    internal static async Task<long> RunOwnedAsync(int milliseconds,Action preflight,Action begin,Action end,
        Action safety,Action postflight,Func<int,CancellationToken,Task> delay,Func<long> clock,
        CancellationToken token,Func<Func<Task<long>>,Task<long>>? execute=null)
    {
        ValidateDuration(milliseconds);
        token.ThrowIfCancellationRequested();
        if(Interlocked.CompareExchange(ref ownedPulse,1,0)!=0)
            throw new InvalidOperationException("A short forward pulse already owns input.");
        try
        {
            preflight();
            token.ThrowIfCancellationRequested();
            // Task.Run never inherits the UI SynchronizationContext. Do not
            // pass token to Task.Run: cleanup must run if stop wins startup.
            long held=await (execute ?? (work=>Task.Run(work)))(()=>RunAsync(milliseconds,begin,end,safety,
                ()=>{},delay,clock,token));
            token.ThrowIfCancellationRequested();
            postflight();
            return held;
        }
        finally {Volatile.Write(ref ownedPulse,0);}
    }

    internal static async Task<long> RunAsync(int milliseconds,Action begin,Action end,Action safety,
        Action postflight,Func<int,CancellationToken,Task> delay,Func<long> clock,CancellationToken token)
    {
        ValidateDuration(milliseconds);
        token.ThrowIfCancellationRequested();
        long startedAt=0,heldMilliseconds=0;bool started=false;
        try
        {
            begin();started=true;startedAt=clock();long deadline=startedAt+milliseconds;
            while(clock()<deadline)
            {
                token.ThrowIfCancellationRequested();safety();
                int remaining=(int)Math.Clamp(deadline-clock(),0,milliseconds);
                // Cheap stop/focus checks between short waits. No continuation
                // in this key-held interval may return to the UI context.
                if(remaining>0)await delay(Math.Min(4,remaining),token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();safety();
        }
        finally
        {
            end();
            if(started)heldMilliseconds=Math.Max(0,clock()-startedAt);
        }
        postflight();
        return heldMilliseconds;
    }

    static void ValidateDuration(int milliseconds)
    {
        if(milliseconds is <16 or >60)throw new ArgumentOutOfRangeException(nameof(milliseconds));
    }
}
