namespace PoteHunter;

static class TraceLog
{
    static readonly object gate=new();
    // Everything else, including unknown/new stages, keeps critical admission.
    static readonly HashSet<string> repetitive=new(StringComparer.Ordinal)
    {
        "approach feedback","combat turn feedback","ranged travel feedback","forward pulse timing",
        "world delta","healing health check","precise facing command","target search waiting",
        "anchor approach correction","offensive skill waiting for facing","stationary swing waiting for range",
        "stationary target face unavailable","stationary target attack aim unavailable","hotbar sample",
        "level reading","engaged enemies updated","unavailable attack slots skipped"
    };
    public static void Record(string stage, object details)
        =>RecordAt(stage,details,DateTime.UtcNow);
    internal static void RecordAt(string stage,object details,DateTime timeUtc)
    {
        lock(gate)
        {
            // Freeze mutable details on the owning thread; the worker receives
            // only bytes and never observes world/control/identity objects.
            var frozen=System.Text.Json.JsonSerializer.SerializeToElement(details);
            HuntingSessionLog.Current?.RecordRecovery(stage,frozen);
            DiagnosticPersistence.Enqueue("append calibration trace",DiagnosticPersistence.TraceFile,
                System.Text.Json.JsonSerializer.Serialize(new {TimeUtc=timeUtc,Stage=stage,Details=frozen})+Environment.NewLine,
                !repetitive.Contains(stage),error=>
                {
                    if(error==null)DiagnosticIo.RecordBackgroundSuccess("append calibration trace",DiagnosticPersistence.TraceFile.Path);
                    else DiagnosticIo.RecordBackgroundFailure("append calibration trace",DiagnosticPersistence.TraceFile.Path,error);
                });
        }
    }
}
