using System.Security.Cryptography;
using System.Text.Json;

namespace PoteHunter;

public sealed record SignatureEvidence(string Name, uint CodeRva, string Pattern, int CaptureOffset, uint? CapturedRva);
public sealed record ProfileDetection(BuildProfile Profile, bool Automatic, uint TimeDateStamp, IReadOnlyList<SignatureEvidence> Evidence);

public static partial class ProfileDiscovery
{
    sealed record Rule(string Name, string Pattern, int CaptureOffset = -1);
    sealed record Manifest(int Version, Rule[] Roots, Rule[] Checks);
    static readonly Lazy<Manifest> signatures = new(() => LoadManifest("BuildSignatures.json"));
    static readonly Lazy<Manifest> september30Signatures = new(() => LoadManifest("BuildSignatures-20260930.json"));
    static Manifest LoadManifest(string name)
    {
        using var stream=typeof(ProfileDiscovery).Assembly.GetManifestResourceStream("PoteHunter."+name)
            ?? throw Failure("the bundled signature catalog is missing");
        var manifest=JsonSerializer.Deserialize<Manifest>(stream) ?? throw Failure("the signature catalog is invalid");
        Require(manifest.Version==1 && manifest.Roots.Length>=8 && manifest.Checks.Length>=9,"incomplete signature catalog");
        return manifest;
    }
    static InvalidOperationException Failure(string reason) => new("Automatic client detection stopped: " + reason + ". Controls remain disabled.");
    static void Require(bool condition,string reason) { if(!condition) throw Failure(reason); }

    internal static ProfileDetection ResolveForConnection(byte[] bytes)
    {
        var result=Resolve(bytes);
        // This grants reads only after discovery validates all required roots
        // and members. Loaded signatures/layout must still pass in World.Connect.
        PoteMemoryProbe.ClientCompatibility.AuthorizeDiscoveredRead(result.Profile.Sha256);
        return result;
    }

    static ProfileDetection Resolve(byte[] bytes)
    {
        string hash=Convert.ToHexStringLower(SHA256.HashData(bytes));
        var image=new PeImage(bytes);
        if(BuildProfile.TryResolve(hash,out var known))
        {
            Require(known.ImageSize==image.ImageSize,"known build image size mismatch");
            return new(known,false,image.TimeDateStamp,[]);
        }
        return Discover(bytes);
    }

    public static ProfileDetection Discover(byte[] bytes)
    {
        var matches=new List<ProfileDetection>();
        var failures=new List<string>();
        foreach(var catalog in new[]{(signatures.Value,SceneLayout.Legacy,"original"),
            (september30Signatures.Value,SceneLayout.September30,"September 30")})
        {
            try { matches.Add(Discover(bytes,catalog.Item1,catalog.Item2)); }
            catch(InvalidOperationException ex) { failures.Add(catalog.Item3+": "+ex.Message); }
        }
        Require(matches.Count<=1,"multiple incompatible layout catalogs matched");
        if(matches.Count==0) throw Failure("no verified layout catalog matched ("+string.Join("; ",failures)+")");
        return matches[0];
    }

