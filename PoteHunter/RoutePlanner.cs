namespace PoteHunter;

public sealed record RouteObstacle(Vec Center, double Radius, string Reason);

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
        if (obstacles.Any(obstacle => obstacle == null || !obstacle.Center.Finite || !double.IsFinite(obstacle.Radius) || obstacle.Radius < 0))
            return null;
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
            if (obstacle == null || !obstacle.Center.Finite || !double.IsFinite(obstacle.Radius) || obstacle.Radius < 0) return false;
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
            Vec delta = point - obstacle.Center;
            if (delta.X * delta.X + delta.Y * delta.Y <= obstacle.Radius * obstacle.Radius) return false;
        }
        return true;
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
    }
}
