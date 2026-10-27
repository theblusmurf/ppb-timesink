using System.Text.Json;

namespace PoteHunter;

public sealed partial class HunterForm
{
    readonly CheckBox useTeleporterRoutes=new(){Name="useTeleporterRoutes",Text="Use Teleport Scroll routes",AutoSize=true};
    readonly Button configureTeleporter=new(){Name="configureTeleporter",Text="New scroll profile",AutoSize=true};
    readonly ComboBox scrollRouteProfile=new(){Name="scrollRouteProfile",DropDownStyle=ComboBoxStyle.DropDownList,Width=240,DropDownWidth=360};
    readonly Button assignScrollRouteProfile=new(){Name="assignScrollRouteProfile",Text="Use for this route",AutoSize=true};
    readonly Button renameScrollRouteProfile=new(){Name="renameScrollRouteProfile",Text="Rename profile",AutoSize=true};
    readonly Label teleporterStatus=new(){Name="teleporterStatus",AutoSize=true,MaximumSize=new(240,0),ForeColor=UiMuted};
    readonly Label teleporterHelp=new(){Name="teleporterHelp",AutoSize=true,MaximumSize=new(240,0),ForeColor=UiMuted,
        Text="Optional same-map scroll route. Put one Teleport Scroll on the active hotbar page. Calibrate a destination once, then select its saved profile for each compatible farm route. Runtime waits 16 seconds without moving; setup sends no input."};
    readonly CancellationTokenSource teleporterSetupLifetime=new();
    CollapsibleSection? teleporterSection;
    bool teleporterProfileReady;
    string teleporterStatusSignature="";
    string teleporterSetupStage="";

    void InitializeTeleporterSettings()
    {
        scrollRouteProfile.SelectionChangeCommitted+=(_,_)=>RefreshScrollProfileControls();
        assignScrollRouteProfile.Click+=(_,_)=>AssignSelectedScrollProfile();
        renameScrollRouteProfile.Click+=(_,_)=>RenameSelectedScrollProfile();
        priorityHint.SetToolTip(scrollRouteProfile,"Choose a reusable destination calibration, then Use for this route. Each target and route slot remembers its own assignment.");
        configureTeleporter.Click+=async(_,_)=>await RunTeleporterSetup();
        useTeleporterRoutes.CheckedChanged+=(_,_)=>{OverlaySettingsChanged();RefreshTeleporterSettings(true);};
        priorityHint.SetToolTip(configureTeleporter,"Create a reusable destination calibration once. Put one Teleport Scroll on the active hotbar page, then manually capture its map, confirmation and landing. Existing destinations can be selected above without recalibrating or using a scroll.");
        priorityHint.SetToolTip(useTeleporterRoutes,"Off: normal walking routes. On: use the captured Teleport Scroll as the first step, verify its landing, then walk the recorded farm route. Already on that landing path skips the scroll. Configure the selected target/route first; change this toggle while stopped.");
        FormClosed+=(_,_)=>{teleporterSetupLifetime.Cancel();teleporterSetupLifetime.Dispose();};
        RefreshTeleporterSettings(true);
    }

    // Called by the Navigation sidebar builder after the original controls are rehomed.
    void AddTeleporterNavigationSection(FlowLayoutPanel side)
    {
        if(teleporterSection!=null)return;
        teleporterSection=new("navigationTeleporterRoute","Teleport Scroll route","compass",false){Width=264,Summary="Optional · setup required"};
        teleporterSection.Content.Padding=new(10,6,10,10);
        foreach(var control in new Control[]{useTeleporterRoutes,new Label{Text="Saved scroll profile",AutoSize=true},scrollRouteProfile,assignScrollRouteProfile,renameScrollRouteProfile,configureTeleporter,teleporterStatus,teleporterHelp})
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
        RefreshScrollProfileControls();
        if(stopped)RefreshTeleporterSettings();
    }

