namespace PoteHunter;

internal readonly record struct SavedRouteTurnRetry(bool Ready,string Reason,int WaypointIndex,
    long ElapsedMilliseconds,Vec? Position=null,double? ActualHeading=null);

// A retry belongs to a particular saved path/checkpoint, not its changing aim
// vector or the most recent movement sample. It does not send any game input.
internal sealed class SavedRouteTurnRecovery
{
    internal const int WaypointAllowanceMilliseconds=20000;
    internal const int SettleMilliseconds=120;
    RecoveryPath? owner;
    int waypointIndex;
    long waypointStartedAt;
    bool retried;

    public void ObserveWaypoint(RecoveryPath path,long now)
    {
        ArgumentNullException.ThrowIfNull(path);
        if(now<0)throw new ArgumentOutOfRangeException(nameof(now));
        if(ReferenceEquals(owner,path))
        {
            if(path.Index<waypointIndex)throw new InvalidOperationException("Saved route checkpoint moved backwards.");
            if(path.Index==waypointIndex)return;
        }
        owner=path;waypointIndex=path.Index;waypointStartedAt=now;retried=false;
    }

    public async Task<SavedRouteTurnRetry> RetryAsync(RecoveryPath path,Action release,Action reset,
        Func<int,CancellationToken,Task> delay,Func<(Vec Position,double Heading)> observe,
        Func<long> clock,CancellationToken token,Action<SavedRouteTurnRetry>? retryStarted=null)
    {
        // Even a cancelled or already-used attempt releases movement/mouse
        // before a diagnostic read, reset or guarded settling delay.
        release();token.ThrowIfCancellationRequested();
        long now=clock();
        long elapsed=Math.Max(0,now-waypointStartedAt);
        if(!ReferenceEquals(owner,path)||path.Index!=waypointIndex)
            return new(false,"WaypointChanged",path.Index,elapsed);
        if(now<waypointStartedAt || elapsed>=WaypointAllowanceMilliseconds)
            return new(false,"WaypointDeadline",waypointIndex,elapsed);
        if(retried)return new(false,"AlreadyRetried",waypointIndex,elapsed);
        retried=true;
        reset();retryStarted?.Invoke(new(false,"RetryStarted",waypointIndex,elapsed));
        await delay(SettleMilliseconds,token);token.ThrowIfCancellationRequested();
        if(clock()-waypointStartedAt>=WaypointAllowanceMilliseconds)
            return new(false,"WaypointDeadline",waypointIndex,Math.Max(0,clock()-waypointStartedAt));
        var fresh=observe();token.ThrowIfCancellationRequested();
        // Input.Delay(0) in the live caller repeats focus, health, character,
        // window identity and protection admission after synchronous reads.
        await delay(0,token);token.ThrowIfCancellationRequested();
        if(!fresh.Position.Finite || !double.IsFinite(fresh.Heading))
            throw new InvalidOperationException("Saved route turn retry position or heading is unavailable.");
        now=clock();elapsed=Math.Max(0,now-waypointStartedAt);
        if(now<waypointStartedAt || elapsed>=WaypointAllowanceMilliseconds)
            return new(false,"WaypointDeadline",waypointIndex,elapsed,fresh.Position,fresh.Heading);
        if(!ReferenceEquals(owner,path)||path.Index!=waypointIndex)
            return new(false,"WaypointChanged",path.Index,elapsed,fresh.Position,fresh.Heading);
        return new(true,"RetryReady",waypointIndex,elapsed,fresh.Position,fresh.Heading);
    }
}
