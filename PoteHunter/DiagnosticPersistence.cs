using System.Text;

namespace PoteHunter;

internal sealed record DiagnosticWriterSnapshot(long AcceptedRecords,long WrittenRecords,long DroppedRepetitiveRecords,
    long CriticalAdmissionWaitCount,long RejectedCriticalRecords,long WriteFailureCount,long BatchAppendCount,
    long SnapshotCoalescedCount,long WrittenSnapshotCount,long RejectedSnapshotCount,long DrainTimeoutCount,
    int QueuedRecords,long QueuedBytes,int CriticalPendingRecords,int PendingSnapshots,long PendingSnapshotBytes,
    int MaximumQueuedRecords,int ActiveFailureCount,string? LastError,bool Accepting,long FileRotationCount,long LegacyArchiveCount);

internal sealed record DiagnosticFile(string Path,long MaximumBytes,int RetainedFiles,string? Header=null);

internal interface IDiagnosticStorage
{
    void Append(DiagnosticFile file,IReadOnlyList<byte[]> records);
    void Replace(string operation,string path,string text);
}

/// <summary>Only frozen diagnostics enter this queue. Game/input/settings delegates never run here.</summary>
internal sealed class BoundedDiagnosticWriter : IDisposable
{
    internal const int DefaultCapacity=4096,DefaultCriticalReserve=512,MaximumRecordBytes=2*1024*1024;
    internal const long DefaultByteCapacity=32L*1024*1024,DefaultReservedBytes=8L*1024*1024;
    const int MaximumSnapshots=16,MaximumBatchRecords=128,MaximumBatchBytes=256*1024;
    const long MaximumSnapshotBytes=16L*1024*1024;
    readonly object gate=new(),ioGate=new();
    readonly LinkedList<Record> records=new();
    readonly HashSet<long> inFlight=new();
    readonly Dictionary<string,SnapshotRecord> snapshots=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,string> errors=new(StringComparer.OrdinalIgnoreCase);
    readonly SemaphoreSlim wake=new(0,1);
    readonly IDiagnosticStorage storage;
    readonly int capacity,reserve,batchDelayMs;
    readonly long byteCapacity,reservedBytes;
    readonly Task worker;
    long queuedBytes,snapshotBytes,sequence,accepted,written,dropped,admissionWaits,rejectedCritical,failures,batches,
        coalesced,snapshotsWritten,snapshotsRejected,drainTimeouts;
    SnapshotRecord? inFlightSnapshot;
    string? lastRejection;
    int maximumQueued,drainers;
    bool accepting=true,stopping,busy;

    sealed record Record(long Sequence,string Operation,DiagnosticFile File,byte[] Bytes,int PayloadBytes,bool Critical,
        Action? Work,Action<Exception?>? Completion);
    sealed record SnapshotRecord(long Sequence,string Operation,string Path,string Text,int Bytes);

    internal BoundedDiagnosticWriter(IDiagnosticStorage? storage=null,int capacity=DefaultCapacity,
        int reserve=DefaultCriticalReserve,long byteCapacity=DefaultByteCapacity,long reservedBytes=DefaultReservedBytes,
        int batchDelayMs=25)
    {
        if(capacity<1 || reserve<0 || reserve>=capacity || byteCapacity<1 || reservedBytes<0 || reservedBytes>=byteCapacity || batchDelayMs<0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        this.storage=storage??new DiagnosticFileStorage();this.capacity=capacity;this.reserve=reserve;
        this.byteCapacity=byteCapacity;this.reservedBytes=reservedBytes;this.batchDelayMs=batchDelayMs;
        worker=Task.Run(Run);
    }

    internal bool Enqueue(string operation,DiagnosticFile file,string text,bool critical,Action<Exception?>? completion=null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);ArgumentException.ThrowIfNullOrWhiteSpace(file.Path);
        ArgumentNullException.ThrowIfNull(text);
        if(!text.EndsWith('\n'))throw new ArgumentException("Diagnostic records must end with a complete newline.",nameof(text));
        return EnqueueFrozen(operation,file,Encoding.UTF8.GetBytes(text),critical,null,completion);
    }

