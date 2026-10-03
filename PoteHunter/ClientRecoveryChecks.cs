using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PoteHunter;

internal static class ClientRecoveryChecks
{
    static void Require(bool value,string message){if(!value)throw new Exception("Client recovery: "+message);}
    internal static void Run()=>RunAsync().GetAwaiter().GetResult();
    static async Task RunAsync()
    {
        using(var self=System.Diagnostics.Process.GetCurrentProcess())
            Require(ProcessImagePath.Read(self).Equals(Environment.ProcessPath,StringComparison.OrdinalIgnoreCase),"direct executable path query differs from current process");
        using(var self=await HunterForm.DiscoverSetupProcess(()=>System.Diagnostics.Process.GetCurrentProcess(),TimeSpan.FromSeconds(2)))
            Require(self?.Id==Environment.ProcessId,"background setup discovery lost process identity");
        bool discoveryTimeout=false;
        // Bound the delayed worker too; it returns no native handle after timeout.
        try{await HunterForm.DiscoverSetupProcess(()=>{Thread.Sleep(100);return null;},TimeSpan.FromMilliseconds(20));}
        catch(InvalidOperationException){discoveryTimeout=true;}
        Require(discoveryTimeout,"blocked setup discovery did not return through timeout");
        using var image=new Bitmap(640,480);
        using(var g=Graphics.FromImage(image)){g.Clear(Color.Black);g.FillRectangle(Brushes.White,15,15,15,10);g.FillRectangle(Brushes.White,110,100,15,10);}
        var marker=RepairPatch.Capture(image,new(10,10,40,24));var button=RepairPatch.Capture(image,new(100,90,40,24));
        const string password="Offline-password-\u03a9";
        string encrypted=LoginSecret.Protect(password);byte[] opened=LoginSecret.Open(encrypted);
        try{Require(Encoding.Unicode.GetString(opened)==password,"Windows encrypted password did not round-trip");}finally{CryptographicOperations.ZeroMemory(opened);}
        var profile=new ClientRecoveryProfile(1,false,@"C:\Game\client.exe",new string('a',64),640,480,encrypted,
            [new("Password",true,marker,null,new(120,120)),new("Login",false,marker,button,button.Center),new("Enter game",false,marker,button,button.Center)]);
        profile.Validate();Require(!JsonSerializer.Serialize(profile).Contains(password),"plaintext secret serialized");
        var launcher=new LauncherCalibration(@"C:\Game\PlayPOTE-Launcher.exe",new string('b',64),640,480,new("Launcher Play",false,marker,button,button.Center));
        launcher.Validate();
        var upgraded=profile with{Launcher=launcher};upgraded.Validate();
        var restored=JsonSerializer.Deserialize<ClientRecoveryProfile>(JsonSerializer.Serialize(upgraded))!;
        restored.Validate();Require(restored.Launcher?.Executable==launcher.Executable && restored.Launcher.Play.Point==launcher.Play.Point && restored.ProtectedPassword==profile.ProtectedPassword && restored.Steps.Length==profile.Steps.Length,"launcher migration discarded existing calibration/secret");
        foreach(var bad in new[]{launcher with{Executable=profile.Executable},launcher with{Play=profile.Steps[0]},launcher with{Width=0},launcher with{Play=launcher.Play with{Button=marker}},launcher with{Hash="invalid"}})
        {bool failed=false;try{bad.Validate();}catch(InvalidOperationException){failed=true;}Require(failed,"invalid launcher accepted");}
        var folder=Path.Combine(Path.GetTempPath(),"PPB-client-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var executable=Path.Combine(folder,"client.exe");
        try
        {
            File.WriteAllText(executable,"offline fixture, never executable");
            var verified=profile with{Executable=executable,Hash=ClientRecoveryProfile.FileHash(executable)};verified.VerifyFile();
            bool missingLauncher=false;try{verified.VerifyLauncher();}catch(InvalidOperationException){missingLauncher=true;}Require(missingLauncher,"legacy direct-client launch remained allowed");
            var launcherExecutable=Path.Combine(folder,"PlayPOTE-Launcher.exe");File.WriteAllText(launcherExecutable,"launcher fixture, never executable");
            try
            {
                var launchVerified=verified with{Launcher=launcher with{Executable=launcherExecutable,Hash=ClientRecoveryProfile.FileHash(launcherExecutable)}};launchVerified.VerifyLauncher();
                File.AppendAllText(launcherExecutable,"changed");bool launchRejected=false;try{launchVerified.VerifyLauncher();}catch(InvalidOperationException){launchRejected=true;}Require(launchRejected,"changed launcher accepted");
            }
            finally{File.Delete(launcherExecutable);}
            File.AppendAllText(executable,"changed");bool rejected=false;try{verified.VerifyFile();}catch(InvalidOperationException){rejected=true;}
            Require(rejected,"changed executable accepted");
        }
        finally{File.Delete(executable);Directory.Delete(folder);}
        long now=0;var actions=new List<string>();int observations=0;
        Task Delay(int ms,CancellationToken ct){ct.ThrowIfCancellationRequested();now+=ms;return Task.CompletedTask;}
        await ClientRecoveryPolicy.RunSteps(profile,_=>{observations++;return true;},(step,ct)=>{actions.Add(step.Name);return Task.CompletedTask;},Delay,()=>now,default);
        Require(actions.SequenceEqual(new[]{"Password","Login","Enter game"}) && observations==6,"step order or single-action gates");
        actions.Clear();now=0;observations=0;
        await ClientRecoveryPolicy.RunRecognizedSteps([launcher.Play],_=>{observations++;return true;},(step,ct)=>{actions.Add(step.Name);return Task.CompletedTask;},Delay,()=>now,default);
        await ClientRecoveryPolicy.RunSteps(profile,_=>{observations++;return true;},(step,ct)=>{actions.Add(step.Name);return Task.CompletedTask;},Delay,()=>now,default);
        Require(actions.SequenceEqual(new[]{"Launcher Play","Password","Login","Enter game"}) && observations==8,"launcher must precede login with one recognized click");
        actions.Clear();now=0;bool timeout=false;
        try{await ClientRecoveryPolicy.RunSteps(profile,_=>false,(step,ct)=>{actions.Add(step.Name);return Task.CompletedTask;},Delay,()=>now,default);}catch(InvalidOperationException){timeout=true;}
        Require(timeout && actions.Count==0 && now<=30200,"unrecognized screen sent input or timeout exceeded");
        using var cancelled=new CancellationTokenSource();actions.Clear();now=0;
        try{await ClientRecoveryPolicy.RunSteps(profile,_=>true,(step,ct)=>{actions.Add(step.Name);cancelled.Cancel();return Task.CompletedTask;},Delay,()=>now,cancelled.Token);}catch(OperationCanceledException){}
        Require(actions.Count==1,"cancelled sequence continued to another step");
        foreach(var bad in new[]{profile with{Steps=[]},profile with{Steps=[profile.Steps[1]]},profile with{Steps=[profile.Steps[0]]},profile with{Steps=[profile.Steps[0],profile.Steps[0],profile.Steps[1]]},profile with{Executable=@"C:\Game\unrelated.exe"},profile with{ProtectedPassword=""}})
        {bool failed=false;try{bad.Validate();}catch(InvalidOperationException){failed=true;}Require(failed,"invalid setup accepted");}
        Require(profile.Steps[1].Matches(image),"captured paired gates rejected");
        using(var g=Graphics.FromImage(image))g.FillRectangle(Brushes.Black,button.Bounds);
        Require(!profile.Steps[1].Matches(image),"missing button accepted");
        Require(ClientRecoveryPolicy.MayRecover(true,true,false,false,true),"unexpected exit not recoverable");
        Require(!ClientRecoveryPolicy.MayRecover(true,true,true,false,true) && !ClientRecoveryPolicy.MayRecover(true,true,false,true,true) &&
            !ClientRecoveryPolicy.MayRecover(false,true,false,false,true) && !ClientRecoveryPolicy.MayRecover(true,false,false,false,true) && !ClientRecoveryPolicy.MayRecover(true,true,false,false,false),"manual stop/focus/live/route gates");
        var route=new SavedNavigationRoute(5,new(0,12),1,[new(0,12),new(0,6),new(0,0)],DateTime.UnixEpoch,"Test",10,5);
        var saved=new ClientResume("Test",5,new(.1,12),1,10,"Mimic",route,0);
        Require(ClientRecoveryPolicy.SameCharacter(saved,"test",5," mimic ") && !ClientRecoveryPolicy.SameCharacter(saved,"Other",5,"Mimic") &&
            !ClientRecoveryPolicy.SameCharacter(saved,"Test",6,"Mimic") && !ClientRecoveryPolicy.SameCharacter(saved,"Test",5,"Tribal"),"resume identity guard");
        Require(RecoveryTravel.Nearest(route,new(0,0)).Distance==0 && RecoveryTravel.Nearest(route,new(11,0)).Distance>10,"route corridor guard");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"client-recovery-checks.json"),JsonSerializer.Serialize(new{Passed=true,HardwareInputEmitted=false,
            Checks=new[]{"DPAPI round trip; no plaintext serialization","launcher calibration retains login steps/secret","direct-client launcher rejected","changed/missing launcher rejected","launcher Play once before login","ordered single actions with two recognition observations","unrecognized screen timeout without input","cancellation stops remaining steps","paired screen/button gating","invalid profiles rejected","manual stop/live-process/route guards","character/zone/target identity","route corridor"}}));
    }
}
