namespace PoteHunter;

static class TraceLog
{
    public static void Record(string stage, object details)
    {
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "calibration-trace.jsonl"), System.Text.Json.JsonSerializer.Serialize(new { TimeUtc = DateTime.UtcNow, Stage = stage, Details = details }) + Environment.NewLine);
    }
}
