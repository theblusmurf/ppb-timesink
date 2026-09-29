using System.Text.RegularExpressions;

namespace PoteHunter;

public static class SkillHealthRule
{
    public static Health ChooseHealth(bool healerMode,Health character,Health? recipient)=>healerMode?recipient??default:character;
    public static string NormalizeKeys(string? value)
    {
        string keys=new((value??"").Where(c=>!char.IsWhiteSpace(c) && c!=',').ToArray());
        if(keys.Any(c=>!"1234567890".Contains(c)) || keys.Distinct().Count()!=keys.Length)
            throw new InvalidOperationException("HP-condition keys must be unique digits from 1 through 0.");
        return keys;
    }
    // Name-only matching: damage descriptions often mention enemy health.
    // Explicit keys cover unfamiliar/localized names without guessing effects.
    public static bool AutomaticMatch(HotbarSlot slot)
    {
        if(slot.Kind!=SlotKind.Skill)return false;
        string name=slot.Name;
        if(Regex.IsMatch(name,@"\bpower\s+drain\b",RegexOptions.IgnoreCase))return true;
        if(Regex.IsMatch(name,@"\b(mana|mp)\b",RegexOptions.IgnoreCase))return false;
        return Regex.IsMatch(name,@"\b(heal|healing|greater\s+heal|first\s+aid|regeneration|regenerate|recovery|recover|cure|mend|renew|restore)\b",RegexOptions.IgnoreCase);
    }
    public static bool Applies(HotbarSlot slot,Options options)=>slot.Kind==SlotKind.Skill &&
        options.HealthSkillCondition && (AutomaticMatch(slot) || slot.Key.Length==1 && (options.HealthConditionKeys??"").Contains(slot.Key[0]));
    public static bool Allows(HotbarSlot slot,Health character,Options options)
    {
        if(!Applies(slot,options))return true;
        return options.HealthSkillPercent>0 && options.HealthSkillPercent<=100 && character.Known && !character.Dead &&
            (decimal)character.Current*100 <= options.HealthSkillPercent*character.Maximum;
    }
    public static bool ReserveExempt(HotbarSlot slot,bool healerMode,uint characterId,uint? recipientId)=>
        AutomaticMatch(slot) && (!healerMode || recipientId==characterId);
    public static bool CanActivate(HotbarSlot expected,HotbarSlot current,Health character,Options options)=>
        current.Kind==SlotKind.Skill && current.Id==expected.Id && current.Name==expected.Name && current.Key==expected.Key &&
        current.SkillUse==expected.SkillUse && current.SkillTarget==expected.SkillTarget && current.Ready && Allows(current,character,options);
}

public sealed partial class HunterForm
{
    readonly CheckBox healthSkillCondition=new(){Text="Power Drain / heals at HP ≤",Checked=true,AutoSize=true};
    readonly NumericUpDown healthSkillPercent=Number(1,100);
    readonly TextBox healthConditionKeys=new(){Width=90,PlaceholderText="Optional"};

    Health SkillConditionHealth(Options options)
    {
        if(!options.HealerMode)return SkillHealthRule.ChooseHealth(false,world.TargetHealth(world.LocalPlayer().Id),null);
        if(activeHealTarget is not HealTarget recipient)return SkillHealthRule.ChooseHealth(true,default,null);
        var live=world.Find(recipient.Entity.Id);
        if(live==null || TargetIdentity(live)!=TargetIdentity(recipient.Entity) || live.Model!=recipient.Entity.Model)return default;
        if(live.Name!=recipient.Entity.Name && !(options.GroupMode && string.IsNullOrWhiteSpace(live.Name) &&
            world.Party().Members.Any(m=>m.Id==live.Id && m.Name==recipient.Member.Name)))return default;
        return SkillHealthRule.ChooseHealth(true,default,world.TargetHealth(live.Id));
    }
    bool HealthSkillAllowed(HotbarSlot slot,Options options)=>SkillHealthRule.Allows(slot,SkillConditionHealth(options),options) && ManaSkillAllowed(slot,options);

    SkillGroupStatus CombatSkillGroup(Entity current,Health currentHealth,Vec position,Options options)
    {
        var health=new Dictionary<uint,Health>(world.HealthSnapshot());
        if(currentHealth.Known)health[current.Id]=currentHealth;
        return SkillGroupGate.Evaluate(encounter.EngagedCandidates,current,health,position,
            Math.Max(0,(double)options.NearbyEnemyRadius));
    }

    void ResumeBasicAttackAfterSkill(Entity target,Options options,CancellationToken token,string mode)
    {
        // A failed re-check or the fallback release below must never leave the
        // left button up.  Re-issue the idempotent hold even when the input
        // tracker already reports it held; this makes every skill exit an
        // explicit hand-off back to the swing and avoids a stale held-state
        // decision starving the next attack animation.
        if(token.IsCancellationRequested || !Input.Allowed())return;
        try
        {
            bool wasHeld=Input.BasicAttackHeld;
            Input.HoldMouse(false,true,token);
            TraceLog.Record(wasHeld ? "basic attack preserved after skill" : "basic attack rearmed after skill",
                new{target.Id,target.DisplayName,Mode=mode});
        }
        catch(InvalidOperationException ex)
        {
            // Preserve the combat loop's original exception/cleanup path. A
            // later pass will retry the re-arm once the input guard is usable.
            TraceLog.Record("basic attack rearm deferred",new{target.Id,target.DisplayName,Mode=mode,Error=ex.Message});
        }
    }

    async Task<bool> CastHealthCheckedSkill(HotbarSlot expected,Options options,CancellationToken token)
    {
        // Recheck after selecting a key and before every activation/retry. A
        // potion or ally may have healed us during the intervening key delay.
        var current=CheckedHotbar().Slot(expected.Key[0]);
        var hp=SkillConditionHealth(options);
        if(!SkillHealthRule.CanActivate(expected,current,hp,options) || !ManaSkillAllowed(current,options))
        {
            TraceLog.Record("skill activation withheld",new{expected.Key,expected.Name,hp.Current,hp.Maximum,HealthSource=options.HealerMode?"Healing target":"Character",Threshold=options.HealthSkillPercent,Condition=SkillHealthRule.Applies(expected,options),current.Ready});
            return false;
        }
        await Input.CastSkill(current.SkillUse,(int)options.HealChargeMilliseconds,token);
        return true;
    }
}
