using System.Diagnostics;

namespace PoteHunter;

internal sealed class LoginSetupDialog : Form
{
    internal bool PreviewOnly;
    protected override bool ShowWithoutActivation=>PreviewOnly;
}

public sealed partial class HunterForm
{
    readonly CheckBox autoClientRecovery=new(){Text="Relaunch client + login + return",AutoSize=true};
    readonly Button setupClientRecovery=new(){Text="Login setup",AutoSize=true};
    readonly Button testClientLogin=new(){Text="Test login · 5s",AutoSize=true};
    readonly Label clientRecoveryStatus=new(){AutoSize=true,MaximumSize=new Size(540,0),ForeColor=UiMuted};
    ClientRecoveryProfile? clientRecoveryProfile;
    ClientResume? armedClientResume,pendingClientResume,resumingClient;
    bool clientRecoveryRunning,internalClientStop;
    CancellationTokenSource? clientRecoveryCancel;

    void AddClientRecoverySettings(TableLayoutPanel card)
    {
        try{clientRecoveryProfile=ClientRecoveryProfile.Read();clientRecoveryProfile?.Validate();autoClientRecovery.Checked=clientRecoveryProfile?.Enabled==true;}
        catch{clientRecoveryProfile=null;clientRecoveryStatus.Text="Saved login setup is invalid. Configure it again.";}
        CompactAdd(card,CompactRow("Client crash",autoClientRecovery));
        CompactAdd(card,CompactFlow(setupClientRecovery,testClientLogin));CompactAdd(card,clientRecoveryStatus);
        if(clientRecoveryStatus.Text.Length==0)clientRecoveryStatus.Text=clientRecoveryProfile==null?"Configure login screens and a local password. Requires a saved solo return route.":"Login setup saved. Test from the login screen while hunting is stopped.";
        autoClientRecovery.CheckedChanged+=(_,_)=>
        {
            if(clientRecoveryProfile==null){autoClientRecovery.Checked=false;clientRecoveryStatus.Text="Complete Login setup before enabling recovery.";return;}
            try{if(autoClientRecovery.Checked)clientRecoveryProfile.VerifyLauncher();clientRecoveryProfile=clientRecoveryProfile with{Enabled=autoClientRecovery.Checked};clientRecoveryProfile.Save();if(!autoClientRecovery.Checked)CancelClientRecovery();}
            catch(Exception ex){autoClientRecovery.Checked=false;clientRecoveryStatus.Text=ex.Message;}
        };
        setupClientRecovery.Click+=async(_,_)=>await ConfigureClientLogin();
        testClientLogin.Click+=async(_,_)=>await TestClientLogin();
        priorityHint.SetToolTip(autoClientRecovery,"Only an unexpected client exit during a solo hunt resumes automatically. PlayPoteBot retains your anchor/facing and selected saved route. F9, manual Stop and focus loss cancel recovery. Password is encrypted for this Windows account.");
    }
    void CancelClientRecovery(){armedClientResume=null;pendingClientResume=null;resumingClient=null;clientRecoveryCancel?.Cancel();}
    string? ClientRecoveryStartProblem(Options options)
    {
        if(!autoClientRecovery.Checked)return null;
        if(options.GroupMode || options.HealerMode)return "Client recovery currently requires solo hunting. Turn it off for group/healer mode.";
        try
        {
            var profile=clientRecoveryProfile ?? throw new InvalidOperationException("Complete Login setup before enabling client recovery.");profile.VerifyLauncher();
            if(!profile.Hash.Equals(world.ClientHash,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Login setup belongs to a different client build. Calibrate the connected client.");
            var self=world.LocalPlayer();
            var route=navigation.SavedRoutesForZone(world.ActiveZone()).FirstOrDefault(x=>RecoveryRouting.Compatible(x.Route,world.ActiveZone(),self.Name,self.Height) && RecoveryTravel.Recorded(x.Route) && (x.Route.Anchor-self.Position).Length<=2.5);
            if(route.Route==null && RecoveryTravel.StartupSlot(navigation.SavedRoutes,self.Position,world.ActiveZone(),self.Name,self.Height,SelectedSavedNavigationSlot(),(double)options.RouteCorridorRadius)<0)
                return "Record a compatible route for the selected targets before enabling client recovery (Home / End in Navigation).";
            var secret=LoginSecret.Open(profile.ProtectedPassword);System.Security.Cryptography.CryptographicOperations.ZeroMemory(secret);
        }
        catch(Exception ex){return ex.Message;}
        return null;
    }
    void ArmClientRecovery(Entity self,int zone,Vec anchor,double heading,double height,Options options,SavedNavigationRoute? route,int slot)
    {
        if(autoClientRecovery.Checked && !options.GroupMode && !options.HealerMode && route!=null && RecoveryTravel.Recorded(route))
            armedClientResume=new(self.Name,zone,anchor,heading,height,options.Target,route,slot);
        else armedClientResume=null;
    }
    bool TryQueueClientRecovery()
    {
        if(pendingClientResume!=null)return true;
        if(!world.ClientProcessExited || !ClientRecoveryPolicy.MayRecover(autoClientRecovery.Checked,working,world.ClientProcessAlive,false,armedClientResume!=null))return false;
        pendingClientResume=armedClientResume;connected=false;
        internalClientStop=true;try{Stop("Client exited unexpectedly. Preparing saved login recovery.");}finally{internalClientStop=false;}
        TraceLog.Record("client recovery queued",new{Character=pendingClientResume!.Character,Zone=pendingClientResume.Zone,Anchor=pendingClientResume.Anchor});
        return true;
    }
    async Task RecoverClientIfPending()
    {
        if(pendingClientResume==null || busy || working || clientRecoveryRunning)return;
        var saved=pendingClientResume;pendingClientResume=null;clientRecoveryRunning=true;
        clientRecoveryCancel=new();var token=clientRecoveryCancel.Token;
        bool restart=false;
        settings.Enabled=false;start.Enabled=connect.Enabled=false;
        try
        {
            await RunClientLogin(true,token);token.ThrowIfCancellationRequested();
            long loadDeadline=Environment.TickCount64+60000;
            do
            {
                token.ThrowIfCancellationRequested();
                if(connected && !world.CheckInputWindow().Allowed)throw new OperationCanceledException("Client recovery lost game focus.");
                await Connect();token.ThrowIfCancellationRequested();if(connected)break;await Task.Delay(1000,token);
            }while(Environment.TickCount64<loadDeadline);
            if(!connected)throw new InvalidOperationException("Login finished but the character could not be read. Hunting remains stopped.");
            var self=world.LocalPlayer();
            if(!ClientRecoveryPolicy.SameCharacter(saved,self.Name,world.ActiveZone(),CurrentOptions().Target))
                throw new InvalidOperationException("The loaded character, zone or target selection differs from the interrupted hunt. Hunting remains stopped.");
            if(!world.TargetHealth(self.Id).Known || world.TargetHealth(self.Id).Dead)
                throw new InvalidOperationException("Client recovery requires verified living health before route return.");
            navigation.SelectTargetSelection(saved.Target);
            var route=navigation.GetSavedRoute(saved.Slot);
            if(route==null || route!=saved.Route && !route.Points.SequenceEqual(saved.Route.Points) ||
                !RecoveryRouting.Compatible(route,saved.Zone,saved.Character,saved.Height) || RecoveryTravel.Nearest(route,self.Position).Distance>(double)CurrentOptions().RouteCorridorRadius)
                throw new InvalidOperationException("The character is outside the saved route corridor, or the route changed. Recovery stopped.");
            TraceLog.Record("client login verified; saved return pending",new{Character=self.Name,Anchor=saved.Anchor});
            restart=true;
        }
        catch(OperationCanceledException){message="Client recovery cancelled. Hunting remains stopped.";}
        catch(Exception ex){message="Client recovery stopped: "+ex.Message;TraceLog.Record("client recovery stopped",new{Reason=ex.Message});}
        finally
        {
            clientRecoveryRunning=false;clientRecoveryCancel?.Dispose();clientRecoveryCancel=null;
            if(!working){armedClientResume=null;settings.Enabled=true;start.Enabled=connect.Enabled=true;}
            clientRecoveryStatus.Text=message;
        }
        if(restart){resumingClient=saved;try{await StartHunting();}finally{if(ReferenceEquals(resumingClient,saved))resumingClient=null;}}
    }
    static Process? FindConfiguredClient(ClientRecoveryProfile profile)
    {
        var clients=Process.GetProcessesByName("client");
        if(clients.Length>1){foreach(var p in clients)p.Dispose();throw new InvalidOperationException("More than one client is running. Choose a single game client before login recovery.");}
        if(clients.Length==0)return null;
        var process=clients[0];
        try{if(!string.Equals(ProcessImagePath.Read(process),profile.Executable,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("A different client executable is running. Recovery will not launch a duplicate.");return process;}
        catch{process.Dispose();throw;}
    }
    static Process? FindConfiguredLauncher(LauncherCalibration launcher)
    {
        var found=Process.GetProcessesByName(Path.GetFileNameWithoutExtension(launcher.Executable));
        if(found.Length>1){foreach(var item in found)item.Dispose();throw new InvalidOperationException("Multiple game launchers are running. Keep only one before recovery.");}
        if(found.Length==0)return null;
        var process=found[0];
        try{if(!string.Equals(ProcessImagePath.Read(process),launcher.Executable,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("A different launcher executable is running.");return process;}
        catch{process.Dispose();throw;}
    }
    static GameWindow.Candidate? LauncherWindow(Process process)
    {
        var report=GameWindow.Inspect(process);
        var windows=report.Candidates.Where(c=>c.ProcessId==process.Id && c.DirectProcessId==process.Id && c.Visible && c.Owner==0 && c.ClientWidth>=320 && c.ClientHeight>=200).ToArray();
        if(windows.Length>1)throw new InvalidOperationException("Multiple launcher windows are visible. Close extra launcher dialogs before recovery.");
        return windows.SingleOrDefault();
    }
    internal static async Task<Process?> DiscoverSetupProcess(Func<Process?> discover,TimeSpan timeout)
    {
        var pending=Task.Run(discover);
        try{return await pending.WaitAsync(timeout);}
        catch(TimeoutException)
        {
            // A delayed native read must not leak its returned Process or re-enter the UI.
            _=pending.ContinueWith(done=>{if(done.Status==TaskStatus.RanToCompletion)done.Result?.Dispose();else _=done.Exception;},TaskScheduler.Default);
            throw new InvalidOperationException("Process discovery timed out. Setup is responsive; check the selected executable and try again.");
        }
    }
    async Task<Process> LaunchClientThroughLauncher(ClientRecoveryProfile profile,CancellationToken token)
    {
        profile.VerifyLauncher();token.ThrowIfCancellationRequested();var launcher=profile.Launcher!;
        using var process=FindConfiguredLauncher(launcher) ?? Process.Start(new ProcessStartInfo(launcher.Executable){WorkingDirectory=Path.GetDirectoryName(launcher.Executable)!,UseShellExecute=true})
            ?? throw new InvalidOperationException("The game launcher could not be started.");
        message="Waiting for the game launcher Play/Start button.";
        GameWindow.Candidate? identity=null;long readyDeadline=Environment.TickCount64+30000;
        while(identity==null)
        {
            token.ThrowIfCancellationRequested();
            if(process.HasExited || Environment.TickCount64>=readyDeadline)throw new InvalidOperationException("The launcher did not open a usable window within 30 seconds.");
            identity=LauncherWindow(process);if(identity==null)await Task.Delay(500,token);
        }
        Input.SetForegroundWindow((nint)identity.Handle);await Task.Delay(250,token);
        var surface=new LoginSurface(process,identity,profile,true);
        await ClientRecoveryPolicy.RunRecognizedSteps([launcher.Play],step=>surface.Recognize(step,token),async(step,ct)=>
        {
            using var alreadyRunning=FindConfiguredClient(profile);
            if(alreadyRunning!=null)throw new InvalidOperationException("A client appeared before Play was clicked. Recovery stopped to avoid launching a duplicate.");
            await surface.Act(step,ct);TraceLog.Record("client launcher Play completed",new{Confirmed=true});
        },Task.Delay,()=>Environment.TickCount64,token);
        message="Launcher Play clicked once. Waiting for Client.exe.";
        long deadline=Environment.TickCount64+90000;
        while(Environment.TickCount64<deadline)
        {
            token.ThrowIfCancellationRequested();profile.VerifyFile();
            var client=FindConfiguredClient(profile);if(client!=null)return client;
            await Task.Delay(500,token);
        }
        throw new InvalidOperationException("The launcher did not start the calibrated Client.exe within 90 seconds. Play was not repeated.");
    }
    async Task RunClientLogin(bool relaunch,CancellationToken token)
    {
        var profile=clientRecoveryProfile ?? throw new InvalidOperationException("Configure Login setup first.");profile.VerifyFile();
        if(connected && world.ClientProcessAlive)throw new InvalidOperationException("Disconnect/log out to the login screen before testing login. No input sent.");
        var existing=FindConfiguredClient(profile);bool launched=existing==null;
        using var process=existing ?? await LaunchClientThroughLauncher(profile,token);
        if(launched){message="Client opened through launcher. Waiting for login window.";await Task.Delay(3000,token);}
        GameWindow.Candidate? identity=null;long deadline=Environment.TickCount64+30000;
        while(identity==null)
        {
            token.ThrowIfCancellationRequested();if(process.HasExited || Environment.TickCount64>=deadline)throw new InvalidOperationException("Client did not open a recognized game window within 30 seconds.");
            var report=GameWindow.Inspect(process);
            if(report.SelectedHandle!=0)identity=report.Candidates.Single(c=>c.Handle==report.SelectedHandle);
            else await Task.Delay(500,token);
        }
        if(relaunch || launched){Input.SetForegroundWindow((nint)identity.Handle);await Task.Delay(250,token);}
        var surface=new LoginSurface(process,identity,profile);
        await ClientRecoveryPolicy.RunSteps(profile,step=>surface.Recognize(step,token),async(step,ct)=>
        {
            message="Login recovery: "+step.Name;await surface.Act(step,ct);TraceLog.Record("client login step completed",new{Step=Array.IndexOf(profile.Steps,step)+1,Password=step.Password});
        },Task.Delay,()=>Environment.TickCount64,token);
        // Loading is read-only. Login setup never adds hidden/repeated Enter clicks.
        message="Login buttons completed. Waiting for character loading.";
        for(int i=0;i<25;i++){surface.Check(token);await Task.Delay(200,token);}
    }
    async Task TestClientLogin()
    {
        if(busy || working || clientRecoveryRunning)return;
        clientRecoveryRunning=true;settings.Enabled=false;start.Enabled=connect.Enabled=false;clientRecoveryCancel=new();var token=clientRecoveryCancel.Token;
        try{for(int s=5;s>0;s--){message=$"Login test: switch to the game · {s}s";await Task.Delay(1000,token);}await RunClientLogin(false,token);message="Login steps completed. Test leaves hunting stopped; use Connect to verify your character.";}
        catch(OperationCanceledException){message="Login test cancelled.";}
        catch(Exception ex){message="Login test stopped: "+ex.Message;}
        finally{clientRecoveryRunning=false;clientRecoveryCancel.Dispose();clientRecoveryCancel=null;settings.Enabled=true;start.Enabled=connect.Enabled=true;clientRecoveryStatus.Text=message;}
    }
    async Task ConfigureClientLogin()
    {
        if(busy || working || clientRecoveryRunning){message="Stop hunting before Login setup.";return;}
        clientRecoveryRunning=true;settings.Enabled=false;start.Enabled=connect.Enabled=false;
        try{using var dialog=CreateClientLoginSetup();await Task.CompletedTask;dialog.ShowDialog(this);}
        finally{clientRecoveryRunning=false;settings.Enabled=true;start.Enabled=connect.Enabled=true;}
    }
    internal Form CreateClientLoginSetup()
    {
        var dialog=new LoginSetupDialog{Text="PlayPoteBot · Login setup",Size=new(730,850),MinimumSize=new(650,610),StartPosition=FormStartPosition.CenterParent,BackColor=UiWindow,ForeColor=UiText,Font=Font};
        var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new(16)};dialog.Controls.Add(layout);
        var instructions=new Label{AutoSize=true,MaximumSize=new(660,0),Text="Choose the game launcher and capture its Play/Start button first. Keep client.exe selected separately for verification and login captures; PlayPoteBot never starts it directly. Add one password-field step followed by Login and character/Enter Game buttons. Open each screen manually before Capture. Capture with the game password EMPTY and pointer away. Select static screen text distinct from the button. Setup sends no game input.\n\nPassword stays encrypted for this Windows account. Blank keeps the existing secret. Your saved account name stays in the game."};layout.Controls.Add(instructions);
        LauncherCalibration? launcher=clientRecoveryProfile?.Launcher;
        var launcherPath=new TextBox{Width=620,ReadOnly=true,Text=launcher?.Executable??""};layout.Controls.Add(launcherPath);
        var launcherActions=new FlowLayoutPanel{AutoSize=true};var browseLauncher=new Button{Text="Choose game launcher",AutoSize=true};var captureLauncher=new Button{Text="Capture launcher Play · 5s",AutoSize=true};launcherActions.Controls.AddRange([browseLauncher,captureLauncher]);layout.Controls.Add(launcherActions);
        var launcherStatus=new Label{AutoSize=true,Text=launcher==null?"Launcher Play/Start not captured.":"Launcher Play/Start captured."};layout.Controls.Add(launcherStatus);
        var executable=new TextBox{Width=620,ReadOnly=true,Text=clientRecoveryProfile?.Executable??""};layout.Controls.Add(executable);
        var browse=new Button{Text="Choose client.exe",AutoSize=true};layout.Controls.Add(browse);
        var password=new TextBox{Width=350,UseSystemPasswordChar=true,MaxLength=256};layout.Controls.Add(new Label{Text="Local password (never enter it in chat)",AutoSize=true});layout.Controls.Add(password);
        var list=new ListBox{Width=620,Height=150};layout.Controls.Add(list);
        var name=new TextBox{Width=280,MaxLength=60,Text="Login"};var kind=new CheckBox{Text="This step fills the password field",AutoSize=true};layout.Controls.Add(new Label{Text="Login step name",AutoSize=true});layout.Controls.Add(name);layout.Controls.Add(kind);
        var actions=new FlowLayoutPanel{AutoSize=true};var capture=new Button{Text="Capture next step · 5s",AutoSize=true};var remove=new Button{Text="Remove last",AutoSize=true};var save=new Button{Text="Save setup",AutoSize=true};actions.Controls.AddRange([capture,remove,save]);layout.Controls.Add(actions);
        var status=new Label{AutoSize=true,MaximumSize=new(640,0)};layout.Controls.Add(status);
        var steps=clientRecoveryProfile?.Steps.ToList()??[];int width=clientRecoveryProfile?.Width??0,height=clientRecoveryProfile?.Height??0;
        void Refresh(){list.Items.Clear();for(int i=0;i<steps.Count;i++)list.Items.Add($"{i+1}. {steps[i].Name}"+(steps[i].Password?" · password":" · click"));}Refresh();
        browse.Click+=(_,_)=>{using var picker=new OpenFileDialog{Title="Choose the game's client.exe",Filter="Game client|client.exe",CheckFileExists=true};if(picker.ShowDialog(dialog)==DialogResult.OK){executable.Text=Path.GetFullPath(picker.FileName);steps.Clear();width=height=0;Refresh();}};
        remove.Click+=(_,_)=>{if(steps.Count>0){steps.RemoveAt(steps.Count-1);Refresh();}};
        bool capturing=false;
        dialog.FormClosing+=(_,e)=>{if(capturing){e.Cancel=true;status.Text="Wait for the capture countdown to finish.";}};
        browseLauncher.Click+=(_,_)=>{using var picker=new OpenFileDialog{Title="Choose PlayPOTE launcher (not Client.exe)",Filter="Game launcher|*.exe",CheckFileExists=true};if(picker.ShowDialog(dialog)==DialogResult.OK){launcherPath.Text=Path.GetFullPath(picker.FileName);launcher=null;launcherStatus.Text="Capture the launcher's Play/Start button.";}};
        captureLauncher.Click+=async(_,_)=>
        {
            capturing=true;actions.Enabled=password.Enabled=browse.Enabled=launcherActions.Enabled=false;
            try
            {
                if(!File.Exists(launcherPath.Text) || Path.GetFileName(launcherPath.Text).Equals("client.exe",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Choose the game launcher, not Client.exe.");
                var selected=new LauncherCalibration(launcherPath.Text,ClientRecoveryProfile.FileHash(launcherPath.Text),0,0,null!);
                status.Text="Checking the selected launcher executable…";
                using var process=await DiscoverSetupProcess(()=>FindConfiguredLauncher(selected),TimeSpan.FromSeconds(5))??throw new InvalidOperationException("Open the game launcher manually and display Play/Start first.");
                for(int s=5;s>0;s--){status.Text=$"Switch to the launcher with the pointer away from Play · {s}s";await Task.Delay(1000);}
                var identity=LauncherWindow(process)??throw new InvalidOperationException("Launcher window unavailable.");
                if(!NavigationOverlay.TryGetClientScreenBounds((nint)identity.Handle,out var bounds))throw new InvalidOperationException("Launcher bounds unavailable.");
                selected=selected with{Width=bounds.Width,Height=bounds.Height};
                var temporary=new ClientRecoveryProfile(1,false,executable.Text,"",0,0,"",[]){Launcher=selected};
                using var frame=new LoginSurface(process,identity,temporary,true).Capture(default);dialog.Activate();
                using var editor=RepairSetupForm.ForLogin(frame,false,UiWindow,UiText);editor.Text="PlayPoteBot launcher setup · Play/Start";
                if(editor.ShowDialog(dialog)==DialogResult.OK && editor.Selection is {} captured)
                {
                    selected=selected with{Play=new("Launcher Play",false,captured.Marker,captured.Button,captured.Click)};selected.VerifyFile();launcher=selected;
                    launcherStatus.Text="Launcher Play/Start captured.";status.Text="Continue capturing the client login steps below.";
                }
            }
            catch(Exception ex){status.Text=ex.Message;}
            finally{capturing=false;actions.Enabled=password.Enabled=browse.Enabled=launcherActions.Enabled=true;}
        };
        capture.Click+=async(_,_)=>
        {
            if(steps.Count>=8){status.Text="Maximum eight steps. Remove the last step to recapture.";return;}
            capturing=true;actions.Enabled=false;password.Enabled=false;browse.Enabled=false;launcherActions.Enabled=false;
            try
            {
                if(!File.Exists(executable.Text) || name.Text.Trim().Length==0)throw new InvalidOperationException("Choose client.exe and name this step.");
                var profile=new ClientRecoveryProfile(1,false,executable.Text,ClientRecoveryProfile.FileHash(executable.Text),width,height,"",[]);
                status.Text="Checking the selected client executable…";
                using var process=await DiscoverSetupProcess(()=>FindConfiguredClient(profile),TimeSpan.FromSeconds(5))??throw new InvalidOperationException("Open the configured client and manually display this screen first.");
                for(int s=5;s>0;s--){status.Text=$"Switch to the game with an empty password field · {s}s";await Task.Delay(1000);}
                var handle=GameWindow.Find(process,out var identity);
                if(!NavigationOverlay.TryGetClientScreenBounds(handle,out var bounds))throw new InvalidOperationException("Game window unavailable.");
                if(width!=0 && bounds.Size!=new Size(width,height))throw new InvalidOperationException("Keep the same window size for every login step.");
                profile=profile with{Width=bounds.Width,Height=bounds.Height};var surface=new LoginSurface(process,identity,profile);
                using var frame=surface.Capture(default);dialog.Activate();
                using var editor=RepairSetupForm.ForLogin(frame,kind.Checked,UiWindow,UiText);
                if(editor.ShowDialog(dialog)==DialogResult.OK && editor.Selection is {} selected)
                {steps.Add(new(name.Text.Trim(),kind.Checked,selected.Marker,selected.Button,selected.Click));width=frame.Width;height=frame.Height;Refresh();status.Text="Step captured. Manually open the next screen, then capture it.";}
            }
            catch(Exception ex){status.Text=ex.Message;}
            finally{capturing=false;actions.Enabled=true;password.Enabled=true;browse.Enabled=true;launcherActions.Enabled=true;}
        };
        save.Click+=(_,_)=>
        {
            try
            {
                string secret=password.Text.Length>0?LoginSecret.Protect(password.Text):clientRecoveryProfile?.ProtectedPassword??"";
                var profile=new ClientRecoveryProfile(1,false,executable.Text,ClientRecoveryProfile.FileHash(executable.Text),width,height,secret,steps.ToArray()){Launcher=launcher};profile.VerifyLauncher();profile.Save();
                clientRecoveryProfile=profile;autoClientRecovery.Checked=false;password.Clear();clientRecoveryStatus.Text="Login setup saved. Test from the login screen, then enable client recovery.";dialog.DialogResult=DialogResult.OK;
            }
            catch(Exception ex){status.Text=ex.Message;}
        };
        dialog.FormClosed+=(_,_)=>password.Clear();
        return dialog;
    }
    internal void CheckClientRecoveryUi()
    {
        using var dialog=CreateClientLoginSetup();((LoginSetupDialog)dialog).PreviewOnly=true;dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new(-30000,-30000);dialog.Show();dialog.PerformLayout();Application.DoEvents();
        var children=dialog.Controls[0].Controls.Cast<Control>().ToArray();
        if(!children.OfType<TextBox>().Any(t=>t.UseSystemPasswordChar && t.MaxLength==256) ||
            !children.OfType<ListBox>().Any() || !children.Any(c=>c.Text=="Choose client.exe") || !children.OfType<FlowLayoutPanel>().Any(p=>p.Controls.Cast<Control>().Any(c=>c.Text=="Capture launcher Play · 5s")))
            throw new Exception("Login setup must expose a masked local password, step list and client picker.");
        using var preview=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(preview,new Rectangle(Point.Empty,dialog.Size));
        preview.Save(Path.Combine(AppContext.BaseDirectory,"ui-client-login-setup.png"));
    }
}
