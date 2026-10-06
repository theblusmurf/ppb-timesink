namespace PoteHunter;

public sealed record RouteObstacle(Vec Center, double Radius, string Reason, Vec[]? Polygon = null);

public static class RoutePlanner
{
    static readonly (int X, int Y, double Cost)[] Neighbors =
    [
        (-1, 0, 1), (0, -1, 1), (0, 1, 1), (1, 0, 1),
        (-1, -1, Math.Sqrt(2)), (-1, 1, Math.Sqrt(2)), (1, -1, Math.Sqrt(2)), (1, 1, Math.Sqrt(2))
    ];

    public static List<Vec>? Plan(Vec start, Vec goal, Vec anchor, double huntRadius,
        IReadOnlyList<RouteObstacle> obstacles, int maxExpanded = 12000)
    {
        ArgumentNullException.ThrowIfNull(obstacles);
        if (!start.Finite || !goal.Finite || !anchor.Finite || !double.IsFinite(huntRadius) || huntRadius <= 0 || maxExpanded <= 0)
            return null;
        if (obstacles.Any(obstacle => obstacle == null || !obstacle.Center.Finite || !double.IsFinite(obstacle.Radius) || obstacle.Radius < 0 ||
            obstacle.Polygon is {} polygon && (polygon.Length<3||polygon.Any(point=>!point.Finite))))
            return null;
        // Every A* point stays inside this boundary. Distant map geometry cannot intersect a valid step.
        obstacles=obstacles.Where(o=>o.Polygon is {Length:>=3} p
            ? p.Min(v=>v.X)<=anchor.X+huntRadius&&p.Max(v=>v.X)>=anchor.X-huntRadius&&p.Min(v=>v.Y)<=anchor.Y+huntRadius&&p.Max(v=>v.Y)>=anchor.Y-huntRadius
            : Math.Abs(o.Center.X-anchor.X)<=huntRadius+o.Radius&&Math.Abs(o.Center.Y-anchor.Y)<=huntRadius+o.Radius).ToArray();
        if (!WithinHunt(start, anchor, huntRadius) || !WithinHunt(goal, anchor, huntRadius) ||
            !PointClear(start, obstacles) || !PointClear(goal, obstacles)) return null;
        if (SegmentClear(start, goal, obstacles)) return [goal];

        var origin = new Node(0, 0);
        var costs = new Dictionary<Node, double> { [origin] = 0 };
        var previous = new Dictionary<Node, Node>();
        var open = new PriorityQueue<OpenEntry, (double F, double H, long Sequence)>();
        long sequence = 0;
        double startHeuristic = (goal - start).Length;
        open.Enqueue(new(origin, 0), (startHeuristic, startHeuristic, sequence++));
        Node? reached = null;
        int expanded = 0;

        while (open.Count > 0 && expanded < maxExpanded)
        {
            OpenEntry entry = open.Dequeue();
            if (!costs.TryGetValue(entry.Node, out double best) || entry.Cost > best + 1e-9) continue;
            expanded++;
            Vec current = Position(start, entry.Node);
            if ((goal - current).Length <= Math.Sqrt(2) + 1e-9 && SegmentClear(current, goal, obstacles))
            {
                reached = entry.Node;
                break;
            }

            foreach (var neighbor in Neighbors)
            {
                var next = new Node(entry.Node.X + neighbor.X, entry.Node.Y + neighbor.Y);
                Vec nextPosition = Position(start, next);
                if (!LegalPoint(nextPosition, anchor, huntRadius, obstacles) || !SegmentClear(current, nextPosition, obstacles)) continue;
                if (neighbor.X != 0 && neighbor.Y != 0)
                {
                    Vec sideX = Position(start, new Node(entry.Node.X + neighbor.X, entry.Node.Y));
                    Vec sideY = Position(start, new Node(entry.Node.X, entry.Node.Y + neighbor.Y));
                    if (!LegalPoint(sideX, anchor, huntRadius, obstacles) || !LegalPoint(sideY, anchor, huntRadius, obstacles) ||
                        !SegmentClear(current, sideX, obstacles) || !SegmentClear(current, sideY, obstacles)) continue;
                }

                double nextCost = best + neighbor.Cost;
                if (costs.TryGetValue(next, out double known) && nextCost >= known - 1e-9) continue;
                costs[next] = nextCost;
                previous[next] = entry.Node;
                double heuristic = (goal - nextPosition).Length;
                open.Enqueue(new(next, nextCost), (nextCost + heuristic, heuristic, sequence++));
            }
        }
        if (reached == null) return null;

        var raw = new List<Vec> { goal };
        Node cursor = reached.Value;
        while (cursor != origin)
        {
            raw.Add(Position(start, cursor));
            cursor = previous[cursor];
        }
        raw.Add(start);
        raw.Reverse();
        return Smooth(raw, anchor, huntRadius, obstacles);
    }

