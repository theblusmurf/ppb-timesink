using System.Text.Json;

namespace PoteHunter;

public sealed class MovementBlockedException(Vec position,Vec direction) : Exception("Forward movement made no progress")
{
    public Vec Position {get;}=position;
    public Vec Direction {get;}=direction;
}
public sealed class RouteUnavailableException(string message) : Exception(message);
public sealed record LearnedObstacle(Vec Center,double Radius,double Height,DateTime ExpiresUtc);
public sealed record SavedNavigationRoute(int Zone,Vec Anchor,double Heading,Vec[] Points,DateTime SavedUtc);

public sealed class Navigation
{
    public static string DefaultSavedRoutePath => Path.Combine(AppContext.BaseDirectory,"navigation-route.json");
    readonly List<LearnedObstacle> blocked=new();
    readonly List<Vec> trail=new();
    readonly List<Vec> recordingTrail=new();
    readonly List<Vec> route=new();
    string context="",goalKey="";
    Vec lastGoal,lastPosition;
    Vec recordingAnchor;
    bool hasPosition;
    bool recording;
    double height;
    int attempts;
    SavedNavigationRoute? savedRoute;
    public int RouteVersion {get;private set;}
    public string Status {get;private set;}="Recording observed movement";
    public IReadOnlyList<Vec> Trail=>trail;
    public IReadOnlyList<Vec> RecordingTrail=>recordingTrail;
    public IReadOnlyList<Vec> Route=>route;
    public IReadOnlyList<LearnedObstacle> Blocked=>blocked;
    public SavedNavigationRoute? SavedRoute=>savedRoute;
    public string Context=>context;
    public int RecoveryAttempts=>attempts;

    public void Observe(string newContext,Vec position,double newHeight)
    {
        if(!position.Finite || !double.IsFinite(newHeight)) return;
        if(context!=newContext || hasPosition && (position-lastPosition).Length>40)
        {
            Clear(); context=newContext; hasPosition=false;
        }
        height=newHeight;
        blocked.RemoveAll(o=>o.ExpiresUtc<=DateTime.UtcNow || Math.Abs(o.Height-height)<2 && (o.Center-position).Length<o.Radius+.05);
        if(!hasPosition || (position-lastPosition).Length>.3)
        {
            trail.Add(position); if(trail.Count>1000) trail.RemoveAt(0);
            if(recording && (!recordingTrail.Any() || (position-recordingTrail[^1]).Length>.3))
            {
                recordingTrail.Add(position); if(recordingTrail.Count>2000)recordingTrail.RemoveAt(0);
            }
            lastPosition=position; hasPosition=true;
        }
    }

    public void BeginRecording(Vec anchor)
    {
        recordingTrail.Clear(); recordingAnchor=anchor; recording=anchor.Finite;
        if(recording)recordingTrail.Add(anchor);
    }

    public void EndRecording()=>recording=false;

    public bool SaveCurrentRoute(int zone,Vec anchor,double heading,string? path=null)
    {
        if(!anchor.Finite || !double.IsFinite(heading))return false;
        var source=(recordingTrail.Count>1?recordingTrail:trail).Where(point=>point.Finite).ToList();
        if(source.Count<2)return false;
        var points=NormalizeRoute(source,anchor);
        if(points.Count<2)return false;
        savedRoute=new SavedNavigationRoute(zone,anchor,heading,points.ToArray(),DateTime.UtcNow);
        try
        {
            string destination=path??DefaultSavedRoutePath;
            File.WriteAllText(destination+".tmp",JsonSerializer.Serialize(savedRoute,new JsonSerializerOptions{WriteIndented=true}));
            File.Move(destination+".tmp",destination,true);
        }
        catch(IOException){return false;}
        catch(UnauthorizedAccessException){return false;}
        return true;
    }

    public bool LoadSavedRoute(int zone,string? path=null)
    {
        try
        {
            string source=path??DefaultSavedRoutePath;
            if(!File.Exists(source))return false;
            var loaded=JsonSerializer.Deserialize<SavedNavigationRoute>(File.ReadAllText(source));
            if(loaded is null || loaded.Zone!=zone || !loaded.Anchor.Finite || !double.IsFinite(loaded.Heading) ||
                loaded.Points is null || loaded.Points.Length<2 || loaded.Points.Any(point=>!point.Finite))return false;
            savedRoute=loaded with {Points=NormalizeRoute(loaded.Points,loaded.Anchor).ToArray()};
            return savedRoute.Points.Length>1;
        }
        catch(JsonException){return false;}
        catch(IOException){return false;}
        catch(UnauthorizedAccessException){return false;}
    }

    public void ClearSavedRoute(string? path=null)
    {
        savedRoute=null;
        try
        {
            string source=path??DefaultSavedRoutePath;
            if(File.Exists(source))File.Delete(source);
            if(File.Exists(source+".tmp"))File.Delete(source+".tmp");
        }
        catch(IOException){ }
        catch(UnauthorizedAccessException){ }
    }

