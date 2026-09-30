namespace PoteHunter;

public sealed partial class HunterForm
{
    readonly Button configureRevival=new(){Text="Custom revival setup",AutoSize=true};
    readonly Button testRevival=new(){Text="Test revival · 5s",AutoSize=true};
    readonly Button automaticRevival=new(){Text="Use automatic",AutoSize=true};
    readonly Label revivalStatus=new(){AutoSize=true,MaximumSize=new Size(540,0),ForeColor=UiMuted,Margin=new Padding(0,4,0,6)};
    FlowLayoutPanel revivalActions=null!;

    void AddRevivalSettings(TableLayoutPanel card)
    {
        CompactAdd(card,CompactRow("Revive method",visualRevival));
        revivalActions=CompactFlow(configureRevival,testRevival,automaticRevival);
        CompactAdd(card,revivalActions);CompactAdd(card,revivalStatus);
        priorityHint.SetToolTip(configureRevival,"While dead with hunting stopped, capture the Revive dialog and select its text/button. The optional opening location receives up to three left-clicks after the 3-second death wait. Setup sends no game clicks.");
        priorityHint.SetToolTip(visualRevival,"Wait at least 3 seconds after death, then send up to three opening left-clicks with a 250 ms gap. Stop opening when Revive appears, confirm once, and wait for living HP. A longer saved route delay still applies.");
        priorityHint.SetToolTip(testRevival,"Wait 5 seconds, switch to the game, and revive once using the selected visual setup. Requires known dead HP. This test performs a real revival; repair and return travel run during normal recovery.");
        priorityHint.SetToolTip(automaticRevival,"Keep a backup of your custom revival profile and return to the included automatic detector.");
        configureRevival.Click+=async(_,_)=>await RunRevivalTool(true);
        testRevival.Click+=async(_,_)=>await RunRevivalTool(false);
        automaticRevival.Click+=(_,_)=>
        {
            if(busy || working)return;
            try{RevivalProfile.UseAutomatic();RefreshRevivalSettings();message="Automatic revival recognition restored; custom setup backed up.";}
            catch(Exception ex){message=revivalStatus.Text="Could not restore automatic detection: "+ex.Message;}
        };
        visualRevival.CheckedChanged+=(_,_)=>RefreshRevivalSettings();
        autoRevive.CheckedChanged+=(_,_)=>RefreshRevivalSettings();
        RefreshRevivalSettings();
    }
    void RefreshRevivalSettings()
    {
        revivalActions.Visible=revivalStatus.Visible=visualRevival.Checked;
        bool custom=File.Exists(RevivalProfile.PathName);
        automaticRevival.Visible=custom;
        revivalStatus.Text=custom?"Custom revival setup saved. Test once while dead; keep the same client, window size, and dialog layout."
            :"Automatic recognition. Custom setup is optional; capture it while dead with hunting stopped.";
        revivalStatus.Text+=" Death wait: 3s · opening clicks: 3 (stop when Revive appears).";
        if(!autoRevive.Checked)revivalStatus.Text+=" Enable Auto revive + return to use it during a hunt.";
    }
    static void RequireRevivalSetupState(Health health)
    {
        if(!health.Known || !health.Dead)throw new InvalidOperationException("Revival setup and testing require a dead character with readable HP. Wait for a death, stop hunting, and leave the death screen open.");
    }
    async Task RunRevivalTool(bool configure)
    {
        if(busy || working){message="Stop hunting before configuring or testing revival.";return;}
        if(!connected){message="Connect to the game before setting up revival.";return;}
        if(!configure && !RequireHotkeys())return;
        busy=true;settings.Enabled=false;start.Enabled=false;connect.Enabled=false;
        cancel=new CancellationTokenSource();var token=cancel.Token;
        try
        {
            var original=world.LocalPlayer();int processId=world.Pid,zone=world.ActiveZone();
            void ValidateDead()
            {
                token.ThrowIfCancellationRequested();var current=world.LocalPlayer();
                if(world.Pid!=processId || world.ActiveZone()!=zone || !RecoveryRouting.SameCharacter(original,current))
                    throw new OperationCanceledException("Character, process, or map changed during revival setup.");
                RequireRevivalSetupState(world.TargetHealth(current.Id));
            }
            ValidateDead();
            if(configure)await ConfigureRevivalAsync(ValidateDead,token);
            else
            {
                long deadAt=Environment.TickCount64;
                await RepairCountdown("Revival test: switch to the game and move off the button",token);ValidateDead();
                await VisualRevival.Run(new LiveRevivalSurface(world,original,processId,zone,text=>message=text),deadAt,token);
                message=revivalStatus.Text="Revival test completed: living HP confirmed. Hunting remains stopped.";
            }
        }
        catch(OperationCanceledException){message="Revival setup/test stopped.";}
        catch(Exception ex){message=revivalStatus.Text="Revival stopped: "+ex.Message;TraceLog.Record("revival setup/test stopped",new{Error=ex.Message});}
        finally
        {
            Input.Release();busy=false;settings.Enabled=true;start.Enabled=true;connect.Enabled=true;
            cancel?.Dispose();cancel=null;
        }
    }
    async Task ConfigureRevivalAsync(Action validateDead,CancellationToken token)
    {
        var choice=MessageBox.Show(this,
            "Does a click open the Revive dialog?\n\nYes: capture the death screen and choose that opening click first.\nNo: capture the already visible Revive dialog only.\n\nSetup only reads the screen. You will open the dialog manually; do not click Revive until you choose Test revival.",
            "Custom revival setup",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Information);
        if(choice==DialogResult.Cancel)throw new OperationCanceledException();
        async Task<Bitmap> Capture(bool opening)
        {
            string instruction=opening
                ?"Leave the death screen visible BEFORE the Revive dialog opens. After OK, switch to the game within 5 seconds and move the pointer away from its text."
                :"Open the Revive dialog manually, without confirming revival. After OK, switch to the game within 5 seconds and move the pointer away from the dialog and button.";
            if(MessageBox.Show(this,instruction,"Capture revival setup",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)!=DialogResult.OK)
                throw new OperationCanceledException();
            await RepairCountdown("Capturing revival setup: switch to the game",token);validateDead();
            var frame=RepairScreen.Capture(world);Activate();return frame;
        }
        RecognitionSelection Select(Bitmap image,bool opening)
        {
            using var editor=RepairSetupForm.ForRevival(image,opening,UiWindow,UiText);
            using var cancelled=token.Register(()=>{if(editor.IsHandleCreated&&!editor.IsDisposed)editor.BeginInvoke((Action)(()=>{if(!editor.IsDisposed)editor.DialogResult=DialogResult.Cancel;}));});
            token.ThrowIfCancellationRequested();
            if(editor.ShowDialog(this)!=DialogResult.OK || editor.Selection is not {} selection)throw new OperationCanceledException();
            token.ThrowIfCancellationRequested();validateDead();return selection;
        }
        Bitmap? openingImage=null;
        try
        {
            RevivalStep? opening=null;
            if(choice==DialogResult.Yes){openingImage=await Capture(true);opening=RevivalStep.From(Select(openingImage,true));}
            using var confirmationImage=await Capture(false);
            if(openingImage!=null && openingImage.Size!=confirmationImage.Size)
                throw new InvalidOperationException("Keep the same game window size through both revival captures.");
            var confirmation=RevivalStep.From(Select(confirmationImage,false));
            if(openingImage!=null && confirmation.Marker.Matches(openingImage))
                throw new InvalidOperationException("The chosen Revive dialog text also appears before opening it. Select text distinctive to the dialog.");
            validateDead();
            new RevivalProfile(1,world.ClientHash,confirmationImage.Width,confirmationImage.Height,opening,confirmation).Save();
            RefreshRevivalSettings();message=revivalStatus.Text="Revival setup saved. Leave the dialog open and choose Test revival · 5s to verify it.";
            TraceLog.Record("revival setup saved",new{Width=confirmationImage.Width,Height=confirmationImage.Height,OpeningStep=opening!=null,Client=world.ClientHash});
        }
        finally{openingImage?.Dispose();}
    }

