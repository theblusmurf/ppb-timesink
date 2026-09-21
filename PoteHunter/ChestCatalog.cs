using System.Text.Json;

namespace PoteHunter;

public sealed record TrackedChest(int Zone,uint Id,Vec Position,string Label,DateTime FirstSeenUtc,DateTime LastSeenUtc);

public sealed class ChestCatalog
{
    readonly Dictionary<(int Zone,uint Id),TrackedChest> chests=[];
    readonly string path;

    public ChestCatalog(string? storagePath=null)
    {
        path=storagePath??Path.Combine(AppContext.BaseDirectory,"known-chests.json");
        try
        {
            foreach(var chest in JsonSerializer.Deserialize<TrackedChest[]>(File.ReadAllText(path))??[])
                if(chest.Zone>0&&chest.Id!=0&&chest.Position.Finite)chests[(chest.Zone,chest.Id)]=chest;
        }
        catch(FileNotFoundException) { }
        catch(JsonException) { }
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
        if(changed)Save();
    }

    void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,JsonSerializer.Serialize(chests.Values.OrderBy(chest=>chest.Zone).ThenBy(chest=>chest.Id),new JsonSerializerOptions {WriteIndented=true}));
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
