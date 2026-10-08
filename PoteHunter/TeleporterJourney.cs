namespace PoteHunter;

// Walking legs retain the ordinary path schema. The portal jump is never a
// waypoint, a collision-tested segment, or an implied reverse connection.
internal sealed record TeleporterJourney(SavedNavigationRoute DepartureLeg,SavedNavigationRoute LandingLeg)
{
    static IEnumerable<Vec> Connector(Vec from,Vec to)
    {
        int steps=Math.Max(1,(int)Math.Ceiling((to-from).Length/2));
        for(int i=1;i<=steps;i++)yield return from+(to-from)*(i/(double)steps);
    }
    static Vec[] Clean(IEnumerable<Vec> points)
    {
        var result=new List<Vec>();
        foreach(var point in points)if(result.Count==0||(point-result[^1]).Length>.01)result.Add(point);
        return result.ToArray();
    }
    static double Along(SavedNavigationRoute route,(Vec Point,int Index,double Distance) nearest)
    {
        double distance=0;Vec previous=route.Anchor;
        foreach(var point in route.Points.Take(nearest.Index)){distance+=(point-previous).Length;previous=point;}
        return distance+(nearest.Point-previous).Length;
    }
    public static TeleporterJourney Create(SavedNavigationRoute route,TeleporterProfile profile,double corridor)
    {
        profile.Validate(profile.ClientHash,new(profile.Width,profile.Height));
        if(!RecoveryTravel.Recorded(route)||!RecoveryRouting.CompatibleIdentity(route,profile.Landing.Zone,profile.Character)||
            !double.IsFinite(corridor)||corridor<=0)
            throw new RouteUnavailableException("The teleporter needs a compatible recorded walking route.");
        var source=RecoveryTravel.Nearest(route,profile.Departure.Position);
        var landing=RecoveryTravel.Nearest(route,profile.Landing.Position);
        if(source.Distance>Math.Min(corridor,TeleporterProfile.SourceRadius)||landing.Distance>Math.Min(corridor,TeleporterProfile.LandingRadius)||
            Along(route,source)<=Along(route,landing)+10)
            throw new RouteUnavailableException("Record a route containing the departure before the landing, then the anchor. Both portal ends must be close to that route.");
        var prefix=Clean(new[]{profile.Departure.Position}.Concat(Connector(profile.Departure.Position,source.Point)).Concat(route.Points.Skip(source.Index)));
        var suffix=Clean(route.Points.Take(landing.Index).Concat(new[]{landing.Point}).Concat(Connector(landing.Point,profile.Landing.Position)));
        var journey=new TeleporterJourney(route with{Anchor=profile.Departure.Position,Height=profile.Departure.Height,Points=prefix},route with{Points=suffix});
        if(!ValidLeg(journey.DepartureLeg)||!ValidLeg(journey.LandingLeg))
            throw new RouteUnavailableException("The teleporter walking legs are incomplete or too long. Record shorter continuous legs.");
        return journey;
    }
    static bool ValidLeg(SavedNavigationRoute route)=>RecoveryTravel.Recorded(route)||
        route.Points is {Length:1} && route.Points[0].Finite && (route.Points[0]-route.Anchor).Length<=.01;
    public static SavedTravel PlanLeg(SavedNavigationRoute route,Vec current,double corridor)
    {
        if(route.Points.Length!=1)return RecoveryTravel.Plan([route],route,current,false,corridor);
        if(!ValidLeg(route)||!current.Finite||!double.IsFinite(corridor)||corridor<=0||(current-route.Anchor).Length>corridor)
            throw new RouteUnavailableException("Character is outside the calibrated portal endpoint corridor.");
        return new(Clean(Connector(current,route.Anchor)),route.Anchor);
    }
    public bool NeedsTeleport(Vec current,double corridor)=>
        RecoveryTravel.Nearest(DepartureLeg,current).Distance<=corridor &&
        RecoveryTravel.Nearest(DepartureLeg,current).Distance<=RecoveryTravel.Nearest(LandingLeg,current).Distance;

    public static void Checks()
    {
        // Recognition/persistence/identity boundary checks live in TeleporterChecks.
        var points=Enumerable.Range(0,51).Select(i=>new Vec(100-i*2,0)).ToArray();
        var route=new SavedNavigationRoute(8,new(100,0),0,points,DateTime.UnixEpoch,"Fixture",4);
        // Test the walking split directly: neither leg contains the omitted gap.
        var nearest=RecoveryTravel.Nearest(route,new(30,0));
        var prefix=route with{Anchor=new(30,0),Points=Clean(new[]{new Vec(30,0)}.Concat(route.Points.Skip(nearest.Index)))};
        var tail=route with{Points=Clean(route.Points.Take(RecoveryTravel.Nearest(route,new(80,0)).Index).Concat(new[]{new Vec(80,0)}))};
        if(!RecoveryTravel.Recorded(prefix)||!RecoveryTravel.Recorded(tail)||prefix.RevivalOrigin!=new Vec(0,0)||tail.RevivalOrigin!=new Vec(80,0))
            throw new Exception("Teleporter walking legs did not retain continuous departure/landing origins.");
        var walk=RecoveryTravel.Plan([tail],tail,new(80,0),false,3);
        if(walk.Points.Any(p=>p.X<80)||walk.Destination!=route.Anchor)
            throw new Exception("Teleporter landing continuation backtracked across the omitted portal gap.");
    }
}
