param([Parameter(Mandatory=$true)][string]$Path)
$ErrorActionPreference='Stop'
# Load resources as data only; never execute the inspected application.
if(-not ('PoteHunterPackaging.ManifestResource' -as [type])) {
    Add-Type @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
namespace PoteHunterPackaging {
    public static class ManifestResource {
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
        static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
        [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr resource);
        [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
        public static string Read(string path) {
            IntPtr module=LoadLibraryEx(path,IntPtr.Zero,0x22);
            if(module==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try {
                IntPtr resource=FindResource(module,(IntPtr)1,(IntPtr)24);
                if(resource==IntPtr.Zero) throw new InvalidOperationException("Executable manifest is missing.");
                uint size=SizeofResource(module,resource);
                IntPtr data=LockResource(LoadResource(module,resource));
                if(data==IntPtr.Zero || size==0 || size>65536) throw new InvalidOperationException("Invalid executable manifest.");
                byte[] bytes=new byte[size];Marshal.Copy(data,bytes,0,bytes.Length);
                return Encoding.UTF8.GetString(bytes).Trim('\uFEFF','\0');
            } finally { FreeLibrary(module); }
        }
    }
}
'@
}
[xml]$manifest=[PoteHunterPackaging.ManifestResource]::Read([IO.Path]::GetFullPath($Path))
$execution=$manifest.SelectNodes("//*[local-name()='requestedExecutionLevel']")
if($execution.Count -ne 1 -or $execution[0].level -ne 'requireAdministrator' -or $execution[0].uiAccess -ne 'false') {
    throw 'PoteHunter must request administrator access by default, without UIAccess.'
}
Write-Output 'Administrator startup verified in the packaged executable manifest.'
