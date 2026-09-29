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
public sealed record SavedNavigationRouteSet(SavedNavigationRoute?[] Routes);

public sealed class Navigation
{
    public const int SavedRouteSlotCount = 3;
    public static string DefaultSavedRoutesPath => Path.Combine(AppContext.BaseDirectory,"navigation-routes.json");
    // Release1.39 wrote a single route. Keep the old filename as a read-only
    // migration source so existing death-recovery routes remain available.
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
    readonly SavedNavigationRoute?[] savedRoutes = new SavedNavigationRoute?[SavedRouteSlotCount];
    public int RouteVersion {get;private set;}
    public string Status {get;private set;}="Recording observed movement";
    public IReadOnlyList<Vec> Trail=>trail;
    public IReadOnlyList<Vec> RecordingTrail=>recordingTrail;
    public IReadOnlyList<Vec> Route=>route;
    public IReadOnlyList<LearnedObstacle> Blocked=>blocked;
    public IReadOnlyList<SavedNavigationRoute?> SavedRoutes=>savedRoutes;
    public SavedNavigationRoute? SavedRoute=>savedRoutes[0];
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

    public SavedNavigationRoute? GetSavedRoute(int slot) => slot is >= 0 and < SavedRouteSlotCount ? savedRoutes[slot] : null;
    public IEnumerable<(int Slot,SavedNavigationRoute Route)> SavedRoutesForZone(int zone) =>
        savedRoutes.Select((route,slot)=>new {route,slot})
            .Where(item=>item.route is not null && item.route.Zone==zone)
            .Select(item=>(item.slot,item.route!));

    public bool SaveCurrentRoute(int zone,Vec anchor,double heading,string? path=null) =>
        SaveCurrentRoute(zone,anchor,heading,0,path);

    public bool SaveCurrentRoute(int zone,Vec anchor,double heading,int slot,string? path=null)
    {
        if(slot is < 0 or >= SavedRouteSlotCount || !anchor.Finite || !double.IsFinite(heading))return false;
        var source=(recordingTrail.Count>0?recordingTrail:trail).Where(point=>point.Finite).ToList();
        // A route slot is also useful as a stationary anchor. Keep a single
        // point when the user saves before walking; navigation can still
        // travel directly to that anchor and occupancy detection can use it.
        if(source.Count==0)source.Add(anchor);
        var points=NormalizeRoute(source,anchor);
        if(points.Count<1)return false;
        var previous=savedRoutes[slot];
        savedRoutes[slot]=new SavedNavigationRoute(zone,anchor,heading,points.ToArray(),DateTime.UtcNow);
        try
        {
            string destination=path??DefaultSavedRoutesPath;
            File.WriteAllText(destination+".tmp",JsonSerializer.Serialize(new SavedNavigationRouteSet(savedRoutes),new JsonSerializerOptions{WriteIndented=true}));
            File.Move(destination+".tmp",destination,true);
            if(path==null && slot==0 && File.Exists(DefaultSavedRoutePath))File.Delete(DefaultSavedRoutePath);
        }
        catch(IOException){savedRoutes[slot]=previous;return false;}
        catch(UnauthorizedAccessException){savedRoutes[slot]=previous;return false;}
        return true;
    }

    public bool LoadSavedRoutes(string? path=null)
    {
        Array.Clear(savedRoutes,0,savedRoutes.Length);
        try
        {
            string source=path??DefaultSavedRoutesPath;
            if(path==null && !File.Exists(source))source=DefaultSavedRoutePath;
            if(!File.Exists(source))return false;
            string json=File.ReadAllText(source);
            SavedNavigationRoute?[]? loadedRoutes=null;
            try { loadedRoutes=JsonSerializer.Deserialize<SavedNavigationRouteSet>(json)?.Routes; }
            catch(JsonException) { }
            // Migrate the single-route Release1.39 shape into the primary slot.
            if(loadedRoutes==null)
            {
                var legacy=JsonSerializer.Deserialize<SavedNavigationRoute>(json);
                loadedRoutes=legacy==null ? Array.Empty<SavedNavigationRoute?>() : new SavedNavigationRoute?[]{legacy};
            }
            for(int slot=0;slot<Math.Min(SavedRouteSlotCount,loadedRoutes.Length);slot++)
            {
                var loaded=loadedRoutes[slot];
                if(loaded is null || !loaded.Anchor.Finite || !double.IsFinite(loaded.Heading) ||
                    loaded.Points is null || loaded.Points.Length<1 || loaded.Points.Any(point=>!point.Finite))continue;
                var normalized=NormalizeRoute(loaded.Points,loaded.Anchor);
                if(normalized.Count>0)savedRoutes[slot]=loaded with {Points=normalized.ToArray()};
            }
            return savedRoutes.Any(route=>route!=null);
        }
        catch(JsonException){return false;}
        catch(IOException){return false;}
        catch(UnauthorizedAccessException){return false;}
    }

    public bool LoadSavedRoute(int zone,string? path=null) =>
        LoadSavedRoutes(path) && savedRoutes.Any(route=>route?.Zone==zone);

