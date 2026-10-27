namespace PoteHunter;

// A settled physical return may hand an already owned pack back to ordinary
// combat before restoring saved facing. This owns no input or travel and never
// treats a nearby fresh spawn as an engagement. Post-loot callers may reserve
// one explicitly approved in-range fresh identity for ordinary combat instead.
// That reservation cannot select replacements or renew the facing allowance.
// Facing remains pending until
// the caller restores it, with one absolute allowance across target changes.
internal sealed class ArrivedCombatHandoff
{
    internal const int MaximumDeferralMilliseconds=120000;
    Vec anchor;
    double heading;
    long episode,startedAt,lastObservedAt;
    Entity? reservedFresh;
    internal bool Pending {get;private set;}
    internal bool IsFreshReservation(Entity target)=>Pending && reservedFresh is {} reserved &&
        target.Id==reserved.Id && target.Generation==reserved.Generation && target.Address==reserved.Address;

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

    internal bool TryDeferFresh(StationaryReturnDefense.Observation sample,bool settled,RestPosture posture,
        double savedHeading,long recoveryEpisode,long now)
    {
        // A fresh reservation is available once, only at a new post-loot
        // handoff. Existing owned combat must not become an endless spawn chain.
        if(Pending || !TryDefer(sample,settled,posture,savedHeading,recoveryEpisode,now))return false;
        reservedFresh=sample.Target;
        return true;
    }

    internal Entity? ChoosePending(IEnumerable<Entity> fresh,IEnumerable<Entity> owned,
        IReadOnlyDictionary<uint,Health> health,Vec position,double range,Func<Entity,bool> approved,
        Func<Entity,bool> freshApproved)
    {
        var observed=fresh.ToArray();
        return Choose(observed,owned,health,position,range,approved) ??
            (reservedFresh is {} reserved ? Choose(observed,[reserved],health,position,range,
                e=>approved(e)&&freshApproved(e)) : null);
    }

    internal static Entity? ChooseFresh(IEnumerable<Entity> fresh,IReadOnlyDictionary<uint,Health> health,
        Vec position,Vec anchor,double range,double huntRadius,Func<Entity,bool> approved)
    {
        if(!position.Finite || !anchor.Finite || (position-anchor).Length>NearbyLootPickup.AnchorArrivalTolerance ||
            !double.IsFinite(huntRadius) || huntRadius<=0)return null;
        var observed=fresh.ToArray();
        var ambiguous=observed.GroupBy(e=>e.Id)
            .Where(group=>group.Select(e=>(e.Generation,e.Address)).Distinct().Skip(1).Any())
            .Select(group=>group.Key).ToHashSet();
        if(!double.IsFinite(range) || range<=0)return null;
        // Keep ordinary fresh-target proximity order; highest-HP ordering is
        // reserved for finishing an already owned stationary pack.
        return observed.Where(e=>!ambiguous.Contains(e.Id) && e.Monster && e.Targetable && !e.PriorityLootObject &&
            Targeting.IsStationaryHuntTargetId(e.Id) && e.Position.Finite && health.GetValueOrDefault(e.Id) is {Known:true,Dead:false} &&
            (e.Position-position).Length<=range && (e.Position-anchor).Length<=huntRadius && approved(e))
            .OrderBy(e=>(e.Position-position).Length).ThenBy(e=>e.Id).FirstOrDefault();
    }

    internal void Reset(){Pending=false;reservedFresh=null;anchor=default;heading=0;episode=startedAt=lastObservedAt=0;}

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
