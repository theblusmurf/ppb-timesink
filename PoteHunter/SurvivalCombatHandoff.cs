namespace PoteHunter;

internal enum SurvivalHandoffOutcome { Unavailable, CastAttempted, Yielded, NoProgress, DurationExpired }

// One urgent range correction belongs to the whole low-HP episode, rather
// than a target, route goal or local combat loop. Unknown HP cannot reset it.
// A failed correction therefore cannot become an endless outward chase.
internal sealed class SurvivalCombatHandoff
{
    internal const double MaximumCorrection=.5;
    internal const int MaximumMilliseconds=2000,NoProgressMilliseconds=1500;
    bool correctionConsumed;
    internal bool CorrectionConsumed=>correctionConsumed;
    internal bool MovedLastRun {get;private set;}

    internal readonly record struct Observation(Vec Position,Vec Anchor,Health PlayerHealth,Entity? Target,
        Health TargetHealth,double Range,bool ContextVerified,bool Owned,bool SkillReady,long RecoveryEpisode,
        string? BlockedReason=null);

    internal void ObserveHealth(Health health,decimal threshold)
    {
        if(health is {Known:true,Dead:false} && threshold>0 && threshold<=100 &&
            (decimal)health.Current*100>threshold*health.Maximum)correctionConsumed=false;
    }

    internal static bool RequiresEnemy(HotbarSlot slot)=>
        slot.SkillTarget is SkillTargetKind.Melee or SkillTargetKind.Enemy or SkillTargetKind.EnemyLine or
            SkillTargetKind.EnemyObject || slot.Name.Contains("Power Drain",StringComparison.OrdinalIgnoreCase);

    internal static string? Blocked(Observation sample)
    {
        if(sample.BlockedReason!=null)return sample.BlockedReason;
        if(!sample.ContextVerified)return "unsafe survival handoff context";
        if(sample.PlayerHealth is not {Known:true,Dead:false})return "player health unknown or dead";
        if(!sample.SkillReady)return "self-heal no longer ready or HP/MP condition changed";
        if(!StationaryReturnDefense.NearAnchor(sample.Position,sample.Anchor))return "outside saved-anchor leash";
        if(!double.IsFinite(sample.Range) || sample.Range<=0)return "invalid attack range";
        if(!sample.Owned)return "no verified owned target";
        if(sample.Target is not {Monster:true,Targetable:true,PriorityLootObject:false} target ||
            !target.Position.Finite || !Targeting.IsStationaryHuntTargetId(target.Id))return "target unavailable or ineligible";
        if(sample.TargetHealth is not {Known:true,Dead:false})return "target health unknown or dead";
        if((target.Position-sample.Position).Length>sample.Range+MaximumCorrection)return "owned target beyond bounded survival correction";
        return null;
    }

    static bool SameTarget(Entity first,Entity? next)=>next!=null && first.Id==next.Id &&
        first.Generation==next.Generation && first.Address==next.Address;

    internal static Entity? Choose(IEnumerable<Entity> fresh,IEnumerable<Entity> owned,
        IReadOnlyDictionary<uint,Health> health,Vec position,double range,Func<Entity,bool> approved)=>
        ArrivedCombatHandoff.Choose(fresh,owned,health,position,range+MaximumCorrection,approved);

    // The movement layer supplies a conservative physical pulse endpoint,
    // not just the tiny requested goal. Preserve its existing obstacle/route
    // predicate and reject the complete envelope before any W-down.
    internal static Func<Vec,Vec,bool> BoundAdvance(Func<Vec,Vec,bool>? previous,Vec origin)=>
        (from,to)=>origin.Finite && from.Finite && to.Finite &&
            (from-origin).Length<=MaximumCorrection && (to-origin).Length<=MaximumCorrection &&
            previous?.Invoke(from,to)==true;

