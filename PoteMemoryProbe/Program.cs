using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace PoteMemoryProbe;

static class Native
{
    internal const uint ReadAndQuery = 0x0010 | 0x0400;
    [StructLayout(LayoutKind.Sequential)]
    internal struct Region
    {
        public nint BaseAddress, AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State, Protect, Type;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, byte[] buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool WriteProcessMemory(SafeProcessHandle process, nint address, byte[] buffer, nuint size, out nuint written);
    [StructLayout(LayoutKind.Sequential)]
    struct Region32 { public uint BaseAddress, AllocationBase, AllocationProtect, RegionSize, State, Protect, Type; }
    [DllImport("kernel32.dll", EntryPoint="VirtualQueryEx", SetLastError = true)]
    static extern nuint VirtualQuery64(SafeProcessHandle process, nint address, out Region info, nuint size);
    [DllImport("kernel32.dll", EntryPoint="VirtualQueryEx", SetLastError = true)]
    static extern nuint VirtualQuery32(SafeProcessHandle process, nint address, out Region32 info, nuint size);
    internal static nuint VirtualQueryEx(SafeProcessHandle process, nint address, out Region info, nuint size)
    {
        if(IntPtr.Size==8) return VirtualQuery64(process,address,out info,(nuint)Marshal.SizeOf<Region>());
        nuint result=VirtualQuery32(process,address,out var region,(nuint)Marshal.SizeOf<Region32>());
        info=new Region {BaseAddress=(nint)region.BaseAddress,AllocationBase=(nint)region.AllocationBase,
            AllocationProtect=region.AllocationProtect,RegionSize=region.RegionSize,State=region.State,Protect=region.Protect,Type=region.Type};
        return result;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref uint size);
    internal static bool Readable(Region r) => r.State == 0x1000 && (r.Protect & 0x100) == 0 && (r.Protect & 0xff) is 2 or 4 or 8 or 0x20 or 0x40 or 0x80;
    internal static bool Writable(Region r) => (r.Protect & 0xff) is 4 or 8 or 0x40 or 0x80;
    internal static string PathOf(SafeProcessHandle handle)
    {
        var text = new StringBuilder(32768); uint length = (uint)text.Capacity;
        if (!QueryFullProcessImageName(handle, 0, text, ref length)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return text.ToString();
    }
    internal static SafeProcessHandle Open(int pid)
    {
        var handle = OpenProcess(ReadAndQuery, false, pid);
        if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error, "Read-only OpenProcess failed"); }
        return handle;
    }
    internal static SafeProcessHandle OpenForTargetSelection(int pid)
    {
        const uint processVmOperation=0x0008,processVmWrite=0x0020;
        var handle=OpenProcess(ReadAndQuery|processVmOperation|processVmWrite,false,pid);
        if(handle.IsInvalid){int error=Marshal.GetLastWin32Error();handle.Dispose();throw new Win32Exception(error,"Target-selection write access was denied");}
        return handle;
    }
    internal static void Write(SafeProcessHandle process,nint address,byte[] bytes)
    {
        if(bytes.Length==0)throw new ArgumentOutOfRangeException(nameof(bytes));
        if(!WriteProcessMemory(process,address,bytes,(nuint)bytes.Length,out var written)||written!=(nuint)bytes.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(),$"Memory write failed at 0x{address:X}");
    }
    internal static byte[] Read(SafeProcessHandle handle, nint address, int count)
    {
        var buffer = new byte[count];
        bool succeeded = ReadProcessMemory(handle, address, buffer, (nuint)count, out var read);
        int error = succeeded ? 0 : Marshal.GetLastWin32Error();
        if (!succeeded || read != (nuint)count)
        {
            if(IntPtr.Size==8 && error is 0 or 5 && WindowsClientRead.TryRead(handle,address,buffer,out var nativeRead,out var nativeStatus))
                return buffer;
            throw new Win32Exception(error, $"Memory read failed at 0x{address:X}: requested {count} bytes, received {read}; API success={succeeded}, Windows error={error}.");
        }
        return buffer;
    }

