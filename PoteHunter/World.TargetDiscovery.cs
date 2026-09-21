using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PoteMemoryProbe;

namespace PoteHunter;

public readonly record struct TargetStateWord(long Address,uint Id);
internal sealed record TargetStateLayout(string ClientHash,uint[] ModuleRvas);
internal sealed record TargetStateSnapshot(bool Available,string Status,IReadOnlyList<uint> Ids)
{
    public bool Matches(uint id) => Available&&Ids.Count>0&&Ids.All(value=>value==id);
}
internal sealed record InternalTargetSelection(bool Applied,string Status,uint TargetId,IReadOnlyList<uint> TargetIds)
{
    public static InternalTargetSelection Rejected(string status,uint targetId=0)=>new(false,status,targetId,[]);
}
internal sealed record InternalTargetCandidateSelection(bool Applied,string Status,uint TargetId,IReadOnlyDictionary<string,uint> Values)
{
    public static InternalTargetCandidateSelection Rejected(string status,uint targetId=0)=>new(false,status,targetId,new Dictionary<string,uint>());
}

public sealed partial class World
{
    IReadOnlyList<(long Start,int Length)>? targetDiscoveryRanges;
    TargetStateLayout? targetStateLayout;
    string? targetStateFailure;
    internal long ModuleBase => moduleBase;

    internal TargetStateSnapshot TargetState()
    {
        if(targetStateLayout==null&&targetStateFailure==null)
        {
            try {targetStateLayout=TargetStateDiscovery.LoadLayout(ClientHash);}
            catch(Exception ex) {targetStateFailure=ex.Message;}
        }
        if(targetStateLayout==null)return new(false,targetStateFailure??"Run Stage 1 target capture before target verification.",[]);
        try
        {
            uint[] first=ReadTargetState(targetStateLayout),second=ReadTargetState(targetStateLayout);
            if(!first.SequenceEqual(second))return new(false,"Client target state is updating; wait for a stable target.",[]);
            return new(true,"Current-client target fields verified from Stage 1 capture.",first);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new(false,"Client target state is unavailable: "+ex.Message,[]);
        }
    }

    // Experimental no-fire probe. This writes only the Stage-1-verified target
    // mirrors, then confirms the client still exposes the requested identity.
    // It never sends an input packet or starts an attack.
    internal InternalTargetSelection TrySelectTargetInternally(Entity target)
    {
        if(!ConnectionVerified||handle==null||handle.IsInvalid||moduleBase==0)
            return InternalTargetSelection.Rejected("Connect to a verified client before internal target selection.",target.Id);
        if(target.Id==0||target.Address<0x10000)return InternalTargetSelection.Rejected("The selected target has no valid live identity.",target.Id);
        var current=Find(target.Id);
        if(current==null||current.Generation!=target.Generation||current.Address!=target.Address)
            return InternalTargetSelection.Rejected("The selected target changed before internal selection.",target.Id);
        var state=TargetState();
        if(!state.Available||targetStateLayout==null)
            return InternalTargetSelection.Rejected("Internal target selection is unavailable: "+state.Status,target.Id);
        try
        {
            using var writer=Native.OpenForTargetSelection(Pid);
            if(!Native.PathOf(writer).Equals(PoteMemoryProbe.Program.ClientPath,StringComparison.OrdinalIgnoreCase))
                return InternalTargetSelection.Rejected("The writable process identity did not match the verified client.",target.Id);
            byte[] bytes=BitConverter.GetBytes(target.Id);
            foreach(uint rva in targetStateLayout.ModuleRvas)Native.Write(writer,(nint)(moduleBase+rva),bytes);
            var verified=TargetState();
            return verified.Matches(target.Id)
                ?new(true,"Verified target-state mirrors now select the requested ID.",target.Id,verified.Ids)
                :InternalTargetSelection.Rejected("Client target-state mirrors did not retain the requested ID.",target.Id);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return InternalTargetSelection.Rejected("Internal target selection failed: "+ex.Message,target.Id);
        }
    }

