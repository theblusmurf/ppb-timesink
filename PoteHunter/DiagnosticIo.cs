using System.Text;
using System.Text.Json;

namespace PoteHunter;

internal sealed record DiagnosticFailure(string Operation, string? Path, string ExceptionType,
    int HResult, string Error, string? StackTrace, string? TickOperation, DateTime TimeUtc, long Count);

internal sealed record DiagnosticIoSnapshot(long FailureCount, long RetryCount, long CleanupFailureCount,
    long AuditWriteFailureCount, long AuditRecordCount, long SuppressedAuditRecordCount,
    int ActiveWarningCount, DiagnosticFailure? LastFailure, DiagnosticFailure[] ActiveWarnings,
    DiagnosticFailure[] RecentFailures);

/// <summary>Optional diagnostic persistence only; required settings/input/client operations keep their own failure policy.</summary>
internal static class DiagnosticIo
{
    const int RecentCapacity = 24, ActiveCapacity = 16, CounterCapacity = 32;
    const long AuditSizeLimit = 1024 * 1024, AuditRepeatDelayMs = 30_000;
    static readonly object gate = new(), appendGate = new();
    static readonly UTF8Encoding utf8 = new(false);
    static State state = new(Path.Combine(AppContext.BaseDirectory, "diagnostic-errors.jsonl"));

    sealed class State(string auditPath)
    {
        public readonly string AuditPath = auditPath;
        public readonly Queue<DiagnosticFailure> Recent = new();
        public readonly Dictionary<string, DiagnosticFailure> Active = new(StringComparer.Ordinal);
        public readonly Dictionary<string, (long Count, long LastAuditAt)> Counts = new(StringComparer.Ordinal);
        public long Failures, Retries, CleanupFailures, AuditFailures, AuditRecords, AuditSuppressed;
        public DiagnosticFailure? Last;
    }

    public static string? StatusSummary
    {
        get
        {
            lock (gate)
            {
                if (state.Active.Count == 0) return DiagnosticPersistence.StatusSummary;
                var newest = state.Active.Values.MaxBy(failure => failure.TimeUtc)!;
                return "Diagnostic persistence delayed: " + newest.Operation +
                    (state.Active.Count > 1 ? $" ({state.Active.Count} operations)" : "");
            }
        }
    }

    public static DiagnosticIoSnapshot Snapshot()
    {
        lock (gate)
            return new(state.Failures, state.Retries, state.CleanupFailures, state.AuditFailures,
                state.AuditRecords, state.AuditSuppressed, state.Active.Count, state.Last,
                state.Active.Values.OrderBy(failure => failure.TimeUtc).ToArray(), state.Recent.ToArray());
    }

