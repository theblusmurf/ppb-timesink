using System.Text.Json;
namespace PoteHunter;

public static class PotionChecks
{
    public static void Run()
    {
        var checks=new List<string>();
        void Check(bool ok,string name){if(!ok)throw new Exception("Potions: "+name);checks.Add(name);}
        var item=new HotbarSlot("7",SlotKind.Item,700,"Test potion",0,0,false,0,"Potion");
        var hp=item with{Description="Restores 1,200 health points."};
        var mp=item with{Description="Replenishes 500 MP."};
        var both=item with{Description="Recovers health and mana points."};
        Check(RecoveryItems.Recognized(hp)&&!ManaRecovery.Recognized(hp),"HP-only potion classification");
        Check(ManaRecovery.Recognized(mp)&&!RecoveryItems.Recognized(mp),"MP-only potion and abbreviation");
        Check(RecoveryItems.Recognized(both)&&ManaRecovery.Recognized(both),"combined amountless restoration");
        Check(RecoveryItems.Parse("Potion",hp.Description)==(1200,0)&&RecoveryItems.Parse("Potion",mp.Description)==(0,500),"restoration amount parsing");
        Check(RecoveryItems.Parse("Food","Restores 300 health and 100 mana points.")== (300,100),"existing combined food preserved");
        Check(RecoveryItems.Recognized(item with{Description="Restores 25% HP."})&&RecoveryItems.Parse("Potion","Restores 25% HP.")== (0,0),"percentage recovery never treated as fixed points");
        Check(!RecoveryItems.Recognized(hp with{Kind=SlotKind.Skill})&&!ManaRecovery.Recognized(mp with{Category="Sword"}),"nonitems and equipment rejected");
        Check(!RecoveryItems.Recognized(item with{Description="Increases maximum health by 500."})&&!ManaRecovery.Recognized(item with{Description="Increases mana regeneration."}),"stat buffs are not recovery items");
        Check(RecoveryItems.Choose(new(0,[hp with{Locked=true}]),0)==null&&ManaRecovery.ChooseReady(new(0,[mp with{RemainingCooldown=1}]))==null,"recovery cooldown and lock enforced");
        var attack=item with{Name="Test attack potion",Description="Increases attack power by 20 for 5 minutes."};
        var defense=item with{Key="8",Id=701,Name="Test defense potion",Description="Raises defense by 10 for 60 seconds."};
        Check(PotionItems.Buff(attack)==PotionBuff.Attack&&PotionItems.Buff(defense)==PotionBuff.Defense,"attack and defense descriptions");
        var combined=attack with{Description="Increases attack and defense for 2 minutes."};
        Check(PotionItems.Buff(combined)==(PotionBuff.Attack|PotionBuff.Defense)&&
            !new PotionUpkeep().Evaluate("self",new(0,[combined]),new(false,"",[]),true,false,0)[0].Use,"combined buffs require both switches");
        Check(PotionItems.Buff(attack with{Category="Sword"})==PotionBuff.None&&PotionItems.Buff(attack with{Description="Reduces enemy defense."})==PotionBuff.None,"equipment and debuffs excluded");
        Check(PotionItems.DurationSeconds(attack)==300&&PotionItems.DurationSeconds(defense)==60,"duration units");
        var unknown=attack with{Description="Increases attack power by 20."};
        var unavailable=new ActiveEffectSnapshot(false,"Unavailable",[]);
        var policy=new PotionUpkeep();var bar=new HotbarSnapshot(0,[attack,defense]);
        Check(policy.Evaluate("self",bar,unavailable,false,false,0).All(d=>!d.Use),"buff potions opt in");
        var due=policy.Evaluate("self",bar,unavailable,true,false,0);
        Check(due[0].Use&&!due[1].Use,"independent attack and defense switches");
        policy.RecordAttempt(due[0],0);
        Check(!policy.Evaluate("self",bar,unavailable,true,true,299999)[0].Use&&policy.Evaluate("self",bar,unavailable,true,true,300000)[0].Use,"full duration before reuse");
        Check(!policy.Evaluate("self",bar with{PageBase=10,Slots=[attack with{Key="9",Id=999}]},unavailable,true,true,1000)[0].Use,"page slot and tier changes do not bypass duration");
        Check(policy.Evaluate("other",bar,unavailable,true,true,1000)[0].Use,"character change clears prior character timer");
        Check(!new PotionUpkeep().Evaluate("self",new(0,[unknown]),unavailable,true,true,0)[0].Use,"unknown duration and effect held");
        var active=new ActiveEffectSnapshot(true,"",[new(1,attack.Name,"",1,0)]);
        Check(!new PotionUpkeep().Evaluate("self",bar,active,true,true,0)[0].Use,"active effect overrides zero display timer");
        Check(!new PotionUpkeep().Evaluate("self",new(0,[attack with{Locked=true}]),unavailable,true,true,0)[0].Use,"buff cooldown and lock enforced");
        var options=JsonSerializer.Deserialize<Options>(JsonSerializer.Serialize(new Options{UseAttackPotions=true,UseDefensePotions=true}))!;
        Check(options.UseAttackPotions&&options.UseDefensePotions&&!JsonSerializer.Deserialize<Options>("{}")!.UseAttackPotions,"settings roundtrip and opt-in migration");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"potion-checks.json"),JsonSerializer.Serialize(new{Passed=true,LiveGameTested=false,Checks=checks},new JsonSerializerOptions{WriteIndented=true}));
    }
}
