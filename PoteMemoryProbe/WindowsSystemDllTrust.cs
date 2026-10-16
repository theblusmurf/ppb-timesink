using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PoteMemoryProbe;

// Windows servicing changes whole-file hashes without necessarily changing the
// exports used by the compatibility path. Trust the signed system file, then
// let each caller validate the exact export bytes it needs.
internal static class WindowsSystemDllTrust
{
    static readonly Guid AuthenticodePolicy = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct FileInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Path;
        public nint Handle;
        public nint Subject;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TrustData
    {
        public uint Size;
        public nint PolicyCallback;
        public nint SipClient;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public nint File;
        public uint StateAction;
        public nint StateData;
        public nint UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public nint SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    static extern int WinVerifyTrust(nint window, ref Guid action, ref TrustData data);

    internal static void Verify(string path, ReadOnlySpan<byte> image, string expectedName)
    {
        string systemPath = Path.Combine(Environment.SystemDirectory, expectedName);
        if (!OperatingSystem.IsWindows() ||
            !Path.GetFullPath(path).Equals(systemPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Native compatibility requires the Windows system DLL.");

        // WinVerifyTrust verifies the PE's Authenticode signature and trust
        // chain without opening UI or downloading certificate data.
        var info = new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), Path = path };
        nint infoPointer = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        try
        {
            Marshal.StructureToPtr(info, infoPointer, false);
            var data = new TrustData {
                Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2,
                UnionChoice = 1, File = infoPointer, ProviderFlags = 0x1000
            };
            Guid policy = AuthenticodePolicy;
            if (WinVerifyTrust(0, ref policy, ref data) != 0)
                throw new InvalidOperationException("Windows system DLL signature is not trusted; native compatibility stopped.");
        }
        finally
        {
            Marshal.DestroyStructure<FileInfo>(infoPointer);
            Marshal.FreeHGlobal(infoPointer);
        }

#pragma warning disable SYSLIB0057 // Extract signer only; WinVerifyTrust above validates the file signature.
        using var signer = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
        if (!signer.Subject.StartsWith("CN=Microsoft Windows, O=Microsoft Corporation,", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Windows system DLL has an unexpected signer; native compatibility stopped.");

        // Reject a file swap between our initial read and signature check.
        byte[] afterVerification = File.ReadAllBytes(path);
        if (!SHA256.HashData(image).AsSpan().SequenceEqual(SHA256.HashData(afterVerification)))
            throw new InvalidOperationException("Windows system DLL changed during validation; native compatibility stopped.");
    }
}
