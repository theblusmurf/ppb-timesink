namespace PoteHunter;

public static class ManaReserveRule
{
    public static bool Allows(HotbarSlot slot,ManaReading mana,int percent,bool exempt=false)
    {
        if(percent==0)return true;
        if(percent is <0 or >100)return false;
        if(exempt)return true;
        return slot.Kind!=SlotKind.Skill || mana.Known && slot.ManaCost is >=0 &&
            (decimal)(mana.Current-slot.ManaCost.Value)*100>=percent*(decimal)mana.Maximum;
    }

    public static int? Cost(SkillUseKind use,IReadOnlyList<SkillManaCost>? ranks,int rank)
    {
        if(ranks==null||ranks.Count!=5||rank is <0 or >4||ranks.Any(cost=>cost.Base<0||cost.Extra<0))return null;
        if(use is SkillUseKind.Instance or SkillUseKind.Chant)return ranks[rank].Base+ranks[rank].Extra;
        if(use==SkillUseKind.Cast)return ranks.Take(rank+1).Max(cost=>cost.Base)+ranks.Take(rank+1).Max(cost=>cost.Extra)+6;
        return null;
    }
}
