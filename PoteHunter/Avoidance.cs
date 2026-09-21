namespace PoteHunter;

public record AvoidRule(string Name = "", double Radius = 12);
public record AvoidZone(Vec Center, double Radius, string Reason);

public static class Avoidance
{
    public static void Validate(IEnumerable<AvoidRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        foreach (var rule in rules)
        {
            if (rule == null) throw new InvalidOperationException("Avoid rules cannot contain null entries.");
            if (string.IsNullOrWhiteSpace(rule.Name))
                throw new InvalidOperationException("An avoid rule needs a name.");
            if (!double.IsFinite(rule.Radius) || rule.Radius is < 1 or > 150)
                throw new InvalidOperationException("Avoid radius must be from 1 through 150.");
        }
    }

    public static List<AvoidZone> BuildZones(IEnumerable<AvoidRule> rules, IEnumerable<Entity> entities, uint selfId)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(entities);
        var ruleList = rules.ToList();
        Validate(ruleList);
        var entityList = entities.Where(entity => entity.Id != selfId && entity.Position.Finite).ToList();
        var zones = new List<AvoidZone>();
        foreach (var rule in ruleList)
        {
            string name = rule.Name.Trim();
            if (name.Length > 0)
            {
                foreach (var entity in entityList.Where(entity =>
                    entity.Name.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                    entity.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase)))
                    zones.Add(new AvoidZone(entity.Position, rule.Radius, $"Avoid {entity.DisplayName}"));
            }
        }
        return zones;
    }

    public static string? BlockedPoint(Vec point, IEnumerable<AvoidZone> zones, double clearance = 1)
    {
        ValidateGeometry(point, zones, clearance, out var zoneList);
        foreach (var zone in zoneList)
        {
            double radius = zone.Radius + clearance;
            Vec delta = point - zone.Center;
            if (delta.X * delta.X + delta.Y * delta.Y <= radius * radius) return zone.Reason;
        }
        return null;
    }

    public static string? BlockedSegment(Vec start, Vec end, IEnumerable<AvoidZone> zones, double clearance = 1)
    {
        ValidateGeometry(start, zones, clearance, out var zoneList);
        if (!end.Finite) throw new ArgumentOutOfRangeException(nameof(end), "Segment endpoints must be finite.");
        Vec segment = end - start;
        double lengthSquared = segment.X * segment.X + segment.Y * segment.Y;
        foreach (var zone in zoneList)
        {
            Vec fromCenter = zone.Center - start;
            double t = lengthSquared <= 1e-12 ? 0 : Math.Clamp((fromCenter.X * segment.X + fromCenter.Y * segment.Y) / lengthSquared, 0, 1);
            Vec closest = start + segment * t;
            Vec delta = closest - zone.Center;
            double radius = zone.Radius + clearance;
            if (delta.X * delta.X + delta.Y * delta.Y <= radius * radius) return zone.Reason;
        }
        return null;
    }

    static void ValidateGeometry(Vec point, IEnumerable<AvoidZone> zones, double clearance, out List<AvoidZone> zoneList)
    {
        ArgumentNullException.ThrowIfNull(zones);
        if (!point.Finite) throw new ArgumentOutOfRangeException(nameof(point), "Points must be finite.");
        if (!double.IsFinite(clearance) || clearance < 0) throw new ArgumentOutOfRangeException(nameof(clearance), "Clearance must be finite and nonnegative.");
        zoneList = zones.ToList();
        if (zoneList.Any(zone => zone == null || !zone.Center.Finite || !double.IsFinite(zone.Radius) || zone.Radius < 0))
            throw new ArgumentException("Avoid zones must have finite centers and nonnegative finite radii.", nameof(zones));
    }

    public static void SelfTest()
    {
        var circle = new[] { new AvoidZone(new Vec(0, 0), 2, "center") };
        if (BlockedPoint(new Vec(-3.1, 0), circle, 0) != null || BlockedPoint(new Vec(3.1, 0), circle, 0) != null ||
            BlockedSegment(new Vec(-3.1, 0), new Vec(3.1, 0), circle, 0) != "center")
            throw new Exception("A segment crossing a forbidden circle was not blocked.");
        if (BlockedSegment(new Vec(-3, 2), new Vec(3, 2), circle, 0) != "center")
            throw new Exception("A tangent segment must be blocked.");

        var entities = new[]
        {
            new Entity(1, 10, "Dragon Keeper", new Vec(1, 1), 0),
            new Entity(2, 11, "elder DRAGON", new Vec(2, 2), 0),
            new Entity(3, 12, "Bystander", new Vec(3, 3), 0)
        };
        var dynamicZones = BuildZones([new AvoidRule("dragon", 9)], entities, 10);
        if (dynamicZones.Count != 1 || dynamicZones[0].Center != new Vec(2, 2))
            throw new Exception("Dynamic avoid matching must be case-insensitive and exclude self.");

        foreach (var invalid in new[]
        {
            new AvoidRule(), new AvoidRule("   "), new AvoidRule("invalid radius", double.NaN),
            new AvoidRule("too small", .5), new AvoidRule("too large", 151)
        })
        {
            bool rejected = false;
            try { Validate([invalid]); } catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new Exception("Invalid avoid rule was accepted.");
        }
    }
}
