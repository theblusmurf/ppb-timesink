using System.Runtime.InteropServices;

namespace PoteHunter;

internal sealed class HeldInputs<T>(Action<T, bool> emit,object? synchronization=null) where T : notnull
{
    readonly HashSet<T> held = new();
    readonly object gate=synchronization ?? new object();
    public bool IsHeld(T key) {lock(gate)return held.Contains(key);}
    public void Set(T key, bool down)
    {
        lock(gate)
        {
            if (down)
            {
                if (held.Contains(key)) return;
                emit(key, true);
                held.Add(key);
            }
            else if (held.Contains(key)) { emit(key, false); held.Remove(key); }
        }
    }
    public void ReleaseAll(Func<T,bool>? preserve = null)
    {
        lock(gate)
            foreach (var key in held.ToArray()) { if(preserve?.Invoke(key)==true)continue;try { Set(key, false); } catch { /* Retry a failed key-up on the next release. */ } }
    }
}

public static class Input
{
    [StructLayout(LayoutKind.Sequential)] internal struct Mouse { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct Keyboard { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Explicit)] internal struct Union { [FieldOffset(0)] public Mouse Mouse; [FieldOffset(0)] public Keyboard Keyboard; }
    [StructLayout(LayoutKind.Sequential)] internal struct Packet { public uint Type; public Union Value; }
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [StructLayout(LayoutKind.Sequential)] struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll",SetLastError=true)] static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetCursorPos(int x,int y);
    internal static bool AlignPointer(Point destination,Func<Point> read,Func<Point,bool> position,Action validate)
    {
        static bool Near(Point actual,Point expected)=>Math.Abs(actual.X-expected.X)<=2 && Math.Abs(actual.Y-expected.Y)<=2;
        validate();
        if(Near(read(),destination))return true;
        // A foreground game may displace an injected absolute move before the
        // final click. Correct once with Windows screen coordinates, then
        // require an actual cursor match; never admit a displaced click.
        validate();
        if(!position(destination))return false;
        validate();
        return Near(read(),destination);
    }
    internal static bool AlignPointer(Point screen,CancellationToken token,string traceStage="revival pointer alignment")
    {
        if(!SystemInformation.VirtualScreen.Contains(screen))throw new InvalidOperationException("Revival pointer destination is outside the desktop.");
        Point before=Cursor();
        bool aligned=AlignPointer(screen,Cursor,point=>SetCursorPos(point.X,point.Y),()=>Check(token));
        TraceLog.Record(traceStage,new{Expected=screen,Before=before,Actual=Cursor(),Aligned=aligned,Method="Windows screen coordinates"});
        return aligned;
    }
    // Opening the death dialog is a click on the central background, not on a
    // recognised button. Some clients constrain/recentre the pointer 50 pixels
    // above the requested centre. Accept only a stable, nearby central point.
    internal static bool OpeningPointerAllowed(Rectangle client,Point before,Point after)
    {
        if(client.Width<=0 || client.Height<=0 || !client.Contains(before) || !client.Contains(after))return false;
        int centerX=client.Left+client.Width/2,centerY=client.Top+client.Height/2;
        int toleranceX=Math.Clamp(client.Width/20,2,64),toleranceY=Math.Clamp(client.Height/20,2,64);
        return Math.Abs(after.X-centerX)<=toleranceX && Math.Abs(after.Y-centerY)<=toleranceY &&
            Math.Abs(before.X-after.X)<=2 && Math.Abs(before.Y-after.Y)<=2;
    }
    internal static bool AlignOpeningPointer(Rectangle client,CancellationToken token)
    {
        Point center=new(client.Left+client.Width/2,client.Top+client.Height/2);
        if(AlignPointer(center,token))return true;
        Check(token);Point before=Cursor();
        Check(token);Point actual=Cursor();
        bool accepted=OpeningPointerAllowed(client,before,actual);
        TraceLog.Record("revival central opening alignment",new{Client=client,Expected=center,Before=before,
            Actual=actual,Accepted=accepted,MaximumTolerancePixels=64});
        return accepted;
    }
    public static Point Cursor()
    {
        if(!GetCursorPos(out var p))throw new InvalidOperationException($"Windows could not read the mouse position (error {Marshal.GetLastWin32Error()}).");
        return new Point(p.X,p.Y);
    }
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32.dll", SetLastError = true)] static extern bool PostMessage(IntPtr hwnd, uint message, nuint wparam, nint lparam);
    internal static void PostKey(IntPtr hwnd, int processId, Keys key, bool up)
    {
        GetWindowThreadProcessId(hwnd, out uint actualPid);
        if (actualPid != processId) throw new InvalidOperationException("Game window identity changed.");
        uint bits = 1u | (MapVirtualKey((uint)key, 0) << 16) | (up ? 0xc0000000u : 0u);
        if (!PostMessage(hwnd, up ? 0x101u : 0x100u, (nuint)key, (nint)unchecked((int)bits))) throw new InvalidOperationException("Windows rejected the targeted input message.");
    }
    internal static void PostMove(IntPtr hwnd, int processId, int clientX, int clientY)
    {
        GetWindowThreadProcessId(hwnd, out uint actualPid);
        if (actualPid != processId) throw new InvalidOperationException("Game window identity changed.");
        if (((clientX is < -0x8000 or > 0x7fff) || (clientY is < -0x8000 or > 0x7fff))) throw new ArgumentOutOfRangeException(nameof(clientX), "Client coordinates do not fit a mouse message.");
        nint lparam = (nint)((nuint)unchecked((short)clientX) | ((nuint)unchecked((short)clientY) << 16));
        if (!PostMessage(hwnd, 0x200u, 0, lparam)) throw new InvalidOperationException("Windows rejected the posted mouse move.");
    }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, Packet[] packets, int size);
    [DllImport("user32.dll", SetLastError = true)] static extern nint SendMessageW(IntPtr hwnd, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr window, int id);
    // The pulse worker and owner-context cleanup share one state/packet lock.
    // A release invalidates its lease before releasing keys, so a worker which
    // has not yet pressed W cannot press it after Stop/Release returned.
    static readonly object inputGate=new();
    static long releaseGeneration;
    static bool forwardPulseRunning;
    static Action<bool>? forwardPulseTransition;
    static readonly HeldInputs<Keys> keys = new((key, down) =>
    {
        Send(KeyPacket(key,!down));
        if(key==Keys.W)forwardPulseTransition?.Invoke(down);
        RecordHeldInput(down ? "key down" : "key up",new{Key=key.ToString()});
    },inputGate);
    static readonly HeldInputs<bool> buttons = new((right, down) => { Send(MousePacket(right, !down)); RecordHeldInput(down ? "mouse down" : "mouse up", new { Button = right ? "right" : "left" }); },inputGate);
    public static bool BasicAttackHeld => buttons.IsHeld(false);
    public static bool RightButtonHeld => buttons.IsHeld(true);

    internal static void HoldRangedFire(CancellationToken token) => HoldMouse(true,true,token);
    static void RecordHeldInput(string stage, object details)
    {
        // Logging after successful input must not prevent its held state from being committed.
        try { if(!PulseInputTrace.TryRecord(stage,details))TraceLog.Record(stage, details); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public static Func<bool> Allowed = () => false;
    // Called on the owning context after full preflight. Its returned callback
    // must contain only captured immutable identity and cheap Win32 checks.
    // No scene reads, WinForms controls or UI-bound delegates run on the worker.
    public static Func<Func<bool>>? CapturePulseSafety;
    public static Action? Preflight;
    public static Func<bool>? PickupHoldProvider;
    public static bool PickupHeld => keys.IsHeld(Keys.E);
    static Action<Packet>? selfTestSink;
    static Func<Keys,bool>? selfTestDown;
    public static bool Down(Keys key) => selfTestDown?.Invoke(key) ?? (selfTestSink==null && (GetAsyncKeyState((int)key) & 0x8000) != 0);
    internal static void CheckSafety(CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if(Down(Keys.F9))throw new OperationCanceledException("F9 stop key pressed.");
            if(Down(Keys.Escape))throw new OperationCanceledException("Escape stop key pressed.");
            if(Down(Keys.Enter) && !keys.IsHeld(Keys.Enter))throw new OperationCanceledException("Enter/chat key pressed.");
            if(!Allowed())throw new OperationCanceledException("Game window lost focus or is no longer available.");
        }
        catch {Release();throw;}
    }
    static void Check(CancellationToken token)
    {
        try
        {
            CheckSafety(token);
            Preflight?.Invoke();
            // The nearby-loot decision owns E while enabled. Updating raw held
            // state here avoids recursively invoking Check through Hold.
            if(PickupHoldProvider is { } pickup)keys.Set(Keys.E,pickup());
        }
        catch {Release();throw;}
    }
    internal static void CheckFaultWatchSafety()
    {
        var savedAllowed=Allowed;var savedPreflight=Preflight;var savedDown=selfTestDown;
        var savedSink=selfTestSink;var savedPickup=PickupHoldProvider;
        try
        {
            int packets=0;selfTestSink=_=>packets++;selfTestDown=_=>false;Allowed=()=>true;
            PickupHoldProvider=()=>throw new Exception("Fault watch invoked pickup");
            Preflight=()=>throw new Exception("Fault watch invoked combat preflight");
            CheckSafety(default);
            if(packets!=0)throw new Exception("Healthy fault watch emitted input.");
            foreach(Keys key in new[]{Keys.F9,Keys.Escape,Keys.Enter})
            {
                selfTestDown=k=>k==key;bool stopped=false;
                try{CheckSafety(default);}catch(OperationCanceledException){stopped=true;}
                if(!stopped)throw new Exception("Fault watch ignored a stop/chat key.");
            }
            selfTestDown=_=>false;Allowed=()=>false;bool lostFocus=false;
            try{CheckSafety(default);}catch(OperationCanceledException){lostFocus=true;}
            if(!lostFocus)throw new Exception("Fault watch ignored focus loss.");
            Allowed=()=>true;using var cancelled=new CancellationTokenSource();cancelled.Cancel();bool stoppedByToken=false;
            try{CheckSafety(cancelled.Token);}catch(OperationCanceledException){stoppedByToken=true;}
            if(!stoppedByToken)throw new Exception("Fault watch ignored run cancellation.");
        }
        finally{Release();Allowed=savedAllowed;Preflight=savedPreflight;selfTestDown=savedDown;selfTestSink=savedSink;PickupHoldProvider=savedPickup;}
    }
    static void Send(Packet p)
    {
        lock(inputGate)
        {
            bool release=p.Type==1 ? p.Value.Keyboard.Flags==10 : p.Type==0 && p.Value.Mouse.Flags is 4 or 16;
            if(forwardPulseRunning && !PulseInputTrace.IsActive && !release)
                throw new InvalidOperationException("A short forward pulse already owns input.");
            SendSerialized(p);
        }
    }
    static void SendSerialized(Packet p)
    {
        if(selfTestSink!=null) {selfTestSink(p);return;}
        int size = Marshal.SizeOf<Packet>();
        if (WindowsClientInput.TrySend(p, size, out uint nativeSent, out int nativeError))
        {
            if (nativeSent == 1) return;
            throw new InvalidOperationException($"Windows native input rejected the event (Windows error {nativeError}; accepted {nativeSent}/1).");
        }
        uint sent = SendInput(1, [p], size);
        int error = Marshal.GetLastWin32Error();
        if (sent == 1) return;
        try
        {
            TraceLog.Record("input rejected", new
            {
                Backend = "Windows SendInput", Requested = 1, Sent = sent, WindowsError = error,
                PacketType = p.Type, Flags = p.Type == 1 ? p.Value.Keyboard.Flags : p.Value.Mouse.Flags, PacketSize = size
            });
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        string detail = error == 0 ? "Windows error 0; no specific error was supplied" : $"Windows error {error}";
        throw new InvalidOperationException($"Windows SendInput rejected an input event ({detail}; accepted {sent}/1).");
    }
    static Packet KeyPacket(Keys key, bool up) => new() { Type = 1, Value = new Union { Keyboard = new Keyboard { Scan = (ushort)MapVirtualKey((uint)key, 0), Flags = 8u | (up ? 2u : 0u) } } };
    static Packet UnicodePacket(char character, bool up) => new() { Type = 1, Value = new Union { Keyboard = new Keyboard { Scan = character, Flags = 4u | (up ? 2u : 0u) } } };
    static Packet MousePacket(bool right, bool up) => new() { Value = new Union { Mouse = new Mouse { Flags = right ? (up ? 16u : 8u) : (up ? 4u : 2u) } } };
    internal static async Task CheckCombatTurnInput()
    {
        var savedAllowed=Allowed;var savedPreflight=Preflight;var savedPickup=PickupHoldProvider;
        var savedSink=selfTestSink;var savedDown=selfTestDown;var packets=new List<Packet>();
        try
        {
            selfTestSink=packets.Add;selfTestDown=_=>false;Allowed=()=>true;Preflight=null;PickupHoldProvider=null;
            Release();packets.Clear();HoldMouse(false,true,default);long clock=0;
            await CombatTurnTracking.WaitAsync(75,()=>clock,()=>{Turn(1,default);return true;},
                async (ms,ct)=>{await Delay(0,ct);clock+=ms;},default);
            if(!BasicAttackHeld||packets.Count(p=>p.Value.Mouse.Flags==2)!=1||packets.Any(p=>p.Value.Mouse.Flags==4)||
                packets.Count(p=>p.Value.Mouse.Flags==1)!=5)
                throw new Exception("Combat turn tracking interrupted held basic attack or skipped corrections.");
            Release();
            foreach(string guard in new[]{"focus","F9","Escape","Enter","health"})
            {
                Allowed=()=>true;selfTestDown=_=>false;Preflight=null;HoldMouse(false,true,default);packets.Clear();clock=0;
                if(guard=="focus")Allowed=()=>false;
                else if(guard=="health")Preflight=()=>throw new OperationCanceledException("health changed");
                else selfTestDown=key=>key.ToString()==guard;
                bool stopped=false;
                try{await CombatTurnTracking.WaitAsync(75,()=>clock,()=>{Turn(1,default);return true;},
                    async(ms,ct)=>{await Delay(0,ct);clock+=ms;},default);}
                catch(OperationCanceledException){stopped=true;}
                if(!stopped||BasicAttackHeld||packets.Any(p=>p.Value.Mouse.Flags==1))
                    throw new Exception("Combat turn tracking bypassed "+guard+" or retained attack after interruption.");
            }
        }
        finally
        {
            Preflight=null;PickupHoldProvider=null;Release();Allowed=savedAllowed;Preflight=savedPreflight;
            PickupHoldProvider=savedPickup;selfTestSink=savedSink;selfTestDown=savedDown;
        }
    }
    public static async Task Delay(int ms, CancellationToken token)
    {
        long deadline = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < deadline) { Check(token); int step = (int)Math.Min(20, deadline - Environment.TickCount64); if (step > 0) await Task.Delay(step, token); }
        Check(token);
    }
    internal static async Task<long> PulseForward(int milliseconds,CancellationToken token)
    {
        var trace=new PulseInputTrace();Func<bool>? windowAllowed=null;long generation=0;
        long origin=System.Diagnostics.Stopwatch.GetTimestamp(),pressedAt=0,releasedAt=0;
        DateTime? downUtc=null,upUtc=null;bool reserved=false,leaseCaptured=false,completed=false;
        void CheapSafety(bool requireHeld)
        {
            token.ThrowIfCancellationRequested();
            if(Down(Keys.F9))throw new OperationCanceledException("F9 stop key pressed.");
            if(Down(Keys.Escape))throw new OperationCanceledException("Escape stop key pressed.");
            if(Down(Keys.Enter) && !keys.IsHeld(Keys.Enter))throw new OperationCanceledException("Enter/chat key pressed.");
            if(windowAllowed?.Invoke()!=true)throw new OperationCanceledException("Game window lost focus or identity during a short forward pulse.");
            lock(inputGate)
            {
                if(generation!=releaseGeneration || requireHeld && !keys.IsHeld(Keys.W))
                    throw new OperationCanceledException("Short forward pulse stopped by input release.");
            }
        }
        try
        {
            await MovementPulseTiming.RunOwnedAsync(milliseconds,()=>
            {
                // All UI/scene work and native-export initialization precede W.
                Check(token);
                if(selfTestSink==null)WindowsClientInput.ValidateReady();
                windowAllowed=CapturePulseSafety?.Invoke() ?? throw new InvalidOperationException("Short forward pulse window safety is unavailable.");
                lock(inputGate)
                {
                    if(forwardPulseRunning || keys.IsHeld(Keys.W))throw new InvalidOperationException("Forward input is already held by another owner.");
                    generation=releaseGeneration;forwardPulseRunning=reserved=leaseCaptured=true;
                    forwardPulseTransition=down=>
                    {
                        if(down){pressedAt=System.Diagnostics.Stopwatch.GetTimestamp();downUtc=DateTime.UtcNow;}
                        else {releasedAt=System.Diagnostics.Stopwatch.GetTimestamp();upUtc=DateTime.UtcNow;}
                    };
                }
            },()=>
            {
                lock(inputGate)
                {
                    CheapSafety(false);keys.Set(Keys.W,true);
                }
            },()=>
            {
                lock(inputGate)
                {
                    try {keys.Set(Keys.W,false);}
                    finally
                    {
                        forwardPulseRunning=false;reserved=false;
                        if(!keys.IsHeld(Keys.W))forwardPulseTransition=null;
                    }
                }
            },()=>CheapSafety(true),()=>Check(token),Task.Delay,
                ()=> (long)System.Diagnostics.Stopwatch.GetElapsedTime(origin).TotalMilliseconds,token,
                work=>Task.Run(async()=>
                {
                    using var scope=trace.Enter();
                    return await work().ConfigureAwait(false);
                }));
            completed=true;
            return (long)Math.Ceiling(System.Diagnostics.Stopwatch.GetElapsedTime(pressedAt,releasedAt).TotalMilliseconds);
        }
        catch
        {
            // The worker has joined before owner cleanup releases attack/E or
            // any other held input. The worker itself only releases its W.
            if(leaseCaptured)Release();throw;
        }
        finally
        {
            lock(inputGate)
            {
                if(reserved)
                {
                    try {keys.Set(Keys.W,false);}
                    finally {forwardPulseRunning=false;}
                }
                if(leaseCaptured)forwardPulseTransition=null;
            }
            try
            {
                trace.Flush();
                if(pressedAt!=0 && releasedAt!=0)
                {
                    double held=System.Diagnostics.Stopwatch.GetElapsedTime(pressedAt,releasedAt).TotalMilliseconds;
                    TraceLog.Record("forward pulse timing",new{RequestedMilliseconds=milliseconds,HeldMilliseconds=held,
                        ReleaseLatenessMilliseconds=Math.Max(0,held-milliseconds),KeyDownUtc=downUtc,KeyUpUtc=upUtc,
                        Completed=completed,Clock="Stopwatch",Owner="serialized pulse worker"});
                }
            }
            catch(IOException) { }
            catch(UnauthorizedAccessException) { }
        }
    }
    internal static async Task CheckForwardPulseInput()
    {
        var savedAllowed=Allowed;var savedPreflight=Preflight;var savedPickup=PickupHoldProvider;
        var savedCapture=CapturePulseSafety;var savedSink=selfTestSink;var savedDown=selfTestDown;
        var packets=new System.Collections.Concurrent.ConcurrentQueue<Packet>();
        void Require(bool passed,string message)
        {if(!passed)throw new InvalidOperationException("Forward pulse input: "+message);}
        int DownPackets()=>packets.Count(p=>p.Type==1 && p.Value.Keyboard.Flags==8);
        int UpPackets()=>packets.Count(p=>p.Type==1 && p.Value.Keyboard.Flags==10);
        void Arm()
        {
            Preflight=null;PickupHoldProvider=null;selfTestSink=packets.Enqueue;selfTestDown=_=>false;
            Allowed=()=>true;CapturePulseSafety=()=>()=>true;Release();packets.Clear();
        }
        try
        {
            Arm();int fullChecks=0;
            Preflight=()=>{Require(!keys.IsHeld(Keys.W),"full scene check ran while W was held");fullChecks++;};
            long held=await PulseForward(16,default);
            Require(held>=16 && fullChecks==2 && DownPackets()==1 && UpPackets()==1 && !keys.IsHeld(Keys.W),
                "healthy pulse did not preflight/postflight around one paired input");

            foreach(string scenario in new[]{"focus","F9","Escape","Enter","token","guard throw"})
            {
                Arm();int interruptedState=0;fullChecks=0;
                using var cancellation=new CancellationTokenSource();
                Preflight=()=>fullChecks++;
                selfTestSink=packet=>
                {
                    packets.Enqueue(packet);
                    if(packet.Type==1 && packet.Value.Keyboard.Flags==8)
                    {Volatile.Write(ref interruptedState,1);if(scenario=="token")cancellation.Cancel();}
                };
                selfTestDown=key=>Volatile.Read(ref interruptedState)!=0 && key.ToString()==scenario;
                CapturePulseSafety=()=>()=>
                {
                    if(Volatile.Read(ref interruptedState)!=0 && scenario=="guard throw")throw new InvalidOperationException("Synthetic window read failed.");
                    return scenario!="focus" || Volatile.Read(ref interruptedState)==0;
                };
                bool interrupted=false;
                try {await PulseForward(60,cancellation.Token);}
                catch(Exception e) when(e is OperationCanceledException or InvalidOperationException){interrupted=true;}
                Require(interrupted && fullChecks==1 && DownPackets()==1 && UpPackets()==1 && !keys.IsHeld(Keys.W),
                    scenario+" bypassed a guard, ran postflight or retained input");
            }

            Arm();CapturePulseSafety=null;bool missingGuard=false;
            try {await PulseForward(16,default);}catch(InvalidOperationException){missingGuard=true;}
            Require(missingGuard && packets.IsEmpty,"missing captured window safety admitted input");

            Arm();bool nested=false;
            Preflight=()=>
            {
                try {PulseForward(16,default).GetAwaiter().GetResult();}
                catch(InvalidOperationException){nested=true;}
            };
            await PulseForward(16,default);
            Require(nested && DownPackets()==1 && UpPackets()==1,"nested pulse disturbed the owning input");

            Arm();using var safetyReached=new ManualResetEventSlim();using var continueSafety=new ManualResetEventSlim();
            int safetyPaused=0;
            CapturePulseSafety=()=>()=>
            {
                if(keys.IsHeld(Keys.W) && Interlocked.Exchange(ref safetyPaused,1)==0)
                {safetyReached.Set();Require(continueSafety.Wait(TimeSpan.FromSeconds(2)),"release test did not unblock its worker");}
                return true;
            };
            var stoppedPulse=PulseForward(60,default);
            Require(safetyReached.Wait(TimeSpan.FromSeconds(2)),"pulse worker did not start its safety checks");
            bool overlap=false;
            try {await PulseForward(16,default);}catch(InvalidOperationException){overlap=true;}
            Require(overlap && keys.IsHeld(Keys.W) && DownPackets()==1 && UpPackets()==0,
                "overlap rejection released the first pulse");
            Release();continueSafety.Set();bool stopped=false;
            try {await stoppedPulse;}catch(OperationCanceledException){stopped=true;}
            Require(stopped && DownPackets()==1 && UpPackets()==1 && !keys.IsHeld(Keys.W),
                "Release raced startup/cleanup or duplicated the key-up");

            Arm();int rejectedUps=0;
            selfTestSink=packet=>
            {
                if(packet.Type==1 && packet.Value.Keyboard.Flags==10 && Interlocked.Increment(ref rejectedUps)==1)
                    throw new InvalidOperationException("Synthetic rejected key-up");
                packets.Enqueue(packet);
            };
            bool upFailed=false;try {await PulseForward(16,default);}catch(InvalidOperationException){upFailed=true;}
            Require(upFailed && DownPackets()==1 && UpPackets()==1 && rejectedUps==2 && !keys.IsHeld(Keys.W),
                "failed key-up lost held state or was not retried safely by owner cleanup");
        }
        finally
        {
            Preflight=null;PickupHoldProvider=null;selfTestSink=packets.Enqueue;selfTestDown=_=>false;Release();
            Allowed=savedAllowed;Preflight=savedPreflight;PickupHoldProvider=savedPickup;
            CapturePulseSafety=savedCapture;selfTestSink=savedSink;selfTestDown=savedDown;
        }
    }
    public static void Chat(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !Allowed()) throw new InvalidOperationException("Chat input requires the game in the foreground.");
        Send(KeyPacket(Keys.Enter, false)); Send(KeyPacket(Keys.Enter, true));
        foreach (char character in text)
        {
            if (character is '\r' or '\n') continue;
            Send(UnicodePacket(character, false)); Send(UnicodePacket(character, true));
        }
        Send(KeyPacket(Keys.Enter, false)); Send(KeyPacket(Keys.Enter, true));
    }
    public static async Task Key(Keys key, int ms, CancellationToken token)
    {
        try { Hold(key, true, token); await Delay(ms, token); }
        finally { Hold(key, false, token); }
    }
    internal static async Task CheckReviveInput()
    {
        var originalAllowed=Allowed;var originalPreflight=Preflight;var originalPickup=PickupHoldProvider;
        var packets=new List<Packet>();selfTestSink=packets.Add;selfTestDown=key=>keys.IsHeld(key);
        Allowed=()=>true;Preflight=null;PickupHoldProvider=null;
        try
        {
            Release();packets.Clear();
            await Key(Keys.Enter,25,default);
            if(packets.Count!=2 || packets[0].Value.Keyboard.Flags!=8 || packets[1].Value.Keyboard.Flags!=10 || keys.IsHeld(Keys.Enter))
                throw new Exception("Revive confirmation did not produce one complete Enter pulse.");
            selfTestDown=key=>key==Keys.Enter;
            bool stopped=false;try{await Delay(0,default);}catch(OperationCanceledException){stopped=true;}
            if(!stopped)throw new Exception("Manual Enter no longer stops chat input.");
            selfTestDown=key=>keys.IsHeld(key);
            Preflight=()=>Allowed=()=>false;
            stopped=false;try{await Key(Keys.Enter,25,default);}catch(OperationCanceledException){stopped=true;}
            if(!stopped || keys.IsHeld(Keys.Enter))throw new Exception("Revive Enter did not release on focus loss.");
        }
        finally
        {
            Preflight=null;PickupHoldProvider=null;Release();selfTestSink=null;selfTestDown=null;
            Allowed=originalAllowed;Preflight=originalPreflight;PickupHoldProvider=originalPickup;
        }
    }
    public static void Hold(Keys key, bool down, CancellationToken token,Func<bool>? admitDown=null)
    {
        if (down) Check(token);
        if(down && admitDown?.Invoke()==false)return;
        // Existing pickup pulses cannot release a nearby-loot hold or extend
        // one after the provider has observed that the radius is empty.
        if(key==Keys.E && PickupHoldProvider!=null)return;
        lock(inputGate)
        {
            if(!down && key==Keys.W && forwardPulseRunning)releaseGeneration++;
            keys.Set(key, down);
        }
    }
    public static async Task Click(bool right, CancellationToken token)
    {
        try { HoldMouse(right, true, token); await Delay(70, token); }
        finally { HoldMouse(right, false, token); }
    }
    public static void MovePointer(Point screen,CancellationToken token)
    {
        Check(token);
        var desktop=SystemInformation.VirtualScreen;
        if(!desktop.Contains(screen) || desktop.Width<2 || desktop.Height<2)
            throw new InvalidOperationException("Repair pointer destination is outside the desktop.");
        Send(new Packet{Value=new Union{Mouse=new Mouse{
            X=(int)Math.Round((screen.X-desktop.Left)*65535d/(desktop.Width-1)),
            Y=(int)Math.Round((screen.Y-desktop.Top)*65535d/(desktop.Height-1)),Flags=0xc001}}});
    }
    internal static void CheckRepairPointer()
    {
        Point desired=new(1720,720),actual=new(1720,670);int corrections=0,checks=0;
        if(!AlignPointer(desired,()=>actual,point=>{corrections++;actual=point;return true;},()=>checks++) || corrections!=1 || checks!=3)
            throw new Exception("Displaced revival pointer was not corrected and revalidated.");
        if(!AlignPointer(desired,()=>desired,_=>throw new Exception("An aligned pointer was moved again."),()=>{}))
            throw new Exception("Aligned revival pointer was refused.");
        if(AlignPointer(desired,()=>new(1720,670),_=>true,()=>{}) || AlignPointer(desired,()=>new(1720,670),_=>false,()=>{}))
            throw new Exception("Clipped or rejected pointer allowed a misplaced revival click.");
        int validations=0;bool cancelled=false;
        try{AlignPointer(desired,()=>new(1720,670),_=>true,()=>{if(++validations==3)throw new OperationCanceledException();});}
        catch(OperationCanceledException){cancelled=true;}
        if(!cancelled)throw new Exception("Pointer correction ignored focus/cancellation revalidation.");
        var gameClient=new Rectangle(0,0,3440,1440);
        if(!OpeningPointerAllowed(gameClient,new(1720,670),new(1720,670)) ||
            !OpeningPointerAllowed(gameClient,desired,desired) ||
            OpeningPointerAllowed(gameClient,new(1720,600),new(1720,600)) ||
            OpeningPointerAllowed(gameClient,new(1720,670),new(1720,675)) ||
            OpeningPointerAllowed(new(1600,700,240,40),new(1720,670),new(1720,670)) ||
            OpeningPointerAllowed(Rectangle.Empty,desired,desired))
            throw new Exception("Central opening policy mishandled the live 50-pixel offset, drift, or client bounds.");
        var shiftedClient=new Rectangle(-3440,120,3440,1440);
        if(!OpeningPointerAllowed(shiftedClient,new(-1720,790),new(-1720,790)) ||
            OpeningPointerAllowed(new(0,0,400,300),new(200,100),new(200,100)))
            throw new Exception("Opening tolerance ignored desktop origins or small-window scaling.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"revival-center-pointer-checks.json"),
            System.Text.Json.JsonSerializer.Serialize(new{Passed=true,ObservedOffsetPixels=50,MaximumTolerancePixels=64,
                StableCentralOpeningAccepted=true,FarAndDriftingPointersRejected=true,OutsideClientRejected=true,
                ButtonAlignmentRemainsPrecise=true}));
        var savedAllowed=Allowed;var savedPreflight=Preflight;var savedPickup=PickupHoldProvider;
        var packets=new List<Packet>();selfTestSink=packets.Add;Allowed=()=>true;Preflight=null;PickupHoldProvider=null;
        try
        {
            Release();packets.Clear();
            var desktop=SystemInformation.VirtualScreen;
            MovePointer(desktop.Location,default);
            MovePointer(new(desktop.Right-1,desktop.Bottom-1),default);
            if(packets.Count!=2 || packets.Any(p=>p.Type!=0 || p.Value.Mouse.Flags!=0xc001) ||
                packets[0].Value.Mouse.X!=0 || packets[0].Value.Mouse.Y!=0 ||
                packets[1].Value.Mouse.X!=65535 || packets[1].Value.Mouse.Y!=65535)
                throw new Exception("Repair pointer coordinates did not span the virtual desktop correctly.");
            void Refused(Point point,CancellationToken token)
            {
                bool failed=false;try{MovePointer(point,token);}catch(InvalidOperationException){failed=true;}catch(OperationCanceledException){failed=true;}
                if(!failed || packets.Count!=2)throw new Exception("Invalid, cancelled, or unfocused repair moved the pointer.");
            }
            Refused(new(desktop.Right,desktop.Bottom),default);
            Refused(desktop.Location,new CancellationToken(true));
            Allowed=()=>false;Refused(desktop.Location,default);
            Allowed=()=>true;Preflight=()=>throw new InvalidOperationException("Character changed");Refused(desktop.Location,default);
        }
        finally {Preflight=null;PickupHoldProvider=null;Release();selfTestSink=null;Allowed=savedAllowed;Preflight=savedPreflight;PickupHoldProvider=savedPickup;}
    }
    public static async Task CastSkill(SkillUseKind use,int chargeMilliseconds,CancellationToken token,Action? onInputStarted=null)
    {
        // Older clients do not expose a use-kind for ordinary active skills.
        // Treat that missing metadata as the short instance activation rather
        // than throwing after the basic attack has already been interrupted.
        SkillUseKind activation=use==SkillUseKind.Unknown ? SkillUseKind.Instance : use;
        int duration=activation switch
        {
            SkillUseKind.Instance or SkillUseKind.Chant=>70,
            SkillUseKind.Cast when chargeMilliseconds is >=50 and <=10000=>chargeMilliseconds,
            SkillUseKind.Cast=>throw new ArgumentOutOfRangeException(nameof(chargeMilliseconds)),
            _=>throw new InvalidOperationException("Unsupported skill activation type; no input sent.")
        };
        try
        {
            HoldMouse(true,true,token);
            onInputStarted?.Invoke();
            await Delay(duration,token);
        }
        finally {HoldMouse(true,false,token);}
    }
    internal static async Task CheckSkillKinds()
    {
        var savedAllowed=Allowed;var savedPreflight=Preflight;var packets=new List<Packet>();
        selfTestSink=packets.Add;Allowed=()=>true;Preflight=null;
        try
        {
            await CastSkill(SkillUseKind.Instance,9000,default);
            await CastSkill(SkillUseKind.Chant,9000,default);
            await CastSkill(SkillUseKind.Cast,120,default);
            if(packets.Count!=6 || packets.Any(p=>p.Type!=0) || packets.Where((p,i)=>p.Value.Mouse.Flags!=(i%2==0?8u:16u)).Any())throw new Exception("Skill activation emitted extra input or wrong mouse sequence.");
            int started=0;
            await CastSkill(SkillUseKind.Instance,1000,default,()=>
            {
                if(packets.Count!=7 || packets[^1].Value.Mouse.Flags!=8)throw new Exception("Tag recorded before its attack input.");
                started++;
            });
            if(started!=1 || packets.Count!=8 || packets[^1].Value.Mouse.Flags!=16)throw new Exception("Tag activation or mouse release failed.");
            selfTestSink=_=>throw new InvalidOperationException("Rejected tag input");
            bool rejectedTag=false;
            try {await CastSkill(SkillUseKind.Instance,1000,default,()=>started++);} catch(InvalidOperationException){rejectedTag=true;}
            if(!rejectedTag || started!=1)throw new Exception("Rejected ranged input counted a tag.");
            selfTestSink=packets.Add;
            int unknownBefore=packets.Count;
            await CastSkill(SkillUseKind.Unknown,1000,default);
            if(packets.Count!=unknownBefore+2 || packets[^2].Value.Mouse.Flags!=8 || packets[^1].Value.Mouse.Flags!=16)
                throw new Exception("Unknown skill type did not use the safe short activation.");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"skill-type-input-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{Passed=true,HardwareInputEmitted=false,Checks=new[]{"instance uses short click despite charge setting","chant uses short click","cast uses charge","one mouse down/up per activation","no party F-key inputs","unknown skill metadata uses safe short activation"}}));
        }
        finally{Preflight=null;Release();selfTestSink=null;Allowed=savedAllowed;Preflight=savedPreflight;}
    }
    public static void HoldMouse(bool right, bool down, CancellationToken token)
    {
        if (down) Check(token);
        buttons.Set(right, down);
    }
    public static void Aim(int deltaX, int deltaY, CancellationToken token)
    {
        Check(token);
        Send(new Packet { Value = new Union { Mouse = new Mouse { X = deltaX, Y = deltaY, Flags = 1 } } });
    }
    public static void Turn(int pixels, CancellationToken token) => Aim(pixels, 0, token);
    public static void Release(bool preserveNearbyPickup = false,bool preserveBasicAttack = false)
    {
        // Activity transitions may retain an already-held E, but never create
        // new input or consult a provider during cleanup.
        lock(inputGate)
        {
            releaseGeneration++;
            // Release pulse-owned W before any ordinary release logging, which
            // may write to disk for other keys held by the owning context.
            if(forwardPulseRunning)try {keys.Set(Keys.W,false);}catch { }
            keys.ReleaseAll(preserveNearbyPickup && PickupHoldProvider!=null ? key=>key==Keys.E : null);
            buttons.ReleaseAll(preserveBasicAttack ? right=>!right : null);
        }
    }

    internal static async Task CheckHeldRangedFire()
    {
        var savedAllowed=Allowed;var savedPreflight=Preflight;var savedPickup=PickupHoldProvider;
        var packets=new List<Packet>();selfTestSink=packets.Add;Allowed=()=>true;Preflight=null;PickupHoldProvider=null;
        try
        {
            Release();packets.Clear();
            HoldRangedFire(default);
            for(int target=0;target<3;target++)
            {
                Hold(Keys.W,true,default);Aim(12,-4,default);await Delay(5,default);
                HoldRangedFire(default);Hold(Keys.W,false,default);
            }
            if(!RightButtonHeld || packets.Count(p=>p.Type==0 && p.Value.Mouse.Flags==8)!=1 ||
                packets.Count(p=>p.Type==0 && p.Value.Mouse.Flags==1 && p.Value.Mouse.X==12 && p.Value.Mouse.Y==-4)!=3 ||
                packets.Any(p=>p.Type==0 && p.Value.Mouse.Flags==16))
                throw new Exception("Ranged sweep emitted incorrect XY aim or released/re-clicked right fire during movement/target changes.");
            Release();
            if(RightButtonHeld || packets.Count(p=>p.Type==0 && p.Value.Mouse.Flags==16)!=1)
                throw new Exception("Gather transition failed to release held right fire.");
            HoldRangedFire(default);Allowed=()=>false;
            bool stopped=false;try{await Delay(1,default);}catch(OperationCanceledException){stopped=true;}
            if(!stopped || RightButtonHeld)throw new Exception("Lost focus did not release ranged fire.");
            Allowed=()=>true;HoldRangedFire(default);Preflight=()=>throw new TargetProtectionException("Protected target");
            bool protectedStop=false;try{Turn(1,default);}catch(TargetProtectionException){protectedStop=true;}
            if(!protectedStop || RightButtonHeld)throw new Exception("Target protection did not release ranged fire.");
            bool travelAllowed=true;packets.Clear();
            Preflight=()=>travelAllowed=false;
            Hold(Keys.W,true,default,()=>travelAllowed);
            if(packets.Any(p=>p.Type==1 && p.Value.Keyboard.Flags==8))
                throw new Exception("A movement key was re-pressed after preflight invalidated travel.");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"held-ranged-input-checks.json"),System.Text.Json.JsonSerializer.Serialize(new
            {
                Passed=true,HardwareInputEmitted=false,Checks=new[]{"one right down across three target changes","movement and aiming while firing","release on gathering","release on focus loss","release on target protection"}
            }));
        }
        finally{Preflight=null;Release();selfTestSink=null;Allowed=savedAllowed;Preflight=savedPreflight;PickupHoldProvider=savedPickup;}
    }
    internal static async Task CheckNearbyLootInput()
    {
        var originalAllowed=Allowed;var originalPreflight=Preflight;var originalPickup=PickupHoldProvider;
        var packets=new List<Packet>();selfTestSink=packets.Add;Allowed=()=>true;Preflight=null;PickupHoldProvider=null;
        ushort eScan=(ushort)MapVirtualKey((uint)Keys.E,0);
        int ECount(bool down)=>packets.Count(p=>p.Type==1 && p.Value.Keyboard.Scan==eScan && ((p.Value.Keyboard.Flags&2)==0)==down);
        void AssertReleased(string reason)
        {
            var heldKeys=new HashSet<ushort>();var heldButtons=new HashSet<bool>();
            foreach(var p in packets)
            {
                if(p.Type==1){if((p.Value.Keyboard.Flags&2)==0)heldKeys.Add(p.Value.Keyboard.Scan);else heldKeys.Remove(p.Value.Keyboard.Scan);}
                else{uint f=p.Value.Mouse.Flags;if((f&2)!=0)heldButtons.Add(false);if((f&4)!=0)heldButtons.Remove(false);if((f&8)!=0)heldButtons.Add(true);if((f&16)!=0)heldButtons.Remove(true);}
            }
            if(PickupHeld || BasicAttackHeld || heldKeys.Count!=0 || heldButtons.Count!=0)throw new Exception(reason);
        }
        bool nearby=false;
        void Arm()
        {
            Allowed=()=>true;Preflight=null;nearby=true;PickupHoldProvider=()=>nearby;
            Hold(Keys.W,true,default);HoldMouse(false,true,default);
        }
        try
        {
            Release();packets.Clear();PickupHoldProvider=()=>nearby;
            await Key(Keys.E,5,default);
            if(packets.Count!=0 || PickupHeld)throw new Exception("Empty nearby-loot provider allowed a legacy E pulse");
            nearby=true;
            Hold(Keys.W,true,default);HoldMouse(false,true,default);
            Hold(Keys.E,true,default);Hold(Keys.E,false,default);
            await Key(Keys.D1,5,default);
            await Key(Keys.E,5,default);
            if(!PickupHeld || !BasicAttackHeld || ECount(true)!=1 || ECount(false)!=0)throw new Exception("Nearby pickup was tapped or released during movement, combo, skill, or legacy pickup input");
            Release(preserveNearbyPickup:true);
            if(!PickupHeld || BasicAttackHeld || ECount(false)!=0)throw new Exception("Activity transition did not preserve only nearby pickup");

            int checks=0;
            Preflight=()=>{if(++checks>=2)nearby=false;};
            await Key(Keys.E,30,default);
            Preflight=null;
            if(PickupHeld || ECount(true)!=1 || ECount(false)!=1)throw new Exception("Nearby pickup failed to release when loot disappeared during a legacy E hold");
            int afterEmpty=packets.Count;
            Release(preserveNearbyPickup:true);Hold(Keys.E,false,default);
            if(packets.Count!=afterEmpty)throw new Exception("Cleanup re-pressed E after the nearby radius emptied");
            AssertReleased("Nearby pickup empty-radius cleanup left input held");

            Arm();PickupHoldProvider=()=>throw new InvalidOperationException("Fixture pickup scan failed");
            bool failed=false;try{await Delay(0,default);}catch(InvalidOperationException){failed=true;}
            if(!failed)throw new Exception("Pickup provider failure was swallowed");
            AssertReleased("Pickup provider failure left input held");

            Arm();Allowed=()=>false;
            bool focusStopped=false;try{await Delay(0,default);}catch(OperationCanceledException){focusStopped=true;}
            if(!focusStopped)throw new Exception("Nearby pickup ignored focus loss");
            AssertReleased("Focus loss left nearby pickup or combat input held");

            Arm();bool cancelStopped=false;
            try{await Delay(0,new CancellationToken(true));}catch(OperationCanceledException){cancelStopped=true;}
            if(!cancelStopped)throw new Exception("Nearby pickup ignored cancellation");
            AssertReleased("Cancellation left nearby pickup or combat input held");

            Arm();Preflight=()=>throw new InvalidOperationException("Fixture preflight failure");
            bool preflightStopped=false;try{await Delay(0,default);}catch(InvalidOperationException){preflightStopped=true;}
            if(!preflightStopped)throw new Exception("Nearby pickup ignored preflight failure");
            AssertReleased("Preflight failure left nearby pickup or combat input held");

            Arm();Release();AssertReleased("Forced release preserved nearby pickup");
            int afterForce=packets.Count;
            Release(preserveNearbyPickup:true);Hold(Keys.E,false,default);
            if(packets.Count!=afterForce)throw new Exception("Cleanup re-pressed E after a forced release");
            Preflight=null;PickupHoldProvider=null;Hold(Keys.E,true,default);Release(preserveNearbyPickup:true);
            AssertReleased("Preserve-nearby release preserved an ordinary E hold without a provider");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"nearby-loot-input-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{
                Passed=true,HardwareInputEmitted=false,Checks=new[]{"empty provider suppresses legacy pickup","one E down through movement, combo, and skill input","legacy E up preserves nearby hold","activity cleanup preserves existing E only","empty scan releases during legacy E pulse","cleanup never re-presses E","provider failure releases all input","focus loss releases all input","cancellation releases all input","preflight failure releases all input","forced release clears nearby hold","ordinary E release unchanged without provider"}
            },new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        }
        finally{Preflight=null;PickupHoldProvider=null;selfTestSink=packets.Add;Release();selfTestSink=null;Allowed=originalAllowed;Preflight=originalPreflight;PickupHoldProvider=originalPickup;}
    }
    internal static async Task CheckCombatPickup()
    {
        var originalAllowed=Allowed;var originalPreflight=Preflight;
        var packets=new List<Packet>();selfTestSink=packets.Add;Allowed=()=>true;Preflight=null;
        try
        {
            Release();packets.Clear();
            if(BasicAttackHeld)throw new Exception("Basic attack remained held after release");
            selfTestSink=_=>throw new InvalidOperationException("Simulated rejected attack input");
            bool rejected=false;
            try{HoldMouse(false,true,default);}catch(InvalidOperationException){rejected=true;}
            if(!rejected || BasicAttackHeld)throw new Exception("Rejected basic attack input was recorded as held");
            selfTestSink=packets.Add;
            HoldMouse(false,true,default);
            if(!BasicAttackHeld)throw new Exception("Successful basic attack input was not recorded as held");
            for(int i=0;i<4;i++) {Hold(Keys.E,true,default);HoldMouse(false,true,default);await Delay(5,default);}
            ushort eScan=(ushort)MapVirtualKey((uint)Keys.E,0);
            if(packets.Count(p=>p.Type==1 && p.Value.Keyboard.Scan==eScan && (p.Value.Keyboard.Flags&2)==0)!=1 ||
                packets.Count(p=>p.Type==0 && (p.Value.Mouse.Flags&2)!=0)!=1 ||
                packets.Any(p=>p.Type==0 && (p.Value.Mouse.Flags&4)!=0))
                throw new Exception("Pickup failed to hold E and left mouse together without tapping");
            Hold(Keys.E,false,default);await Key(Keys.D1,5,default);
            if(!BasicAttackHeld || packets.Any(p=>p.Type==0 && (p.Value.Mouse.Flags&4)!=0))throw new Exception("Releasing E interrupted basic combo");
            Hold(Keys.W,true,default);HoldMouse(true,true,default);
            int transitionPackets=packets.Count;
            Release(preserveBasicAttack:true);
            if(!BasicAttackHeld || RightButtonHeld || keys.IsHeld(Keys.W) ||
                packets.Skip(transitionPackets).Any(p=>p.Type==0 && (p.Value.Mouse.Flags&6)!=0))
                throw new Exception("Engaged target transition interrupted left attack or retained movement/skill input.");
            Release();transitionPackets=packets.Count;
            Release(preserveBasicAttack:true);
            if(BasicAttackHeld || packets.Count!=transitionPackets)throw new Exception("Target cleanup created a new attack hold.");
            HoldMouse(false,true,default);
            Hold(Keys.E,true,default);int checks=0;bool preempted=false;
            var keeper=new Entity(2,0x80001752,"Gamekeeper",new(12,0),0,Model:"MON_SnowGun2.GCMDS");
            Preflight=()=> {if(++checks>=2)throw new PriorityTargetException(keeper);};
            try {await Click(true,default);}catch(PriorityTargetException){preempted=true;Release();}
            if(!preempted)throw new Exception("Priority could not interrupt a skill click");
            var heldKeys=new HashSet<ushort>();var heldButtons=new HashSet<bool>();
            foreach(var p in packets)
            {
                if(p.Type==1) {if((p.Value.Keyboard.Flags&2)==0)heldKeys.Add(p.Value.Keyboard.Scan);else heldKeys.Remove(p.Value.Keyboard.Scan);}
                else {uint f=p.Value.Mouse.Flags;if((f&2)!=0)heldButtons.Add(false);if((f&4)!=0)heldButtons.Remove(false);if((f&8)!=0)heldButtons.Add(true);if((f&16)!=0)heldButtons.Remove(true);}
            }
            if(BasicAttackHeld || heldKeys.Count!=0 || heldButtons.Count!=0)throw new Exception("Priority switch left combat inputs held");
            Preflight=null;
            HoldMouse(false,true,default);
            selfTestSink=_=>throw new InvalidOperationException("Simulated rejected attack release");
            Release();
            if(!BasicAttackHeld)throw new Exception("Failed basic attack release discarded the held state before retry");
            selfTestSink=packets.Add;
            Release();
            if(BasicAttackHeld)throw new Exception("Successful basic attack release retry retained the held state");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"combat-pickup-checks.json"),System.Text.Json.JsonSerializer.Serialize(new {
                Passed=true,HardwareInputEmitted=false,Checks=new[]{"rejected attack input is not held","successful attack input is held","left mouse and E held together","no repeated down events","E release preserves combo","priority interrupts skill input","all inputs released on switch","failed release retains held state until retry","cooldown eligibility","nearby player and old-drop guards"}},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        }
        finally {Preflight=null;selfTestSink=packets.Add;Release();selfTestSink=null;Allowed=originalAllowed;Preflight=originalPreflight;}
    }
    internal static async Task CheckRetreatInterruptions()
    {
        var originalAllowed=Allowed;var originalPreflight=Preflight;
        var packets=new List<Packet>();
        selfTestSink=packets.Add;Allowed=()=>true;
        try
        {
            foreach(string scenario in new[]{"idle","combo","skill","right click","loot hold","healing","rest"})
            {
                Preflight=null;packets.Clear();
                if(scenario!="idle") {Hold(Keys.W,true,default);HoldMouse(false,true,default);}
                int checks=0;
                Preflight=()=> {if(++checks>=2) throw new RetreatRequiredException("Fixture threat entered buffer");};
                bool caught=false;
                try
                {
                    switch(scenario)
                    {
                        case "skill":await Key(Keys.D1,70,default);break;
                        case "right click":await Click(true,default);break;
                        case "loot hold":await Key(Keys.E,800,default);break;
                        case "healing":await Key(Keys.D9,70,default);break;
                        case "rest":await Key(Keys.C,70,default);break;
                        default:await Delay(100,default);break;
                    }
                }
                catch(RetreatRequiredException) {caught=true;Release();}
                if(!caught)throw new Exception("Retreat did not interrupt "+scenario);
                var downKeys=new HashSet<ushort>();var downButtons=new HashSet<bool>();
                foreach(var packet in packets)
                {
                    if(packet.Type==1)
                    {
                        if((packet.Value.Keyboard.Flags&2)==0) downKeys.Add(packet.Value.Keyboard.Scan);else downKeys.Remove(packet.Value.Keyboard.Scan);
                    }
                    else
                    {
                        uint flags=packet.Value.Mouse.Flags;
                        if((flags&2)!=0)downButtons.Add(false);if((flags&4)!=0)downButtons.Remove(false);
                        if((flags&8)!=0)downButtons.Add(true);if((flags&16)!=0)downButtons.Remove(true);
                    }
                }
                if(downKeys.Count!=0 || downButtons.Count!=0)throw new Exception("Retreat left input held during "+scenario);
            }
            Preflight=null;Allowed=()=>false;
            bool focusStopped=false;try{await Key(Keys.W,70,default);}catch(OperationCanceledException){focusStopped=true;}
            if(!focusStopped)throw new Exception("Input ignored focus loss");
            Allowed=()=>true;
            bool cancelStopped=false;try{await Delay(50,new CancellationToken(true));}catch(OperationCanceledException){cancelStopped=true;}
            if(!cancelStopped)throw new Exception("Input ignored cancellation");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"retreat-input-checks.json"),System.Text.Json.JsonSerializer.Serialize(new {Passed=true,HardwareInputEmitted=false,Checks=new[]{"idle interruption","held W and combo release","skill key interruption","right-click interruption","held E interruption","healing key interruption","C rest key interruption","focus loss","cancellation"}},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        }
        finally {Release();selfTestSink=null;Allowed=originalAllowed;Preflight=originalPreflight;}
    }
    internal static void SendMove(IntPtr hwnd, int processId, int clientX, int clientY)
    {
        GetWindowThreadProcessId(hwnd, out uint actualPid);
        if (actualPid != processId) throw new InvalidOperationException("Game window identity changed.");
        if (((clientX is < -0x8000 or > 0x7fff) || (clientY is < -0x8000 or > 0x7fff))) throw new ArgumentOutOfRangeException(nameof(clientX), "Client coordinates do not fit a mouse message.");
        nint lparam = (nint)((nuint)unchecked((short)clientX) | ((nuint)unchecked((short)clientY) << 16));
        SendMessageW(hwnd, 0x200u, 0, lparam);
    }
}
public sealed partial class Movement
{
    public Func<Vec,Vec,bool>? CanAdvance { get; set; }
    public Vec Forward { get; private set; }
    public double RadiansPerPixel { get; private set; }
    public double UnitsPerMs { get; private set; }
    bool advancing;
    Vec progressPosition;
    long progressAt, lastMotionTrace;
    readonly TurnResponse turnResponse = new();
    public void ResetTurnResponse() { turnResponse.Reset();smoothSteering.Reset();fineFacing=false; }
    public static double Angle(Vec a, Vec b) => Math.Atan2(a.X * b.Y - a.Y * b.X, a.X * b.X + a.Y * b.Y);
    public static Vec Rotate(Vec a, double angle) => new(a.X * Math.Cos(angle) - a.Y * Math.Sin(angle), a.X * Math.Sin(angle) + a.Y * Math.Cos(angle));
    // The client advances X/Z with cos(-heading-pi/2), sin(-heading-pi/2).
    public static Vec FromClientHeading(double heading) => new(-Math.Sin(heading), -Math.Cos(heading));
    public static int CalculateTurn(double errorRadians, double radiansPerPixel, bool walking)
    {
        if (!double.IsFinite(errorRadians) || !double.IsFinite(radiansPerPixel) || Math.Abs(radiansPerPixel) < .00001)
            throw new InvalidOperationException("Invalid aim calibration. Restart with F8.");
        double error = Math.Atan2(Math.Sin(errorRadians), Math.Cos(errorRadians));
        if (Math.Abs(error) < .035) return 0;
        // Turn decisively while the error is large, then use a smaller gain in
        // the final degrees so the faster feedback loop does not chatter around
        // the target. The previous .50/.80 gains and 25 ms cadence made a
        // character visibly lag behind a side target before the next swing.
        double correction = error * (Math.Abs(error) < .20 ? .70 : .88);
        double angularLimit = walking ? .28 : .50;
        int pixelLimit = walking ? 64 : 112;
        int pixels = Math.Clamp((int)Math.Round(Math.Clamp(correction, -angularLimit, angularLimit) / radiansPerPixel), -pixelLimit, pixelLimit);
        return pixels != 0 ? pixels : Math.Sign(error / radiansPerPixel);
    }
    internal static int TurnFeedbackDelay(int pixels)
    {
        int magnitude=Math.Abs(pixels);
        return magnitude>=64?12:magnitude>=24?16:20;
    }
    public static async Task<Movement> Calibrate(World world, CancellationToken token, Action<string, object>? trace = null)
    {
        var character=world.LocalPlayer();
        double Heading()
        {
            var current=world.LocalPlayer();
            if(!LocalCharacter.Same(character,current))throw new OperationCanceledException("Character changed during calibration.");
            return current.Heading;
        }
        const int maxAttempts=5;
        for(int attempt=1;attempt<=maxAttempts;attempt++)
        {
            // A transient busy/rest/animation state can swallow a single turn
            // command. Settle before sampling, and give the game a longer beat
            // between attempts so one momentary stall does not fail calibration.
            await Input.Delay(attempt==1?150:400,token);
            double start=Heading();
            trace?.Invoke("aim calibration start",new {RawHeading=start,Attempt=attempt,Character=character.Name});
            Input.Turn(60,token);
            double turned=await AimCalibration.WaitForHeading(Heading,Input.Delay,()=>Environment.TickCount64,start,token);
            Input.Turn(-60,token);
            double restored=await AimCalibration.WaitForHeading(Heading,Input.Delay,()=>Environment.TickCount64,turned,token);
            bool valid=AimCalibration.Measure(start,turned,restored,out double rawChange,out double restoreError);
            trace?.Invoke("aim calibration measured",new {Start=start,Turned=turned,Restored=restored,RawChange=rawChange,RestoreError=restoreError,Attempt=attempt,Valid=valid});
            if(valid)return new Movement {Forward=FromClientHeading(restored),UnitsPerMs=0,RadiansPerPixel=-rawChange/60};
            trace?.Invoke("aim calibration retry",new {Attempt=attempt});
        }
        throw new InvalidOperationException("The game did not give a consistent turn response. Keep the mouse still and press F8 again.");
    }

