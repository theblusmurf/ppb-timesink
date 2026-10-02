namespace PoteHunter;

// Retains this run's cancellation/identity gates after a known movement fault.
// It sends no input and never creates or restarts a hunting run.
internal sealed class FaultDeathWatch
{
    public const int WaitMilliseconds=120000;
    public const int RecoveryMilliseconds=600000;
    public bool Active {get;private set;}
    public string? Reason {get;private set;}
    long startedAt;
    public bool TryBegin(Exception failure,bool enabled,bool group,bool cancelled,long now)
    {
        if(Active || !enabled || group || cancelled || failure is not (TurnUnresponsiveException or MovementBlockedException or AnchorReturnException))return false;
        Active=true;Reason=failure.Message;startedAt=now;return true;
    }
    public bool Expired(long now,bool deathPending)=>Active && now-startedAt>=(deathPending?RecoveryMilliseconds:WaitMilliseconds);
    public int RecoveryRemaining(long now)=>Active?(int)Math.Clamp(RecoveryMilliseconds-(now-startedAt),0,RecoveryMilliseconds):RecoveryMilliseconds;
    public void Reset(){Active=false;Reason=null;startedAt=0;}
}
