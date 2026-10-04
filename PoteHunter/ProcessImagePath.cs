using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PoteHunter;

// Query only the executable identity. Process.MainModule enumerates every loaded
// module and can block indefinitely on the live protected game client.
internal static class ProcessImagePath
{
    [DllImport("kernel32.dll",SetLastError=true)]
    static extern SafeProcessHandle OpenProcess(uint access,[MarshalAs(UnmanagedType.Bool)] bool inherit,int processId);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool QueryFullProcessImageNameW(SafeProcessHandle process,uint flags,StringBuilder path,ref uint length);

    internal static string Read(Process process)
    {
        using var handle=OpenProcess(0x1000,false,process.Id); // PROCESS_QUERY_LIMITED_INFORMATION
        if(handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error(),"Could not verify the executable path. Keep PlayPoteBot and the client at compatible permissions.");
        var path=new StringBuilder(32768);uint length=(uint)path.Capacity;
        if(!QueryFullProcessImageNameW(handle,0,path,ref length) || length==0)
            throw new Win32Exception(Marshal.GetLastWin32Error(),"The executable path is unavailable. No login input sent.");
        return Path.GetFullPath(path.ToString());
    }
}