    public async Task<bool> Face(World world, Vec delta, CancellationToken token,double tolerance=.035)
    {
        double heading=world.PlayerHeading();
        Forward = FromClientHeading(heading);
        if (delta.Length < .01) {turnResponse.Reset();return true;}
        double angle = Angle(Forward, delta);
        if(Math.Abs(angle)<=tolerance)
        {
            // A wide attack tolerance must not restart the fine tracking ramp.
            if(Math.Abs(angle)<=.035&&!fineFacing){smoothSteering.Reset();turnResponse.Reset();}
            return true;
        }
        int pixels = smoothSteering.Next(angle,heading,RadiansPerPixel,false,Environment.TickCount64,turnRateBudget,TurnSpeedDegreesPerSecond);
        Vec position=world.PlayerPosition();
        if (pixels != 0) Input.Turn(pixels, token);
        if(turnResponse.Observe(position,heading,pixels,Environment.TickCount64,awaitingResponse:true))throw new TurnUnresponsiveException(position,Forward);
        await Input.Delay(TurnFeedbackDelay(pixels), token);
        return false;
    }
    public bool StopApproach()
    {
        bool wasTraveling=StopTravel();
        bool wasAdvancing = advancing;
        Input.Hold(Keys.W, false, CancellationToken.None);
        advancing = false;
        return wasAdvancing || wasTraveling;
    }
    public async Task Approach(World world, Vec position, Vec delta, CancellationToken token,bool watchTurns=false,double arrivalTolerance=0,Vec? steeringDelta=null)
    {
        long now = Environment.TickCount64;
        arrivalMotion.Observe(position,now,advancing);
        if(arrivalTolerance>0 && delta.Length<=arrivalMotion.BrakingDistance(arrivalTolerance))
        {
            await ApproachPrecisely(world,position+delta,arrivalTolerance,token);return;
        }
        double heading=world.PlayerHeading();
        Forward = FromClientHeading(heading);
        // Route lookahead changes heading only. Checkpoint distance still
        // controls braking, precise arrival and the route progress watchdog.
        Vec aim=steeringDelta is Vec candidate && candidate.Finite && candidate.Length>.01 ? candidate : delta;
        double angle = Angle(Forward, aim);
        // Hysteresis avoids repeatedly releasing/repressing W around the turn threshold.
        bool shouldAdvance = Math.Abs(angle) < (advancing ? .95 : .60);
        // If the current heading would clip an avoid zone, turn in place toward the clear route.
        if (shouldAdvance && CanAdvance!=null && !CanAdvance(position,position + Forward*Math.Clamp(delta.Length,.35,2.5))) shouldAdvance=false;
        if (shouldAdvance && !advancing) { progressPosition = position; progressAt = now; }
        Input.Hold(Keys.W, shouldAdvance, token);
        advancing = shouldAdvance;
        if (advancing)
        {
            if ((position - progressPosition).Length > .15) { progressPosition = position; progressAt = now; }
            else if (now - progressAt > 1800) { StopApproach(); throw new MovementBlockedException(position,Forward); }
        }
        int pixels = smoothSteering.Next(angle,heading,RadiansPerPixel,advancing,now,turnRateBudget,TurnSpeedDegreesPerSecond);
        if(!watchTurns)turnResponse.Reset();
        if (pixels != 0)
        {
            Input.Turn(pixels, token);
        }
        if(watchTurns && turnResponse.Observe(position,heading,pixels,now,awaitingResponse:Math.Abs(angle)>.035))throw new TurnUnresponsiveException(position,Forward);
        if (now - lastMotionTrace > 300)
        {
            lastMotionTrace = now;
            TraceLog.Record("approach feedback", new { Position = position, TargetDelta = delta, SteeringDelta=aim, Forward, ActualHeading=heading, ErrorDegrees = angle * 180 / Math.PI, HoldingW = advancing, TurnPixels = pixels });
        }
        await Input.Delay(TurnFeedbackDelay(pixels), token);
    }
}
