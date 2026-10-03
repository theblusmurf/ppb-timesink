using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using PoteMemoryProbe;

namespace PoteHunter;

public readonly record struct Vec(double X, double Y)
{
    public double Length => Math.Sqrt(X * X + Y * Y);
    public static Vec operator -(Vec a, Vec b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec operator +(Vec a, Vec b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec operator /(Vec a, double d) => new(a.X / d, a.Y / d);
    public static Vec operator *(Vec a, double d) => new(a.X * d, a.Y * d);
    public bool Finite => double.IsFinite(X) && double.IsFinite(Y);
}
public record Entity(long Address, uint Id, string Name, Vec Position, double Height, double Heading = 0, uint Generation = 0, string Model = "")
{
    public bool Monster => (Id & 0xF0000000) == 0x80000000 &&
        (Name.StartsWith("Lv. ", StringComparison.Ordinal) || Targeting.IsKnownUnprefixedMonster(Id, Model));
    public bool PriorityLootObject => Targeting.IsPriorityLootObject(Id, Model);
    public bool Targetable => Monster || PriorityLootObject;
    public string DisplayName => PriorityLootObject ? Targeting.PriorityLabel(Id) : string.IsNullOrWhiteSpace(Name) ? "Unnamed object" : Name;
}
public readonly record struct Health(int Current, uint Maximum)
{
    public bool Known => Maximum > 0;
    public bool Dead => Known && Current <= 0;
}
public record GroundItem(uint KeyA, uint KeyB, int TypeId, string Name, Vec Position, double Height, string Description = "")
{
    public long EncodedGoldAmount => TypeId < 0 ? unchecked((uint)TypeId) & 0x7fffffffu : 0;

    internal static GroundItem FromRecord(ReadOnlySpan<byte> record, string name, string description = "")
    {
        // The high bit of +0x08 marks currency; the remaining bits carry its
        // amount. +0x0c is not initialized by the client's ground-item constructor.
        return new GroundItem(BitConverter.ToUInt32(record), BitConverter.ToUInt32(record[4..]),
            BitConverter.ToInt32(record[8..]), name,
            new Vec(BitConverter.ToSingle(record[0x1c..]) / 100.0, BitConverter.ToSingle(record[0x24..]) / 100.0),
            BitConverter.ToSingle(record[0x20..]) / 100.0, description);
    }
}
public sealed partial class World : IDisposable
{
    BuildProfile profile = BuildProfile.Original;
    ProfileDetection? detection;
    uint CreatureRva => profile.CreatureVtable;
    SafeProcessHandle? handle;
    Process? process;
    long moduleBase;
    List<long> addresses = new();
    readonly Dictionary<uint, MonsterDefinition> definitions = new();
    readonly Dictionary<int, string> itemNames = new();
    readonly Dictionary<uint, uint> healthRecords = new();
    readonly Dictionary<ushort, string> skillNames = new();
    readonly Dictionary<ushort,SkillMetadata> skillMetadata=new();
    readonly Dictionary<int, ItemDetails> itemDetails = new();
    public IntPtr Window { get; private set; }
    GameWindow.Candidate? windowIdentity;
    internal GameWindow.InputState CheckInputWindow() => GameWindow.CheckInput(windowIdentity, Pid, IsProcessAlive(handle));
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
    internal static bool IsProcessAlive(SafeProcessHandle? gameHandle) => gameHandle != null &&
        !gameHandle.IsInvalid && !gameHandle.IsClosed && GetExitCodeProcess(gameHandle, out uint code) && code == 259;
    public int Pid => process?.Id ?? 0;
    public string ClientHash => profile.Sha256;
    internal bool ConnectionVerified { get; private set; }
    public bool RestSupported {get;private set;}
    public bool PartySupported {get;private set;}
    IReadOnlyList<(string Name,string Description)> effectNames=[];
    public bool ActiveEffectsSupported {get;private set;}
    public ActiveEffectSnapshot ActiveEffects()
    {
        if(!ActiveEffectsSupported)return new(false,"Active-effect layout is unavailable for this build",[]);
        try
        {
            var self=LocalPlayer();uint scene=Pointer(moduleBase+profile.Scene);
            var first=Native.Read(handle!,(nint)(scene+profile.Layout.Effects),0x138);
            var second=Native.Read(handle!,(nint)(scene+profile.Layout.Effects),0x138);
            if(scene!=Pointer(moduleBase+profile.Scene) || !LocalCharacter.Same(self,LocalPlayer()) || !first.AsSpan(0,0x9c).SequenceEqual(second.AsSpan(0,0x9c)))
                return new(false,"Active effects are updating",[]);
            return ActiveEffectSnapshot.Decode(second,effectNames);
        }
        catch(System.ComponentModel.Win32Exception){return new(false,"Active-effect read unavailable",[]);}
    }
    public PartySnapshot Party()
    {
        if(!PartySupported)return new(false,[],"Party layout is unavailable for this client build");
        var self=LocalPlayer();uint scene=Pointer(moduleBase+profile.Scene);
        var first=Native.Read(handle!,(nint)scene,0x30d);
        var second=Native.Read(handle!,(nint)(scene+0x240),0xcd);
        if(scene!=Pointer(moduleBase+profile.Scene) || !first.AsSpan(0x240,0xcd).SequenceEqual(second))
            return new(false,[],"Party is updating");
        try{return PartySnapshot.Parse(first,self.Id);}
        catch(InvalidOperationException ex){return new(false,[],ex.Message);}
    }
    public RestReading RestState()
    {
        if(!RestSupported) return new(RestPosture.Unknown);
        var self=LocalPlayer();
        var data=Native.Read(handle!,(nint)self.Address,0x24d);
        if(BitConverter.ToUInt32(data,4)!=self.Id || BitConverter.ToUInt32(data,8)!=self.Generation || data[0x12]!=0)
            throw new InvalidOperationException("Character changed while reading rest state.");
        return RestReading.Decode(data[0x24c],BitConverter.ToUInt32(data,0x1b8));
    }
    public int ActiveZone()
    {
        uint actor=Pointer(moduleBase+profile.LocalActor),zone=Pointer(actor+0x168);
        if(zone is not (>=1 and <=18) && zone!=100) throw new InvalidOperationException("The current map zone is not ready.");
        return (int)zone;
    }
    public string NavigationContext(Entity self) => $"{ClientHash}:{Pid}:Zone{ActiveZone()}:{Pointer(moduleBase+profile.Scene):X8}:{self.Id:X8}:{self.Generation}";
    public bool AutomaticProfile => detection?.Automatic==true;
    public string ProfileStatus { get; private set; }="Not connected";
    public string ConnectionStage { get; private set; }="Not connected";
    public int CandidateCount => addresses.Count;
    public string PlayerName { get; private set; } = "";
    public void Connect()
    {
        Dispose();
        detection=null;
        ConnectionStage="Checking client file";
        try
        {
            detection=ProfileDiscovery.ResolveForConnection(File.ReadAllBytes(PoteMemoryProbe.Program.ClientPath)); profile=detection.Profile;
            ProfileStatus=detection.Automatic ? "Signatures found; checking live layout" : "Known build; checking live layout";
            WriteProfileAudit(false,ProfileStatus);
        }
        catch(Exception ex) { ProfileStatus=ex.Message; WriteProfileAudit(false,ex.Message); throw; }
        ConnectionStage="Selecting client process";
        var games = Process.GetProcessesByName("Client");
        Process? selected = null;
        try
        {
            foreach (var game in games)
            {
                using var identity = Native.OpenProcess(0x1000, false, game.Id);
                if (!identity.IsInvalid && Native.PathOf(identity).Equals(PoteMemoryProbe.Program.ClientPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (selected != null) throw new InvalidOperationException("Keep only one PlayPOTE client open.");
                    selected = game;
                }
            }
            if (selected == null) throw new InvalidOperationException("Launch PlayPOTE and log into the world first.");
            ConnectionStage="Opening client for reading";
            try { handle = Native.Open(selected.Id); }
            catch(System.ComponentModel.Win32Exception ex) when(ex.NativeErrorCode==5)
            {
                throw new InvalidOperationException("Windows denied read-only access to Client.exe. If the game is running as administrator, launch PoteHunter as administrator too; otherwise run both normally.",ex);
            }
            if (!Native.PathOf(handle).Equals(PoteMemoryProbe.Program.ClientPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Client identity changed.");
            process = selected;
            ConnectionStage="Locating client image mapping";
            moduleBase = Native.MainImageBase(handle, PoteMemoryProbe.Program.ClientPath);
            ConnectionStage="Locating game window";
            Window = GameWindow.Find(selected, out var identityWindow);
            windowIdentity = identityWindow;
            ConnectionStage="Reading client image header";
            byte[] header = Native.Read(handle, (nint)moduleBase, 4096);
            if (header[0] != 'M' || header[1] != 'Z') throw new InvalidOperationException("Client module validation failed.");
            int pe = BitConverter.ToInt32(header, 0x3c);
            if (pe < 0x40 || pe > 3000 || BitConverter.ToUInt32(header, pe) != 0x4550 || BitConverter.ToUInt32(header, pe + 24 + 56) != profile.ImageSize || BitConverter.ToUInt32(header,pe+8)!=detection.TimeDateStamp)
                throw new InvalidOperationException("The loaded client differs from the verified on-disk build. Restart the game after its update.");
            ConnectionStage="Validating live signatures";
            if(detection.Automatic) ValidateLoadedSignatures();
            ConnectionStage="Checking supported live data";
            try
            {
                var proofs=ProfileDiscovery.ActiveEffectEvidence(PoteMemoryProbe.Program.ClientPath,profile.Scene,profile.Layout);
                ActiveEffectsSupported=proofs.Count==6 && proofs.All(e=>{
                    var bytes=Native.Read(handle!,(nint)(moduleBase+e.CodeRva),e.Pattern.Split(' ').Length);
                    return ProfileDiscovery.Matches(bytes,e.Pattern) && (!e.CapturedRva.HasValue || BitConverter.ToUInt32(bytes,e.CaptureOffset)==(ulong)moduleBase+e.CapturedRva.Value);
                });
                if(ActiveEffectsSupported)
                {
                    uint table=proofs.Single(e=>e.Name=="Effect labels").CapturedRva!.Value;
                    effectNames=Enumerable.Range(1,77).Select(index=>{
                        var bytes=Native.Read(handle!,(nint)(moduleBase+table+index*1024),1024);
                        int end=Array.IndexOf(bytes,(byte)0);if(end<0)end=bytes.Length;
                        string text=Encoding.UTF8.GetString(bytes,0,end);var parts=text.Split('\\',StringSplitOptions.RemoveEmptyEntries);
                        return (Name:parts.FirstOrDefault()??"",Description:text.Replace("\\\\","\n"));
                    }).ToArray();
                    ActiveEffectsSupported=effectNames[8].Name=="Encourage" && effectNames[11].Name=="Harden Skin" && effectNames[7].Name=="Regeneration";
                }
            }
            catch{ActiveEffectsSupported=false;effectNames=[];}
            var restEvidence=ProfileDiscovery.RestEvidence(PoteMemoryProbe.Program.ClientPath);
            RestSupported=restEvidence!=null && ProfileDiscovery.Matches(
                Native.Read(handle!,(nint)(moduleBase+restEvidence.CodeRva),restEvidence.Pattern.Split(' ').Length),restEvidence.Pattern);
            string[] partyPatterns=[
                "8A 81 44 02 00 00 89 4D F4 88 45 FF 53 56 57 84 C0",
                "81 C1 E5 02 00 00 39 11 74 ?? 40 83 C1 04 3B C6",
                "8B 45 A4 8B 44 85 B8 C1 E0 04 05 45 02 00 00 03 F8 57 E8 ?? ?? ?? ?? 83 C4 04 83 F8 5A 0F 8E ?? ?? ?? ?? 33 C9 33 F6 8A 04 37",
                "8B 81 40 02 00 00 3B 82 64 01 00 00 0F 94 C0 C3"];
            PartySupported=partyPatterns.All(pattern=> {
                if(profile.Layout==SceneLayout.September30)
                    pattern=pattern.Replace("8B 45 A4 8B 44 85 B8 C1 E0 04","8B C6 C1 E0 04",StringComparison.Ordinal)
                        .Replace("33 C9 33 F6 8A 04 37","33 C9 33 F6 90 8A 04 37",StringComparison.Ordinal);
                var evidence=ProfileDiscovery.OptionalEvidence(PoteMemoryProbe.Program.ClientPath,"Party roster",pattern);
                return evidence!=null && ProfileDiscovery.Matches(Native.Read(handle!,(nint)(moduleBase+evidence.CodeRva),pattern.Split(' ').Length),pattern);
            });
            ConnectionStage="Scanning active creatures";
            Rescan();
            ConnectionStage="Validating character layout";
            if(detection.Automatic) ValidateAutomaticLayout();
            ProfileStatus=detection.Automatic ? "Automatic layout verified" : "Known layout verified";
            ConnectionVerified=true;
            ConfigureCamera();
            ConfigureMana();
            ConfigureWallet();
            WriteProfileAudit(true,ProfileStatus);
            ConnectionStage="Connected";
        }
        catch(Exception ex) { ProfileStatus=ex.Message; WriteProfileAudit(false,ex.Message); Dispose(); throw; }
        finally { foreach (var game in games) if (game != process) game.Dispose(); }
    }
    void WriteProfileAudit(bool validated,string status)
    {
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"profile-status.json"),System.Text.Json.JsonSerializer.Serialize(new {TimeUtc=DateTime.UtcNow,ProcessId=Pid,Automatic=detection?.Automatic??false,RuntimeVerified=validated,Status=status,Profile=detection?.Profile,Evidence=detection?.Evidence},new System.Text.Json.JsonSerializerOptions {WriteIndented=true})); }
        catch(IOException) { }
        catch(UnauthorizedAccessException) { }
    }
    void ValidateLoadedSignatures()
    {
        foreach(var evidence in detection!.Evidence)
        {
            int length=evidence.Pattern.Split(' ',StringSplitOptions.RemoveEmptyEntries).Length;
            var live=Native.Read(handle!,(nint)(moduleBase+evidence.CodeRva),length);
            if(!ProfileDiscovery.Matches(live,evidence.Pattern) || evidence.CapturedRva.HasValue &&
                BitConverter.ToUInt32(live,evidence.CaptureOffset)!=(ulong)moduleBase+evidence.CapturedRva.Value)
                throw new InvalidOperationException("The loaded client code does not match the discovered layout: "+evidence.Name+". Restart the client or verify this update.");
        }
    }
    void ValidateAutomaticLayout()
    {
        var self=LocalPlayer(); _=PlayerLevel();
        _=ActiveZone();
        if(!TargetHealth(self.Id).Known) throw new InvalidOperationException("Automatic layout validation needs a logged-in character with readable HP.");
        void Table(long table,int pointerOffset,int countOffset,int stride,int keyOffset,bool shortKey,string name)
        {
            uint records=Pointer(table+pointerOffset),count=Pointer(table+countOffset);
            if(records<0x10000 || count is 0 or >50000 || (ulong)records+count*(ulong)stride>uint.MaxValue)
                throw new InvalidOperationException("Automatic layout validation failed for "+name+" table bounds.");
            uint? previous=null;
            foreach(uint index in new uint[]{0,count/2,count-1}.Distinct())
            {
                var bytes=Native.Read(handle!,(nint)(records+index*(long)stride+keyOffset),shortKey?2:4);
                uint key=shortKey?BitConverter.ToUInt16(bytes):BitConverter.ToUInt32(bytes);
                if(previous.HasValue && key<=previous.Value) throw new InvalidOperationException("Automatic layout validation failed for "+name+" sorted keys.");
                previous=key;
            }
        }
        Table(Pointer(moduleBase+profile.MonsterDefinitions),0,4,0x248,0x15c,false,"monster");
        Table(moduleBase+profile.ItemDefinitions,4,0,0x26c,0,true,"item");
        Table(Pointer(moduleBase+profile.SkillDefinitions),0,4,0xa14,0x1d8,true,"skill");
        foreach(var entity in Poll().Where(e=>e.Monster).Take(5))
        {
            var definition=Definition(entity.Id&0xffff);
            if(definition.Level is <1 or >500 || definition.Category is <0 or >32 || string.IsNullOrWhiteSpace(definition.Model))
                throw new InvalidOperationException("Automatic layout validation failed for creature definitions.");
        }
        var hotbar=Hotbar();
        foreach(var slot in hotbar.Slots)
        {
            if(slot.Kind==SlotKind.Skill && !skillNames.ContainsKey((ushort)slot.Id)) throw new InvalidOperationException("Automatic layout validation failed for a slotted skill.");
            if(slot.Kind==SlotKind.Item && string.IsNullOrWhiteSpace(DescribeItem(slot.Id).Category)) throw new InvalidOperationException("Automatic layout validation failed for a slotted item.");
        }
        uint scene=Pointer(moduleBase+profile.Scene);
        if(Pointer(scene+profile.Layout.GroundItems)<0x10000 || Pointer(scene+profile.Layout.GroundCount)>8192) throw new InvalidOperationException("Automatic layout validation failed for the ground-item list.");
        _=Loot();
    }
    public void Rescan()
    {
        _ = Poll();
        _ = LocalPlayer();
    }
    uint Pointer(long address) => BitConverter.ToUInt32(Native.Read(handle!, (nint)address, 4));
    uint Manager()
    {
        if (handle == null || process == null || process.HasExited) throw new InvalidOperationException("The game closed. Reconnect after logging in.");
        uint manager = Pointer(moduleBase + profile.CreatureManager);
        if (manager < 0x10000) throw new InvalidOperationException("The active world is not available. Log into the world first.");
        return manager;
    }
    Dictionary<uint, long> ActiveMap()
    {
        uint manager = Manager(), sentinel = Pointer(manager + 4), count = Pointer(manager + 8);
        if (sentinel < 0x10000 || count > 8192) throw new InvalidOperationException("Invalid active-creature manager.");
        var result = new Dictionary<uint, long>();
        var stack = new Stack<uint>(); var visited = new HashSet<uint>();
        stack.Push(Pointer(sentinel + 4));
        while (stack.Count > 0)
        {
            uint address = stack.Pop();
            if (address == sentinel || address < 0x10000 || !visited.Add(address)) continue;
            if (visited.Count > 8192) throw new InvalidOperationException("Active-creature traversal limit reached.");
            try
            {
                var node = Native.Read(handle!, (nint)address, 24);
                if (node[0xd] != 0) continue;
                stack.Push(BitConverter.ToUInt32(node, 0)); stack.Push(BitConverter.ToUInt32(node, 8));
                uint id = BitConverter.ToUInt32(node, 0x10), value = BitConverter.ToUInt32(node, 0x14);
                if (value >= 0x10000) result[id] = value;
            }
            catch (System.ComponentModel.Win32Exception) { /* Node changed during this read-only snapshot. */ }
        }
        return result;
    }
    public Entity? Find(uint id)
    {
        uint sentinel = Pointer(Manager() + 4);
        if (sentinel < 0x10000) return null;
        uint node = Pointer(sentinel + 4);
        for (int depth = 0; depth < 128 && node != sentinel && node >= 0x10000; depth++)
        {
            try
            {
                var data = Native.Read(handle!, (nint)node, 24);
                if (data[0xd] != 0) return null;
                uint key = BitConverter.ToUInt32(data, 0x10);
                if (key == id) return ReadEntity(BitConverter.ToUInt32(data, 0x14), id);
                node = BitConverter.ToUInt32(data, id < key ? 0 : 8);
            }
            catch (System.ComponentModel.Win32Exception) { return null; }
        }
        return null;
    }
    public List<Entity> Poll()
    {
        var result = new List<Entity>();
        var active = ActiveMap();
        addresses = active.Values.ToList();
        foreach (var (id, address) in active)
        {
            var entity = ReadEntity(address, id);
            if (entity != null) result.Add(entity);
        }
        return result;
    }
    Entity? ReadEntity(long address, uint expectedId)
    {
        if (address < 0x10000) return null;
        try
        {
                var bytes = Native.Read(handle!, (nint)address, 0x154);
                uint id = BitConverter.ToUInt32(bytes, 4);
                // Mirror the client's own lookup at 0x73AD80: inactive pooled records must not be returned.
                if (BitConverter.ToUInt32(bytes, 0) != moduleBase + CreatureRva || id != expectedId || bytes[0x12] != 0) return null;
                uint length = BitConverter.ToUInt32(bytes, 0x28), capacity = BitConverter.ToUInt32(bytes, 0x2c);
                if (id == 0 || length > 128 || capacity < length) return null;
                byte[] nameBytes = length == 0 ? [] : capacity < 16 ? bytes.Skip(0x18).Take((int)length).ToArray() : Native.Read(handle!, (nint)BitConverter.ToUInt32(bytes, 0x18), (int)length);
                string name = Encoding.UTF8.GetString(nameBytes);
                if (name.Any(c => char.IsControl(c) || c == '\uFFFD')) return null;
                string model = "";
                // Read every valid model, including currently unclassified/unnamed props, for the encounter catalog.
                {
                    uint modelLength = BitConverter.ToUInt32(bytes, 0x60), modelCapacity = BitConverter.ToUInt32(bytes, 0x64);
                    if (modelLength > 128 || modelCapacity < modelLength) return null;
                    byte[] modelBytes = modelLength == 0 ? [] : modelCapacity < 16 ? bytes.Skip(0x50).Take((int)modelLength).ToArray() : Native.Read(handle!, (nint)BitConverter.ToUInt32(bytes, 0x50), (int)modelLength);
                    model = Encoding.UTF8.GetString(modelBytes);
                    if (model.Any(c => char.IsControl(c) || c == '\uFFFD')) return null;
                }
                var pos = new Vec(BitConverter.ToSingle(bytes, 0x12c) / 100.0, BitConverter.ToSingle(bytes, 0x134) / 100.0);
                double height = BitConverter.ToSingle(bytes, 0x130) / 100.0;
                double heading = BitConverter.ToSingle(bytes, 0x150);
                uint backing = BitConverter.ToUInt32(bytes, 0x4c);
                if (backing >= 0x10000)
                {
                    var transform = Native.Read(handle!, (nint)(backing + 0x20), 0x1c);
                    pos = new Vec(BitConverter.ToSingle(transform, 0) / 100.0, BitConverter.ToSingle(transform, 8) / 100.0);
                    height = BitConverter.ToSingle(transform, 4) / 100.0;
                    heading = BitConverter.ToSingle(transform, 0x18);
                }
                if (!pos.Finite || Math.Abs(pos.X) > 10000 || Math.Abs(pos.Y) > 10000 || pos.Length < 1 || !double.IsFinite(height) || Math.Abs(height) > 10000 || !double.IsFinite(heading)) return null;
                // Reject an allocation reused while its variable-length name was being read.
                var identity = Native.Read(handle!, (nint)address, 0x68);
                if (BitConverter.ToUInt32(identity, 0) != moduleBase + CreatureRva || BitConverter.ToUInt32(identity, 4) != id || identity[0x12] != 0 ||
                    BitConverter.ToUInt32(identity, 8) != BitConverter.ToUInt32(bytes, 8) || !identity.AsSpan(0x18, 0x18).SequenceEqual(bytes.AsSpan(0x18, 0x18)) ||
                    !identity.AsSpan(0x50, 0x18).SequenceEqual(bytes.AsSpan(0x50, 0x18))) return null;
                return new Entity(address, id, name, pos, height, heading, BitConverter.ToUInt32(bytes, 8), model);
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }
    public Entity LocalPlayer()
    {
        uint actor = Pointer(moduleBase + profile.LocalActor);
        if (actor < 0x10000) throw new InvalidOperationException("Local player unavailable.");
        uint id = Pointer(actor + 0x164), address = Pointer(Manager());
        var self = LocalCharacter.Require(id,ReadEntity(address,id));
        PlayerName=self.Name;
        return self;
    }
    public Vec PlayerPosition() => LocalPlayer().Position;
    public double PlayerHeading() => LocalPlayer().Heading;
    public int PlayerLevel()
    {
        if (handle == null) throw new InvalidOperationException("Not connected.");
        uint scene = BitConverter.ToUInt32(Native.Read(handle, (nint)(moduleBase + profile.Scene), 4));
        if (scene < 0x10000) throw new InvalidOperationException("Player scene unavailable.");
        int level = Native.Read(handle, (nint)(scene + 0x176), 1)[0];
        if (level is < 1 or > 250) throw new InvalidOperationException("Player level unavailable.");
        return level;
    }
    public Dictionary<uint, Health> HealthSnapshot()
    {
        var result = new Dictionary<uint, Health>();
        uint manager = Pointer(moduleBase + profile.UidDataManager);
        if (manager < 0x10000) return result;
        uint sentinel = Pointer(manager + 4), count = Pointer(manager + 8);
        if (sentinel < 0x10000 || count > 8192) return result;
        var seen = new HashSet<uint>(); uint node = Pointer(sentinel);
        while (node >= 0x10000 && node != sentinel && seen.Add(node) && seen.Count <= 8192)
        {
            var link = Native.Read(handle!, (nint)node, 12);
            uint record = BitConverter.ToUInt32(link, 8);
            if (record >= 0x10000)
            {
                try
                {
                    var data = Native.Read(handle!, (nint)record, 0x75);
                    if (data[0x74] != 0)
                    {
                        uint uid = BitConverter.ToUInt32(data, 0), max = BitConverter.ToUInt32(data, 0x4c);
                        int hp = BitConverter.ToInt32(data, 0x44);
                        if (max is > 0 and < 2000000000 && hp <= max) { result[uid] = new Health(hp, max); healthRecords[uid] = record; }
                    }
                }
                catch (System.ComponentModel.Win32Exception) { }
            }
            node = BitConverter.ToUInt32(link, 0);
        }
        return result;
    }
    public Health TargetHealth(uint id)
    {
        if (healthRecords.TryGetValue(id, out uint record))
        {
            try
            {
                var b = Native.Read(handle!, (nint)record, 0x75);
                uint maximum = BitConverter.ToUInt32(b, 0x4c); int hp = BitConverter.ToInt32(b, 0x44);
                if (BitConverter.ToUInt32(b, 0) == id && b[0x74] != 0 && maximum is > 0 and < 2000000000 && hp <= maximum) return new Health(hp, maximum);
            }
            catch (System.ComponentModel.Win32Exception) { }
            healthRecords.Remove(id);
        }
        return HealthSnapshot().GetValueOrDefault(id);
    }
    public List<GroundItem> Loot()
    {
        uint scene = Pointer(moduleBase + profile.Scene);
        var result = new List<GroundItem>();
        if (scene < 0x10000) return result;
        uint sentinel = Pointer(scene + profile.Layout.GroundItems), count = Pointer(scene + profile.Layout.GroundCount);
        if (sentinel < 0x10000 || count > 8192) return result;
        uint node = Pointer(sentinel); var seen = new HashSet<uint>();
        while (node >= 0x10000 && node != sentinel && seen.Add(node) && seen.Count <= 8192)
        {
            var link = Native.Read(handle!, (nint)node, 12);
            uint record = BitConverter.ToUInt32(link, 8);
            if (record >= 0x10000)
            {
                try
                {
                    var b = Native.Read(handle!, (nint)record, 0x28);
                    int type = BitConverter.ToInt32(b, 8);
                    var details = DescribeItem(type);
                    var item = GroundItem.FromRecord(b, details.Name, details.Description);
                    if (item.Position.Finite && Math.Abs(item.Position.X) <= 10000 && Math.Abs(item.Position.Y) <= 10000 && double.IsFinite(item.Height)) result.Add(item);
                }
                catch (System.ComponentModel.Win32Exception) { }
            }
            node = BitConverter.ToUInt32(link, 0);
        }
        return result;
    }
    string ItemName(int type)
    {
        if (itemNames.TryGetValue(type, out var known)) return known;
        string fallback = type < 0 ? "Gold" : $"Item type {type}";
        if (type is < 0 or > 65535) return fallback;
        long table = moduleBase + profile.ItemDefinitions;
        uint count = Pointer(table), records = Pointer(table + 4);
        if (count is 0 or > 65536 || records < 0x10000) return fallback;
        int low = 0, high = (int)count - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2; long address = records + middle * 0x26cL;
            ushort key = BitConverter.ToUInt16(Native.Read(handle!, (nint)address, 2));
            if (key < type) low = middle + 1;
            else if (key > type) high = middle - 1;
            else
            {
                var bytes = Native.Read(handle!, (nint)(address + 0x34), 0x40);
                int end = Array.IndexOf(bytes, (byte)0); if (end < 0) end = bytes.Length;
                string name = Encoding.UTF8.GetString(bytes, 0, end);
                if (name.Length == 0 || name.Any(c => char.IsControl(c) || c == '\uFFFD')) name = fallback;
                itemNames[type] = name; return name;
            }
        }
        return fallback;
    }
    internal byte[] ItemRecord(int type)
    {
        if (type is < 0 or > 65535) return [];
        long table = moduleBase + profile.ItemDefinitions;
        uint count = Pointer(table), records = Pointer(table + 4);
        if (count is 0 or > 65536 || records < 0x10000) return [];
        int low = 0, high = (int)count - 1;
        while (low <= high)
        {
            int middle = low + (high-low)/2; long address = records + middle * 0x26cL;
            ushort key = BitConverter.ToUInt16(Native.Read(handle!, (nint)address, 2));
            if (key < type) low = middle + 1;
            else if (key > type) high = middle - 1;
            else return Native.Read(handle!, (nint)address, 0x26c);
        }
        return [];
    }
    public ItemDetails DescribeItem(int type)
    {
        if (itemDetails.TryGetValue(type,out var known)) return known;
        var bytes = ItemRecord(type);
        if (bytes.Length == 0) return new ItemDetails(type,ItemName(type),"","",0,0);
        static string Text(byte[] data,int offset,int length)
        {
            int end = Array.IndexOf(data,(byte)0,offset,length); if (end < 0) end = offset+length;
            string value = Encoding.UTF8.GetString(data,offset,end-offset);
            return value.Any(c => c=='\uFFFD' || char.IsControl(c) && c is not '\r' and not '\n' and not '\t') ? "" : value;
        }
        string name=Text(bytes,0x34,0x40), category=Text(bytes,0xfc,0x20), description=Text(bytes,0x11c,0x100);
        var effect=RecoveryItems.Parse(category,description);
        var result=new ItemDetails(type,string.IsNullOrWhiteSpace(name)?ItemName(type):name,category,description,effect.Health,effect.Mana);
        itemDetails[type]=result; return result;
    }
    public HotbarSnapshot Hotbar()
    {
        uint scene = Pointer(moduleBase + profile.Scene);
        if (scene < 0x10000) throw new InvalidOperationException("Hotbar scene unavailable.");
        uint page = Pointer(scene + profile.Layout.HotbarPage);
        if (page != 0 && page != 10) throw new InvalidOperationException("Unrecognized hotbar page.");
        var pointers = Native.Read(handle!, (nint)(scene + profile.Layout.HotbarSlots + page * 4), 40);
        var slots = new List<HotbarSlot>();
        for (int i = 0; i < 10; i++)
        {
            string key = "1234567890"[i].ToString();
            uint slot = BitConverter.ToUInt32(pointers, i * 4);
            if (slot < 0x10000) { slots.Add(new HotbarSlot(key, SlotKind.Empty, 0, "Empty", 0, 0, false, 0)); continue; }
            var b = Native.Read(handle!, (nint)slot, 0x1c4);
            uint kind = BitConverter.ToUInt32(b, profile.Layout.SlotKind), assignment = BitConverter.ToUInt32(b, 0x1c0);
            uint total = BitConverter.ToUInt32(b, profile.Layout.CooldownTotal), remaining = BitConverter.ToUInt32(b, profile.Layout.CooldownRemaining);
            bool locked = b[profile.Layout.Locked] != 0; int lockRemaining = BitConverter.ToInt32(b, profile.Layout.LockRemaining);
            if (assignment < 0x10000) { slots.Add(new HotbarSlot(key, SlotKind.Empty, 0, "Empty", total, remaining, locked, lockRemaining)); continue; }
            int id = 0; string name = "Unknown assignment"; SlotKind slotKind = SlotKind.Unknown;
            if (kind == 1)
            {
                ushort skill = BitConverter.ToUInt16(Native.Read(handle!, (nint)(assignment + 0x10), 2));
                id = skill; name = SkillName(skill); slotKind = SlotKind.Skill;
            }
            else if (kind == 0)
            {
                id = BitConverter.ToUInt16(Native.Read(handle!, (nint)(assignment + 0x10), 2));
                name = ItemName(id); slotKind = SlotKind.Item;
            }
            else
            {
                uint item = Pointer(assignment + 4);
                if (item >= 0x10000)
                {
                    byte itemType = Native.Read(handle!, (nint)(item + 0x15), 1)[0];
                    if (itemType is 0x32 or 0x34)
                    {
                        id = BitConverter.ToUInt16(Native.Read(handle!, (nint)(item + 0x260), 2));
                        name = ItemName(id); slotKind = SlotKind.Item;
                    }
                }
            }
            var detail = slotKind == SlotKind.Item ? DescribeItem(id) : new ItemDetails(id,name,"","",0,0);
            var meta=slotKind==SlotKind.Skill?skillMetadata.GetValueOrDefault((ushort)id):null;
            int? manaCost=null;
            if(slotKind==SlotKind.Skill&&meta!=null&&ManaSupported)
            {
                byte rank=Native.Read(handle!,(nint)(assignment+0x290),1)[0];
                manaCost=ManaReserveRule.Cost(meta.Use,meta.ManaCosts,rank);
                if(Pointer(slot+0x1c0)!=assignment)throw new InvalidOperationException("Skill assignment changed during mana-cost read.");
            }
            slots.Add(new HotbarSlot(key, slotKind, id, name, total, remaining, locked, lockRemaining,detail.Category,meta?.Description??detail.Description,detail.RestoresHealth,detail.RestoresMana,meta?.Use??SkillUseKind.Unknown,meta?.Target??SkillTargetKind.Unknown,manaCost));
        }
        if (Pointer(scene + profile.Layout.HotbarPage) != page) throw new InvalidOperationException("Hotbar page changed during the read.");
        return new HotbarSnapshot((int)page, slots);
    }
    public ushort SelectedSkill()
    {
        uint scene=Pointer(moduleBase+profile.Scene),page=Pointer(scene+profile.Layout.HotbarPage),selected=Pointer(scene+profile.Layout.SelectedSlot);
        if(page is not (0 or 10) || selected<0x10000)return 0;
        var slots=Native.Read(handle!,(nint)(scene+profile.Layout.HotbarSlots+page*4),40);
        if(!Enumerable.Range(0,10).Any(i=>BitConverter.ToUInt32(slots,i*4)==selected))return 0;
        if(Pointer(selected+profile.Layout.SlotKind)!=1)return 0;
        uint assignment=Pointer(selected+0x1c0);if(assignment<0x10000)return 0;
        ushort id=BitConverter.ToUInt16(Native.Read(handle!,(nint)(assignment+0x10),2));
        return scene==Pointer(moduleBase+profile.Scene) && page==Pointer(scene+profile.Layout.HotbarPage) && selected==Pointer(scene+profile.Layout.SelectedSlot) && assignment==Pointer(selected+0x1c0)?id:(ushort)0;
    }
    string SkillName(ushort id)
    {
        if (skillNames.TryGetValue(id, out var known)) return known;
        string fallback = $"Skill {id}";
        uint manager = Pointer(moduleBase + profile.SkillDefinitions);
        if (manager < 0x10000) return fallback;
        uint records = Pointer(manager), count = Pointer(manager + 4);
        if (count is 0 or > 65536 || records < 0x10000 || (ulong)records + count * 0xa14UL > uint.MaxValue) return fallback;
        int low = 0, high = (int)count - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2; long record = records + middle * 0xa14L;
            ushort key = BitConverter.ToUInt16(Native.Read(handle!, (nint)(record + 0x1d8), 2));
            if (key < id) low = middle + 1;
            else if (key > id) high = middle - 1;
            else
            {
                var bytes = Native.Read(handle!, (nint)(record + 0x60), 128);
                int end = Array.IndexOf(bytes, (byte)0); if (end < 0) end = bytes.Length;
                string name = Encoding.UTF8.GetString(bytes, 0, end);
                if (name.Length == 0 || name.Any(c => char.IsControl(c) || c == '\uFFFD')) name = fallback;
                var recordBytes=Native.Read(handle!,(nint)record,0xa10);
                int use=BitConverter.ToInt32(recordBytes,0x1c8),target=BitConverter.ToInt32(recordBytes,0x1cc);
                string description=Encoding.UTF8.GetString(recordBytes,0xc8,0x100).Split('\0')[0].Replace("\\\\","\n");
                skillMetadata[id]=new(Enum.IsDefined(typeof(SkillUseKind),use)?(SkillUseKind)use:SkillUseKind.Unknown,
                    Enum.IsDefined(typeof(SkillTargetKind),target)?(SkillTargetKind)target:SkillTargetKind.Unknown,description,
                    Enumerable.Range(0,5).Select(rank=>new SkillManaCost(BitConverter.ToUInt16(recordBytes,rank*0x204+0x1da),BitConverter.ToUInt16(recordBytes,rank*0x204+0x1de))).ToArray());
                skillNames[id] = name; return name;
            }
        }
        return fallback;
    }
    public MonsterDefinition Definition(uint prototypeId)
    {
        if (definitions.TryGetValue(prototypeId, out var cached)) return cached;
        if (handle == null) throw new InvalidOperationException("Not connected.");
        uint manager = BitConverter.ToUInt32(Native.Read(handle, (nint)(moduleBase + profile.MonsterDefinitions), 4));
        if (manager < 0x10000) return default;
        var header = Native.Read(handle, (nint)manager, 8);
        uint records = BitConverter.ToUInt32(header, 0), count = BitConverter.ToUInt32(header, 4);
        if (records < 0x10000 || count is 0 or > 50000 || (ulong)records + count * 0x248UL > uint.MaxValue) return default;
        int low = 0, high = (int)count - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            long record = records + middle * 0x248L;
            uint key = BitConverter.ToUInt32(Native.Read(handle, (nint)(record + 0x15c), 4));
            if (key < prototypeId) low = middle + 1;
            else if (key > prototypeId) high = middle - 1;
            else
            {
                var bytes = Native.Read(handle, (nint)record, 0x248);
                static string Text(byte[] bytes, int offset, int length)
                {
                    int end = Array.IndexOf(bytes, (byte)0, offset, length); if (end < 0) end = offset + length;
                    string value = Encoding.UTF8.GetString(bytes, offset, end-offset);
                    return value.Any(c => char.IsControl(c) || c == '\uFFFD') ? "" : value;
                }
                var result = new MonsterDefinition(BitConverter.ToInt32(bytes, 0x1d8), bytes[0x16e], Text(bytes,0x40,0x40), Text(bytes,0x80,0x30));
                definitions[prototypeId] = result; return result;
            }
        }
        return default;
    }
    public Threat Difficulty(Entity entity, int playerLevel) => entity.Monster ? Definition(entity.Id & 0xffff).Difficulty(playerLevel) : Threat.Unknown;
    internal Dictionary<uint, byte[]> ObserveWorldData()
    {
        uint manager = Pointer(moduleBase + profile.UidDataManager);
        var result = new Dictionary<uint, byte[]>();
        if (manager < 0x10000) return result;
        uint sentinel = Pointer(manager + 0x10);
        if (sentinel < 0x10000) return result;
        uint node = Pointer(sentinel);
        var seen = new HashSet<uint>();
        while (node >= 0x10000 && node != sentinel && seen.Add(node) && seen.Count <= 8192)
        {
            var entry = Native.Read(handle!, (nint)node, 16);
            uint id = BitConverter.ToUInt32(entry, 8), data = BitConverter.ToUInt32(entry, 12);
            if (data >= 0x10000)
            {
                try { var bytes = Native.Read(handle!, (nint)data, 0x80); if (BitConverter.ToUInt32(bytes, 0) == id && bytes[0x74] != 0) result[id] = bytes; }
                catch (System.ComponentModel.Win32Exception) { }
            }
            node = BitConverter.ToUInt32(entry, 0);
        }
        return result;
    }
    internal uint[] ActiveIds() => ActiveMap().Keys.ToArray();
    internal byte[] ObservePartyScene() => Native.Read(handle!,(nint)Pointer(moduleBase+profile.Scene),0x4500);
    internal object ObserveGroup()
    {
        var self=LocalPlayer();var entities=Poll();var health=HealthSnapshot();
        object ObservePlayer(Entity e)
        {
            uint backing=Pointer(e.Address+0x4c);
            return new {Entity=e,Raw=Convert.ToHexString(ObserveCreature(e) ?? []),
                Backing=backing>=0x10000?Convert.ToHexString(Native.Read(handle!,(nint)backing,0x400)):null,
                Uid=healthRecords.TryGetValue(e.Id,out uint record)?Convert.ToHexString(Native.Read(handle!,(nint)record,0x80)):null};
        }
        return new {TimeUtc=DateTime.UtcNow,Zone=ActiveZone(),Self=self,
            Players=entities.Where(e=>CombatCourtesy.IsOtherPlayer(e,self.Id)).OrderBy(e=>(e.Position-self.Position).Length).Take(16)
                .Select(ObservePlayer).ToArray(),
            Party=Party(),SceneParty=Convert.ToHexString(Native.Read(handle!,(nint)(Pointer(moduleBase+profile.Scene)+0x238),0x3ac)),
            Monsters=entities.Where(e=>e.Targetable).OrderBy(e=>(e.Position-self.Position).Length).Take(128)
                .Select(e=>new {Entity=e,HP=health.GetValueOrDefault(e.Id)}).ToArray()};
    }
    internal object ObserveRest()
    {
        var self=LocalPlayer();
        uint actor=Pointer(moduleBase+profile.LocalActor),backing=Pointer(self.Address+0x4c);
        _=TargetHealth(self.Id);
        return new {TimeUtc=DateTime.UtcNow,self.Id,self.Generation,self.Address,self.Position,
            Creature=Convert.ToHexString(Native.Read(handle!,(nint)self.Address,0x2d0)),
            Actor=Convert.ToHexString(Native.Read(handle!,(nint)actor,0x300)),
            Backing=backing>=0x10000?Convert.ToHexString(Native.Read(handle!,(nint)backing,0x200)):null,
            Uid=healthRecords.TryGetValue(self.Id,out uint record)?Convert.ToHexString(Native.Read(handle!,(nint)record,0x80)):null};
    }
    internal byte[]? ObserveCreature(Entity identity)
    {
        if (handle == null) throw new InvalidOperationException("Not connected.");
        try
        {
            var b = Native.Read(handle, (nint)identity.Address, 0x2d0);
            return BitConverter.ToUInt32(b, 0) == moduleBase + CreatureRva && BitConverter.ToUInt32(b, 4) == identity.Id ? b : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }
    public void Dispose()
    {
        ConnectionVerified=false;
        ResetCamera();
        ResetMana();
        ResetWallet();
        windowIdentity = null;
        PlayerName="";
        RestSupported=false;
        PartySupported=false;
        ActiveEffectsSupported=false;effectNames=[];
        skillMetadata.Clear();
        handle?.Dispose(); handle = null; process?.Dispose(); process = null; Window = IntPtr.Zero; addresses.Clear(); definitions.Clear(); itemNames.Clear(); healthRecords.Clear(); skillNames.Clear(); itemDetails.Clear();
    }
}