    static ProfileDetection Discover(byte[] bytes,Manifest manifest,SceneLayout layout)
    {
        var image=new PeImage(bytes);
        var evidence=new List<SignatureEvidence>();
        var roots=new Dictionary<string,uint>(StringComparer.Ordinal);
        foreach(var rule in manifest.Roots)
        {
            var pattern=new Pattern(rule.Pattern);
            Require(rule.CaptureOffset>=0 && rule.CaptureOffset+4<=pattern.Length,"invalid capture for " + rule.Name);
            var matches=image.Find(pattern);
            Require(matches.Count>0 && matches.Count<=16,"missing or overly broad signature: " + rule.Name);
            var values=new HashSet<uint>();
            foreach(uint match in matches)
            {
                uint value=image.U32Rva(checked(match+(uint)rule.CaptureOffset));
                Require(value>=image.ImageBase && (ulong)value<(ulong)image.ImageBase+image.ImageSize,"invalid address for " + rule.Name);
                uint rva=value-image.ImageBase; values.Add(rva);
                evidence.Add(new(rule.Name,match,rule.Pattern,rule.CaptureOffset,rva));
            }
            Require(values.Count==1,"ambiguous address for " + rule.Name);
            uint resolved=values.Single();
            Require(!roots.TryGetValue(rule.Name,out uint previous) || previous==resolved,"independent signatures disagree for " + rule.Name);
            roots[rule.Name]=resolved;
        }
        foreach(var rule in manifest.Checks)
        {
            var matches=image.Find(new Pattern(rule.Pattern));
            Require(matches.Count==1,"member layout could not be verified: " + rule.Name);
            evidence.Add(new(rule.Name,matches[0],rule.Pattern,-1,null));
        }
        string[] names=["CreatureVtable","Scene","LocalActor","CreatureManager","UidDataManager","MonsterDefinitions","ItemDefinitions","SkillDefinitions"];
        Require(names.All(roots.ContainsKey) && names.Select(n=>roots[n]).Distinct().Count()==8,"incomplete or aliased memory roots");
        foreach(string name in names)
        {
            var section=image.SectionAt(roots[name],name=="CreatureVtable"?8u:16u);
            Require(section!=null,"root is outside mapped sections: "+name);
            Require(name=="CreatureVtable" ? !section!.Writable && !section.Executable : section!.Writable,"unexpected section for "+name);
        }
        uint destructor=image.U32Rva(roots["CreatureVtable"]);
        Require(destructor>=image.ImageBase && image.SectionAt(destructor-image.ImageBase,1)?.Executable==true,"creature vtable does not reference executable code");
        var profile=new BuildProfile(Convert.ToHexStringLower(SHA256.HashData(bytes)),image.ImageSize,
            roots["CreatureVtable"],roots["Scene"],roots["LocalActor"],roots["CreatureManager"],roots["UidDataManager"],roots["MonsterDefinitions"],roots["ItemDefinitions"],roots["SkillDefinitions"]) { Layout=layout };
        return new(profile,true,image.TimeDateStamp,evidence);
    }

    public static bool Matches(byte[] bytes,string pattern) => new Pattern(pattern).Matches(bytes,0);

    // Optional features must not disable normal hunting when their additional layout is unrecognized.
    public static SignatureEvidence? RestEvidence(string path)
    {
        const string pattern="8B 85 ?? ?? ?? ?? 8A 88 4C 02 00 00 B8 ?? ?? ?? ?? 84 C9 0F 44 C6";
        return OptionalEvidence(path,"Standing/sitting regeneration flag",pattern);
    }
    public static IReadOnlyList<SignatureEvidence> ActiveEffectEvidence(string path,uint sceneRva,SceneLayout? layout=null)
    {
        var image=new PeImage(File.ReadAllBytes(path));
        var rules=new[]{
            new Rule("Effect scene root","A1 ?? ?? ?? ?? 33 C9 8B 3D ?? ?? ?? ?? 89 85 C0 FC FF FF 89 8D BC FC FF FF 8B 80 "+
                (layout==SceneLayout.September30?"74":"4C")+" 36 00 00",1),
            new Rule("Effect timer array","8B 85 C0 FC FF FF 8D 93 00 04 00 00 05 "+
                (layout==SceneLayout.September30?"AC":"84")+" 06 00 00 89 95 AC FC FF FF BE 01 00 00 00 89 85 B8 FC FF FF"),
            new Rule("Effect magnitude","0F B7 98 64 FF FF FF 66 85 DB 0F 84 ?? ?? ?? ?? 83 3A 00"),
            new Rule("Effect seconds","8B 85 B8 FC FF FF 8B D3 33 DB 0F B7 00 83 F8 01 8D 48 FF 8D 46 FF 0F 43 D9"),
            new Rule("Effect stride","46 83 C2 04 83 C0 02 89 95 AC FC FF FF 89 85 B8 FC FF FF 83 FE 4E"),
            new Rule("Effect labels","C1 E6 0A 8D 85 C4 FD FF FF 81 C6 ?? ?? ?? ?? 56 50 FF D7 83 C4 0C",11)};
        var evidence=new List<SignatureEvidence>();
        foreach(var rule in rules)
        {
            var found=image.Find(new Pattern(rule.Pattern));if(found.Count!=1)return [];
            uint? captured=null;
            if(rule.CaptureOffset>=0){uint va=image.U32Rva(found[0]+(uint)rule.CaptureOffset);if(va<image.ImageBase || va-image.ImageBase>=image.ImageSize)return [];captured=va-image.ImageBase;}
            evidence.Add(new(rule.Name,found[0],rule.Pattern,rule.CaptureOffset,captured));
        }
        if(evidence[0].CapturedRva!=sceneRva || evidence.Max(e=>e.CodeRva)-evidence.Min(e=>e.CodeRva)>0x1000)return [];
        return evidence;
    }
    // Gold is the first currency passed to the shared formatter by both inventory panels.
    // Capture its member offset instead of assuming that every client has the same scene layout.
    public static IReadOnlyList<SignatureEvidence> WalletEvidence(string path,uint sceneRva)
        =>WalletEvidence(File.ReadAllBytes(path),sceneRva);

