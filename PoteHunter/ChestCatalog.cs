using System.Text.Json;

namespace PoteHunter;

public sealed record TrackedChest(int Zone,uint Id,Vec Position,string Label,DateTime FirstSeenUtc,DateTime LastSeenUtc);

public sealed class ChestCatalog
{
    readonly Dictionary<(int Zone,uint Id),TrackedChest> chests=[];
    readonly string path;
    readonly Func<long> clock;
    bool dirty,loadPending;
    long retryAt;

    public ChestCatalog(string? storagePath=null):this(storagePath??Path.Combine(AppContext.BaseDirectory,"known-chests.json"),()=>Environment.TickCount64) { }

    internal ChestCatalog(string storagePath,Func<long> clock)
    {
        path=Path.GetFullPath(storagePath);
        this.clock=clock??throw new ArgumentNullException(nameof(clock));
        loadPending=!Load();
        if(loadPending)retryAt=clock()+2000;
    }

    bool Load()
    {
        try
        {
            foreach(var chest in JsonSerializer.Deserialize<TrackedChest[]>(File.ReadAllText(path))??[])
                if(chest.Zone>0&&chest.Id!=0&&chest.Position.Finite)
                {
                    var key=(chest.Zone,chest.Id);
                    // Deferred loading must retain observations made while the old cache was inaccessible.
                    if(chests.TryGetValue(key,out var observed))
                        chests[key]=observed with {FirstSeenUtc=chest.FirstSeenUtc<observed.FirstSeenUtc?chest.FirstSeenUtc:observed.FirstSeenUtc};
                    else chests[key]=chest;
                }
        }
        catch(FileNotFoundException) { }
        catch(DirectoryNotFoundException) { }
        catch(JsonException) { }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException)
        { DiagnosticIo.RecordFailure("load chest sightings",path,error);return false; }
        DiagnosticIo.RecordSuccess("load chest sightings");
        return true;
    }

    public IReadOnlyList<TrackedChest> ForZone(int zone) => chests.Values.Where(chest=>chest.Zone==zone).OrderBy(chest=>chest.LastSeenUtc).ToArray();

    public void Observe(int zone,IEnumerable<Entity> visible)
    {
        if(zone<=0)throw new ArgumentOutOfRangeException(nameof(zone));
        bool changed=false;DateTime now=DateTime.UtcNow;
        foreach(var entity in visible.Where(Targeting.IsChest))
        {
            if(!entity.Position.Finite)continue;
            var key=(zone,entity.Id);string label=Targeting.ChestLabel(entity);
            if(chests.TryGetValue(key,out var prior))
            {
                var next=prior with {Position=entity.Position,Label=label,LastSeenUtc=now};
                chests[key]=next;changed|=next.Position!=prior.Position||next.Label!=prior.Label;
            }
            else {chests[key]=new(zone,entity.Id,entity.Position,label,now,now);changed=true;}
        }
        dirty|=changed;
        if((dirty||loadPending)&&clock()>=retryAt)
        {
            if(loadPending)
            {
                loadPending=!Load();
                if(loadPending) {retryAt=clock()+2000;return;}
            }
            if(dirty)Save();
            else retryAt=0;
        }
    }

    void Save()
    {
        string text=JsonSerializer.Serialize(chests.Values.OrderBy(chest=>chest.Zone).ThenBy(chest=>chest.Id),new JsonSerializerOptions {WriteIndented=true});
        if(DiagnosticIo.TryAtomicWrite("save chest sightings",path,text)) {dirty=false;retryAt=0;}
        else retryAt=clock()+2000;
    }

    public static void SelfTest()
    {
        string test=Path.Combine(Path.GetTempPath(),"PoteHunter-chests-"+Guid.NewGuid()+".json");
        try
        {
            var catalog=new ChestCatalog(test);var chest=new Entity(1,0x80000bc0,"Treasure Box",new(4,5),0,Model:"MON_luckybag.GCMDS");
            catalog.Observe(12,[chest]);
            if(catalog.ForZone(12).Count!=1||catalog.ForZone(11).Count!=0)throw new Exception("Chest catalog did not scope a sighting to its zone.");
            var restored=new ChestCatalog(test);if(restored.ForZone(12).Single().Position!=chest.Position)throw new Exception("Chest catalog did not persist a sighting.");
        }
        finally {if(File.Exists(test))File.Delete(test);}
    }
}
