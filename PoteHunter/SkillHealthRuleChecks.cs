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
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"skill-health-rule-checks.json"),JsonSerializer.Serialize(new{Passed=true,HardwareInputEmitted=false,LiveGameTested=false,Checks=new[]{"name detection and exclusions","inclusive unrounded 50% boundary","character HP in combat","recipient HP in healer mode","missing/dead HP","cooldown lock and retry preserved","other attacks continue","post-selection activation recheck","manual key and configurable threshold","settings roundtrip"}},new JsonSerializerOptions{WriteIndented=true}));
    }
}
