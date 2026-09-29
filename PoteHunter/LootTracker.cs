using System.Globalization;
using System.Text.RegularExpressions;

namespace PoteHunter;

public sealed record LootTrackerItemSummary(string Name,long Count);
public sealed record LootTrackerRateSummary(string Name,double PerHour);
public sealed record LootTrackerSourceSummary(string Source,int Kills,int Drops,IReadOnlyList<LootTrackerItemSummary> Items);
public sealed record LootTrackerDropSummary(string Source,string Name,Vec Position,DateTime SeenUtc);
public sealed record LootTrackerSnapshot(int Zone,int PendingKills,IReadOnlyList<LootTrackerSourceSummary> Sources,IReadOnlyList<LootTrackerDropSummary> RecentDrops,IReadOnlyList<LootTrackerItemSummary> TrackedLoot,DateTime SessionStartedUtc,TimeSpan Elapsed,DateTime RateStartedUtc,TimeSpan RateElapsed,IReadOnlyList<LootTrackerRateSummary> HourlyLoot);

/// <summary>Attributes newly observed ground items to the nearby tracked farm target that died most recently.</summary>
public sealed class LootTracker
{
    const double DropAttributionRadius=12;
    static readonly TimeSpan DropAttributionWindow=TimeSpan.FromSeconds(20);
    static readonly string[] SourceOrder=["Mimic","Tribal","Pulkhan","Tower"];
    static readonly string[] TrackedLootOrder=["Silvin","Mithril","Iternium","Fehu","Gold","Gems"];
    // The client exposes currency as negative item ids with a display name such as
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
    readonly HashSet<LootIdentity> known=[];
    readonly Dictionary<LootIdentity,DateTime> unmatched=[];
    readonly DateTime sessionStartedUtc;
    readonly Dictionary<string,long> rateBaseline=TrackedLootOrder.ToDictionary(name=>name,_=>0L,StringComparer.OrdinalIgnoreCase);
    DateTime rateStartedUtc;
    TimeSpan activeRateElapsed;
    DateTime? activeSinceUtc;
    bool rateActive;
    int zone;
    bool initialized;

    public LootTracker()
    {
        sessionStartedUtc=DateTime.UtcNow;
        rateStartedUtc=sessionStartedUtc;
    }

