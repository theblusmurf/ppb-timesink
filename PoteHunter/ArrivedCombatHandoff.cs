namespace PoteHunter;

// A settled physical return may hand an already owned pack back to ordinary
// combat before restoring saved facing. This owns no input or travel and never
// treats a nearby fresh spawn as an engagement. Facing remains pending until
// the caller restores it, with one absolute allowance across target changes.
internal sealed class ArrivedCombatHandoff
{
    internal const int MaximumDeferralMilliseconds=120000;
    Vec anchor;
    double heading;
    long episode,startedAt,lastObservedAt;
    internal bool Pending {get;private set;}

    internal bool Matches(Vec currentAnchor,double currentHeading,long currentEpisode)=>
        Pending && anchor==currentAnchor && heading==currentHeading && episode==currentEpisode;

    void ObserveClock(long now)
    {
        if(now<0 || Pending && now<lastObservedAt)
            throw new InvalidOperationException("Arrived combat handoff clock is unavailable or moved backward.");
        lastObservedAt=now;
    }
    internal bool Expired(long now)
    {
        ObserveClock(now);
        return Pending && now-startedAt>=MaximumDeferralMilliseconds;
    }

    internal bool TryDefer(StationaryReturnDefense.Observation sample,bool settled,RestPosture posture,
        double savedHeading,long recoveryEpisode,long now)
    {
        ObserveClock(now);
        if(!settled || posture!=RestPosture.Standing || !double.IsFinite(savedHeading) ||
            !StationaryReturnDefense.CanDefend(sample) ||
            (sample.Position-sample.Anchor).Length>NearbyLootPickup.AnchorArrivalTolerance)return false;
        if(Pending && !Matches(sample.Anchor,savedHeading,recoveryEpisode))Reset();
        if(!Pending)
        {
            anchor=sample.Anchor;heading=savedHeading;episode=recoveryEpisode;
            startedAt=lastObservedAt=now;Pending=true;
        }
        return !Expired(now);
    }

    internal void Reset(){Pending=false;anchor=default;heading=0;episode=startedAt=lastObservedAt=0;}

    // Completing this same repaired return advances DeathRecovery's epoch.
    // Transfer only that confirmed completion, without granting another lease.
    internal void CompleteRecovery(Vec currentAnchor,double currentHeading,long completedEpisode,long nextEpisode)
    {
        if(Matches(currentAnchor,currentHeading,completedEpisode))episode=nextEpisode;
    }

    internal static Entity? Choose(IEnumerable<Entity> fresh,IEnumerable<Entity> owned,
        IReadOnlyDictionary<uint,Health> health,Vec position,double range,Func<Entity,bool>? approved=null)
    {
        var identities=owned.Select(e=>(e.Id,e.Generation,e.Address)).ToHashSet();
        var observed=fresh.ToArray();
        var ambiguous=observed.GroupBy(e=>e.Id)
            .Where(group=>group.Select(e=>(e.Generation,e.Address)).Distinct().Skip(1).Any())
            .Select(group=>group.Key).ToHashSet();
        return Targeting.ChooseStationaryEngaged(observed.Where(e=>!ambiguous.Contains(e.Id) &&
            identities.Contains((e.Id,e.Generation,e.Address)) && (approved?.Invoke(e)??true)),health,position,range);
    }
}