    public static bool SegmentClear(Vec a, Vec b, IReadOnlyList<RouteObstacle> obstacles)
    {
        if (obstacles == null || !a.Finite || !b.Finite) return false;
        Vec segment = b - a;
        double lengthSquared = segment.X * segment.X + segment.Y * segment.Y;
        foreach (var obstacle in obstacles)
        {
            if (obstacle == null || !obstacle.Center.Finite || !double.IsFinite(obstacle.Radius) || obstacle.Radius < 0 ||
                obstacle.Polygon is { } polygon && (polygon.Length < 3 || polygon.Any(point=>!point.Finite))) return false;
            if(obstacle.Polygon is {Length:>=3} shape)
            {
                if(Inside(a,shape)||Inside(b,shape))return false;
                for(int i=0;i<shape.Length;i++)if(Intersects(a,b,shape[i],shape[(i+1)%shape.Length]))return false;
                continue;
            }
            Vec fromStart = obstacle.Center - a;
            double t = lengthSquared <= 1e-12 ? 0 : Math.Clamp((fromStart.X * segment.X + fromStart.Y * segment.Y) / lengthSquared, 0, 1);
            Vec closest = a + segment * t;
            Vec delta = closest - obstacle.Center;
            if (delta.X * delta.X + delta.Y * delta.Y <= obstacle.Radius * obstacle.Radius) return false;
        }
        return true;
    }

    static List<Vec> Smooth(IReadOnlyList<Vec> raw, Vec anchor, double huntRadius, IReadOnlyList<RouteObstacle> obstacles)
    {
        var result = new List<Vec>();
        int current = 0;
        while (current < raw.Count - 1)
        {
            int next = raw.Count - 1;
            while (next > current + 1 && (!WithinHunt(raw[next], anchor, huntRadius) || !SegmentClear(raw[current], raw[next], obstacles))) next--;
            if (!WithinHunt(raw[next], anchor, huntRadius) || !SegmentClear(raw[current], raw[next], obstacles)) return [];
            result.Add(raw[next]);
            current = next;
        }
        return result;
    }

    static bool LegalPoint(Vec point, Vec anchor, double huntRadius, IReadOnlyList<RouteObstacle> obstacles) =>
        point.Finite && WithinHunt(point, anchor, huntRadius) && PointClear(point, obstacles);

    static bool WithinHunt(Vec point, Vec anchor, double radius) => (point - anchor).Length <= radius + 1e-9;

    static bool PointClear(Vec point, IReadOnlyList<RouteObstacle> obstacles)
    {
        foreach (var obstacle in obstacles)
        {
            if(obstacle.Polygon is {Length:>=3} polygon)
            {
                if(Inside(point,polygon))return false;
                continue;
            }
            Vec delta = point - obstacle.Center;
            if (delta.X * delta.X + delta.Y * delta.Y <= obstacle.Radius * obstacle.Radius) return false;
        }
        return true;
    }

