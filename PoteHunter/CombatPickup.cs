namespace PoteHunter;

public sealed class PriorityTargetException(Entity target) : Exception("Switching to "+target.DisplayName)
{
    public Entity Target { get; } = target;
}

public static class CombatPickup
{
    public static bool SkillsCooling(string keys,HotbarSnapshot bar) => keys.Length>0 && keys.All(key=>
        bar.Slots.SingleOrDefault(s=>s.Key==key.ToString()) is {Kind:SlotKind.Skill} slot && slot.RemainingCooldown>0);

    public static string? Blocked(Vec position,IEnumerable<Entity> entities,uint selfId,IEnumerable<GroundItem> drops,
        IReadOnlySet<(uint,uint)> baseline,bool antiKillSteal,double playerRadius)
    {
        if(!antiKillSteal)return null;
        return CombatCourtesy.PlayerNear(position,entities,selfId,Math.Max(3,playerRadius)) ??
            (drops.Any(d=>baseline.Contains((d.KeyA,d.KeyB)) && (d.Position-position).Length<=3)
                ? "Pre-existing loot is inside pickup range" : null);
    }

    public static void SelfTest()
    {
        var one=new HotbarSlot("1",SlotKind.Skill,1,"Bash",12000,8000,false,0);
        var two=one with{Key="2",Id=2};var bar=new HotbarSnapshot(0,[one,two]);
        if(!SkillsCooling("12",bar) || SkillsCooling("",bar) || SkillsCooling("123",bar) ||
            SkillsCooling("12",new(0,[one,two with{RemainingCooldown=0}])) ||
            SkillsCooling("12",new(0,[one,two with{Kind=SlotKind.Item}])))
            throw new Exception("Combat pickup cooldown eligibility failed");
        var other=new Entity(7,77,"Other player",new(2,0),0,Model:"PC_Akhan_A.GCMDS");
        var drop=new GroundItem(1,2,3020,"Bread",new(1,0),0);
        HashSet<(uint,uint)> baseline=[(1,2)];
        if(Blocked(new(),[],1,[drop],baseline,true,1)==null || Blocked(new(),[other],1,[],baseline,true,1)==null ||
            Blocked(new(),[],1,[drop with{KeyA=3}],baseline,true,1)!=null ||
            Blocked(new(),[],1,[drop with{Position=new(5,0)}],baseline,true,1)!=null)
            throw new Exception("Combat pickup ownership protection failed");
    }
}