    // Movement and activation use the same freshly injected observation guard.
    // The caller also retains its normal input preflight and collision checks.
    internal async Task<SurvivalHandoffOutcome> RunAsync(Func<Observation> observe,
        Func<Vec,CancellationToken,Task> move,Action stopMovement,
        Func<Func<bool>,CancellationToken,Task> cast,Func<int,CancellationToken,Task> delay,
        Func<long> clock,CancellationToken token,Action<string,Observation>? blocked=null)
    {
        MovedLastRun=false;
        token.ThrowIfCancellationRequested();
        var initial=observe();var reason=Blocked(initial);
        if(reason!=null){blocked?.Invoke(reason,initial);return SurvivalHandoffOutcome.Unavailable;}
        var expected=initial.Target!;long started=clock(),previous=started,progressAt=started;
        if(started<0)throw new InvalidOperationException("Survival handoff clock is unavailable.");
        using var handoffBudget=CancellationTokenSource.CreateLinkedTokenSource(token);
        handoffBudget.CancelAfter(MaximumMilliseconds);
        var boundedToken=handoffBudget.Token;
        var origin=initial.Position;double bestDistance=(expected.Position-origin).Length;
        bool moved=false;
        bool Validate(Observation sample)
        {
            string? rejected=Blocked(sample);
            if(rejected==null && (!SameTarget(expected,sample.Target) || sample.Anchor!=initial.Anchor ||
                sample.RecoveryEpisode!=initial.RecoveryEpisode))rejected="target identity or saved return changed";
            if(rejected==null && (sample.Position-origin).Length>MaximumCorrection+StationaryMeleeAssist.ArrivalTolerance)
                rejected="bounded survival displacement exceeded";
            if(rejected!=null)blocked?.Invoke(rejected,sample);
            return rejected==null;
        }
        long Now()
        {
            long now=clock();if(now<previous)throw new InvalidOperationException("Survival handoff clock moved backward.");
            previous=now;return now;
        }
        bool ActivationReady()
        {
            boundedToken.ThrowIfCancellationRequested();
            var sample=observe();return Now()-started<MaximumMilliseconds && Validate(sample) &&
                (sample.Target!.Position-sample.Position).Length<=sample.Range;
        }
        try
        {
            while(true)
            {
                boundedToken.ThrowIfCancellationRequested();
                long now=Now();var sample=observe();
                if(!Validate(sample))return SurvivalHandoffOutcome.Yielded;
                double distance=(sample.Target!.Position-sample.Position).Length;
                if(now-started>=MaximumMilliseconds)return SurvivalHandoffOutcome.DurationExpired;
                if(distance<bestDistance-.01){bestDistance=distance;progressAt=now;}
                if(now-progressAt>=NoProgressMilliseconds)return SurvivalHandoffOutcome.NoProgress;
                if(distance<=sample.Range)
                {
                    stopMovement();
                    if(moved)
                    {
                        var released=sample.Position;
                        await delay(120,boundedToken);
                        var settled=observe();
                        if(!Validate(settled))return SurvivalHandoffOutcome.Yielded;
                        if((settled.Position-released).Length>StationaryMeleeAssist.ArrivalTolerance)
                        {await delay(25,boundedToken);continue;}
                    }
                    // Admission is rechecked by the injected cast after key
                    // selection and input preflight, including its fallback.
                    if(!ActivationReady())return SurvivalHandoffOutcome.Yielded;
                    await cast(ActivationReady,boundedToken);
                    return SurvivalHandoffOutcome.CastAttempted;
                }
                if(correctionConsumed && !moved)
                {blocked?.Invoke("survival correction budget already consumed",sample);return SurvivalHandoffOutcome.Unavailable;}
                if(!StationaryMeleeAssist.TryGoal(sample.Position,sample.Target.Position,sample.Anchor,sample.Range,out var goal) ||
                    (goal-origin).Length>MaximumCorrection)
                {blocked?.Invoke("no safe bounded survival goal",sample);return SurvivalHandoffOutcome.Yielded;}
                // Consume before the first await. A target/focus/death failure
                // after a requested move must never grant a replacement budget.
                correctionConsumed=true;moved=MovedLastRun=true;
                if(!Validate(observe()))return SurvivalHandoffOutcome.Yielded;
                await move(goal,boundedToken);
                stopMovement();
                await delay(25,boundedToken);
            }
        }
        catch(OperationCanceledException ex) when(handoffBudget.IsCancellationRequested &&
            !token.IsCancellationRequested && ex.CancellationToken==boundedToken)
        {return SurvivalHandoffOutcome.DurationExpired;}
        finally {stopMovement();}
    }
}

internal sealed class SelfHealBlockTrace
{
    readonly Dictionary<string,(string Reason,long At)> slots=new();
    internal bool ShouldRecord(string key,string reason,long now)
    {
        if(slots.TryGetValue(key,out var last) && last.Reason==reason && now-last.At<2000)return false;
        if(!slots.ContainsKey(key) && slots.Count>=10)slots.Remove(slots.Keys.First());
        slots[key]=(reason,now);return true;
    }
    internal void Clear(string key)=>slots.Remove(key);
}