    static IReadOnlyList<SignatureEvidence> WalletEvidence(byte[] bytes,uint sceneRva)
    {
        var image=new PeImage(bytes);
        var rules=new[]{
            new Rule("Wallet Akhan scene","8B 3D ?? ?? ?? ?? 89 BD C4 FE FF FF 8B 01 FF 50 40",2),
            new Rule("Wallet Akhan display","8B 9D CC FE FF FF FF B7 ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B 8B 44 02 00 00 83 C4 04 50 E8 ?? ?? ?? ?? FF B7 ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B 8B 48 02 00 00 83 C4 04 50 E8 ?? ?? ?? ?? FF B7 ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B 8B 4C 02 00 00 83 C4 04 50 E8 ?? ?? ?? ??"),
            new Rule("Wallet Human scene","8B 3D ?? ?? ?? ?? 89 BD C0 FE FF FF 8B 01 FF 50 40",2),
            new Rule("Wallet Human display","8B 9D C0 FE FF FF FF B3 ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B 8F 5C 02 00 00 83 C4 04 50 E8 ?? ?? ?? ?? FF B3 ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B 8F 60 02 00 00 83 C4 04 50 E8 ?? ?? ?? ?? FF B3 ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B 8F 64 02 00 00 83 C4 04 50 E8 ?? ?? ?? ??")};
        var result=new List<SignatureEvidence>();
        foreach(var rule in rules)
        {
            var found=image.Find(new Pattern(rule.Pattern));if(found.Count!=1)return [];
            uint? captured=null;
            if(rule.CaptureOffset>=0)
            {
                uint address=image.U32Rva(found[0]+(uint)rule.CaptureOffset);
                if(address<image.ImageBase || address-image.ImageBase!=sceneRva)return [];
                captured=address-image.ImageBase;
            }
            result.Add(new(rule.Name,found[0],rule.Pattern,rule.CaptureOffset,captured));
        }
        for(int i=0;i<4;i+=2)
            if(result[i+1].CodeRva<=result[i].CodeRva || result[i+1].CodeRva-result[i].CodeRva>0x2000)return [];
        uint field=image.U32Rva(result[1].CodeRva+8);
        if(field is <0x100 or >0x400 || field!=image.U32Rva(result[3].CodeRva+8))return [];
        // Both panels must use the same three currency formatters and text setters.
        long? formatter=null,setter=null;
        foreach(var panel in new[]{result[1],result[3]})
            for(uint n=0;n<3;n++)
            {
                uint call=panel.CodeRva+12+n*26;
                long target=call+5+(int)image.U32Rva(call+1);
                long textTarget=call+20+(int)image.U32Rva(call+16);
                if(formatter.HasValue && formatter!=target || setter.HasValue && setter!=textTarget)return [];
                formatter=target;setter=textTarget;
            }
        return result;
    }

    public static SignatureEvidence? OptionalEvidence(string path,string name,string pattern)
    {
        var image=new PeImage(File.ReadAllBytes(path));
        var matches=image.Find(new Pattern(pattern));
        return matches.Count==1 ? new(name,matches[0],pattern,-1,null) : null;
    }

