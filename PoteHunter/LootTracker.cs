namespace PoteHunter;

public sealed record LootTrackerItemSummary(string Name,int Count);
public sealed record LootTrackerSourceSummary(string Source,int Kills,int Drops,IReadOnlyList<LootTrackerItemSummary> Items);
public sealed record LootTrackerDropSummary(string Source,string Name,Vec Position,DateTime SeenUtc);
public sealed record LootTrackerSnapshot(int Zone,int PendingKills,IReadOnlyList<LootTrackerSourceSummary> Sources,IReadOnlyList<LootTrackerDropSummary> RecentDrops,IReadOnlyList<LootTrackerItemSummary> TrackedLoot);

/// <summary>Attributes newly observed ground items to the nearby tracked farm target that died most recently.</summary>
public sealed class LootTracker
{
    const double DropAttributionRadius=12;
    static readonly TimeSpan DropAttributionWindow=TimeSpan.FromSeconds(20);
    static readonly string[] SourceOrder=["Mimic","Tribal","Pulkhan","Tower"];
    static readonly string[] TrackedLootOrder=["Silvin","Mithril","Iternium","Fehu","Gold","Gems"];
    readonly object gate=new();
    readonly Dictionary<string,SourceState> sources=SourceOrder.ToDictionary(name=>name,_=>new SourceState());
    readonly Dictionary<string,int> trackedLoot=TrackedLootOrder.ToDictionary(name=>name,_=>0,StringComparer.OrdinalIgnoreCase);
    readonly List<PendingKill> pending=[];
    readonly List<LootTrackerDropSummary> recent=[];
    readonly HashSet<(uint,uint)> seen=[];
    int zone;
    bool initialized;

    sealed class SourceState
    {
        public int Kills;
        public int Drops;
        public readonly Dictionary<string,int> Items=new(StringComparer.OrdinalIgnoreCase);
    }
    sealed record PendingKill(string Source,uint Id,uint Generation,Vec Position,DateTime SeenUtc,int Zone);

    public static string? SourceFor(Entity entity) => (entity.Id & 0xffff) switch
    {
        5971 => "Mimic",
        5972 => "Tribal",
        5973 => "Pulkhan",
        5974 => "Tower",
        _ => null
    };

    /// <summary>Maps the requested named valuables to one stable counter even when the client adds a quantity or suffix.</summary>
    public static string? TrackedLootFor(GroundItem item)
    {
        string text=$"{item.Name} {item.Description}";
        if(text.Contains("silvin",StringComparison.OrdinalIgnoreCase) || text.Contains("silvein",StringComparison.OrdinalIgnoreCase))return "Silvin";
        if(text.Contains("mithril",StringComparison.OrdinalIgnoreCase) || text.Contains("mitheil",StringComparison.OrdinalIgnoreCase))return "Mithril";
        if(text.Contains("iternium",StringComparison.OrdinalIgnoreCase))return "Iternium";
        if(text.Contains("fehu",StringComparison.OrdinalIgnoreCase))return "Fehu";
        if(text.Contains("gold",StringComparison.OrdinalIgnoreCase))return "Gold";
        if(text.Contains("gem",StringComparison.OrdinalIgnoreCase))return "Gems";
        return null;
    }

    public void Reset()
    {
        lock(gate) ResetLocked(zone);
    }

    public void ObserveZone(int newZone)
    {
        lock(gate)
        {
            if(zone==newZone)return;
            ResetLocked(newZone);
        }
    }

    public void RecordKill(Entity target,Vec position,int targetZone)
    {
        string? source=SourceFor(target);
        if(source==null || !position.Finite)return;
        lock(gate)
        {
            if(zone!=targetZone)ResetLocked(targetZone);
            DateTime now=DateTime.UtcNow;
            if(pending.Any(k=>k.Source==source && k.Id==target.Id && k.Generation==target.Generation && now-k.SeenUtc<TimeSpan.FromSeconds(5)))return;
            sources[source].Kills++;
            pending.Add(new PendingKill(source,target.Id,target.Generation,position,now,targetZone));
            PrunePending(now);
        }
    }