    [DllImport("psapi.dll", CharSet=CharSet.Unicode, SetLastError=true, ExactSpelling=true)]
    static extern uint GetMappedFileNameW(SafeProcessHandle process, nint address, StringBuilder name, uint length);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true, ExactSpelling=true)]
    static extern uint QueryDosDeviceW(string name, StringBuilder target, uint length);
    internal static long MainImageBase(SafeProcessHandle handle, string executablePath)
    {
        // Query the image mapping without reading the target's loader list.
        string fullPath = Path.GetFullPath(executablePath);
        var device = new StringBuilder(32768);
        if (fullPath.Length < 3 || fullPath[1] != ':' || QueryDosDeviceW(fullPath[..2], device, (uint)device.Capacity) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot resolve the client volume.");
        string expected = device.ToString().Split('\0')[0] + fullPath[2..];
        var visited = new HashSet<nint>();
        ulong cursor = 0, limit = IntPtr.Size == 8 ? 0x00007FFFFFFF0000UL : 0xFFFF0000UL;
        var elapsed = Stopwatch.StartNew();
        for (int regions = 0; regions < 20000 && cursor < limit; regions++)
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Client image lookup exceeded five seconds.");
            if (VirtualQueryEx(handle, (nint)cursor, out var region, (nuint)Marshal.SizeOf<Region>()) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot query the client image map.");
            if (region.Type == 0x1000000 && visited.Add(region.AllocationBase))
            {
                var mapped = new StringBuilder(32768);
                if (GetMappedFileNameW(handle, region.AllocationBase, mapped, (uint)mapped.Capacity) > 0 &&
                    mapped.ToString().Equals(expected, StringComparison.OrdinalIgnoreCase))
                    return (long)(nuint)region.AllocationBase;
            }
            ulong next = (ulong)(nuint)region.BaseAddress + (ulong)region.RegionSize;
            if (next <= cursor) break;
            cursor = next;
        }
        throw new InvalidOperationException("The verified client executable was not found in its image map.");
    }
}

sealed class Report
{
    public string TimestampUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public string Status { get; set; } = "NotStarted";
    public string Message { get; set; } = "";
    public string ClientPath { get; set; } = Program.ClientPath;
    public string ExpectedSha256 { get; set; } = Program.ExpectedHash;
    public string? ActualSha256 { get; set; }
    public int? ProcessId { get; set; }
    public string? ModuleBase { get; set; }
    public int? WindowsError { get; set; }
    public bool HeaderReadVerified { get; set; }
    public bool MemoryMapCompleted { get; set; }
    public bool ScanTruncated { get; set; }
    public int ReadableRegions { get; set; }
    public long BytesRead { get; set; }
    public int FailedReads { get; set; }
    public List<ClassReport> Classes { get; set; } = new();
}
sealed class ClassReport
{
    public string TypeName { get; set; } = "";
    public string VtableRva { get; set; } = "";
    public string? RuntimeVtable { get; set; }
    public List<string> CandidateAddresses { get; set; } = new();
    public bool CandidateLimitReached { get; set; }
}