    sealed class Pattern
    {
        readonly byte[] values;
        readonly bool[] fixedBytes;
        readonly byte[] anchor;
        readonly int anchorOffset;
        public int Length=>values.Length;
        public Pattern(string text)
        {
            var tokens=text.Split(' ',StringSplitOptions.RemoveEmptyEntries);
            Require(tokens.Length is >=12 and <=512,"invalid signature length");
            values=new byte[tokens.Length]; fixedBytes=new bool[tokens.Length];
            int longest=0,run=0,start=0;
            for(int i=0;i<tokens.Length;i++)
            {
                if(tokens[i]=="??") { run=0; continue; }
                Require(byte.TryParse(tokens[i],System.Globalization.NumberStyles.HexNumber,null,out values[i]),"invalid signature byte");
                fixedBytes[i]=true;
                if(run++==0) start=i;
                if(run>longest) { longest=run; anchorOffset=start; }
            }
            Require(longest>=4 && fixedBytes.Count(b=>b)>=12,"signature is too broad");
            anchor=values.AsSpan(anchorOffset,longest).ToArray();
        }
        public bool Matches(byte[] bytes,int start)
        {
            if(start<0 || start>bytes.Length-values.Length) return false;
            for(int i=0;i<values.Length;i++) if(fixedBytes[i] && bytes[start+i]!=values[i]) return false;
            return true;
        }
        public List<int> Find(byte[] bytes,int offset,int length)
        {
            var results=new List<int>(); int cursor=offset;
            while(cursor<=offset+length-anchor.Length)
            {
                int found=bytes.AsSpan(cursor,offset+length-cursor).IndexOf(anchor);
                if(found<0) break;
                int anchorAt=cursor+found, candidate=anchorAt-anchorOffset;
                if(candidate>=offset && candidate+Length<=offset+length && Matches(bytes,candidate))
                {
                    results.Add(candidate);
                    Require(results.Count<=64,"too many signature matches");
                }
                cursor=anchorAt+1;
            }
            return results;
        }
    }

    sealed record Section(uint Rva,uint Size,int FileOffset,int FileSize,uint Flags)
    {
        public bool Executable=>(Flags&0x20000000)!=0;
        public bool Writable=>(Flags&0x80000000)!=0;
    }
    sealed class PeImage
    {
        readonly byte[] bytes;
        readonly List<Section> sections=new();
        public uint ImageBase {get;}
        public uint ImageSize {get;}
        public uint TimeDateStamp {get;}
        uint U32(int offset) { Require(offset>=0 && offset<=bytes.Length-4,"truncated PE header"); return BitConverter.ToUInt32(bytes,offset); }
        ushort U16(int offset) { Require(offset>=0 && offset<=bytes.Length-2,"truncated PE header"); return BitConverter.ToUInt16(bytes,offset); }
        public PeImage(byte[] bytes)
        {
            this.bytes=bytes;
            Require(bytes.Length>=512 && U16(0)==0x5a4d,"not a PE executable");
            uint peValue=U32(0x3c); Require(peValue<=int.MaxValue,"invalid PE offset"); int pe=(int)peValue;
            Require(pe>=0x40 && pe<=bytes.Length-248 && U32(pe)==0x4550 && U16(pe+4)==0x14c && U16(pe+24)==0x10b,"expected a PE32 x86 client");
            int count=U16(pe+6),optionalLength=U16(pe+20);
            Require(count is >0 and <=96 && optionalLength>=96,"invalid PE section table");
            ImageBase=U32(pe+24+28); ImageSize=U32(pe+24+56); TimeDateStamp=U32(pe+8);
            Require(ImageBase>0 && ImageSize>4096 && (ulong)ImageBase+ImageSize<=uint.MaxValue,"invalid mapped image bounds");
            int sectionTable=checked(pe+24+optionalLength);
            Require(sectionTable>=0 && (long)sectionTable+count*40<=bytes.Length,"truncated PE sections");
            for(int i=0;i<count;i++)
            {
                int p=sectionTable+i*40; uint rva=U32(p+12),size=Math.Max(U32(p+8),U32(p+16)),rawSize=U32(p+16),rawOffset=U32(p+20);
                Require((ulong)rva+size<=ImageSize && (ulong)rawOffset+rawSize<=(ulong)bytes.Length,"invalid PE section bounds");
                sections.Add(new(rva,size,checked((int)rawOffset),checked((int)rawSize),U32(p+36)));
            }
            Require(sections.Any(s=>s.Executable && s.FileSize>0),"no executable code section");
        }
        public Section? SectionAt(uint rva,uint length) => sections.FirstOrDefault(s=>rva>=s.Rva && (ulong)rva+length<=(ulong)s.Rva+s.Size);
        public uint U32Rva(uint rva)
        {
            var section=SectionAt(rva,4); Require(section!=null && (ulong)(rva-section.Rva)+4<=(ulong)section.FileSize,"capture is not file-backed");
            return U32(checked(section!.FileOffset+(int)(rva-section.Rva)));
        }
        public int FileOffset(uint rva)
        {
            var section=SectionAt(rva,1); Require(section!=null && rva-section.Rva<section.FileSize,"RVA is not file-backed");
            return checked(section!.FileOffset+(int)(rva-section.Rva));
        }
        public List<uint> Find(Pattern pattern) => sections.Where(s=>s.Executable).SelectMany(s=>pattern.Find(bytes,s.FileOffset,s.FileSize).Select(p=>checked(s.Rva+(uint)(p-s.FileOffset)))).ToList();
    }

