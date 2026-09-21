namespace PoteHunter;

public sealed class MovementBlockedException(Vec position,Vec direction) : Exception("Forward movement made no progress")
{
    public Vec Position {get;}=position;
    public Vec Direction {get;}=direction;
}
public sealed class RouteUnavailableException(string message) : Exception(message);
public sealed record LearnedObstacle(Vec Center,double Radius,double Height,DateTime ExpiresUtc);

public sealed class Navigation
{
    readonly List<LearnedObstacle> blocked=new();
    readonly List<Vec> trail=new();
    readonly List<Vec> route=new();
    string context="",goalKey="";
    Vec lastGoal,lastPosition;
    bool hasPosition;
    double height;
    int attempts;
    public int RouteVersion {get;private set;}
    public string Status {get;private set;}="Recording observed movement";
    public IReadOnlyList<Vec> Trail=>trail;
    public IReadOnlyList<Vec> Route=>route;
    public IReadOnlyList<LearnedObstacle> Blocked=>blocked;
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
            lastPosition=position; hasPosition=true;
        }
    }

    public void Clear()
    {
        blocked.Clear(); trail.Clear(); route.Clear(); goalKey=""; attempts=0; RouteVersion++;
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
    }
}
