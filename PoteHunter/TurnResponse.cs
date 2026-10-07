namespace PoteHunter;

public sealed class TurnUnresponsiveException(Vec position,Vec forward,string message="The game did not respond to sustained turning input.",
    TurnResponseObservation? observation=null)
    : Exception(message)
{
    public Vec Position { get; }=position;
    public Vec Forward { get; }=forward;
    public TurnResponseObservation? Observation { get; }=observation;
}

// Numeric observations plus a short machine owner label. No history, player
// name or unbounded target list is retained by the controller or failure log.
public sealed record TurnResponseObservation(string Owner,double GoalHeading,double SentGoalHeading,
    double BaselineHeading,double ActualHeading,double ErrorRadians,double BaselineErrorRadians,
    double NetErrorReductionRadians,double BestErrorReductionRadians,double RequiredProgressRadians,
    long NoProgressMilliseconds,long SinceSentMilliseconds,int SentPixels,int ObservedCommandPixels,int SentCommands,
    Vec BaselinePosition,Vec Position);

public sealed class TurnResponse
{
    public const long NoResponseMilliseconds=1500;
    public const double HeadingProgressRadians=.02;
    public const double PositionProgressUnits=.10;
    public const double FineProgressRadians=.002;
    bool tracking;
    Vec baselinePosition;
    double baselineHeading;
    long baselineAt;
    long? lastObservationAt;
    string? goalOwner;
    double sentGoalHeading,previousHeading,sentDirection,netReduction,bestReduction,checkpointReduction,baselineError;
    long lastSentAt;
    int sentCommands,lastSentPixels;
    public TurnResponseObservation? Observation { get; private set; }

    public void Reset()
    {
        tracking=false;baselinePosition=default;baselineHeading=0;baselineAt=0;lastObservationAt=null;
        goalOwner=null;sentGoalHeading=previousHeading=sentDirection=netReduction=bestReduction=checkpointReduction=baselineError=0;
        lastSentAt=0;sentCommands=lastSentPixels=0;Observation=null;
    }

    static double Wrap(double angle)=>Math.Atan2(Math.Sin(angle),Math.Cos(angle));
    static double RequiredProgress(double error,double tolerance)=>
        Math.Clamp(Math.Max(0,Math.Abs(error)-tolerance)*.5,FineProgressRadians,HeadingProgressRadians);

    // Measure what the last sent turn actually accomplished, independently of
    // a moving target's updated direction. Repeated packets, reversed goals or
    // target motion never renew the clock without measured heading progress.
    // Signed reductions accumulate; away motion/overshoot subtracts them, so
    // oscillating through an old best cannot earn the same allowance twice.
    public bool ObserveGoal(Vec position,double heading,int commandedPixels,long now,string owner,
        double errorRadians,double toleranceRadians,bool awaitingResponse=true)
    {
        if(!position.Finite)throw new ArgumentOutOfRangeException(nameof(position));
        if(!double.IsFinite(heading))throw new ArgumentOutOfRangeException(nameof(heading));
        if(!double.IsFinite(errorRadians))throw new ArgumentOutOfRangeException(nameof(errorRadians));
        if(!double.IsFinite(toleranceRadians)||toleranceRadians<=0||toleranceRadians>Math.PI)
            throw new ArgumentOutOfRangeException(nameof(toleranceRadians));
        if(string.IsNullOrWhiteSpace(owner)||owner.Length>80)throw new ArgumentOutOfRangeException(nameof(owner));
        if(now<0)throw new ArgumentOutOfRangeException(nameof(now));
        if(lastObservationAt is long previous && now<previous)
            throw new InvalidOperationException("Turning observations arrived out of order.");
        lastObservationAt=now;
        double normalized=Wrap(heading),error=Wrap(errorRadians),goal=Wrap(normalized-error);
        if(goalOwner!=owner)
        {
            // Callers supply a stable verified activity/target identity. Only
            // an actual owner transition or explicit Reset grants a new goal.
            tracking=false;goalOwner=owner;netReduction=bestReduction=checkpointReduction=0;
            sentCommands=lastSentPixels=0;Observation=null;
        }
        if(commandedPixels==0&&!awaitingResponse){tracking=false;Observation=null;return false;}
        if(!tracking)
        {
            if(commandedPixels==0)return false;
            tracking=true;baselinePosition=position;baselineHeading=previousHeading=normalized;baselineAt=now;
            baselineError=Math.Abs(error);sentGoalHeading=goal;sentDirection=Math.Sign(error);
            netReduction=bestReduction=checkpointReduction=0;lastSentAt=now;lastSentPixels=commandedPixels;sentCommands=1;
        }
        else
        {
            double priorError=Wrap(previousHeading-sentGoalHeading),measuredError=Wrap(normalized-sentGoalHeading);
            double measuredTurn=-Wrap(normalized-previousHeading);
            double reduction=Math.Abs(priorError)-Math.Abs(measuredError);
            // The half-turn wrap has two equally short arcs. Preserve the
            // sign of the requested correction instead of crediting an
            // equally sized response in the opposite direction.
            if(reduction>0 && measuredTurn*sentDirection<=0)reduction=-Math.Abs(measuredTurn);
            netReduction+=reduction;
            bestReduction=Math.Max(bestReduction,netReduction);
            double required=RequiredProgress(baselineError,toleranceRadians);
            if(reduction>0 && netReduction>=bestReduction && bestReduction-checkpointReduction>=required)
            {
                checkpointReduction=bestReduction;baselinePosition=position;baselineHeading=normalized;
                baselineError=Math.Abs(error);baselineAt=now;
            }
            previousHeading=normalized;
            if(commandedPixels!=0)
            {
                sentGoalHeading=goal;sentDirection=Math.Sign(error);lastSentAt=now;lastSentPixels=commandedPixels;
                if(sentCommands<int.MaxValue)sentCommands++;
            }
        }
        Observation=new(owner,goal,sentGoalHeading,baselineHeading,normalized,error,baselineError,
            netReduction,bestReduction,RequiredProgress(baselineError,toleranceRadians),
            now-baselineAt,now-lastSentAt,lastSentPixels,commandedPixels,sentCommands,baselinePosition,position);
        return now-baselineAt>=NoResponseMilliseconds;
    }

