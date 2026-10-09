using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PoteHunter;

internal static class DiagnosticPersistenceChecks
{
    static void Require(bool value,string message){if(!value)throw new Exception("Diagnostic writer: "+message);}
    public static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"PlayPoteBot-diagnostic-writer-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            OrderedFrozenAndDrain(root);ReservedCriticalAdmission(root);HardCriticalFallback(root);
            FailedWriteRecovery(root);LatestSnapshot(root);FailedSnapshotBackoff(root);RotationAndLegacy(root);PartialAppend(root);
            ObservationCapture(root);object comparison=PersistenceComparison(root);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"diagnostic-writer-checks.json"),JsonSerializer.Serialize(new
            {
                Passed=true,HardwareInputEmitted=false,GameInputUsed=false,LivePerformanceMeasured=false,
                Checks=new[]{"immutable JSON captured before enqueue; ordered batched append and complete drain",
                    "bounded memory and reserved critical admission evict only queued repetitive diagnostics",
                    "critical-only saturation returns after a bounded admission wait, never runs storage on the caller, and exposes rejection",
                    "write failures retain critical records, expose counters, and recover exactly once",
                    "latest snapshots coalesce; an old in-flight snapshot cannot overwrite a newer stop snapshot",
                    "record-boundary rotation and numbered retention; user session history stays untrimmed",
                    "oversized preexisting history preserved intact in a separate legacy archive",
                    "partial append rollback prevents duplicate replay; failed rollback quarantines history",
                    "async observation capture freezes input, accepts one frame, and counts catalog entries once across retry",
                    "shutdown/drain is bounded; local shutdown rejects further admission without ending the shared test writer"},
                OfflineComparison=comparison,
                Limitation="File-fixture operation/latency measurements only; no gameplay/UI/CPU gain established. Legacy archives and user session history are intentionally retained separately."
            },new JsonSerializerOptions{WriteIndented=true}));
        }
        finally
        {
            Require(DiagnosticPersistence.Drain(TimeSpan.FromSeconds(5)),"shared fixture drain did not finish before cleanup");
            string full=Path.GetFullPath(root),temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            Require(full.StartsWith(temp,StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("PlayPoteBot-diagnostic-writer-",StringComparison.Ordinal),"fixture cleanup escaped its private temporary root");
            Directory.Delete(root,true);
        }
    }
    static DiagnosticFile FileSpec(string root,string name,long cap=1024*1024,int retained=2,string? header=null)
        =>new(Path.Combine(root,name),cap,retained,header);
    static string Line(int sequence)=>JsonSerializer.Serialize(new{Sequence=sequence})+"\n";
    static int Sequence(byte[] bytes){using var document=JsonDocument.Parse(bytes);return document.RootElement.GetProperty("Sequence").GetInt32();}
    static void OrderedFrozenAndDrain(string root)
    {
        var storage=new MemoryStorage();using var writer=new BoundedDiagnosticWriter(storage,batchDelayMs:50);
        var mutable=new List<int>{7};
        Require(writer.EnqueueJson("freeze",FileSpec(root,"frozen.jsonl"),new{Values=mutable},true),"frozen record rejected");mutable[0]=99;
        for(int n=0;n<300;n++)Require(writer.Enqueue("ordered",FileSpec(root,"ordered.jsonl"),Line(n),true),"ordered event rejected");
        Require(writer.Drain(TimeSpan.FromSeconds(3)),"ordered drain timed out");
        Require(storage.Records.Where(item=>item.Path.EndsWith("ordered.jsonl",StringComparison.Ordinal)).Select(item=>Sequence(item.Bytes)).SequenceEqual(Enumerable.Range(0,300)),"event ordering changed across batches");
        using var frozen=JsonDocument.Parse(storage.Records.Single(item=>item.Path.EndsWith("frozen.jsonl",StringComparison.Ordinal)).Bytes);
        Require(frozen.RootElement.GetProperty("Values")[0].GetInt32()==7,"queued JSON observed later mutation");
        var state=writer.Snapshot();Require(state.WrittenRecords==301 && state.QueuedRecords==0 && state.BatchAppendCount<10,"writer did not batch/drain records");
        Require(writer.Shutdown(TimeSpan.FromSeconds(1)) && !writer.Enqueue("closed",FileSpec(root,"closed.jsonl"),Line(0),true) && writer.Snapshot().RejectedCriticalRecords==1,"local shutdown accepted a later critical event");
    }
    static void ReservedCriticalAdmission(string root)
    {
        var storage=new MemoryStorage{BlockAppend=true};using var writer=new BoundedDiagnosticWriter(storage,capacity:6,reserve:2,byteCapacity:4096,reservedBytes:1024,batchDelayMs:0);
        var file=FileSpec(root,"overflow.jsonl");writer.Enqueue("repetitive",file,Line(0),false);
        Require(storage.Entered.Wait(2000),"blocked worker did not start");
        for(int n=1;n<8;n++)writer.Enqueue("repetitive",file,Line(n),false);
        for(int n=100;n<103;n++)Require(writer.Enqueue("critical",file,Line(n),true),"critical record did not evict repetitive backlog");
        var full=writer.Snapshot();Require(full.QueuedRecords<=6 && full.QueuedBytes<=4096 && full.DroppedRepetitiveRecords>0 && full.RejectedCriticalRecords==0,"overflow grew beyond its cap or dropped critical input");
        storage.Release.Set();Require(writer.Drain(TimeSpan.FromSeconds(3)),"overflow drain timed out");
        Require(storage.Records.Select(item=>Sequence(item.Bytes)).Where(n=>n>=100).SequenceEqual(new[]{100,101,102}),"critical retention/order failed");
        Require(storage.Records.Any(item=>Sequence(item.Bytes)==0),"in-flight record was evicted while writing");
    }
    static void HardCriticalFallback(string root)
    {
        var denied=new MemoryStorage{BlockAppend=true};using var blocked=new BoundedDiagnosticWriter(denied,capacity:2,reserve:0,byteCapacity:4096,reservedBytes:0,batchDelayMs:0);
        var target=FileSpec(root,"fallback-denied.jsonl");blocked.Enqueue("critical",target,Line(1),true);blocked.Enqueue("critical",target,Line(2),true);
        Require(denied.Entered.Wait(2000),"saturation worker did not block on storage");
        Exception? visible=null;var elapsed=Stopwatch.StartNew();Require(!blocked.Enqueue("critical",target,Line(3),true,error=>visible=error),"blocked full critical queue claimed acceptance");
        Require(elapsed.Elapsed<TimeSpan.FromMilliseconds(250),"critical saturation stalled behind storage on the control caller");
        Require(visible is IOException && blocked.Snapshot() is{RejectedCriticalRecords:1,CriticalAdmissionWaitCount:>0,QueuedRecords:2,ActiveFailureCount:>0},"hard critical rejection was silent or erased retained evidence");
        denied.Release.Set();Require(blocked.Drain(TimeSpan.FromSeconds(3)) && denied.Records.Select(item=>Sequence(item.Bytes)).SequenceEqual(new[]{1,2}),"retained critical records did not recover in order");
    }
    static void FailedWriteRecovery(string root)
    {
        var storage=new MemoryStorage{FailAppend=true};using var writer=new BoundedDiagnosticWriter(storage,batchDelayMs:0);
        writer.Enqueue("critical",FileSpec(root,"retry.jsonl"),Line(11),true);
        Require(!writer.Drain(TimeSpan.FromMilliseconds(100)),"unwritable critical queue falsely reported drain success");
        Require(writer.Snapshot() is{CriticalPendingRecords:1,WriteFailureCount:>0,DrainTimeoutCount:1,ActiveFailureCount:>0},"write failure lost retained critical state or bounded timeout counters");
        storage.FailAppend=false;Require(writer.Drain(TimeSpan.FromSeconds(3)),"unlock did not recover pending audit");
        Require(storage.Records.Select(item=>Sequence(item.Bytes)).SequenceEqual(new[]{11}) && writer.Snapshot().ActiveFailureCount==0,"retry duplicated history or retained a stale write warning");
    }
    static void LatestSnapshot(string root)
    {
        var storage=new MemoryStorage{BlockReplace=true};using var writer=new BoundedDiagnosticWriter(storage,batchDelayMs:0);
        string path=Path.Combine(root,"latest.json");writer.EnqueueSnapshot("status",path,"{\"Sequence\":1,\"Working\":true}");
        Require(storage.Entered.Wait(2000),"snapshot worker did not start");
        writer.EnqueueSnapshot("status",path,"{\"Sequence\":2,\"Working\":true}");writer.EnqueueSnapshot("status",path,"{\"Sequence\":3,\"Working\":false}");
        storage.Release.Set();Require(writer.Drain(TimeSpan.FromSeconds(3)),"latest snapshot drain timed out");
        Require(storage.Snapshots.Count==2 && storage.Snapshots.Last().Text.Contains("\"Working\":false",StringComparison.Ordinal) && storage.Snapshots.All(item=>!item.Text.Contains("\"Sequence\":2",StringComparison.Ordinal)) && writer.Snapshot().SnapshotCoalescedCount>=2,"stale status replaced/coalesced the newer stop incorrectly");
    }
    static void RotationAndLegacy(string root)
    {
        var storage=new DiagnosticFileStorage();var file=FileSpec(root,"rotation.jsonl",80,2);
        for(int n=0;n<30;n++)storage.Append(file,[Encoding.UTF8.GetBytes(Line(n))]);
        Require(Directory.GetFiles(root,"rotation.jsonl*").Length==3 && storage.RotationCount>0,"rotation retention grew beyond current plus two archives");
        var observed=new List<int>();
        foreach(string path in new[]{file.Path+".2",file.Path+".1",file.Path})
        {
            byte[] bytes=System.IO.File.ReadAllBytes(path);Require(bytes.Length<=80 && bytes.Last()==10,"rotation split a record or exceeded its cap");
            observed.AddRange(System.IO.File.ReadAllLines(path).Select(line=>JsonDocument.Parse(line).RootElement.GetProperty("Sequence").GetInt32()));
        }
        Require(observed.SequenceEqual(observed.Order()) && observed.Last()==29,"retained rotation order changed");
        var sessions=FileSpec(root,"history.csv",long.MaxValue,0,"Sequence");
        for(int n=0;n<100;n++)storage.Append(sessions,[Encoding.UTF8.GetBytes(n+"\n")]);
        Require(System.IO.File.ReadAllLines(sessions.Path).Length==101 && !System.IO.File.Exists(sessions.Path+".1"),"session history was trimmed as optional diagnostics");
        var legacy=FileSpec(root,"legacy.jsonl",80,2);byte[] old=Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0,100).Select(Line)));System.IO.File.WriteAllBytes(legacy.Path,old);
        storage.Append(legacy,[Encoding.UTF8.GetBytes(Line(100))]);
        Require(System.IO.File.ReadAllBytes(legacy.Path+".legacy").SequenceEqual(old) && System.IO.File.ReadAllBytes(legacy.Path).Length<=80 && storage.LegacyArchiveCount==1,"oversized existing history was sliced/lost rather than preserved intact");
        for(int n=101;n<125;n++)storage.Append(legacy,[Encoding.UTF8.GetBytes(Line(n))]);
        Require(System.IO.File.ReadAllBytes(legacy.Path+".legacy").SequenceEqual(old),"rolling retention deleted the separate legacy history");
        System.IO.File.WriteAllBytes(legacy.Path,old);bool refused=false;try{storage.Append(legacy,[Encoding.UTF8.GetBytes(Line(126))]);}catch(IOException){refused=true;}
        Require(refused && System.IO.File.ReadAllBytes(legacy.Path+".legacy").SequenceEqual(old) && System.IO.File.ReadAllBytes(legacy.Path).SequenceEqual(old),"another oversized migration overwrote historic evidence");
        var header=FileSpec(root,"header-cap.csv",16,1,"long header");bool capped=false;try{storage.Append(header,[Encoding.UTF8.GetBytes("123456\n")]);}catch(IOException){capped=true;}
        Require(capped,"file cap ignored its CSV header");
    }
    static void FailedSnapshotBackoff(string root)
    {
        var storage=new MemoryStorage{FailReplace=true};using var writer=new BoundedDiagnosticWriter(storage,batchDelayMs:0);
        writer.EnqueueSnapshot("denied snapshot",Path.Combine(root,"denied-latest.json"),"{\"Working\":false}");
        Require(SpinWait.SpinUntil(()=>Volatile.Read(ref storage.ReplaceAttempts)>0,1000),"snapshot failure fixture did not run");
        Thread.Sleep(350);
        Require(Volatile.Read(ref storage.ReplaceAttempts)<=4 && writer.Snapshot() is{WriteFailureCount:>0,PendingSnapshots:1,ActiveFailureCount:>0},"snapshot-only failure spun without bounded retry backoff");
        storage.FailReplace=false;Require(writer.Drain(TimeSpan.FromSeconds(3)) && storage.Snapshots.Count==1,"latest failed snapshot did not recover once after unlock");
    }
    static void PartialAppend(string root)
    {
        string path=Path.Combine(root,"partial.jsonl");System.IO.File.WriteAllText(path,Line(0));bool first=true;
        var storage=new DiagnosticFileStorage(file=>new PartialStream(new FileStream(file,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read),first?(first=false,true).Item2:false,false));
        var spec=new DiagnosticFile(path,1024,1);bool failed=false;try{storage.Append(spec,[Encoding.UTF8.GetBytes(Line(1))]);}catch(IOException){failed=true;}
        Require(failed && System.IO.File.ReadAllText(path)==Line(0),"partial failure left an incomplete batch");storage.Append(spec,[Encoding.UTF8.GetBytes(Line(1))]);
        Require(System.IO.File.ReadAllText(path)==Line(0)+Line(1),"rolled-back replay duplicated records");
        string uncertain=Path.Combine(root,"uncertain.jsonl");System.IO.File.WriteAllText(uncertain,Line(0));int opens=0;
        var quarantined=new DiagnosticFileStorage(file=>{opens++;return new PartialStream(new FileStream(file,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read),true,true);});
        var risky=new DiagnosticFile(uncertain,1024,1);try{quarantined.Append(risky,[Encoding.UTF8.GetBytes(Line(1))]);}catch(IOException){}
        byte[] preserved=System.IO.File.ReadAllBytes(uncertain);bool blocked=false;try{quarantined.Append(risky,[Encoding.UTF8.GetBytes(Line(1))]);}catch(IOException){blocked=true;}
        Require(blocked && opens==1 && System.IO.File.ReadAllBytes(uncertain).SequenceEqual(preserved),"failed rollback blindly replayed uncertain bytes");
    }
    static void ObservationCapture(string root)
    {
        var slowStorage=new MemoryStorage{BlockAppend=true};var slow=new DataRecorder(Path.Combine(root,"slow-observations"),slowStorage);
        Require(slow.EnqueueCapture(new{Sequence=1},[]),"slow observation fixture rejected capture");
        Require(slowStorage.Entered.Wait(2000),"observation worker did not enter blocked storage");
        var admission=Stopwatch.StartNew();Require(!slow.EnqueueCapture(new{Sequence=2},[]),"busy observation recorder accepted another count frame");
        Require(admission.Elapsed<TimeSpan.FromMilliseconds(250),"observation admission waited on the worker's catalog/storage gate");
        slowStorage.Release.Set();Require(DiagnosticPersistence.Drain(TimeSpan.FromSeconds(5)),"slow observation fixture did not drain");
        string folder=Path.Combine(root,"observations");Directory.CreateDirectory(folder);
        using var scope=DiagnosticIo.BeginChecks(Path.Combine(root,"capture-audit.jsonl"));
        var recorder=new DataRecorder(folder);var values=new List<int>{4};var types=new[]{new ObjectTypeObservation("fixture",1,"Original","model","definition",1,2,false)};
        string path=Path.Combine(folder,"live-observations.jsonl");
        using(var locked=new FileStream(path,FileMode.Create,FileAccess.ReadWrite,FileShare.None))
        {
            Require(recorder.EnqueueCapture(new{Values=values},types),"async observation rejected a first frame");values[0]=8;types[0]=types[0] with{Name="Changed"};
            Require(!recorder.EnqueueCapture(new{Values=values},types) && recorder.SkippedPendingCaptures>0,"recorder admitted multiple pending count frames");
            Require(SpinWait.SpinUntil(()=>recorder.LastError!=null,2000),"blocked observation did not expose its asynchronous failure");
        }
        Require(DiagnosticPersistence.Drain(TimeSpan.FromSeconds(5)) && recorder.LastError==null,"async observation did not drain after unlock");
        using var frame=JsonDocument.Parse(System.IO.File.ReadAllText(path));Require(frame.RootElement.GetProperty("Values")[0].GetInt32()==4,"async observation read a mutable caller snapshot later");
        using var catalog=JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(folder,"observed-object-types.json")));
        Require(catalog.RootElement.GetProperty("fixture").GetProperty("Count").GetInt64()==1 && catalog.RootElement.GetProperty("fixture").GetProperty("Name").GetString()=="Original","retry duplicated accepted catalog counts or observed later object mutations");
    }
    static object PersistenceComparison(string root)
    {
        const int count=400;var synchronous=new List<double>();var queued=new List<double>();int syncOpens=0,asyncOpens=0;
        var syncStorage=new DiagnosticFileStorage(path=>{syncOpens++;return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read);});
        var before=FileSpec(root,"comparison-sync.jsonl");var total=Stopwatch.StartNew();
        for(int n=0;n<count;n++){long tick=Stopwatch.GetTimestamp();syncStorage.Append(before,[Encoding.UTF8.GetBytes(Line(n))]);synchronous.Add(Stopwatch.GetElapsedTime(tick).TotalMicroseconds);}
        double syncTotal=total.Elapsed.TotalMilliseconds;
        var asyncStorage=new DiagnosticFileStorage(path=>{asyncOpens++;return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read);});
        using var writer=new BoundedDiagnosticWriter(asyncStorage,batchDelayMs:50);var after=FileSpec(root,"comparison-async.jsonl");total.Restart();
        for(int n=0;n<count;n++){long tick=Stopwatch.GetTimestamp();Require(writer.Enqueue("comparison",after,Line(n),true),"comparison enqueue rejected");queued.Add(Stopwatch.GetElapsedTime(tick).TotalMicroseconds);}
        double callerTotal=total.Elapsed.TotalMilliseconds;Require(writer.Drain(TimeSpan.FromSeconds(5)),"comparison did not drain");double fullTotal=total.Elapsed.TotalMilliseconds;
        Require(syncOpens==count && asyncOpens<=8 && System.IO.File.ReadAllText(before.Path)==System.IO.File.ReadAllText(after.Path),"batched operation reduction changed final ordered bytes");
        static object Times(List<double> values){values.Sort();return new{MedianMicroseconds=values[values.Count/2],P95Microseconds=values[(int)(values.Count*.95)],P99Microseconds=values[(int)(values.Count*.99)]};}
        return new{Method="Equivalent 400-record local temporary-file workload; no game/client/UI workload",Records=count,
            Synchronous=new{AppendOpenOperations=syncOpens,CallerMilliseconds=syncTotal,Latency=Times(synchronous)},
            Batched=new{AppendOpenOperations=asyncOpens,CallerMilliseconds=callerTotal,IncludingDrainMilliseconds=fullTotal,Latency=Times(queued)},
            OrderedBytesEqual=true,QueueRejectedRecords=writer.Snapshot().RejectedCriticalRecords};
    }
    sealed class MemoryStorage : IDiagnosticStorage
    {
        public volatile bool FailAppend;
        public volatile bool FailReplace;
        public int ReplaceAttempts;
        public bool BlockAppend,BlockReplace;
        bool blocked;
        public readonly ManualResetEventSlim Entered=new(),Release=new();
        public readonly List<(string Path,byte[] Bytes)> Records=[];
        public readonly List<(string Path,string Text)> Snapshots=[];
        public void Append(DiagnosticFile file,IReadOnlyList<byte[]> records)
        {
            if(BlockAppend && !blocked){blocked=true;Entered.Set();if(!Release.Wait(3000))throw new IOException("fixture append wait timed out");}
            if(FailAppend)throw new IOException("fixture append denied");
            foreach(var bytes in records)Records.Add((file.Path,bytes.ToArray()));
        }
        public void Replace(string operation,string path,string text)
        {
            Interlocked.Increment(ref ReplaceAttempts);
            if(BlockReplace && !blocked){blocked=true;Entered.Set();if(!Release.Wait(3000))throw new IOException("fixture snapshot wait timed out");}
            if(FailReplace)throw new IOException("fixture snapshot denied");
            Snapshots.Add((path,text));
        }
    }
    sealed class PartialStream(Stream inner,bool failWrite,bool failRollback) : Stream
    {
        public override bool CanRead=>inner.CanRead;public override bool CanSeek=>inner.CanSeek;public override bool CanWrite=>inner.CanWrite;
        public override long Length=>inner.Length;public override long Position{get=>inner.Position;set=>inner.Position=value;}
        public override void Flush()=>inner.Flush();public override int Read(byte[] buffer,int offset,int count)=>inner.Read(buffer,offset,count);
        public override long Seek(long offset,SeekOrigin origin)=>inner.Seek(offset,origin);
        public override void SetLength(long length){if(failRollback)throw new IOException("fixture rollback denied");inner.SetLength(length);}
        public override void Write(byte[] buffer,int offset,int count)
        {if(failWrite){inner.Write(buffer,offset,Math.Min(4,count));inner.Flush();throw new IOException("fixture partial append");}inner.Write(buffer,offset,count);}
        protected override void Dispose(bool disposing){if(disposing)inner.Dispose();base.Dispose(disposing);}
    }
}
