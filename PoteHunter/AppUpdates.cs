using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PoteHunter;

internal sealed record AvailableUpdate(string Version,long AssetId,long Size,string Sha256);
internal static class AppUpdates
{
    internal const string Repository="theblusmurf/PoteHunter";
    static readonly string Preferences="Software\\PoteHunter\\Updates";
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
        string token=UpdateCredentials.Read()??throw new InvalidOperationException("Save a read-only GitHub token first.");
        var client=new HttpClient {Timeout=TimeSpan.FromSeconds(90)};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PoteHunter-Updater/1.0");
        client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version","2022-11-28");
        return client;
    }
    static void RequireResponse(System.Net.HttpStatusCode code)
    {
        if((int)code is >=200 and <300)return;
        throw new InvalidOperationException(code switch {
            System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden=>"GitHub access was denied. Check the token and repository Contents read permission.",
            System.Net.HttpStatusCode.NotFound=>"Release not accessible. Check access to the private PoteHunter repository.",
            _=>$"GitHub returned HTTP {(int)code}. Try again later."});
    }
    public static async Task<AvailableUpdate?> Check(CancellationToken token)
    {
        using var client=Client();
        using var response=await client.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest",HttpCompletionOption.ResponseHeadersRead,token);
        RequireResponse(response.StatusCode);
        using var stream=await response.Content.ReadAsStreamAsync(token);
        using var memory=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=await stream.ReadAsync(buffer,token))!=0){if(memory.Length+count>2*1024*1024)throw new InvalidOperationException("Release metadata exceeds the supported size.");memory.Write(buffer,0,count);}
        using var document=JsonDocument.Parse(memory.ToArray());return SelectRelease(document.RootElement,CurrentVersion);
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
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"updater-checks.json"),JsonSerializer.Serialize(new{Passed=true,NumericVersions=true,
            PrereleasesExcluded=true,InvalidDigestRejected=true,NoNetworkOrCredentialsUsed=true}));
    }
}

// Windows Credential Manager owns protection/storage; the token never enters
// settings, logs, installer arguments or Git. Scope is the signed-in Windows user.
internal static class UpdateCredentials
{
    const string Target="PoteHunter:GitHub:theblusmurf/PoteHunter";
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Credential
    {public uint Flags,Type;public string TargetName,Comment;public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;public uint BlobSize;public IntPtr Blob;public uint Persist,AttributeCount;public IntPtr Attributes;public string TargetAlias,UserName;}
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CredWrite(ref Credential credential,uint flags);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CredRead(string target,uint type,uint flags,out IntPtr credential);
    [DllImport("advapi32.dll")]static extern void CredFree(IntPtr pointer);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool CredDelete(string target,uint type,uint flags);
    public static string? Read()
    {
        if(!CredRead(Target,1,0,out var pointer)){if(Marshal.GetLastWin32Error()==1168)return null;throw new InvalidOperationException("Windows could not read the updater credential.");}
        try{var credential=Marshal.PtrToStructure<Credential>(pointer);if(credential.BlobSize>5120)throw new InvalidOperationException("Updater credential is invalid.");var bytes=new byte[credential.BlobSize];Marshal.Copy(credential.Blob,bytes,0,bytes.Length);return Encoding.UTF8.GetString(bytes);}finally{CredFree(pointer);}
    }
    public static void Save(string token)
    {
        token=token.Trim();if(token.Length<10 || token.Length>2000 || token.Any(char.IsWhiteSpace))throw new InvalidOperationException("Enter a valid GitHub token without whitespace.");
        var bytes=Encoding.UTF8.GetBytes(token);var pointer=Marshal.AllocHGlobal(bytes.Length);
        try{Marshal.Copy(bytes,0,pointer,bytes.Length);var credential=new Credential{Type=1,TargetName=Target,Comment="PoteHunter private release downloads",BlobSize=(uint)bytes.Length,Blob=pointer,Persist=2,UserName="PoteHunter",TargetAlias=""};if(!CredWrite(ref credential,0))throw new InvalidOperationException("Windows could not save the updater credential.");}finally{Marshal.FreeHGlobal(pointer);Array.Clear(bytes);}
    }
    public static void Delete(){if(!CredDelete(Target,1,0)&&Marshal.GetLastWin32Error()!=1168)throw new InvalidOperationException("Windows could not remove the updater credential.");}
}

