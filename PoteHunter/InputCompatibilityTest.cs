using System.Text.Json;

namespace PoteHunter;

internal static class InputCompatibilityTest
{
    internal static async Task<int> Run()
    {
        WindowsClientInput.DeadlineTicks = Environment.TickCount64 + 20000;
        using var deadline = new System.Threading.Timer(_ =>
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "input-compatibility-test.json"), JsonSerializer.Serialize(new { Passed = false, Error = "20-second test deadline reached", ReaderPid = Environment.ProcessId })); }
            finally { Environment.Exit(3); }
        }, null, 20000, Timeout.Infinite);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var world = new World();
        object result;
        int code;
        try
        {
            ApplicationConfiguration.Initialize();
            await Task.Delay(4000, timeout.Token);
            world.Connect();
            WindowsClientInput.Bind(world);
            Input.Allowed = () => world.CheckInputWindow().Allowed;
            while (!world.CheckInputWindow().Allowed) await Task.Delay(100, timeout.Token);
            await Input.Delay(1000, timeout.Token);
            var movement = await Movement.Calibrate(world, timeout.Token, TraceLog.Record);
            if (WindowsClientInput.SuccessfulPackets == 0) throw new InvalidOperationException("Calibration responded, but the compatibility path was not exercised.");
            result = new { Passed = true, TimeUtc = DateTime.UtcNow, ReaderPid = Environment.ProcessId,
                world.ClientHash, Character = world.LocalPlayer().Name, movement.RadiansPerPixel,
                InputBackend = WindowsClientInput.Backend, NativePackets = WindowsClientInput.SuccessfulPackets, Test = "At most two pairs of 60-pixel opposing mouse turns; no walking or attacks" };
            code = 0;
        }
        catch (Exception ex)
        {
            result = new { Passed = false, TimeUtc = DateTime.UtcNow, ReaderPid = Environment.ProcessId, Error = ex.Message, InputBackend = WindowsClientInput.Backend };
            code = 1;
        }
        finally { Input.Release(); Input.Allowed = () => false; }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "input-compatibility-test.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return code;
    }
}