    internal bool EnqueueJson(string operation,DiagnosticFile file,object details,bool critical)
        =>Enqueue(operation,file,System.Text.Json.JsonSerializer.Serialize(details)+Environment.NewLine,critical);

    internal bool EnqueueWork(string operation,string path,int payloadBytes,Action work,Action<Exception?>? completion=null)
    {
        ArgumentNullException.ThrowIfNull(work);
        if(payloadBytes<0 || payloadBytes>MaximumRecordBytes)return Reject(operation,path,true,completion,"Diagnostic capture exceeds its bounded payload limit.");
        // Count the immutable work payload against the same memory budget.
        return EnqueueFrozen(operation,new(Path.GetFullPath(path),MaximumRecordBytes,0),[],true,work,completion,payloadBytes);
    }

    bool EnqueueFrozen(string operation,DiagnosticFile file,byte[] bytes,bool critical,Action? work,Action<Exception?>? completion,int? chargedBytes=null)
    {
        file=file with{Path=Path.GetFullPath(file.Path)};
        int cost=chargedBytes??bytes.Length;
        long headerBytes=file.Header==null?0:Encoding.UTF8.GetByteCount(file.Header+Environment.NewLine);
        if(cost>MaximumRecordBytes || cost>byteCapacity || bytes.LongLength>file.MaximumBytes-headerBytes)
            return Reject(operation,file.Path,critical,completion,"Diagnostic record exceeds its bounded payload/file limit.");
        bool stopped;
        lock(gate)
        {
            stopped=!accepting;
            if(!stopped)
            {
                if(critical)EvictRepetitive(cost);
                if(Fits(cost,critical)){Add(operation,file,bytes,cost,critical,work,completion);return true;}
                if(!critical){dropped++;return false;}
                admissionWaits++;
            }
        }
        if(stopped)return Reject(operation,file.Path,critical,completion,"Diagnostic writer has stopped accepting records.");
        // A failed/hung disk must never block Stop or movement feedback. At
        // the hard critical limit, signal the worker and allow at most 5 ms
        // for space; storage never executes on an enqueueing caller.
        lock(gate)
        {
            Signal();Monitor.Wait(gate,5);
            if(accepting && Fits(cost,true)){Add(operation,file,bytes,cost,true,work,completion);return true;}
        }
        return Reject(operation,file.Path,true,completion,"Critical diagnostic backlog is full; its bounded admission wait could not make room.");
    }

    bool Fits(int bytes,bool critical)=>records.Count<(critical?capacity:capacity-reserve) &&
        queuedBytes+bytes<=(critical?byteCapacity:byteCapacity-reservedBytes);
    void Add(string operation,DiagnosticFile file,byte[] bytes,int cost,bool critical,Action? work,Action<Exception?>? completion)
    {
        records.AddLast(new Record(++sequence,operation,file,bytes,cost,critical,work,completion));
        queuedBytes+=cost;accepted++;maximumQueued=Math.Max(maximumQueued,records.Count);Signal();
    }
    void EvictRepetitive(int bytes)
    {
        var node=records.First;
        while(!Fits(bytes,true) && node!=null)
        {
            var next=node.Next;
            if(!node.Value.Critical && !inFlight.Contains(node.Value.Sequence)){queuedBytes-=node.Value.PayloadBytes;records.Remove(node);dropped++;}
            node=next;
        }
    }
    bool Reject(string operation,string path,bool critical,Action<Exception?>? completion,string reason)
    {
        lock(gate){if(critical)rejectedCritical++;else dropped++;lastRejection=reason;SetError(path,reason);}
        Notify(completion,new IOException(reason));return false;
    }

