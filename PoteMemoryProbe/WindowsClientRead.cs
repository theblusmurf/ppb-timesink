using System.ComponentModel;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace PoteMemoryProbe;

// Read-only compatibility path for this verified local game and Windows build.
// The executable stub lives only in this process; installed modules are never patched.
internal static class WindowsClientRead
{
    const string SystemDllHash="a74f7482085eab125ccc09152ab7e0b5994bcb13e1a7b29880bdbb24179ecb8b";
    sealed class VerifiedTarget { }
    static readonly ConditionalWeakTable<SafeProcessHandle,VerifiedTarget> verified=new();
    static readonly Lazy<Reader> reader=new(()=>new Reader());
    static int used;
    public static bool Enabled { get; set; }
    public static string Backend=>Volatile.Read(ref used)==0?"Windows ReadProcessMemory":"Verified Windows native read (local compatibility path)";

    public static bool TryRead(SafeProcessHandle handle,nint address,byte[] buffer,out nuint read,out int status)
    {
        read=0; status=unchecked((int)0xC0000022);
        if(!Enabled) return false;
        if(RuntimeInformation.ProcessArchitecture!=Architecture.X64) return false;
        verified.GetValue(handle,h=>
        {
            if(!Native.PathOf(h).Equals(Program.ClientPath,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Compatibility reads are restricted to the verified game executable.");
            string clientHash=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Program.ClientPath)));
            if(!ClientCompatibility.SupportsRead(clientHash))
                throw new InvalidOperationException($"Game client changed (SHA-256 {clientHash}). This version needs a compatibility check.");
            return new VerifiedTarget();
        });
        bool reference=false;
        var pinned=GCHandle.Alloc(buffer,GCHandleType.Pinned);
        try
        {
            handle.DangerousAddRef(ref reference);
            status=reader.Value.Read(handle.DangerousGetHandle(),address,pinned.AddrOfPinnedObject(),(nuint)buffer.Length,out read);
            bool complete=status>=0 && read==(nuint)buffer.Length;
            if(complete) Interlocked.Exchange(ref used,1);
            return complete;
        }
        finally {if(reference)handle.DangerousRelease();pinned.Free();}
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate int NtRead(nint process,nint address,nint buffer,nuint length,out nuint read);
    [DllImport("kernel32.dll",SetLastError=true)] static extern nint VirtualAlloc(nint address,nuint size,uint type,uint protection);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool VirtualProtect(nint address,nuint size,uint protection,out uint oldProtection);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool VirtualFree(nint address,nuint size,uint type);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool FlushInstructionCache(nint process,nint address,nuint size);

    sealed class Reader
    {
        readonly nint allocation;
        public readonly NtRead Read;
        public Reader()
        {
            string path=Path.Combine(Environment.SystemDirectory,"ntdll.dll");
            byte[] image=File.ReadAllBytes(path);
            if(!Convert.ToHexString(SHA256.HashData(image)).Equals(SystemDllHash,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Windows changed; this build's native-read compatibility path is unavailable.");
            nint module=NativeLibrary.Load(path);
            byte[] stub;
            try
            {
                nint entry=NativeLibrary.GetExport(module,"NtReadVirtualMemory");
                int rva=checked((int)(entry.ToInt64()-module.ToInt64()));
                using var file=new MemoryStream(image,false);using var pe=new PEReader(file);
                var section=pe.PEHeaders.SectionHeaders.Single(s=>rva>=s.VirtualAddress && rva+24<=s.VirtualAddress+s.SizeOfRawData);
                int offset=section.PointerToRawData+rva-section.VirtualAddress;
                stub=image.AsSpan(offset,24).ToArray();
                if(!stub.AsSpan(0,4).SequenceEqual(new byte[]{0x4c,0x8b,0xd1,0xb8}) ||
                    !stub.AsSpan(8).SequenceEqual(Convert.FromHexString("F604250803FE7F0175030F05C3CD2EC3")))
                    throw new InvalidOperationException("The verified Windows read entry has an unexpected instruction layout.");
            }
            finally {NativeLibrary.Free(module);}
            nint memory=VirtualAlloc(0,(nuint)stub.Length,0x3000,4);
            if(memory==0) throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot allocate the local read entry.");
            try
            {
                Marshal.Copy(stub,0,memory,stub.Length);
                if(!VirtualProtect(memory,(nuint)stub.Length,0x20,out _) || !FlushInstructionCache(-1,memory,(nuint)stub.Length))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot initialize the local read entry.");
                Read=Marshal.GetDelegateForFunctionPointer<NtRead>(memory);
                allocation=memory;
            }
            catch {VirtualFree(memory,0,0x8000);throw;}
        }
        ~Reader(){if(allocation!=0)VirtualFree(allocation,0,0x8000);}
    }
}
