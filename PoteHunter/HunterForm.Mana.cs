namespace PoteHunter;

public sealed partial class HunterForm
{
    readonly CheckBox autoMana = new() { Text="Use MP items",AutoSize=true };
    readonly NumericUpDown manaBelow = Number(1,95);
    readonly NumericUpDown manaDelay = Number(1,120);
    readonly TrackBar manaReserve = new(){Minimum=0,Maximum=100,TickFrequency=10,SmallChange=1,LargeChange=5,Width=180,Height=28,AutoSize=false};
    readonly Label manaReserveValue = new(){AutoSize=true,MinimumSize=new Size(62,0),Padding=new Padding(0,6,0,0)};
    readonly CheckBox continuousCombatPositioning = new(){Text="Smooth side-step: keep targets in front",AutoSize=true,Checked=true};
    readonly Label gatheringStatusLabel=new(){AutoSize=true,Padding=new Padding(0,4,0,0)};
    readonly ManaRecovery manaRecovery = new();
    string manaRecoveryStatus = "Not connected";

    void AddManaSettings()
    {
        manaBelow.Width=52;
        var row=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=Padding.Empty};
        row.Controls.AddRange([autoMana,new Label{Text="below",AutoSize=true,Padding=new Padding(0,4,0,0)},manaBelow,new Label{Text="%",AutoSize=true,Padding=new Padding(0,4,0,0)}]);
        settings.Controls.Add(new Label{Text="Mana recovery",AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(0,9,5,9)},0,10);
        settings.Controls.Add(row,1,10);
        settings.Controls.Add(new Label{Text="MP item delay (seconds)",AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(0,9,5,9)},2,10);
        manaDelay.Anchor=AnchorStyles.Left|AnchorStyles.Right;settings.Controls.Add(manaDelay,3,10);
        var reserveRow=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=Padding.Empty};reserveRow.Controls.AddRange([manaReserve,manaReserveValue]);
        settings.Controls.Add(new Label{Text="Mana reserve",AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(0,9,5,9)},0,11);
        settings.Controls.Add(reserveRow,1,11);settings.SetColumnSpan(reserveRow,3);
        settings.Controls.Add(new Label{Text="Combat movement",AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(0,9,5,9)},0,12);
        settings.Controls.Add(continuousCombatPositioning,1,12);settings.SetColumnSpan(continuousCombatPositioning,3);
        settings.Controls.Add(new Label{Text="Tight gathering",AutoSize=true,Anchor=AnchorStyles.Left},0,13);
        var gatherRow=new FlowLayoutPanel{AutoSize=true,WrapContents=true,Margin=Padding.Empty};gatherRow.Controls.AddRange([tightGathering,gatheringStatusLabel]);
        settings.Controls.Add(gatherRow,1,13);settings.SetColumnSpan(gatherRow,3);
        healthSkillPercent.Width=52;
        var healthRow=new FlowLayoutPanel{AutoSize=true,WrapContents=true,Margin=Padding.Empty};
        healthRow.Controls.AddRange([healthSkillCondition,healthSkillPercent,new Label{Text="%",AutoSize=true},new Label{Text="Extra keys",AutoSize=true},healthConditionKeys]);
        settings.Controls.Add(new Label{Text="Healing skills",AutoSize=true,Anchor=AnchorStyles.Left},0,14);
        settings.Controls.Add(healthRow,1,14);settings.SetColumnSpan(healthRow,3);
        priorityHint.SetToolTip(healthSkillCondition,"Power Drain and recognized healing skills use the character's HP during combat. In healer mode the selected recipient's HP is used. Self-heals may spend the mana reserve.");
        priorityHint.SetToolTip(tightGathering,"Briefly gather already engaged enemies within 0.5 map units, then resume attacks. Requires melee positioning.");
        priorityHint.SetToolTip(continuousCombatPositioning,"Uses short left/right movement pulses during melee swings to bring more engaged targets into the forward attack cone. Respects boundaries, routes, attack range, and group follow distance.");
        priorityHint.SetToolTip(autoMana,"Use a ready Food or Potion hotbar slot that restores MP when MP reaches this threshold. HP-only items are excluded.");
        try
        {
            var options=Options.Read();autoMana.Checked=options.AutoRestoreMana;
            manaBelow.Value=Math.Clamp(options.ManaBelowPercent,1,95);manaDelay.Value=Math.Clamp(options.ManaDelaySeconds,1,120);
            manaReserve.Value=Math.Clamp(options.ManaReservePercent,0,100);
            continuousCombatPositioning.Checked=options.ContinuousCombatPositioning;
            tightGathering.Checked=options.TightGathering;
            healthSkillCondition.Checked=options.HealthSkillCondition;
            healthSkillPercent.Value=Math.Clamp(options.HealthSkillPercent,1,100);
            healthConditionKeys.Text=options.HealthConditionKeys??"";
        }
        catch{autoMana.Checked=true;manaBelow.Value=30;manaDelay.Value=5;manaReserve.Value=0;healthSkillPercent.Value=50;}
        void UpdateReserveLabel()=>manaReserveValue.Text=manaReserve.Value==0?"0% (off)":$"{manaReserve.Value}%";
        UpdateReserveLabel();
        manaReserve.ValueChanged+=(_,_)=>{UpdateReserveLabel();if(!working&&!busy){try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}}};
        void SaveCombatSettings(){if(!working&&!busy){try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}}}
        tightGathering.CheckedChanged+=(_,_)=>SaveCombatSettings();
        healthSkillCondition.CheckedChanged+=(_,_)=>SaveCombatSettings();
        healthSkillPercent.ValueChanged+=(_,_)=>SaveCombatSettings();
        healthConditionKeys.Validated+=(_,_)=>SaveCombatSettings();
        continuousCombatPositioning.CheckedChanged+=(_,_)=>{if(!working&&!busy){try{CurrentOptions().Save();}catch(Exception ex){message=ex.Message;}}};
    }

    Options WithManaSettings(Options options)
    {
        options.AutoRestoreMana=autoMana.Checked;options.ManaBelowPercent=manaBelow.Value;options.ManaDelaySeconds=manaDelay.Value;options.ManaReservePercent=manaReserve.Value;options.ContinuousCombatPositioning=continuousCombatPositioning.Checked;
        options.TightGathering=tightGathering.Checked;
        options.HealthSkillCondition=healthSkillCondition.Checked;options.HealthSkillPercent=healthSkillPercent.Value;
        options.HealthConditionKeys=SkillHealthRule.NormalizeKeys(healthConditionKeys.Text);
        options.UseAttackPotions=attackPotions.Checked;options.UseDefensePotions=defensePotions.Checked;
        return options;
    }

    bool ManaSkillAllowed(HotbarSlot slot,Options options)
    {
        if(options.ManaReservePercent==0)return true;
        var mana=world.ReadMana();
        bool allowed=ManaReserveRule.Allows(slot,mana,options.ManaReservePercent,SkillHealthRule.ReserveExempt(slot,options.HealerMode,world.LocalPlayer().Id,activeHealTarget?.Member.Id));
        if(!allowed)manaRecoveryStatus=mana.Known&&slot.ManaCost.HasValue?
            $"MP {mana.Current}/{mana.Maximum} · holding {slot.Name} to keep {options.ManaReservePercent}%":"Mana reserve: cost unavailable";
        return allowed;
    }

    async Task<bool> TryRestoreMana(Options options,CancellationToken token)
    {
        if(!options.AutoRestoreMana){manaRecoveryStatus="MP items disabled";return false;}
        var self=world.LocalPlayer();var hp=world.TargetHealth(self.Id);
        if(!hp.Known || hp.Dead)throw new InvalidOperationException("Cannot use an MP item while character health is unavailable or dead.");
        var mana=world.ReadMana();
        if(!mana.Known){manaRecoveryStatus="MP reading unavailable";return false;}
        manaRecovery.Configure(new ManaRecoverySettings((double)options.ManaBelowPercent,TimeSpan.FromSeconds((double)options.ManaDelaySeconds)));
        var decision=manaRecovery.Evaluate(mana,CheckedHotbar(),DateTimeOffset.UtcNow);
        manaRecoveryStatus=decision.Reason;
        if(!decision.Ready)return false;
        var slot=decision.Slot!;
        ClearRangedPending();ReleaseCombatPickup();movement?.StopApproach();
        message=$"Using {slot.Name} on {slot.Key} · MP {mana.Current}/{mana.Maximum}";
        TraceLog.Record("mana item input",new{Key=slot.Key,slot.Name,mana.Current,mana.Maximum,Threshold=options.ManaBelowPercent});
        await Input.Key((Keys)slot.Key[0],70,token);
        manaRecovery.RecordUse(DateTimeOffset.UtcNow);
        if(RecoveryItems.Recognized(slot))nextHealAt=Math.Max(nextHealAt,Environment.TickCount64+(long)options.HealDelaySeconds*1000);
        await Input.Delay(150,token);
        TraceLog.Record("mana item observation",new{Before=mana,After=world.ReadMana(),Item=slot.Name});
        return true;
    }
}