    internal InternalTargetCandidateSelection TrySelectTargetWithCandidates(Entity target,IReadOnlyList<long> candidates)
    {
        if(candidates.Count==0)return InternalTargetCandidateSelection.Rejected("No internal target candidates were supplied.",target.Id);
        if(!ConnectionVerified||handle==null||handle.IsInvalid||moduleBase==0)
            return InternalTargetCandidateSelection.Rejected("Connect to a verified client before internal target selection.",target.Id);
        var current=Find(target.Id);
        if(current==null||current.Generation!=target.Generation||current.Address!=target.Address)
            return InternalTargetCandidateSelection.Rejected("The selected target changed before internal selection.",target.Id);
        var state=TargetState();
        if(!state.Available||targetStateLayout==null)
            return InternalTargetCandidateSelection.Rejected("Internal target selection is unavailable: "+state.Status,target.Id);
        try
        {
            foreach(long address in candidates)
            {
                if(VirtualQueryEx(handle,(nint)address,out var memory,(nuint)Marshal.SizeOf<MemoryBasicInformation>())==0 ||
                    memory.State!=MemCommit||!Writable(memory.Protect))
                    return InternalTargetCandidateSelection.Rejected($"Candidate address 0x{address:X8} is not committed writable client memory.",target.Id);
            }
            using var writer=Native.OpenForTargetSelection(Pid);
            if(!Native.PathOf(writer).Equals(PoteMemoryProbe.Program.ClientPath,StringComparison.OrdinalIgnoreCase))
                return InternalTargetCandidateSelection.Rejected("The writable process identity did not match the verified client.",target.Id);
            byte[] bytes=BitConverter.GetBytes(target.Id);
            foreach(uint rva in targetStateLayout.ModuleRvas)Native.Write(writer,(nint)(moduleBase+rva),bytes);
            foreach(long address in candidates)Native.Write(writer,(nint)address,bytes);
            var values=candidates.ToDictionary(address=>$"0x{address:X8}",address=>ReadTargetStateWord(address));
            bool applied=TargetState().Matches(target.Id)&&values.Values.All(value=>value==target.Id);
            return new(applied,applied?"Candidate target fields retained the requested ID.":"One or more candidate target fields did not retain the requested ID.",target.Id,values);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return InternalTargetCandidateSelection.Rejected("Candidate target selection failed: "+ex.Message,target.Id);
        }
    }

    uint[] ReadTargetState(TargetStateLayout layout)
    {
        if(handle==null||handle.IsInvalid||moduleBase==0)throw new InvalidOperationException("Client is disconnected.");
        return layout.ModuleRvas.Select(rva=>BitConverter.ToUInt32(Native.Read(handle,(nint)(moduleBase+rva),4))).ToArray();
    }

    internal IReadOnlyDictionary<string,uint> TargetStateNeighborhood()
    {
        var state=TargetState();
        if(!state.Available||targetStateLayout==null||targetStateLayout.ModuleRvas.Length==0)
            return new Dictionary<string,uint>();
        uint first=targetStateLayout.ModuleRvas.Min();
        return Enumerable.Range(0,4).ToDictionary(offset=>$"0x{first+(uint)(offset*4):X6}",offset=>
            BitConverter.ToUInt32(Native.Read(handle!,(nint)(moduleBase+first+(uint)(offset*4)),4)));
    }

    internal IReadOnlyList<TargetStateWord> FindTargetStateWords(IReadOnlySet<uint> targetIds)
    {
        ArgumentNullException.ThrowIfNull(targetIds);
        if(handle==null||handle.IsInvalid||moduleBase==0)throw new InvalidOperationException("Connect to a validated client before capturing target state.");
        var result=new List<TargetStateWord>();
        foreach(var range in WritableModuleRanges())
        {
            for(int readOffset=0;readOffset<range.Length;readOffset+=1024*1024)
            {
                int length=Math.Min(1024*1024,range.Length-readOffset);byte[] bytes;
                try {bytes=Native.Read(handle,(nint)(range.Start+readOffset),length);}
                catch(System.ComponentModel.Win32Exception) {continue;}
                for(int offset=0;offset<=bytes.Length-4;offset+=4)
                {
                    uint id=BitConverter.ToUInt32(bytes,offset);
                    if(targetIds.Contains(id))result.Add(new(range.Start+readOffset+offset,id));
                }
            }
        }
        return result;
    }

