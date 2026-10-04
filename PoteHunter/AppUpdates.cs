using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PoteHunter;

internal sealed record AvailableUpdate(string Version,long AssetId,long Size,string Sha256);
internal static class AppUpdates
{
    internal const string Repository="theblusmurf/PoteHunter-Releases";
    static readonly string Preferences="Software\\PoteHunter\\Updates";
    static string Preference(string name) { using var key=Registry.CurrentUser.OpenSubKey(Preferences); return key?.GetValue(name) as string ?? ""; }
    static void Preference(string name,string value) { using var key=Registry.CurrentUser.CreateSubKey(Preferences); key.SetValue(name,value); }
    public static bool AutoPatch { get=>Preference("AutoPatch")!="0"; set=>Preference("AutoPatch",value?"1":"0"); }
    internal static string BlockedPatch { get=>Preference("BlockedPatch"); set=>Preference("BlockedPatch",value); }
    internal static string PatchStatus { get=>Preference("PatchStatus"); set=>Preference("PatchStatus",value); }
    public static string CurrentVersion=>File.Exists(Path.Combine(AppContext.BaseDirectory,"release-version.txt"))
        ?File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"release-version.txt")).Trim():"Development";
    public static bool CheckOnStart
    {
        get {using var key=Registry.CurrentUser.OpenSubKey(Preferences);return key?.GetValue("CheckOnStart") is not int value || value!=0;}
        set {using var key=Registry.CurrentUser.CreateSubKey(Preferences);key.SetValue("CheckOnStart",value?1:0);}
    }
    internal static int ReleaseNumber(string version)=>Regex.IsMatch(version,@"^Release1\.[0-9]{1,9}$")
        && int.TryParse(version.AsSpan(9),out int number)?number:-1;
    internal static AvailableUpdate? SelectRelease(JsonElement release,string current)
    {
        if(release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())return null;
        string tag=release.GetProperty("tag_name").GetString()??"";
        if(ReleaseNumber(tag)<0 || ReleaseNumber(current)<0 || ReleaseNumber(tag)<=ReleaseNumber(current))return null;
        string expected=$"PoteHunter-{tag}-Setup.exe";
        var assets=release.GetProperty("assets").EnumerateArray().Where(a=>a.GetProperty("name").GetString()==expected).ToArray();
        if(assets.Length!=1)return null;
        var asset=assets[0];string digest=asset.TryGetProperty("digest",out var d)?d.GetString()??"":"";
        long id=asset.GetProperty("id").GetInt64(),size=asset.GetProperty("size").GetInt64();
        if(id<=0 || size<=0 || size>512L*1024*1024 || asset.GetProperty("state").GetString()!="uploaded" ||
            !Regex.IsMatch(digest,@"^sha256:[a-fA-F0-9]{64}$"))
            throw new InvalidOperationException("The release installer is incomplete or lacks a verified SHA256 digest.");
        return new(tag,id,size,digest[7..]);
    }
    static HttpClient Client()
    {
        var client=new HttpClient {Timeout=TimeSpan.FromSeconds(90)};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PoteHunter-Updater/1.0");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version","2022-11-28");
        return client;
    }
    static void RequireResponse(System.Net.HttpStatusCode code)
    {
        if((int)code is >=200 and <300)return;
        throw new InvalidOperationException(code switch {
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden=>"The public GitHub update feed is temporarily unavailable or rate limited. Try again later.",
            System.Net.HttpStatusCode.NotFound=>"No official release is available from the public PlayPoteBot update feed yet.",
            _=>$"GitHub returned HTTP {(int)code}. Try again later."});
    }
    public static async Task<AvailableUpdate?> Check(CancellationToken token,string? current=null)
    {
        using var client=Client();
        using var response=await client.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest",HttpCompletionOption.ResponseHeadersRead,token);
        RequireResponse(response.StatusCode);
        using var stream=await response.Content.ReadAsStreamAsync(token);
        using var memory=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=await stream.ReadAsync(buffer,token))!=0){if(memory.Length+count>2*1024*1024)throw new InvalidOperationException("Release metadata exceeds the supported size.");memory.Write(buffer,0,count);}
        using var document=JsonDocument.Parse(memory.ToArray());return SelectRelease(document.RootElement,current??CurrentVersion);
    }
    public static async Task<string> Download(AvailableUpdate update,CancellationToken token)
    {
        using var client=Client();using var request=new HttpRequestMessage(HttpMethod.Get,$"https://api.github.com/repos/{Repository}/releases/assets/{update.AssetId}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);RequireResponse(response.StatusCode);
        string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PoteHunter","UpdateCache",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);string path=Path.Combine(folder,$"PoteHunter-{update.Version}-Setup.exe");
        try
        {
            using var input=await response.Content.ReadAsStreamAsync(token);
            await using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {var buffer=new byte[65536];long length=0;int count;while((count=await input.ReadAsync(buffer,token))!=0){length+=count;if(length>update.Size)throw new InvalidOperationException("Installer download exceeds its published size.");await output.WriteAsync(buffer.AsMemory(0,count),token);}if(length!=update.Size)throw new InvalidOperationException("Installer download is incomplete.");}
            await using var verify=File.OpenRead(path);string hash=Convert.ToHexString(await SHA256.HashDataAsync(verify,token));
            if(!hash.Equals(update.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Installer checksum does not match GitHub. Installation blocked.");
            return path;
        }
        catch {if(File.Exists(path))File.Delete(path);throw;}
    }
    internal static void Checks()
    {
        string Fixture(string tag,bool pre=false,string? digest=null)=>JsonSerializer.Serialize(new{tag_name=tag,draft=false,prerelease=pre,
            assets=new[]{new{name=$"PoteHunter-{tag}-Setup.exe",id=17L,size=100L,state="uploaded",digest=digest??"sha256:"+new string('a',64)}}});
        AvailableUpdate? Read(string json,string current){using var doc=JsonDocument.Parse(json);return SelectRelease(doc.RootElement,current);}
        if(Read(Fixture("Release1.100"),"Release1.99")?.AssetId!=17 || Read(Fixture("Release1.99"),"Release1.100")!=null ||
            Read(Fixture("Release1.100",true),"Release1.99")!=null || Read(Fixture("Release1.100"),"Development")!=null ||
            ReleaseNumber("Release1.65/evil")!=-1)throw new Exception("Updater release selection admitted an invalid or older release.");
        bool rejected=false;try{Read(Fixture("Release1.100",digest:"sha256:bad"),"Release1.99");}catch(InvalidOperationException){rejected=true;}
        if(!rejected)throw new Exception("Updater admitted an unchecked installer.");
        using var client=Client();
        if(Repository!="theblusmurf/PoteHunter-Releases" || client.DefaultRequestHeaders.Authorization!=null)
            throw new Exception("Public updater requires anonymous access to the distribution repository.");
        AutoPatcher.Checks();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"updater-checks.json"),JsonSerializer.Serialize(new{Passed=true,NumericVersions=true,
            PrereleasesExcluded=true,InvalidDigestRejected=true,PublicFeed=true,NoAuthorizationHeader=true,NoNetworkOrCredentialsUsed=true}));
    }
}