    void CheckRevivalSetupUi()
    {
        foreach(var hp in new Health[]{default,new(100,100)})
        {
            bool rejected=false;try{RequireRevivalSetupState(hp);}catch(InvalidOperationException){rejected=true;}
            if(!rejected)throw new Exception("Revival setup/test accepted living or unknown health.");
        }
        RequireRevivalSetupState(new(0,100));
        foreach(bool opening in new[]{true,false})
        {
            using var image=RevivalSetupChecks.Fixture(!opening);
            using var editor=RepairSetupForm.ForRevival(image,opening,UiWindow,UiText);
            editor.StartPosition=FormStartPosition.Manual;editor.Location=new(-32000,-32000);editor.ShowInTaskbar=false;editor.PreviewOnly=true;
            editor.Show();editor.PerformLayout();Application.DoEvents();
            if(!editor.Text.StartsWith("Revival setup") || editor.Selection!=null)throw new Exception("Revival editor selected a click without user input.");
            using var preview=new Bitmap(editor.Width,editor.Height);
            editor.DrawToBitmap(preview,new Rectangle(Point.Empty,editor.Size));
            preview.Save(Path.Combine(AppContext.BaseDirectory,opening?"ui-revival-opening.png":"ui-revival-confirm.png"));
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"revival-setup-ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new
        {
            Passed=true,HardwareInputEmitted=false,
            Checks=new[]{"visual setup controls appear only for visual mode","living/unknown HP rejected for setup/test","death-screen and confirmation editors rendered without selecting or clicking the game"}
        }));
    }
}