    internal IReadOnlyList<TargetStateWord> FindProcessTargetStateWords(IReadOnlySet<uint> targetIds)
    {
        ArgumentNullException.ThrowIfNull(targetIds);
        if(handle==null||handle.IsInvalid)throw new InvalidOperationException("Connect to a validated client before capturing target state.");
        var result=new List<TargetStateWord>();
        foreach(var range in WritableProcessRanges())
        {
            for(int readOffset=0;readOffset<range.Length;readOffset+=1024*1024)
            {
                int length=Math.Min(1024*1024,range.Length-readOffset);byte[] bytes;
                try {bytes=Native.Read(handle,(nint)(range.Start+readOffset),length);}
                catch(System.ComponentModel.Win32Exception) {continue;}
                for(int offset=0;offset<=bytes.Length-4;offset+=4)
                {
                    uint id=BitConverter.ToUInt32(bytes,offset);
                    if(targetIds.Contains(id))result.Add(new(range.Start+readOffset+offset,id));
                }
            }
        }
        return result;
    }

    internal uint ReadTargetStateWord(long address) => BitConverter.ToUInt32(Native.Read(handle!,(nint)address,4));

    IReadOnlyList<(long Start,int Length)> WritableModuleRanges()
    {
        if(targetDiscoveryRanges!=null)return targetDiscoveryRanges;
        byte[] image=File.ReadAllBytes(PoteMemoryProbe.Program.ClientPath);
        if(image.Length<0x100||BitConverter.ToUInt16(image,0)!=0x5a4d)throw new InvalidOperationException("Target discovery requires a valid client executable.");
        int pe=checked((int)BitConverter.ToUInt32(image,0x3c));
        if(pe<0||pe>image.Length-24||BitConverter.ToUInt32(image,pe)!=0x4550)throw new InvalidOperationException("Target discovery could not read the client PE header.");
        int sections=BitConverter.ToUInt16(image,pe+6),optional=BitConverter.ToUInt16(image,pe+20),table=pe+24+optional;
        var ranges=new List<(long Start,int Length)>();
        for(int index=0;index<sections;index++)
        {
            int entry=table+index*40;
            if(entry<0||entry>image.Length-40)throw new InvalidOperationException("Target discovery found a truncated section table.");
            uint virtualSize=BitConverter.ToUInt32(image,entry+8),rva=BitConverter.ToUInt32(image,entry+12),rawSize=BitConverter.ToUInt32(image,entry+16),flags=BitConverter.ToUInt32(image,entry+36);
            if((flags&0x80000000)==0)continue;
            ulong size=Math.Max(virtualSize,rawSize);
            if(size==0||rva>uint.MaxValue-size)continue;
            long first=moduleBase+rva,last=first+checked((long)size);
            for(long address=first;address<last;)
            {
                if(VirtualQueryEx(handle!,(nint)address,out var memory,(nuint)Marshal.SizeOf<MemoryBasicInformation>())==0)break;
                long regionStart=memory.BaseAddress.ToInt64(),regionEnd=checked(regionStart+(long)memory.RegionSize);
                long next=Math.Max(address+0x1000,regionEnd);
                if(memory.State==MemCommit&&Writable(memory.Protect))
                {
                    long start=Math.Max(first,regionStart),end=Math.Min(last,regionEnd);
                    if(end>start&&end-start<=64L*1024*1024)ranges.Add((start,checked((int)(end-start))));
                }
                address=next;
            }
        }
        if(ranges.Count==0)throw new InvalidOperationException("Target discovery found no writable client sections.");
        return targetDiscoveryRanges=ranges;
    }

