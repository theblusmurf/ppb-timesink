namespace PoteHunter;

public sealed class TargetProtectionException(string message) : Exception(message);

public sealed class CombatCourtesy
{
    readonly HashSet<(uint Id, uint Generation, long Address)> started = new();
    static (uint, uint, long) Identity(Entity e) => (e.Id,e.Generation,e.Address);
    public void Reset() => started.Clear();
    public bool StartedHere(Entity e) => started.Contains(Identity(e));
    public void MarkAttack(Entity e) => started.Add(Identity(e));
    public void Forget(Entity e) => started.Remove(Identity(e));

    // Observed player bodies use low UIDs and PC_* models. Guards/merchants use UID category 4.
    // Separate mount objects have no verified owner link, so model alone does not identify another player.
    public static bool IsOtherPlayer(Entity e, uint selfId) => e.Id != 0 && e.Id != selfId &&
        (e.Id & 0xf0000000) == 0 && e.Model.StartsWith("PC_",StringComparison.OrdinalIgnoreCase);

    public static string? PlayerNear(Vec point, IEnumerable<Entity> entities, uint selfId, double radius)
    {
        var player = entities.Where(e => IsOtherPlayer(e,selfId) && (e.Position-point).Length <= radius)
            .OrderBy(e => (e.Position-point).Length).FirstOrDefault();
        return player == null ? null : "Player nearby: " + (string.IsNullOrWhiteSpace(player.Name) ? $"{player.Id:X8}" : player.Name);
    }

    public string? Blocked(Entity target, Health hp, IEnumerable<Entity> entities, uint selfId, bool enabled, double radius, bool recentCollateral=false)
    {
        if (!enabled) return null;
        return PlayerNear(target.Position,entities,selfId,radius) ??
            (hp.Known && hp.Current < hp.Maximum && !StartedHere(target) && !recentCollateral ? "Already damaged by someone else / before this hunt" : null);
    }

    public static void SelfTest()
    {
        var policy=new CombatCourtesy();
        var target=new Entity(1,0x80000001,"Lv. 1 Monster",new Vec(20,0),0,Generation:1);
        var player=new Entity(2,1234,"",new Vec(24,0),0,Model:"PC_MAN.GCMDS");
        var guard=new Entity(3,0x40000002,"Guard",new Vec(20,0),0,Model:"NPC_M13.GCMDS");
        if (policy.Blocked(target,new Health(90,100),[],7,true,15)==null || policy.Blocked(target,new Health(100,100),[player],7,true,15)==null)
            throw new Exception("Anti-KS must reject damaged targets and unnamed nearby players");
        if (policy.Blocked(target,new Health(100,100),[guard],7,true,15)!=null || IsOtherPlayer(player,1234))
            throw new Exception("Anti-KS must exclude NPC guards and the local player");
        policy.MarkAttack(target);
        if (policy.Blocked(target,new Health(90,100),[],7,true,15)!=null || policy.Blocked(target with { Generation=2 },new Health(90,100),[],7,true,15)==null)
            throw new Exception("An initiated fight must survive its own HP loss without trusting a reused identity");
        if (policy.Blocked(target,new Health(90,100),[player],7,true,15)==null) throw new Exception("New nearby players must interrupt even an initiated fight");
        policy.Forget(target);
        if (policy.Blocked(target,new Health(90,100),[],7,true,15)==null || policy.Blocked(target,new Health(90,100),[player],7,false,15)!=null)
            throw new Exception("Relinquished targets and the anti-KS toggle were mishandled");
    }
}