static class Program
{
    internal const string ClientPath = @"C:\Program Files (x86)\AAT-Games\Client.exe";
    internal const string ExpectedHash = "13497aa1b4db51815336072d6c632e6c220a37035c1012a07e8d0d19aa4e39b5";
    static readonly (string Name, uint Rva)[] Types = [
        ("CRYLSceneObject", 0x3f8030), ("CCharacterControl", 0x42e58c),
        ("Broadcast2nd::CCharacterData", 0x436904), ("Broadcast2nd::CMonsterData", 0x436914),
        ("RYLCreature", 0x42dfd4)
    ];
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    static int Main(string[] args)
    {
        if (args.Contains("--fixture")) return Fixture();
        if (args.Contains("--self-test")) return SelfTest();
        if (args.SequenceEqual(["--sample"])) return Sample();
        if (args.Any(a => a != "--scan")) { Console.Error.WriteLine("Usage: PoteMemoryProbe.exe [--scan | --self-test]"); return 1; }
        var report = new Report();
        int code;
        try { code = Probe(report, args.Contains("--scan")); }
        catch (Win32Exception ex) { report.Status = "AccessFailed"; report.WindowsError = ex.NativeErrorCode; report.Message = ex.Message; code = 3; }
        catch (Exception ex) { report.Status = "Error"; report.Message = ex.Message; code = 4; }
        Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions));
        try
        {
            string reportPath = Path.Combine(AppContext.BaseDirectory, "probe-report.json");
            File.WriteAllText(reportPath + ".tmp", JsonSerializer.Serialize(report, JsonOptions));
            File.Move(reportPath + ".tmp", reportPath, true);
            Console.WriteLine($"Report saved: {reportPath}");
        }
        catch (Exception ex) { Console.Error.WriteLine($"Could not save report: {ex.Message}"); return 4; }
        return code;
    }
    static int Sample()
    {
        try
        {
            var previous = JsonSerializer.Deserialize<Report>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "probe-report.json"))) ?? throw new Exception("No scan report.");
            if (previous.Status != "CandidateScanCompleted" || previous.ProcessId == null) throw new Exception("Run a candidate scan first.");
            using var file = File.OpenRead(ClientPath);
            if (Convert.ToHexStringLower(SHA256.HashData(file)) != ExpectedHash) throw new Exception("Client version changed.");
            using var handle = Native.Open(previous.ProcessId.Value);
            if (!Native.PathOf(handle).Equals(ClientPath, StringComparison.OrdinalIgnoreCase)) throw new Exception("Process identity changed.");
            using var process = Process.GetProcessById(previous.ProcessId.Value);
            long moduleBase = process.MainModule?.BaseAddress.ToInt64() ?? throw new Exception("No module.");
            if ($"0x{moduleBase:X8}" != previous.ModuleBase) throw new Exception("Module base changed. Rescan.");
            var rows = new List<object>();
            foreach (var type in previous.Classes)
            {
                var known = Types.Single(t => t.Name == type.TypeName);
                foreach (string addressText in type.CandidateAddresses)
                {
                    long address = Convert.ToInt64(addressText[2..], 16);
                    byte[] data;
                    int objectSize = type.TypeName == "RYLCreature" ? 720 : type.TypeName == "CCharacterControl" ? 512 : type.TypeName == "Broadcast2nd::CCharacterData" ? 208 : 52;
                    try { data = Native.Read(handle, (nint)address, objectSize); } catch (Win32Exception) { continue; }
                    if (BitConverter.ToUInt32(data, 0) != moduleBase + known.Rva) continue;
                    string? name = null;
                    if (type.TypeName == "RYLCreature")
                    {
                        uint length = BitConverter.ToUInt32(data, 0x28), capacity = BitConverter.ToUInt32(data, 0x2c);
                        if (length is > 0 and <= 63 && capacity >= length)
                        {
                            try
                            {
                                byte[] nameBytes = capacity < 16 ? data.Skip(0x18).Take((int)length).ToArray() : Native.Read(handle, (nint)BitConverter.ToUInt32(data, 0x18), (int)length);
                                name = Encoding.UTF8.GetString(nameBytes);
                            }
                            catch (Win32Exception) { }
                        }
                    }
                    rows.Add(new { Type = type.TypeName, Address = addressText, Name = name, Words = Enumerable.Range(0, data.Length / 4).Select(i => new { Offset = $"0x{i*4:X2}", Hex = $"0x{BitConverter.ToUInt32(data,i*4):X8}", Int = BitConverter.ToInt32(data,i*4), Float = float.IsFinite(BitConverter.ToSingle(data,i*4)) ? (float?)BitConverter.ToSingle(data,i*4) : null }).ToArray() });
                }
            }
            string path = Path.Combine(AppContext.BaseDirectory, "object-sample.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { TimeUtc = DateTime.UtcNow, ProcessId = previous.ProcessId, Candidates = rows }, JsonOptions));
            Console.WriteLine($"Sampled {rows.Count} type-checked candidate objects: {path}");
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "sample-error.txt"), ex.Message); Console.Error.WriteLine(ex.Message); return 1; }
    }
    static int Probe(Report report, bool scan)
    {
        if (!File.Exists(ClientPath)) { report.Status = "ClientMissing"; report.Message = "Client.exe was not found at the configured installation path."; return 2; }
        using (var stream = File.OpenRead(ClientPath)) report.ActualSha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (report.ActualSha256 != ExpectedHash) { report.Status = "VersionMismatch"; report.Message = "The client changed. Rebuild the class map for this version before scanning."; return 2; }
        var candidates = Process.GetProcessesByName("Client");
        try
        {
            var matching = new List<Process>();
            bool unresolved = false;
            foreach (var p in candidates)
            {
                try
                {
                    using var identityHandle = Native.OpenProcess(0x1000, false, p.Id);
                    if (identityHandle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Limited process identity query failed");
                    if (string.Equals(Native.PathOf(identityHandle), ClientPath, StringComparison.OrdinalIgnoreCase)) matching.Add(p);
                }
                catch (Win32Exception ex) { unresolved = true; report.WindowsError = ex.NativeErrorCode; }
                catch { unresolved = true; }
            }
            if (matching.Count != 1)
            {
                report.Status = matching.Count > 1 ? "MultipleClients" : unresolved ? "IdentityUnavailable" : "GameNotRunning";
                report.Message = matching.Count > 1 ? "Keep exactly one PlayPOTE client open for this diagnostic." : unresolved ? "A Client process exists, but its identity could not be verified." : "Start PlayPOTE normally and log into the world, then run this probe again.";
                return 2;
            }
            report.ProcessId = matching[0].Id;
            using var handle = Native.Open(matching[0].Id);
            if (!string.Equals(Native.PathOf(handle), ClientPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Process identity changed.");
            report.ProcessId = matching[0].Id;
            var module = matching[0].MainModule ?? throw new InvalidOperationException("Main module unavailable.");
            long baseAddress = module.BaseAddress.ToInt64();
            report.ModuleBase = $"0x{baseAddress:X8}";
            var header = Native.Read(handle, (nint)baseAddress, 64);
            report.HeaderReadVerified = header[0] == 'M' && header[1] == 'Z';
            if (!report.HeaderReadVerified) throw new InvalidOperationException("Unexpected module header; scanning stopped.");
            report.Classes = Types.Select(t => new ClassReport { TypeName = t.Name, VtableRva = $"0x{t.Rva:X8}", RuntimeVtable = $"0x{baseAddress + t.Rva:X8}" }).ToList();
            if (scan) Scan(handle, baseAddress, module.ModuleMemorySize, report);
            report.Status = scan ? "CandidateScanCompleted" : "ReadAccessVerified";
            report.Message = scan ? "Candidate addresses are pointer matches, not validated monsters or coordinates. Scan is bounded to 1 GiB / 30 seconds / 200 candidates per type." : "Read access to the client header works. This does not yet establish access to live monster data.";
            return 0;
        }
        finally { foreach (var p in candidates) p.Dispose(); }
    }
    internal static void Scan(SafeProcessHandle handle, long moduleBase, int moduleSize, Report report)
    {
        var timer = Stopwatch.StartNew();
        var regions = new List<Native.Region>();
        long address = 0;
        while (address < 0x100000000L && timer.Elapsed.TotalSeconds < 5)
        {
            if (Native.VirtualQueryEx(handle, (nint)address, out var r, (nuint)Marshal.SizeOf<Native.Region>()) == 0) break;
            long regionBase = (long)(nuint)r.BaseAddress;
            long next = regionBase + checked((long)r.RegionSize);
            if (next <= address) break;
            if (Native.Readable(r))
            {
                report.ReadableRegions++;
                if (r.Type == 0x20000 || (r.Type == 0x1000000 && Native.Writable(r) && regionBase >= moduleBase && regionBase < moduleBase + moduleSize)) regions.Add(r);
            }
            address = next;
        }
        report.MemoryMapCompleted = address >= 0x100000000L;
        report.ScanTruncated = !report.MemoryMapCompleted;
        long examined = 0;
        const long cap = 1024L * 1024 * 1024;
        var words = Types.Select(t => checked((uint)(moduleBase + t.Rva))).ToArray();
        var buffer = new byte[1024 * 1024];
        foreach (var r in regions.OrderBy(r => r.Type == 0x20000 ? 0 : 1))
        {
            for (long offset = 0; offset < (long)r.RegionSize;)
            {
                if (timer.Elapsed.TotalSeconds >= 30 || examined >= cap) { report.ScanTruncated = true; return; }
                int count = (int)Math.Min(buffer.Length, Math.Min((long)r.RegionSize - offset, cap - examined));
                long start = (long)(nuint)r.BaseAddress + offset;
                bool ok = Native.ReadProcessMemory(handle, (nint)start, buffer, (nuint)count, out var read);
                int obtained = (int)Math.Min((nuint)count, read);
                report.BytesRead += obtained;
                if (!ok || obtained != count) report.FailedReads++;
                foreach (var hit in FindWords(buffer, obtained, words))
                {
                    var entry = report.Classes[hit.TypeIndex];
                    if (entry.CandidateAddresses.Count < 200) entry.CandidateAddresses.Add($"0x{start + hit.Offset:X8}");
                    else entry.CandidateLimitReached = true;
                }
                offset += count; examined += count;
            }
        }
    }
    internal static IEnumerable<(int TypeIndex, int Offset)> FindWords(byte[] buffer, int validBytes, uint[] words)
    {
        for (int i = 0; i + 4 <= validBytes; i += 4)
        {
            uint word = BitConverter.ToUInt32(buffer, i);
            for (int t = 0; t < words.Length; t++) if (word == words[t]) yield return (t, i);
        }
    }
    static int Fixture()
    {
        nint block = Marshal.AllocHGlobal(64);
        try
        {
            var bytes = new byte[64];
            BitConverter.GetBytes(0x1234ABCDu).CopyTo(bytes, 0);
            BitConverter.GetBytes(0xBEEF1234u).CopyTo(bytes, 32);
            Marshal.Copy(bytes, 0, block, bytes.Length);
            Console.WriteLine(block.ToInt64()); Console.Out.Flush();
            Console.ReadLine(); return 0;
        }
        finally { Marshal.FreeHGlobal(block); }
    }
    static int SelfTest()
    {
        try
        {
            if (Marshal.SizeOf<Native.Region>() != 48) throw new Exception("Unexpected x64 memory-region layout.");
            var info = new ProcessStartInfo(Environment.ProcessPath!, "--fixture") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardInput = true, CreateNoWindow = true };
            using var child = Process.Start(info) ?? throw new Exception("Could not launch fixture.");
            try
            {
                var line = child.StandardOutput.ReadLineAsync();
                if (!line.Wait(TimeSpan.FromSeconds(5)) || !long.TryParse(line.Result, out long address)) throw new Exception("Fixture handshake failed.");
                using var handle = Native.Open(child.Id);
                if (!Native.PathOf(handle).Equals(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) throw new Exception("Identity validation failed.");
                if (Native.VirtualQueryEx(handle, (nint)address, out var region, (nuint)Marshal.SizeOf<Native.Region>()) == 0 || !Native.Readable(region)) throw new Exception("Memory mapping failed.");
                byte[] bytes = Native.Read(handle, (nint)address, 64);
                var matches = FindWords(bytes, bytes.Length, [0x1234ABCDu, 0xBEEF1234u]).ToArray();
                if (matches.Length != 2 || matches[0] != (0, 0) || matches[1] != (1, 32)) throw new Exception("Candidate matching failed.");
                if (FindWords(bytes, 3, [0x1234ABCDu]).Any()) throw new Exception("Partial-read boundary check failed.");
                if (FindWords(bytes, 64, [0xFFEEDDCCu]).Any()) throw new Exception("False-match check failed.");
                if (Native.Readable(new Native.Region { State = 0x1000, Protect = 0x104 })) throw new Exception("Guard-page exclusion failed.");
                if (Native.Readable(new Native.Region { State = 0x1000, Protect = 1 })) throw new Exception("No-access exclusion failed.");
                if (Native.Readable(new Native.Region { State = 0x2000, Protect = 4 })) throw new Exception("Uncommitted exclusion failed.");
                bool rejected = false;
                try { Native.Read(handle, 0, 4); } catch (Win32Exception) { rejected = true; }
                if (!rejected) throw new Exception("Invalid-read check failed.");
                Console.WriteLine("PASS: cross-process read, identity, region layout/query, candidate matching, partial boundaries, no false matches, guard/no-access/uncommitted exclusions, invalid-read rejection.");
                return 0;
            }
            finally
            {
                if (!child.HasExited) { child.StandardInput.WriteLine(); if (!child.WaitForExit(2000)) child.Kill(); }
            }
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
    }
}