    IReadOnlyList<(long Start,int Length)> WritableProcessRanges()
    {
        if(handle==null||handle.IsInvalid)throw new InvalidOperationException("Client is disconnected.");
        var ranges=new List<(long Start,int Length)>();
        const long limit=0x80000000;
        for(long address=0x10000;address<limit;)
        {
            if(VirtualQueryEx(handle,(nint)address,out var memory,(nuint)Marshal.SizeOf<MemoryBasicInformation>())==0)break;
            long regionStart=memory.BaseAddress.ToInt64(),regionEnd=checked(regionStart+(long)memory.RegionSize);
            long next=Math.Max(address+0x1000,regionEnd);
            if(memory.State==MemCommit&&Writable(memory.Protect)&&memory.RegionSize is >0 and <=64*1024*1024)
                ranges.Add((regionStart,checked((int)memory.RegionSize)));
            address=next;
        }
        if(ranges.Count==0)throw new InvalidOperationException("Target discovery found no writable client memory.");
        return ranges;
    }

    const uint MemCommit=0x1000;
    static bool Writable(uint protect) => (protect&0xff) is 0x04 or 0x08 or 0x40 or 0x80;
    [StructLayout(LayoutKind.Sequential)]
    struct MemoryBasicInformation
    {
        public nint BaseAddress,AllocationBase;
        public uint AllocationProtect;
        public nuint RegionSize;
        public uint State,Protect,Type;
    }
    [DllImport("kernel32.dll",SetLastError=true)]
    static extern nuint VirtualQueryEx(SafeProcessHandle process,nint address,out MemoryBasicInformation information,nuint length);
}

internal static class TargetStateDiscovery
{
    internal static TargetStateLayout LoadLayout(string clientHash)
    {
        string path=Path.Combine(AppContext.BaseDirectory,"target-state-capture-report.json");
        if(!File.Exists(path))throw new InvalidOperationException("Run Stage 1 target capture before target verification.");
        return ParseLayout(File.ReadAllText(path),clientHash);
    }

