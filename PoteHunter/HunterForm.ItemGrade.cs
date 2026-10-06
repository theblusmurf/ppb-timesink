namespace PoteHunter;

public sealed partial class HunterForm
{
    readonly CheckBox itemGradeEnabled=new(){Text="Show stat grades and gem estimates for the hovered item",AutoSize=true,MaximumSize=new Size(680,0),Checked=true};
    readonly CheckBox itemGradeAutoShow=new(){Text="Show automatically when an item tooltip appears",AutoSize=true,MaximumSize=new Size(680,0),Checked=true};
    readonly ComboBox itemGradeTarget=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=90};
    readonly ComboBox itemGradeFocus=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=215};
    readonly ComboBox itemGradeHotkey=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=120};
    readonly Label itemGradeStatus=new(){AutoSize=true,MaximumSize=new Size(680,0)};
    readonly TextBox itemGradeLastResult=new(){Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Width=680,Height=170,Font=new Font("Consolas",9f),WordWrap=false};
    readonly System.Windows.Forms.Timer itemGradeHoverTimer=new(){Interval=ItemGradeHoverPollMilliseconds};
    /// <summary>Not a multiple of the 60 Hz frame (16.7 ms), so the poll cannot lock onto the same point of the game's frame every time.</summary>
    internal const int ItemGradeHoverPollMilliseconds=130;
    /// <summary>
    /// Polls during which the game's hovered-tooltip pointer no longer names the shown item before the box hides. The game clears and
    /// rewrites that pointer every frame, and other UI elements can take it for a frame, so a few stray polls must not blink the box.
    /// </summary>
    internal const int ItemGradeAwayPollsToHide=6;
    ItemGradeOverlay? itemGradeOverlay;
    bool itemGradeOptionsAdded,applyingItemGradeOptions,itemGradeHotkeyRegistered;
    Keys? itemGradeRegisteredKey;
    uint itemGradeShownHelper,itemGradeSkippedHelper;
    int itemGradeAwayPolls;

    ItemGrade? SelectedItemGradeTarget=>ItemGradeLadder.ParseTarget(itemGradeTarget.SelectedItem?.ToString());
    Keys SelectedItemGradeKey=>ItemGradeHotkeys.Parse(itemGradeHotkey.SelectedItem?.ToString());

    /// <summary>Called after OrganizeOptionsPage so the page joins the Options tabs.</summary>
    internal TabPage AddItemGradeOptions()
    {
        if(itemGradeOptionsAdded)throw new InvalidOperationException("Item grade page already exists.");
        itemGradeOptionsAdded=true;
        itemGradeFocus.Items.AddRange(ItemGradeDesk.FocusChoices);
        itemGradeTarget.Items.Add(ItemGradeLadder.AutomaticName);
        foreach(var grade in ItemGradeLadder.Targets)itemGradeTarget.Items.Add(grade.ToString());
        foreach(string choice in ItemGradeHotkeys.Choices)itemGradeHotkey.Items.Add(choice);
        try{ApplyItemGradeSettings(Options.Read());}
        catch(Exception ex){ApplyItemGradeSettings(new Options());itemGradeStatus.Text="Item grade settings could not be loaded; defaults are active. "+ex.Message;}
        var page=new TabPage("Item grades"){AutoScroll=true,Padding=new Padding(12)};
        var stack=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(5)};
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        void Add(Control control){control.Margin=new Padding(3,6,3,6);stack.Controls.Add(control,0,stack.RowCount++);}
        Add(new Label{AutoSize=true,Font=new Font("Segoe UI Semibold",12),Text="Item grades"});
        Add(new Label{AutoSize=true,MaximumSize=new Size(680,0),Text="Connect, then hover a weapon or armor until its tooltip appears. Upgrade Desk opens beside the item with stat progress, a focus-stat gem plan and upgrade estimates. Automatic mode lets mouse clicks pass through and keeps the game in focus."});
        Add(itemGradeEnabled);
        Add(itemGradeAutoShow);
        var row=new FlowLayoutPanel{AutoSize=true,WrapContents=true};
        row.Controls.Add(new Label{AutoSize=true,Text="Target grade",Margin=new Padding(0,6,6,0)});row.Controls.Add(itemGradeTarget);
        row.Controls.Add(new Label{AutoSize=true,Text="(Auto: a stat at AAA shows gems to S, every other stat gems to AAA)",Margin=new Padding(6,6,6,0)});
        row.Controls.Add(new Label{AutoSize=true,Text="Optional key to read on demand",Margin=new Padding(18,6,6,0)});row.Controls.Add(itemGradeHotkey);
        Add(row);
        var focusRow=new FlowLayoutPanel{AutoSize=true,WrapContents=true};
        focusRow.Controls.Add(new Label{AutoSize=true,Text="Focus stat",Margin=new Padding(0,6,6,0)});focusRow.Controls.Add(itemGradeFocus);
        focusRow.Controls.Add(new Label{AutoSize=true,Text="If absent on this item, Auto chooses an available stat.",Margin=new Padding(6,6,6,0)});
        Add(focusRow);
        Add(itemGradeStatus);
        Add(new Label{AutoSize=true,MaximumSize=new Size(680,0),Text="Gem and +10 projections are estimates from the imported table and upgrade assumptions. Existing sockets beyond the two mapped fields are not confirmed."});
        Add(new Label{AutoSize=true,Text="Last reading"});
        Add(itemGradeLastResult);
        page.Controls.Add(stack);
        void FitGradePage()
        {
            int width=Math.Max(250,page.ClientSize.Width-page.Padding.Horizontal-stack.Padding.Horizontal-12);
            foreach(Control control in stack.Controls)
            {
                if(control is Label)control.MaximumSize=new Size(width,0);
                if(control is CheckBox check){check.MaximumSize=new Size(width,32);check.Width=Math.Min(680,width);check.Height=32;}
                if(control is FlowLayoutPanel)control.MaximumSize=new Size(width,0);
                if(control is FlowLayoutPanel flow){flow.Width=width;foreach(var label in flow.Controls.OfType<Label>())label.MaximumSize=new Size(Math.Max(180,width-120),0);}
            }
            itemGradeLastResult.MaximumSize=new Size(width,170);itemGradeLastResult.Size=new Size(Math.Min(680,width),170);
        }
        page.SizeChanged+=(_,_)=>FitGradePage();FitGradePage();

        itemGradeEnabled.CheckedChanged+=(_,_)=>ItemGradeSettingsChanged();
        itemGradeAutoShow.CheckedChanged+=(_,_)=>ItemGradeSettingsChanged();
        itemGradeFocus.SelectedIndexChanged+=(_,_)=>ItemGradeSettingsChanged();
        itemGradeTarget.SelectedIndexChanged+=(_,_)=>ItemGradeSettingsChanged();
        itemGradeHotkey.SelectedIndexChanged+=(_,_)=>ItemGradeSettingsChanged();
        itemGradeHoverTimer.Tick+=(_,_)=>ItemGradeHoverTick();
        if(!offlinePreviewMode)itemGradeHoverTimer.Start();
        FormClosed+=(_,_)=>{itemGradeHoverTimer.Stop();itemGradeHoverTimer.Dispose();UnregisterItemGradeHotkey();itemGradeOverlay?.Dispose();itemGradeOverlay=null;};
        UpdateItemGradeStatus();
        Shown+=(_,_)=>{if(!offlinePreviewMode)RegisterItemGradeHotkey();};
        return page;
    }

    internal void ApplyItemGradeSettings(Options options)
    {
        applyingItemGradeOptions=true;
        try
        {
            itemGradeFocus.SelectedItem=ItemGradeDesk.FocusChoices.First(c=>c.Key==ItemGradeDesk.NormalizeFocus(options.ItemGradeFocusStat));
            itemGradeEnabled.Checked=options.ItemGradeOverlayEnabled;
            itemGradeAutoShow.Checked=options.ItemGradeAutoShow;
            itemGradeTarget.SelectedItem=ItemGradeLadder.TargetName(ItemGradeLadder.ParseTarget(options.ItemGradeTarget));
            itemGradeHotkey.SelectedItem=ItemGradeHotkeys.Name(ItemGradeHotkeys.Parse(options.ItemGradeHotkey));
        }
        finally{applyingItemGradeOptions=false;}
    }

    internal Options WithItemGradeSettings(Options options)
    {
        options.ItemGradeFocusStat=(itemGradeFocus.SelectedItem as ItemGradeDesk.FocusChoice)?.Key??"Auto";
        options.ItemGradeOverlayEnabled=itemGradeEnabled.Checked;
        options.ItemGradeAutoShow=itemGradeAutoShow.Checked;
        options.ItemGradeTarget=ItemGradeLadder.TargetName(SelectedItemGradeTarget);
        options.ItemGradeHotkey=ItemGradeHotkeys.Name(SelectedItemGradeKey);
        return options;
    }

    void ItemGradeSettingsChanged()
    {
        if(applyingItemGradeOptions)return;
        if(IsHandleCreated&&!offlinePreviewMode)RegisterItemGradeHotkey();
        HideItemGradeOverlay(); // Refresh the same hovered item after focus or target changes.
        try{WithItemGradeSettings(Options.Read()).Save();}catch(Exception ex){itemGradeStatus.Text="Could not save item grade settings: "+ex.Message;return;}
        UpdateItemGradeStatus();
    }

    /// <summary>Registers the optional key separately from the bot hotkeys so a taken key never blocks hunting.</summary>
    void RegisterItemGradeHotkey()
    {
        if(offlinePreviewMode)return;
        var key=SelectedItemGradeKey;
        if(itemGradeHotkeyRegistered&&itemGradeRegisteredKey==key&&itemGradeEnabled.Checked)return;
        UnregisterItemGradeHotkey();
        if(!itemGradeEnabled.Checked||key==Keys.None||ItemGradeHotkeys.Reserved(key))return;
        itemGradeHotkeyRegistered=Input.RegisterHotKey(Handle,ItemGradeHotkeys.Id,0x4000,(uint)key);
        int error=itemGradeHotkeyRegistered?0:System.Runtime.InteropServices.Marshal.GetLastWin32Error();
        itemGradeRegisteredKey=itemGradeHotkeyRegistered?key:null;
        TraceLog.Record("item grade hotkey registration",new{Key=key.ToString(),Registered=itemGradeHotkeyRegistered,WindowsError=error});
        if(!itemGradeHotkeyRegistered)itemGradeStatus.Text=$"The {ItemGradeHotkeys.Name(key)} key could not be registered (Windows error {error}); another program may be using it. Choose a different key.";
    }

    void UnregisterItemGradeHotkey()
    {
        if(!itemGradeHotkeyRegistered)return;
        if(IsHandleCreated)Input.UnregisterHotKey(Handle,ItemGradeHotkeys.Id);
        itemGradeHotkeyRegistered=false;itemGradeRegisteredKey=null;
    }

    void UpdateItemGradeStatus()
    {
        var key=SelectedItemGradeKey;
        string target=ItemGradeLadder.TargetName(SelectedItemGradeTarget);
        string keyText=key==Keys.None?"":itemGradeHotkeyRegistered?$" {ItemGradeHotkeys.Name(key)} also reads on demand.":IsHandleCreated&&!offlinePreviewMode?$" {ItemGradeHotkeys.Name(key)} is not registered yet.":$" {ItemGradeHotkeys.Name(key)} will be registered when the window opens.";
        itemGradeStatus.Text=!itemGradeEnabled.Checked?"Off."
            :itemGradeAutoShow.Checked?$"Ready. Hover an item in the game until its tooltip shows; the box opens by itself. Target {target}.{keyText}"
            :key==Keys.None?"Automatic pop-up is off and no key is chosen, so nothing will show. Turn the pop-up on or choose a key."
            :$"Manual. Hover an item and press {ItemGradeHotkeys.Name(key)}. Target {target}.{keyText}";
    }

    void HideItemGradeOverlay()
    {
        if(itemGradeOverlay is {Visible:true})itemGradeOverlay.Hide();
        itemGradeShownHelper=0;itemGradeSkippedHelper=0;itemGradeAwayPolls=0;
    }

    /// <summary>Polls the game's own hovered-tooltip pointer (one 4-byte read) and shows or hides the box as the tooltip comes and goes.</summary>
    void ItemGradeHoverTick()
    {
        if(!itemGradeEnabled.Checked||!itemGradeAutoShow.Checked||offlinePreviewMode||!connected||!world.ConnectionVerified||Input.GetForegroundWindow()!=world.Window||Input.IsIconic(world.Window))
        {if(itemGradeShownHelper!=0)HideItemGradeOverlay();return;}
        uint helper=world.HoveredTooltipHandle();
        if(helper!=0&&helper==itemGradeShownHelper){itemGradeAwayPolls=0;return;}
        void Away(){if(itemGradeShownHelper!=0&&++itemGradeAwayPolls>=ItemGradeAwayPollsToHide)HideItemGradeOverlay();}
        if(helper==0||helper==itemGradeSkippedHelper){Away();return;}
        try
        {
            var reading=world.ItemUnderCursor(Input.Cursor());
            if(!reading.Found)
            {
                if(reading.Retry)return;
                // A tooltip that is not a gradeable item (skill, non-equipment): nothing to show, no need to read it again, and the box goes away.
                itemGradeSkippedHelper=helper;
                Away();
                return;
            }
            ShowItemGradePlan(reading,auto:true);
            itemGradeShownHelper=helper;itemGradeSkippedHelper=0;itemGradeAwayPolls=0;
        }
        catch(Exception ex){HideItemGradeOverlay();TraceLog.Record("item grade hover read failed",new{Error=ex.ToString()});}
    }

    void ShowItemGradePlan(HoveredItemReading reading,bool auto)
    {
        var cursor=Input.Cursor();
        var target=SelectedItemGradeTarget;
        itemGradeOverlay??=new ItemGradeOverlay();
        itemGradeOverlay.SetClickThrough(auto);
        itemGradeOverlay.AutoHide=!auto;
        TraceLog.Record("item grade read",new{reading.Found,reading.Status,reading.Entry?.PrototypeId,reading.Name,Stats=reading.Stats,reading.UpgradeLevel,reading.Details,Target=ItemGradeLadder.TargetName(target),Automatic=auto});
        var plan=ItemGradePlanner.Plan(ItemGradeTable.Find(reading.Entry!.PrototypeId),reading.Name,reading.Entry.PrototypeId,reading.Stats,target,reading.UpgradeLevel);
        itemGradeLastResult.Text=ItemGradePlanner.Describe(plan);
        string footer="Gem / +10 estimates · sockets beyond two fields unconfirmed";
        itemGradeOverlay.ShowPlan(plan,footer,cursor,preferLeft:auto,focus:(itemGradeFocus.SelectedItem as ItemGradeDesk.FocusChoice)?.Key??"Auto");
    }

    /// <summary>Optional key handler: read the item under the cursor from client memory and show its grades now.</summary>
    internal void ShowItemGradeUnderCursor()
    {
        var cursor=Input.Cursor();
        itemGradeOverlay??=new ItemGradeOverlay();
        string key=ItemGradeHotkeys.Name(SelectedItemGradeKey);
        try
        {
            itemGradeOverlay.SetClickThrough(false);itemGradeOverlay.AutoHide=true;
            if(!connected||!world.ConnectionVerified)
            {itemGradeOverlay.ShowMessage("Connect PoteHunter to the game first.",cursor);return;}
            var reading=world.ItemUnderCursor(cursor);
            if(!reading.Found)
            {
                TraceLog.Record("item grade read",new{reading.Found,reading.Status,reading.Entry?.PrototypeId,reading.Name,Automatic=false});
                itemGradeLastResult.Text=reading.Status;
                itemGradeOverlay.ShowMessage(reading.Status,cursor);
                return;
            }
            ShowItemGradePlan(reading,auto:false);
            itemGradeShownHelper=reading.Details?.Helper??0;itemGradeSkippedHelper=0;itemGradeAwayPolls=0;
        }
        catch(Exception ex)
        {
            TraceLog.Record("item grade read failed",new{Error=ex.ToString()});
            itemGradeLastResult.Text="Read failed: "+ex.Message;
            itemGradeOverlay.ShowMessage("Couldn't read that item ("+ex.Message+"). Press "+key+" to try again.",cursor);
        }
    }
}
