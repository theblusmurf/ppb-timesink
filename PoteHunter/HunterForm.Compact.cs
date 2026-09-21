namespace PoteHunter;

public sealed partial class HunterForm
{
    readonly ComboBox compactMode=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=175,AccessibleName="Operating mode"};
    readonly NumericUpDown compactHealBelow=Number(1,100);
    readonly Label compactSaved=new(){Text="Autosave on",AutoSize=true,ForeColor=UiMuted,Margin=new Padding(12,6,0,0)};
    readonly System.Windows.Forms.Timer compactSaveTimer=new(){Interval=600};
    bool compactSync;
    static Label Caption(string text)=>new(){Text=text,AutoSize=true,Margin=new Padding(8,5,4,0),ForeColor=UiMuted};
    static FlowLayoutPanel CompactFlow(params Control[] controls)
    {
        var flow=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,WrapContents=true,Margin=Padding.Empty};
        foreach(var control in controls.SelectMany(c=>c is FlowLayoutPanel panel?panel.Controls.Cast<Control>().ToArray():new[]{c}).ToArray())
        {
            control.Anchor=AnchorStyles.Left;control.Dock=DockStyle.None;
            if(control is NumericUpDown)control.Width=65;
            flow.Controls.Add(control);
        }
        return flow;
    }
    static TableLayoutPanel CompactTable()
    {
        var table=new TableLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,ColumnCount=1,RowCount=0,Margin=Padding.Empty};
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));return table;
    }
    static void CompactAdd(TableLayoutPanel table,Control control)
    {
        int row=table.RowCount++;table.RowStyles.Add(new RowStyle(SizeType.AutoSize));table.Controls.Add(control,0,row);
    }
    static TableLayoutPanel CompactRow(string label,params Control[] controls)
    {
        var row=new TableLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top,ColumnCount=2,RowCount=1,Margin=new Padding(0,0,0,5)};
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,122));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        row.Controls.Add(new Label{Text=label,AutoSize=true,ForeColor=UiMuted,Margin=new Padding(0,6,8,0)},0,0);
        var content=CompactFlow(controls);row.Controls.Add(content,1,0);
        row.RowStyles.Add(new RowStyle(SizeType.Absolute,34));
        bool measuring=false;
        row.Layout+=(_,_)=>
        {
            if(measuring)return;measuring=true;
            try
            {
                int height=Math.Max(31,content.GetPreferredSize(new Size(Math.Max(1,row.ClientSize.Width-122),0)).Height);
                if(row.RowStyles[0].Height!=height)row.RowStyles[0].Height=height;
            }
            finally{measuring=false;}
        };
        return row;
    }
    void ArrangeCompactSettings()
    {
        var old=settings.Controls.Cast<Control>().Select(c=>(Control:c,Row:settings.GetRow(c))).ToArray();
        settings.Controls.Clear();settings.RowStyles.Clear();settings.ColumnStyles.Clear();
        settings.RowCount=0;settings.ColumnCount=1;settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        compactMode.Items.AddRange(["Solo","Group combat","Healer","Group healer"]);
        priorityHint.SetToolTip(compactMode,"Group healer follows the selected party tank and heals nearby members. Other modes retain solo combat, group combat, or stationary healing.");
        compactMode.SelectedIndex=healerMode.Checked?(groupEnabled.Checked?3:2):(groupEnabled.Checked?1:0);
        compactHealBelow.Value=Math.Min(partyHealBelow.Value,healthSkillCondition.Checked?healthSkillPercent.Value:100);
        var operating=CompactCard("OPERATING MODE");
        var recovery=CompactCard("RECOVERY & RESERVES");
        CompactAdd(settings,operating);CompactAdd(settings,recovery);
        CompactAdd(operating,CompactRow("Mode",compactMode,compactSaved));
        tankPicker.Width=170;filter.Width=170;skillKeys.Width=135;healingSkillKeys.Width=135;
        var tankRow=CompactRow("Tank",tankPicker,Caption("Follow"),groupFollow);
        var targetRow=CompactRow("Targets",filter,Caption("Radius"),radius);
        var combatRow=CompactRow("Combat",ranged,archerClass,Caption("Range"),melee);
        var skillRow=CompactRow("Attack skills",autoSkills,skillKeys);
        var healingRow=CompactRow("Healing skills",autoHealingSkills,healingSkillKeys);
        var thresholdRow=CompactRow("Heal below",compactHealBelow,Caption("%   Range"),partyHealRange);
        foreach(var row in new[]{tankRow,targetRow,combatRow,skillRow,healingRow,thresholdRow})CompactAdd(operating,row);
        autoHeal.Text="HP items";autoMana.Text="MP items";
        CompactAdd(recovery,CompactRow("Items",autoHeal,healBelow,Caption("%"),autoMana,manaBelow,Caption("%")));
        healthSkillCondition.Text="Use at HP ≤";
        var selfRow=CompactRow("Self-heal skills",healthSkillCondition,healthSkillPercent,Caption("%"));CompactAdd(recovery,selfRow);
        manaReserve.Width=170;manaReserve.TickStyle=TickStyle.None;
        CompactAdd(recovery,CompactRow("Mana reserve",manaReserve,manaReserveValue));
        var advanced=CompactCard("ADVANCED SETTINGS");advanced.Visible=false;
        CompactAdd(advanced,CompactRow("Social",greetPlayers));
        CompactAdd(advanced,CompactRow("Character",player));
        CompactAdd(advanced,CompactRow("Allowed colors",difficultyBoxes.Values.Cast<Control>().ToArray()));
        CompactAdd(advanced,CompactRow("Timing",Caption("Skill retry (s)"),skillSeconds,Caption("Loot hold (ms)"),lootHold));
        CompactAdd(advanced,CompactRow("Item delays",Caption("HP (s)"),healDelay,Caption("MP (s)"),manaDelay));
        CompactAdd(advanced,CompactRow("Healing",Caption("Charge (ms)"),healCharge,Caption("Extra keys"),healthConditionKeys));
        CompactAdd(advanced,CompactRow("Group limits",Caption("Attack radius"),groupAttack,Caption("Follow limit"),groupLimit));
        foreach(int oldRow in new[]{6,7,8,9,12,13})
        {
            var fields=old.Where(f=>f.Row==oldRow).Select(f=>f.Control).ToArray();
            if(fields.Length>0)CompactAdd(advanced,CompactRow(fields.OfType<Label>().FirstOrDefault()?.Text??"Automation",fields.Where(c=>c is not Label).ToArray()));
        }
        var toggle=new Button{Name="advancedSettingsToggle",Text="+  Advanced settings",AutoSize=true,Anchor=AnchorStyles.Left,Margin=new Padding(0,4,0,8)};
        bool expanded=false;toggle.Click+=(_,_)=>{expanded=!expanded;advanced.Visible=expanded;toggle.Text=expanded?"−  Advanced settings":"+  Advanced settings";};
        var actions=CompactFlow(toggle);actions.Name="setupActions";CompactAdd(settings,actions);CompactAdd(settings,advanced);
        healerMode.Visible=false;groupEnabled.Visible=false;
        if(groupPage.Controls.OfType<TableLayoutPanel>().FirstOrDefault() is { } layout)
        {if(layout.Controls.OfType<FlowLayoutPanel>().FirstOrDefault() is { } header)header.Visible=false;layout.RowStyles[0]=new RowStyle(SizeType.Absolute,0);}
        void RefreshMode()
        {
            tankRow.Visible=groupEnabled.Checked;targetRow.Visible=!healerMode.Checked;combatRow.Visible=!healerMode.Checked;
            skillRow.Visible=!healerMode.Checked;healingRow.Visible=healerMode.Checked;thresholdRow.Visible=healerMode.Checked;selfRow.Visible=!healerMode.Checked;
            if(!compactSync){compactSync=true;compactHealBelow.Value=Math.Min(partyHealBelow.Value,healthSkillCondition.Checked?healthSkillPercent.Value:100);compactSync=false;}
        }
        compactMode.SelectedIndexChanged+=(_,_)=>
        {
            if(compactSync)return;bool wasBusy=busy;busy=true;compactSync=true;
            try{healerMode.Checked=compactMode.SelectedIndex>=2;groupEnabled.Checked=compactMode.SelectedIndex is 1 or 3;if(compactMode.SelectedIndex!=0)rangedPullEnabled.Checked=false;}
            finally{busy=wasBusy;compactSync=false;}
            RefreshMode();QueueCompactSave();
        };
        void SyncMode()
        {
            if(compactSync)return;compactSync=true;
            compactMode.SelectedIndex=healerMode.Checked?(groupEnabled.Checked?3:2):(groupEnabled.Checked?1:0);
            compactSync=false;RefreshMode();
        }
        healerMode.CheckedChanged+=(_,_)=>SyncMode();groupEnabled.CheckedChanged+=(_,_)=>SyncMode();
        compactHealBelow.ValueChanged+=(_,_)=>
        {
            if(compactSync)return;bool wasBusy=busy;busy=true;compactSync=true;
            try{partyHealBelow.Value=compactHealBelow.Value;healthSkillPercent.Value=compactHealBelow.Value;}
            finally{busy=wasBusy;compactSync=false;}
            QueueCompactSave();
        };
        healthSkillPercent.ValueChanged+=(_,_)=>RefreshMode();partyHealBelow.ValueChanged+=(_,_)=>RefreshMode();healthSkillCondition.CheckedChanged+=(_,_)=>RefreshMode();
        RefreshMode();
        supportSettings.Controls.Clear();supportSettings.ColumnStyles.Clear();supportSettings.RowStyles.Clear();supportSettings.RowCount=0;supportSettings.ColumnCount=1;
        supportSettings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        supportSettings.Padding=Padding.Empty;supportSettings.BackColor=UiWindow;
        var supportCard=CompactCard("AUTOMATIC SUPPORT");CompactAdd(supportSettings,supportCard);
        maintainBuffs.Text="Skill buffs";
        CompactAdd(supportCard,CompactRow("Maintain",maintainBuffs,attackPotions,defensePotions));
        CompactAdd(supportCard,new Label{Text="Choose your mode and healing thresholds in Setup. Place skills and potions on the active hotbar.",AutoSize=true,MaximumSize=new Size(580,0),Margin=new Padding(0,8,0,16),ForeColor=UiMuted});
        CompactAdd(supportCard,buffStatus);CompactAdd(supportCard,potionStatus);
    }

    void CheckCompactControls()
    {
        for(int index=0;index<4;index++)
        {
            compactMode.SelectedIndex=index;SaveCompactSettings();var saved=Options.Read();
            if(saved.HealerMode!=(index>=2)||saved.GroupMode!=(index is 1 or 3))throw new Exception("Compact mode mapping failed.");
        }
        compactHealBelow.Value=72;SaveCompactSettings();
        var healSettings=Options.Read();
        if(healSettings.PartyHealBelowPercent!=72 || healSettings.HealthSkillPercent!=72)
            throw new Exception("Unified healing threshold did not update both eligibility rules.");
        filter.Text="Compact autosave check";QueueCompactSave();
        var wait=System.Diagnostics.Stopwatch.StartNew();
        while(compactSaveTimer.Enabled && wait.ElapsedMilliseconds<2000){Application.DoEvents();Thread.Sleep(10);}
        if(Options.Read().Target!="Compact autosave check" || compactSaved.Text!="Saved")throw new Exception("Compact autosave failed.");
        string before=File.ReadAllText(Options.PathName);healthConditionKeys.Text="Q";SaveCompactSettings();
        if(File.ReadAllText(Options.PathName)!=before||compactSaved.Text!="Check settings")throw new Exception("Invalid compact settings were written.");
        healthConditionKeys.Text="";filter.Text="";SaveCompactSettings();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"compact-ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{Passed=true,Checks=new[]{"four persisted modes","single effective healing threshold","autosave feedback","invalid edits preserve saved settings"}}));
    }
    void QueueCompactSave()
    {
        if(working||busy||compactSync)return;compactSaved.Text="Unsaved changes";compactSaveTimer.Stop();compactSaveTimer.Start();
    }
    void SaveCompactSettings()
    {
        compactSaveTimer.Stop();if(working||busy)return;
        try{CurrentOptions().Save();compactSaved.Text="Saved";}
        catch(Exception ex){compactSaved.Text="Check settings";message=ex.Message;priorityHint.SetToolTip(compactSaved,ex.Message);}
    }
    void WireCompactSaving(Control root)
    {
        void Wire(Control control)
        {
            if(control is TextBoxBase text && !text.ReadOnly)text.Validated+=(_,_)=>QueueCompactSave();
            if(control is NumericUpDown number)number.ValueChanged+=(_,_)=>QueueCompactSave();
            if(control is CheckBox check)check.CheckedChanged+=(_,_)=>QueueCompactSave();
            if(control is ComboBox combo)combo.SelectionChangeCommitted+=(_,_)=>QueueCompactSave();
            if(control is DataGridView grid){grid.CellEndEdit+=(_,_)=>QueueCompactSave();grid.UserDeletedRow+=(_,_)=>QueueCompactSave();}
            if(control is Button button && button.Text.StartsWith("Save ",StringComparison.Ordinal))button.Visible=false;
            foreach(Control child in control.Controls)Wire(child);
        }
        Wire(root);compactSaveTimer.Tick+=(_,_)=>SaveCompactSettings();
        FormClosing+=(_,_)=>{if(compactSaveTimer.Enabled)SaveCompactSettings();};FormClosed+=(_,_)=>compactSaveTimer.Dispose();
    }
}

