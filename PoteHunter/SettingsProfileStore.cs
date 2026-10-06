using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PoteHunter;

internal sealed record SettingsProfileDocument(int SchemaVersion,string Application,string Name,DateTimeOffset UpdatedUtc,Options Settings);
internal sealed class SettingsProfileStore
{
    const int Schema=2;
    const long MaximumBytes=1_000_000;
    readonly string directory;
    static readonly JsonSerializerOptions JsonOptions=new(){WriteIndented=true};
    // Character identity, machine-specific display choices, and global keys never travel with a profile.
    internal static readonly string[] LocalFields=["Player","ShowNavigationOverlay","ShowNavigationRoutes","ShowRouteOverlay",
        "ShowSentinelRadar","SentinelSoundEnabled","SentinelRange","SentinelVolumePercent","SentinelRadarPositionSaved","SentinelRadarX","SentinelRadarY",
        "Radar3D","RouteOverlay3D","Overlay3DTopView","NavigationOverlaySize","NavigationViewRadius","Navigation3D","Navigation3DMap",
        "Navigation3DTerrain","Navigation3DObjects","Navigation3DRoutes","Navigation3DAnchors","Navigation3DMapOpacity",
        "ShowLootTrackerOverlay","LootTrackerDesign","LootTrackerDesignVersion","LootTrackerScalePercent","LootTrackerBackgroundOpacityPercent",
        "LootTrackerOverlayX","LootTrackerOverlayY","ShowTreasureChestMarkers","ItemGradeHotkey","ItemGradeScalePercent"];
    public SettingsProfileStore(string? root=null)=>directory=root??Path.Combine(AppContext.BaseDirectory,"settings-profiles");
    static string Name(string name)
    {
        name=(name??"").Trim();
        if(name.Length is <1 or >48||name.Any(char.IsControl))throw new InvalidDataException("Use a profile name with 1–48 printable characters.");
        return name;
    }
    string PathFor(string name)=>Path.Combine(directory,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Name(name).ToUpperInvariant())))+".json");
    static Options Clone(Options options)=>JsonSerializer.Deserialize<Options>(JsonSerializer.Serialize(options))!;
    static Options Portable(Options source)
    {
        var result=Clone(source);var defaults=new Options();
        foreach(string name in LocalFields){var property=typeof(Options).GetProperty(name)!;property.SetValue(result,property.GetValue(defaults));}
        return result;
    }
    internal static Options KeepLocal(Options profile,Options local)
    {
        var result=Clone(profile);
        foreach(string name in LocalFields){var property=typeof(Options).GetProperty(name)!;property.SetValue(result,property.GetValue(local));}
        return result;
    }
    static void Validate(Options options)
    {
        if(options.Target is null||options.Target.Length>200||options.AllowedDifficulties is null||options.AllowedDifficulties.Length>9||options.AvoidNames is null||options.AvoidNames.Count>100)
            throw new InvalidDataException("The profile contains invalid targets or protection rules.");
        if(options.AllowedDifficulties.Any(x=>!Enum.TryParse<Threat>(x,true,out _)))throw new InvalidDataException("The profile contains an unknown difficulty.");
        Avoidance.Validate(options.AvoidNames);
        if(options.HuntRadius is <5 or >150||options.MeleeRange is <1.5m or >30||options.GroupFollowDistance is <2 or >20||
           options.GroupFollowLimit<options.GroupFollowDistance||options.RevivalDelaySeconds is <0 or >600)
            throw new InvalidDataException("The profile contains invalid hunting or recovery limits.");
        if((options.SkillKeys??"").Length>100||(options.HealingSkillKeys??"").Length>100||(options.HealthConditionKeys??"").Length>100)
            throw new InvalidDataException("The profile contains an oversized skill-key list.");
    }
    static SettingsProfileDocument Parse(string json)
    {
        if(Encoding.UTF8.GetByteCount(json)>MaximumBytes)throw new InvalidDataException("The profile is larger than 1 MB.");
        var node=JsonNode.Parse(json) as JsonObject??throw new InvalidDataException("Invalid profile file.");
        if(node["Settings"] is not JsonObject settings)throw new InvalidDataException("The profile has no settings.");
        foreach(string name in settings.Select(p=>p.Key))
            if(typeof(Options).GetProperty(name)==null)throw new InvalidDataException("This profile uses unsupported setting: "+name);
        var doc=JsonSerializer.Deserialize<SettingsProfileDocument>(json)??throw new InvalidDataException("Invalid profile file.");
        if(doc.SchemaVersion!=Schema||doc.Application!="PlayPoteBot")throw new InvalidDataException("Choose a PlayPoteBot settings profile exported by this version.");
        Validate(doc.Settings);
        return doc with{Name=Name(doc.Name),Settings=Portable(doc.Settings)};
    }
    static string ReadFile(string path)
    {
        if(new FileInfo(path).Length>MaximumBytes)throw new InvalidDataException("The profile is larger than 1 MB.");
        return File.ReadAllText(path);
    }
    static void AtomicWrite(string path,string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string tmp=path+".tmp-"+Guid.NewGuid().ToString("N");
        try{File.WriteAllText(tmp,content);File.Move(tmp,path,true);}finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
    void Write(SettingsProfileDocument doc)
    {
        string path=PathFor(doc.Name);
        if(File.Exists(path))File.Copy(path,path+".backup-"+Guid.NewGuid().ToString("N"));
        AtomicWrite(path,JsonSerializer.Serialize(doc,JsonOptions));
    }
    public string[] List()
    {
        if(!Directory.Exists(directory))return [];
        var names=new List<string>();
        foreach(var path in Directory.EnumerateFiles(directory,"*.json"))
        {
            try{var doc=Parse(ReadFile(path));if(string.Equals(PathFor(doc.Name),path,StringComparison.OrdinalIgnoreCase))names.Add(doc.Name);}
            catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException){}
        }
        return names.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public bool Exists(string name)=>File.Exists(PathFor(name));
    public void Save(string name,Options options){Validate(options);Write(new(Schema,"PlayPoteBot",Name(name),DateTimeOffset.UtcNow,Portable(options)));}
    public SettingsProfileDocument Read(string name)
    {
        var doc=Parse(ReadFile(PathFor(name)));
        if(!doc.Name.Equals(name,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Profile name does not match its file.");
        return doc;
    }
    public Options Materialize(string name,Options local)=>KeepLocal(Read(name).Settings,local);
    public void Export(string name,string path)=>AtomicWrite(path,JsonSerializer.Serialize(Read(name),JsonOptions));
    public SettingsProfileDocument Inspect(string path)=>Parse(ReadFile(path));
    public string Import(string path){var doc=Inspect(path);Write(doc);return doc.Name;}
    public string Activate(string name,string settingsPath,Options local)
    {
        Options result=Materialize(name,local);Validate(result);
        string backup=settingsPath+".backup-"+Guid.NewGuid().ToString("N");
        if(File.Exists(settingsPath))File.Copy(settingsPath,backup);else AtomicWrite(backup,JsonSerializer.Serialize(local,JsonOptions));
        AtomicWrite(settingsPath,JsonSerializer.Serialize(result,JsonOptions));
        return backup;
    }
    public void Delete(string name)
    {
        string path=PathFor(name);if(!File.Exists(path))return;
        File.Move(path,path+".deleted-"+Guid.NewGuid().ToString("N"));
    }
}
