namespace PoteHunter;

internal sealed record SavedTravel(Vec[] Points,Vec Destination);

internal static class RecoveryTravel
{
    internal const double DefaultStartupRadius=10;
    internal static (Vec Point,int Index,double Distance) Nearest(SavedNavigationRoute route,Vec current)
    {
        var best=(Point:route.Points[0],Index:0,Distance:(route.Points[0]-current).Length);
        for(int i=0;i<route.Points.Length-1;i++)
        {
            Vec a=route.Points[i],delta=route.Points[i+1]-a;
            double square=delta.X*delta.X+delta.Y*delta.Y;
            double fraction=square>0?Math.Clamp(((current.X-a.X)*delta.X+(current.Y-a.Y)*delta.Y)/square,0,1):0;
            Vec point=a+delta*fraction;double distance=(point-current).Length;
            if(distance<best.Distance)best=(point,i+1,distance);
        }
        return best;
    }
    internal static int StartupSlot(IReadOnlyList<SavedNavigationRoute?> routes,Vec current,int zone,string character,double height,int preferred,double corridorRadius=DefaultStartupRadius)
    {
        if(!current.Finite || !double.IsFinite(corridorRadius) || corridorRadius<=0)return -1;
        var candidates=Enumerable.Range(0,routes.Count).Where(i=>routes[i] is {} route && Recorded(route) &&
            RecoveryRouting.Compatible(route,zone,character,height)).Select(i=>(Slot:i,Distance:Nearest(routes[i]!,current).Distance))
            .Where(item=>item.Distance<=corridorRadius).ToArray();
        return candidates.OrderBy(item=>item.Slot==preferred?0:1).ThenBy(item=>item.Distance).Select(item=>item.Slot).DefaultIfEmpty(-1).First();
    }
    internal static bool Recorded(SavedNavigationRoute route)=>route.HasRecordedRoute && route.Points.Length<=2000 &&
        route.Points.All(p=>p.Finite) && (route.Points[0]-route.Anchor).Length<=.5 &&
        route.Points.Zip(route.Points.Skip(1)).All(p=>(p.First-p.Second).Length<=8.01);
    internal static bool SharedOrigin(SavedNavigationRoute a,SavedNavigationRoute b)=>Recorded(a) && Recorded(b) &&
        a.Zone==b.Zone && (a.RevivalOrigin-b.RevivalOrigin).Length<=3 &&
        (a.Height<=0 || b.Height<=0 || Math.Abs(a.Height-b.Height)<2);

    static IEnumerable<Vec> Connector(Vec from,Vec to)
    {
        int steps=Math.Max(1,(int)Math.Ceiling((to-from).Length/2));
        for(int i=1;i<=steps;i++)yield return from+(to-from)*((double)i/steps);
    }
    static double Length(Vec start,IEnumerable<Vec> points)
    {
        double length=0;foreach(var point in points){length+=(point-start).Length;start=point;}return length;
    }
    internal static SavedTravel Plan(IReadOnlyList<SavedNavigationRoute?> routes,SavedNavigationRoute target,Vec current,bool retreat,double joinRadius=20)
    {
        if(!current.Finite || !Recorded(target) || !double.IsFinite(joinRadius) || joinRadius<=0)throw new RouteUnavailableException("A valid recorded return route and join radius are required.");
        // Prefer the path we are actually standing on. Switching farms follows
        // that path back to the shared origin, then the destination's path.
        var entries=routes.Where(r=>r!=null && SharedOrigin(r,target) &&
            (string.IsNullOrWhiteSpace(r.Character) || string.IsNullOrWhiteSpace(target.Character) || r.Character.Equals(target.Character,StringComparison.OrdinalIgnoreCase)))
            .Cast<SavedNavigationRoute>().Select(route=>
            {
                var nearest=Nearest(route,current);
                return(Route:route,nearest.Point,nearest.Index,nearest.Distance);
            }).Where(e=>e.Distance<=joinRadius).ToArray();
        if(entries.Length==0)throw new RouteUnavailableException($"Character is more than {joinRadius:0.#} units from the compatible saved routes.");
        double closest=entries.Min(e=>e.Distance);
        var choices=new List<SavedTravel>();
        foreach(var entry in entries.Where(e=>e.Distance<=Math.Min(joinRadius,closest+.5)))
        {
            var points=Connector(current,entry.Point).ToList();
            if(!retreat && entry.Route==target)
                points.AddRange(target.Points.Take(entry.Index).Reverse());
            else
            {
                points.AddRange(entry.Route.Points.Skip(entry.Index));
                points.AddRange(Connector(entry.Route.RevivalOrigin,target.RevivalOrigin));
                if(!retreat)points.AddRange(target.Points.Reverse().Skip(1));
            }
            var destination=retreat?target.RevivalOrigin:target.Anchor;
            points.Add(destination);
            choices.Add(new(points.DistinctConsecutive().ToArray(),destination));
        }
        return choices.OrderBy(plan=>Length(current,plan.Points)).First();
    }
    static IEnumerable<Vec> DistinctConsecutive(this IEnumerable<Vec> points)
    {
        Vec? previous=null;foreach(var point in points)
            if(previous==null || (point-previous.Value).Length>.05){yield return point;previous=point;}
    }
}

internal sealed class RecoveryFallbackCycle(int startingSlot)
{
    public const long RetryMilliseconds=600000;
    readonly HashSet<int> rejected=[];
    long? lastWait;
    public long Remaining {get;private set;}
    public bool Waiting {get;private set;}
    public int StartingSlot {get;private set;}=startingSlot;
    public void Reject(int slot)=>rejected.Add(slot);
    public bool Rejected(int slot)=>rejected.Contains(slot);
    public IEnumerable<int> Candidates(int count)=>Enumerable.Range(0,count).Select(i=>(StartingSlot+i)%count).Where(i=>!rejected.Contains(i));
    public int Select(int count,Func<int,bool> compatible,Func<int,bool> occupied)=>Candidates(count).FirstOrDefault(s=>compatible(s)&&!occupied(s),-1);
    public void BeginWait(){if(Waiting)return;Waiting=true;Remaining=RetryMilliseconds;lastWait=null;}
    public bool Wait(long now)
    {
        if(lastWait is long before && now>=before && now-before<=2000)Remaining=Math.Max(0,Remaining-(now-before));
        lastWait=now;return Remaining==0;
    }
    public void Pause()=>lastWait=null;
    public void Restart(){rejected.Clear();Waiting=false;Remaining=0;lastWait=null;}
    public void Arrived(int slot){StartingSlot=slot;Restart();}
}

internal static class LeashReturn
{
    internal static bool ShouldReturn(bool group,bool engaged,bool priority,bool outsideTrip,double targetDistance,double huntRadius,double completionRadius)
        =>!group && engaged && double.IsFinite(targetDistance) &&
            targetDistance>(priority||outsideTrip?completionRadius:huntRadius);
    internal static async Task Run(Func<bool> arrived,Func<Task> advance,Func<Task> face,
        Func<bool> resume,Func<Task> wait,Func<long> now,CancellationToken token)
    {
        long deadline=now()+60000;
        while(!arrived())
        {
            token.ThrowIfCancellationRequested();
            if(now()>deadline)throw new RouteUnavailableException("Return to the anchor made no progress after the enemy left range.");
            await advance();
        }
        token.ThrowIfCancellationRequested();await face();
        while(!resume()){token.ThrowIfCancellationRequested();await wait();}
    }
}