    public void ClearSavedRoute(int slot,string? path=null)
    {
        if(slot is < 0 or >= SavedRouteSlotCount)return;
        savedRoutes[slot]=null;
        try
        {
            string source=path??DefaultSavedRoutesPath;
            if(savedRoutes.Any(route=>route!=null))
            {
                File.WriteAllText(source+".tmp",JsonSerializer.Serialize(new SavedNavigationRouteSet(savedRoutes),new JsonSerializerOptions{WriteIndented=true}));
                File.Move(source+".tmp",source,true);
            }
            else
            {
                if(File.Exists(source))File.Delete(source);
                if(path==null && File.Exists(DefaultSavedRoutePath))File.Delete(DefaultSavedRoutePath);
            }
            if(path==null && File.Exists(DefaultSavedRoutePath))File.Delete(DefaultSavedRoutePath);
        }
        catch(IOException){ }
        catch(UnauthorizedAccessException){ }
    }

    public void ClearSavedRoute(string? path=null)
    {
        Array.Clear(savedRoutes,0,savedRoutes.Length);
        try
        {
            string source=path??DefaultSavedRoutesPath;
            if(File.Exists(source))File.Delete(source);
            if(File.Exists(source+".tmp"))File.Delete(source+".tmp");
            if(path==null && File.Exists(DefaultSavedRoutePath))File.Delete(DefaultSavedRoutePath);
            if(path==null && File.Exists(DefaultSavedRoutePath+".tmp"))File.Delete(DefaultSavedRoutePath+".tmp");
        }
        catch(IOException){ }
        catch(UnauthorizedAccessException){ }
    }

    public bool TryGetRecoveryRoute(int zone,Vec current,Vec anchor,out IReadOnlyList<Vec> waypoints)
    {
        waypoints=[];
        var candidates=savedRoutes.Where(saved=>saved is not null && saved.Zone==zone && saved.Points.Length>=2 &&
            (saved.Anchor-anchor).Length<=2.5).Cast<SavedNavigationRoute>();
        foreach(var saved in candidates)
        {
            if(TryBuildRoute(saved,current,out waypoints))return true;
        }
        return false;
    }

    public bool TryGetRouteToSavedAnchor(int zone,Vec current,int slot,out IReadOnlyList<Vec> waypoints)
    {
        waypoints=[];
        var saved=GetSavedRoute(slot);
        return saved is not null && saved.Zone==zone && TryBuildRoute(saved,current,out waypoints);
    }

    static bool TryBuildRoute(SavedNavigationRoute saved,Vec current,out IReadOnlyList<Vec> waypoints)
    {
        waypoints=[];
        if(!current.Finite || !saved.Anchor.Finite || saved.Points.Length<1)return false;
        int nearest=0;double nearestDistance=double.PositiveInfinity;
        for(int index=0;index<saved.Points.Length;index++)
        {
            double distance=(saved.Points[index]-current).Length;
            if(distance<nearestDistance){nearestDistance=distance;nearest=index;}
        }
        // A saved route is only useful when the respawn position is near the
        // recorded path. Otherwise the normal bounded planner is safer.
        if(!double.IsFinite(nearestDistance) || nearestDistance>20)return false;
        waypoints=saved.Points.Take(nearest+1).Reverse().Where(point=>(point-saved.Anchor).Length>.35).ToArray();
        return waypoints.Count>0;
    }

    public double SavedRouteRadius(Vec anchor)=>SavedRouteRadius(0,anchor);
    public double SavedRouteRadius(int slot,Vec anchor)
    {
        var saved=GetSavedRoute(slot);
        return saved is null || !anchor.Finite ? 0 :
            saved.Points.Where(point=>point.Finite).Select(point=>(point-anchor).Length).DefaultIfEmpty(0).Max();
    }

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
        Recording=recording,RecordedPoints=recordingTrail.Count,SavedRoutes=savedRoutes.Select((saved,slot)=>saved is null?null:new {Slot=slot,saved.Zone,saved.Anchor,saved.Heading,saved.SavedUtc,PointCount=saved.Points.Length,Radius=SavedRouteRadius(slot,saved.Anchor)}).ToArray(),
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
        if(!recorded.SaveCurrentRoute(7,anchor,1.25,0,selfTestRoute) || recorded.SavedRoute?.Points[0]!=anchor)
            throw new Exception("Navigation recording did not save an anchor-first route.");
        if(!recorded.TryGetRecoveryRoute(7,new Vec(4,0),anchor,out var recovery) || recovery.Count==0 || recovery[^1]!=new Vec(2,0))
            throw new Exception("Saved navigation route did not reverse toward the anchor.");
        if(!recorded.SaveCurrentRoute(7,new Vec(4,0),1.5,1,selfTestRoute) ||
            !recorded.TryGetRouteToSavedAnchor(7,new Vec(0,0),1,out var alternate) || alternate.Count==0 || alternate[^1]!=new Vec(2,0))
            throw new Exception("Alternative navigation route did not reverse toward its saved anchor.");
        var stationary=new Navigation();
        if(!stationary.SaveCurrentRoute(7,new Vec(8,0),2,2,selfTestRoute) || stationary.GetSavedRoute(2)?.Points.Length!=1 ||
            !stationary.LoadSavedRoutes(selfTestRoute) || stationary.GetSavedRoute(2)?.Anchor!=new Vec(8,0))
            throw new Exception("A stationary route slot did not persist its anchor.");
        recorded.ClearSavedRoute(selfTestRoute);
    }
}