    public static bool TryAtomicWrite(string operation, string path, string text)
    {
        Validate(operation, path, text);
        string fullPath = Path.GetFullPath(path);
        string parent = Path.GetDirectoryName(fullPath)!;
        string temporary = Path.Combine(parent, "." + Path.GetFileName(fullPath) + "." +
            Environment.ProcessId + "." + Guid.NewGuid().ToString("N") + ".diagnostic-tmp");
        string backup = temporary + ".diagnostic-backup";
        bool preserveBackup = false;
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(parent);
                    File.WriteAllText(temporary, text, utf8);
                    if (File.Exists(fullPath)) File.Replace(temporary, fullPath, backup);
                    else File.Move(temporary, fullPath); // An unexpected concurrent creation is a failure, never a blind overwrite.
                    RecordSuccess(operation, fullPath);
                    return true;
                }
                catch (Exception error) when (IsIoFailure(error))
                {
                    if (!RestoreBackup(backup, fullPath))
                    {
                        preserveBackup = true;
                        RecordFailure(operation, fullPath, error);
                        return false;
                    }
                    if (Retry(error, attempt)) continue;
                    RecordFailure(operation, fullPath, error);
                    return false;
                }
            }
        }
        finally
        {
            CleanupTemporary(temporary);
            if (!preserveBackup) CleanupTemporary(backup);
        }
    }

    public static bool TryAppend(string operation, string path, string text)
    {
        Validate(operation, path, text);
        string fullPath = Path.GetFullPath(path);
        byte[] bytes = utf8.GetBytes(text);
        lock (appendGate)
        {
            FileStream stream;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    stream = new FileStream(fullPath, FileMode.Append, FileAccess.Write,
                        FileShare.ReadWrite | FileShare.Delete);
                    break;
                }
                catch (Exception error) when (IsIoFailure(error))
                {
                    if (Retry(error, attempt)) continue;
                    RecordFailure(operation, fullPath, error);
                    return false;
                }
            }
            // Retry only the open. A failed write may have appended some bytes, so replaying it could duplicate history.
            try
            {
                using (stream) stream.Write(bytes);
                RecordSuccess(operation, fullPath);
                return true;
            }
            catch (Exception error) when (IsIoFailure(error))
            {
                RecordFailure(operation, fullPath, error);
                return false;
            }
        }
    }

    public static void RecordFailure(string operation, string? path, Exception error)
        =>RecordFailureCore(operation,path,error,true);

    // The worker's own failure must not recursively enqueue another audit.
    internal static void RecordBackgroundFailure(string operation,string path,Exception error)
        =>RecordFailureCore(operation,path,error,false);
    internal static void RecordBackgroundSuccess(string operation,string path)=>RecordSuccess(operation,path);

    static void RecordFailureCore(string operation,string? path,Exception error,bool writeAudit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(error);
        string boundedOperation = Bound(operation, 128)!;
        string? boundedPath = Bound(path, 1024);
        string key = boundedOperation + "\n" + boundedPath;
        State owner;
        DiagnosticFailure failure;
        bool audit;
        lock (gate)
        {
            owner = state;
            owner.Failures++;
            long now = Environment.TickCount64;
            bool known = owner.Counts.TryGetValue(key, out var prior);
            long count = known ? prior.Count + 1 : 1;
            audit = !known || now - prior.LastAuditAt >= AuditRepeatDelayMs;
            if (!known && owner.Counts.Count >= CounterCapacity) owner.Counts.Remove(owner.Counts.Keys.First());
            owner.Counts[key] = (count, audit ? now : prior.LastAuditAt);
            string? stage = error.Data["TickOperation"] as string;
            failure = new(boundedOperation, boundedPath, Bound(error.GetType().FullName, 256)!, error.HResult,
                Bound(error.Message, 1024)!, Bound(error.StackTrace, 4096), Bound(stage, 128), DateTime.UtcNow, count);
            owner.Last = failure;
            if (!owner.Active.ContainsKey(key) && owner.Active.Count >= ActiveCapacity)
                owner.Active.Remove(owner.Active.MinBy(entry => entry.Value.TimeUtc).Key);
            owner.Active[key] = failure;
            if (owner.Recent.Count >= RecentCapacity) owner.Recent.Dequeue();
            owner.Recent.Enqueue(failure);
            if (!audit) owner.AuditSuppressed++;
        }
        if (audit && writeAudit) AppendAudit(owner, failure);
    }

    public static void RecordSuccess(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        string bounded = Bound(operation, 128)!;
        lock (gate)
            foreach (string key in state.Active.Where(entry => entry.Value.Operation == bounded).Select(entry => entry.Key).ToArray())
                state.Active.Remove(key);
    }

    static void RecordSuccess(string operation, string path)
    {
        lock (gate) state.Active.Remove(Bound(operation, 128) + "\n" + Bound(path, 1024));
    }

    static void Validate(string operation, string path, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(text);
    }

    static bool IsIoFailure(Exception error) => error is IOException or UnauthorizedAccessException;

    static bool Retry(Exception error, int attempt)
    {
        // Windows sharing/access/lock violations only; a missing path, full disk or other IO failure is not transient here.
        int code = error.HResult & 0xffff;
        if (attempt >= 2 || code is not (5 or 32 or 33)) return false;
        lock (gate) state.Retries++;
        Thread.Sleep(5 * (attempt + 1));
        return true;
    }

    static void CleanupTemporary(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { File.Delete(path); return; }
            catch (DirectoryNotFoundException) { return; } // No temporary file remains if its directory is already absent.
            catch (Exception error) when (IsIoFailure(error))
            {
                if (Retry(error, attempt)) continue;
                lock (gate) state.CleanupFailures++;
                RecordFailure("cleanup diagnostic temp", path, error);
                return;
            }
        }
    }

    static bool RestoreBackup(string backup, string destination)
    {
        // Rare ReplaceFile failure1176/1177 can move the old snapshot to the backup name.
        // Restore only a missing destination; a concurrent writer's complete replacement must remain intact.
        if (!File.Exists(backup) || File.Exists(destination)) return true;
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(backup, destination); return true; }
            catch (Exception error) when (IsIoFailure(error))
            {
                if (Retry(error, attempt)) continue;
                RecordFailure("restore diagnostic snapshot", backup, error);
                return false; // The caller deliberately retains this recoverable old snapshot.
            }
        }
    }

    static void AppendAudit(State owner, DiagnosticFailure failure)
    {
        DiagnosticPersistence.Enqueue("append diagnostic audit",new(owner.AuditPath,AuditSizeLimit,1),
            JsonSerializer.Serialize(failure)+Environment.NewLine,true,error=>
            {
                // Capture this state: a late completion from an offline check
                // must never alter the restored application's counters.
                lock(gate){if(error==null)owner.AuditRecords++;else owner.AuditFailures++;}
            });
    }

    static string? Bound(string? value, int length) => value?.Length > length ? value[..length] : value;

    // Offline checks exclusively swap this state so deliberate failures cannot leak into normal self-test/UI status.
    internal static IDisposable BeginChecks(string auditPath)
    {
        string fullPath = Path.GetFullPath(auditPath);
        lock (gate)
        {
            State previous = state;
            state = new(fullPath);
            return new CheckScope(previous);
        }
    }

    sealed class CheckScope(State previous) : IDisposable
    {
        bool disposed;
        public void Dispose()
        {
            if(!DiagnosticPersistence.Drain(TimeSpan.FromSeconds(5)))
                throw new IOException("Offline diagnostic audit did not drain before its state was restored.");
            lock (gate)
            {
                if (disposed) return;
                state = previous;
                disposed = true;
            }
        }
    }
}
