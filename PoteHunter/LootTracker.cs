using System.Globalization;
using System.Text.RegularExpressions;

namespace PoteHunter;

public sealed record LootTrackerItemSummary(string Name,long Count);
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
    // The client exposes currency as negative item ids with a fallback name such as
    // "Special drop (115)".  Gem records use their actual names (for example
    // Emerald and BlackMoon), so matching only the words "gold" and "gem" misses
    // the labels that are present in the live loot list.
    static readonly string[] GoldTokens=["gold","coin","currency","money"];
    static readonly string[] GemTokens=["gem","emerald","blackmoon","black moon","diamond","sapphire","ruby","topaz","amethyst","opal","pearl","onyx","moonstone"];
    static readonly Regex SpecialDropAmountPattern=new(@"\bspecial\s+drop\s*\(\s*(?<amount>[\d,]+)\s*\)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled);
    static readonly Regex GoldAmountPattern=new(@"(?:(?<before>[\d,]+)\s*(?:gold|coins?|currency|money)\b|(?:gold|coins?|currency|money)\D{0,8}(?<after>[\d,]+))",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled);
    readonly object gate=new();
    readonly Dictionary<string,SourceState> sources=SourceOrder.ToDictionary(name=>name,_=>new SourceState());
    readonly Dictionary<string,long> trackedLoot=TrackedLootOrder.ToDictionary(name=>name,_=>0L,StringComparer.OrdinalIgnoreCase);
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
        if(GemTokens.Any(token=>text.Contains(token,StringComparison.OrdinalIgnoreCase)))return "Gems";
        if(text.Contains("special drop",StringComparison.OrdinalIgnoreCase) || GoldTokens.Any(token=>text.Contains(token,StringComparison.OrdinalIgnoreCase)))return "Gold";
        return null;
    }

    /// <summary>Returns the currency represented by a drop. Special-drop ids encode the amount in their low 31 bits.</summary>
    public static long GoldAmountFor(GroundItem item)
    {
        string text=$"{item.Name} {item.Description}";
        if(item.TypeId<0 && text.Contains("special drop",StringComparison.OrdinalIgnoreCase))
        {
            uint encoded=unchecked((uint)item.TypeId)&0x7fffffffu;
            if(encoded>0)return encoded;
        }
        Match special=SpecialDropAmountPattern.Match(text);
        Match named=GoldAmountPattern.Match(text);
        string raw=special.Success?special.Groups["amount"].Value:
            named.Success?(named.Groups["before"].Success?named.Groups["before"].Value:named.Groups["after"].Value):"";
        return long.TryParse(raw.Replace(",",""),NumberStyles.None,CultureInfo.InvariantCulture,out long amount) && amount>0?amount:1;
    }

    public void Reset()
    {
        lock(gate) ResetLocked(zone,resetSession:true);
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
                if(trackedName!=null)
                {
                    long amount=trackedName=="Gold"?GoldAmountFor(item):1;
                    trackedLoot[trackedName]=trackedLoot.GetValueOrDefault(trackedName)+amount;
                }
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

    void ResetLocked(int newZone,bool resetSession=false)
    {
        zone=newZone;initialized=false;seen.Clear();pending.Clear();recent.Clear();
        foreach(SourceState state in sources.Values){state.Kills=0;state.Drops=0;state.Items.Clear();}
        if(resetSession)foreach(string name in TrackedLootOrder)trackedLoot[name]=0;
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
        var encodedGold=new GroundItem(12,12,unchecked((int)(0x80000000u|115u)),"Special drop (115)",new(.5,.5),0);
        if(TrackedLootFor(encodedGold)!="Gold" || GoldAmountFor(encodedGold)!=115 ||
            TrackedLootFor(new GroundItem(13,13,13,"Gold coin",new(.5,.5),0))!="Gold" ||
            GoldAmountFor(new GroundItem(16,16,16,"Gold 1,250",new(.5,.5),0))!=1250 ||
            GoldAmountFor(new GroundItem(17,17,17,"8 gold",new(.5,.5),0))!=8 ||
            TrackedLootFor(new GroundItem(14,14,14,"Emerald",new(.5,.5),0))!="Gems" ||
            TrackedLootFor(new GroundItem(15,15,15,"BlackMoon",new(.5,.5),0))!="Gems")
            throw new Exception("Client currency and gem labels did not normalize to the requested counters.");
        var amountTracker=new LootTracker();amountTracker.ObserveDrops([existing],8);amountTracker.RecordKill(mimic,new(0,0),8);amountTracker.ObserveDrops([existing,encodedGold],8);
        if(amountTracker.Snapshot().TrackedLoot.First(item=>item.Name=="Gold").Count!=115)
            throw new Exception("Gold amount was not accumulated from the special-drop id.");
        tracker.ObserveDrops([existing,new GroundItem(9,9,9,"Distant",new(100,100),0)],8);
        if(tracker.Snapshot().Sources.First(source=>source.Source=="Mimic").Drops!=7)throw new Exception("Distant loot was attributed to a tracked target.");
        tracker.ObserveZone(9);
        snap=tracker.Snapshot();
        if(snap.Sources.Any(source=>source.Kills!=0 || source.Drops!=0))throw new Exception("Zone changes did not reset zone attribution.");
        if(snap.TrackedLoot.Any(item=>item.Count!=1))throw new Exception("Zone changes erased session loot totals.");
        tracker.Reset();
        if(tracker.Snapshot().TrackedLoot.Any(item=>item.Count!=0))throw new Exception("Explicit tracker reset did not clear session loot totals.");
    }
}