    public static void SelfTest()
    {
        var pattern=new Pattern("01 02 03 04 ?? ?? 07 08 09 0A 0B 0C 0D 0E");
        byte[] sample=[0,1,2,3,4,88,99,7,8,9,10,11,12,13,14,0];
        if(!pattern.Find(sample,0,sample.Length).SequenceEqual(new[]{1}) || pattern.Matches(sample,0)) throw new Exception("Signature wildcard/search bounds");
        bool rejected=false; try { Discover(new byte[512]); } catch(InvalidOperationException) { rejected=true; }
        if(!rejected) throw new Exception("Malformed client image was accepted");
        _=signatures.Value;
        CheckLayoutCatalogs();
        CheckWalletLayouts();
    }

    static void CheckWalletLayouts()
    {
        byte[] fixture=new byte[0x5000];
        void U16(int at,ushort value)=>BitConverter.TryWriteBytes(fixture.AsSpan(at),value);
        void U32(int at,uint value)=>BitConverter.TryWriteBytes(fixture.AsSpan(at),value);
        U16(0,0x5a4d);U32(0x3c,0x80);U32(0x80,0x4550);U16(0x84,0x14c);U16(0x86,1);
        U16(0x94,0xe0);U16(0x98,0x10b);U32(0xb4,0x400000);U32(0xd0,0x5000);
        U32(0x180,0x4000);U32(0x184,0x1000);U32(0x188,0x4000);U32(0x18c,0x1000);U32(0x19c,0x60000020);
        byte[] scene=Convert.FromHexString("8B3D0048400089BDC4FEFFFF8B01FF5040");
        scene.CopyTo(fixture,0x1100);scene[8]=0xc0;scene.CopyTo(fixture,0x2100);
        void Panel(int at,byte register,byte local,byte control,uint member)
        {
            byte[] prefix=[0x8b,0x9d,local,0xfe,0xff,0xff];prefix.CopyTo(fixture,at);
            for(int i=0;i<3;i++)
            {
                int p=at+6+i*26;
                byte[] code=[0xff,register,0,0,0,0,0xe8,0,0,0,0,0x8b,register==0xb7?(byte)0x8b:(byte)0x8f,(byte)(control+i*4),2,0,0,0x83,0xc4,4,0x50,0xe8,0,0,0,0];
                code.CopyTo(fixture,p);U32(p+2,i==0?member:i==1?0x16au:0x3dfcu);
                U32(p+7,unchecked((uint)(0x4000-(p+11))));U32(p+22,unchecked((uint)(0x4100-(p+26))));
            }
        }
        Panel(0x1200,0xb7,0xcc,0x44,0x177);Panel(0x2200,0xb3,0xc0,0x5c,0x177);
        if(WalletEvidence(fixture,0x4800).Count!=4)throw new Exception("Independent wallet display proof was not accepted");
        void Reject(int at,uint value)
        {
            byte[] corrupt=(byte[])fixture.Clone();BitConverter.TryWriteBytes(corrupt.AsSpan(at),value);
            if(WalletEvidence(corrupt,0x4800).Count!=0)throw new Exception("Conflicting wallet display proof was accepted");
        }
        Reject(0x2102,0x404804);Reject(0x2208,0x178);Reject(0x1208,0);Reject(0x220d,1);
        byte[] duplicate=(byte[])fixture.Clone();fixture.AsSpan(0x1200,84).CopyTo(duplicate.AsSpan(0x3200));
        if(WalletEvidence(duplicate,0x4800).Count!=0)throw new Exception("Ambiguous wallet display code was accepted");
        // Older layout offsets are extracted from the display instructions too.
        Panel(0x1200,0xb7,0xcc,0x44,0x14f);Panel(0x2200,0xb3,0xc0,0x5c,0x14f);
        if(WalletEvidence(fixture,0x4800).Count!=4)throw new Exception("A verified alternate wallet member offset was not discovered");
    }

