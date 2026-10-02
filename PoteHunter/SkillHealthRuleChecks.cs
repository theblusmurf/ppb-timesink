using System.Text.Json;
namespace PoteHunter;

public static class SkillHealthRuleChecks
{
    public static void Run()
    {
        static void Check(bool result,string reason){if(!result)throw new Exception("Skill HP rule: "+reason);}
        var rule=new Options();
        var drain=new HotbarSlot("1",SlotKind.Skill,12,"Power Drain Lv.1",1000,0,false,0,SkillUse:SkillUseKind.Instance,SkillTarget:SkillTargetKind.Enemy);
        var attack=drain with{Key="2",Id=13,Name="Bash Lv.1"};
        var heal=drain with{Key="3",Id=14,Name="Greater Healing Lv.2",SkillTarget=SkillTargetKind.Friend};
        Check(rule.HealthSkillCondition && rule.HealthSkillPercent==50,"default enabled at 50 percent");
        foreach(var skill in new[]{drain,heal,heal with{Name="First Aid"},heal with{Name="Regeneration Lv.1"}})
        {
            Check(SkillHealthRule.AutomaticMatch(skill),"healing name recognized");
            Check(SkillHealthRule.Allows(skill,new(50,100),rule) && SkillHealthRule.Allows(skill,new(49,100),rule),"inclusive threshold");
            Check(!SkillHealthRule.Allows(skill,new(51,100),rule),"above threshold held");
            Check(!SkillHealthRule.Allows(skill,new(0,0),rule) && !SkillHealthRule.Allows(skill,new(0,100),rule),"unknown and dead HP held");
        }
        Check(!SkillHealthRule.AutomaticMatch(attack) && !SkillHealthRule.AutomaticMatch(heal with{Name="Mana Recovery"}) &&
            !SkillHealthRule.AutomaticMatch(heal with{Name="Unhealable Strike"}) && !SkillHealthRule.AutomaticMatch(drain with{Kind=SlotKind.Item}),"ordinary skills mana skills and items excluded");
        Check(SkillHealthRule.Allows(attack,new(99,100),rule),"ordinary attacks unaffected");
        Check(SkillHealthRule.Allows(drain,new(5000,10001),rule) && !SkillHealthRule.Allows(drain,new(5001,10001),rule),"no rounded percentage boundary");
        var bar=new HotbarSnapshot(0,[drain,attack,heal]);var retry=new Dictionary<char,long>();
        int Pick(Health hp)=>SkillRotation.Choose("123",0,bar,retry,1000,s=>SkillHealthRule.Allows(s,hp,rule));
        Check(Pick(new(90,100))==1 && Pick(new(50,100))==0,"blocked heal does not block other attacks");
        bar=new(0,[drain with{RemainingCooldown=10},attack,heal]);
        Check(Pick(new(50,100))==1,"condition does not bypass cooldown");
        bar=new(0,[drain with{Locked=true},attack,heal]);Check(Pick(new(50,100))==1,"condition does not bypass lock");
        bar=new(0,[drain,attack,heal]);retry['1']=1001;Check(Pick(new(50,100))==1,"condition does not bypass retry delay");
        Check(SkillHealthRule.CanActivate(drain,drain,new(50,100),rule) && !SkillHealthRule.CanActivate(drain,drain,new(51,100),rule),"HP recovery between key and cast cancels activation/retry");
        Check(!SkillHealthRule.CanActivate(drain,drain with{Id=99},new(40,100),rule) &&
            !SkillHealthRule.CanActivate(drain,drain with{RemainingCooldown=1},new(40,100),rule),"changed slot and newly cooling skill withheld");
        Check(SkillHealthRule.ReserveExempt(drain,false,1,null),"combat Power Drain may spend reserve");
        Check(SkillHealthRule.ReserveExempt(heal,true,1,1) && !SkillHealthRule.ReserveExempt(heal,true,1,2) && !SkillHealthRule.ReserveExempt(heal,true,1,null),"only self-healing recipient exempt in healer mode");
        Check(!SkillHealthRule.ReserveExempt(attack,false,1,null),"ordinary attacks cannot spend reserve");
        Check(ManaReserveRule.Allows(drain,default,50,SkillHealthRule.ReserveExempt(drain,false,1,null)) && !ManaReserveRule.Allows(attack,default,50,SkillHealthRule.ReserveExempt(attack,false,1,null)),"unknown MP does not prevent self-healing but holds ordinary attacks");
        var chosen=SkillHealthRule.ChooseHealth(false,new(90,100),new(20,100));
        Check(!SkillHealthRule.Allows(drain,chosen,rule),"combat uses character HP not enemy HP");
        chosen=SkillHealthRule.ChooseHealth(true,new(90,100),new(20,100));
        Check(SkillHealthRule.Allows(heal,chosen,rule),"healthy healer may heal low HP recipient");
        chosen=SkillHealthRule.ChooseHealth(true,new(20,100),new(90,100));
        Check(!SkillHealthRule.Allows(heal,chosen,rule),"low healer HP does not bypass healthy recipient threshold");
        Check(!SkillHealthRule.Allows(heal,SkillHealthRule.ChooseHealth(true,new(20,100),null),rule),"missing recipient does not fall back to healer HP");
        rule.HealthConditionKeys="2";Check(!SkillHealthRule.Allows(attack,new(90,100),rule),"extra key opts ordinary skill into condition");
        rule.HealthSkillPercent=75;Check(SkillHealthRule.Allows(drain,new(75,100),rule) && !SkillHealthRule.Allows(drain,new(76,100),rule),"custom threshold");
        rule.HealthSkillCondition=false;Check(SkillHealthRule.Allows(drain,new(99,100),rule),"disable restores previous eligibility");
        Check(SkillHealthRule.NormalizeKeys("1, 3 0")=="130","key normalization");
        foreach(var invalid in new[]{"11","Q","-1"}){bool threw=false;try{SkillHealthRule.NormalizeKeys(invalid);}catch(InvalidOperationException){threw=true;}Check(threw,"invalid keys rejected");}
        var saved=JsonSerializer.Deserialize<Options>(JsonSerializer.Serialize(rule))!;
        Check(!saved.HealthSkillCondition && saved.HealthSkillPercent==75 && saved.HealthConditionKeys=="2","settings roundtrip");
        Check(JsonSerializer.Deserialize<Options>("{}")!.HealthSkillPercent==50,"older settings default");
        CheckCombatSelection();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"skill-health-rule-checks.json"),JsonSerializer.Serialize(new{Passed=true,HardwareInputEmitted=false,LiveGameTested=false,Checks=new[]{"name detection and exclusions","inclusive unrounded 50% boundary","character HP in combat","recipient HP in healer mode","missing/dead HP","cooldown lock and retry preserved","other attacks continue","post-selection activation recheck","manual key and configurable threshold","settings roundtrip","priority heal with one or three enemies and healthy packs","priority before attacks and shared timers","offensive pack rules retained","valid target and extra-key exclusions"}},new JsonSerializerOptions{WriteIndented=true}));
    }

    static void CheckCombatSelection()
    {
        static void Check(bool result,string reason){if(!result)throw new Exception("Combat self-heal priority: "+reason);}
        var rule=new Options {HealthSkillCondition=true,HealthSkillPercent=40};
        var bash=new HotbarSlot("1",SlotKind.Skill,1,"Bash Lv.1",11760,0,false,0,SkillUse:SkillUseKind.Instance,SkillTarget:SkillTargetKind.Melee);
        var fast=bash with{Key="2",Id=2,Name="Fast Hit Lv.1"};
        var drain=bash with{Key="3",Id=3,Name="Power Drain Lv.1",SkillTarget=SkillTargetKind.Enemy};
        var bar=new HotbarSnapshot(0,[bash,fast,drain]);
        var retry=new Dictionary<char,long>();
        var targets=Enumerable.Range(1,10).Select(i=>new Entity((uint)i,0x80000000u+(uint)i,"Mimic",new(i*.1,0),0)).ToArray();
        SkillGroupStatus Pack(int count,int percent)=>SkillGroupGate.Evaluate(targets.Take(count),targets[0],
            targets.ToDictionary(t=>t.Id,_=>new Health(percent,100)),new(),12);
        int Pick(Health hp,SkillGroupStatus pack,bool target=true,bool warm=true,bool delay=true,int cursor=0,Func<HotbarSlot,bool>? allowed=null)=>
            CombatSkillPolicy.Choose("123",cursor,bar,retry,1000,hp,rule,pack,target,warm,delay,allowed??(_=>true));
        var low=new Health(378,1000);var wounded=Pack(10,70);
        foreach(var pack in new[]{Pack(1,15),Pack(3,16),Pack(4,79),Pack(10,80),Pack(10,100),wounded})
        {
            Check(Pick(low,pack,warm:false,delay:false)==2,"heal waited for enemy count/HP or combat timers");
            Check(Pick(low,pack)==2 && Pick(low,pack,cursor:1)==2,"offensive rotation outranked a ready heal");
        }
        Check(Pick(new(400,1000),Pack(3,16))==2 && Pick(new(401,1000),Pack(3,16))==-1,"inclusive HP threshold");
        Check(Pick(default,Pack(3,16))==-1 && Pick(new(0,1000),Pack(3,16))==-1,"unknown/dead HP rejected");
        Check(Pick(low,wounded,target:false)==-1,"missing/dead/protected combat target permitted a skill");
        Check(Pick(low,Pack(3,16),allowed:s=>s.Key!="3")==-1 && Pick(low,wounded,allowed:s=>s.Key!="3")==0,"eligibility denial bypassed or blocked valid attacks");
        bar=new(0,[bash,fast,drain with{RemainingCooldown=1}]);
        Check(Pick(low,Pack(3,16))==-1 && Pick(low,wounded)==0,"game cooldown bypassed");
        bar=new(0,[bash,fast,drain with{Locked=true}]);Check(Pick(low,Pack(3,16))==-1,"slot lock bypassed");
        bar=new(0,[bash,fast,drain]);retry['3']=1001;Check(Pick(low,Pack(3,16))==-1,"retry deadline bypassed");
        retry['3']=1000;Check(Pick(low,Pack(3,16))==2,"elapsed retry deadline not released");retry.Clear();
        foreach(var pack in new[]{Pack(1,15),Pack(3,16),Pack(4,79),Pack(10,80),Pack(10,100)})
            Check(Pick(new(900,1000),pack)==-1,"ordinary skills bypassed pack count/HP");
        Check(Pick(new(900,1000),wounded)==0 && Pick(new(900,1000),wounded,cursor:1)==1,"normal attack rotation changed");
        Check(Pick(new(900,1000),wounded,warm:false)==-1 && Pick(new(900,1000),wounded,delay:false)==-1,"offensive warmup/delay bypassed");
        rule.HealthConditionKeys="1";
        bar=new(0,[bash,fast,drain with{RemainingCooldown=1}]);
        Check(!CombatSkillPolicy.IsPriorityHeal(bash,rule) && Pick(low,Pack(3,16))==-1,"extra HP key made an ordinary attack bypass pack rules");
        Check(Pick(low,wounded,delay:false)==0,"existing extra-key delay exemption changed");
        rule.HealthConditionKeys="";bar=new(0,[bash,fast,drain]);
        rule.HealthSkillCondition=false;
        Check(!CombatSkillPolicy.IsPriorityHeal(drain,rule) && Pick(low,Pack(3,16))==-1 && Pick(new(900,1000),wounded,cursor:2)==2,"disabled HP condition changed ordinary rotation");
        rule.HealthSkillCondition=true;rule.HealerMode=true;
        Check(!CombatSkillPolicy.IsPriorityHeal(drain,rule),"recipient healing treated as combat self-healing");
        rule.HealerMode=false;
        bar=new(0,[bash,fast,drain with{Name="Greater Healing Lv.1",SkillTarget=SkillTargetKind.Friend}]);
        Check(Pick(low,Pack(1,100))==2,"recognized direct heal still required a full pack");
        Check(!SkillHealthRule.CanActivate(drain,drain,new(401,1000),rule) &&
            !SkillHealthRule.CanActivate(drain,drain with{Id=4},low,rule),"post-selection HP/identity check bypassed");
    }
}
