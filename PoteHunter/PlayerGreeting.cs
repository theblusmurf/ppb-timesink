namespace PoteHunter;

public sealed class PlayerGreeting
{
    readonly HashSet<(uint Id, uint Generation, long Address)> greeted = new();
    public void Reset() => greeted.Clear();
    public Entity? Next(Vec position, IEnumerable<Entity> entities, uint selfId, double radius)
    {
        if (!position.Finite || !double.IsFinite(radius) || radius <= 0) return null;
        return entities.Where(e => CombatCourtesy.IsOtherPlayer(e, selfId) && e.Position.Finite &&
            (e.Position - position).Length <= radius && !greeted.Contains((e.Id, e.Generation, e.Address)))
            .OrderBy(e => (e.Position - position).Length).ThenBy(e => e.Id).FirstOrDefault();
    }
    public void Mark(Entity player) => greeted.Add((player.Id, player.Generation, player.Address));
    public static void SelfTest()
    {
        var policy = new PlayerGreeting();
        var self = new Entity(1, 10, "Self", new(0, 0), 0, Model: "PC_MAN.GCMDS");
        var near = new Entity(2, 20, "Alice", new(25, 0), 0, Generation: 1, Model: "PC_A.GCMDS");
        var far = near with { Id = 21, Position = new(25.01, 0) };
        if (policy.Next(new(0, 0), [self, far], 10, 25) != null) throw new Exception("Outside player was recognized.");
        var found = policy.Next(new(0, 0), [self, near], 10, 25) ?? throw new Exception("Nearby player was not recognized.");
        policy.Mark(found);
        if (policy.Next(new(0, 0), [self, near], 10, 25) != null) throw new Exception("Greeting repeated.");
        if (policy.Next(new(0, 0), [self, near with { Generation = 2 }], 10, 25) == null) throw new Exception("New identity was suppressed.");
    }
}

