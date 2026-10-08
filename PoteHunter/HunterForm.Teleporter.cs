using System.Text.Json;

namespace PoteHunter;

public sealed partial class HunterForm
{
    readonly CheckBox useTeleporterRoutes=new(){Name="useTeleporterRoutes",Text="Use teleporter routes",AutoSize=true};
    readonly Button configureTeleporter=new(){Name="configureTeleporter",Text="Configure teleporter",AutoSize=true};
    readonly Label teleporterStatus=new(){Name="teleporterStatus",AutoSize=true,MaximumSize=new(240,0),ForeColor=UiMuted};
    readonly Label teleporterHelp=new(){Name="teleporterHelp",AutoSize=true,MaximumSize=new(240,0),ForeColor=UiMuted,
        Text="Optional, same-map link for this target and selected route. The recorded path must contain departure, landing, then anchor. Capture the destination marker, Move to Location / OK, then the landing. Setup sends no game input."};
    readonly CancellationTokenSource teleporterSetupLifetime=new();
    CollapsibleSection? teleporterSection;
    bool teleporterProfileReady;
    string teleporterStatusSignature="";

    void InitializeTeleporterSettings()
    {
        configureTeleporter.Click+=async(_,_)=>await RunTeleporterSetup();
        useTeleporterRoutes.CheckedChanged+=(_,_)=>{OverlaySettingsChanged();RefreshTeleporterSettings(true);};
        priorityHint.SetToolTip(configureTeleporter,"Stop hunting at the departure point. Open CaernarvonMap manually, identify the correct blue destination marker and its Move to Location / OK dialog, then manually teleport and capture your living landing on the selected recorded route. No setup click or key is sent to the game.");
        priorityHint.SetToolTip(useTeleporterRoutes,"Off by default. Uses only a complete captured link for this character, client/window, target selection and destination route slot. Configure first; a saved spot alone is insufficient.");
        FormClosed+=(_,_)=>{teleporterSetupLifetime.Cancel();teleporterSetupLifetime.Dispose();};
        RefreshTeleporterSettings(true);
    }

    // Called by the Navigation sidebar builder after the original controls are rehomed.
    void AddTeleporterNavigationSection(FlowLayoutPanel side)
    {
        if(teleporterSection!=null)return;
        teleporterSection=new("navigationTeleporterRoute","Teleporter route","compass",false){Width=264,Summary="Optional · setup required"};
        teleporterSection.Content.Padding=new(10,6,10,10);
        foreach(var control in new Control[]{useTeleporterRoutes,configureTeleporter,teleporterStatus,teleporterHelp})
        {
            control.Margin=new(0,3,0,4);control.Dock=DockStyle.Top;control.MaximumSize=new(240,0);
            teleporterSection.Content.RowStyles.Add(new(SizeType.AutoSize));
            teleporterSection.Content.Controls.Add(control,0,teleporterSection.Content.RowCount++);
        }
        side.Controls.Add(teleporterSection);RefreshTeleporterSettings(true);
    }

    void RefreshTeleporterControls()
    {
        if(configureTeleporter.IsDisposed)return;
        bool stopped=!busy&&!working&&!navigation.Recording;
        configureTeleporter.Enabled=stopped&&connected&&world.ConnectionVerified;
        // An enabled saved choice can always be switched off while stopped,
        // even when a client/window or route change makes its profile stale.
        useTeleporterRoutes.Enabled=stopped&&(teleporterProfileReady||useTeleporterRoutes.Checked);
        if(stopped)RefreshTeleporterSettings();
    }