    sealed class SourceState
    {
        public int Kills;
        public int Drops;
        public readonly Dictionary<string,int> Items=new(StringComparer.OrdinalIgnoreCase);
    }
    sealed record PendingKill(string Source,uint Id,uint Generation,Vec Position,DateTime SeenUtc,int Zone);
    // KeyA/KeyB identify the ground-pile object.  TypeId is the pile's current
    // item value and can change while the same pile is being refreshed (gold
    // piles are the common example), so it must not make a second drop.
    readonly record struct LootIdentity(int Zone,uint KeyA,uint KeyB);

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
        // Only a negative special-drop type is a currency pile.  A textual
        // fallback alone is not enough: unidentified item records can also be
        // surfaced as "Special drop (...)" and must remain unclassified.
        if(item.TypeId<0 && text.Contains("special drop",StringComparison.OrdinalIgnoreCase))return "Gold";
        if(GoldTokens.Any(token=>text.Contains(token,StringComparison.OrdinalIgnoreCase)))return "Gold";
        return null;
    }

    /// <summary>Returns the currency represented by a drop. The displayed amount is preferred, with the encoded id as a fallback.</summary>
    public static long GoldAmountFor(GroundItem item)
    {
        string text=$"{item.Name} {item.Description}";
        Match special=SpecialDropAmountPattern.Match(text);
        if(special.Success && long.TryParse(special.Groups["amount"].Value.Replace(",",""),NumberStyles.None,CultureInfo.InvariantCulture,out long displayedAmount) && displayedAmount>0)
            return displayedAmount;
        if(item.TypeId<0)
        {
            uint encoded=unchecked((uint)item.TypeId)&0x7fffffffu;
            if(encoded>0)return encoded;
        }
        Match named=GoldAmountPattern.Match(text);
        string raw=special.Success?special.Groups["amount"].Value:
            named.Success?(named.Groups["before"].Success?named.Groups["before"].Value:named.Groups["after"].Value):"";
        return long.TryParse(raw.Replace(",",""),NumberStyles.None,CultureInfo.InvariantCulture,out long amount) && amount>0?amount:1;
    }

    /// <summary>Marks whether the bot is actively farming so GPH excludes idle and disconnected time.</summary>
    public void ObserveActivity(bool active)
    {
        lock(gate)
        {
            DateTime now=DateTime.UtcNow;
            AccumulateActiveLocked(now);
            if(rateActive==active)return;
            rateActive=active;
            activeSinceUtc=active?now:null;
        }
    }

    public void Reset()
    {
        lock(gate) ResetLocked(zone,resetSession:true);
    }

    /// <summary>Starts a new earning-rate window while preserving the application-session totals.</summary>
    public void ResetTimer()
    {
        lock(gate) ResetRateWindowLocked(DateTime.UtcNow);
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

    public void ObserveDrops(IEnumerable<GroundItem> drops,int currentZone,Vec? farmingCenter=null,double? farmingRadius=null)
    {
        ArgumentNullException.ThrowIfNull(drops);
        Vec? farmCenter=farmingCenter;
        double? farmRadius=farmingRadius;
        bool restrictToFarm=farmCenter is Vec center && center.Finite && farmRadius is double limit && double.IsFinite(limit) && limit>=0;
        var current=new Dictionary<LootIdentity,GroundItem>();
        foreach(var item in drops)
        {
            if(!item.Position.Finite)continue;
            current.TryAdd(new LootIdentity(currentZone,item.KeyA,item.KeyB),item);
        }
        lock(gate)
        {
            if(zone!=currentZone)ResetLocked(currentZone);
            if(!initialized)
            {
                known.UnionWith(current.Keys);unmatched.Clear();initialized=true;
                return;
            }
            DateTime now=DateTime.UtcNow;
            PrunePending(now);
            foreach(var entry in current)
            {
                LootIdentity key=entry.Key;
                GroundItem item=entry.Value;
                if(known.Contains(key))continue;
                if(restrictToFarm && (item.Position-farmCenter!.Value).Length>farmRadius!.Value)
                {
                    known.Add(key);unmatched.Remove(key);continue;
                }
                if(!unmatched.TryGetValue(key,out DateTime firstSeen))unmatched[key]=firstSeen=now;
                PendingKill? match=pending.Where(k=>k.Zone==currentZone && now-k.SeenUtc<=DropAttributionWindow && (item.Position-k.Position).Length<=DropAttributionRadius)
                    .OrderBy(k=>(item.Position-k.Position).Length).ThenByDescending(k=>k.SeenUtc).FirstOrDefault();
                if(match==null)
                {
                    if(now-firstSeen>DropAttributionWindow){known.Add(key);unmatched.Remove(key);}
                    continue;
                }
                string? trackedName=TrackedLootFor(item);
                string itemName=trackedName ?? (string.IsNullOrWhiteSpace(item.Name)?"Unknown item":item.Name.Trim());
                SourceState state=sources[match.Source];state.Drops++;
                state.Items[itemName]=state.Items.GetValueOrDefault(itemName)+1;
                long amount=trackedName=="Gold"?GoldAmountFor(item):1;
                if(trackedName!=null)
                {
                    trackedLoot[trackedName]=trackedLoot.GetValueOrDefault(trackedName)+amount;
                }
                string recentName=trackedName=="Gold"?$"Gold ({amount:N0})":itemName;
                recent.Insert(0,new LootTrackerDropSummary(match.Source,recentName,item.Position,now));
                if(recent.Count>12)recent.RemoveRange(12,recent.Count-12);
                known.Add(key);unmatched.Remove(key);
            }
            // Keep a short tombstone for a pile that disappears between two
            // reads.  Auto-pickup can remove a pile before the kill record is
            // observed; retaining the identity lets a reappearing pile still
            // be attributed within the same drop window without double count.
            foreach(LootIdentity key in unmatched.Keys.Where(key=>now-unmatched[key]>DropAttributionWindow).ToArray())
            {
                unmatched.Remove(key);known.Add(key);
            }
        }
    }

    public LootTrackerSnapshot Snapshot()
    {
        lock(gate)
        {
            DateTime now=DateTime.UtcNow;
            TimeSpan elapsed=PositiveDuration(now-sessionStartedUtc);
            AccumulateActiveLocked(now);
            TimeSpan rateElapsed=PositiveDuration(activeRateElapsed);
            double rateHours=rateElapsed.TotalHours;
            var hourly=TrackedLootOrder.Select(name=>
            {
                long earned=Math.Max(0,trackedLoot[name]-rateBaseline.GetValueOrDefault(name));
                return new LootTrackerRateSummary(name,rateHours>0?earned/rateHours:0);
            }).ToArray();
            return new LootTrackerSnapshot(zone,pending.Count,
                SourceOrder.Select(source=>
                {
                    SourceState state=sources[source];
                    return new LootTrackerSourceSummary(source,state.Kills,state.Drops,
                        state.Items.OrderByDescending(item=>item.Value).ThenBy(item=>item.Key,StringComparer.OrdinalIgnoreCase)
                            .Take(5).Select(item=>new LootTrackerItemSummary(item.Key,item.Value)).ToArray());
                }).ToArray(),recent.ToArray(),TrackedLootOrder.Select(name=>new LootTrackerItemSummary(name,trackedLoot[name])).ToArray(),sessionStartedUtc,elapsed,rateStartedUtc,rateElapsed,hourly);
        }
    }

    void PrunePending(DateTime now) => pending.RemoveAll(k=>now-k.SeenUtc>DropAttributionWindow);

    static TimeSpan PositiveDuration(TimeSpan duration)=>duration<TimeSpan.Zero?TimeSpan.Zero:duration;

    void AccumulateActiveLocked(DateTime now)
    {
        if(!rateActive || activeSinceUtc is not DateTime started)return;
        if(now>started)activeRateElapsed+=now-started;
        activeSinceUtc=now;
    }

    void ResetRateWindowLocked(DateTime now)
    {
        AccumulateActiveLocked(now);
        rateStartedUtc=now;
        activeRateElapsed=TimeSpan.Zero;
        activeSinceUtc=rateActive?now:null;
        foreach(string name in TrackedLootOrder)rateBaseline[name]=trackedLoot[name];
    }

    void ResetLocked(int newZone,bool resetSession=false)
    {
        zone=newZone;initialized=false;unmatched.Clear();pending.Clear();recent.Clear();
        foreach(SourceState state in sources.Values){state.Kills=0;state.Drops=0;state.Items.Clear();}
        if(resetSession)
        {
            known.Clear();
            foreach(string name in TrackedLootOrder)trackedLoot[name]=0;
            ResetRateWindowLocked(DateTime.UtcNow);
        }
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
        var baselineGold=new GroundItem(30,30,unchecked((int)(0x80000000u|115u)),"Special drop (115)",new(0,0),0);
        var dedupeTracker=new LootTracker();
        dedupeTracker.ObserveDrops([baselineGold],8,new(0,0),10);
        dedupeTracker.RecordKill(mimic,new(0,0),8);
        dedupeTracker.ObserveDrops([],8,new(0,0),10);
        dedupeTracker.ObserveDrops([baselineGold],8,new(0,0),10);
        if(dedupeTracker.Snapshot().TrackedLoot.First(item=>item.Name=="Gold").Count!=0)
            throw new Exception("A baseline pile was credited after disappearing and reappearing.");
        var newGold=baselineGold with { KeyA=31 };
        dedupeTracker.ObserveDrops([baselineGold,newGold],8,new(0,0),10);
        if(dedupeTracker.Snapshot().TrackedLoot.First(item=>item.Name=="Gold").Count!=115)
            throw new Exception("A new pile with a distinct identity was not credited.");
        var delayedTracker=new LootTracker();
        delayedTracker.ObserveDrops([],8,new(0,0),10);
        delayedTracker.ObserveDrops([newGold],8,new(0,0),10);
        delayedTracker.RecordKill(mimic,new(0,0),8);
        delayedTracker.ObserveDrops([newGold],8,new(0,0),10);
        if(delayedTracker.Snapshot().TrackedLoot.First(item=>item.Name=="Gold").Count!=115)
            throw new Exception("A drop seen before its kill was not retried for attribution.");
        var radiusTracker=new LootTracker();
        radiusTracker.ObserveDrops([],8,new(0,0),10);
        radiusTracker.RecordKill(mimic,new(0,0),8);
        radiusTracker.ObserveDrops([new GroundItem(32,32,unchecked((int)(0x80000000u|99u)),"Special drop (99)",new(20,0),0)],8,new(0,0),10);
        if(radiusTracker.Snapshot().TrackedLoot.First(item=>item.Name=="Gold").Count!=0)
            throw new Exception("A pile outside the saved farming radius was credited.");
        var typeIdentityTracker=new LootTracker();
        typeIdentityTracker.ObserveDrops([new GroundItem(40,40,1,"Existing",new(0,0),0)],8,new(0,0),10);
        typeIdentityTracker.RecordKill(mimic,new(0,0),8);
        typeIdentityTracker.ObserveDrops([new GroundItem(40,40,unchecked((int)(0x80000000u|77u)),"Special drop (77)",new(0,0),0)],8,new(0,0),10);
        if(typeIdentityTracker.Snapshot().TrackedLoot.First(item=>item.Name=="Gold").Count!=0)
            throw new Exception("A refreshed pile was double-counted when its item type changed.");
        var idleTracker=new LootTracker();idleTracker.ResetTimer();
        if(idleTracker.Snapshot().RateElapsed!=TimeSpan.Zero)
            throw new Exception("Idle time was included in the active earning window.");
        tracker.ObserveDrops([existing,new GroundItem(9,9,9,"Distant",new(100,100),0)],8);
        if(tracker.Snapshot().Sources.First(source=>source.Source=="Mimic").Drops!=7)throw new Exception("Distant loot was attributed to a tracked target.");
        tracker.ObserveZone(9);
        snap=tracker.Snapshot();
        if(snap.Sources.Any(source=>source.Kills!=0 || source.Drops!=0))throw new Exception("Zone changes did not reset zone attribution.");
        if(snap.TrackedLoot.Any(item=>item.Count!=1))throw new Exception("Zone changes erased session loot totals.");
        tracker.Reset();
        if(tracker.Snapshot().TrackedLoot.Any(item=>item.Count!=0))throw new Exception("Explicit tracker reset did not clear session loot totals.");
        var timerTracker=new LootTracker();
        timerTracker.ObserveDrops([existing],8);timerTracker.RecordKill(mimic,new(0,0),8);timerTracker.ObserveDrops([existing,encodedGold],8);
        var timed=timerTracker.Snapshot();
        if(timed.Elapsed<TimeSpan.Zero || timed.RateElapsed<TimeSpan.Zero || timed.HourlyLoot.Count!=TrackedLootOrder.Length || timed.HourlyLoot.First(item=>item.Name=="Gold").PerHour<0)
            throw new Exception("Loot earnings timer snapshot was invalid.");
        timerTracker.ResetTimer();
        var resetTimed=timerTracker.Snapshot();
        if(resetTimed.TrackedLoot.First(item=>item.Name=="Gold").Count!=115 || resetTimed.HourlyLoot.Any(item=>item.PerHour!=0))
            throw new Exception("Resetting the loot timer changed totals or retained the old rate window.");
    }
}