public partial class HunterForm
{
    internal void ConfigureUpdates()
    {
        Shown+=async(_,_)=>
        {
            try{if(!AppUpdates.CheckOnStart || UpdateCredentials.Read()==null)return;
                var update=await AppUpdates.Check(CancellationToken.None);
                if(!IsDisposed && update!=null)message=$"{update.Version} available · Setup > Updates";
            }catch { /* Offline/expired access cannot interfere with hunting. Check manually for details. */ }
        };
    }
    async void OpenUpdates()
    {
        using var dialog=new Form{Text="PoteHunter updates",Width=570,Height=330,StartPosition=FormStartPosition.CenterParent,MinimizeBox=false,MaximizeBox=false};
        var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new(16),AutoScroll=true};dialog.Controls.Add(layout);
        layout.Controls.Add(new Label{Text=$"Installed: {AppUpdates.CurrentVersion} · {AppUpdates.Repository}",AutoSize=true});
        layout.Controls.Add(new Label{Text="Private repository: use a fine-grained token for PoteHunter with Contents: Read.\nStored in Windows Credential Manager; never copied to settings or GitHub.",AutoSize=true});
        var access=new TextBox{Width=510,UseSystemPasswordChar=true,PlaceholderText="Paste token once (leave empty to keep saved token)"};layout.Controls.Add(access);
        var row=new FlowLayoutPanel{Width=510,Height=36};layout.Controls.Add(row);
        var save=new Button{Text="Save token",AutoSize=true};var forget=new Button{Text="Remove token",AutoSize=true};var check=new Button{Text="Check updates",AutoSize=true};row.Controls.AddRange([save,forget,check]);
        var automatic=new CheckBox{Text="Check automatically at startup",Checked=AppUpdates.CheckOnStart,AutoSize=true};layout.Controls.Add(automatic);
        var result=new Label{Text="Check updates to view the latest official release.",AutoSize=true,MaximumSize=new(510,0)};layout.Controls.Add(result);
        var install=new Button{Text="Download and install",AutoSize=true,Enabled=false};layout.Controls.Add(install);
        AvailableUpdate? available=null;using var cancellation=new CancellationTokenSource();dialog.FormClosing+=(_,_)=>cancellation.Cancel();
        save.Click+=(_,_)=>{try{UpdateCredentials.Save(access.Text);access.Clear();result.Text="Token saved.";}catch(Exception ex){result.Text=ex.Message;}};
        forget.Click+=(_,_)=>{try{UpdateCredentials.Delete();access.Clear();available=null;install.Enabled=false;result.Text="Token removed.";}catch(Exception ex){result.Text=ex.Message;}};
        automatic.CheckedChanged+=(_,_)=>AppUpdates.CheckOnStart=automatic.Checked;
        check.Click+=async(_,_)=>
        {
            check.Enabled=false;install.Enabled=false;available=null;result.Text="Checking GitHub…";
            try{available=await AppUpdates.Check(cancellation.Token);if(!dialog.IsDisposed){result.Text=available==null?"No newer installable official release found.":$"{available.Version} available. Stop hunting before installing.";install.Enabled=available!=null;}}
            catch(Exception ex){if(!dialog.IsDisposed)result.Text=ex is OperationCanceledException?"Check cancelled.":ex.Message;}
            finally{if(!dialog.IsDisposed)check.Enabled=true;}
        };
        install.Click+=async(_,_)=>
        {
            if(working || busy){result.Text="Stop hunting and setup/tests before installing an update.";return;}
            if(available==null)return;install.Enabled=check.Enabled=save.Enabled=forget.Enabled=false;result.Text="Downloading and verifying installer…";
            try
            {
                string path=await AppUpdates.Download(available,cancellation.Token);
                if(working || busy || dialog.IsDisposed)return;
                if(MessageBox.Show(dialog,$"Install {available.Version}? PoteHunter will close. Your settings and saved routes stay in place.","Install update",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
                var start=new ProcessStartInfo(path){UseShellExecute=true};start.ArgumentList.Add("/DIR="+AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
                Process.Start(start);dialog.Close();Close();
            }
            catch(Exception ex){if(!dialog.IsDisposed)result.Text=ex is OperationCanceledException?"Download cancelled.":ex.Message;}
            finally{if(!dialog.IsDisposed){install.Enabled=available!=null;check.Enabled=save.Enabled=forget.Enabled=true;}}
        };
        await Task.Yield();dialog.ShowDialog(this);
    }
}