    public bool TryGetRecoveryRoute(int zone,Vec current,Vec anchor,out IReadOnlyList<Vec> waypoints)
    {
        waypoints=[];
        var saved=savedRoute;
        if(saved is null || saved.Zone!=zone || !current.Finite || !anchor.Finite ||
            (saved.Anchor-anchor).Length>2.5 || saved.Points.Length<2)return false;
        int nearest=0;double nearestDistance=double.PositiveInfinity;
        for(int index=0;index<saved.Points.Length;index++)
        {
            double distance=(saved.Points[index]-current).Length;
            if(distance<nearestDistance){nearestDistance=distance;nearest=index;}
        }
        // A saved route is only useful when the respawn position is near the
        // recorded path. Otherwise the normal bounded planner is safer.
        if(!double.IsFinite(nearestDistance) || nearestDistance>20)return false;
        waypoints=saved.Points.Take(nearest+1).Reverse().Where(point=>(point-anchor).Length>.35).ToArray();
        return waypoints.Count>0;
    }

    public double SavedRouteRadius(Vec anchor)=>savedRoute is null || !anchor.Finite ? 0 :
        savedRoute.Points.Where(point=>point.Finite).Select(point=>(point-anchor).Length).DefaultIfEmpty(0).Max();

    static List<Vec> NormalizeRoute(IEnumerable<Vec> raw,Vec anchor)
    {
        var points=new List<Vec>();
        foreach(var point in raw)
        {
            if(!point.Finite)continue;
            if(points.Count==0 || (point-points[^1]).Length>.3)points.Add(point);
        }
        if(points.Count==0)return points;
        if((points[^1]-anchor).Length < (points[0]-anchor).Length)points.Reverse();
        if((points[0]-anchor).Length>.35)points.Insert(0,anchor);
        else points[0]=anchor;
        return points;
    }

    public void Clear()
    {
        blocked.Clear(); trail.Clear(); recordingTrail.Clear(); route.Clear(); goalKey=""; attempts=0; recording=false; RouteVersion++;
        Status="Observed map cleared";
    }
    public void BeginGoal(string key)
    {
        if(goalKey==key) return;
        goalKey=key; attempts=0; route.Clear(); RouteVersion++;
    }
    public void ShowRetreatRoute(Vec? waypoint,string status)
    {
        route.Clear();if(waypoint.HasValue)route.Add(waypoint.Value);
        Status=status;
    }
    public List<RouteObstacle> Obstacles(IEnumerable<AvoidZone> avoid)
    {
        blocked.RemoveAll(o=>o.ExpiresUtc<=DateTime.UtcNow);
        return avoid.Select(z=>new RouteObstacle(z.Center,z.Radius+1,z.Reason))
            .Concat(blocked.Where(o=>Math.Abs(o.Height-height)<2).Select(o=>new RouteObstacle(o.Center,o.Radius,"Observed blocked direction"))).ToList();
    }
    public bool CanAdvance(Vec from,Vec to,IEnumerable<AvoidZone> avoid) => RoutePlanner.SegmentClear(from,to,Obstacles(avoid));

    public Vec Waypoint(Vec position,Vec goal,Vec anchor,double huntRadius,IEnumerable<AvoidZone> avoid)
    {
        if(!position.Finite || !goal.Finite || !anchor.Finite || !double.IsFinite(huntRadius) || huntRadius<=0)
            throw new RouteUnavailableException("Invalid route boundary or position");
        if((goal-anchor).Length>huntRadius+1e-9)
        {
            route.Clear();RouteVersion++;Status="Destination is outside the movement boundary";
            throw new RouteUnavailableException(Status);
        }
        var obstacles=Obstacles(avoid);
        if(RoutePlanner.SegmentClear(position,goal,obstacles))
        {
            if(route.Count!=1) RouteVersion++;
            route.Clear(); route.Add(goal); lastGoal=goal; Status="Direct route"; return goal;
        }
        while(route.Count>0 && (route[0]-position).Length<.55) route.RemoveAt(0);
        bool valid=route.Count>0 && (lastGoal-goal).Length<=1.25;
        Vec previous=position;
        if(valid) foreach(var point in route)
        {
            if(!point.Finite || (point-anchor).Length>huntRadius+1e-9 || !RoutePlanner.SegmentClear(previous,point,obstacles)) {valid=false;break;}
            previous=point;
        }
        if(!valid)
        {
            var planned=RoutePlanner.Plan(position,goal,anchor,huntRadius,obstacles);
            route.Clear(); RouteVersion++;
            if(planned==null || planned.Count==0) {Status="No clear local route";throw new RouteUnavailableException(Status);}
            route.AddRange(planned); lastGoal=goal;
        }
        Status=$"Routing around obstacles · {route.Count} waypoint(s)";
        return route[0];
    }