    public void ObserveDrops(IEnumerable<GroundItem> drops,int currentZone)
    {
        ArgumentNullException.ThrowIfNull(drops);
        var current=new Dictionary<(uint,uint),GroundItem>();
        foreach(var item in drops)
        {
            if(!item.Position.Finite)continue;
            current.TryAdd((item.KeyA,item.KeyB),item);
        }
        lock(gate)
        {
            if(zone!=currentZone)ResetLocked(currentZone);
            if(!initialized)
            {
                seen.Clear();seen.UnionWith(current.Keys);initialized=true;
                return;
            }
            DateTime now=DateTime.UtcNow;
            PrunePending(now);
            foreach(var item in current.Values)
            {
                var key=(item.KeyA,item.KeyB);
                if(!seen.Add(key))continue;
                PendingKill? match=pending.Where(k=>k.Zone==currentZone && now-k.SeenUtc<=DropAttributionWindow && (item.Position-k.Position).Length<=DropAttributionRadius)
                    .OrderBy(k=>(item.Position-k.Position).Length).ThenByDescending(k=>k.SeenUtc).FirstOrDefault();
                if(match==null)continue;
                string itemName=string.IsNullOrWhiteSpace(item.Name)?"Unknown item":item.Name.Trim();
                SourceState state=sources[match.Source];state.Drops++;
                state.Items[itemName]=state.Items.GetValueOrDefault(itemName)+1;
                string? trackedName=TrackedLootFor(item);
                if(trackedName!=null)trackedLoot[trackedName]=trackedLoot.GetValueOrDefault(trackedName)+1;
                recent.Insert(0,new LootTrackerDropSummary(match.Source,itemName,item.Position,now));
                if(recent.Count>12)recent.RemoveRange(12,recent.Count-12);
            }
            seen.RemoveWhere(key=>!current.ContainsKey(key));
        }
    }

    public LootTrackerSnapshot Snapshot()
    {
        lock(gate)
        {
            return new LootTrackerSnapshot(zone,pending.Count,
                SourceOrder.Select(source=>
                {
                    SourceState state=sources[source];
                    return new LootTrackerSourceSummary(source,state.Kills,state.Drops,
                        state.Items.OrderByDescending(item=>item.Value).ThenBy(item=>item.Key,StringComparer.OrdinalIgnoreCase)
                            .Take(5).Select(item=>new LootTrackerItemSummary(item.Key,item.Value)).ToArray());
                }).ToArray(),recent.ToArray(),TrackedLootOrder.Select(name=>new LootTrackerItemSummary(name,trackedLoot[name])).ToArray());
        }
    }

    void PrunePending(DateTime now) => pending.RemoveAll(k=>now-k.SeenUtc>DropAttributionWindow);

    void ResetLocked(int newZone)
    {
        zone=newZone;initialized=false;seen.Clear();pending.Clear();recent.Clear();
        foreach(SourceState state in sources.Values){state.Kills=0;state.Drops=0;state.Items.Clear();}
        foreach(string name in TrackedLootOrder)trackedLoot[name]=0;
    }

    public static void SelfTest()
    {
        var tracker=new LootTracker();
        var existing=new GroundItem(1,1,1,"Existing",new(0,0),0);
        tracker.ObserveDrops([existing],8);
        var mimic=new Entity(10,0x80001753,"Mimic",new(0,0),0,Generation:4,Model:"MON_mimic.GCMDS");
        if(SourceFor(mimic)!="Mimic" || SourceFor(mimic with {Id=0x80001754})!="Tribal" || SourceFor(mimic with {Id=0x80001755})!="Pulkhan" || SourceFor(mimic with {Id=0x80001756})!="Tower")
            throw new Exception("Tracked loot source classification failed.");
        tracker.RecordKill(mimic,new(0,0),8);
        tracker.ObserveDrops([existing,new GroundItem(2,2,2,"Frost potion",new(.5,.5),0)],8);
        var snap=tracker.Snapshot();var row=snap.Sources.First(source=>source.Source=="Mimic");
        if(row.Kills!=1 || row.Drops!=1 || row.Items.FirstOrDefault()?.Name!="Frost potion")throw new Exception("Tracked loot attribution failed.");
        tracker.ObserveDrops([existing,
            new GroundItem(3,3,3,"Silvin",new(.5,.5),0),
            new GroundItem(4,4,4,"Mithril shard",new(.6,.5),0),
            new GroundItem(5,5,5,"Rare ore",new(.7,.5),0,"Iternium ore"),
            new GroundItem(6,6,6,"Fehu rune",new(.8,.5),0),
            new GroundItem(7,7,7,"Gold",new(.9,.5),0),
            new GroundItem(8,8,8,"Gemstone",new(1,.5),0)],8);
        snap=tracker.Snapshot();
        if(snap.Sources.First(source=>source.Source=="Mimic").Drops!=7)throw new Exception("Tracked item drops were not attributed.");
        if(snap.TrackedLoot.Any(item=>item.Count!=1) || snap.TrackedLoot.Count!=6)throw new Exception("Named valuable counters were not recorded.");
        if(TrackedLootFor(new GroundItem(10,10,10,"Silvein",new(.5,.5),0))!="Silvin" || TrackedLootFor(new GroundItem(11,11,11,"Mitheil",new(.5,.5),0))!="Mithril")
            throw new Exception("Legacy valuable aliases did not normalize to the corrected labels.");
        tracker.ObserveDrops([existing,new GroundItem(9,9,9,"Distant",new(100,100),0)],8);
        if(tracker.Snapshot().Sources.First(source=>source.Source=="Mimic").Drops!=7)throw new Exception("Distant loot was attributed to a tracked target.");
        tracker.ObserveZone(9);
        if(tracker.Snapshot().Sources.Any(source=>source.Kills!=0 || source.Drops!=0))throw new Exception("Zone changes did not reset tracked loot.");
    }
}
