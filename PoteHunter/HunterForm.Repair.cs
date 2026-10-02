namespace PoteHunter;

public sealed partial class HunterForm
{
    readonly CheckBox autoRepair=new(){Text="Auto repair after revival",AutoSize=true};
    readonly CheckBox visualRevival=new(){Text="Recognize Revive button",AutoSize=true,Checked=true};
    readonly Button configureRepair=new(){Text="Custom repair setup",AutoSize=true};
    readonly Button testRepair=new(){Text="Test repair · 5s",AutoSize=true};
    readonly Label repairStatus=new(){AutoSize=true,MaximumSize=new Size(540,0),ForeColor=UiMuted,Margin=new Padding(0,4,0,6)};
    bool repairInProgress;

    void AddRepairSettings(TableLayoutPanel card)
    {
        AddRevivalSettings(card);
        CompactAdd(card,CompactRow("Repair",autoRepair));
        var actions=CompactFlow(configureRepair,testRepair);CompactAdd(card,actions);CompactAdd(card,repairStatus);
        priorityHint.SetToolTip(autoRepair,"Recognize the inventory hammer and repair confirmation, repair once after revival, then follow the saved return route. Off by default. Requires the game's repair capability and cost.");
        priorityHint.SetToolTip(configureRepair,"Optional fallback for another inventory layout. Automatic recognition normally uses the included Domitus templates. Saved custom setup survives client updates while the same controls and window size still match. Custom setup sends no game input.");
        priorityHint.SetToolTip(testRepair,"With hunting stopped, wait 5 seconds, switch to the game, and run the configured repair sequence once. This confirms the game's repair cost.");
        void Refresh()
        {
            actions.Visible=repairStatus.Visible=autoRepair.Checked;
            revivalDelaySeconds.Enabled=farmOnArrival.Enabled=visualRevival.Enabled=autoRevive.Checked;
            reviveKey.Enabled=autoRevive.Checked&&!visualRevival.Checked;
            repairStatus.Text=!autoRevive.Checked?"Repair is saved for the next automatic revival; enable Auto revive + return to use it."
                :"Saved custom setup survives client updates with an unchanged layout. Test repair first; custom setup is optional.";
        }
        autoRepair.CheckedChanged+=(_,_)=>{Refresh();QueueCompactSave();};autoRevive.CheckedChanged+=(_,_)=>Refresh();
        visualRevival.CheckedChanged+=(_,_)=>{Refresh();QueueCompactSave();};
        priorityHint.SetToolTip(visualRevival,"Recognize the Revive button before clicking and wait for living HP. Turn off to use the configured revival key instead.");
        configureRepair.Click+=async(_,_)=>await RunRepairTool(true);
        testRepair.Click+=async(_,_)=>await RunRepairTool(false);
        Refresh();
    }

    async Task RunRepairTool(bool configure)
    {
        if(busy || working){message="Stop hunting before configuring or testing repair.";return;}
        if(!connected){message="Connect to the game before setting up repair.";return;}
        busy=true;settings.Enabled=false;start.Enabled=false;connect.Enabled=false;
        cancel=new CancellationTokenSource();var token=cancel.Token;
        try
        {
            if(configure)await ConfigureRepairAsync(token);
            else
            {
                await RepairCountdown("Repair test: switch to the game",token);
                await RunRepairAsync(token);
                message="Repair UI sequence completed. Check equipment durability in the game.";
            }
        }
        catch(OperationCanceledException){message="Repair setup/test stopped.";}
        catch(Exception ex){message="Repair stopped: "+ex.Message;repairStatus.Text=message;TraceLog.Record("repair stopped",new{Error=ex.Message});}
        finally
        {
            Input.Release();repairInProgress=false;busy=false;settings.Enabled=true;start.Enabled=true;connect.Enabled=true;
            cancel?.Dispose();cancel=null;
        }
    }

    async Task RepairCountdown(string text,CancellationToken token)
    {
        for(int seconds=5;seconds>0;seconds--){message=$"{text} · {seconds}s";await Task.Delay(1000,token);}
        token.ThrowIfCancellationRequested();
    }

