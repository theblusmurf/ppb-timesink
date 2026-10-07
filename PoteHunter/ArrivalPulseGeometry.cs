namespace PoteHunter;

internal static class ArrivalPulseGeometry
{
    internal static int FrameDuration(int milliseconds)
    {
        if(milliseconds is <16 or >60)throw new ArgumentOutOfRangeException(nameof(milliseconds));
        // Intermediate sub-frame holds rarely add useful precision but can
        // cross a client update and add another complete physical step.
        return milliseconds==60?60:milliseconds/16*16;
    }

    internal static bool PathClear(Vec position,Vec forward,double displacement,Func<Vec,Vec,bool>? canAdvance)=>
        position.Finite && forward.Finite && Math.Abs(forward.Length-1)<=1e-6 &&
        double.IsFinite(displacement) && displacement>0 &&
        canAdvance?.Invoke(position,position+forward*displacement)!=false;

    internal static bool ForwardObservation(Vec before,Vec after,Vec forward)
    {
        if(!before.Finite || !after.Finite || !forward.Finite || Math.Abs(forward.Length-1)>1e-6)return false;
        Vec displacement=after-before;
        double length=displacement.Length;
        double along=displacement.X*forward.X+displacement.Y*forward.Y;
        double across=Math.Abs(displacement.X*forward.Y-displacement.Y*forward.X);
        return length>.01 && length<3 && along>0 && across<=Math.Max(.025,length*.15);
    }
}

internal readonly record struct ArrivalPulseObservation(Vec Position,bool Settled,long ElapsedMilliseconds,
    long PositionReadElapsedMilliseconds,long QuietMilliseconds,double QuietDisplacement,int PositionReads,int GraceReads,
    // MaximumReadMilliseconds covers sampled reads; total elapsed also includes
    // the initial released-position read and its post-read safety validation.
    long MaximumReadMilliseconds,long MaximumGuardOverheadMilliseconds,string Status)
{
    internal bool GraceUsed=>ElapsedMilliseconds>ArrivalPulseSettling.NormalMilliseconds;
}

// Observes released motion only. The caller retains input ownership and uses
// its ordinary guarded delay, so health/focus/identity/cancellation still run.
internal static class ArrivalPulseSettling
{
    internal const int InitialMilliseconds=120,QuietMilliseconds=80,SampleMilliseconds=40,
        NoMovementMilliseconds=200,NormalMilliseconds=320,MaximumMilliseconds=1000;
    internal const double QuietDistance=.025;

    internal static async Task<ArrivalPulseObservation> ObserveAsync(Vec before,Vec released,
        Func<Vec> position,Func<int,CancellationToken,Task> delay,Func<long> clock,CancellationToken token,long? startedAt=null)
    {
        if(!before.Finite || !released.Finite)throw new InvalidOperationException("Forward correction position is unavailable.");
        long enteredAt=clock(),began=startedAt??enteredAt;
        if(began<0 || began>enteredAt)throw new ArgumentOutOfRangeException(nameof(startedAt));
        long deadline=began+MaximumMilliseconds,normalDeadline=began+NormalMilliseconds,quietAt=enteredAt;
        long readAt=enteredAt,validatedAt=enteredAt,maxRead=0,maxGuard=0,quietElapsed=0;
        int reads=0,graceReads=0;
        double quietDisplacement=0;
        Vec quietOrigin=released,current=released;
        // Initial read/guard latency consumes the same hard budget. It cannot
        // be counted as a quiet position interval before that read completed.
        int nextWait=(int)Math.Max(0,InitialMilliseconds-(enteredAt-began));
        ArrivalPulseObservation Result(bool settled,string status)=>new(current,settled,Math.Max(0,validatedAt-began),
            Math.Max(0,readAt-began),quietElapsed,quietDisplacement,reads,graceReads,maxRead,maxGuard,status);
        while(true)
        {
            token.ThrowIfCancellationRequested();
            validatedAt=clock();long remaining=deadline-validatedAt;
            if(remaining<=0)return Result(false,"HardDeadline");
            int wait=(int)Math.Min(nextWait,remaining);long delayAt=validatedAt;
            await delay(wait,token);
            token.ThrowIfCancellationRequested();
            long readStartedAt=clock();
            maxGuard=Math.Max(maxGuard,Math.Max(0,readStartedAt-delayAt-wait));
            validatedAt=readStartedAt;
            if(readStartedAt>deadline)return Result(false,"GuardDeadline");
            current=position();
            token.ThrowIfCancellationRequested();
            readAt=clock();reads++;
            if(readAt<readStartedAt)throw new InvalidOperationException("Forward correction observations arrived out of order.");
            maxRead=Math.Max(maxRead,readAt-readStartedAt);
            if(readAt>normalDeadline)graceReads++;
            if(!current.Finite)throw new InvalidOperationException("Forward correction position is unavailable.");
            // Compare to the start of the quiet window: several individually
            // small changes must not masquerade as a settled observation.
            // Keep this actual read time separate from a later synchronous
            // guard: guard latency is not another quiet position observation.
            quietDisplacement=(current-quietOrigin).Length;
            if(quietDisplacement>QuietDistance){quietOrigin=current;quietAt=readAt;}
            quietElapsed=Math.Max(0,readAt-quietAt);
            // A focus/health/identity/stop change during the read must be checked
            // before settling can authorize the next operation. The caller's
            // ordinary guarded delay is used even for a zero-length wait.
            await delay(0,token);
            token.ThrowIfCancellationRequested();
            validatedAt=clock();
            if(validatedAt<readAt)throw new InvalidOperationException("Forward correction observations arrived out of order.");
            maxGuard=Math.Max(maxGuard,validatedAt-readAt);
            // Reads and all synchronous safety work consume this original hard
            // deadline. A stable but too-late observation cannot renew it.
            if(validatedAt>deadline)return Result(false,readAt>deadline?"ReadDeadline":"GuardDeadline");
            bool moved=(current-before).Length>QuietDistance;
            long observedElapsed=readAt-began;
            if(observedElapsed>=InitialMilliseconds && quietElapsed>=QuietMilliseconds &&
                (moved || observedElapsed>=NoMovementMilliseconds))
                return Result(true,validatedAt>normalDeadline?(moved?"DelayedQuiet":"DelayedNoMovement"):(moved?"Quiet":"NoMovement"));
            // Passing the usual 320ms window keeps the same released-input
            // observation owner alive, rather than failing or starting a new
            // pulse. Quiet confirmation still has the original 1000ms bound.
            if(validatedAt>=deadline)return Result(false,"QuietWindowDeadline");
            nextWait=SampleMilliseconds;
        }
    }
}