    // Construct file-backed PE fixtures, without copying or publishing game data.
    // Exercise complete catalog selection, agreeing roots and mandatory fields.
    static void CheckLayoutCatalogs()
    {
        foreach(var catalog in new[]{(signatures.Value,SceneLayout.Legacy),
            (september30Signatures.Value,SceneLayout.September30)})
        {
            byte[] fixture=new byte[0xb000];
            void U16(int at,ushort value)=>BitConverter.TryWriteBytes(fixture.AsSpan(at,2),value);
            void U32(int at,uint value)=>BitConverter.TryWriteBytes(fixture.AsSpan(at,4),value);
            U16(0,0x5a4d);U32(0x3c,0x80);U32(0x80,0x4550);U16(0x84,0x14c);
            U16(0x86,3);U16(0x94,0xe0);U16(0x98,0x10b);U32(0xb4,0x400000);U32(0xd0,0xb000);
            void Section(int at,uint rva,uint size,uint flags)
            {
                U32(at+8,size);U32(at+12,rva);U32(at+16,size);U32(at+20,rva);U32(at+36,flags);
            }
            Section(0x178,0x1000,0x6000,0x60000020);
            Section(0x1a0,0x8000,0x1000,0x40000040);
            Section(0x1c8,0x9000,0x2000,0xc0000040);
            string[] names=["CreatureVtable","Scene","LocalActor","CreatureManager","UidDataManager","MonsterDefinitions","ItemDefinitions","SkillDefinitions"];
            var roots=names.Select((name,index)=>(name,rva:index==0?0x8000u:0x9000u+(uint)index*0x100)).ToDictionary(x=>x.name,x=>x.rva);
            U32(0x8000,0x401100);
            var locations=new Dictionary<string,int>();
            int cursor=0x1100;
            foreach(var group in catalog.Item1.Roots.Concat(catalog.Item1.Checks).GroupBy(r=>r.Pattern))
            {
                var tokens=group.Key.Split(' ');
                locations[group.Key]=cursor;
                for(int i=0;i<tokens.Length;i++) fixture[cursor+i]=tokens[i]=="??"?(byte)0x5a:Convert.ToByte(tokens[i],16);
                foreach(var rule in group.Where(r=>r.CaptureOffset>=0)) U32(cursor+rule.CaptureOffset,0x400000+roots[rule.Name]);
                cursor+=tokens.Length+32;
            }
            // Some independent root signatures are substrings of longer ones.
            // Populate their wildcard captures at every matching location too.
            var fixtureImage=new PeImage(fixture);
            foreach(var rule in catalog.Item1.Roots)
                foreach(uint match in fixtureImage.Find(new Pattern(rule.Pattern)))
                    U32(fixtureImage.FileOffset(match)+rule.CaptureOffset,0x400000+roots[rule.Name]);
            var resolved=Discover(fixture);
            if(resolved.Profile.Layout!=catalog.Item2 || resolved.Profile.Scene!=roots["Scene"] ||
                resolved.Profile.LocalActor!=roots["LocalActor"] || !resolved.Automatic)
                throw new Exception("Complete catalog did not select its verified layout and captured roots.");
            void Reject(byte[] corrupt)
            {
                bool rejected=false;try { ResolveForConnection(corrupt); }catch(InvalidOperationException){rejected=true;}
                string hash=Convert.ToHexStringLower(SHA256.HashData(corrupt));
                if(!rejected || PoteMemoryProbe.ClientCompatibility.SupportsRead(hash))
                    throw new Exception("Incomplete or conflicting catalog granted read approval.");
            }
            byte[] missing=(byte[])fixture.Clone();
            missing[locations[catalog.Item1.Checks.Single(r=>r.Name=="Hotbar slots and page").Pattern]]=0xcc;
            Reject(missing);
            byte[] disagree=(byte[])fixture.Clone();
            var scene=catalog.Item1.Roots.First(r=>r.Name=="Scene");
            BitConverter.TryWriteBytes(disagree.AsSpan(locations[scene.Pattern]+scene.CaptureOffset,4),0x400000+roots["Scene"]+0x10);
            Reject(disagree);
            if(catalog.Item2==SceneLayout.September30)
            {
                foreach(var rule in catalog.Item1.Checks.Where(r=>r.Name.StartsWith("Slot ") || r.Name.StartsWith("Ground ") || r.Name=="Selected hotbar slot"))
                {
                    byte[] corrupt=(byte[])fixture.Clone();corrupt[locations[rule.Pattern]]=0xcc;Reject(corrupt);
                }
            }
        }
    }

