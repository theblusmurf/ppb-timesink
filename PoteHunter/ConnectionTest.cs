using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using PoteMemoryProbe;

namespace PoteHunter;

internal static class ConnectionTest
{
    public static int Run(string[] args)
    {
        int seconds=60;
        int duration=Array.IndexOf(args,"--connection-seconds");
        if(duration>=0 && (duration+1>=args.Length || !int.TryParse(args[duration+1],out seconds) || seconds is <1 or >600)) return 2;
        string root=AppContext.BaseDirectory;
        void Save(string file,object value)
        {
            string path=Path.Combine(root,file);
            File.WriteAllText(path+".tmp",JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
            File.Move(path+".tmp",path,true);
        }
        var world=new World();
        var started=DateTime.UtcNow;
        var timer=Stopwatch.StartNew();
        string apartment=Thread.CurrentThread.GetApartmentState().ToString();
        var work=Task.Run(()=>
        {
            try
            {
                nint pointer=Marshal.AllocHGlobal(4);
                try
                {
                    Marshal.WriteInt32(pointer,0x12345678);
                    using var self=Native.Open(Environment.ProcessId);
                    if(Native.VirtualQueryEx(self,pointer,out var region,(nuint)Marshal.SizeOf<Native.Region>())==0 || !Native.Readable(region) ||
                        (nuint)pointer<(nuint)region.BaseAddress || (nuint)pointer+4>(nuint)region.BaseAddress+region.RegionSize)
                        throw new Exception("Memory query control failed for the current process architecture.");
                    if(BitConverter.ToInt32(Native.Read(self,pointer,4))!=0x12345678) throw new Exception("Memory reader control value is incorrect.");
                }
                finally {Marshal.FreeHGlobal(pointer);}
                world.Connect();
                if(world.CameraSupported)
                {
                    var camera=world.ReadCamera();
                    Save("camera-read-check.json",new{Passed=true,world.CameraStatus,
                        Eye=new{camera.Position.X,camera.Position.Y,camera.Position.Z},
                        Forward=new{camera.Forward.X,camera.Forward.Y,camera.Forward.Z},
                        Up=new{camera.Up.X,camera.Up.Y,camera.Up.Z},camera.VerticalFovRadians,camera.AspectRatio,HardwareInputEmitted=false});
                }
                else Save("camera-read-check.json",new{Passed=false,world.CameraStatus,HardwareInputEmitted=false});
                var mana=world.ReadMana();
                Save("mana-read-check.json",new{Passed=mana.Known,world.ManaSupported,world.ManaStatus,Mana=mana,HardwareInputEmitted=false});
                if(args.Contains("--check-input-backend"))
                {
                    WindowsClientInput.Enabled=true;
                    WindowsClientInput.Bind(world);
                    WindowsClientInput.ValidateReady();
                    Save("input-backend-check.json",new {Passed=true,world.ClientHash,world.ConnectionVerified,HardwareInputEmitted=false});
                }
                var bar=world.Hotbar();
                var effects=world.ActiveEffects();
                Save("connection-features.json",new {TimeUtc=DateTime.UtcNow,world.ClientHash,
                    Character=world.LocalPlayer().Name,world.RestSupported,Posture=world.RestState().Posture.ToString(),
                    world.PartySupported,world.ActiveEffectsSupported,EffectsAvailable=effects.Available,
                    HotbarPage=bar.Page,Slots=bar.Slots.Select(slot=>new {slot.Key,Kind=slot.Kind.ToString(),slot.Id,slot.Name,slot.Ready}).ToArray()});
                using var observations=new StreamWriter(Path.Combine(root,"connection-samples.jsonl"),false){AutoFlush=true};
                int samples=0;
                double samplingDeadline=Math.Max(0.1,seconds-1);
                while(timer.Elapsed.TotalSeconds<samplingDeadline)
                {
                    var player=world.LocalPlayer();
                    var health=world.TargetHealth(player.Id);
                    if(string.IsNullOrWhiteSpace(player.Name) || !health.Known) throw new InvalidOperationException("Character or health is unreadable.");
                    observations.WriteLine(JsonSerializer.Serialize(new {TimeUtc=DateTime.UtcNow,ReaderPid=Environment.ProcessId,world.Pid,Player=player.Name,Health=health,player.Position,Level=world.PlayerLevel(),MemoryBackend=WindowsClientRead.Backend}));
                    samples++;
                    int remaining=(int)Math.Ceiling((samplingDeadline-timer.Elapsed.TotalSeconds)*1000);
                    if(remaining>0)Thread.Sleep(Math.Min(1000,remaining));
                }
                if(samples==0)throw new TimeoutException("Connection did not leave time for a character sample.");
                return samples;
            }
            finally {world.Dispose();}
        });
        while(!work.IsCompleted && timer.Elapsed<TimeSpan.FromSeconds(seconds))
        {
            Save("connection-progress.json",new {TimeUtc=DateTime.UtcNow,ReaderPid=Environment.ProcessId,Stage=world.ConnectionStage,StartedUtc=started});
            Thread.Sleep(500);
        }
        try
        {
            if(!work.IsCompleted) throw new TimeoutException("Connection test exceeded its deadline at stage: "+world.ConnectionStage);
            int samples=work.GetAwaiter().GetResult();
            Save("connection-result.json",new {Passed=true,ReaderPid=Environment.ProcessId,StartedUtc=started,FinishedUtc=DateTime.UtcNow,Architecture=RuntimeInformation.ProcessArchitecture.ToString(),Apartment=apartment,Seconds=seconds,ElapsedSeconds=timer.Elapsed.TotalSeconds,Samples=samples,MemoryBackend=WindowsClientRead.Backend});
            return 0;
        }
        catch(Exception ex)
        {
            Save("connection-result.json",new {Passed=false,ReaderPid=Environment.ProcessId,StartedUtc=started,FinishedUtc=DateTime.UtcNow,Architecture=RuntimeInformation.ProcessArchitecture.ToString(),Apartment=apartment,Stage=world.ConnectionStage,Error=ex.ToString()});
            return 1;
        }
    }
}
