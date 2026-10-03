using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PoteHunter;

internal sealed record LoginStep(string Name,bool Password,RepairPatch Marker,RepairPatch? Button,Point Point)
{
    public bool Matches(Bitmap frame)=>Marker.MatchesText(frame) && (Password || Button is {} button && (button.MatchesControl(frame)||button.MatchesText(frame)));
}
internal sealed record ClientRecoveryProfile(int Version,bool Enabled,string Executable,string Hash,int Width,int Height,
    string ProtectedPassword,LoginStep[] Steps)
{
    public LauncherCalibration? Launcher { get; init; }
    internal static string PathName=>Path.Combine(AppContext.BaseDirectory,"client-recovery-profile.json");
    internal static ClientRecoveryProfile? Read()=>File.Exists(PathName)?JsonSerializer.Deserialize<ClientRecoveryProfile>(File.ReadAllText(PathName)):null;
    internal void Validate()
    {
        Launcher?.Validate();
        if(Version!=1 || Width<320 || Height<200 || Steps is not {Length:>=1 and <=8} ||
            !Path.IsPathFullyQualified(Executable) || !Path.GetFileName(Executable).Equals("client.exe",StringComparison.OrdinalIgnoreCase) ||
            Hash.Length!=64 || !Hash.All(Uri.IsHexDigit) || Steps[^1].Password || Steps.Count(s=>s.Password)!=1 ||
            string.IsNullOrWhiteSpace(ProtectedPassword))throw new InvalidOperationException("Capture one password step followed by login/character buttons (up to eight steps) and save a local password.");
        foreach(var step in Steps)
            if(string.IsNullOrWhiteSpace(step.Name) || step.Name.Length>60 || !step.Marker.Valid(new(Width,Height)) ||
                !new Rectangle(0,0,Width,Height).Contains(step.Point) ||
                !step.Password && (step.Button?.Valid(new(Width,Height))!=true || step.Marker.Bounds.IntersectsWith(step.Button.Bounds)))
                throw new InvalidOperationException("A login screen/button selection is invalid. Capture it again.");
    }
    internal void VerifyLauncher()
    {
        VerifyFile();
        (Launcher ?? throw new InvalidOperationException("Choose and capture the game launcher Play/Start button in Login setup. Existing login steps are retained.")).VerifyFile();
    }
    internal void VerifyFile()
    {
        Validate();
        if(!File.Exists(Executable) || !FileHash(Executable).Equals(Hash,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The client executable changed or is missing. Recalibrate login setup.");
    }
    internal static string FileHash(string path){using var file=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();}
    internal void Save()
    {
        Validate();var temporary=PathName+".tmp";File.WriteAllText(temporary,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));File.Move(temporary,PathName,true);
    }
}

internal sealed record LauncherCalibration(string Executable,string Hash,int Width,int Height,LoginStep Play)
{
    internal void Validate()
    {
        if(!Path.IsPathFullyQualified(Executable) || !Path.GetExtension(Executable).Equals(".exe",StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(Executable).Equals("client.exe",StringComparison.OrdinalIgnoreCase) || Hash.Length!=64 || !Hash.All(Uri.IsHexDigit) ||
            Width<320 || Height<200 || Play==null || Play.Password || string.IsNullOrWhiteSpace(Play.Name) ||
            !Play.Marker.Valid(new(Width,Height)) || Play.Button?.Valid(new(Width,Height))!=true ||
            Play.Marker.Bounds.IntersectsWith(Play.Button.Bounds) || !new Rectangle(0,0,Width,Height).Contains(Play.Point))
            throw new InvalidOperationException("Capture the launcher screen and its Play/Start button. The launcher must be different from client.exe.");
    }
    internal void VerifyFile()
    {
        Validate();
        if(!File.Exists(Executable) || !ClientRecoveryProfile.FileHash(Executable).Equals(Hash,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The game launcher changed or is missing. Recalibrate its Play/Start button.");
    }
}

// DPAPI binds the saved secret to this Windows account. No secret reaches settings, trace logs, clipboard or process arguments.
internal static class LoginSecret
{
    [StructLayout(LayoutKind.Sequential)] struct Blob{public int Length;public nint Data;}
    [DllImport("crypt32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CryptProtectData(ref Blob input,string description,nint entropy,nint reserved,nint prompt,uint flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)] static extern bool CryptUnprotectData(ref Blob input,nint description,nint entropy,nint reserved,nint prompt,uint flags,out Blob output);
    [DllImport("kernel32.dll")] static extern nint LocalFree(nint memory);
    static byte[] Transform(byte[] value,bool protect)
    {
        var input=new Blob{Length=value.Length,Data=Marshal.AllocHGlobal(value.Length)};Blob output=default;
        try
        {
            Marshal.Copy(value,0,input.Data,value.Length);
            bool ok=protect?CryptProtectData(ref input,"PPB client login",0,0,0,1,out output):CryptUnprotectData(ref input,0,0,0,0,1,out output);
            if(!ok)throw new InvalidOperationException("Windows could not access the saved login secret. Enter it again in setup.");
            var result=new byte[output.Length];Marshal.Copy(output.Data,result,0,result.Length);return result;
        }
        finally
        {
            for(int i=0;i<input.Length;i++)Marshal.WriteByte(input.Data,i,0);Marshal.FreeHGlobal(input.Data);
            if(output.Data!=0){for(int i=0;i<output.Length;i++)Marshal.WriteByte(output.Data,i,0);LocalFree(output.Data);}
        }
    }
    internal static string Protect(string password)
    {
        if(password.Length is <1 or >256 || password.Any(char.IsControl))throw new InvalidOperationException("Enter a password of 1–256 characters without control characters.");
        var bytes=Encoding.Unicode.GetBytes(password);try{return Convert.ToBase64String(Transform(bytes,true));}finally{CryptographicOperations.ZeroMemory(bytes);}
    }
    internal static byte[] Open(string saved)
    {
        var bytes=Transform(Convert.FromBase64String(saved),false);
        if(bytes.Length is <2 or >512 || bytes.Length%2!=0){CryptographicOperations.ZeroMemory(bytes);throw new InvalidOperationException("Saved password is invalid. Enter it again locally.");}
        return bytes;
    }
}

internal sealed record ClientResume(string Character,int Zone,Vec Anchor,double Heading,double Height,string Target,SavedNavigationRoute Route,int Slot);
internal static class ClientRecoveryPolicy
{
    internal static bool MayRecover(bool enabled,bool hunting,bool processAlive,bool manualStop,bool hasRoute)=>enabled && hunting && !processAlive && !manualStop && hasRoute;
    internal static bool SameCharacter(ClientResume saved,string character,int zone,string target)=>
        saved.Character.Equals(character,StringComparison.OrdinalIgnoreCase) && saved.Zone==zone &&
        saved.Target.Trim().Equals(target.Trim(),StringComparison.OrdinalIgnoreCase);
    internal static async Task RunSteps(ClientRecoveryProfile profile,Func<LoginStep,bool> recognize,
        Func<LoginStep,CancellationToken,Task> perform,Func<int,CancellationToken,Task> delay,Func<long> now,CancellationToken token)
    {
        profile.Validate();
        await RunRecognizedSteps(profile.Steps,recognize,perform,delay,now,token);
    }
    internal static async Task RunRecognizedSteps(IEnumerable<LoginStep> steps,Func<LoginStep,bool> recognize,
        Func<LoginStep,CancellationToken,Task> perform,Func<int,CancellationToken,Task> delay,Func<long> now,CancellationToken token)
    {
        foreach(var step in steps)
        {
            long deadline=now()+30000;int stable=0;
            while(stable<2){token.ThrowIfCancellationRequested();if(now()>=deadline)throw new InvalidOperationException($"Login screen not recognized: {step.Name}. Recovery stopped without repeating clicks.");stable=recognize(step)?stable+1:0;await delay(200,token);}
            // The live surface rechecks both recognition gates immediately before input.
            await perform(step,token);await delay(500,token);
        }
    }
}

// Separate pre-login input surface: never weakens the connected-world input gate.
internal sealed class LoginSurface(Process process,GameWindow.Candidate identity,ClientRecoveryProfile profile,bool launcher=false)
{
    string Executable=>launcher?profile.Launcher!.Executable:profile.Executable;
    Size FrameSize=>launcher?new(profile.Launcher!.Width,profile.Launcher.Height):new(profile.Width,profile.Height);
    [StructLayout(LayoutKind.Sequential)] struct Mouse{public int X,Y;public uint Data,Flags,Time;public nuint Extra;}
    [StructLayout(LayoutKind.Sequential)] struct Keyboard{public ushort Key,Scan;public uint Flags,Time;public nuint Extra;}
    [StructLayout(LayoutKind.Explicit)] struct Union{[FieldOffset(0)] public Mouse Mouse;[FieldOffset(0)] public Keyboard Keyboard;}
    [StructLayout(LayoutKind.Sequential)] struct Packet{public uint Type;public Union Value;}
    [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,Packet[] packets,int size);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    internal void Check(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(process.HasExited || !GameWindow.CheckInput(identity,process.Id,!process.HasExited).Allowed)
            throw new OperationCanceledException("Client login stopped: window ownership, focus or process changed.");
        if(!NavigationOverlay.TryGetClientScreenBounds((nint)identity.Handle,out var bounds) || bounds.Size!=FrameSize || !SystemInformation.VirtualScreen.Contains(bounds))
            throw new InvalidOperationException("Client window size/visibility changed. Recalibrate login setup.");
    }
    internal Bitmap Capture(CancellationToken token)
    {
        Check(token);NavigationOverlay.TryGetClientScreenBounds((nint)identity.Handle,out var bounds);
        var frame=new Bitmap(bounds.Width,bounds.Height);try{using var g=Graphics.FromImage(frame);g.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);return frame;}catch{frame.Dispose();throw;}
    }
    internal bool Recognize(LoginStep step,CancellationToken token){using var frame=Capture(token);return step.Matches(frame);}
    void Send(Packet packet,CancellationToken token){Check(token);if(SendInput(1,[packet],Marshal.SizeOf<Packet>())!=1)throw new InvalidOperationException("Windows rejected client login input.");}
    void Key(ushort key,bool up,CancellationToken token)=>Send(new(){Type=1,Value=new(){Keyboard=new(){Key=key,Flags=up?2u:0}}},token);
    void Release(Packet packet){SendInput(1,[packet],Marshal.SizeOf<Packet>());}
    internal async Task Act(LoginStep step,CancellationToken token)
    {
        if(launcher){profile.VerifyLauncher();if(step.Password)throw new InvalidOperationException("Passwords are only entered in the game client.");}else profile.VerifyFile();
        if(!string.Equals(process.MainModule?.FileName,Executable,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Login process executable identity changed.");
        Check(token);NavigationOverlay.TryGetClientScreenBounds((nint)identity.Handle,out var bounds);
        var point=new Point(bounds.X+step.Point.X,bounds.Y+step.Point.Y);
        if(!SetCursorPos(point.X,point.Y))throw new InvalidOperationException("Windows rejected login pointer positioning.");
        await Task.Delay(80,token);Check(token);
        if(Input.Cursor()!=point || !Recognize(step,token))throw new InvalidOperationException("Login screen/button changed before clicking.");
        bool down=false;
        try{Send(new(){Value=new(){Mouse=new(){Flags=2}}},token);down=true;await Task.Delay(60,token);}
        finally{if(down)Release(new(){Value=new(){Mouse=new(){Flags=4}}});}
        if(!step.Password)return;
        await Task.Delay(100,token);Check(token);
        bool control=false,a=false;
        try{Key(0x11,false,token);control=true;Key(0x41,false,token);a=true;}
        finally{if(a)Release(new(){Type=1,Value=new(){Keyboard=new(){Key=0x41,Flags=2}}});if(control)Release(new(){Type=1,Value=new(){Keyboard=new(){Key=0x11,Flags=2}}});}
        var secret=LoginSecret.Open(profile.ProtectedPassword);
        try
        {
            for(int i=0;i<secret.Length;i+=2)
            {
                ushort code=BitConverter.ToUInt16(secret,i);bool sent=false;
                try{Send(new(){Type=1,Value=new(){Keyboard=new(){Scan=code,Flags=4}}},token);sent=true;}
                finally{if(sent)Release(new(){Type=1,Value=new(){Keyboard=new(){Scan=code,Flags=6}}});}
            }
        }
        finally{CryptographicOperations.ZeroMemory(secret);}
    }
}