    public static object CheckSnapshots(string knownPath,string updatedPath)
    {
        byte[] knownBytes=File.ReadAllBytes(knownPath),updatedBytes=File.ReadAllBytes(updatedPath);
        var baseline=Discover(knownBytes); var updated=Discover(updatedBytes);
        if(!BuildProfile.TryResolve(baseline.Profile.Sha256,out var expected) || baseline.Profile!=expected)
            throw new Exception("Automatic discovery did not reproduce the independently mapped baseline");
        if(updated.Profile.Sha256==baseline.Profile.Sha256) throw new Exception("A different updated client is required for the holdout check");
        var image=new PeImage(updatedBytes); const uint shift=0x4000;
        byte[] relocated=(byte[])updatedBytes.Clone();
        foreach(var evidence in updated.Evidence.Where(e=>e.CapturedRva.HasValue && e.Name!="CreatureVtable"))
        {
            int offset=image.FileOffset(evidence.CodeRva)+(int)evidence.CaptureOffset;
            BitConverter.TryWriteBytes(relocated.AsSpan(offset,4),image.ImageBase+evidence.CapturedRva!.Value+shift);
        }
        var shifted=Discover(relocated).Profile;
        foreach(string name in new[]{"Scene","LocalActor","CreatureManager","UidDataManager","MonsterDefinitions","ItemDefinitions","SkillDefinitions"})
        {
            var property=typeof(BuildProfile).GetProperty(name)!;
            if((uint)property.GetValue(shifted)! != (uint)property.GetValue(updated.Profile)!+shift) throw new Exception("Discovery reused a fixed address for "+name);
        }
        void Reject(byte[] bytes,string expectedReason)
        {
            try { Discover(bytes); }
            catch(InvalidOperationException ex) when(ex.Message.Contains(expectedReason,StringComparison.OrdinalIgnoreCase)) { return; }
            throw new Exception("Invalid signature scenario was not rejected: "+expectedReason);
        }
        byte[] ambiguous=(byte[])updatedBytes.Clone();
        var constructor=updated.Evidence.First(e=>e.Name=="CreatureVtable");
        int capture=image.FileOffset(constructor.CodeRva)+constructor.CaptureOffset;
        BitConverter.TryWriteBytes(ambiguous.AsSpan(capture,4),image.ImageBase+constructor.CapturedRva!.Value+4);
        Reject(ambiguous,"ambiguous address for CreatureVtable");
        byte[] changedLayout=(byte[])updatedBytes.Clone();
        var layout=updated.Evidence.First(e=>e.Name=="Hotbar slots and page");
        changedLayout[image.FileOffset(layout.CodeRva)]=0xcc;
        Reject(changedLayout,"member layout could not be verified");
        byte[] invalidRoot=(byte[])updatedBytes.Clone();
        var scene=updated.Evidence.First(e=>e.Name=="Scene");
        BitConverter.TryWriteBytes(invalidRoot.AsSpan(image.FileOffset(scene.CodeRva)+scene.CaptureOffset,4),1u);
        Reject(invalidRoot,"invalid address for Scene");
        return new {TimeUtc=DateTime.UtcNow,Passed=true,Baseline=baseline.Profile,Updated=updated.Profile,
            Checks=new[]{"Known baseline reproduced","Previously unsupported patch discovered","Synthetic moved globals rediscovered","Ambiguous vtable rejected","Changed member layout rejected","Invalid root pointer rejected"},
            CodeEvidenceCount=updated.Evidence.Count,ClientFilesModified=false};
    }
}
