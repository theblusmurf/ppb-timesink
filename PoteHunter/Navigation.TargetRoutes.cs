using System.Text.Json;

namespace PoteHunter;

public sealed record SavedNavigationRouteLibrary(int Version,
    Dictionary<string,SavedNavigationRoute?[]> TargetRoutes,SavedNavigationRoute?[] UnassignedRoutes);

public sealed partial class Navigation
{
    readonly Dictionary<string,SavedNavigationRoute?[]> targetRoutes=new(StringComparer.Ordinal);
    SavedNavigationRoute?[] unassignedRoutes=new SavedNavigationRoute?[SavedRouteSlotCount];
    string routeTargetKey="",routeTargetLabel="All targets";
    bool routeReadFailed,legacyRouteFile;
    string? loadedRouteSource;
    public string RouteTargetKey=>routeTargetKey;
    public string RouteTargetLabel=>routeTargetLabel;
    public int UnassignedRouteCount=>unassignedRoutes.Count(route=>route!=null);
    public bool CanAssignUnassignedRoutes=>UnassignedRouteCount>0 && savedRoutes.All(route=>route==null);
    public static string TargetSelectionKey(string? target)=>target?.Trim().ToUpperInvariant() ?? "";

    public bool SelectTargetSelection(string? target,string? path=null)
    {
        string key=TargetSelectionKey(target);
        if(key!=routeTargetKey)
        {
            bool wasRecording=recording;
            Clear();hasPosition=false;
            recordingCancelled=wasRecording;
            if(wasRecording)Status="Route recording cancelled after target selection changed";
        }
        routeTargetKey=key;routeTargetLabel=string.IsNullOrWhiteSpace(target)?"All targets":target.Trim();
        return LoadSavedRoutes(path);
    }

    public bool LoadSavedRoutes(string? path=null)
    {
        Array.Clear(savedRoutes);targetRoutes.Clear();Array.Clear(unassignedRoutes);
        routeReadFailed=false;legacyRouteFile=false;
        try
        {
            string source=path??DefaultSavedRoutesPath;
            if(path==null && !File.Exists(source))source=DefaultSavedRoutePath;
            loadedRouteSource=source;
            if(!File.Exists(source))return false;
            string json=File.ReadAllText(source);
            using var doc=JsonDocument.Parse(json);
            if(doc.RootElement.ValueKind!=JsonValueKind.Object)throw new JsonException("Invalid route document");
            if(doc.RootElement.TryGetProperty("Version",out _))
            {
                var library=JsonSerializer.Deserialize<SavedNavigationRouteLibrary>(json);
                if(library is not {Version:2,TargetRoutes:not null,UnassignedRoutes:not null})throw new JsonException("Unsupported route library");
                foreach(var entry in library.TargetRoutes)
                {
                    string key=TargetSelectionKey(entry.Key);
                    if(!targetRoutes.TryAdd(key,ValidatedSlots(entry.Value)))throw new JsonException("Duplicate target route selection");
                }
                unassignedRoutes=ValidatedSlots(library.UnassignedRoutes);
            }
            else
            {
                if(!doc.RootElement.TryGetProperty("Routes",out _) && !doc.RootElement.TryGetProperty("Zone",out _))
                    throw new JsonException("Unknown legacy route shape");
                // Do not guess the target of routes saved by older releases.
                // They remain available for an explicit assignment in Navigation.
                var routes=doc.RootElement.TryGetProperty("Routes",out _)
                    ? JsonSerializer.Deserialize<SavedNavigationRouteSet>(json)?.Routes
                    : new[]{JsonSerializer.Deserialize<SavedNavigationRoute>(json)};
                unassignedRoutes=ValidatedSlots(routes);legacyRouteFile=true;
            }
            if(targetRoutes.TryGetValue(routeTargetKey,out var selected))Array.Copy(selected,savedRoutes,SavedRouteSlotCount);
            return savedRoutes.Any(route=>route!=null);
        }
        catch(Exception ex) when(ex is JsonException or IOException or UnauthorizedAccessException)
        {
            routeReadFailed=true;Array.Clear(savedRoutes);targetRoutes.Clear();Array.Clear(unassignedRoutes);
            Status="Saved route file could not be read; existing file was preserved";return false;
        }
    }

    static SavedNavigationRoute?[] ValidatedSlots(SavedNavigationRoute?[]? input)
    {
        var slots=new SavedNavigationRoute?[SavedRouteSlotCount];
        if(input==null)return slots;
        for(int slot=0;slot<Math.Min(slots.Length,input.Length);slot++)
        {
            var loaded=input[slot];
            if(loaded is null || !loaded.Anchor.Finite || !double.IsFinite(loaded.Heading) ||
                loaded.Points is not {Length:>0} || loaded.Points.Any(point=>!point.Finite))continue;
            var points=NormalizeRoute(loaded.Points,loaded.Anchor);
            if(points.Count>0)slots[slot]=loaded with {Points=points.ToArray(),Character=loaded.Character?.Trim()??"",
                Height=FiniteOrZero(loaded.Height),HuntRadius=PositiveOrZero(loaded.HuntRadius),
                RevivalDelaySeconds=Math.Clamp(loaded.RevivalDelaySeconds,0,600)};
        }
        return slots;
    }

    bool ReloadRoutesForWrite(string? path)
    {
        LoadSavedRoutes(path);
        return !routeReadFailed;
    }

    bool PersistTargetRoutes(string? path)
    {
        if(routeReadFailed)return false;
        try
        {
            string destination=path??DefaultSavedRoutesPath;
            if(legacyRouteFile && loadedRouteSource!=null && File.Exists(loadedRouteSource))
                File.Copy(loadedRouteSource,loadedRouteSource+".before-target-routes-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff")+".bak",false);
            var next=new Dictionary<string,SavedNavigationRoute?[]>(targetRoutes,StringComparer.Ordinal)
                {[routeTargetKey]=savedRoutes.ToArray()};
            File.WriteAllText(destination+".tmp",JsonSerializer.Serialize(new SavedNavigationRouteLibrary(2,next,unassignedRoutes),new JsonSerializerOptions{WriteIndented=true}));
            File.Move(destination+".tmp",destination,true);
            targetRoutes[routeTargetKey]=savedRoutes.ToArray();legacyRouteFile=false;
            return true;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {Status="Saved route file could not be written; existing routes were preserved";return false;}
    }

    public bool AssignUnassignedRoutes(string? path=null)
    {
        if(recording || !ReloadRoutesForWrite(path) || !CanAssignUnassignedRoutes)return false;
        var previous=unassignedRoutes;Array.Copy(previous,savedRoutes,SavedRouteSlotCount);
        unassignedRoutes=new SavedNavigationRoute?[SavedRouteSlotCount];
        if(PersistTargetRoutes(path))return true;
        unassignedRoutes=previous;Array.Clear(savedRoutes);return false;
    }
}