    internal bool EnqueueSnapshot(string operation,string path,string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);ArgumentNullException.ThrowIfNull(text);
        path=Path.GetFullPath(path);int utf8Bytes=Encoding.UTF8.GetByteCount(text);
        long cost=text.Length*2L; // The queued snapshot owns a UTF-16 string.
        lock(gate)
        {
            snapshots.TryGetValue(path,out var prior);
            int replacedBytes=prior!=null && prior!=inFlightSnapshot?prior.Bytes:0;
            if(!accepting || utf8Bytes>MaximumRecordBytes || snapshots.Count>=MaximumSnapshots && prior==null ||
                snapshotBytes-replacedBytes+cost>MaximumSnapshotBytes)
            {snapshotsRejected++;SetError(path,"Latest diagnostic snapshot was rejected by the bounded writer.");return false;}
            if(prior!=null){snapshotBytes-=replacedBytes;coalesced++;}
            snapshots[path]=new(++sequence,operation,path,text,(int)cost);snapshotBytes+=cost;Signal();return true;
        }
    }

    internal DiagnosticWriterSnapshot Snapshot()
    {
        lock(gate)return new(accepted,written,dropped,admissionWaits,rejectedCritical,failures,batches,coalesced,snapshotsWritten,
            snapshotsRejected,drainTimeouts,records.Count,queuedBytes,records.Count(item=>item.Critical),snapshots.Count,
            snapshotBytes,maximumQueued,errors.Count,errors.Values.LastOrDefault()??lastRejection,accepting,
            storage is DiagnosticFileStorage files?files.RotationCount:0,storage is DiagnosticFileStorage legacy?legacy.LegacyArchiveCount:0);
    }
    internal bool Drain(TimeSpan timeout)
    {
        if(timeout<TimeSpan.Zero || timeout>TimeSpan.FromSeconds(30))throw new ArgumentOutOfRangeException(nameof(timeout));
        long until=Environment.TickCount64+(long)timeout.TotalMilliseconds;
        lock(gate)
        {
            drainers++;Signal();
            try
            {
                while(records.Count>0 || snapshots.Count>0 || busy)
                {
                    long left=until-Environment.TickCount64;
                    if(left<=0){drainTimeouts++;return false;}
                    Monitor.Wait(gate,(int)Math.Min(left,100));
                }
                return true;
            }
            finally{drainers--;}
        }
    }
    internal bool Shutdown(TimeSpan timeout)
    {
        lock(gate)accepting=false;
        bool drained=Drain(timeout);
        lock(gate){stopping=true;Signal();}
        return drained;
    }
    public void Dispose()=>Shutdown(TimeSpan.FromSeconds(2));

    async Task Run()
    {
        while(true)
        {
            await wake.WaitAsync().ConfigureAwait(false);
            lock(gate){if(stopping)return;}
            bool delay;lock(gate)delay=drainers==0;
            if(delay && batchDelayMs>0)await Task.Delay(batchDelayMs).ConfigureAwait(false);
            bool failed=false;
            while(true)
            {
                lock(gate){if(stopping)return;if(records.Count==0 && snapshots.Count==0)break;}
                lock(ioGate)
                {
                    if(!ProcessRecords())failed=true;
                    if(!ProcessSnapshot())failed=true;
                }
                if(failed){await Task.Delay(250).ConfigureAwait(false);Signal();break;}
            }
        }
    }

    bool ProcessRecords()
    {
        List<Record> batch=[];
        lock(gate)
        {
            if(records.First==null)return true;
            var first=records.First.Value;long bytes=0;
            foreach(var item in records)
            {
                if(item.File!=first.File || item.Work!=null && batch.Count>0 || first.Work!=null && batch.Count>0 ||
                    batch.Count>=MaximumBatchRecords || batch.Count>0 && bytes+item.Bytes.Length>Math.Min(MaximumBatchBytes,
                        first.File.MaximumBytes-(first.File.Header==null?0:Encoding.UTF8.GetByteCount(first.File.Header+Environment.NewLine))))break;
                batch.Add(item);bytes+=item.Bytes.Length;inFlight.Add(item.Sequence);
            }
            busy=true;
        }
        Exception? error=null;
        try
        {
            if(batch[0].Work is {} work)work();
            else{storage.Append(batch[0].File,batch.Select(item=>item.Bytes).ToArray());lock(gate)batches++;}
        }
        catch(Exception ex) when(PersistenceFailure(ex)){error=ex;}
        lock(gate)
        {
            if(error!=null){failures++;SetError(batch[0].File.Path,error.Message);}
            else errors.Remove(batch[0].File.Path);
            foreach(var item in batch)
            {
                if(error==null || !item.Critical)
                {
                    // Enqueue may evict repetitive nodes while a batch writes.
                    var node=records.First;while(node!=null && node.Value.Sequence!=item.Sequence)node=node.Next;
                    if(node!=null){records.Remove(node);queuedBytes-=item.PayloadBytes;}
                    if(error==null)written++;else dropped++;
                }
                inFlight.Remove(item.Sequence);
            }
        }
        foreach(var item in batch)Notify(item.Completion,error);
        lock(gate){busy=false;Monitor.PulseAll(gate);}
        return error==null;
    }
    bool ProcessSnapshot()
    {
        SnapshotRecord? item;
        lock(gate){item=snapshots.Values.MinBy(snapshot=>snapshot.Sequence);if(item==null)return true;busy=true;inFlightSnapshot=item;}
        Exception? error=null;
        try{storage.Replace(item.Operation,item.Path,item.Text);}
        catch(Exception ex) when(PersistenceFailure(ex)){error=ex;}
        lock(gate)
        {
            if(error==null)
            {
                snapshotsWritten++;errors.Remove(item.Path);
                if(snapshots.TryGetValue(item.Path,out var current) && current.Sequence==item.Sequence)
                {snapshots.Remove(item.Path);snapshotBytes-=item.Bytes;}
            }
            else{failures++;SetError(item.Path,error.Message);}
            // A newer pending value owns its own charge. Release a superseded
            // in-flight string only after the storage call has finished.
            if(snapshots.TryGetValue(item.Path,out var latest) && latest.Sequence!=item.Sequence)snapshotBytes-=item.Bytes;
            inFlightSnapshot=null;
            busy=false;Monitor.PulseAll(gate);
        }
        return error==null;
    }
    void SetError(string path,string message)
    {
        if(!errors.ContainsKey(path) && errors.Count>=16)errors.Remove(errors.Keys.First());
        errors[path]=message.Length>1024?message[..1024]:message;
    }
    static bool PersistenceFailure(Exception ex)=>ex is IOException or UnauthorizedAccessException or
        System.Security.SecurityException or ArgumentException or NotSupportedException or InvalidOperationException or System.Text.Json.JsonException;
    static void Notify(Action<Exception?>? callback,Exception? error)
    {
        // A diagnostic completion cannot take the worker down or affect input.
        try{callback?.Invoke(error);}catch(Exception ex) when(PersistenceFailure(ex)){}
    }
    void Signal(){try{wake.Release();}catch(SemaphoreFullException){}}
}

