using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PoteHunter;

// Reuses the verified installer, including its existing user-data preservation
// and uninstall registration. The worker is a copy of the packaged executable,
// outside the installation, so no running image needs to be replaced.
internal sealed record PatchRequest(string Directory, string CurrentVersion, AvailableUpdate Update,
    int ParentId, long ParentStartedUtcTicks);

internal static partial class AutoPatcher
{
    internal const string GateName="Local\\PoteHunter.Patching";
    internal static string CacheRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PoteHunter", "UpdateCache");
    internal static bool Idle(bool working, bool busy, bool recording, bool modal) => !working && !busy && !recording && !modal;

    internal static void NoLinks(string path)
    {
        for (string? item = Path.GetFullPath(path); item != null; item = Path.GetDirectoryName(item))
            if ((File.Exists(item) || System.IO.Directory.Exists(item)) &&
                (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Patching through a linked folder or file is unsupported.");
    }
    internal static string Validate(string requestPath, PatchRequest request, string cacheRoot)
    {
        string path = Path.GetFullPath(requestPath), folder = Path.GetDirectoryName(path)!;
        if (!Path.GetFileName(path).Equals("patch-request.json", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetDirectoryName(folder)!.Equals(Path.GetFullPath(cacheRoot), StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(Path.GetFileName(folder), "^[a-f0-9]{32}$"))
            throw new InvalidOperationException("Invalid patch staging folder.");
        if(request.Update==null)throw new InvalidOperationException("Patch release is missing.");
        string target = Path.GetFullPath(request.Directory);
        if (!target.Equals(request.Directory, StringComparison.OrdinalIgnoreCase) || target == Path.GetPathRoot(target) ||
            !File.Exists(Path.Combine(target, "PoteHunter.exe")) || request.ParentId <= 0 || request.ParentStartedUtcTicks <= 0 ||
            AppUpdates.ReleaseNumber(request.CurrentVersion) < 0 ||
            AppUpdates.ReleaseNumber(request.Update.Version) <= AppUpdates.ReleaseNumber(request.CurrentVersion) ||
            request.Update.AssetId <= 0 || request.Update.Size <= 0 || request.Update.Size > 512L * 1024 * 1024 ||
            !Regex.IsMatch(request.Update.Sha256, "^[a-fA-F0-9]{64}$"))
            throw new InvalidOperationException("Invalid patch target or release.");
        NoLinks(path); NoLinks(target);
        return Path.Combine(folder, $"PoteHunter-{request.Update.Version}-Setup.exe");
    }
    internal static async Task VerifyInstaller(string path, AvailableUpdate update, CancellationToken token)
    {
        NoLinks(path);
        using var input = File.OpenRead(path);
        if (input.Length != update.Size || !Convert.ToHexString(await SHA256.HashDataAsync(input, token))
            .Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Patch installer size or checksum changed. Installation blocked.");
    }
    internal static ProcessStartInfo InstallerStart(string installer, string target, string log)
    {
        var start = new ProcessStartInfo(installer) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "/SP-", "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART",
            "/NOCLOSEAPPLICATIONS", "/NORESTARTAPPLICATIONS", "/RESTARTEXITCODE=3010", "/DIR=" + target, "/LOG=" + log })
            start.ArgumentList.Add(arg);
        return start;
    }
    internal static void Install(string installer, string target, AvailableUpdate update, string log)
    {
        using var locked = new FileStream(installer, FileMode.Open, FileAccess.Read, FileShare.Read);
        if(locked.Length!=update.Size || !Convert.ToHexString(SHA256.HashData(locked)).Equals(update.Sha256,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Patch installer changed before launch. Installation blocked.");
        using var setup = Process.Start(InstallerStart(installer, target, log))
            ?? throw new InvalidOperationException("Could not launch the installer.");
        setup.WaitForExit();
        if (setup.ExitCode != 0) throw new InvalidOperationException($"Installer returned {setup.ExitCode}. See the patch log; no automatic retry or restart.");
        if (File.ReadAllText(Path.Combine(target, "release-version.txt")).Trim() != update.Version)
            throw new InvalidOperationException("Installed version could not be confirmed. Automatic restart blocked.");
    }
    internal static async Task<string> Stage(string installer, AvailableUpdate update, CancellationToken token)
    {
        string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        string folder = Path.GetDirectoryName(installer)!;
        using var parent = Process.GetCurrentProcess();
        string executable = parent.MainModule?.FileName ?? "";
        if (!executable.Equals(Path.Combine(target, "PoteHunter.exe"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Automatic patching requires the packaged PoteHunter.exe.");
        var request = new PatchRequest(target, AppUpdates.CurrentVersion, update, parent.Id, parent.StartTime.ToUniversalTime().Ticks);
        string requestPath = Path.Combine(folder, "patch-request.json");
        if (!Validate(requestPath, request, CacheRoot).Equals(installer, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected patch installer path.");
        await VerifyInstaller(installer, update, token);
        token.ThrowIfCancellationRequested();
        string worker = Path.Combine(folder, "PoteHunter-Patcher.exe");
        File.Copy(executable, worker, false);
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request), token);
        return requestPath;
    }
    internal static async Task Launch(string requestPath, CancellationToken token)
    {
        var start = new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(requestPath)!, "PoteHunter-Patcher.exe"))
            { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--apply-patch"); start.ArgumentList.Add(requestPath);
        using var worker = Process.Start(start) ?? throw new InvalidOperationException("Could not start the patch worker.");
        string ready=Path.Combine(Path.GetDirectoryName(requestPath)!, "worker-ready.txt");
        DateTime deadline=DateTime.UtcNow.AddSeconds(45);
        while(!File.Exists(ready))
        {
            if(worker.HasExited || DateTime.UtcNow>=deadline)throw new InvalidOperationException("Patch worker did not acknowledge handoff. Application remains open.");
            await Task.Delay(100,token);
        }
    }
    internal static async Task<int> Run(string requestPath)
    {
        string? log = null; PatchRequest? request = null;
        try
        {
            // Reject path escapes before reading a request controlled by staging.
            string path = Path.GetFullPath(requestPath), folder = Path.GetDirectoryName(path)!;
            if (Path.GetFileName(path) != "patch-request.json" ||
                Path.GetDirectoryName(folder) != CacheRoot || !Regex.IsMatch(Path.GetFileName(folder), "^[a-f0-9]{32}$"))
                throw new InvalidOperationException("Invalid patch request path.");
            NoLinks(path);
            if (new FileInfo(path).Length > 16384) throw new InvalidOperationException("Patch request is too large.");
            request = JsonSerializer.Deserialize<PatchRequest>(await File.ReadAllTextAsync(path))
                ?? throw new InvalidOperationException("Patch request is missing.");
            string installer = Validate(path, request, CacheRoot);
            string worker = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (!worker.Equals(Path.Combine(folder, "PoteHunter-Patcher.exe"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Patch worker must run from staging.");
            log = Path.Combine(folder, "patcher.log");
            void Record(string text) => File.AppendAllText(log, DateTime.UtcNow.ToString("O") + " " + text + Environment.NewLine);
            using var parent = Process.GetProcessById(request.ParentId);
            if (parent.StartTime.ToUniversalTime().Ticks != request.ParentStartedUtcTicks ||
                !(parent.MainModule?.FileName ?? "").Equals(Path.Combine(request.Directory, "PoteHunter.exe"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The original PoteHunter process no longer matches.");
            Record("Waiting for the original application to exit; no forced termination.");
            File.WriteAllText(Path.Combine(folder,"worker-ready.txt"),"ready");
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2))) await parent.WaitForExitAsync(timeout.Token);
            using var mutex = new Mutex(true, GateName, out bool owned);
            if (!owned) throw new InvalidOperationException("Another PoteHunter instance is starting or patching. Patch deferred.");
            try
            {
                RequireGuiExited();
                if (File.ReadAllText(Path.Combine(request.Directory, "release-version.txt")).Trim() != request.CurrentVersion)
                    throw new InvalidOperationException("Installation changed while the patch was staged.");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                // Named mutex ownership is thread-affine: no await while held.
                var current = AppUpdates.Check(timeout.Token, request.CurrentVersion).GetAwaiter().GetResult();
                if (current != request.Update) throw new InvalidOperationException("The staged patch is no longer the latest verified release.");
                VerifyInstaller(installer, request.Update, timeout.Token).GetAwaiter().GetResult();
                Record("Installing " + request.Update.Version);
                Install(installer, request.Directory, request.Update, Path.Combine(folder, "installer.log"));
                AppUpdates.PatchStatus = "Installed " + request.Update.Version;
                AppUpdates.BlockedPatch = "";
                Record("Installation confirmed; reopening PoteHunter with hunting stopped.");
            }
            finally { mutex.ReleaseMutex(); }
            // Closing the last handle removes the named gate before GUI startup.
            mutex.Dispose();
            using var reopened = Process.Start(new ProcessStartInfo(Path.Combine(request.Directory, "PoteHunter.exe"))
                { UseShellExecute = true, WorkingDirectory = request.Directory });
            return 0;
        }
        catch (Exception ex)
        {
            if (request?.Update?.Version is string failedVersion) AppUpdates.BlockedPatch = failedVersion;
            AppUpdates.PatchStatus = "Automatic patch stopped: " + ex.Message;
            if (log != null) File.AppendAllText(log, DateTime.UtcNow.ToString("O") + " ERROR " + ex.Message + Environment.NewLine);
            MessageBox.Show(AppUpdates.PatchStatus + "\nUse Setup > Updates to retry manually.\n" + (log ?? ""), "PoteHunter patcher");
            return 1;
        }
    }
    internal static void RequireGuiExited()
    {
        try { using var gui=Mutex.OpenExisting("Local\\PoteHunter.SingleInstance"); }
        catch(WaitHandleCannotBeOpenedException) { return; }
        throw new InvalidOperationException("Another PoteHunter instance is running. Patch deferred.");
    }
}
