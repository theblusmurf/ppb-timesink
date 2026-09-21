using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace PoteHunter;

internal static class GameWindow
{
    const string GameTitle = "Path of the Emperor";
    public sealed record Candidate(long Handle, int ProcessId, long Owner, bool Visible,
        string Title, string ClassName, int ClientWidth, int ClientHeight, string Source)
    {
        public int DirectProcessId { get; init; }
        public uint DirectThreadId { get; init; }
        public int DirectQueryError { get; init; }
        public uint VerifiedThreadId { get; init; }
    }
    public sealed record ThreadCheck(uint ThreadId, uint ProcessId, int WindowsFound, int Error);
    public sealed record InputState(long Window, long ForegroundWindow, bool OwnershipVerified, bool Minimized, bool ProcessAlive = true)
    {
        public bool Allowed => Window != 0 && ProcessAlive && OwnershipVerified && ForegroundWindow == Window && !Minimized;
        public string? BlockReason => Window == 0 ? "Connect to the game first." :
            !ProcessAlive ? "The connected game process is unavailable. Use Connect / refresh." :
            !OwnershipVerified ? "The game window identity could not be verified. Use Connect / refresh." :
            Minimized ? "Restore the game window before starting." :
            ForegroundWindow != Window ? "Switch to the game and press F8 to calibrate and hunt." : null;
    }
    public sealed record Report(DateTime TimeUtc, int ProcessId, long MainWindowHandle,
        string? MainWindowError, bool EnumWindowsSucceeded, int EnumWindowsError,
        string? EnumerationError, int FindWindowExError, bool FindWindowExLimitReached,
        IReadOnlyList<Candidate> Candidates, long SelectedHandle, string SelectionStatus)
    {
        public IReadOnlyList<ThreadCheck> ThreadChecks { get; init; } = [];
        public string? ThreadDiscoveryError { get; init; }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    delegate bool EnumCallback(nint window, nint parameter);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumWindows(EnumCallback callback, nint parameter);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumThreadWindows(uint threadId, EnumCallback callback, nint parameter);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern SafeWaitHandle OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint threadId);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint GetProcessIdOfThread(SafeWaitHandle thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    static extern nint FindWindowExW(nint parent, nint after, string? className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int GetWindowTextW(nint window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int GetClassNameW(nint window, StringBuilder text, int count);
    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")]
    static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsWindow(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetClientRect(nint window, out Rect rect);

    public static Report Inspect(Process game, bool verifyThreads = false)
    {
        int processId = game.Id;
        long original = 0;
        string? originalError = null;
        try { game.Refresh(); original = game.MainWindowHandle.ToInt64(); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        { originalError = ex.Message; }

        var candidates = new Dictionary<long, Candidate>();
        void Capture(nint window, string source, uint verifiedThreadId = 0)
        {
            if (window == 0 || !IsWindow(window)) return;
            uint ownerPid = 0;
            uint directThreadId = GetWindowThreadProcessId(window, out ownerPid);
            int directError = directThreadId == 0 ? Marshal.GetLastWin32Error() : 0;
            if (verifiedThreadId != 0 && directThreadId != 0 && verifiedThreadId != directThreadId) return;
            int directPid = unchecked((int)ownerPid);
            // A missing direct PID can be recovered from a queried thread owner.
            // A conflicting nonzero PID must never be replaced by that evidence.
            if (ownerPid == 0 && verifiedThreadId != 0) ownerPid = (uint)processId;
            var title = new StringBuilder(1024);
            GetWindowTextW(window, title, title.Capacity);
            if (ownerPid != (uint)processId && !ExactTitle(title.ToString())) return;
            var className = new StringBuilder(256);
            GetClassNameW(window, className, className.Capacity);
            bool hasRect = GetClientRect(window, out var rect);
            long handle = window.ToInt64();
            if (candidates.TryGetValue(handle, out var previous)) source = previous.Source + ", " + source;
            candidates[handle] = new(handle, unchecked((int)ownerPid), GetWindow(window, 4).ToInt64(),
                IsWindowVisible(window), title.ToString(), className.ToString(),
                hasRect ? Math.Max(0, rect.Right - rect.Left) : 0,
                hasRect ? Math.Max(0, rect.Bottom - rect.Top) : 0, source)
                { DirectProcessId = directPid, DirectThreadId = directThreadId, DirectQueryError = directError, VerifiedThreadId = verifiedThreadId };
        }

        if (original != 0) Capture((nint)original, "Process.MainWindowHandle");
        string? enumerationError = null;
        int enumerated = 0;
        EnumCallback callback = (window, _) =>
        {
            // Never allow an exception to cross the unmanaged callback boundary.
            try
            {
                if (++enumerated > 4096) { enumerationError = "Window enumeration limit reached."; return false; }
                Capture(window, "EnumWindows");
                return true;
            }
            catch (Exception ex) { enumerationError = ex.Message; return false; }
        };
        bool enumSucceeded = EnumWindows(callback, 0);
        int enumError = enumSucceeded ? 0 : Marshal.GetLastWin32Error();
        GC.KeepAlive(callback);

        int findError = 0;
        bool findLimit = false;
        if (!candidates.Values.Any(c => Eligible(c, processId) && ExactTitle(c.Title)))
        {
            // This standard API can find an owned render window omitted by MainWindowHandle.
            nint after = 0;
            var seen = new HashSet<nint>();
            for (int i = 0; i < 64; i++)
            {
                nint window = FindWindowExW(0, after, null, GameTitle);
                if (window == 0) { findError = Marshal.GetLastWin32Error(); break; }
                if (!seen.Add(window)) { findLimit = true; break; }
                Capture(window, "FindWindowExW");
                after = window;
                if (i == 63) findLimit = true;
            }
        }

        var threadChecks = new List<ThreadCheck>();
        string? threadError = null;
        if (verifyThreads || !candidates.Values.Any(c => Eligible(c, processId)))
        {
            try
            {
                game.Refresh();
                var threads = game.Threads;
                try
                {
                    if (threads.Count > 512) throw new InvalidOperationException("Too many game threads to inspect.");
                    var threadTimer = Stopwatch.StartNew();
                    foreach (ProcessThread thread in threads)
                    {
                        if (threadTimer.Elapsed.TotalSeconds > 2) throw new InvalidOperationException("Thread-window discovery exceeded its time limit.");
                        uint threadId = (uint)thread.Id;
                        using var threadHandle = OpenThread(0x0800, false, threadId);
                        uint threadPid = threadHandle.IsInvalid ? 0 : GetProcessIdOfThread(threadHandle);
                        if (threadPid != (uint)processId)
                        {
                            threadChecks.Add(new(threadId, threadPid, 0, Marshal.GetLastWin32Error()));
                            continue;
                        }
                        int count = 0;
                        string? callbackError = null;
                        EnumCallback threadCallback = (window, _) =>
                        {
                            try
                            {
                                if (++count > 1024) { callbackError = "Thread-window enumeration limit reached."; return false; }
                                Capture(window, "EnumThreadWindows (queried thread owner)", threadId);
                                return true;
                            }
                            catch (Exception ex) { callbackError = ex.Message; return false; }
                        };
                        bool completed = EnumThreadWindows(threadId, threadCallback, 0);
                        int error = completed ? 0 : Marshal.GetLastWin32Error();
                        GC.KeepAlive(threadCallback);
                        threadChecks.Add(new(threadId, threadPid, count, error));
                        if (callbackError != null) throw new InvalidOperationException(callbackError);
                        if (!completed && count > 0) throw new InvalidOperationException("Thread-window enumeration stopped before completing.");
                    }
                }
                finally { foreach (ProcessThread thread in threads) thread.Dispose(); }
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
            { threadError = ex.Message; }
        }
        var rows = candidates.Values.OrderBy(c => c.Handle).ToArray();
        var selection = Select(rows, processId);
        if (enumerationError != null) selection = (0, enumerationError);
        if (findLimit) selection = (0, "Title-window discovery did not complete.");
        if (threadError != null) selection = (0, threadError);
        if (selection.Handle != 0)
        {
            if (!Validate(rows.Single(c => c.Handle == selection.Handle), processId))
                selection = (0, "The game window changed during discovery.");
        }
        return new(DateTime.UtcNow, processId, original, originalError, enumSucceeded, enumError,
            enumerationError, findError, findLimit, rows, selection.Handle, selection.Status)
            { ThreadChecks = threadChecks, ThreadDiscoveryError = threadError };
    }

    public static nint Find(Process game, out Candidate identity)
    {
        var attempts = new List<Report>();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var report = Inspect(game);
            if (report.SelectedHandle != 0)
            {
                if (!Validate(report.Candidates.Single(c => c.Handle == report.SelectedHandle), game.Id))
                    report = report with { SelectedHandle = 0, SelectionStatus = "The game window changed before connection." };
            }
            attempts.Add(report);
            Save(attempts);
            if (report.SelectedHandle != 0)
            {
                identity = report.Candidates.Single(c => c.Handle == report.SelectedHandle);
                return (nint)report.SelectedHandle;
            }
            if (attempt == 0) Thread.Sleep(300);
        }
        throw new InvalidOperationException($"No usable game window was found for Client PID {game.Id}. {attempts[^1].SelectionStatus} Details: game-window-check.json.");
    }

    static bool Validate(Candidate candidate, int processId)
    {
        nint window = (nint)candidate.Handle;
        if (candidate.ProcessId != processId || !IsWindow(window)) return false;
        uint expectedThreadId = candidate.VerifiedThreadId != 0 ? candidate.VerifiedThreadId : candidate.DirectThreadId;
        uint directPid = 0;
        uint directThreadId = GetWindowThreadProcessId(window, out directPid);
        if (directThreadId != 0 && expectedThreadId != 0 && directThreadId != expectedThreadId) return false;
        if (directPid != 0 && directPid != (uint)processId) return false;
        if (expectedThreadId == 0) return directPid == (uint)processId;
        using var thread = OpenThread(0x0800, false, expectedThreadId);
        if (thread.IsInvalid || GetProcessIdOfThread(thread) != (uint)processId) return false;
        bool found = false;
        EnumCallback callback = (current, _) => { if (current != window) return true; found = true; return false; };
        EnumThreadWindows(expectedThreadId, callback, 0);
        GC.KeepAlive(callback);
        return found && IsWindow(window);
    }

    public static InputState CheckInput(Candidate? identity, int processId, bool processAlive = true)
    {
        long window = identity?.Handle ?? 0;
        bool owned = processAlive && identity != null && processId != 0 && Validate(identity, processId);
        return new(window, Input.GetForegroundWindow().ToInt64(), owned, window != 0 && Input.IsIconic((nint)window), processAlive);
    }

    static void Save(IReadOnlyList<Report> attempts)
    {
        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "game-window-check.json"),
                JsonSerializer.Serialize(new { Attempts = attempts }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static bool ExactTitle(string title) => string.Equals(title, GameTitle, StringComparison.OrdinalIgnoreCase);
    static bool Eligible(Candidate candidate, int processId) => candidate.Handle != 0 &&
        candidate.ProcessId == processId && candidate.Visible && candidate.ClientWidth >= 320 && candidate.ClientHeight >= 200 &&
        candidate.ClassName is not ("#32770" or "tooltips_class32" or "IME" or "MSCTFIME UI");

    static (long Handle, string Status) Select(IEnumerable<Candidate> candidates, int processId)
    {
        var eligible = candidates.Where(c => Eligible(c, processId)).DistinctBy(c => c.Handle).ToArray();
        var exact = eligible.Where(c => ExactTitle(c.Title)).ToArray();
        if (exact.Length == 1) return (exact[0].Handle, "Matched the game title and verified process ID.");
        if (exact.Length > 1) return (0, "More than one visible game window matches the title and process ID.");
        if (eligible.Length == 1) return (eligible[0].Handle, "Matched the only visible render-size window belonging to the verified process.");
        if (eligible.Length > 1) return (0, "Multiple visible windows belong to the game; selection is ambiguous.");
        return (0, "No visible game window with a client area of at least 320 by 200 pixels was available.");
    }

    public static void SelfTest()
    {
        if (!new InputState(10,10,true,false).Allowed ||
            new InputState(10,11,true,false).Allowed || new InputState(10,10,false,false).Allowed ||
            new InputState(10,10,true,true).Allowed || new InputState(0,0,false,false).Allowed ||
            new InputState(10,10,true,false,false).Allowed)
            throw new InvalidOperationException("Game input gate failed foreground, ownership, minimized or disconnected checks.");
        const int pid = 123;
        var render = new Candidate(10, pid, 99, true, GameTitle, "GameRender", 1280, 720, "fixture");
        void Check(string name, long expected, params Candidate[] rows)
        {
            if (Select(rows, pid).Handle != expected) throw new InvalidOperationException("Game-window selection test failed: " + name);
        }
        Check("owned render window", 10, render);
        Check("foreign process title", 0, render with { ProcessId = 456 });
        Check("hidden window", 0, render with { Visible = false });
        Check("zero client area", 0, render with { ClientWidth = 0, ClientHeight = 0 });
        Check("small helper", 0, render with { ClientWidth = 319 });
        Check("dialog helper", 0, render with { ClassName = "#32770" });
        Check("exact-title ambiguity", 0, render, render with { Handle = 11 });
        Check("untitled render fallback", 10, render with { Title = "" });
        Check("fallback ambiguity", 0, render with { Title = "A" }, render with { Handle = 11, Title = "B" });
        Check("exact-title preference", 10, render, render with { Handle = 11, Title = "Other" });
        Check("duplicate discovery", 10, render, render with { Source = "second fixture" });
    }
}
