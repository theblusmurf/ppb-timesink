using System.Runtime.InteropServices;

namespace PoteHunter;

internal sealed class HeldInputs<T>(Action<T, bool> emit) where T : notnull
{
    readonly HashSet<T> held = new();
    public bool IsHeld(T key) => held.Contains(key);
    public void Set(T key, bool down)
    {
        if (down)
        {
            if (held.Contains(key)) return;
            emit(key, true);
            held.Add(key);
        }
        else if (held.Contains(key)) { emit(key, false); held.Remove(key); }
    }
    public void ReleaseAll(Func<T,bool>? preserve = null)
    {
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
    static readonly HeldInputs<Keys> keys = new((key, down) => { Send(KeyPacket(key, !down)); RecordHeldInput(down ? "key down" : "key up", new { Key = key.ToString() }); });
    static readonly HeldInputs<bool> buttons = new((right, down) => { Send(MousePacket(right, !down)); RecordHeldInput(down ? "mouse down" : "mouse up", new { Button = right ? "right" : "left" }); });
    public static bool BasicAttackHeld => buttons.IsHeld(false);
    public static bool RightButtonHeld => buttons.IsHeld(true);

    internal static void HoldRangedFire(CancellationToken token) => HoldMouse(true,true,token);
    static void RecordHeldInput(string stage, object details)
    {
        // Logging after successful input must not prevent its held state from being committed.
        try { TraceLog.Record(stage, details); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public static Func<bool> Allowed = () => false;
    public static Action? Preflight;
    public static Func<bool>? PickupHoldProvider;
    public static bool PickupHeld => keys.IsHeld(Keys.E);
    static Action<Packet>? selfTestSink;
    static Func<Keys,bool>? selfTestDown;
    public static bool Down(Keys key) => selfTestDown?.Invoke(key) ?? (selfTestSink==null && (GetAsyncKeyState((int)key) & 0x8000) != 0);
    static void Check(CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if(Down(Keys.F9))throw new OperationCanceledException("F9 stop key pressed.");
            if(Down(Keys.Escape))throw new OperationCanceledException("Escape stop key pressed.");
            if(Down(Keys.Enter) && !keys.IsHeld(Keys.Enter))throw new OperationCanceledException("Enter/chat key pressed.");
            if(!Allowed())throw new OperationCanceledException("Game window lost focus or is no longer available.");
            Preflight?.Invoke();
            // The nearby-loot decision owns E while enabled. Updating raw held
            // state here avoids recursively invoking Check through Hold.
            if(PickupHoldProvider is { } pickup)keys.Set(Keys.E,pickup());
        }
        catch {Release();throw;}
    }
    static void Send(Packet p)
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
    public static async Task Delay(int ms, CancellationToken token)
    {
        long deadline = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < deadline) { Check(token); int step = (int)Math.Min(20, deadline - Environment.TickCount64); if (step > 0) await Task.Delay(step, token); }
        Check(token);
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
        keys.Set(key, down);
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
    public static async Task ChargeClick(int milliseconds, CancellationToken token)
    {
        if(milliseconds<50||milliseconds>10000)throw new ArgumentOutOfRangeException(nameof(milliseconds));
        try { HoldMouse(true,true,token); await Delay(milliseconds,token); }
        finally { HoldMouse(true,false,token); }
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
    public static void Release(bool preserveNearbyPickup = false)
    {
        // Activity transitions may retain an already-held E, but never create
        // new input or consult a provider during cleanup.
        keys.ReleaseAll(preserveNearbyPickup && PickupHoldProvider!=null ? key=>key==Keys.E : null);
        buttons.ReleaseAll();
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
    public void ResetTurnResponse() => turnResponse.Reset();
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
        int pixels = Math.Abs(angle)<=tolerance ? 0 : CalculateTurn(angle, RadiansPerPixel, false);
        Vec position=world.PlayerPosition();
        if(turnResponse.Observe(position,heading,pixels,Environment.TickCount64))throw new TurnUnresponsiveException(position,Forward);
        if (pixels == 0) return true;
        Input.Turn(pixels, token);
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
    public async Task Approach(World world, Vec position, Vec delta, CancellationToken token,bool watchTurns=false)
    {
        long now = Environment.TickCount64;
        double heading=world.PlayerHeading();
        Forward = FromClientHeading(heading);
        double angle = Angle(Forward, delta);
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
        int pixels = CalculateTurn(angle, RadiansPerPixel, advancing);
        if(!watchTurns)turnResponse.Reset();
        else if(turnResponse.Observe(position,heading,pixels,now))throw new TurnUnresponsiveException(position,Forward);
        if (pixels != 0)
        {
            Input.Turn(pixels, token);
        }
        if (now - lastMotionTrace > 300)
        {
            lastMotionTrace = now;
            TraceLog.Record("approach feedback", new { Position = position, TargetDelta = delta, Forward, ErrorDegrees = angle * 180 / Math.PI, HoldingW = advancing, TurnPixels = pixels });
        }
        await Input.Delay(TurnFeedbackDelay(pixels), token);
    }
}
