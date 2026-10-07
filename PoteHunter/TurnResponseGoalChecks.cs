using System.Text.Json;
namespace PoteHunter;

internal static class TurnResponseGoalChecks
{
    static void Require(bool condition,string reason)
    {if(!condition)throw new Exception("Goal turn response: "+reason);}

    internal static void Run()
    {
        Vec origin=default;
        var fine=new TurnResponse();const double initial=.04634;
        Require(!fine.ObserveGoal(origin,0,1,0,"saved-facing",initial,.035),"first sent turn failed");
        // The recorded 2.655-degree error has less than 0.02 rad left before
        // strict acceptance. Delayed quantized motion must count without
        // widening that gate or counting a commanded pixel as movement.
        for(int step=1;step<=4;step++)
        {
            double observed=step*.0024;
            Require(!fine.ObserveGoal(origin,-observed,1,step*400,"saved-facing",initial-observed,.035),
                "measured near-goal quantized progress inherited the coarse 0.02-radian threshold");
        }
        Require(fine.Observation is {NoProgressMilliseconds:<=400,RequiredProgressRadians:<.02} &&
            initial-.0096>.035,"near-goal fixture silently widened acceptance or lost its measured progress");
        Require(initial-.012<=.035,"fixture did not reach the unchanged facing threshold");

        var small=new TurnResponse();small.ObserveGoal(origin,0,1,0,"fine-combat",.039,.018);
        Require(!small.ObserveGoal(origin,-.006,1,600,"fine-combat",.033,.018)&&
            !small.ObserveGoal(origin,-.012,1,1200,"fine-combat",.027,.018)&&
            !small.ObserveGoal(origin,-.014,0,1500,"fine-combat",.025,.018),
            "accumulated correct fine movement was rejected before the unchanged one-degree settling gate");

        var stationary=new TurnResponse();stationary.ObserveGoal(origin,0,1,0,"target:1",.04,.018);
        Require(!stationary.ObserveGoal(origin,0,-1,1000,"target:1",-.4,.018)&&
            stationary.ObserveGoal(origin,0,1,1500,"target:1",.03,.018),
            "moving/reversed target directions renewed a stalled owner's deadline");
        Require(stationary.Observation is {Owner:"target:1",NoProgressMilliseconds:1500,SentCommands:3,
            NetErrorReductionRadians:0,BestErrorReductionRadians:0},"failure telemetry lost stable owner or actual no-progress proof");

        var unsent=new TurnResponse();
        Require(!unsent.ObserveGoal(origin,0,0,0,"unsent",.04,.018)&&
            !unsent.ObserveGoal(origin,0,0,10000,"unsent",.04,.018)&&unsent.Observation==null,
            "smoothing waits started a response timer without sent input");

        var wrong=new TurnResponse();wrong.ObserveGoal(origin,0,1,0,"wrong",.2,.035);
        Require(wrong.ObserveGoal(origin,.03,1,1500,"wrong",.23,.035),
            "large wrong-way measured heading hid a sustained turning fault");
        var translating=new TurnResponse();translating.ObserveGoal(origin,0,1,0,"stationary-face",.2,.035);
        Require(translating.ObserveGoal(new(2,2),0,1,1500,"stationary-face",.2,.035),
            "position drift was treated as a successful stationary heading correction");

        var jitter=new TurnResponse();jitter.ObserveGoal(origin,0,1,0,"jitter",.047,.035);
        Require(!jitter.ObserveGoal(origin,-.002,1,600,"jitter",.045,.035)&&
            !jitter.ObserveGoal(origin,.001,1,1200,"jitter",.048,.035)&&
            jitter.ObserveGoal(origin,-.002,1,1500,"jitter",.045,.035),
            "subthreshold alternating noise renewed the response clock");
        var oscillating=new TurnResponse();oscillating.ObserveGoal(origin,0,1,0,"oscillating",.2,.035);
        Require(!oscillating.ObserveGoal(origin,-.03,1,100,"oscillating",.17,.035)&&
            !oscillating.ObserveGoal(origin,0,1,800,"oscillating",.2,.035)&&
            !oscillating.ObserveGoal(origin,-.03,1,1500,"oscillating",.17,.035)&&
            oscillating.ObserveGoal(origin,-.03,0,1600,"oscillating",.17,.035),
            "oscillation earned the same measured best improvement twice");
        var overshot=new TurnResponse();overshot.ObserveGoal(origin,0,1,0,"overshot",.04,.018);
        Require(!overshot.ObserveGoal(origin,-.03,1,100,"overshot",.01,.018)&&
            !overshot.ObserveGoal(origin,-.08,-1,800,"overshot",-.04,.018)&&
            !overshot.ObserveGoal(origin,-.03,1,1500,"overshot",.01,.018)&&
            overshot.ObserveGoal(origin,-.03,0,1600,"overshot",.01,.018),
            "overshoot and reversed correction repeatedly credited an already reached best angle");

        foreach(double start in new[]{Math.PI-.004,-Math.PI+.004})
        {
            static double Wrap(double angle)=>Math.Atan2(Math.Sin(angle),Math.Cos(angle));
            var wrapped=new TurnResponse();wrapped.ObserveGoal(origin,start,1,0,"wrap",initial,.035);
            Require(!wrapped.ObserveGoal(origin,Wrap(start-.006),1,1300,"wrap",initial-.006,.035)&&
                !wrapped.ObserveGoal(origin,Wrap(start-.006),0,1500,"wrap",initial-.006,.035),
                "quantized correct movement across the heading wrap was lost");
        }
        var halfTurn=new TurnResponse();halfTurn.ObserveGoal(origin,0,1,0,"half-turn",Math.PI,.035);
        Require(halfTurn.ObserveGoal(origin,.03,1,1500,"half-turn",-Math.PI+.03,.035),
            "half-turn wrap credited the opposite measured direction");

        var newOwner=new TurnResponse();newOwner.ObserveGoal(origin,0,1,0,"target:1",.2,.035);
        Require(!newOwner.ObserveGoal(origin,0,-1,1200,"target:2",-.2,.035)&&
            !newOwner.ObserveGoal(origin,0,0,2699,"target:2",-.2,.035)&&
            newOwner.ObserveGoal(origin,0,0,2700,"target:2",-.2,.035),
            "verified new owner retained the old clock or lost the real new stall deadline");
        newOwner.Reset();
        Require(!newOwner.ObserveGoal(origin,0,0,10000,"new-loot",.2,.035),"reset fabricated an outstanding sent correction");

        var invalid=new TurnResponse();invalid.ObserveGoal(origin,0,1,100,"valid",.2,.035);
        foreach(double error in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
        {
            bool rejected=false;try{invalid.ObserveGoal(origin,0,1,200,"valid",error,.035);}
            catch(ArgumentOutOfRangeException){rejected=true;}Require(rejected,"nonfinite goal error changed the response baseline");
        }
        foreach(double tolerance in new[]{0d,double.NaN,double.PositiveInfinity,Math.PI+.01})
        {
            bool rejected=false;try{invalid.ObserveGoal(origin,0,1,200,"valid",.2,tolerance);}
            catch(ArgumentOutOfRangeException){rejected=true;}Require(rejected,"invalid tolerance changed the response baseline");
        }
        bool outOfOrder=false;try{invalid.ObserveGoal(origin,0,1,99,"valid",.2,.035);}
        catch(InvalidOperationException){outOfOrder=true;}
        Require(outOfOrder&&invalid.ObserveGoal(origin,0,1,1600,"valid",.2,.035),
            "invalid/out-of-order observation renewed the last valid watchdog");
        bool ownerRejected=false;try{new TurnResponse().ObserveGoal(origin,0,1,0,new string('x',81),.2,.035);}
        catch(ArgumentOutOfRangeException){ownerRejected=true;}Require(ownerRejected,"failure owner label was unbounded");

        var cancelled=new Movement();bool interrupted=false;
        try{cancelled.ObserveTurning(origin,0,1,0,"cancelled",.2,.035,new CancellationToken(true));}
        catch(OperationCanceledException){interrupted=true;}
        Require(interrupted,"response observation swallowed cancellation");
        cancelled.ObserveTurning(origin,0,0,10000,"cancelled",.2,.035,default);
        var failure=new TurnUnresponsiveException(origin,new(0,-1),observation:stationary.Observation);
        Require(ReferenceEquals(failure.Observation,stationary.Observation),"failure discarded its measured goal observation");

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"turn-goal-response-checks.json"),JsonSerializer.Serialize(new
        {
            Passed=true,HardwareInputEmitted=false,NoResponseMilliseconds=TurnResponse.NoResponseMilliseconds,
            FineProgressRadians=TurnResponse.FineProgressRadians,
            Checks=new[]{"recorded near-gate quantized response","strict fine/ordinary facing gates retained",
                "moving goal without measured heading cannot renew","unsent waits never arm","wrong-way and position-only motion rejected",
                "signed accumulated noise/oscillation/overshoot cannot recycle progress","angle wrap and requested half-turn direction",
                "stable verified owner transitions and explicit reset","invalid/time-reversed observations preserve baseline",
                "cancellation before observation","bounded measured failure evidence"}
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