    // Commands alone are not progress: either the measured facing or position
    // must change. Small delayed changes accumulate against the same baseline.
    public bool Observe(Vec position,double heading,int commandedPixels,long now,bool awaitingResponse=false)
    {
        if(!position.Finite)throw new ArgumentOutOfRangeException(nameof(position));
        if(!double.IsFinite(heading))throw new ArgumentOutOfRangeException(nameof(heading));
        if(now<0)throw new ArgumentOutOfRangeException(nameof(now));
        if(lastObservationAt is long previous && now<previous)
            throw new InvalidOperationException("Turning observations arrived out of order.");
        lastObservationAt=now;
        if(goalOwner!=null){tracking=false;goalOwner=null;Observation=null;}
        if(commandedPixels==0 && !awaitingResponse)
        {
            tracking=false;
            return false;
        }
        // A smoothing wait can check a previously sent turn, but cannot start
        // a no-response window for input that was never actually sent.
        if(!tracking && commandedPixels==0)return false;
        // Normalize before subtraction so even large finite angles cannot
        // overflow, and crossing the heading wrap is measured as a small turn.
        double normalized=Math.Atan2(Math.Sin(heading),Math.Cos(heading));
        double difference=normalized-baselineHeading;
        double turned=Math.Abs(Math.Atan2(Math.Sin(difference),Math.Cos(difference)));
        if(!tracking || turned>=HeadingProgressRadians || (position-baselinePosition).Length>=PositionProgressUnits)
        {
            tracking=true;baselinePosition=position;baselineHeading=normalized;baselineAt=now;
            return false;
        }
        return now-baselineAt>=NoResponseMilliseconds;
    }

