namespace PoteHunter;

internal sealed record SavedTravel(Vec[] Points,Vec Destination);

internal static class RecoveryTravel
{
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
    internal static SavedTravel Plan(IReadOnlyList<SavedNavigationRoute?> routes,SavedNavigationRoute target,Vec current,bool retreat)
    {
        if(!current.Finite || !Recorded(target))throw new RouteUnavailableException("A valid recorded return route is required.");
        // Prefer the path we are actually standing on. Switching farms follows
        // that path back to the shared origin, then the destination's path.
        var entries=routes.Where(r=>r!=null && SharedOrigin(r,target) &&
            (string.IsNullOrWhiteSpace(r.Character) || string.IsNullOrWhiteSpace(target.Character) || r.Character.Equals(target.Character,StringComparison.OrdinalIgnoreCase)))
            .Cast<SavedNavigationRoute>().Select(route=>
            {
                var nearest=route.Points.Select((p,i)=>(Point:p,Index:i,Distance:(p-current).Length)).OrderBy(p=>p.Distance).First();
                return(Route:route,nearest.Index,nearest.Distance);
            }).Where(e=>e.Distance<=20).ToArray();
        if(entries.Length==0)throw new RouteUnavailableException("Character is more than 20 units from the compatible saved routes.");
        double closest=entries.Min(e=>e.Distance);
        var choices=new List<SavedTravel>();
        foreach(var entry in entries.Where(e=>e.Distance<=Math.Min(20,closest+.5)))
        {
            var points=Connector(current,entry.Route.Points[entry.Index]).ToList();
            if(!retreat && entry.Route==target)
                points.AddRange(target.Points.Take(entry.Index).Reverse());
            else
            {
                points.AddRange(entry.Route.Points.Skip(entry.Index+1));
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
