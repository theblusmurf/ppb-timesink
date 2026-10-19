namespace PoteHunter;

public sealed partial class HunterForm
{
    readonly ComboBox repairMethod=new(){Name="repairMethod",DropDownStyle=ComboBoxStyle.DropDownList,Width=250};
    readonly Button recordMerchantRoute=new(){Name="recordMerchantRoute",Text="Record Hoys route",AutoSize=true};
    readonly Button finishMerchantRoute=new(){Name="finishMerchantRoute",Text="Finish Hoys route · End",AutoSize=true};
    readonly Button cancelMerchantRoute=new(){Name="cancelMerchantRoute",Text="Cancel recording",AutoSize=true};
    readonly Label merchantRouteStatus=new(){Name="merchantRouteStatus",AutoSize=true,MaximumSize=new(540,0),ForeColor=UiMuted};
    readonly CheckBox killForMerchantRepair=new(){Name="killForMerchantRepair",Text="Use /kill at durability threshold",AutoSize=true};
    FlowLayoutPanel? merchantRouteActions;
    bool recordingMerchantRoute;
    Entity? merchantRouteOrigin;
    int merchantRouteZone;
    string merchantSetupSummary="Record a route from the shared hunt-route start to Hoys in Roche Camp, then capture his shop controls.";
    RepairMethod SelectedRepairMethod=>repairMethod.SelectedIndex==1?RepairMethod.Merchant:RepairMethod.Inventory;

