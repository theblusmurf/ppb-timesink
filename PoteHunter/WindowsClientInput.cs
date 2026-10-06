using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace PoteHunter;

// Explicit opt-in for the reviewed local client. Calls the existing Windows export;
// it never changes installed code, creates executable memory, or loads a driver.
internal static class WindowsClientInput
{
    const string DllHash = "9cd636e8a1b300e2f4b16f339d650eda71fa02b92b100e4b2f89ce7c04811767";
    static readonly byte[] ExpectedEntry = Convert.FromHexString("4C8BD1B87A100000F604250803FE7F0175030F05C3CD2EC3");
    static readonly Lazy<VerifiedExport> export = new(() => new VerifiedExport());
    static World? target;
    static int used;
    public static bool Enabled { get; set; }
    internal static long DeadlineTicks { get; set; } = long.MaxValue;
    public static string Backend => Volatile.Read(ref used) == 0 ? "Windows SendInput" : "Verified Windows native input (local compatibility path)";
    public static int SuccessfulPackets => Volatile.Read(ref used);
    public static void Bind(World world) => target = world;

    internal static string? ConnectionBlockReason(bool readerEnabled,bool connected,string hash)
    {
        if(!readerEnabled)return "Input compatibility requires the native-read startup option. Run Start-Fixed-Detection.cmd.";
        if(!connected)return "Input is unavailable because client connection validation has not completed. Connect to the game first.";
        if(!PoteMemoryProbe.ClientCompatibility.SupportsRead(hash))return $"Client signature discovery has not approved this file (SHA-256 {hash}). Reconnect to validate the updated client.";
        return null;
    }

    static World VerifiedTarget()
    {
        string? blocked=ConnectionBlockReason(PoteMemoryProbe.WindowsClientRead.Enabled,target?.ConnectionVerified==true,target?.ClientHash??"");
        if(blocked!=null)throw new InvalidOperationException(blocked);
        return target!;
    }

    // Read-only readiness check: no mouse or keyboard event is submitted.
    internal static void ValidateReady()
    {
        if(!Enabled)return;
        var world=VerifiedTarget();
        var state=world.CheckInputWindow();
        if(!state.ProcessAlive || !state.OwnershipVerified)throw new InvalidOperationException("The connected game window is no longer available.");
        export.Value.ValidateEntry();
    }

    internal static bool TrySend(Input.Packet packet, int size, out uint sent, out int error)
    {
        sent = 0; error = 0;
        if (!Enabled) return false;
        var world=VerifiedTarget();
        var state = world.CheckInputWindow();
        if (!state.ProcessAlive || !state.OwnershipVerified) throw new OperationCanceledException("The verified game window is no longer available.");
        // Releases may occur after focus changes; new presses and turns must still be in game.
        bool release = packet.Type == 1 ? packet.Value.Keyboard.Flags == 10 : packet.Type == 0 &&
            packet.Value.Mouse.Flags is 4 or 16 && packet.Value.Mouse.X == 0 && packet.Value.Mouse.Y == 0 && packet.Value.Mouse.Data == 0;
        if (!release && !state.Allowed) throw new OperationCanceledException(state.BlockReason);
        var verified = export.Value;
        verified.ValidateEntry();
        state = world.CheckInputWindow();
        if (Environment.TickCount64 >= DeadlineTicks || !state.ProcessAlive || !state.OwnershipVerified || (!release && !state.Allowed))
            throw new OperationCanceledException("Input stopped: deadline or game window changed.");
        sent = verified.Send(1, [packet], size);
        error = Marshal.GetLastWin32Error();
        if (sent == 1) Interlocked.Increment(ref used);
        try
        {
            var details=new { Requested = 1, Sent = sent, WindowsError = error,
                PacketType = packet.Type, Flags = packet.Type == 1 ? packet.Value.Keyboard.Flags : packet.Value.Mouse.Flags, PacketSize = size };
            if(!PulseInputTrace.TryRecord("compatibility input",details))TraceLog.Record("compatibility input",details);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return true;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    delegate uint NativeSend(uint count, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] Input.Packet[] packets, int size);

    sealed class VerifiedExport
    {
        nint module;
        readonly nint address;
        public readonly NativeSend Send;
        public VerifiedExport()
        {
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64 || Marshal.SizeOf<Input.Packet>() != 40)
                throw new InvalidOperationException("Input compatibility requires the reviewed 64-bit input layout.");
            string path = Path.Combine(Environment.SystemDirectory, "win32u.dll");
            byte[] image = File.ReadAllBytes(path);
            if (!Convert.ToHexString(SHA256.HashData(image)).Equals(DllHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Windows changed; input compatibility needs a new check.");
            module = NativeLibrary.Load(path);
            try
            {
                address = NativeLibrary.GetExport(module, "NtUserSendInput");
                using var stream = new MemoryStream(image); using var pe = new PEReader(stream);
                long rva = address.ToInt64() - module.ToInt64();
                var section = pe.PEHeaders.SectionHeaders.Single(s => rva >= s.VirtualAddress && rva + ExpectedEntry.Length <= (long)s.VirtualAddress + s.SizeOfRawData);
                int offset = checked(section.PointerToRawData + (int)rva - section.VirtualAddress);
                if (!image.AsSpan(offset, ExpectedEntry.Length).SequenceEqual(ExpectedEntry)) throw new InvalidOperationException("Unexpected Windows input export.");
                ValidateEntry();
                Send = Marshal.GetDelegateForFunctionPointer<NativeSend>(address);
            }
            catch { NativeLibrary.Free(module); module = 0; throw; }
        }
        public void ValidateEntry()
        {
            var actual = new byte[ExpectedEntry.Length]; Marshal.Copy(address, actual, 0, actual.Length);
            if (!actual.SequenceEqual(ExpectedEntry)) throw new InvalidOperationException("The Windows input export changed; compatibility stopped.");
        }
        ~VerifiedExport() { if (module != 0) NativeLibrary.Free(module); }
    }
}
