namespace PoteHunter;

public enum GroupAction { Wait, Follow, Attack }
public sealed record GroupDecision(GroupAction Action,string Status,Entity? Tank=null,Entity? Target=null,Vec? Destination=null);

public static class GroupPolicy
{
    static bool Same(Entity a,Entity b)=>a.Id==b.Id && a.Generation==b.Generation && a.Address==b.Address;
    public static GroupDecision Decide(PartySnapshot party,string tankName,Entity self,IReadOnlyList<Entity> entities,
        IReadOnlyDictionary<uint,Health> health,double followDistance,double attackRadius,double leash,
        Func<Entity,Health,bool> allowed,Entity? locked=null,bool prioritizeGamekeeper=false)
    {
        if(!double.IsFinite(followDistance) || followDistance<2 || !double.IsFinite(attackRadius) || attackRadius<1 ||
            !double.IsFinite(leash) || leash<followDistance)throw new InvalidOperationException("Group distances are invalid.");
        if(!party.Available)return new(GroupAction.Wait,party.Status);
        var member=party.Members.SingleOrDefault(m=>m.Name.Equals(tankName,StringComparison.OrdinalIgnoreCase));
        if(member==null || member.Id==self.Id)return new(GroupAction.Wait,"Select another current party member as tank");
        var tank=entities.SingleOrDefault(e=>e.Id==member.Id && CombatCourtesy.IsOtherPlayer(e,self.Id));
        if(tank==null)return new(GroupAction.Wait,"Tank is outside the loaded area");
        if(health.GetValueOrDefault(tank.Id).Dead)return new(GroupAction.Wait,"Tank is dead",tank);
        double distance=(tank.Position-self.Position).Length;
        if(distance>leash)return new(GroupAction.Wait,"Tank is beyond the follow limit",tank);
        // Finish a valid close fight while still inside the tank's combat area; do not chase it away from the tank.
        var candidates=entities.Where(e=>e.Targetable && e.Position.Finite && (e.Position-tank.Position).Length<=attackRadius &&
            health.GetValueOrDefault(e.Id) is {Known:true,Dead:false} && allowed(e,health[e.Id])).ToArray();
        var retained=locked==null?null:candidates.FirstOrDefault(e=>Same(e,locked));
        var urgent=prioritizeGamekeeper ? candidates.Where(Targeting.IsGamekeeper).OrderBy(e=>(e.Position-self.Position).Length).ThenBy(e=>e.Id).FirstOrDefault() : null;
        if(urgent!=null && retained!=null && !Targeting.IsGamekeeper(retained) && distance<=followDistance+attackRadius+2)
            return new(GroupAction.Attack,"Prioritizing Gamekeeper near "+member.Name,tank,urgent);
        if(retained!=null && distance<=followDistance+attackRadius+2)return new(GroupAction.Attack,"Fighting near "+member.Name,tank,retained);
        if(distance>followDistance+1)
            return new(GroupAction.Follow,"Following "+member.Name,tank,null,tank.Position+(self.Position-tank.Position)/distance*followDistance);
        var target=candidates.OrderByDescending(e=>Targeting.PriorityRank(e,prioritizeGamekeeper)).ThenBy(e=>(e.Position-tank.Position).Length).ThenBy(e=>e.Id).FirstOrDefault();
        return target==null?new(GroupAction.Wait,"In position near "+member.Name+"; waiting for an allowed target",tank):
            new(GroupAction.Attack,"Attacking near "+member.Name,tank,target);
    }

    public static void SelfTest()
    {
        var self=new Entity(1,3447,"Gimp",new(0,0),0,Model:"PC_Akhan_A.GCMDS");
        var tank=new Entity(2,3420,"",new(10,0),0,Generation:1,Model:"PC_Akhan_A.GCMDS");
        var nearSelf=new Entity(3,0x80000001,"Lv. 1 Nearby",new(1,0),0,Generation:1);
        var nearTank=new Entity(4,0x80000002,"Lv. 1 Tank area",new(11,0),0,Generation:2);
        var party=new PartySnapshot(true,[new(3447,"Gimp",false),new(3420,"E-Thug",true)],"Ready");
        Entity[] actors=[self,tank,nearSelf,nearTank];var hp=new Dictionary<uint,Health>{{tank.Id,new(100,100)},{nearSelf.Id,new(100,100)},{nearTank.Id,new(40,100)}};
        var follow=Decide(party,"E-Thug",self,actors,hp,4,6,50,(_,_)=>true);
        if(follow.Action!=GroupAction.Follow || follow.Destination!=new Vec(6,0) || follow.Target!=null)throw new Exception("Group must follow before pulling targets");
        var close=self with{Position=new(7,0)};
        if(Decide(party,"E-Thug",close,actors,hp,4,6,50,(_,_)=>true).Target!=nearTank)throw new Exception("Group attacked outside the tank area");
        if(Decide(party,"E-Thug",close,actors,hp,4,6,50,(_,_)=>false).Target!=null)throw new Exception("Group bypassed target protections");
        if(Decide(new(true,[party.Members[0]],"Tank left"),"E-Thug",close,actors,hp,4,6,50,(_,_)=>true).Action!=GroupAction.Wait ||
            Decide(party,"E-Thug",close,[self,nearSelf,nearTank],hp,4,6,50,(_,_)=>true).Action!=GroupAction.Wait)
            throw new Exception("Group chased a departed/unloaded tank");
        hp[nearTank.Id]=new(0,100);if(Decide(party,"E-Thug",close,actors,hp,4,6,50,(_,_)=>true,nearTank).Target!=null)throw new Exception("Group retained dead target");
        hp[nearTank.Id]=new(40,100);
        var retained=Decide(party,"E-Thug",self with{Position=new(16,0)},actors,hp,4,6,50,(_,_)=>true,nearTank);
        if(retained.Target!=nearTank)throw new Exception("Group broke a valid fight on small tank movement");
        var moved=nearTank with{Position=new(30,0)};
        if(Decide(party,"E-Thug",close,[self,tank,nearSelf,moved],hp,4,6,50,(_,_)=>true,nearTank).Target!=null)throw new Exception("Group chased monster away from tank");
        var keeper=new Entity(5,0x80251752,"Gamekeeper",new(14,0),0,Model:"MON_SnowGun2.GCMDS");
        hp[keeper.Id]=new(50,50);Entity[] withKeeper=[self,tank,nearTank,keeper];
        if(Decide(party,"E-Thug",close,withKeeper,hp,4,6,50,(_,_)=>true,nearTank,true).Target!=keeper ||
            Decide(party,"E-Thug",close,withKeeper,hp,4,6,50,(_,_)=>true,nearTank,false).Target!=nearTank ||
            Decide(party,"E-Thug",close,withKeeper,hp,4,6,50,(e,_)=>e.Id!=keeper.Id,nearTank,true).Target!=nearTank ||
            Decide(party,"E-Thug",close,[self,tank,nearTank,keeper with{Position=new(20,0)}],hp,4,6,50,(_,_)=>true,nearTank,true).Target!=nearTank)
            throw new Exception("Group Gamekeeper priority failed retention, toggle, protection, or tank radius.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"group-checks.json"),System.Text.Json.JsonSerializer.Serialize(new {Passed=true,Checks=new[]{"party tank identity","follow distance","only targets near tank","target policy respected","missing/departed tank waits","dead targets released","valid fight retained","escaping target not chased","Gamekeeper preemption respects toggle and protections"}},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    }
}