    void AddMerchantRepairSettings(TableLayoutPanel card)
    {
        repairMethod.Items.AddRange(["Inventory","Merchant · Hoys, Roche Camp"]);repairMethod.SelectedIndex=0;
        CompactAdd(card,CompactRow("Repair using",repairMethod));
        CompactAdd(card,killForMerchantRepair);
        merchantRouteActions=CompactFlow(recordMerchantRoute,finishMerchantRoute,cancelMerchantRoute);
        CompactAdd(card,merchantRouteActions);CompactAdd(card,merchantRouteStatus);
        repairMethod.SelectedIndexChanged+=(_,_)=>{RefreshMerchantRepairSettings();QueueCompactSave();};
        killForMerchantRepair.CheckedChanged+=(_,_)=>QueueCompactSave();
        priorityHint.SetToolTip(killForMerchantRepair,"Optional, off by default. At the low-durability threshold send /kill once, require confirmed death, then auto-revive, visit Hoys, repair and return along the saved hunt route. Requires both Auto revive + return and Auto repair after revival. Failure stops without another command.");
        recordMerchantRoute.Click+=(_,_)=>StartMerchantRouteRecording();
        finishMerchantRoute.Click+=(_,_)=>FinishMerchantRouteRecording();
        cancelMerchantRoute.Click+=(_,_)=>
        {
            if(busy||working||!recordingMerchantRoute)return;
            navigation.EndRecording();recordingMerchantRoute=false;merchantRouteOrigin=null;
            message="Hoys recording cancelled; the previous saved setup is preserved.";RefreshNavigationRecordingControls();
        };
        priorityHint.SetToolTip(repairMethod,"Inventory uses the existing inventory hammer. Merchant follows your separate Hoys route, repairs through his General Store, verifies durability, and retraces the trip before resuming.");
        priorityHint.SetToolTip(recordMerchantRoute,"Stand at the same start used by your hunt return routes. Record, walk to Hoys, stop within interaction range facing his name, then Save or End. Hunt route slots are preserved. This button records manual movement only.");
        RefreshMerchantSetupSummary();RefreshMerchantRepairSettings();
    }
    void RefreshMerchantSetupSummary()
    {
        try
        {
            var profile=MerchantRepairProfile.Load();
            merchantSetupSummary=$"Hoys: {profile.Route.Points.Length} route points · {profile.Route.Character} · zone {profile.Route.Zone}. "+
                (profile.Ui==null?"Capture shop controls next.":"Route and shop controls saved. Keep the same camera zoom, pitch and UI layout.");
        }
        catch(Exception ex) when(ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {merchantSetupSummary=ex.Message;}
    }
    void RefreshMerchantRepairSettings()
    {
        if(merchantRouteActions==null)return;
        bool merchant=SelectedRepairMethod==RepairMethod.Merchant;
        merchantRouteActions.Visible=merchantRouteStatus.Visible=merchant;
        killForMerchantRepair.Visible=merchant;
        bool idle=!busy&&!working&&!clientRecoveryRunning;
        recordMerchantRoute.Enabled=idle&&connected&&!navigation.Recording;
        finishMerchantRoute.Enabled=idle&&connected&&recordingMerchantRoute&&navigation.Recording;
        cancelMerchantRoute.Enabled=idle&&recordingMerchantRoute;
        repairMethod.Enabled=idle&&!navigation.Recording;
        killForMerchantRepair.Enabled=idle&&!navigation.Recording;
        configureRepair.Text=merchant?"Capture Hoys shop":"Custom repair setup";
        merchantRouteStatus.Text=recordingMerchantRoute
            ?navigation.Recording?$"Recording Hoys route · {navigation.RecordingTrail.Count} points. Stop at Hoys, face his name, then press End.":"Hoys recording was interrupted. Record it again; the previous saved setup is preserved."
            :merchantSetupSummary;
    }
    void StartMerchantRouteRecording()
    {
        if(busy||working||clientRecoveryRunning||navigation.Recording){message="Stop hunting and finish the current route recording first.";return;}
        if(!connected){message="Connect before recording the Hoys route.";return;}
        try
        {
            var self=world.LocalPlayer();var hp=world.TargetHealth(self.Id);
            if(!hp.Known||hp.Dead)throw new InvalidOperationException("Record with a living character.");
            merchantRouteOrigin=self;merchantRouteZone=world.ActiveZone();
            navigation.BeginRecording(self.Position);recordingMerchantRoute=true;
            message="Recording Hoys route. Walk from the shared route start to Hoys, face his name, then press End.";
        }
        catch(Exception ex){message="Hoys route recording stopped: "+ex.Message;}
        RefreshNavigationRecordingControls();
    }
    void FinishMerchantRouteRecording()
    {
        if(busy||working||!connected){message="Connect and stop hunting before saving the Hoys route.";return;}
        try
        {
            if(!recordingMerchantRoute||!navigation.Recording||navigation.RecordingCancelled||merchantRouteOrigin==null)
                throw new InvalidOperationException("Start a new Hoys recording before saving it.");
            var self=world.LocalPlayer();var hp=world.TargetHealth(self.Id);
            if(!LocalCharacter.Same(merchantRouteOrigin,self)||world.ActiveZone()!=merchantRouteZone||!hp.Known||hp.Dead)
                throw new InvalidOperationException("Character, map or health changed during recording.");
            var points=Navigation.NormalizeRoute(navigation.RecordingTrail,self.Position).ToArray();
            var route=new SavedNavigationRoute(merchantRouteZone,self.Position,world.PlayerHeading(),points,DateTime.UtcNow,self.Name,self.Height);
            new MerchantRepairProfile(1,world.ClientHash,route,merchantRouteOrigin.Height,null).Save();
            navigation.EndRecording();recordingMerchantRoute=false;merchantRouteOrigin=null;
            RefreshMerchantSetupSummary();
            message="Hoys route saved separately. Capture Hoys shop controls next; hunt route slots were preserved.";
        }
        catch(Exception ex){message="Hoys route not saved: "+ex.Message;}
        RefreshNavigationRecordingControls();
    }

    async Task ConfigureMerchantRepairAsync(CancellationToken token)
    {
        var profile=MerchantRepairProfile.Load();var original=world.LocalPlayer();int zone=world.ActiveZone(),pid=world.Pid;
        nint window=world.Window;string hash=world.ClientHash;var size=RepairScreen.Bounds(world).Size;
        void Validate()
        {
            token.ThrowIfCancellationRequested();var self=world.LocalPlayer();var hp=world.TargetHealth(self.Id);
            if(!connected||!world.ConnectionVerified||world.Pid!=pid||world.Window!=window||world.ClientHash!=hash||
                !LocalCharacter.Same(original,self)||world.ActiveZone()!=zone||!hp.Known||hp.Dead||
                !RecoveryRouting.Compatible(profile.Route,zone,self.Name,self.Height)||(self.Position-profile.Route.Anchor).Length>.5||RepairScreen.Bounds(world).Size!=size)
                throw new InvalidOperationException("Keep the same living character stopped at the saved Hoys endpoint, on the captured floor and window size.");
        }
        Validate();double heading=world.PlayerHeading();
        async Task<Bitmap> Capture(string instructions)
        {
            if(MessageBox.Show(this,instructions+"\n\nAfter OK, you have five seconds to switch to the game and move the pointer away. Setup sends no game input.","Hoys repair setup",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)!=DialogResult.OK)
                throw new OperationCanceledException();
            await RepairCountdown("Capturing Hoys setup: switch to the game",token);Validate();
            var image=RepairScreen.Capture(world);Activate();return image;
        }
        RecognitionSelection Pick(Bitmap image,string stage,bool pointOnly)
        {
            using var editor=RepairSetupForm.ForMerchant(image,stage,pointOnly,UiWindow,UiText);
            if(editor.ShowDialog(this)!=DialogResult.OK||editor.Selection==null)throw new OperationCanceledException();
            return editor.Selection;
        }
        using var npcImage=await Capture("Close inventory, menus and the shop. Stand at the saved endpoint facing Hoys with his clickable name visible. Keep this camera zoom and pitch for future trips.");
        heading=world.PlayerHeading();
        var npc=Pick(npcImage,"Hoys name",true);
        using var menuImage=await Capture("Manually click Hoys' name. Leave the menu open with General Store Merchant visible; do not select it yet.");
        var menu=Pick(menuImage,"General Store Merchant",true);
        using var shopImage=await Capture("Manually select General Store Merchant. Leave the shop open with Repair All visible; do not click Repair All.");
        var repair=Pick(shopImage,"Repair All",false);var close=Pick(shopImage,"shop close",false);
        var answer=MessageBox.Show(this,"Does Repair All open a separate cost-confirmation dialog?\n\nYes: capture that dialog before its confirmation is clicked.\nNo: verify durability directly after the Repair All click.","Hoys repair confirmation",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);
        if(answer==DialogResult.Cancel)throw new OperationCanceledException();
        RecognitionSelection? confirm=null;
        if(answer==DialogResult.Yes)
        {
            using var confirmationImage=await Capture("Manually open the Repair All confirmation. Do not confirm the cost. Leave the repair question and its confirmation button visible.");
            confirm=Pick(confirmationImage,"repair confirmation",false);
            if(confirm.Marker.MatchesText(shopImage)||confirm.Marker.MatchesControl(shopImage))
                throw new InvalidOperationException("The confirmation marker also appears without its dialog. Select the distinctive repair question.");
        }
        Validate();var ui=new MerchantRepairUi(size.Width,size.Height,npc,menu,repair,close,confirm);ui.Validate(size);
        var first=LiveMerchantRepair.Recognize(npcImage,ui);var second=LiveMerchantRepair.Recognize(menuImage,ui);var third=LiveMerchantRepair.Recognize(shopImage,ui);
        if(!first.Hoys||first.GeneralStore||first.Shop||!second.GeneralStore||second.Shop||!third.Shop||!third.RepairAll||!third.CloseShop)
            throw new InvalidOperationException("The captured stages are not distinct. Capture Hoys' name, General Store Merchant, then the shop again.");
        (profile with{ClientHash=hash,Route=profile.Route with{Heading=heading},Ui=ui}).Save();
        RefreshMerchantSetupSummary();RefreshMerchantRepairSettings();
        message=repairStatus.Text="Hoys route and shop setup saved. Cancel any open cost dialog and close the shop before Test repair.";
    }

    string? MerchantRepairStartProblem(Options options)
    {
        if(options.EquipmentRepairMethod!=RepairMethod.Merchant||(!options.AutoRepairAfterDeath&&!options.AutoRepairLowDurability))return null;
        try
        {
            if(options.GroupMode||options.HealerMode)throw new InvalidOperationException("Merchant repair trips require solo hunt mode.");
            if(options.AutoRepairLowDurability&&options.UseKillForMerchantRepair&&(!options.AutoReviveAfterDeath||!options.AutoRepairAfterDeath))
                throw new InvalidOperationException("Use /kill at threshold requires Auto revive + return and Auto repair after revival.");
            if(options.AutoRepairLowDurability&&options.UseKillForMerchantRepair&&options.VisualRevivalDetection)
                _=RevivalProfile.Load(world.ClientHash,RepairScreen.Bounds(world).Size);
            var profile=MerchantRepairProfile.Load();
            _=profile.ControlsFor(world.ClientHash,RepairScreen.Bounds(world).Size);
            _=profile.Plan(navigation.SavedRoutes,world.LocalPlayer(),world.ActiveZone(),(double)options.RouteCorridorRadius);
            if(!navigation.SavedRoutes.Any(route=>route!=null&&RecoveryTravel.SharedOrigin(route,profile.Route)))
                throw new InvalidOperationException("Record the Hoys route from the same start as this target's saved hunt route.");
            return null;
        }
        catch(Exception ex) when(ex is InvalidOperationException or IOException or UnauthorizedAccessException or RouteUnavailableException)
        {return "Merchant repair setup: "+ex.Message;}
    }
}