internal sealed class DiagnosticFileStorage : IDiagnosticStorage
{
    readonly Func<string,Stream> open;
    readonly HashSet<string> uncertain=new(StringComparer.OrdinalIgnoreCase);
    bool allAppendsUncertain;
    long rotations,legacyArchives;
    internal long RotationCount=>Interlocked.Read(ref rotations);
    internal long LegacyArchiveCount=>Interlocked.Read(ref legacyArchives);
    internal DiagnosticFileStorage(Func<string,Stream>? open=null)=>this.open=open??(path=>new FileStream(path,
        FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read|FileShare.Delete));
    public void Append(DiagnosticFile file,IReadOnlyList<byte[]> records)
    {
        if(allAppendsUncertain || uncertain.Contains(file.Path))throw new IOException("Diagnostic append rollback failed; this file is quarantined in this writer and no uncertain bytes will be replayed.");
        Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
        long incoming=records.Sum(bytes=>(long)bytes.Length);
        int headerBytes=file.Header==null?0:Encoding.UTF8.GetByteCount(file.Header+Environment.NewLine);
        if(incoming+headerBytes>file.MaximumBytes)throw new IOException("Diagnostic batch exceeds its file cap.");
        if(File.Exists(file.Path))
        {
            long existing=new FileInfo(file.Path).Length;
            if(existing>file.MaximumBytes)
            {
                // Preserve an old unlimited trace in full. A second oversized
                // migration cannot overwrite that historical archive.
                string legacy=file.Path+".legacy";
                if(File.Exists(legacy))throw new IOException("An oversized diagnostic history and its legacy archive both exist; preserve/archive them before retrying.");
                File.Move(file.Path,legacy);
                Interlocked.Increment(ref legacyArchives);
            }
            else if(existing+incoming>file.MaximumBytes){Rotate(file);Interlocked.Increment(ref rotations);}
        }
        using var output=open(file.Path);
        long original=output.Length;
        if(original>0)
        {
            output.Position=original-1;
            if(output.ReadByte()!=10)throw new IOException("Diagnostic history has an incomplete final record; the existing file was preserved.");
            if(file.Header!=null)
            {
                output.Position=0;
                using var reader=new StreamReader(output,Encoding.UTF8,true,1024,leaveOpen:true);
                if(reader.ReadLine()!=file.Header)throw new IOException("Session CSV header is incomplete or changed; the existing file was preserved.");
            }
        }
        output.Position=original;
        try
        {
            if(original==0 && file.Header!=null)output.Write(Encoding.UTF8.GetBytes(file.Header+Environment.NewLine));
            foreach(byte[] record in records)output.Write(record);
            output.Flush();
        }
        catch
        {
            // A failed append may be partial. Restore the exact pre-batch
            // boundary before retrying; never blindly replay uncertain bytes.
            try{output.SetLength(original);output.Flush();}
            catch
            {
                if(uncertain.Count>=64 && !uncertain.Contains(file.Path))
                {allAppendsUncertain=true;throw new IOException("Too many uncertain append histories; no automatic replay is safe.");}
                uncertain.Add(file.Path);throw;
            }
            throw;
        }
    }
    internal static void Rotate(DiagnosticFile file)
    {
        if(file.RetainedFiles<1)throw new IOException("Diagnostic file reached its cap and has no archive retention.");
        for(int index=file.RetainedFiles;index>=1;index--)
        {
            string destination=file.Path+"."+index;
            if(index==file.RetainedFiles && File.Exists(destination))File.Delete(destination);
            string source=index==1?file.Path:file.Path+"."+(index-1);
            if(File.Exists(source))File.Move(source,destination,true);
        }
    }
    public void Replace(string operation,string path,string text)
    {
        if(!DiagnosticIo.TryAtomicWrite(operation,path,text))throw new IOException("Optional snapshot replacement failed; the previous complete snapshot was retained.");
    }
}