    internal static TargetStateLayout ParseLayout(string json,string clientHash)
    {
        using var document=JsonDocument.Parse(json);var root=document.RootElement;
        if(!root.TryGetProperty("Passed",out var passed)||!passed.GetBoolean()||
            !root.TryGetProperty("HardwareInputEmitted",out var emitted)||emitted.GetBoolean())
            throw new InvalidOperationException("Stage 1 target capture was not a valid read-only report.");
        if(!root.TryGetProperty("ClientHash",out var hash)||!hash.GetString()!.Equals(clientHash,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Stage 1 target capture belongs to a different client build. Capture again after reconnecting.");
        if(!root.TryGetProperty("Fields",out var fields)||fields.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("Stage 1 report has no target fields.");
        var rvas=new List<uint>();
        foreach(var field in fields.EnumerateArray())
        {
            if(!field.TryGetProperty("Transitions",out var transitions)||transitions.GetInt32()<2||
                !field.TryGetProperty("TargetIds",out var ids)||ids.GetArrayLength()<2||
                !field.TryGetProperty("ModuleRva",out var rvaText)||!TryHex(rvaText.GetString(),out uint rva))continue;
            rvas.Add(rva);
        }
        if(rvas.Count<2)throw new InvalidOperationException("Stage 1 capture did not prove two changing target fields. Capture two distinct manual targets again.");
        return new(clientHash,rvas.Distinct().OrderBy(value=>value).ToArray());
    }

    static bool TryHex(string? text,out uint value)
    {
        string digits=text?.StartsWith("0x",StringComparison.OrdinalIgnoreCase)==true?text[2..]:text??"";
        return uint.TryParse(digits,System.Globalization.NumberStyles.AllowHexSpecifier,System.Globalization.CultureInfo.InvariantCulture,out value);
    }
    internal static int RunCommand()
    {
        WindowsClientRead.Enabled=true;
        try
        {
            using var world=new World();world.Connect();
            var entities=world.Poll();
            _=Capture(world,entities,TimeSpan.FromSeconds(30),CancellationToken.None).GetAwaiter().GetResult();
            return 0;
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"target-state-capture-error.txt"),ex.ToString());
            return 1;
        }
    }

    internal static int RunProcessWideCommand()
    {
        WindowsClientRead.Enabled=true;
        try
        {
            using var world=new World();world.Connect();
            _=Capture(world,world.Poll(),TimeSpan.FromSeconds(30),CancellationToken.None,processWide:true).GetAwaiter().GetResult();
            return 0;
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"internal-target-state-capture-error.txt"),ex.ToString());
            return 1;
        }
    }

    internal static int ObserveProcessWideCommand()
    {
        WindowsClientRead.Enabled=true;
        try
        {
            using var world=new World();world.Connect();
            _=Capture(world,world.Poll(),TimeSpan.FromMinutes(10),CancellationToken.None,processWide:true,sampleMilliseconds:1000,observation:true).GetAwaiter().GetResult();
            return 0;
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"internal-target-state-observation-error.txt"),ex.ToString());
            return 1;
        }
    }

    internal static int VerifyCommand()
    {
        WindowsClientRead.Enabled=true;
        try
        {
            using var world=new World();world.Connect();var state=world.TargetState();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"target-state-verification.json"),JsonSerializer.Serialize(new
            {
                Passed=state.Available,HardwareInputEmitted=false,world.ClientHash,world.Pid,Zone=world.ActiveZone(),state.Status,
                TargetIds=state.Ids.Select(id=>$"0x{id:X8}").ToArray()
            },new JsonSerializerOptions {WriteIndented=true}));
            return state.Available?0:1;
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"target-state-verification.json"),JsonSerializer.Serialize(new {Passed=false,HardwareInputEmitted=false,Error=ex.ToString()}));
            return 1;
        }
    }

    internal static async Task<CaptureResult> Capture(World world,IReadOnlyList<Entity> initialEntities,TimeSpan duration,CancellationToken token,bool processWide=false,int sampleMilliseconds=100,bool observation=false)
    {
        ArgumentNullException.ThrowIfNull(world);ArgumentNullException.ThrowIfNull(initialEntities);
        if(duration is {TotalMilliseconds:<1000} or {TotalMinutes:>15})throw new ArgumentOutOfRangeException(nameof(duration));
        if(sampleMilliseconds is <50 or >2000)throw new ArgumentOutOfRangeException(nameof(sampleMilliseconds));
        var candidates=initialEntities.Where(entity=>entity.Monster&&entity.Position.Finite).GroupBy(entity=>entity.Id)
            .Where(group=>group.Count()==1).Select(group=>group.Single()).ToDictionary(entity=>entity.Id);
        if(candidates.Count<2)throw new InvalidOperationException("Target capture needs at least two distinct loaded monsters.");
        string stem=observation?"internal-target-state-observation":processWide?"internal-target-state":"target-state";
        var tracked=processWide?world.FindProcessTargetStateWords(candidates.Keys.ToHashSet()):null;
        string path=Path.Combine(AppContext.BaseDirectory,stem+"-capture.jsonl");
        long started=Environment.TickCount64,deadline=started+(long)duration.TotalMilliseconds;
        var history=new Dictionary<long,TargetHistory>();int snapshots=0;
        var previousValues=new Dictionary<long,uint>();
        await using var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.Read);
        await using var writer=new StreamWriter(stream);
        while(Environment.TickCount64<deadline)
        {
            token.ThrowIfCancellationRequested();
            var matches=tracked==null?world.FindTargetStateWords(candidates.Keys.ToHashSet()):tracked.Select(word=>
            {
                try{return new TargetStateWord(word.Address,world.ReadTargetStateWord(word.Address));}
                catch(System.ComponentModel.Win32Exception){return default;}
            }).Where(word=>word.Address!=0&&candidates.ContainsKey(word.Id)).ToArray();
            var health=candidates.Values.Select(entity=>new {entity.Id,HP=world.TargetHealth(entity.Id)}).ToArray();
            var words=matches.Where(match=>!observation||!previousValues.TryGetValue(match.Address,out uint previous)||previous!=match.Id).Select(match=>
            {
                var entity=candidates[match.Id];
                if(!history.TryGetValue(match.Address,out var seen))history[match.Address]=seen=new(match.Address);
                seen.Observe(match.Id,Environment.TickCount64);
                previousValues[match.Address]=match.Id;
                return new {WordAddress=$"0x{match.Address:X8}",Id=$"0x{match.Id:X8}",entity.DisplayName,entity.Generation,CreatureAddress=entity.Address};
            }).ToArray();
            await writer.WriteLineAsync(JsonSerializer.Serialize(new {TimeUtc=DateTime.UtcNow,ElapsedMs=Environment.TickCount64-started,
                Zone=world.ActiveZone(),SelectedSkill=world.SelectedSkill(),Words=words,Health=health}));
            await writer.FlushAsync();snapshots++;
            if(observation)await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory,"internal-target-state-observation-status.json"),JsonSerializer.Serialize(new
            {
                Running=true,HardwareInputEmitted=false,TimeUtc=DateTime.UtcNow,ElapsedSeconds=(Environment.TickCount64-started)/1000,
                DurationSeconds=duration.TotalSeconds,Snapshots=snapshots,InitialCandidateWords=tracked?.Count??0,ChangedWords=words.Length
            },new JsonSerializerOptions {WriteIndented=true}),token);
            await Task.Delay(sampleMilliseconds,token);
        }
        var fields=history.Values.OrderByDescending(item=>item.Samples).ThenBy(item=>item.Address).Select(item=>new
        {
            Address=$"0x{item.Address:X8}",item.Samples,item.FirstElapsedMs,item.LastElapsedMs,
            ModuleRva=$"0x{item.Address-world.ModuleBase:X}",item.Transitions,
            TargetIds=item.TargetIds.Select(id=>new {Id=$"0x{id:X8}",Monster=candidates.TryGetValue(id,out var entity)?entity.DisplayName:"not present in capture"}).ToArray()
        }).ToArray();
        string report=Path.Combine(AppContext.BaseDirectory,stem+"-capture-report.json");
        await File.WriteAllTextAsync(report,JsonSerializer.Serialize(new
        {
            Passed=true,HardwareInputEmitted=false,ClientHash=world.ClientHash,Zone=world.ActiveZone(),DurationSeconds=duration.TotalSeconds,
            Snapshots=snapshots,ProcessWide=processWide,Observation=observation,SampleMilliseconds=sampleMilliseconds,InitialCandidateWords=tracked?.Count??0,CandidateMonsters=candidates.Values.Select(entity=>new {entity.Id,entity.DisplayName,entity.Generation,entity.Address,entity.Position}).ToArray(),Fields=fields,
            Note="Read-only candidate capture. A field is not authoritative until separate manual target and damage correlations prove it.",Capture=Path.GetFileName(path)
        },new JsonSerializerOptions {WriteIndented=true}),token);
        if(observation)await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory,"internal-target-state-observation-status.json"),JsonSerializer.Serialize(new
        {
            Running=false,Completed=true,HardwareInputEmitted=false,TimeUtc=DateTime.UtcNow,DurationSeconds=duration.TotalSeconds,Snapshots=snapshots,
            InitialCandidateWords=tracked?.Count??0,Report=Path.GetFileName(report)
        },new JsonSerializerOptions {WriteIndented=true}),token);
        return new(path,report,snapshots,fields.Length);
    }

    sealed class TargetHistory(long address)
    {
        public long Address {get;}=address;
        public int Samples {get;private set;}
        public long FirstElapsedMs {get;private set;}=-1;
        public long LastElapsedMs {get;private set;}
        public int Transitions {get;private set;}
        public HashSet<uint> TargetIds {get;}=[];
        uint? previous;
        public void Observe(uint id,long elapsed)
        {
            if(FirstElapsedMs<0)FirstElapsedMs=elapsed;
            if(previous.HasValue&&previous.Value!=id)Transitions++;
            previous=id;LastElapsedMs=elapsed;Samples++;TargetIds.Add(id);
        }
    }

    internal sealed record CaptureResult(string CapturePath,string ReportPath,int Snapshots,int CandidateFields);
}
