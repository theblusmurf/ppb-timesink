namespace PoteHunter;

internal enum StationaryReturnDefenseOutcome { Unavailable, Yielded, BudgetExpired }

// Return intent, recovery episode, and repair completion belong to the caller.
// This helper only lends a short interval to an already in-range enemy; it has
// no movement, loot, route completion, repair, or revival operation.
internal static class StationaryReturnDefense
{
    public const double AnchorRadius=1.5;
    public const int DefaultBudgetMilliseconds=1000;
    public const int PollMilliseconds=50;

    // ContextVerified must be renewed by the live caller on every observation:
    // same local character/map/floor, stationary solo mode, repair ready, and a
    // fresh scene. TargetApproved carries the existing target protections.
    internal readonly record struct Observation(Vec Position,Vec Anchor,Health PlayerHealth,
        Entity? Target,Health TargetHealth,double SwingRange,bool ContextVerified,bool TargetApproved);

    public static bool NearAnchor(Vec position,Vec anchor)=>position.Finite && anchor.Finite &&
        (position-anchor).Length<=AnchorRadius;

    public static bool ShouldHandOffPressure(bool group,bool stationaryAssignment,Vec position,Vec anchor,Health playerHealth)=>
        !group && stationaryAssignment && playerHealth is {Known:true,Dead:false} && NearAnchor(position,anchor);

    public static bool CanDefend(Observation sample)=>sample.ContextVerified && sample.TargetApproved &&
        sample.PlayerHealth is {Known:true,Dead:false} && NearAnchor(sample.Position,sample.Anchor) &&
        double.IsFinite(sample.SwingRange) && sample.SwingRange>0 &&
        sample.Target is {Monster:true,Targetable:true,PriorityLootObject:false} target && target.Position.Finite &&
        (Targeting.IsStationaryHuntTargetId(target.Id) || Targeting.IsGamekeeper(target)) &&
        sample.TargetHealth is {Known:true,Dead:false} && (target.Position-sample.Position).Length<=sample.SwingRange;

    static bool SameTarget(Entity expected,Entity? actual)=>actual!=null && expected.Id==actual.Id &&
        expected.Address==actual.Address && expected.Generation==actual.Generation;

    // Injected attack may aim/hold the existing basic swing, but must not chase
    // or take an assist step. Each await uses the caller's normal input safety
    // checks. No detached timeout/input task survives a completed invocation.
    public static async Task<StationaryReturnDefenseOutcome> RunAsync(Func<Observation> observe,Action stopMovement,
        Func<Observation,CancellationToken,Task> attack,Action releaseAttack,
        Func<int,CancellationToken,Task> delay,Func<long> clock,CancellationToken token,
        int budgetMilliseconds=DefaultBudgetMilliseconds)
    {
        if(budgetMilliseconds is <1 or >10000)throw new ArgumentOutOfRangeException(nameof(budgetMilliseconds));
        token.ThrowIfCancellationRequested();
        var initial=observe();
        if(!CanDefend(initial))return StationaryReturnDefenseOutcome.Unavailable;
        var target=initial.Target!;
        long started=clock(),previous=started;
        if(started<0)throw new InvalidOperationException("Stationary defense clock is unavailable.");
        try
        {
            stopMovement();
            while(true)
            {
                token.ThrowIfCancellationRequested();
                long now=clock();
                if(now<previous)throw new InvalidOperationException("Stationary defense clock moved backward.");
                previous=now;
                long remaining=budgetMilliseconds-(now-started);
                if(remaining<=0)return StationaryReturnDefenseOutcome.BudgetExpired;
                var current=observe();
                if(!CanDefend(current) || current.Anchor!=initial.Anchor || !SameTarget(target,current.Target))return StationaryReturnDefenseOutcome.Yielded;
                // Revalidate after the stop and every delay. Normal incoming
                // damage does not revoke a living character's defense.
                await attack(current,token);
                now=clock();
                if(now<previous)throw new InvalidOperationException("Stationary defense clock moved backward.");
                previous=now;
                remaining=budgetMilliseconds-(now-started);
                if(remaining<=0)return StationaryReturnDefenseOutcome.BudgetExpired;
                await delay((int)Math.Min(PollMilliseconds,remaining),token);
            }
        }
        finally {try{stopMovement();}finally{releaseAttack();}}
    }
}
