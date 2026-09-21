namespace PoteHunter;

public sealed class CombatPressure
{
    public const long QuietMilliseconds=3000;
    Health? lastHealth;
    long lastObservationAt;
    public long? LastDamageAt { get; private set; }

    public void Reset()
    {
        lastHealth=null;lastObservationAt=0;LastDamageAt=null;
    }

    // Low health on the first observation is a baseline, not evidence of an
    // attack. Only a measured fall in the character's current HP renews pressure.
    public bool Observe(Health health,long now)
    {
        if(!health.Known)throw new InvalidOperationException("Player HP is unavailable; incoming damage cannot be checked.");
        if(health.Dead)throw new InvalidOperationException("Character died; incoming damage cannot be checked.");
        if(lastHealth.HasValue && now<lastObservationAt)throw new InvalidOperationException("Incoming damage observations arrived out of order.");
        bool dropped=lastHealth is Health previous && health.Current<previous.Current;
        lastHealth=health;lastObservationAt=now;
        if(dropped)LastDamageAt=now;
        return dropped;
    }

    public bool RecentDamage(long now)=>LastDamageAt is long damagedAt && now>=damagedAt && now-damagedAt<QuietMilliseconds;

    // The caller invokes this only in reaction to measured player damage. There
    // is no verified attacker ID: a nearby creature is a defensive candidate,
    // not a confirmed source of the damage. Existing ownership takes priority,
    // and every candidate still has to pass the caller's target protections.
    public static Entity? ChooseDefense(IEnumerable<Entity> entities,IReadOnlyDictionary<uint,Health> health,
        Vec player,double radius,Func<Entity,Health,bool> permitted,Func<Entity,bool>? owned=null)
    {
        ArgumentNullException.ThrowIfNull(entities);ArgumentNullException.ThrowIfNull(health);ArgumentNullException.ThrowIfNull(permitted);
        if(!player.Finite)throw new ArgumentOutOfRangeException(nameof(player));
        if(!double.IsFinite(radius) || radius<=0)throw new ArgumentOutOfRangeException(nameof(radius));
        var observed=entities.Where(entity=>entity!=null).ToArray();
        var ambiguousIds=observed.GroupBy(entity=>entity.Id)
            .Where(group=>group.Select(entity=>(entity.Generation,entity.Address)).Distinct().Skip(1).Any())
            .Select(group=>group.Key).ToHashSet();
        var candidates=new List<(Entity Entity,double Distance,bool Owned)>();
        foreach(var entity in observed.GroupBy(entity=>(entity.Id,entity.Generation,entity.Address)).Select(group=>group.First()))
        {
            if(!entity.Monster || !entity.Targetable || entity.PriorityLootObject || !entity.Position.Finite || ambiguousIds.Contains(entity.Id))continue;
            var hp=health.GetValueOrDefault(entity.Id);
            if(!hp.Known || hp.Dead)continue;
            // Normalize before squaring so finite but extreme coordinates do
            // not turn an overflowing distance into an accepted candidate.
            double x=Math.Abs(entity.Position.X-player.X),y=Math.Abs(entity.Position.Y-player.Y);
            if(x>radius || y>radius)continue;
            x/=radius;y/=radius;
            double distance=x*x+y*y;
            if(distance>1 || !permitted(entity,hp))continue;
            candidates.Add((entity,distance,owned?.Invoke(entity)==true));
        }
        return candidates.OrderByDescending(candidate=>candidate.Owned).ThenBy(candidate=>candidate.Distance)
            .ThenBy(candidate=>candidate.Entity.Id).Select(candidate=>candidate.Entity).FirstOrDefault();
    }