public partial class HunterForm
{
    readonly System.Windows.Forms.Timer patchTimer=new(){Interval=10000};
    readonly CancellationTokenSource patchCancellation=new();
    bool checkingPatch;
    DateTime nextPatchCheck=DateTime.MinValue;
    AvailableUpdate? pendingPatch;
    string? stagedPatch;
    bool PatchIdle=>AutoPatcher.Idle(working,busy || clientRecoveryRunning || pendingClientResume!=null,navigation.Recording,Application.OpenForms.Cast<Form>().Any(f=>f.Modal));
    internal void ConfigureUpdates()
    {
        Shown+=async(_,_)=>{patchTimer.Start();await CheckAutomaticPatch();};
        patchTimer.Tick+=async(_,_)=>await CheckAutomaticPatch();
        FormClosed+=(_,_)=>{patchTimer.Dispose();patchCancellation.Cancel();patchCancellation.Dispose();};
    }
    async Task CheckAutomaticPatch()
    {
        if(checkingPatch || IsDisposed || !PatchIdle)return;
        if(!AppUpdates.AutoPatch && !AppUpdates.CheckOnStart)return;
        checkingPatch=true;
        try
        {
            if(DateTime.UtcNow>=nextPatchCheck)
            {
                nextPatchCheck=DateTime.UtcNow.AddMinutes(30);
                var update=await AppUpdates.Check(patchCancellation.Token);
                if(update!=pendingPatch){pendingPatch=update;stagedPatch=null;}
                if(!IsDisposed && update!=null)message=$"{update.Version} available · Setup > Updates";
            }
            if(IsDisposed || !AppUpdates.AutoPatch || pendingPatch==null ||
                pendingPatch.Version==AppUpdates.BlockedPatch || !PatchIdle)return;
            if(stagedPatch==null)
            {
                message=$"Downloading and verifying {pendingPatch.Version} · applies when stopped";
                stagedPatch=await AppUpdates.Download(pendingPatch,patchCancellation.Token);
            }
            if(IsDisposed || !AppUpdates.AutoPatch || !PatchIdle)return;
            // Prevent a hotkey or setup task starting during worker handoff.
            busy=true;start.Enabled=connect.Enabled=settings.Enabled=false;
            try
            {
                CurrentOptions().Save();Input.Release();
                string request=await AutoPatcher.Stage(stagedPatch,pendingPatch,patchCancellation.Token);
                await AutoPatcher.Launch(request,patchCancellation.Token);
                message=$"Applying {pendingPatch.Version} · reopening after installation";
                busy=false;Close();
            }
            finally {if(!IsDisposed){busy=false;start.Enabled=connect.Enabled=settings.Enabled=true;}}
        }
        catch(OperationCanceledException) { }
        catch(Exception ex)
        {
            if(!IsDisposed)message="Automatic patch deferred: "+ex.Message+" · Setup > Updates";
            // No repeated downloads/handoffs after a local staging failure.
            if(pendingPatch!=null)AppUpdates.BlockedPatch=pendingPatch.Version;
        }
        finally {checkingPatch=false;}
    }
    async void OpenUpdates()
    {
        using var dialog=new Form{Text="PlayPoteBot updates",Width=570,Height=340,StartPosition=FormStartPosition.CenterParent,MinimizeBox=false,MaximizeBox=false};
        var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new(16),AutoScroll=true};dialog.Controls.Add(layout);
        layout.Controls.Add(new Label{Text=$"Installed: {AppUpdates.CurrentVersion} · {AppUpdates.Repository}",AutoSize=true});
        layout.Controls.Add(new Label{Text="Public downloads · No GitHub sign-in or token required.",AutoSize=true});
        var check=new Button{Text="Check updates",AutoSize=true};layout.Controls.Add(check);
        var automatic=new CheckBox{Text="Check automatically at startup",Checked=AppUpdates.CheckOnStart,AutoSize=true};layout.Controls.Add(automatic);
        var patch=new CheckBox{Text="Automatically patch when hunting and setup are stopped",Checked=AppUpdates.AutoPatch,AutoSize=true};layout.Controls.Add(patch);
        layout.Controls.Add(new Label{Text="Checks every 30 minutes. Reopens with hunting stopped. Recording routes defers patches.",AutoSize=true,MaximumSize=new(510,0)});
        if(AppUpdates.PatchStatus.Length>0)layout.Controls.Add(new Label{Text=AppUpdates.PatchStatus,AutoSize=true,MaximumSize=new(510,0)});
        var result=new Label{Text="Check updates to view the latest official release.",AutoSize=true,MaximumSize=new(510,0)};layout.Controls.Add(result);
        var install=new Button{Text="Download and install",AutoSize=true,Enabled=false};layout.Controls.Add(install);
        AvailableUpdate? available=null;using var cancellation=new CancellationTokenSource();dialog.FormClosing+=(_,_)=>cancellation.Cancel();
        automatic.CheckedChanged+=(_,_)=>AppUpdates.CheckOnStart=automatic.Checked;
        patch.CheckedChanged+=(_,_)=>{AppUpdates.AutoPatch=patch.Checked;if(patch.Checked){AppUpdates.BlockedPatch="";stagedPatch=null;nextPatchCheck=DateTime.MinValue;}};
        check.Click+=async(_,_)=>
        {
            check.Enabled=false;install.Enabled=false;available=null;result.Text="Checking GitHub…";
            try{available=await AppUpdates.Check(cancellation.Token);if(!dialog.IsDisposed){result.Text=available==null?"No newer installable official release found.":$"{available.Version} available. Stop hunting before installing.";install.Enabled=available!=null;}}
            catch(Exception ex){if(!dialog.IsDisposed)result.Text=ex is OperationCanceledException?"Check cancelled.":ex.Message;}
            finally{if(!dialog.IsDisposed)check.Enabled=true;}
        };
        install.Click+=async(_,_)=>
        {
            if(!AutoPatcher.Idle(working,busy || clientRecoveryRunning || pendingClientResume!=null,navigation.Recording,false)){result.Text="Stop hunting, route recording and setup/tests before installing an update.";return;}
            if(available==null)return;install.Enabled=check.Enabled=false;result.Text="Downloading and verifying installer…";
            try
            {
                string path=await AppUpdates.Download(available,cancellation.Token);
                if(!AutoPatcher.Idle(working,busy || clientRecoveryRunning || pendingClientResume!=null,navigation.Recording,false) || dialog.IsDisposed)return;
                if(MessageBox.Show(dialog,$"Install {available.Version}? PlayPoteBot will close. Your settings and saved routes stay in place.","Install update",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
                var start=new ProcessStartInfo(path){UseShellExecute=true};start.ArgumentList.Add("/DIR="+AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
                Process.Start(start);dialog.Close();Close();
            }
            catch(Exception ex){if(!dialog.IsDisposed)result.Text=ex is OperationCanceledException?"Download cancelled.":ex.Message;}
            finally{if(!dialog.IsDisposed){install.Enabled=available!=null;check.Enabled=true;}}
        };
        await Task.Yield();dialog.ShowDialog(this);
    }
}
