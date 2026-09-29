namespace PoteHunter;

public sealed partial class HunterForm
{
    readonly CheckBox attackPotions=new(){Text="Attack potions",AutoSize=true};
    readonly CheckBox defensePotions=new(){Text="Defense potions",AutoSize=true};
    readonly Label potionStatus=new(){AutoSize=true,MaximumSize=new Size(750,0)};
    readonly PotionUpkeep potionUpkeep=new();

    void AddPotionSupport()
    {
        var row=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top};
        row.Controls.AddRange([attackPotions,defensePotions]);
        supportSettings.Controls.Add(row,0,5);supportSettings.SetColumnSpan(row,4);
        var help=new Label{Text="Place potions on the active hotbar. HP / MP use your recovery thresholds. Buff potions are used between fights; unknown durations or effects are skipped.",AutoSize=true,MaximumSize=new Size(750,0),Margin=new Padding(3,6,3,6)};
        supportSettings.Controls.Add(help,0,6);supportSettings.SetColumnSpan(help,4);
        supportSettings.Controls.Add(potionStatus,0,7);supportSettings.SetColumnSpan(potionStatus,4);
        priorityHint.SetToolTip(healerMode,"Enable Group mode and select a party tank to follow while healing. Group attack radius is unused. Keep follow distance within party heal range; both healing HP thresholds still apply.");
        try{var o=Options.Read();attackPotions.Checked=o.UseAttackPotions;defensePotions.Checked=o.UseDefensePotions;}catch{}
        void Save(){if(!working&&!busy){try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}}}
        attackPotions.CheckedChanged+=(_,_)=>Save();defensePotions.CheckedChanged+=(_,_)=>Save();
        potionStatus.Text="Connect to detect slotted attack / defense potions.";
    }

    IReadOnlyList<PotionDecision> ObservePotionBuffs(Options o,HotbarSnapshot bar)
    {
        var self=world.LocalPlayer();
        // Page changes do not reset the protection against duplicate doses.
        var decisions=potionUpkeep.Evaluate($"{self.Id}:{self.Generation}:{self.Name}",bar,world.ActiveEffects(),o.UseAttackPotions,o.UseDefensePotions,Environment.TickCount64);
        potionStatus.Text=decisions.Count==0?"No attack / defense potions detected on this hotbar.":
            string.Join("\n",decisions.Select(d=>$"{d.Slot.Key}: {d.Slot.Name} — {d.Status}"));
        return decisions;
    }

    async Task<bool> TryUseBuffPotion(Options o,CancellationToken token)
    {
        if(!o.UseAttackPotions&&!o.UseDefensePotions)return false;
        var hp=world.TargetHealth(world.LocalPlayer().Id);
        if(!hp.Known)return false;
        if(hp.Dead)
        {
            if(o.AutoReviveAfterDeath)deathRecoveryRequested=true;
            return false;
        }
        if(o.AutoHeal && hp.Current*100m<=hp.Maximum*o.HealBelowPercent)return false;
        var decision=ObservePotionBuffs(o,CheckedHotbar()).FirstOrDefault(d=>d.Use);
        if(decision==null)return false;
        var current=CheckedHotbar().Slot(decision.Slot.Key[0]);
        if(current.Id!=decision.Slot.Id || current.Name!=decision.Slot.Name || !current.Ready || PotionItems.Buff(current)!=decision.Buff)return false;
        // Re-evaluate live effects immediately before input.
        decision=ObservePotionBuffs(o,CheckedHotbar()).FirstOrDefault(d=>d.Use && d.Slot.Key==current.Key && d.Slot.Id==current.Id);
        if(decision==null)return false;
        movement?.StopApproach();ReleaseCombatPickup();ClearRangedPending();Input.Release(preserveNearbyPickup:true);
        potionUpkeep.RecordAttempt(decision,Environment.TickCount64);
        message=$"Using {decision.Buff.ToString().ToLowerInvariant()} potion: {current.Name}";
        TraceLog.Record("buff potion attempt",new{current.Id,current.Name,current.Key,decision.Buff,decision.Duration});
        await Input.Key((Keys)current.Key[0],70,token);
        await Input.Delay(150,token);
        ObservePotionBuffs(o,CheckedHotbar());
        return true;
    }
}