internal static class DiagnosticPersistence
{
    static readonly BoundedDiagnosticWriter writer=new();
    internal static readonly DiagnosticFile TraceFile=new(Path.Combine(AppContext.BaseDirectory,"calibration-trace.jsonl"),8L*1024*1024,4);
    internal static DiagnosticWriterSnapshot Snapshot()=>writer.Snapshot();
    internal static bool Enqueue(string operation,DiagnosticFile file,string text,bool critical,Action<Exception?>? completion=null)
        =>writer.Enqueue(operation,file,text,critical,completion);
    internal static bool EnqueueWork(string operation,string path,int payloadBytes,Action work,Action<Exception?>? completion=null)
        =>writer.EnqueueWork(operation,path,payloadBytes,work,completion);
    internal static bool EnqueueSnapshot(string operation,string path,string text)=>writer.EnqueueSnapshot(operation,path,text);
    internal static bool Drain(TimeSpan timeout)=>writer.Drain(timeout);
    internal static bool Shutdown(TimeSpan timeout)=>writer.Shutdown(timeout);
    internal static string? StatusSummary
    {
        get
        {
            var state=writer.Snapshot();
            if(state.RejectedCriticalRecords>0)return $"Diagnostic audit backlog rejected {state.RejectedCriticalRecords} critical records: {state.LastError}";
            if(state.ActiveFailureCount>0)return "Diagnostic writer delayed: "+state.LastError;
            if(state.DrainTimeoutCount>0)return $"Diagnostic writer drain timed out {state.DrainTimeoutCount} times; pending audit records require a successful drain";
            if(state.DroppedRepetitiveRecords>0 || state.RejectedSnapshotCount>0)
                return $"Diagnostic backlog: {state.DroppedRepetitiveRecords} repetitive records dropped, {state.RejectedSnapshotCount} snapshots rejected";
            return null;
        }
    }
}
