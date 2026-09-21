namespace PoteHunter;

public sealed record HealTarget(PartyMember Member,Entity Entity,Health Health,int PartyIndex,double Percent)
{
    public bool IsSelf(Entity self)=>Entity.Id==self.Id;
}

public static class HealerPolicy
{
    static readonly string[] HealingWords=["heal","first aid","regenerat","recovery","recover","cure","mend","renew","restore"];
    static readonly Keys?[] PartyKeys=[Keys.F1,Keys.F2,Keys.F3,Keys.F4,Keys.F5,Keys.F7,Keys.F10,Keys.F11,Keys.F12];

    public static bool IsHealingSkill(HotbarSlot slot)=>slot.Kind==SlotKind.Skill &&
        HealingWords.Any(word=>slot.Name.Contains(word,StringComparison.OrdinalIgnoreCase));
    public static string DetectHealingKeys(HotbarSnapshot bar)=>new("1234567890".Where(k=>
        bar.Slots.Any(s=>s.Key==k.ToString() && IsHealingSkill(s))).ToArray());
    public static string AvailableHealingKeys(string configured,HotbarSnapshot bar)=>new(configured.Where(k=>
        bar.Slots.Any(s=>s.Key==k.ToString() && s.Kind==SlotKind.Skill)).ToArray());
    public static HotbarSlot? ChooseReady(string keys,HotbarSnapshot bar,int cursor=0)
    {
        if(keys.Length==0)return null;
        for(int i=0;i<keys.Length;i++)
        {
            var slot=bar.Slots.SingleOrDefault(s=>s.Key==keys[(cursor+i)%keys.Length].ToString());
            if(slot is {Kind:SlotKind.Skill,Ready:true})return slot;
        }
        return null;
    }
    public static Keys? PartyTargetKey(PartySnapshot party,uint targetId)
    {
        int index=party.Members.ToList().FindIndex(m=>m.Id==targetId);
        return index>=0 && index<PartyKeys.Length?PartyKeys[index]:null;
    }
    public static HealTarget? Select(PartySnapshot party,Entity self,IReadOnlyList<Entity> entities,
        IReadOnlyDictionary<uint,Health> health,double radius,decimal belowPercent)
    {
        if(!double.IsFinite(radius)||radius<=0)throw new ArgumentOutOfRangeException(nameof(radius));
        if(belowPercent<=0||belowPercent>100)throw new ArgumentOutOfRangeException(nameof(belowPercent));
        var members=party.Available && party.Members.Count>0?party.Members:
            [new PartyMember(self.Id,self.Name,true)];
        var candidates=members.Select((member,index)=>
        {
            var entity=member.Id==self.Id?self:entities.FirstOrDefault(e=>e.Id==member.Id);
            if(entity==null)return null;
            var hp=health.GetValueOrDefault(member.Id);
            if(!hp.Known||hp.Dead||(entity.Position-self.Position).Length>radius)return null;
            double percent=hp.Current*100.0/hp.Maximum;
            return percent<=(double)belowPercent?new HealTarget(member,entity,hp,index,percent):null;
        }).Where(t=>t!=null).Cast<HealTarget>().OrderBy(t=>t.Percent).ThenBy(t=>t.Health.Current).ThenBy(t=>t.PartyIndex).FirstOrDefault();
        return candidates;
    }
    public static bool Same(HealTarget target,IReadOnlyList<Entity> entities)=>entities.Any(e=>e.Id==target.Entity.Id &&
        e.Generation==target.Entity.Generation && e.Address==target.Entity.Address && e.Name==target.Entity.Name);

    public static void SelfTest()
    {
        var self=new Entity(1,100,"Healer",new(0,0),0,Generation:1,Model:"PC_Akhan_A.GCMDS");
        var ally=new Entity(2,101,"Tank",new(4,0),0,Generation:2,Model:"PC_Akhan_A.GCMDS");
        var far=new Entity(3,102,"Far",new(50,0),0,Generation:3,Model:"PC_Akhan_A.GCMDS");
        var party=new PartySnapshot(true,[new(100,"Healer",true),new(101,"Tank",false),new(102,"Far",false)],"3 party members");
        var hp=new Dictionary<uint,Health>{{100,new(95,100)},{101,new(20,100)},{102,new(1,100)}};
        var target=Select(party,self,[self,ally,far],hp,10,80);
        if(target==null||target.Entity.Id!=101||target.PartyIndex!=1||PartyTargetKey(party,101)!=Keys.F2)throw new Exception("Healer did not choose the lowest nearby party member or F-key");
        hp[101]=new(80,100);if(Select(party,self,[self,ally],hp,10,80)?.Entity.Id!=ally.Id)throw new Exception("Heal threshold boundary failed");
        if(Select(party,self,[self,ally],hp,10,79)!=null)throw new Exception("Heal threshold boundary failed");
        hp[101]=new(0,100);if(Select(party,self,[self,ally],hp,10,100)?.Entity.Id==101)throw new Exception("Dead ally was selected");
        var noParty=Select(new(false,[],"Not in a party"),self,[self],new Dictionary<uint,Health>{{100,new(50,100)}},10,80);
        if(noParty==null||!noParty.IsSelf(self))throw new Exception("Healer did not retain self recovery outside a party");
        var bar=new HotbarSnapshot(0,[new("1",SlotKind.Skill,1,"First Aid Lv.1",26000,0,false,0),new("2",SlotKind.Skill,2,"Arcane Shackles",26000,0,false,0),new("9",SlotKind.Skill,9,"Regeneration",26000,100,false,0),new("0",SlotKind.Item,4,"Bread",0,0,false,0)]);
        if(!IsHealingSkill(bar.Slot('1'))||IsHealingSkill(bar.Slot('2'))||!IsHealingSkill(bar.Slot('9'))||DetectHealingKeys(bar)!="19"||AvailableHealingKeys("19",bar)!="19"||ChooseReady("19",bar)?.Key!="1")throw new Exception("Healing skill detection/rotation failed");
        if(ChooseReady("9",bar)!=null||PartyTargetKey(party,999)!=null)throw new Exception("Cooling skill or unknown party member was selected");
        if(!Same(target,[self,ally]))throw new Exception("Party target identity failed");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"healer-checks.json"),System.Text.Json.JsonSerializer.Serialize(new {
            Passed=true,Checks=new[]{"lowest nearby ally selection","self fallback","threshold boundary","dead/unknown exclusion","F-key party selection","healing skill name detection","cooldown filtering","target identity"}},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    }
}
