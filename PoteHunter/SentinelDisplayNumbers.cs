namespace PoteHunter;

// Session-local labels identify markers; the actual client UID remains visible.
// Distance ordering, range changes and brief observation gaps do not renumber them.
internal sealed class SentinelDisplayNumbers
{
    internal const int Capacity=512;
    readonly Dictionary<SentinelPlayerIdentity,(int Number,long Seen)> labels=new();
    SentinelAlertContext? context;
    int nextNumber=1;
    internal int Count=>labels.Count;
    internal void Reset(){labels.Clear();context=null;nextNumber=1;}
    internal void Update(bool connected,bool fresh,SentinelAlertContext current,IEnumerable<Entity> observations,long now)
    {
        if(!connected){Reset();return;}
        if(context!=current){Reset();context=current;}
        if(!fresh)return;
        var identities=observations.GroupBy(e=>e.Id).Where(g=>g.Count()==1).Select(g=>SentinelPlayerIdentity.Of(g.Single()))
            .Where(i=>i.Valid&&i.Id!=current.Local.Id).Distinct().OrderBy(i=>i.Id).ThenBy(i=>i.Generation).ToArray();
        var present=identities.ToHashSet();
        foreach(var key in labels.Where(pair=>!present.Contains(pair.Key)&&(now-pair.Value.Seen>30000||now<pair.Value.Seen))
            .Select(pair=>pair.Key).ToArray())labels.Remove(key);
        foreach(var identity in identities)
        {
            if(labels.TryGetValue(identity,out var existing)){labels[identity]=(existing.Number,now);continue;}
            if(labels.Count>=Capacity)
            {
                var candidates=labels.Where(pair=>!present.Contains(pair.Key)).OrderBy(pair=>pair.Value.Seen).Select(pair=>pair.Key).ToArray();
                if(candidates.Length==0)continue;
                labels.Remove(candidates[0]);
            }
            var used=labels.Values.Select(value=>value.Number).ToHashSet();
            while(used.Contains(nextNumber))nextNumber=nextNumber>=9999?1:nextNumber+1;
            labels.Add(identity,(nextNumber,now));nextNumber=nextNumber>=9999?1:nextNumber+1;
        }
    }
    internal int Number(Entity entity)=>labels.GetValueOrDefault(SentinelPlayerIdentity.Of(entity)).Number;
}
