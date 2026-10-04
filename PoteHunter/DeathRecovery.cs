namespace PoteHunter;

// A positive HP sample confirms revival, but only arrival completes recovery.
internal sealed class DeathRecoveryState
{
    public bool Pending { get; private set; }
    public long ObservedAt { get; private set; }
    bool dead;

    public bool Observe(Health health,long now)
    {
        if(!health.Known)return false;
        if(!health.Dead){dead=false;return false;}
        Pending=true;
        if(dead)return false;
        dead=true;ObservedAt=now;
        return true;
    }

    public long ReadyAt(int delaySeconds,bool visual=false)=>ObservedAt+
        Math.Max(visual?VisualRevival.DeathWaitMilliseconds:500,Math.Clamp(delaySeconds,0,600)*1000L);
    public static void InterruptIfDead(Health health,bool enabled,Action<Health> observeDeath)
    {
        if(!enabled || !health.Dead)return;
        observeDeath(health);
        throw new DeathRecoveryRequiredException();
    }
    public void Reset(){Pending=false;dead=false;ObservedAt=0;}
}

internal sealed class DeathRecoveryRequiredException : Exception;

internal static class RecoveryRouting
{
    public static string? SavedReturnProblem(SavedNavigationRoute? route,int zone,string character,double height,Vec anchor)
    {
        if(route==null || !RecoveryTravel.Recorded(route))return "Record a return route with Home / End in Navigation, or turn off Auto revive + return.";
        if(!Compatible(route,zone,character,height) || (route.Anchor-anchor).Length>2.5)
            return "The recorded route does not match this character, map, or anchor. Select the saved spot before starting.";
        return null;
    }
    // The client may recreate the local body on revival. Retain account/body
    // identity checks while allowing its allocation and generation to change.
    public static bool SameCharacter(Entity before,Entity after)=>
        before.Id==after.Id && before.Name==after.Name && before.Model==after.Model;

    // Height describes the saved farming endpoint, not the elevation of every
    // recorded path segment. Travel can cross hills before reaching that floor.
    public static bool CompatibleIdentity(SavedNavigationRoute route,int zone,string character)=>
        route.Zone==zone && route.Anchor.Finite && double.IsFinite(route.Heading) &&
        (string.IsNullOrWhiteSpace(route.Character) || route.Character.Equals(character,StringComparison.OrdinalIgnoreCase));

    public static bool Compatible(SavedNavigationRoute route,int zone,string character,double huntHeight)=>
        CompatibleIdentity(route,zone,character) && double.IsFinite(route.Height) && double.IsFinite(huntHeight) &&
        (route.Height<=0 || huntHeight<=0 || Math.Abs(route.Height-huntHeight)<2);

    public static bool Occupied(Vec anchor,double height,double radius,IEnumerable<Entity> entities,uint selfId)=>
        CombatCourtesy.PlayerNear(anchor,entities.Where(e=>height<=0 || e.Height<=0 || Math.Abs(e.Height-height)<2),
            selfId,Math.Max(3,radius))!=null;

}

internal sealed class RecoveryPath
{
    readonly Vec[] points;
    int index;
    public int Index=>index;
    public RecoveryPath(IEnumerable<Vec> waypoints,Vec anchor)
    {
        points=waypoints.Append(anchor).ToArray();
        if(points.Any(p=>!p.Finite))throw new RouteUnavailableException("Recovery route contains an invalid position.");
    }
    // A clear, short lookahead avoids turning sideways just to touch the
    // projected route entry. Sharp corners and the final anchor stay exact.
    public bool Final=>index==points.Length-1;
    bool Straight(int at)
    {
        if(at+2>=points.Length)return true;
        Vec outgoing=points[at+1]-points[at],following=points[at+2]-points[at+1];
        double lengths=outgoing.Length*following.Length;
        if(lengths<=.0001)return true;
        return (outgoing.X*following.X+outgoing.Y*following.Y)/lengths>=.94;
    }
    public Vec? Next(Vec current,Func<Vec,Vec,bool>? clear=null)
    {
        if(!current.Finite)throw new RouteUnavailableException("Character position is unavailable during recovery.");
        // Intermediate waypoints are sampled closely together. Never advance
        // merely because one frame of movement was sent to the client.
        while(index<points.Length && (points[index]-current).Length<=(index==points.Length-1?.5:.6))index++;
        // Advance only locally, with an independently checked segment. Never
        // search globally for a closer point on a loop or skip a sharp bend.
        if(clear!=null)
            while(index<points.Length-1 && (points[index]-current).Length<=1.25 &&
                (points[index+1]-current).Length<=4 &&
                (index==0 || Straight(index-1)) && Straight(index) && clear(current,points[index+1]))index++;
        return index<points.Length?points[index]:null;
    }
}