    void RefreshTeleporterSettings(bool force=false)
    {
        if(teleporterStatus.IsDisposed)return;
        int slot=SelectedSavedNavigationSlot();var route=navigation.GetSavedRoute(slot);
        Entity? currentCharacter=null;
        try{if(connected&&world.ConnectionVerified)currentCharacter=world.LocalPlayer();}
        catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception){ }
        string signature=$"{connected}:{world.Pid}:{world.ClientHash}:{world.Window}:{currentCharacter?.Id}:{currentCharacter?.Name}:{navigation.RouteTargetKey}:{slot}:{route?.SavedUtc.Ticks}:{route?.Points.Length}:{CurrentRouteCorridorRadius()}:{useTeleporterRoutes.Checked}";
        try{signature+=$":{File.GetLastWriteTimeUtc(TeleporterProfile.PathName).Ticks}";}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){ }
        if(connected&&NavigationOverlay.TryGetClientScreenBounds(world.Window,out var bounds))signature+=$":{bounds.Size}";
        if(!force&&signature==teleporterStatusSignature)return;
        teleporterStatusSignature=signature;teleporterProfileReady=false;
        try
        {
            if(!connected||!world.ConnectionVerified)teleporterStatus.Text="Connect to verify the captured link for "+navigation.RouteTargetLabel+" · "+SavedNavigationSlotName(slot)+".";
            else
            {
                var size=RepairScreen.Bounds(world).Size;
                var profile=TeleporterProfile.Load(world.ClientHash,size,navigation.RouteTargetKey,slot);
                if(profile==null)teleporterStatus.Text="No captured link for "+navigation.RouteTargetLabel+" · "+SavedNavigationSlotName(slot)+".";
                else
                {
                    var self=currentCharacter??throw new InvalidOperationException("Character reading is unavailable. Refresh the connection.");
                    if(profile.CharacterId!=self.Id||profile.Character!=self.Name)
                        throw new InvalidOperationException("Captured link belongs to another character. Configure this character's link.");
                    RequireTeleporterHandoff(route,profile.Landing,profile.Character,slot,CurrentRouteCorridorRadius());
                    _=TeleporterJourney.Create(route!,profile,CurrentRouteCorridorRadius());
                    teleporterProfileReady=true;
                    teleporterStatus.Text=$"Captured: {profile.Destination}\n{navigation.RouteTargetLabel} · {SavedNavigationSlotName(slot)}\nDeparture {profile.Departure.Position.X:0.#}, {profile.Departure.Position.Y:0.#} → landing {profile.Landing.Position.X:0.#}, {profile.Landing.Position.Y:0.#}.";
                    teleporterStatus.Text+=useTeleporterRoutes.Checked?"\nEnabled for compatible saved routes.":"\nReady; enable Use teleporter routes to use it.";
                }
            }
        }
        catch(Exception ex) when(ex is InvalidOperationException or RouteUnavailableException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {teleporterStatus.Text="Captured link unavailable: "+ex.Message;}
        if(teleporterSection!=null)teleporterSection.Summary=teleporterProfileReady?(useTeleporterRoutes.Checked?"Captured · enabled":"Captured · off"):"Optional · setup required";
        bool stopped=!busy&&!working&&!navigation.Recording;
        configureTeleporter.Enabled=stopped&&connected&&world.ConnectionVerified;
        useTeleporterRoutes.Enabled=stopped&&(teleporterProfileReady||useTeleporterRoutes.Checked);
    }

    static void RequireTeleporterLivingState(Entity body,Health health)
    {
        if(body.Id==0||string.IsNullOrWhiteSpace(body.Name)||!body.Position.Finite||!double.IsFinite(body.Height)||!health.Known||health.Dead)
            throw new InvalidOperationException("Teleporter setup requires the same living character with readable HP and a finite live position.");
    }

    static bool TeleporterSetupCharacterMatches(Entity original,Entity current,bool landing)=>
        original.Id==current.Id&&original.Name==current.Name&&original.Model==current.Model&&
        (landing||original.Address==current.Address&&original.Generation==current.Generation);

    static void RequireTeleporterHandoff(SavedNavigationRoute? route,TeleporterPosition landing,string character,int slot,double corridor)
    {
        var candidates=new SavedNavigationRoute?[Navigation.SavedRouteSlotCount];
        if(slot<0||slot>=candidates.Length)throw new InvalidOperationException("Select a valid destination route slot.");
        candidates[slot]=route;
        if(route==null||!RecoveryTravel.Recorded(route)||
            RecoveryTravel.StartupSlot(candidates,landing.Position,landing.Zone,character,landing.Height,slot,corridor)!=slot)
            throw new InvalidOperationException("Record a compatible onward route in the selected slot from this landing to the farming anchor with Home / End. The landing must be inside its configured route corridor. A saved spot alone cannot be used.");
    }

    async Task RunTeleporterSetup()
    {
        if(busy||working||navigation.Recording){message="Stop hunting and finish route recording before configuring a teleporter.";return;}
        if(!connected||!world.ConnectionVerified){message="Connect to the verified game before configuring a teleporter.";return;}
        SyncNavigationTargetSelection();
        var originalFilterEnabled=filter.Enabled;
        busy=true;settings.Enabled=false;start.Enabled=false;connect.Enabled=false;filter.Enabled=false;
        cancel=CancellationTokenSource.CreateLinkedTokenSource(teleporterSetupLifetime.Token);var token=cancel.Token;
        RefreshNavigationRecordingControls();
        try{await ConfigureTeleporterAsync(token);}
        catch(OperationCanceledException ex)
        {
            message="Teleporter setup cancelled; previous captured links are preserved. "+ex.Message;
            TraceLog.Record("teleporter setup cancelled",new{Reason=ex.Message,HardwareInputEmitted=false});
        }
        catch(Exception ex)
        {
            message="Teleporter setup stopped; previous captured links are preserved. "+ex.Message;
            TraceLog.Record("teleporter setup stopped",new{Error=ex.Message,HardwareInputEmitted=false});
        }
        finally
        {
            busy=false;settings.Enabled=true;start.Enabled=true;connect.Enabled=true;filter.Enabled=originalFilterEnabled;
            cancel?.Dispose();cancel=null;RefreshNavigationRecordingControls();RefreshTeleporterSettings(true);
        }
    }

    async Task ConfigureTeleporterAsync(CancellationToken token)
    {
        var original=world.LocalPlayer();RequireTeleporterLivingState(original,world.TargetHealth(original.Id));
        int processId=world.Pid,zone=world.ActiveZone(),slot=SelectedSavedNavigationSlot();
        IntPtr window=world.Window;string client=world.ClientHash,target=navigation.RouteTargetLabel,targetKey=navigation.RouteTargetKey;
        string targetSelection=filter.Text.Trim();
        var size=RepairScreen.Bounds(world).Size;
        var departure=new TeleporterPosition(zone,original.Position,original.Height);
        Entity Validate(bool landing=false,bool foreground=false)
        {
            token.ThrowIfCancellationRequested();
            if(IsDisposed||!connected||working||!world.ConnectionVerified||world.Pid!=processId||world.Window!=window||world.ClientHash!=client||
                Navigation.TargetSelectionKey(filter.Text)!=targetKey||navigation.RouteTargetKey!=targetKey||SelectedSavedNavigationSlot()!=slot)
                throw new OperationCanceledException("Client, window, target selection, route slot or hunting state changed.");
            var windowState=world.CheckInputWindow();
            if(!windowState.OwnershipVerified||!windowState.ProcessAlive||windowState.Minimized||foreground&&!windowState.Allowed||RepairScreen.Bounds(world).Size!=size)
                throw new OperationCanceledException("Keep the same complete game window visible at the captured size; switch to the game for each capture.");
            var current=world.LocalPlayer();RequireTeleporterLivingState(current,world.TargetHealth(current.Id));
            if(!TeleporterSetupCharacterMatches(original,current,landing)||world.ActiveZone()!=zone)
                throw new OperationCanceledException(landing?"The landing must belong to the same living character and map.":"Character body or map changed before the confirmation was captured.");
            if(!landing&&((current.Position-original.Position).Length>3||Math.Abs(current.Height-original.Height)>3))
                throw new OperationCanceledException("Remain at the departure point until the destination and confirmation are captured.");
            return current;
        }
        Validate();
        using var nameDialog=TeleporterStageDialog.Destination(target,SavedNavigationSlotName(slot),UiWindow,UiText);
        ShowTeleporterStage(nameDialog,token);
        string destination=nameDialog.DestinationName;

        using var departureDialog=TeleporterStageDialog.Instruction("Teleporter setup · 1 of 3",
            $"At the departure point, manually open CaernarvonMap. Leave the three blue destination markers visible; do not select one yet.\n\nYou will identify the marker for {destination} in a captured image. After Capture in 5s, switch to the game and move the pointer away from the map.\n\nSaved scope: {target} · {SavedNavigationSlotName(slot)}. Setup sends no game input.","Capture destination · 5s",UiWindow,UiText);
        ShowTeleporterStage(departureDialog,token);
        await RepairCountdown("Teleporter 1/3: switch to the game with the destination map open",token);Validate(foreground:true);
        using var selectionImage=RepairScreen.Capture(world);Validate(foreground:true);Activate();
        using var selectionEditor=RepairSetupForm.ForTeleporter(selectionImage,false,destination,UiWindow,UiText);
        var select=SelectTeleporterStep(selectionEditor,selectionImage.Size,token);Validate();

        using var confirmationDialog=TeleporterStageDialog.Instruction("Teleporter setup · 2 of 3",
            $"Manually select the blue marker for {destination} in the game. Leave its 'Move to Location' confirmation open. Do not press OK yet and do not select StartLoc. Keep the map in the same position.\n\nAfter Capture in 5s, switch to the game and move the pointer away from the dialog. You will identify its static text and OK button in the captured image.","Capture confirmation · 5s",UiWindow,UiText);
        ShowTeleporterStage(confirmationDialog,token);
        await RepairCountdown("Teleporter 2/3: switch to the game with Move to Location open",token);Validate(foreground:true);
        using var confirmationImage=RepairScreen.Capture(world);Validate(foreground:true);Activate();
        if(selectionImage.Size!=confirmationImage.Size||!select.Marker.Matches(confirmationImage))
            throw new InvalidOperationException("The selected map recognition area must remain visible in the same location behind Move to Location. Capture again with static map text/artwork outside the dialog.");
        using var confirmationEditor=RepairSetupForm.ForTeleporter(confirmationImage,true,destination,UiWindow,UiText);
        var confirm=SelectTeleporterStep(confirmationEditor,confirmationImage.Size,token);Validate();
        if(confirm.Marker.Matches(selectionImage))
            throw new InvalidOperationException("The confirmation marker also appears before Move to Location opens. Choose distinctive dialog text.");

        using var landingDialog=TeleporterStageDialog.Instruction("Teleporter setup · 3 of 3",
            $"Now manually press OK in the captured Move to Location dialog and wait until the character has landed at {destination} with living HP. Close the map, remain still at the landing and use the same game window.\n\nThe selected {SavedNavigationSlotName(slot)} for {target} must already contain a recorded path through the departure, then this landing, then the farming anchor. Both ends must be close to the path. An unrecorded handoff stops setup.\n\nAfter Capture in 5s, switch to the game. Setup reads the live landing; it sends no game input. The complete link is saved only after this final check.","Capture landing · 5s",UiWindow,UiText);
        ShowTeleporterStage(landingDialog,token);
        await RepairCountdown("Teleporter 3/3: switch to the game and remain at the landing",token);
        var landed=Validate(landing:true,foreground:true);
        var landing=new TeleporterPosition(world.ActiveZone(),landed.Position,landed.Height);
        if((landing.Position-departure.Position).Length<=TeleporterProfile.SourceRadius+TeleporterProfile.LandingRadius+2)
            throw new InvalidOperationException("The live landing is still near the departure point. Complete the manual teleport before capturing the landing.");
        RequireTeleporterHandoff(navigation.GetSavedRoute(slot),landing,landed.Name,slot,CurrentRouteCorridorRadius());
        var stable=Validate(landing:true,foreground:true);
        if(stable.Address!=landed.Address||stable.Generation!=landed.Generation||(stable.Position-landed.Position).Length>.5||Math.Abs(stable.Height-landed.Height)>.5)
            throw new InvalidOperationException("The landing is still changing. Wait for the character to settle, then configure again.");
        var profile=new TeleporterProfile(1,client,size.Width,size.Height,destination,original.Name,original.Id,targetSelection,slot,departure,landing,select,confirm);
        profile.Validate(client,size);_=TeleporterJourney.Create(navigation.GetSavedRoute(slot)!,profile,CurrentRouteCorridorRadius());
        token.ThrowIfCancellationRequested();profile.Save();
        message=$"Captured teleporter to {destination} for {target} · {SavedNavigationSlotName(slot)}. All three stages are saved; hunting remains stopped.";
        TraceLog.Record("teleporter setup saved",new{Destination=destination,TargetSelection=targetSelection,TargetLabel=target,DestinationSlot=slot,Departure=departure,Landing=landing,
            Width=size.Width,Height=size.Height,HardwareInputEmitted=false,ManualGenerationChanged=original.Generation!=landed.Generation});
        Activate();
    }

    void ShowTeleporterStage(Form dialog,CancellationToken token)
    {
        using var cancelled=CloseTeleporterDialogOnCancellation(dialog,token);
        token.ThrowIfCancellationRequested();
        if(dialog.ShowDialog(this)!=DialogResult.OK)throw new OperationCanceledException("Cancelled by user.");
        token.ThrowIfCancellationRequested();
    }

    RevivalStep SelectTeleporterStep(RepairSetupForm editor,Size imageSize,CancellationToken token)
    {
        ShowTeleporterStage(editor,token);
        if(editor.Selection is not {} selection)throw new OperationCanceledException("No recognition selection was saved.");
        var step=RevivalStep.From(selection);
        if(!step.Valid(imageSize,true))throw new InvalidOperationException("A separate valid static marker and destination/OK control are required inside the game image.");
        return step;
    }

    static CancellationTokenRegistration CloseTeleporterDialogOnCancellation(Form dialog,CancellationToken token)
    {
        void Close()
        {
            if(dialog.IsDisposed||!dialog.IsHandleCreated)return;
            try{dialog.BeginInvoke((Action)(()=>{if(!dialog.IsDisposed)dialog.DialogResult=DialogResult.Cancel;}));}
            catch(InvalidOperationException){ }
        }
        dialog.Shown+=(_,_)=>{if(token.IsCancellationRequested)Close();};
        return token.Register(Close);
    }

    sealed class TeleporterStageDialog : Form
    {
        readonly TextBox? destination;
        internal string DestinationName=>destination?.Text.Trim()??"";
        internal static TeleporterStageDialog Destination(string target,string slot,Color background,Color foreground)=>
            new("Configure teleporter",$"Enter the destination name shown by the intended blue marker, for example Clauzhuz.\n\nThis link belongs to {target} · {slot}. Before setup, record a continuous walking path through the departure, destination landing and farming anchor in that order. The captured link replaces the middle portion; a teleport jump cannot be recorded as a walking segment.\n\nThree capture stages follow. You open the map, select the marker and press its Move to Location / OK manually. Setup sends no game input.","Begin captures",background,foreground,true);
        internal static TeleporterStageDialog Instruction(string title,string text,string action,Color background,Color foreground)=>
            new(title,text,action,background,foreground,false);
        internal bool PreviewOnly;
        protected override bool ShowWithoutActivation=>PreviewOnly;
        TeleporterStageDialog(string title,string text,string action,Color background,Color foreground,bool askDestination)
        {
            Text=title;BackColor=background;ForeColor=foreground;Font=new("Segoe UI",10);
            Size=new(680,askDestination?395:420);MinimumSize=new(600,350);StartPosition=FormStartPosition.CenterParent;
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new(16)};
            layout.ColumnStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.AutoSize));Controls.Add(layout);
            var instructions=new Label{Text=text,AutoSize=true,MaximumSize=new(620,0),Dock=DockStyle.Top};
            var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true};scroll.Controls.Add(instructions);layout.Controls.Add(scroll,0,0);
            if(askDestination)
            {
                destination=new(){Name="teleporterDestinationName",MaxLength=80,Dock=DockStyle.Top};
                var row=new TableLayoutPanel{ColumnCount=1,RowCount=2,AutoSize=true,Dock=DockStyle.Top,Padding=new(0,8,0,8)};
                row.Controls.Add(new Label{Text="Destination name",AutoSize=true},0,0);row.Controls.Add(destination,0,1);layout.Controls.Add(row,0,1);
            }
            var next=new Button{Text=action,AutoSize=true,DialogResult=DialogResult.OK,Enabled=!askDestination};
            var cancelButton=new Button{Text="Cancel",AutoSize=true,DialogResult=DialogResult.Cancel};
            var actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new(0,10,0,0)};
            actions.Controls.AddRange([cancelButton,next]);layout.Controls.Add(actions,0,2);AcceptButton=next;CancelButton=cancelButton;
            if(destination!=null)destination.TextChanged+=(_,_)=>next.Enabled=!string.IsNullOrWhiteSpace(destination.Text)&&!destination.Text.Any(char.IsControl);
        }
    }

    internal void CheckTeleporterSetupUi()
    {
        if(!offlinePreviewMode||connected||working)throw new InvalidOperationException("Teleporter UI checks require a disconnected offline fixture.");
        if(teleporterSection==null||!navigationPage.Contains(configureTeleporter)||!navigationPage.Contains(useTeleporterRoutes))
            throw new InvalidOperationException("Dedicated Navigation > Teleporter route controls are missing.");
        RefreshTeleporterSettings(true);
        if(configureTeleporter.Enabled||useTeleporterRoutes.Enabled||teleporterProfileReady)
            throw new InvalidOperationException("Disconnected teleporter setup was falsely enabled.");
        try
        {
            teleporterProfileReady=true;busy=true;RefreshTeleporterControls();
            if(configureTeleporter.Enabled||useTeleporterRoutes.Enabled)throw new InvalidOperationException("Teleporter controls bypassed the running/setup lock.");
        }
        finally{busy=false;RefreshTeleporterSettings(true);}
        var original=new Entity(100,7,"Fixture",new(10,20),4,Generation:5,Model:"Player");
        RequireTeleporterLivingState(original,new(100,100));
        foreach(var hp in new Health[]{default,new(0,100)})
        {
            bool rejected=false;try{RequireTeleporterLivingState(original,hp);}catch(InvalidOperationException){rejected=true;}
            if(!rejected)throw new InvalidOperationException("Teleporter setup accepted unknown/dead HP.");
        }
        var recreated=original with{Address=300,Generation=6,Position=new(60,100)};
        if(TeleporterSetupCharacterMatches(original,recreated,false)||!TeleporterSetupCharacterMatches(original,recreated,true)||
            TeleporterSetupCharacterMatches(original,recreated with{Name="Other"},true)||TeleporterSetupCharacterMatches(original,recreated with{Id=8},true))
            throw new InvalidOperationException("Teleporter setup generation/character guards failed.");
        var path=new SavedNavigationRoute(8,new(80,20),0,Enumerable.Range(0,6).Select(i=>new Vec(80-i*4,20)).ToArray(),DateTime.UnixEpoch){Character="Fixture",Height=4};
        var landing=new TeleporterPosition(8,new(60,20),4);
        RequireTeleporterHandoff(path,landing,"Fixture",0,10);
        foreach(var invalid in new SavedNavigationRoute?[]{null,path with{Points=[path.Anchor]},path with{Character="Other"},path with{Zone=9}})
        {
            bool rejected=false;try{RequireTeleporterHandoff(invalid,landing,"Fixture",0,10);}catch(InvalidOperationException){rejected=true;}
            if(!rejected)throw new InvalidOperationException("Unrecorded/incompatible teleporter handoff was accepted.");
        }
        bool outsideRejected=false;
        try{RequireTeleporterHandoff(path,landing with{Position=new(60,31)},"Fixture",0,10);}catch(InvalidOperationException){outsideRejected=true;}
        if(!outsideRejected)throw new InvalidOperationException("Teleporter handoff accepted a landing beyond the selected route corridor.");
        using(var dialog=TeleporterStageDialog.Destination("Fixture target","Primary hunt route",UiWindow,UiText))
        {
            dialog.PreviewOnly=true;dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new(-32000,-32000);dialog.ShowInTaskbar=false;
            dialog.Show();dialog.PerformLayout();Application.DoEvents();
            if(dialog.DestinationName.Length!=0||dialog.AcceptButton is not Button {Enabled:false})throw new InvalidOperationException("Destination name was assumed without user selection.");
            using var preview=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(preview,new(Point.Empty,dialog.Size));preview.Save(Path.Combine(AppContext.BaseDirectory,"ui-teleporter-destination.png"));
        }
        foreach(bool confirmation in new[]{false,true})
        {
            using var image=TeleporterSetupFixture(confirmation);
            using var editor=RepairSetupForm.ForTeleporter(image,confirmation,"Clauzhuz",UiWindow,UiText);
            editor.PreviewOnly=true;editor.StartPosition=FormStartPosition.Manual;editor.Location=new(-32000,-32000);editor.ShowInTaskbar=false;
            editor.Show();editor.PerformLayout();Application.DoEvents();
            if(editor.Selection!=null||!editor.Text.StartsWith("Teleporter setup"))throw new InvalidOperationException("Teleporter editor invented a selection.");
            using var preview=new Bitmap(editor.Width,editor.Height);editor.DrawToBitmap(preview,new(Point.Empty,editor.Size));preview.Save(Path.Combine(AppContext.BaseDirectory,confirmation?"ui-teleporter-confirmation.png":"ui-teleporter-marker.png"));
        }
        var tabs=(TabControl)navigationPage.Parent!;var selected=tabs.SelectedTab;tabs.SelectedTab=navigationPage;
        teleporterSection.Expanded=true;navigationPage.ScrollControlIntoView(teleporterSection);PerformLayout();Application.DoEvents();
        if(!teleporterSection.Content.Visible||!teleporterStatus.Visible)throw new InvalidOperationException("Expanded teleporter controls cannot be seen.");
        foreach(Control control in teleporterSection.Content.Controls)
            if(control.Bounds.Right>teleporterSection.Content.ClientSize.Width+1)throw new InvalidOperationException("Teleporter control overflows its Navigation section.");
        using(var preview=new Bitmap(teleporterSection.Width,teleporterSection.Height))
        {teleporterSection.DrawToBitmap(preview,new(Point.Empty,teleporterSection.Size));preview.Save(Path.Combine(AppContext.BaseDirectory,"ui-teleporter-section.png"));}
        using(var preview=new Bitmap(Width,Height))
        {DrawToBitmap(preview,new(Point.Empty,Size));preview.Save(Path.Combine(AppContext.BaseDirectory,"ui-teleporter-navigation.png"));}
        teleporterSection.Expanded=false;tabs.SelectedTab=selected;
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"teleporter-setup-ui-checks.json"),JsonSerializer.Serialize(new
        {Passed=true,HardwareInputEmitted=false,Checks=new[]{"dedicated scoped Navigation section","disconnected setup/toggle disabled; running/setup lock","known living state required","source generation strict; manual landing generation allowed only for same character","recorded compatible selected-slot landing corridor required","destination name not assumed","paired no-input editors rendered","sidebar fit"}}));
    }

    static Bitmap TeleporterSetupFixture(bool confirmation)
    {
        var image=new Bitmap(960,640);using var graphics=Graphics.FromImage(image);
        graphics.Clear(Color.FromArgb(35,40,45));using var font=new Font("Segoe UI",16,FontStyle.Bold);
        graphics.DrawString("SYNTHETIC CAERNARVON MAP",font,Brushes.White,35,35);
        string[] names=["RocheCamp","Clauzhuz","Other location"];
        for(int i=0;i<names.Length;i++)
        {
            int x=120+i*240;graphics.FillEllipse(Brushes.RoyalBlue,x,110,36,36);
            graphics.DrawLine(Pens.White,x+8,128,x+28,128);graphics.DrawLine(Pens.White,x+18,118,x+18,138);
            graphics.DrawString(names[i],font,Brushes.White,x-35,155);
        }
        if(confirmation)
        {
            graphics.FillRectangle(Brushes.DarkSlateGray,320,245,350,170);
            graphics.DrawString("Move to Location",font,Brushes.White,350,265);
            graphics.FillRectangle(Brushes.DimGray,440,345,90,40);graphics.DrawString("OK",font,Brushes.White,465,349);
        }
        return image;
    }
}
