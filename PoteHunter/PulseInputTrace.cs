namespace PoteHunter;

// Successful input logging is deferred only for the bounded pulse worker.
// Original timestamps are retained; flush happens after key-up on the owner.
// Neither disk I/O nor the session logger can extend the key-held interval.
internal sealed class PulseInputTrace
{
    const int MaximumEntries=64;
    static readonly AsyncLocal<PulseInputTrace?> current=new();
    readonly List<(DateTime TimeUtc,string Stage,object Details)> entries=new();
    int dropped,flushed;
    internal static bool IsActive=>current.Value!=null;
    internal IDisposable Enter()
    {
        if(current.Value!=null)throw new InvalidOperationException("Nested pulse trace ownership is not allowed.");
        current.Value=this;return new Scope(this);
    }
    internal static bool TryRecord(string stage,object details)
    {
        if(current.Value is not { } buffer)return false;
        if(buffer.entries.Count<MaximumEntries)buffer.entries.Add((DateTime.UtcNow,stage,details));
        else buffer.dropped++;
        return true;
    }
    internal void Flush(Action<string,object,DateTime>? record=null)
    {
        if(Interlocked.Exchange(ref flushed,1)!=0)return;
        record ??= TraceLog.RecordAt;
        foreach(var entry in entries)record(entry.Stage,entry.Details,entry.TimeUtc);
        if(dropped>0)record("pulse input trace overflow",new{Dropped=dropped,MaximumEntries},DateTime.UtcNow);
        entries.Clear();
    }
    sealed class Scope(PulseInputTrace owner) : IDisposable
    {
        public void Dispose()
        {
            if(ReferenceEquals(current.Value,owner))current.Value=null;
        }
    }
}
