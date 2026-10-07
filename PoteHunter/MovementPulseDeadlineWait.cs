using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PoteHunter;

// One unnamed timer belongs to one short pulse. No global timer-resolution
// change, timer callback, spin loop, or UI continuation occurs while W is held.
internal sealed class MovementPulseDeadlineWait : WaitHandle
{
    const uint HighResolution=2,TimerAccess=0x00100002;
    readonly WaitHandle[] handles;
    static int fallbackReported;
    internal bool IsHighResolution {get;}

    MovementPulseDeadlineWait(SafeWaitHandle timer,bool highResolution,CancellationToken token)
    {
        SafeWaitHandle=timer;IsHighResolution=highResolution;
        // CancellationToken owns this event; only this timer is disposed here.
        // Accessing WaitHandle can allocate, so do it before the input begins.
        handles=token.CanBeCanceled ? new WaitHandle[]{token.WaitHandle,this} : new WaitHandle[]{this};
    }

    internal static MovementPulseDeadlineWait Create(CancellationToken token,bool conventional=false)
    {
        token.ThrowIfCancellationRequested();
        var timer=CreateWaitableTimerExW(IntPtr.Zero,null,conventional ? 0 : HighResolution,TimerAccess);
        bool highResolution=!conventional;
        if(timer.IsInvalid && !conventional)
        {
            int error=Marshal.GetLastWin32Error();timer.Dispose();
            // Older Windows does not recognize the high-resolution flag. Keep
            // the conventional timer on that system, with a once-only notice.
            if(error!=87)throw new Win32Exception(error,"Short movement timer creation failed before input.");
            timer=CreateWaitableTimerExW(IntPtr.Zero,null,0,TimerAccess);highResolution=false;
        }
        if(timer.IsInvalid)
        {
            int error=Marshal.GetLastWin32Error();timer.Dispose();
            throw new Win32Exception(error,"Short movement timer creation failed before input.");
        }
        try
        {
            var wait=new MovementPulseDeadlineWait(timer,highResolution,token);
            if(!highResolution && !conventional && Interlocked.Exchange(ref fallbackReported,1)==0)
            {
                var details=new {Mode="conventional waitable timer",Reason="high-resolution timer unsupported",SafetySliceMilliseconds=4};
                if(!PulseInputTrace.TryRecord("forward pulse timer fallback",details))TraceLog.Record("forward pulse timer fallback",details);
            }
            return wait;
        }
        catch {timer.Dispose();throw;}
    }

    internal void Wait(long stopwatchTicks,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(stopwatchTicks<=0)return;
        // Negative due time is relative, in 100 ns units. Round upward so a
        // conversion never ends a wait before the requested monotonic slice.
        long due=-Math.Max(1,(long)Math.Ceiling(stopwatchTicks*(double)TimeSpan.TicksPerSecond/Stopwatch.Frequency));
        if(!SetWaitableTimerEx(SafeWaitHandle,ref due,0,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,0))
            throw new Win32Exception(Marshal.GetLastWin32Error(),"Short movement timer could not be armed.");
        // A broken timer cannot leave input held indefinitely. The watchdog is
        // exceptional; stop/focus checks remain between requested <=4 ms
        // slices. Windows scheduling can delay the actual wake or release.
        int signaled=WaitAny(handles,16);
        token.ThrowIfCancellationRequested();
        if(signaled==WaitTimeout)throw new TimeoutException("Short movement timer did not signal; input released.");
    }

    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr attributes,string? name,uint flags,uint access);

    [DllImport("kernel32.dll",SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWaitableTimerEx(SafeWaitHandle timer,ref long dueTime,int period,
        IntPtr callback,IntPtr argument,IntPtr wakeContext,uint tolerableDelay);
}
