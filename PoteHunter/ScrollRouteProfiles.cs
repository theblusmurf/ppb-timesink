using System.Security.Cryptography;
using System.Text.Json;

namespace PoteHunter;

internal sealed record NamedScrollRoute(string Id,string Name,TeleporterProfile Calibration)
{
    public override string ToString()=>Name;
}
internal sealed record ScrollRouteAssignment(string TargetSelection,int Slot,string ProfileId);

// Calibrations are stored once; route assignments contain only a stable ID.
// Reading old captures never rewrites them. Reusing/renaming an old capture
// materializes a snapshot, so later legacy edits cannot change shared routes.
internal static class ScrollRouteProfiles
{
    internal const int MaximumProfiles=32,MaximumAssignments=128,MaximumBytes=24_000_000;
    static readonly object Gate=new();
    internal static string PathName=>Path.Combine(AppContext.BaseDirectory,"scroll-route-profiles.json");
    internal sealed record Registry(int Version,NamedScrollRoute[] Profiles,ScrollRouteAssignment[] Assignments);
    static string FileName(string? path)=>Path.GetFullPath(path??PathName);
    static bool IdValid(string? id)=>id is {Length:>0 and <=80} && id.All(c=>char.IsAsciiLetterOrDigit(c)||c=='-');
    static void NameValid(string? name)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Length>80||name!=name.Trim()||name.Any(char.IsControl))
            throw new InvalidOperationException("Use a scroll profile name of 1–80 visible characters.");
    }
    static void ScopeValid(string? target,int slot)
    {
        if(target==null||target.Length>256||slot is <0 or >=Navigation.SavedRouteSlotCount)
            throw new InvalidOperationException("Select a valid target and saved route slot for the scroll profile.");
    }
    static bool SameContext(TeleporterProfile a,TeleporterProfile b)=>a.CharacterId==b.CharacterId && a.Character==b.Character &&
        a.ClientHash.Equals(b.ClientHash,StringComparison.OrdinalIgnoreCase)&&a.Width==b.Width&&a.Height==b.Height;
    static void Validate(Registry registry)
    {
        if(registry is not {Version:1,Profiles:not null,Assignments:not null} || registry.Profiles.Length>MaximumProfiles ||
            registry.Assignments.Length>MaximumAssignments)
            throw new InvalidOperationException("Scroll profile format or count is invalid; the existing file was preserved.");
        var ids=new HashSet<string>(StringComparer.Ordinal);
        var names=new HashSet<(uint,string,string,int,int,string)>();
        foreach(var profile in registry.Profiles)
        {
            if(profile==null||!IdValid(profile.Id)||!ids.Add(profile.Id)||profile.Calibration?.Scroll==null)
                throw new InvalidOperationException("Scroll profiles contain an incomplete or duplicate identity; the existing file was preserved.");
            NameValid(profile.Name);var c=profile.Calibration;c.Validate(c.ClientHash,new(c.Width,c.Height));
            if(!names.Add((c.CharacterId,c.Character,c.ClientHash.ToUpperInvariant(),c.Width,c.Height,profile.Name.ToUpperInvariant())))
                throw new InvalidOperationException("Compatible scroll profiles have duplicate names; the existing file was preserved.");
        }
        var scopes=new HashSet<(string,int)>();
        foreach(var assignment in registry.Assignments)
        {
            if(assignment==null)throw new InvalidOperationException("Scroll route assignment is incomplete.");
            ScopeValid(assignment.TargetSelection,assignment.Slot);
            if(!ids.Contains(assignment.ProfileId)||!scopes.Add((Navigation.TargetSelectionKey(assignment.TargetSelection),assignment.Slot)))
                throw new InvalidOperationException("Scroll route assignments are duplicate or reference a missing profile; the existing file was preserved.");
        }
    }
    static Registry Read(string file)
    {
        if(!File.Exists(file))return new(1,[],[]);
        if(new FileInfo(file).Length>MaximumBytes)throw new InvalidOperationException("Scroll profile file is too large; it was preserved.");
        Registry? value;
        try{value=JsonSerializer.Deserialize<Registry>(File.ReadAllText(file));}
        catch(JsonException){throw new InvalidOperationException("Scroll profiles could not be read; the existing file was preserved.");}
        if(value==null)throw new InvalidOperationException("Scroll profile file is empty or invalid; it was preserved.");
        Validate(value);return value;
    }
    static void Write(string file,Registry value)
    {
        Validate(value);var bytes=JsonSerializer.SerializeToUtf8Bytes(value);
        if(bytes.Length>MaximumBytes)throw new InvalidOperationException("Scroll profile file is too large; previous profiles were preserved.");
        string temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {stream.Write(bytes);stream.Flush(true);}
            File.Move(temporary,file,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    static string LegacyId(TeleporterProfile profile)=>"legacy-"+Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(profile))).ToLowerInvariant();
    static string ShortName(string name)=>new string(name.Where(c=>!char.IsControl(c)).ToArray()).Trim() is {Length:>0} clean ? clean[..Math.Min(70,clean.Length)] : "Scroll route";
    static string UniqueName(string name,TeleporterProfile calibration,IEnumerable<NamedScrollRoute> profiles)
    {
        string stem=ShortName(name),candidate=stem;int suffix=2;
        var used=profiles.Where(p=>SameContext(p.Calibration,calibration)).Select(p=>p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        while(used.Contains(candidate))candidate=stem+" ("+(suffix++)+")";
        return candidate;
    }
    internal static NamedScrollRoute[] Available(string? path=null,string? legacyPath=null)
    {
        NamedScrollRoute[] saved;lock(Gate)saved=Read(FileName(path)).Profiles.ToArray();
        // Never hold both registry locks: UI reads and assignments cannot deadlock.
        var legacy=TeleporterProfile.LegacyProfiles(legacyPath).Where(p=>p.Scroll!=null).Select(p=>new NamedScrollRoute(LegacyId(p),
            ShortName(p.Destination+" · "+(string.IsNullOrWhiteSpace(p.TargetSelection)?"All targets":p.TargetSelection)+" / "+(p.DestinationSlot+1)),p));
        return saved.Concat(legacy).GroupBy(p=>p.Id,StringComparer.Ordinal).Select(g=>g.First())
            .OrderBy(p=>p.Name,StringComparer.OrdinalIgnoreCase).ThenBy(p=>p.Id,StringComparer.Ordinal).ToArray();
    }
    internal static bool SelectableFor(NamedScrollRoute entry,uint characterId,string character,Size size,int zone)
    {
        var c=entry.Calibration;
        return c.Scroll!=null && c.CharacterId==characterId && c.Character==character &&
            c.Width==size.Width && c.Height==size.Height && c.Landing.Zone==zone;
    }
    internal static string? AssignedId(string? target,int slot,string? path=null,string? legacyPath=null)
    {
        ScopeValid(target??"",slot);string key=Navigation.TargetSelectionKey(target);
        lock(Gate)
        {
            var assigned=Read(FileName(path)).Assignments.FirstOrDefault(a=>Navigation.TargetSelectionKey(a.TargetSelection)==key&&a.Slot==slot);
            if(assigned!=null)return assigned.ProfileId;
        }
        var legacy=TeleporterProfile.LegacyProfiles(legacyPath).FirstOrDefault(p=>p.Scroll!=null&&Navigation.TargetSelectionKey(p.TargetSelection)==key&&p.DestinationSlot==slot);
        return legacy==null?null:LegacyId(legacy);
    }
    internal static TeleporterProfile? Resolve(string hash,Size size,string? target,int slot,string? path=null)
    {
        ScopeValid(target??"",slot);lock(Gate)
        {
            var registry=Read(FileName(path));string key=Navigation.TargetSelectionKey(target);
            var binding=registry.Assignments.FirstOrDefault(a=>Navigation.TargetSelectionKey(a.TargetSelection)==key&&a.Slot==slot);
            if(binding==null)return null;
            var profile=registry.Profiles.Single(p=>p.Id==binding.ProfileId).Calibration with{TargetSelection=target??"",DestinationSlot=slot};
            profile.ValidateForUse(hash,size);return profile;
        }
    }
    internal static NamedScrollRoute Create(TeleporterProfile calibration,string name,string? path=null)
    {
        if(calibration.Scroll==null)throw new InvalidOperationException("A reusable profile requires a captured Teleport Scroll.");
        calibration.Validate(calibration.ClientHash,new(calibration.Width,calibration.Height));
        lock(Gate)
        {
            string file=FileName(path);var current=Read(file);
            var entry=new NamedScrollRoute(Guid.NewGuid().ToString("N"),UniqueName(name,calibration,current.Profiles),calibration);
            Write(file,current with{Profiles=[..current.Profiles,entry]});return entry;
        }
    }
    internal static void Assign(string id,string? target,int slot,string? path=null,string? legacyPath=null)
    {
        ScopeValid(target??"",slot);
        var candidate=Available(path,legacyPath).SingleOrDefault(p=>p.Id==id)??throw new InvalidOperationException("Select an existing scroll profile.");
        lock(Gate)
        {
            string file=FileName(path);var current=Read(file);var entries=current.Profiles;
            if(!entries.Any(p=>p.Id==id))entries=[..entries,candidate with{Name=UniqueName(candidate.Name,candidate.Calibration,entries)}];
            string key=Navigation.TargetSelectionKey(target);
            var assignments=current.Assignments.Where(a=>Navigation.TargetSelectionKey(a.TargetSelection)!=key||a.Slot!=slot)
                .Append(new(key,slot,id)).ToArray();
            Write(file,new(1,entries,assignments));
        }
    }
    internal static void Rename(string id,string name,string? path=null,string? legacyPath=null)
    {
        name=name?.Trim()??"";NameValid(name);
        var candidate=Available(path,legacyPath).SingleOrDefault(p=>p.Id==id)??throw new InvalidOperationException("Select an existing scroll profile.");
        lock(Gate)
        {
            string file=FileName(path);var current=Read(file);
            if(current.Profiles.Any(p=>p.Id!=id&&SameContext(p.Calibration,candidate.Calibration)&&p.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Another compatible scroll profile already uses that name.");
            Write(file,current with{Profiles=[..current.Profiles.Where(p=>p.Id!=id),candidate with{Name=name}]});
        }
    }
}
