using System.Text.Json;
namespace PoteHunter;

public static class GroupHealerChecks
{
    public static void Run()
    {
        var checks=new List<string>();
        void Check(bool ok,string name){if(!ok)throw new Exception("Group healer: "+name);checks.Add(name);}
        var self=new Entity(1,100,"Healer",new(0,0),0,Generation:1,Model:"PC_Akhan_A.GCMDS");
        var tank=new Entity(2,101,"Tank",new(10,0),0,Generation:2,Model:"PC_Akhan_A.GCMDS");
        var ally=new Entity(3,102,"Ally",new(3,0),0,Generation:3,Model:"PC_Akhan_A.GCMDS");
        var enemy=new Entity(4,0x80000001,"Gamekeeper",new(2,0),0,Model:"MON_SnowGun2.GCMDS");
        var party=new PartySnapshot(true,[new(100,"Healer",false),new(101,"Tank",true),new(102,"Ally",false)],"Ready");
        Entity[] entities=[self,tank,ally,enemy];
        var hp=new Dictionary<uint,Health>{{100,new(95,100)},{101,new(80,100)},{102,new(20,100)},{enemy.Id,new(100,100)}};
        GroupDecision Follow(Entity actor,PartySnapshot? roster=null,IReadOnlyList<Entity>? actors=null)=>
            GroupHealerPolicy.Follow(roster??party,"Tank",actor,actors??entities,hp,4,50);
        var follow=Follow(self);
        Check(follow.Action==GroupAction.Follow&&follow.Destination==new Vec(6,0)&&follow.Target==null,"follows tank without offensive target");
        Check(Follow(self with{Position=new(6,0)}).Action==GroupAction.Wait,"holds configured follow distance");
        Check(Follow(self with{Position=new(5,0)}).Action==GroupAction.Wait,"follow hysteresis boundary");
        Check(Follow(self with{Position=new(-41,0)}).Action==GroupAction.Wait,"does not chase beyond follow limit");
        Check(Follow(self,new(false,[],"Unavailable")).Action==GroupAction.Wait,"unavailable party waits");
        Check(Follow(self,new(true,[party.Members[0],party.Members[2]],"Left")).Action==GroupAction.Wait,"departed tank waits");
        Check(Follow(self,actors:[self,ally,enemy]).Action==GroupAction.Wait,"unloaded tank waits");
        Check(Follow(self,actors:[self,tank,tank,ally]).Action==GroupAction.Wait,"ambiguous tank waits");
        Check(GroupHealerPolicy.Follow(party,"Healer",self,entities,hp,4,50).Action==GroupAction.Wait,"self cannot be follow tank");
        hp[101]=new(0,100);Check(Follow(self).Action==GroupAction.Wait,"dead tank waits");
        hp.Remove(101);Check(Follow(self).Action==GroupAction.Wait,"unknown tank health waits");hp[101]=new(80,100);
        Check(Follow(self,actors:[self,tank with{Position=new(double.NaN,0)},ally]).Action==GroupAction.Wait,"invalid tank position waits");
        var heal=HealerPolicy.Select(party,self,entities,hp,40,80)!;
        Check(heal.Member.Id==ally.Id,"lowest-health in-range party member selected while tank needs following");
        var key=HealerPolicy.PartyTargetKey(party,ally.Id);
        Check(GroupHealerPolicy.RecipientValid(heal,self,party,entities,hp,40,key),"live healing recipient accepted");
        Check(!GroupHealerPolicy.RecipientValid(heal,self,party,[self,tank,ally with{Position=new(41,0)}],hp,40,key),"out-of-range recipient rejected before cast");
        Check(!GroupHealerPolicy.RecipientValid(heal,self,party,[self,tank,ally with{Generation=4}],hp,40,key),"reused recipient identity rejected");
        Check(!GroupHealerPolicy.RecipientValid(heal,self,party with{Members=[party.Members[0],party.Members[2],party.Members[1]]},entities,hp,40,key),"party reorder invalidates stale F-key");
        Check(!GroupHealerPolicy.RecipientValid(heal,self,new(true,[party.Members[0],party.Members[1]],"Left"),entities,hp,40,key),"departed healing recipient rejected");
        var skill=new HotbarSlot("3",SlotKind.Skill,3,"Heal",0,0,false,0,SkillUse:SkillUseKind.Cast,SkillTarget:SkillTargetKind.Friend);
        Check(GroupHealerPolicy.HealingSkill(skill,false)&&GroupHealerPolicy.HealingSkill(skill with{SkillTarget=SkillTargetKind.Party},false),"friendly and party heal skills eligible");
        Check(!GroupHealerPolicy.HealingSkill(skill with{SkillTarget=SkillTargetKind.Enemy},false)&&
            !GroupHealerPolicy.HealingSkill(skill with{SkillTarget=SkillTargetKind.Melee},false),"offensive skills excluded from healbot");
        Check(!GroupHealerPolicy.HealingSkill(skill with{SkillTarget=SkillTargetKind.FriendExceptSelf},true)&&
            !GroupHealerPolicy.HealingSkill(skill with{SkillTarget=SkillTargetKind.None},false),"self and other-only target rules enforced");
        var saved=JsonSerializer.Deserialize<Options>(JsonSerializer.Serialize(new Options{HealerMode=true,GroupMode=true,GroupTankName="Tank",UseAttackPotions=true}))!;
        Check(saved.HealerMode&&saved.GroupMode&&saved.GroupTankName=="Tank"&&saved.UseAttackPotions,"combined mode preserves latest potion settings");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"group-healer-checks.json"),JsonSerializer.Serialize(new{Passed=true,LiveGameTested=false,Checks=checks},new JsonSerializerOptions{WriteIndented=true}));
    }
}