    public static void SelfTest()
    {
        static void Require(bool condition,string message)
        {
            if(!condition)throw new Exception("Turn response: "+message);
        }
        Vec origin=new(0,0);
        var stationary=new TurnResponse();
        Require(!stationary.Observe(origin,0,96,0) && !stationary.Observe(origin,0,96,1499) &&
            stationary.Observe(origin,0,96,1500) && stationary.Observe(origin,0,-96,1600),
            "sustained unresponsive corrections were not bounded at1500ms or a reversed command hid the stall.");
        Require(!stationary.Observe(origin,.03,96,1700) && !stationary.Observe(origin,.03,96,3199) &&
            stationary.Observe(origin,.03,96,3200),"real turning failed to restart the no-response window.");

        var delayed=new TurnResponse();
        Require(!delayed.Observe(origin,0,96,0) && !delayed.Observe(origin,0,96,1000) &&
            !delayed.Observe(origin,.01,96,1400) && !delayed.Observe(origin,.021,96,1499) &&
            !delayed.Observe(origin,.021,96,1500) && !delayed.Observe(origin,.021,96,2998) &&
            delayed.Observe(origin,.021,96,2999),"delayed heading response was ignored or small noise repeatedly reset the timer.");

        var wrapping=new TurnResponse();
        Require(!wrapping.Observe(origin,Math.PI-.005,96,0) &&
            !wrapping.Observe(origin,-Math.PI+.005,96,1499) &&
            wrapping.Observe(origin,-Math.PI+.005,96,1500),"crossing the angle wrap fabricated a large turn.");
        wrapping.Reset();
        Require(!wrapping.Observe(origin,Math.PI-.03,96,0) &&
            !wrapping.Observe(origin,-Math.PI+.03,96,1499) &&
            !wrapping.Observe(origin,-Math.PI+.03,96,1500) &&
            wrapping.Observe(origin,-Math.PI+.03,96,2999),"a real turn across the angle wrap failed to reset the timer.");

        var moving=new TurnResponse();
        Require(!moving.Observe(origin,0,96,0) && !moving.Observe(new(.05,0),0,96,1000) &&
            !moving.Observe(new(.101,0),0,96,1499) && !moving.Observe(new(.101,0),0,96,1500) &&
            moving.Observe(new(.101,0),0,96,2999),"position progress was ignored or subthreshold position noise renewed the window.");
        var quiet=new TurnResponse();
        Require(!quiet.Observe(origin,0,96,0) && !quiet.Observe(origin,0,0,1400) &&
            !quiet.Observe(origin,0,0,5000) && !quiet.Observe(origin,0,96,6000) &&
            !quiet.Observe(origin,0,96,7499) && quiet.Observe(origin,0,96,7500),
            "time without a turning command was counted as unresponsive turning.");
        quiet.Reset();
        Require(!quiet.Observe(origin,0,96,0),"explicit reset retained the previous window or observation time.");

        // A new loot owner must not inherit a combat observation's elapsed
        // no-response window. Reset the goal, not the genuine stall deadline.
        var changedGoal=new TurnResponse();
        Require(!changedGoal.Observe(origin,0,96,0) && !changedGoal.Observe(origin,0,96,1200) &&
            changedGoal.Observe(origin,0,96,1600),"old goal fixture did not retain a sustained turn failure.");
        changedGoal.Reset();
        Require(!changedGoal.Observe(origin,0,-32,1600) && !changedGoal.Observe(origin,0,-32,1900) &&
            !changedGoal.Observe(origin,0,-32,3099) && changedGoal.Observe(origin,0,-32,3100),
            "new loot goal inherited old elapsed time or reset disabled genuine no-response detection.");

        var invalid=new TurnResponse();invalid.Observe(origin,0,96,100);
        foreach(var position in new[]{new Vec(double.NaN,0),new Vec(0,double.PositiveInfinity)})
        {
            bool rejected=false;try{invalid.Observe(position,0,96,200);}catch(ArgumentOutOfRangeException){rejected=true;}
            Require(rejected,"nonfinite position was accepted.");
        }
        foreach(double heading in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
        {
            bool rejected=false;try{invalid.Observe(origin,heading,96,200);}catch(ArgumentOutOfRangeException){rejected=true;}
            Require(rejected,"nonfinite heading was accepted.");
        }
        bool reversed=false;try{invalid.Observe(origin,0,96,99);}catch(InvalidOperationException){reversed=true;}
        Require(reversed && invalid.Observe(origin,0,96,1600),"reversed time or invalid samples changed the valid baseline.");
        bool negative=false;try{new TurnResponse().Observe(origin,0,96,-1);}catch(ArgumentOutOfRangeException){negative=true;}
        Require(negative,"negative observation time was accepted.");
        invalid.Observe(origin,0,0,1700);
        reversed=false;try{invalid.Observe(origin,0,96,1699);}catch(InvalidOperationException){reversed=true;}
        Require(reversed,"a zero command discarded monotonic observation validation.");
        var exception=new TurnUnresponsiveException(new(1,2),new(0,-1));
        Require(exception.Position==new Vec(1,2) && exception.Forward==new Vec(0,-1),"the recovery exception lost its measured position or facing.");
        TurnResponseGoalChecks.Run();
    }
}