    async Task ConfigureRepairAsync(CancellationToken token)
    {
        async Task<Bitmap> CaptureStage(bool confirmation)
        {
            string instructions=confirmation
                ?"Open the inventory repair hammer's confirmation dialog in the game, but do not confirm repair. After OK, you have 5 seconds to switch to the game and move the pointer away from the dialog."
                :"After OK, you have 5 seconds to switch to the game, open inventory with I, and move the pointer away from the inventory. The next screen lets you identify its title and repair hammer.";
            if(MessageBox.Show(this,instructions,"Configure repair",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)!=DialogResult.OK)
                throw new OperationCanceledException();
            await RepairCountdown("Capturing repair setup: switch to the game",token);
            var hp=world.TargetHealth(world.LocalPlayer().Id);
            if(!hp.Known || hp.Dead)throw new InvalidOperationException("Configure repair with a living character.");
            var snapshot=RepairScreen.Capture(world);Activate();return snapshot;
        }
        using var inventoryImage=await CaptureStage(false);
        using var inventoryEditor=new RepairSetupForm(inventoryImage,false,UiWindow,UiText);
        if(inventoryEditor.ShowDialog(this)!=DialogResult.OK || inventoryEditor.Selection is not { } inventory)
            throw new OperationCanceledException();
        token.ThrowIfCancellationRequested();
        using var confirmationImage=await CaptureStage(true);
        if(inventoryImage.Size!=confirmationImage.Size || !inventory.Marker.Matches(confirmationImage))
            throw new InvalidOperationException("Keep the inventory in the same position. Its recognition text must remain visible behind the repair dialog.");
        using var confirmationEditor=new RepairSetupForm(confirmationImage,true,UiWindow,UiText);
        if(confirmationEditor.ShowDialog(this)!=DialogResult.OK || confirmationEditor.Selection is not { } confirmation)
            throw new OperationCanceledException();
        token.ThrowIfCancellationRequested();
        if(confirmation.Marker.Matches(inventoryImage))
            throw new InvalidOperationException("The chosen text also appears without the repair dialog. Select the distinctive repair question instead.");
        new RepairProfile(1,world.ClientHash,inventoryImage.Width,inventoryImage.Height,
            inventory.Marker,inventory.Button!,confirmation.Marker,confirmation.Button!).Save();
        message=repairStatus.Text="Repair setup saved. Cancel the open game confirmation and close inventory before testing.";
        TraceLog.Record("repair setup saved",new{Width=inventoryImage.Width,Height=inventoryImage.Height,Client=world.ClientHash});
    }

    async Task RunRepairAsync(CancellationToken token)
    {
        var self=world.LocalPlayer();int zone=world.ActiveZone();
        RepairProfile? profile=null;
        if(File.Exists(RepairProfile.PathName))
        {
            try{profile=RepairProfile.Load(RepairScreen.Bounds(world).Size);}
            catch(Exception ex) when(ex is InvalidOperationException or IOException)
            {
                TraceLog.Record("repair setup rejected",new{Reason=ex.Message});
                throw new InvalidOperationException("Saved repair setup cannot be used. "+ex.Message,ex);
            }
            if(!string.Equals(profile.ClientHash,world.ClientHash,StringComparison.OrdinalIgnoreCase))
                TraceLog.Record("repair setup reused after client update",new{CapturedClient=profile.ClientHash,
                    CurrentClient=world.ClientHash,profile.Width,profile.Height,VisualControlsStillRequired=true});
        }
        var previousPreflight=Input.Preflight;
        void Validate()
        {
            token.ThrowIfCancellationRequested();
            var current=world.LocalPlayer();var hp=world.TargetHealth(current.Id);
            if(hp.Dead && working && activeGuardOptions?.AutoReviveAfterDeath==true)
            {ObserveDeath(hp);throw new DeathRecoveryRequiredException();}
            if(!LocalCharacter.Same(self,current) || world.ActiveZone()!=zone || !hp.Known || hp.Dead)
                throw new InvalidOperationException("Repair stopped because the character, map, or health changed.");
        }
        repairInProgress=true;
        Input.PickupHoldProvider=null;ReleaseCombatPickup();movement?.StopApproach();Input.Release();
        try
        {
            Input.Preflight=()=>{previousPreflight?.Invoke();Validate();};
            message=repairStatus.Text="Running inventory repair";
            await AutoRepair.Run(new LiveRepairSurface(world,profile,Validate,token),token);
            repairStatus.Text="Repair UI sequence completed; inventory closed.";
            TraceLog.Record("repair sequence completed",new{DurabilityVerified=false,BeforeReturn=deathRecovery.Pending});
        }
        finally {Input.Release();Input.Preflight=previousPreflight;repairInProgress=false;}
    }
}
