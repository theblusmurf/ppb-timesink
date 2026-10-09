namespace PoteHunter;

// Names are display metadata, never proof of identity/ownership. A control
// poll may reuse them for at most one second on the same freshly read body.
internal sealed class PlayerNameDisplayCache
{
    internal const int MaximumAgeMilliseconds = 1000;
    sealed record Entry(Entity Body, string Name);
    Dictionary<uint, Entry> entries = [];
    string context = "";
    long observed = long.MinValue;

    public void Reset() { entries.Clear(); context = ""; observed = long.MinValue; }
    public void Store(string currentContext, IReadOnlyList<Entity> bodies, PlayerNameSnapshot names, long now)
    {
        Reset();
        if (!names.Consistent || string.IsNullOrEmpty(currentContext)) return;
        foreach (var group in bodies.GroupBy(e => e.Id))
        {
            var first = group.First();
            if (group.Any(e => !SameBody(first, e)) || !names.Names.TryGetValue(first.Id, out string? name) || string.IsNullOrWhiteSpace(name)) continue;
            entries[first.Id] = new(first, name);
        }
        context = currentContext; observed = now;
    }
    public int Apply(string currentContext, List<Entity> bodies, uint selfId, long now)
    {
        if (currentContext != context || now < observed || now - observed >= MaximumAgeMilliseconds)
        { Reset(); return 0; }
        int count = 0;
        foreach (var group in bodies.Select((body, index) => (body, index)).Where(pair => PlayerNameReader.Eligible(pair.body, selfId)).GroupBy(pair => pair.body.Id))
        {
            if (!entries.TryGetValue(group.Key, out var entry) || group.Any(pair => !SameBody(entry.Body, pair.body))) continue;
            foreach (var (body, index) in group)
                if (PlayerNameReader.Eligible(body, selfId)) { bodies[index] = body with { VerifiedPlayerName = entry.Name }; count++; }
        }
        return count;
    }
    static bool SameBody(Entity a, Entity b) => a.Address == b.Address && a.Id == b.Id && a.Generation == b.Generation &&
        a.Name == b.Name && a.Model == b.Model;
}
