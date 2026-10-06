namespace PoteHunter;

static class TraceLog
{
    public static void Record(string stage, object details)
        =>RecordAt(stage,details,DateTime.UtcNow);
    internal static void RecordAt(string stage,object details,DateTime timeUtc)
    {
        HuntingSessionLog.Current?.RecordRecovery(stage,details);
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "calibration-trace.jsonl"), System.Text.Json.JsonSerializer.Serialize(new { TimeUtc = timeUtc, Stage = stage, Details = details }) + Environment.NewLine);
    }
}