    public static void SelfTest()
    {
        var pressure=new CombatPressure();
        if(pressure.RecentDamage(0) || pressure.Observe(new(20,100),100) || pressure.LastDamageAt.HasValue ||
            pressure.Observe(new(20,100),200) || pressure.Observe(new(25,100),300))
            throw new Exception("Initial low health, stable health, or recovery was mistaken for incoming damage.");
        if(!pressure.Observe(new(24,100),400) || pressure.LastDamageAt!=400 || !pressure.RecentDamage(400) ||
            !pressure.RecentDamage(3399) || pressure.RecentDamage(3400) || pressure.RecentDamage(399))
            throw new Exception("A measured HP drop did not produce a bounded three-second combat pressure window.");
        if(!pressure.Observe(new(23,100),3300) || !pressure.RecentDamage(6299) || pressure.RecentDamage(6300) ||
            pressure.Observe(new(24,100),3400) || pressure.LastDamageAt!=3300)
            throw new Exception("Repeated incoming damage failed to renew the quiet period or recovery renewed it.");
        bool reversed=false;
        try{pressure.Observe(new(23,100),3399);}catch(InvalidOperationException){reversed=true;}
        if(!reversed || pressure.LastDamageAt!=3300)throw new Exception("Out-of-order health observations changed combat pressure.");
        foreach(var invalid in new[]{new Health(0,0),new Health(0,100),new Health(-1,100)})
        {
            bool rejected=false;try{pressure.Observe(invalid,3500);}catch(InvalidOperationException){rejected=true;}
            if(!rejected || pressure.LastDamageAt!=3300)throw new Exception("Unknown or dead player health changed combat pressure.");
        }
        pressure.Reset();
        if(pressure.LastDamageAt.HasValue || pressure.RecentDamage(3500) || pressure.Observe(new(1,100),0))
            throw new Exception("Reset retained an old encounter's health or damage timestamp.");

        static Entity Mob(uint id,double x,double y=0)=>new((long)id+100,id,"Lv. 1 Monster",new(x,y),0,Generation:1);
        var close=Mob(0x80000001,1);var farther=Mob(0x80000002,4);var boundary=Mob(0x80000003,3,4);
        var outside=Mob(0x80000004,5.01);var dead=Mob(0x80000005,.25);var unknown=Mob(0x80000006,.5);
        var npc=close with {Id=0x40000001,Address=200};
        var player=close with {Id=7,Address=201,Name="Player",Model="PC_MAN.GCMDS"};
        var prop=close with {Id=0x8000175F,Address=202,Model="NPC_AG_Container.gcmds"};
        var malformed=Mob(0x80000007,double.NaN);
        Entity[] scene=[close,farther,boundary,outside,dead,unknown,npc,player,prop,malformed];
        var health=scene.ToDictionary(entity=>entity.Id,_=>new Health(50,100));
        health[dead.Id]=new(0,100);health.Remove(unknown.Id);
        Vec position=new(0,0);
        if(ChooseDefense(scene,health,position,5,(_,_)=>true)?.Id!=close.Id ||
            ChooseDefense(scene,health,position,5,(_,_)=>true,entity=>entity.Id==farther.Id)?.Id!=farther.Id ||
            ChooseDefense(scene,health,position,5,(entity,_)=>entity.Id!=farther.Id,entity=>entity.Id==farther.Id)?.Id!=close.Id)
            throw new Exception("Defensive selection ignored nearest distance, existing ownership, or target protections.");
        if(ChooseDefense([boundary,outside],health,position,5,(_,_)=>true)?.Id!=boundary.Id ||
            ChooseDefense([outside],health,position,5,(_,_)=>true)!=null ||
            ChooseDefense([boundary],health,position,4.99,(_,_)=>true)!=null ||
            ChooseDefense([boundary],health,new(10,0),5,(_,_)=>true)!=null)
            throw new Exception("Defensive selection did not use the current player's bounded inclusive response radius.");
        if(ChooseDefense([npc,player,prop,dead,unknown,malformed],health,position,5,(_,_)=>true)!=null ||
            ChooseDefense(scene,health,position,5,(_,_)=>false)!=null ||
            ChooseDefense([],health,position,5,(_,_)=>true)!=null)
            throw new Exception("Defensive selection accepted a non-monster, dead, unknown, malformed, or protected target.");
        var replacement=close with {Generation=2,Address=close.Address+1};
        if(ChooseDefense([close,replacement,farther],health,position,5,(_,_)=>true,entity=>entity.Id==close.Id)?.Id!=farther.Id ||
            ChooseDefense([close,close],health,position,5,(_,_)=>true)?.Id!=close.Id)
            throw new Exception("Ambiguous identities inherited combat ownership or identical observations lost a valid candidate.");
        if(ChooseDefense([close with {Position=new(double.MaxValue,0)}],health,new(-double.MaxValue,0),double.MaxValue,(_,_)=>true)!=null ||
            ChooseDefense([close with {Position=new(double.MaxValue,0)}],health,position,double.MaxValue,(_,_)=>true)?.Id!=close.Id)
            throw new Exception("Defensive selection mishandled overflowing distance arithmetic.");
        foreach(double invalid in new[]{0d,-1d,double.NaN,double.PositiveInfinity})
        {
            bool rejected=false;try{ChooseDefense(scene,health,position,invalid,(_,_)=>true);}catch(ArgumentOutOfRangeException){rejected=true;}
            if(!rejected)throw new Exception("Defensive selection accepted an invalid radius.");
        }
        bool positionRejected=false;
        try{ChooseDefense(scene,health,new(double.NaN,0),5,(_,_)=>true);}catch(ArgumentOutOfRangeException){positionRejected=true;}
        if(!positionRejected)throw new Exception("Defensive selection accepted an invalid player position.");
    }
}
