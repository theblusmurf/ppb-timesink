namespace PoteHunter;

// Released, quiet samples can confirm that a minimum-frame tap produced no
// observed displacement. They cannot prove a larger tap is safe: the caller
// must reserve and check that tap's complete displacement before input.
internal sealed class ArrivalPulseAdaptation
{
    internal const int MinimumEffectiveMilliseconds=32,ObservationMaximumAgeMilliseconds=2000;
    internal const double NoMovementDistance=.01;
    Vec goal,position;
    double tolerance;
    long observedAt,firstQualifiedAt;
    int noMovementCount;
    bool owned,offered;

    internal void Reset() {owned=offered=false;noMovementCount=0;observedAt=firstQualifiedAt=0;}

    internal int Select(Vec currentGoal,Vec currentPosition,double currentTolerance,int planned,long now)
    {
        Validate(currentGoal,currentPosition,currentTolerance,now);
        ArrivalPulseGeometry.FrameDuration(planned);
        Own(currentGoal,currentPosition,currentTolerance);
        Expire(now);
        if(planned!=ArrivalMotion.FrameMilliseconds || noMovementCount<2 || offered)return planned;
        // Offer at most once at the same quiet location, including when the
        // caller rejects its larger clearance and uses the ordinary tap.
        offered=true;noMovementCount=0;
        return MinimumEffectiveMilliseconds;
    }

    internal bool TrySelectClearance(Vec currentGoal,Vec currentPosition,Vec forward,double currentTolerance,
        int planned,long now,Func<int,double> reserve,Func<Vec,Vec,bool>? canAdvance,out int selected,out double step)
    {
        selected=Select(currentGoal,currentPosition,currentTolerance,planned,now);
        step=reserve(selected);
        if(ArrivalPulseGeometry.PathClear(currentPosition,forward,step,canAdvance))return true;
        // An adaptation never overrides an obstacle or boundary. Falling back
        // still consumes its one offer and independently checks the usual tap.
        if(selected==planned)return false;
        selected=planned;step=reserve(selected);
        return ArrivalPulseGeometry.PathClear(currentPosition,forward,step,canAdvance);
    }

    internal void Observe(Vec currentGoal,Vec before,Vec released,Vec after,double currentTolerance,
        int requested,double heldMilliseconds,bool settled,string status,long now)
    {
        Validate(currentGoal,before,currentTolerance,now);
        if(!released.Finite || !after.Finite)throw new InvalidOperationException("Forward correction position is unavailable.");
        ArrivalPulseGeometry.FrameDuration(requested);
        Own(currentGoal,before,currentTolerance);
        Expire(now);
        double displacement=(after-before).Length;
        if(displacement>ArrivalPulseSettling.QuietDistance)
        {
            position=after;noMovementCount=0;offered=false;
        }
        else if(settled && (status is "NoMovement" or "DelayedNoMovement") &&
            displacement<=NoMovementDistance && (released-before).Length<=NoMovementDistance &&
            (after-released).Length<=NoMovementDistance &&
            requested==ArrivalMotion.FrameMilliseconds && double.IsFinite(heldMilliseconds) &&
            heldMilliseconds>=ArrivalMotion.FrameMilliseconds && heldMilliseconds<MinimumEffectiveMilliseconds)
        {
            if(noMovementCount==0)firstQualifiedAt=now;
            noMovementCount=Math.Min(2,noMovementCount+1);
        }
        else noMovementCount=0;
        observedAt=now;
    }

    void Own(Vec currentGoal,Vec currentPosition,double currentTolerance)
    {
        if(!owned || (currentGoal-goal).Length>1e-6 || Math.Abs(currentTolerance-tolerance)>1e-6 ||
            (currentPosition-position).Length>NoMovementDistance)
        {
            goal=currentGoal;position=currentPosition;tolerance=currentTolerance;
            noMovementCount=0;offered=false;observedAt=firstQualifiedAt=0;owned=true;
        }
    }

    void Expire(long now)
    {
        if(now<observedAt || now-observedAt>ObservationMaximumAgeMilliseconds ||
            noMovementCount>0 && (now<firstQualifiedAt || now-firstQualifiedAt>ObservationMaximumAgeMilliseconds))
            noMovementCount=0;
    }

    static void Validate(Vec goal,Vec position,double tolerance,long now)
    {
        if(!goal.Finite || !position.Finite)throw new InvalidOperationException("Forward correction geometry is unavailable.");
        if(!double.IsFinite(tolerance) || tolerance<=0)throw new ArgumentOutOfRangeException(nameof(tolerance));
        if(now<0)throw new ArgumentOutOfRangeException(nameof(now));
    }
}