    void RefreshScrollProfileControls()
    {
        bool ready=!busy&&!working&&!navigation.Recording&&connected&&world.ConnectionVerified;
        scrollRouteProfile.Enabled=ready&&scrollRouteProfile.Items.Count>1;
        assignScrollRouteProfile.Enabled=ready&&scrollRouteProfile.SelectedItem is NamedScrollRoute;
        renameScrollRouteProfile.Enabled=assignScrollRouteProfile.Enabled;
    }

    void RequireScrollProfileContext(NamedScrollRoute entry)
    {
        if(busy||working||navigation.Recording||!connected||!world.ConnectionVerified)
            throw new InvalidOperationException("Stop hunting and connect before changing scroll profiles.");
        var c=entry.Calibration;c.ValidateForUse(world.ClientHash,RepairScreen.Bounds(world).Size);
        var self=world.LocalPlayer();
        if(c.Scroll==null||c.CharacterId!=self.Id||c.Character!=self.Name||c.Landing.Zone!=world.ActiveZone())
            throw new InvalidOperationException("Choose a scroll profile for this character and map.");
    }
    void RequireScrollProfileRoute(NamedScrollRoute entry,int slot)
    {
        RequireScrollProfileContext(entry);
        var route=navigation.GetSavedRoute(slot);var c=entry.Calibration;
        RequireTeleporterHandoff(route,c.Landing,c.Character,slot,CurrentRouteCorridorRadius());
        _=TeleporterJourney.Create(route!,c,CurrentRouteCorridorRadius());
    }
    void AssignSelectedScrollProfile()
    {
        try
        {
            if(scrollRouteProfile.SelectedItem is not NamedScrollRoute selected)throw new InvalidOperationException("Choose a saved scroll profile first.");
            var entry=ScrollRouteProfiles.Available().Single(p=>p.Id==selected.Id);
            int slot=SelectedSavedNavigationSlot();RequireScrollProfileRoute(entry,slot);
            ScrollRouteProfiles.Assign(entry.Id,navigation.RouteTargetKey,slot);
            message=$"Scroll profile '{entry.Name}' assigned to {navigation.RouteTargetLabel} · {SavedNavigationSlotName(slot)}. No calibration or scroll use was needed.";
            RefreshTeleporterSettings(true);
        }
        catch(Exception ex) when(ex is InvalidOperationException or RouteUnavailableException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {message="Scroll profile not assigned: "+ex.Message;}
    }
    void RenameSelectedScrollProfile()
    {
        try
        {
            if(scrollRouteProfile.SelectedItem is not NamedScrollRoute selected)return;
            RequireScrollProfileContext(selected);
            using var dialog=TeleporterStageDialog.RenameProfile(selected.Name,UiWindow,UiText);
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            RequireScrollProfileContext(selected);
            ScrollRouteProfiles.Rename(selected.Id,dialog.DestinationName);
            message="Scroll profile renamed; its calibration and route assignments are unchanged.";
            RefreshTeleporterSettings(true,selected.Id);
        }
        catch(Exception ex) when(ex is InvalidOperationException or RouteUnavailableException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {message="Scroll profile not renamed: "+ex.Message;}
    }
    void RefreshTeleporterSettings(bool force=false,string? selectedProfileId=null)
    {
        if(teleporterStatus.IsDisposed)return;
        int slot=SelectedSavedNavigationSlot();var route=navigation.GetSavedRoute(slot);
        Entity? currentCharacter=null;
        try{if(connected&&world.ConnectionVerified)currentCharacter=world.LocalPlayer();}
        catch(Exception ex) when(ex is InvalidOperationException or System.ComponentModel.Win32Exception){ }
        string signature=$"{connected}:{world.Pid}:{world.ClientHash}:{world.Window}:{currentCharacter?.Id}:{currentCharacter?.Name}:{navigation.RouteTargetKey}:{slot}:{route?.SavedUtc.Ticks}:{route?.Points.Length}:{CurrentRouteCorridorRadius()}:{useTeleporterRoutes.Checked}";
        foreach(var file in new[]{TeleporterProfile.PathName,ScrollRouteProfiles.PathName})
            try{signature+=":"+File.GetLastWriteTimeUtc(file).Ticks;}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){ }
        if(connected&&NavigationOverlay.TryGetClientScreenBounds(world.Window,out var bounds))signature+=":"+bounds.Size;
        if(!force&&signature==teleporterStatusSignature)return;
        teleporterStatusSignature=signature;teleporterProfileReady=false;
        scrollRouteProfile.Items.Clear();scrollRouteProfile.Items.Add("Choose a saved profile");scrollRouteProfile.SelectedIndex=0;
        try
        {
            if(!connected||!world.ConnectionVerified)teleporterStatus.Text="Connect to select a saved scroll profile for this route.";
            else
            {
                var size=RepairScreen.Bounds(world).Size;
                var self=currentCharacter??throw new InvalidOperationException("Character reading is unavailable. Refresh the connection.");
                int zone=world.ActiveZone();
                var available=ScrollRouteProfiles.Available().Where(p=>ScrollRouteProfiles.SelectableFor(p,self.Id,self.Name,size,zone)).ToArray();
                foreach(var entry in available)scrollRouteProfile.Items.Add(entry);
                string? assigned=ScrollRouteProfiles.AssignedId(navigation.RouteTargetKey,slot);
                string? choice=selectedProfileId??assigned;
                if(available.FirstOrDefault(p=>p.Id==choice) is {} selected)scrollRouteProfile.SelectedItem=selected;
                var profile=TeleporterProfile.Load(world.ClientHash,size,navigation.RouteTargetKey,slot);
                if(profile==null)teleporterStatus.Text="Select a saved profile and Use for this route, or create a new scroll profile.";
                else
                {
                    if(profile.Scroll==null)throw new InvalidOperationException("Old portal capture retained. Create a Teleport Scroll profile.");
                    if(profile.CharacterId!=self.Id||profile.Character!=self.Name)
                        throw new InvalidOperationException("Assigned profile belongs to another character. Choose this character's profile.");
                    RequireTeleporterHandoff(route,profile.Landing,profile.Character,slot,CurrentRouteCorridorRadius());
                    _=TeleporterJourney.Create(route!,profile,CurrentRouteCorridorRadius());
                    teleporterProfileReady=true;
                    string name=available.FirstOrDefault(p=>p.Id==assigned)?.Name??profile.Destination;
                    teleporterStatus.Text=$"Profile: {name}\nDestination: {profile.Destination}\nScroll key {profile.Scroll.Key} ({(profile.Scroll.PageBase==0?"Z":"X")} page)\n{navigation.RouteTargetLabel} · {SavedNavigationSlotName(slot)}";
                    if(!profile.ClientHash.Equals(world.ClientHash,StringComparison.OrdinalIgnoreCase))
                        teleporterStatus.Text+="\nSaved on an earlier client; scroll and dialog controls will be checked before use.";
                    teleporterStatus.Text+=useTeleporterRoutes.Checked?"\nEnabled for this route.":"\nReady; enable Use Teleport Scroll routes.";
                }
            }
        }
        catch(Exception ex) when(ex is InvalidOperationException or RouteUnavailableException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {teleporterStatus.Text="Scroll profile unavailable: "+ex.Message;}
        if(teleporterSection!=null)teleporterSection.Summary=teleporterProfileReady?(useTeleporterRoutes.Checked?"Profile assigned · enabled":"Profile assigned · off"):"Select a profile";
        configureTeleporter.Enabled=!busy&&!working&&!navigation.Recording&&connected&&world.ConnectionVerified;
        useTeleporterRoutes.Enabled=!busy&&!working&&!navigation.Recording&&(teleporterProfileReady||useTeleporterRoutes.Checked);
        RefreshScrollProfileControls();
    }

    static void RequireTeleporterLivingState(Entity body,Health health)
    {
        TeleporterSetupPolicy.RequireLiving(body,health);
    }

    int ScrollStartupSlot(Options options,Entity self,int zone)
    {
        if(!options.UseTeleporterRoutes || options.GroupMode || options.HealerMode)return -1;
        int slot=SelectedSavedNavigationSlot();
        var link=TeleporterProfile.Load(world.ClientHash,RepairScreen.Bounds(world).Size,options.Target,slot);
        if(link?.Scroll==null)throw new InvalidOperationException("Configure a Teleport Scroll route for the selected target and route slot, or turn scroll routing off.");
        if(link.CharacterId!=self.Id || link.Character!=self.Name || link.Landing.Zone!=zone)
            throw new InvalidOperationException("The scroll route belongs to another character or map. Configure this route again.");
        var route=navigation.GetSavedRoute(slot);
        RequireTeleporterHandoff(route,link.Landing,self.Name,slot,(double)options.RouteCorridorRadius);
        var journey=TeleporterJourney.Create(route!,link,(double)options.RouteCorridorRadius);
        if(!journey.NeedsTeleport(self.Position,(double)options.RouteCorridorRadius))return -1;
        RequireTeleporterLivingState(self,world.TargetHealth(self.Id));link.Scroll.RequireReady(world.Hotbar());
        return slot;
    }

    static bool TeleporterSetupCharacterMatches(Entity original,Entity current,bool landing)=>
        TeleporterSetupPolicy.SameIdentity(original,current)&&
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
        teleporterSetupStage="starting";
        RefreshNavigationRecordingControls();
        try{await ConfigureTeleporterAsync(token);}
        catch(OperationCanceledException ex)
        {
            message="Teleporter setup cancelled; previous captured links are preserved. "+ex.Message;
            TraceLog.Record("teleporter setup cancelled",new{Stage=teleporterSetupStage,Reason=ex.Message,HardwareInputEmitted=false});
        }
        catch(Exception ex)
        {
            message="Teleporter setup stopped; previous captured links are preserved. "+ex.Message;
            TraceLog.Record("teleporter setup stopped",new{Stage=teleporterSetupStage,Error=ex.Message,HardwareInputEmitted=false});
        }
        finally
        {
            busy=false;settings.Enabled=true;start.Enabled=true;connect.Enabled=true;filter.Enabled=originalFilterEnabled;
            cancel?.Dispose();cancel=null;teleporterSetupStage="";RefreshNavigationRecordingControls();RefreshTeleporterSettings(true);
        }
    }

    async Task ConfigureTeleporterAsync(CancellationToken token)
    {
        var original=world.LocalPlayer();RequireTeleporterLivingState(original,world.TargetHealth(original.Id));
        var scroll=TeleportScrollSlot.Capture(world.Hotbar());
        int processId=world.Pid,slot=SelectedSavedNavigationSlot();
        IntPtr window=world.Window;string client=world.ClientHash,target=navigation.RouteTargetLabel,targetKey=navigation.RouteTargetKey;
        string targetSelection=filter.Text.Trim();
        var size=RepairScreen.Bounds(world).Size;
        void ValidateContext(bool foreground=false)
        {
            token.ThrowIfCancellationRequested();
            if(IsDisposed||!connected||working||!world.ConnectionVerified||world.Pid!=processId||world.Window!=window||world.ClientHash!=client||
                Navigation.TargetSelectionKey(filter.Text)!=targetKey||navigation.RouteTargetKey!=targetKey||SelectedSavedNavigationSlot()!=slot)
                throw new OperationCanceledException("Client, window, target selection, route slot or hunting state changed.");
            if(world.Hotbar().PageBase!=scroll.PageBase)throw new OperationCanceledException("Keep the captured Teleport Scroll hotbar page active throughout setup.");
            var windowState=world.CheckInputWindow();
            if(!windowState.OwnershipVerified||!windowState.ProcessAlive||windowState.Minimized||foreground&&!windowState.Allowed||RepairScreen.Bounds(world).Size!=size)
                throw new OperationCanceledException("Keep the same complete game window visible at the captured size; switch to the game for each capture.");
        }
        TeleporterSetupSample Read(bool foreground=false)
        {
            // Bind the process/window/selection on both sides of each fresh
            // read. Bind HP to a coherent body/map reading too; walking and a
            // recreated body are allowed before this capture-stage sample.
            ValidateContext(foreground);
            int zone=world.ActiveZone();var body=world.LocalPlayer();var health=world.TargetHealth(body.Id);
            var afterBody=world.LocalPlayer();int afterZone=world.ActiveZone();
            ValidateContext(foreground);
            var before=new TeleporterSetupSample(body,zone,health);var after=new TeleporterSetupSample(afterBody,afterZone,health);
            TeleporterSetupPolicy.RequireCaptureStable(original,before,after);return after;
        }
        Read();teleporterSetupStage="destination name";
        using var nameDialog=TeleporterStageDialog.Destination(target,SavedNavigationSlotName(slot),UiWindow,UiText);
        ShowTeleporterStage(nameDialog,token);
        string destination=nameDialog.DestinationName;

        using var departureDialog=TeleporterStageDialog.Instruction("Teleporter setup · 1 of 3",
            $"Manually use Teleport Scroll from its captured hotbar slot and wait for the 16-second countdown. When the destination map opens, leave its markers visible; do not select one yet. Remain still. Setup sends no key or click and does not consume a scroll automatically.\n\nYou will identify the marker for {destination} in a captured image. After Capture in 5s, switch to the game, remain still and move the pointer away from the map.\n\nSaved scope: {target} · {SavedNavigationSlotName(slot)}. Setup sends no game input.","Capture destination · 5s",UiWindow,UiText);
        teleporterSetupStage="destination instructions";
        ShowTeleporterStage(departureDialog,token);
        teleporterSetupStage="destination countdown";
        await RepairCountdown("Teleporter 1/3: switch to the game with the destination map open",token);
        teleporterSetupStage="destination capture";
        var beforeDeparture=Read(foreground:true);
        using var selectionImage=RepairScreen.Capture(world);
        var capturedDeparture=Read(foreground:true);
        TeleporterSetupPolicy.RequireCaptureStable(original,beforeDeparture,capturedDeparture);
        var departure=new TeleporterPosition(capturedDeparture.Zone,capturedDeparture.Body.Position,capturedDeparture.Body.Height);
        TraceLog.Record("teleporter departure captured",new{Stage=teleporterSetupStage,Departure=departure,
            capturedDeparture.Body.Id,capturedDeparture.Body.Generation,HardwareInputEmitted=false});
        Activate();teleporterSetupStage="destination selection";
        using var selectionEditor=RepairSetupForm.ForTeleporter(selectionImage,false,destination,UiWindow,UiText);
        var select=SelectTeleporterStep(selectionEditor,selectionImage.Size,token);
        TeleporterSetupPolicy.RequireDeparture(capturedDeparture,Read());

        using var confirmationDialog=TeleporterStageDialog.Instruction("Teleporter setup · 2 of 3",
            $"Manually select the blue marker for {destination} in the game. The map may close when its 'Move to Location' confirmation opens. Leave that confirmation open; do not press OK yet and do not select StartLoc. Remain at the captured departure point.\n\nAfter Capture in 5s, switch to the game and move the pointer away from the dialog. You will identify its static text and OK button in the captured image. The map does not have to remain visible.","Capture confirmation · 5s",UiWindow,UiText);
        teleporterSetupStage="confirmation instructions";
        ShowTeleporterStage(confirmationDialog,token);
        teleporterSetupStage="confirmation countdown";
        await RepairCountdown("Teleporter 2/3: switch to the game with Move to Location open",token);
        teleporterSetupStage="confirmation capture";
        var beforeConfirmation=Read(foreground:true);TeleporterSetupPolicy.RequireDeparture(capturedDeparture,beforeConfirmation);
        using var confirmationImage=RepairScreen.Capture(world);
        var afterConfirmation=Read(foreground:true);TeleporterSetupPolicy.RequireDeparture(capturedDeparture,afterConfirmation);
        TeleporterSetupPolicy.RequireCaptureStable(original,beforeConfirmation,afterConfirmation);
        if(selectionImage.Size!=confirmationImage.Size)
            throw new InvalidOperationException("Keep the same game window size for both teleporter images.");
        Activate();teleporterSetupStage="confirmation selection";
        using var confirmationEditor=RepairSetupForm.ForTeleporter(confirmationImage,true,destination,UiWindow,UiText);
        var confirm=SelectTeleporterStep(confirmationEditor,confirmationImage.Size,token);
        TeleporterSetupPolicy.RequireDeparture(capturedDeparture,Read());
        if(confirm.Marker.Matches(selectionImage))
            throw new InvalidOperationException("The confirmation marker also appears before Move to Location opens. Choose distinctive dialog text.");

        using var landingDialog=TeleporterStageDialog.Instruction("Teleporter setup · 3 of 3",
            $"Now manually press OK in the captured Move to Location dialog and wait until the character has landed at {destination} with living HP. Close the map, remain still at the landing and use the same game window.\n\nThis landing is saved in a reusable profile. You can record its onward walking routes and assign the profile afterward. Do not record the scroll jump as walking. Each assigned route must join this landing before farming can start.\n\nAfter Capture in 5s, switch to the game. Setup reads the live landing; it sends no game input. The complete link is saved only after this final check.","Capture landing · 5s",UiWindow,UiText);
        teleporterSetupStage="landing instructions";ShowTeleporterStage(landingDialog,token);
        teleporterSetupStage="landing countdown";
        await RepairCountdown("Teleporter 3/3: switch to the game and remain at the landing",token);
        teleporterSetupStage="landing capture";
        var landedSample=Read(foreground:true);TeleporterSetupPolicy.RequireLanding(capturedDeparture,landedSample);
        var landed=landedSample.Body;
        var landing=new TeleporterPosition(landedSample.Zone,landed.Position,landed.Height);
        if((landing.Position-departure.Position).Length<=TeleporterProfile.SourceRadius+TeleporterProfile.LandingRadius+2)
            throw new InvalidOperationException("The live landing is still near the departure point. Complete the manual teleport before capturing the landing.");
        var stable=Read(foreground:true);TeleporterSetupPolicy.RequireLanding(capturedDeparture,stable);
        TeleporterSetupPolicy.RequireCaptureStable(original,landedSample,stable);
        var profile=new TeleporterProfile(1,client,size.Width,size.Height,destination,original.Name,original.Id,targetSelection,slot,departure,landing,select,confirm,scroll);
        profile.Validate(client,size);
        teleporterSetupStage="saving profile";ValidateContext(foreground:true);token.ThrowIfCancellationRequested();
        var saved=ScrollRouteProfiles.Create(profile,destination);
        // A calibration survives even if no onward walking route exists yet.
        // Assign only after the normal route/landing checks have succeeded.
        bool compatible=false;
        try
        {
            RequireTeleporterHandoff(navigation.GetSavedRoute(slot),landing,landed.Name,slot,CurrentRouteCorridorRadius());
            _=TeleporterJourney.Create(navigation.GetSavedRoute(slot)!,profile,CurrentRouteCorridorRadius());compatible=true;
        }
        catch(Exception ex) when(ex is InvalidOperationException or RouteUnavailableException){ }
        if(compatible)ScrollRouteProfiles.Assign(saved.Id,targetSelection,slot);
        message=compatible ? $"Saved scroll profile '{saved.Name}' and assigned it to {target} · {SavedNavigationSlotName(slot)}." :
            $"Saved scroll profile '{saved.Name}'. Record its landing-to-farm route, then select this profile and Use for this route.";
        RefreshTeleporterSettings(true,saved.Id);
        TraceLog.Record("teleporter setup saved",new{Destination=destination,TargetSelection=targetSelection,TargetLabel=target,DestinationSlot=slot,Departure=departure,Landing=landing,
            Width=size.Width,Height=size.Height,HardwareInputEmitted=false,ManualGenerationChanged=capturedDeparture.Body.Generation!=landed.Generation});
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
            new("Configure Teleport Scroll route",$"Enter the destination name shown by the intended blue marker, for example Clauzhuz.\n\nThis creates a reusable destination profile. You can assign it to {target} · {slot} and other compatible farm routes without repeating these captures. Record onward walking routes from the landing to each farming anchor; the scroll jump is not a walking segment. Place one Teleport Scroll on the active hotbar page; its item identity and key are captured automatically.\n\nThree capture stages follow. Use Teleport Scroll manually, wait 16 seconds for its map, select the marker and press its Move to Location / OK manually. The departure is read when the first image is captured. Setup sends no game input.","Begin captures",background,foreground,true);
        internal static TeleporterStageDialog Instruction(string title,string text,string action,Color background,Color foreground)=>
            new(title,text,action,background,foreground,false);
        internal static TeleporterStageDialog RenameProfile(string name,Color background,Color foreground)
        {
            var dialog=new TeleporterStageDialog("Rename scroll profile","Give this reusable scroll calibration a recognizable name. Its destination, captures and assigned routes stay unchanged.","Rename",background,foreground,true,"Profile name");
            dialog.destination!.Text=name;return dialog;
        }
        internal bool PreviewOnly;
        protected override bool ShowWithoutActivation=>PreviewOnly;
        TeleporterStageDialog(string title,string text,string action,Color background,Color foreground,bool askDestination,string fieldLabel="Destination name")
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
                row.Controls.Add(new Label{Text=fieldLabel,AutoSize=true},0,0);row.Controls.Add(destination,0,1);layout.Controls.Add(row,0,1);
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
        if(configureTeleporter.Enabled||useTeleporterRoutes.Enabled||teleporterProfileReady||scrollRouteProfile.Enabled||
            assignScrollRouteProfile.Enabled||renameScrollRouteProfile.Enabled)
            throw new InvalidOperationException("Disconnected teleporter setup was falsely enabled.");
        try
        {
            teleporterProfileReady=true;busy=true;RefreshTeleporterControls();
            if(configureTeleporter.Enabled||useTeleporterRoutes.Enabled||scrollRouteProfile.Enabled||assignScrollRouteProfile.Enabled||
                renameScrollRouteProfile.Enabled)throw new InvalidOperationException("Teleporter controls bypassed the running/setup lock.");
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
        using(var rename=TeleporterStageDialog.RenameProfile("Shared destination",UiWindow,UiText))
        {
            rename.PreviewOnly=true;rename.StartPosition=FormStartPosition.Manual;rename.Location=new(-32000,-32000);rename.ShowInTaskbar=false;
            rename.Show();rename.PerformLayout();Application.DoEvents();
            if(rename.DestinationName!="Shared destination"||rename.AcceptButton is not Button{Enabled:true})
                throw new InvalidOperationException("Profile rename did not retain its current name.");
            using var preview=new Bitmap(rename.Width,rename.Height);rename.DrawToBitmap(preview,new(Point.Empty,rename.Size));
            preview.Save(Path.Combine(AppContext.BaseDirectory,"ui-scroll-profile-rename.png"));
        }
        scrollRouteProfile.Items.Add("Clauzhuz — saved profile");scrollRouteProfile.SelectedIndex=1;
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
        {Passed=true,HardwareInputEmitted=false,Checks=new[]{"dedicated scoped Navigation section","saved-profile selector, assignment and rename controls","disconnected controls disabled; running/setup lock","known living state required","source generation strict; manual landing generation allowed only for same character","recorded compatible selected-slot landing corridor required","destination name not assumed","paired no-input editors and prefilled profile rename rendered","sidebar fit"}}));
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
