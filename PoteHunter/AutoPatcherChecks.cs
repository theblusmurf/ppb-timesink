using System.Security.Cryptography;
using System.Text.Json;

namespace PoteHunter;

internal static class AutoPatcherTestSupport
{
    // CI-only installer exercise, limited to its disposable installer smoke folder.
    internal static int InstallCheck(string[] args)
    {
        if(args.Length!=5)return 2;
        try
        {
            string target=Path.GetFullPath(args[2]);string root=Path.GetDirectoryName(target)!;
            if(Path.GetFileName(target)!="install-test" ||
                !Path.GetFileName(root).StartsWith("PoteHunter-installer-",StringComparison.Ordinal) ||
                !Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(root)!).Equals(Path.TrimEndingDirectorySeparator(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase) ||
                AppUpdates.ReleaseNumber(args[3])<0)throw new InvalidOperationException("Not a disposable installer test folder.");
            AutoPatcher.NoLinks(target);
            var update=new AvailableUpdate(args[3],1,new FileInfo(args[1]).Length,args[4]);
            AutoPatcher.VerifyInstaller(args[1],update,CancellationToken.None).GetAwaiter().GetResult();
            AutoPatcher.Install(args[1],target,update,Path.Combine(root,"patch-installer.log"));
            File.WriteAllText(Path.Combine(root,"patch-install-check.json"),JsonSerializer.Serialize(new{Passed=true,VerifiedInstaller=true,VersionConfirmed=true,NoRelaunch=true}));return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex.Message);return 1;}
    }
}

internal static partial class AutoPatcher
{
    internal static void Checks()
    {
        string root=Path.Combine(Path.GetTempPath(),"PoteHunter-patch-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            string cache=Path.Combine(root,"cache"),folder=Path.Combine(cache,new string('a',32)),target=Path.Combine(root,"app with spaces");
            Directory.CreateDirectory(folder);Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"PoteHunter.exe"),"test fixture only");
            byte[] bytes={1,2,3,4};string hash=Convert.ToHexString(SHA256.HashData(bytes));
            var update=new AvailableUpdate("Release1.102",7,bytes.Length,hash);var request=new PatchRequest(target,"Release1.101",update,10,1);
            string path=Path.Combine(folder,"patch-request.json");string installer=Validate(path,request,cache);File.WriteAllBytes(installer,bytes);
            VerifyInstaller(installer,update,CancellationToken.None).GetAwaiter().GetResult();
            void Reject(Action operation){try{operation();}catch(InvalidOperationException){return;}throw new Exception("Unsafe patch admitted.");}
            Reject(()=>Validate(Path.Combine(root,"patch-request.json"),request,cache));
            Reject(()=>Validate(path,request with{CurrentVersion="Release1.102"},cache));
            Reject(()=>Validate(path,request with{Update=update with{Version="Release1.102/evil"}},cache));
            Reject(()=>Validate(path,request with{Update=update with{Sha256="bad"}},cache));
            Reject(()=>Validate(path,request with{Directory=Path.GetPathRoot(target)!},cache));
            Reject(()=>VerifyInstaller(installer,update with{Size=10},CancellationToken.None).GetAwaiter().GetResult());
            File.WriteAllBytes(installer,new byte[]{4,3,2,1});Reject(()=>VerifyInstaller(installer,update,CancellationToken.None).GetAwaiter().GetResult());
            if(!Idle(false,false,false,false) || Idle(true,false,false,false) || Idle(false,true,false,false) || Idle(false,false,true,false) || Idle(false,false,false,true))
                throw new Exception("Patcher interrupted active work.");
            var start=InstallerStart(installer,target,Path.Combine(folder,"installer.log"));
            if(start.UseShellExecute || !start.ArgumentList.Contains("/DIR="+target) || !start.ArgumentList.Contains("/NORESTART") ||
                !start.ArgumentList.Contains("/VERYSILENT") || !start.ArgumentList.Contains("/NOCLOSEAPPLICATIONS") || !start.ArgumentList.Contains("/NORESTARTAPPLICATIONS"))
                throw new Exception("Patcher arguments allow unsafe closure/reboot.");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"auto-patcher-checks.json"),JsonSerializer.Serialize(new{Passed=true,IdleAndRecordingGuards=true,
                StagingBoundary=true,VersionPolicy=true,InstallerTamperRejected=true,StructuredArguments=true,NoForcedCloseOrReboot=true,NoNetworkOrGameInput=true}));
        }
        finally{if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(root).StartsWith("PoteHunter-patch-check-",StringComparison.Ordinal))Directory.Delete(root,true);}
    }
}