    static bool Inside(Vec point,IReadOnlyList<Vec> polygon)
    {
        bool inside=false;
        for(int i=0,j=polygon.Count-1;i<polygon.Count;j=i++)
        {
            Vec a=polygon[j],b=polygon[i];
            if(DistanceToSegmentSquared(point,a,b)<=1e-12)return true;
            if((a.Y>point.Y)!=(b.Y>point.Y)&&point.X<(b.X-a.X)*(point.Y-a.Y)/(b.Y-a.Y)+a.X)inside=!inside;
        }
        return inside;
    }
    static bool Intersects(Vec a,Vec b,Vec c,Vec d)
    {
        static double Cross(Vec u,Vec v,Vec p)=>(v.X-u.X)*(p.Y-u.Y)-(v.Y-u.Y)*(p.X-u.X);
        double abC=Cross(a,b,c),abD=Cross(a,b,d),cdA=Cross(c,d,a),cdB=Cross(c,d,b);
        return abC*abD<=1e-12&&cdA*cdB<=1e-12&&
            Math.Max(Math.Min(a.X,b.X),Math.Min(c.X,d.X))<=Math.Min(Math.Max(a.X,b.X),Math.Max(c.X,d.X))+1e-9&&
            Math.Max(Math.Min(a.Y,b.Y),Math.Min(c.Y,d.Y))<=Math.Min(Math.Max(a.Y,b.Y),Math.Max(c.Y,d.Y))+1e-9;
    }
    static double DistanceToSegmentSquared(Vec point,Vec a,Vec b)
    {
        Vec delta=b-a;double length=delta.X*delta.X+delta.Y*delta.Y;
        double t=length<=1e-12?0:Math.Clamp(((point.X-a.X)*delta.X+(point.Y-a.Y)*delta.Y)/length,0,1);
        Vec nearest=a+delta*t;double x=point.X-nearest.X,y=point.Y-nearest.Y;return x*x+y*y;
    }

    static Vec Position(Vec start, Node node) => new(start.X + node.X, start.Y + node.Y);
    readonly record struct Node(int X, int Y);
    readonly record struct OpenEntry(Node Node, double Cost);

    public static void SelfTest()
    {
        var none = Array.Empty<RouteObstacle>();
        var direct = Plan(new Vec(0, 0), new Vec(4.25, 1.5), new Vec(0, 0), 10, none);
        if (direct == null || direct.Count != 1 || direct[0] != new Vec(4.25, 1.5)) throw new Exception("Direct clear route was not returned exactly.");

        var circle = new[] { new RouteObstacle(new Vec(3, 0), 1.1, "circle") };
        var detour = Plan(new Vec(0, 0), new Vec(6, 0), new Vec(3, 0), 8, circle);
        if (detour == null || detour.Count < 2 || detour[^1] != new Vec(6, 0)) throw new Exception("Circle detour was not planned.");
        Vec prior = new(0, 0);
        foreach (Vec waypoint in detour)
        {
            if (!SegmentClear(prior, waypoint, circle)) throw new Exception("Smoothed route contains an obstructed segment.");
            prior = waypoint;
        }

        var wall = Enumerable.Range(-3, 7).Select(y => new RouteObstacle(new Vec(0, y), .75, "wall")).ToArray();
        if (Plan(new Vec(-2, 0), new Vec(2, 0), new Vec(0, 0), 3, wall, 2000) != null) throw new Exception("Impossible bounded route was accepted.");
        if (Plan(new Vec(0, 0), new Vec(6, 0), new Vec(0, 0), 5, none) != null) throw new Exception("Goal outside hunt boundary was accepted.");
        if (Plan(new Vec(0, 0), new Vec(3, 0), new Vec(0, 0), 5,
            [new RouteObstacle(new Vec(3, 0), .5, "goal")]) != null) throw new Exception("Blocked goal was accepted.");

        var corners = new[] { new RouteObstacle(new Vec(1, 0), .71, "east"), new RouteObstacle(new Vec(0, 1), .71, "north") };
        var aroundCorners = Plan(new Vec(0, 0), new Vec(2, 2), new Vec(0, 0), 6, corners);
        if (aroundCorners == null || aroundCorners[^1] != new Vec(2, 2) || !aroundCorners.All(point => point.Finite))
            throw new Exception("Legal route around blocked diagonal corners was not found.");
        prior = new Vec(0, 0);
        foreach (Vec waypoint in aroundCorners)
        {
            if (!SegmentClear(prior, waypoint, corners)) throw new Exception("Corner route cut through an obstacle.");
            prior = waypoint;
        }
        if (SegmentClear(new Vec(-2, 1), new Vec(2, 1), [new RouteObstacle(new Vec(0, 0), 1, "tangent")]))
            throw new Exception("Tangent obstacle contact must be blocked.");
        Vec[] wallShape=[new(-.25,-2),new(.25,-2),new(.25,2),new(-.25,2)];
        if(SegmentClear(new Vec(-2,0),new Vec(2,0),[new RouteObstacle(new(0,0),3,"collision wall",wallShape)])||
            !SegmentClear(new Vec(-2,2.5),new Vec(2,2.5),[new RouteObstacle(new(0,0),3,"collision wall",wallShape)]))
            throw new Exception("Imported collision footprints did not block crossing while allowing a route around them.");
    }
}
