using System.Globalization;
using System.Text.RegularExpressions;

namespace PoteHunter;

[Flags]
public enum PotionBuff { None=0, Attack=1, Defense=2 }

public static class PotionItems
{
    const RegexOptions Flags = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    public static bool Consumable(string category) => category.Trim().Equals("Food",StringComparison.OrdinalIgnoreCase) ||
        category.Trim().Equals("Potion",StringComparison.OrdinalIgnoreCase);

    public static (bool Health,bool Mana,int HealthAmount,int ManaAmount) Recovery(string category,string description)
    {
        if(!Consumable(category))return default;
        // Recognize explicit restoration, never a maximum-stat or regeneration buff.
        const string resource=@"(?:[\d,]+(?:\.\d+)?\s*%?\s+)?(?:health|mana|hp|mp)(?:\s+points?)?";
        var clause=Regex.Match(description,@"^\s*(?:restores?|recovers?|replenishes?)\s+(?<effects>"+resource+@"(?:\s*(?:and|&|/|,)\s*"+resource+@")*)\s*\.?\s*$",Flags);
        if(!clause.Success)return default;
        bool hp=false,mp=false;int health=0,mana=0;
        foreach(Match effect in Regex.Matches(clause.Groups["effects"].Value,@"(?:(?<amount>[\d,]+(?:\.\d+)?)(?<percent>\s*%)?\s+)?(?<kind>health|mana|hp|mp)\b",Flags))
        {
            int amount=0;
            if(!effect.Groups["percent"].Success)int.TryParse(effect.Groups["amount"].Value.Replace(",",""),out amount);
            if(Regex.IsMatch(effect.Groups["kind"].Value,@"^(health|hp)$",Flags)){hp=true;health=amount;}
            else{mp=true;mana=amount;}
        }
        return(hp,mp,health,mana);
    }

    public static PotionBuff Buff(HotbarSlot slot)
    {
        if(slot.Kind!=SlotKind.Item || !slot.Category.Trim().Equals("Potion",StringComparison.OrdinalIgnoreCase))return PotionBuff.None;
        PotionBuff result=PotionBuff.None;
        foreach(Match clause in Regex.Matches(slot.Description,@"(?:^|[.\n;])\s*(?:increases?|raises?|boosts?)\s+(?<body>[^.\n;]+)",Flags))
            foreach(Match match in Regex.Matches(clause.Groups["body"].Value,@"(?:^|\band\s+)(?:your\s+)?(?:physical\s+|magical?\s+)?(?<stat>attack(?:\s+power)?|damage|defen[cs]e|armou?r)\b",Flags))
                result |= Regex.IsMatch(match.Groups["stat"].Value,@"^(attack|damage)",Flags)?PotionBuff.Attack:PotionBuff.Defense;
        return result;
    }

    public static double DurationSeconds(HotbarSlot slot)
    {
        double longest=0;
        foreach(Match match in Regex.Matches(slot.Description,@"\b(?:for|duration\s*:?)\s*(?<value>\d+(?:\.\d+)?)\s*(?<unit>seconds?|secs?|minutes?|mins?|hours?|hrs?)\b",Flags))
        {
            if(!double.TryParse(match.Groups["value"].Value,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out double value))continue;
            string unit=match.Groups["unit"].Value.ToLowerInvariant();
            double seconds=value*(unit.StartsWith("h")?3600:unit.StartsWith("m")?60:1);
            if(seconds is >0 and <=86400)longest=Math.Max(longest,seconds);
        }
        return longest;
    }

    public static string Role(HotbarSlot slot)
    {
        var roles=new List<string>();
        if(RecoveryItems.Recognized(slot))roles.Add("HP recovery");
        if(ManaRecovery.Recognized(slot))roles.Add("MP recovery");
        var buff=Buff(slot);
        if(buff.HasFlag(PotionBuff.Attack))roles.Add("Attack buff potion");
        if(buff.HasFlag(PotionBuff.Defense))roles.Add("Defense buff potion");
        return string.Join(" + ",roles);
    }
}

public sealed record PotionDecision(HotbarSlot Slot,PotionBuff Buff,bool Use,string Status,double Duration);

public sealed class PotionUpkeep
{
    readonly Dictionary<PotionBuff,long> heldUntil=new();
    string? identity;
    public IReadOnlyList<PotionDecision> Evaluate(string character,HotbarSnapshot bar,ActiveEffectSnapshot effects,bool attack,bool defense,long now)
    {
        if(identity!=character){identity=character;heldUntil.Clear();}
        return bar.Slots.Where(s=>PotionItems.Buff(s)!=PotionBuff.None).Select(slot=>
        {
            var buff=PotionItems.Buff(slot);var effect=effects.Match(slot.Name);
            double duration=PotionItems.DurationSeconds(slot);
            string reason;
            if((buff.HasFlag(PotionBuff.Attack)&&!attack)||(buff.HasFlag(PotionBuff.Defense)&&!defense))reason="Disabled";
            else if(effect is{Active:true})reason="Buff active";
            else if(new[]{PotionBuff.Attack,PotionBuff.Defense}.Any(k=>buff.HasFlag(k)&&heldUntil.GetValueOrDefault(k)>now))reason="Previous potion duration / retry delay";
            else if(effect==null && duration<=0)reason="Unknown duration / effect — not used";
            else if(!slot.Ready)reason="Cooling down or locked";
            else return new PotionDecision(slot,buff,true,"Ready",duration);
            return new PotionDecision(slot,buff,false,reason,duration);
        }).ToArray();
    }
    public void RecordAttempt(PotionDecision decision,long now)
    {
        // An unconfirmed input still waits the full duration, avoiding repeated
        // consumption when the game's effect name differs from the item name.
        long until=now+(long)(Math.Max(10,decision.Duration)*1000);
        foreach(var kind in new[]{PotionBuff.Attack,PotionBuff.Defense})
            if(decision.Buff.HasFlag(kind))heldUntil[kind]=until;
    }
}
