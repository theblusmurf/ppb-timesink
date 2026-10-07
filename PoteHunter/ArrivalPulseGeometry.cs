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

internal readonly record struct ArrivalPulseObservation(Vec Position,bool Settled,long ElapsedMilliseconds);

// Observes released motion only. The caller retains input ownership and uses
// its ordinary guarded delay, so health/focus/identity/cancellation still run.
internal static class ArrivalPulseSettling
{
    internal const int InitialMilliseconds=120,QuietMilliseconds=80,SampleMilliseconds=40,
        NoMovementMilliseconds=200,MaximumMilliseconds=320;
    internal const double QuietDistance=.025;

    internal static async Task<ArrivalPulseObservation> ObserveAsync(Vec before,Vec released,
        Func<Vec> position,Func<int,CancellationToken,Task> delay,Func<long> clock,CancellationToken token)
    {
        if(!before.Finite || !released.Finite)throw new InvalidOperationException("Forward correction position is unavailable.");
        long began=clock(),deadline=began+MaximumMilliseconds,quietAt=began;
        Vec quietOrigin=released,current=released;
        int nextWait=InitialMilliseconds;
        while(true)
        {
            token.ThrowIfCancellationRequested();
            long remaining=deadline-clock();
            if(remaining<=0)return new(current,false,Math.Max(0,clock()-began));
            await delay((int)Math.Min(nextWait,remaining),token);
            token.ThrowIfCancellationRequested();
            current=position();
            if(!current.Finite)throw new InvalidOperationException("Forward correction position is unavailable.");
            long now=clock(),elapsed=Math.Max(0,now-began);
            // A late stable read must not turn a consumed deadline into a new
            // permission to learn or pulse. Exact-boundary quiet success is
            // valid; reader/guard work beyond that boundary remains a failure.
            if(now>deadline)return new(current,false,elapsed);
            // Compare to the start of the quiet window: several individually
            // small changes must not masquerade as a settled observation.
            if((current-quietOrigin).Length>QuietDistance){quietOrigin=current;quietAt=now;}
            bool moved=(current-before).Length>QuietDistance;
            if(elapsed>=InitialMilliseconds && now-quietAt>=QuietMilliseconds &&
                (moved || elapsed>=NoMovementMilliseconds))return new(current,true,elapsed);
            // Never renew the original observation deadline. Guard/reader work
            // may consume real time, but cannot authorize another pending tap.
            if(now>=deadline)return new(current,false,elapsed);
            nextWait=SampleMilliseconds;
        }
    }
}