    public void RecordBlock(Vec position,Vec forward,double currentHeight)
    {
        if(!position.Finite || !forward.Finite || forward.Length<.5 || !double.IsFinite(currentHeight))
            throw new RouteUnavailableException("Invalid blocked-movement observation");
        attempts++;
        if(attempts>4) throw new RouteUnavailableException("No route after four blocked approaches; target skipped temporarily");
        Vec center=position+forward/forward.Length*1.2;
        blocked.RemoveAll(o=>(o.Center-center).Length<.35 && Math.Abs(o.Height-currentHeight)<2);
        blocked.Add(new(center,.8,currentHeight,DateTime.UtcNow.AddSeconds(90)));
        if(blocked.Count>128) blocked.RemoveAt(0);
        route.Clear(); RouteVersion++;
        Status=$"Blocked direction recorded · trying detour {attempts}/4";
    }

    public object Snapshot() => new {Context,Status,RecoveryAttempts=attempts,BlockedAreas=blocked.ToArray(),Route=route.ToArray(),RecentTrail=trail.TakeLast(250).ToArray(),
        Recording=recording,RecordedPoints=recordingTrail.Count,SavedRoute=savedRoute is null?null:new {savedRoute.Zone,savedRoute.Anchor,savedRoute.Heading,savedRoute.SavedUtc,PointCount=savedRoute.Points.Length,Radius=SavedRouteRadius(savedRoute.Anchor)},
        Coverage="Observed local movement only; unknown ground is not verified walkable"};

    public static void SelfTest()
    {
        var nav=new Navigation(); nav.Observe("zone-session-1",new Vec(0,0),10); nav.BeginGoal("target");
        nav.RecordBlock(new Vec(0,0),new Vec(1,0),10);
        var waypoint=nav.Waypoint(new Vec(0,0),new Vec(6,0),new Vec(0,0),10,[]);
        if(waypoint==new Vec(6,0) || !RoutePlanner.SegmentClear(new Vec(0,0),waypoint,nav.Obstacles([]))) throw new Exception("A blocked direction did not produce a legal detour");
        nav.Observe("zone-session-2",new Vec(0,0),10);
        if(nav.Blocked.Count!=0 || nav.Route.Count!=0) throw new Exception("Navigation crossed scene/character contexts");
        nav.RecordBlock(new Vec(0,0),new Vec(1,0),10); nav.Observe("zone-session-2",new Vec(50,0),10);
        if(nav.Blocked.Count!=0) throw new Exception("Teleport did not clear unscoped observations");
        nav.RecordBlock(new Vec(50,0),new Vec(1,0),10);nav.Observe("zone-session-2",new Vec(51,0),10);
        if(nav.Blocked.Count!=0) throw new Exception("An observed occupied position remained marked blocked");

        var bounded=new Navigation();Vec anchor=new(0,0);
        bool rejected=false;
        try {bounded.Waypoint(anchor,new Vec(26.1,0),anchor,26,[]);} catch(RouteUnavailableException) {rejected=true;}
        if(!rejected || bounded.Route.Count!=0) throw new Exception("A clear direct route bypassed the movement boundary");
        if(bounded.Waypoint(new Vec(26.2,0),anchor,anchor,26,[])!=anchor)
            throw new Exception("A small boundary overshoot prevented a clear inward return");
        rejected=false;
        try {bounded.Waypoint(anchor,new Vec(1,0),anchor,double.NaN,[]);} catch(RouteUnavailableException) {rejected=true;}
        if(!rejected) throw new Exception("A nonfinite route boundary was accepted");

        var detour=new Navigation();Vec start=new(-2,0),goal=new(2,0);
        AvoidZone[] obstacle=[new(anchor,.6,"Fixture obstacle")];
        detour.Waypoint(start,goal,anchor,5,obstacle);
        if(!detour.Route.Any(point=>(point-anchor).Length>2.1)) throw new Exception("The cached boundary regression did not establish an outer detour");
        int version=detour.RouteVersion;
        try {detour.Waypoint(start,goal,anchor,2.1,obstacle);} catch(RouteUnavailableException) { }
        if(detour.RouteVersion==version || detour.Route.Any(point=>(point-anchor).Length>2.1))
            throw new Exception("A cached detour survived a smaller movement boundary");

        string selfTestRoute=Path.Combine(Path.GetTempPath(),"PoteHunter-navigation-self-test-route.json");
        var recorded=new Navigation();recorded.Observe("route",anchor,10);recorded.BeginRecording(anchor);recorded.Observe("route",anchor,10);recorded.Observe("route",new Vec(2,0),10);recorded.Observe("route",new Vec(4,0),10);
        if(!recorded.SaveCurrentRoute(7,anchor,1.25,selfTestRoute) || recorded.SavedRoute?.Points[0]!=anchor)
            throw new Exception("Navigation recording did not save an anchor-first route.");
        if(!recorded.TryGetRecoveryRoute(7,new Vec(4,0),anchor,out var recovery) || recovery.Count==0 || recovery[^1]!=new Vec(2,0))
            throw new Exception("Saved navigation route did not reverse toward the anchor.");
        recorded.ClearSavedRoute(selfTestRoute);
    }
}
