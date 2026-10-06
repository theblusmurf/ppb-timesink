using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using PoteMemoryProbe;

namespace PoteHunter;

public sealed partial class World
{
    DurabilityCodeLayout? durabilityLayout;
    public string DurabilityStatus { get; private set; } = "Equipment durability has not been checked";

    public void ConfigureDurability()
    {
        ResetDurability();
        if (!ConnectionVerified || handle is null || moduleBase == 0) return;
        try
        {
            var layout = ProfileDiscovery.DurabilityEvidence(PoteMemoryProbe.Program.ClientPath, profile.Scene);
            if (layout == null || !string.Equals(layout.ClientHash, ClientHash, StringComparison.OrdinalIgnoreCase))
            {
                DurabilityStatus = "Equipment durability signatures are missing, ambiguous or inconsistent with this client";
                return;
            }
            ProfileDiscovery.ValidateLoadedDurability(layout, moduleBase,
                (rva, count) => Native.Read(handle, (nint)(moduleBase + rva), count));
            durabilityLayout = layout;
            DurabilityStatus = "Equipment durability verified against repair, display and equipment-membership code";
        }
        catch (Exception ex) when (DurabilitySnapshot.RoutineFailure(ex))
        {
            durabilityLayout = null;
            DurabilityStatus = "Equipment durability unavailable: " + ex.Message;
        }
    }

    void ResetDurability()
    {
        durabilityLayout = null;
        DurabilityStatus = "Equipment durability layout is not enabled";
    }

    DurabilityReadContext DurabilityContext(int slotsOffset)
    {
        uint scene = Pointer(moduleBase + profile.Scene), actor = Pointer(moduleBase + profile.LocalActor);
        DurabilitySnapshot.RequirePointer(scene, slotsOffset + DurabilityCodeLayout.SlotCount * 4);
        DurabilitySnapshot.RequirePointer(actor, 0x16c);
        byte[] identity = Native.Read(handle!, (nint)(actor + 0x164), 8);
        uint id = BitConverter.ToUInt32(identity), zone = BitConverter.ToUInt32(identity, 4);
        if (zone is not (>= 1 and <= 18) && zone != 100)
            throw new InvalidOperationException("The equipment map context is not ready");
        var self = LocalPlayer();
        if (self.Id != id || scene != Pointer(moduleBase + profile.Scene) || actor != Pointer(moduleBase + profile.LocalActor) ||
            !identity.AsSpan().SequenceEqual(Native.Read(handle!, (nint)(actor + 0x164), 8)))
            throw new InvalidOperationException("The equipment character or map context changed");
        return new(ClientHash, Pid, moduleBase, scene, actor, (int)zone, self);
    }

    public DurabilityReading ReadDurability()
    {
        var layout = durabilityLayout;
        if (!ConnectionVerified || handle is null || layout == null)
            return DurabilitySnapshot.Unknown(DurabilityStatus);
        // This optional reader is observational. Health, focus, cancellation and
        // input ownership remain decisions of the existing repair/hunt guards.
        try
        {
            return DurabilitySnapshot.Read(layout.SlotsOffset, checked((uint)(moduleBase + layout.EquipmentVtableRva)),
                () =>
                {
                    if (!ClientProcessAlive) throw new InvalidOperationException("The connected client is no longer available");
                    return DurabilityContext(layout.SlotsOffset);
                }, (address, count) => Native.Read(handle!, (nint)address, count));
        }
        catch (Exception ex) when (DurabilitySnapshot.RoutineFailure(ex))
        { return DurabilitySnapshot.Unknown("Equipment durability read unavailable: " + ex.Message); }
    }
}

internal sealed record DurabilityReadContext(string ClientHash, int ProcessId, long Module,
    uint Scene, uint Actor, int Zone, Entity Self)
{
    public bool Same(DurabilityReadContext other) => ClientHash == other.ClientHash && ProcessId == other.ProcessId &&
        Module == other.Module && Scene == other.Scene && Actor == other.Actor && Zone == other.Zone &&
        LocalCharacter.Same(Self, other.Self);
    public string Key => $"{ClientHash}:{ProcessId}:{Module:X}:Zone{Zone}:{Scene:X8}:{Actor:X8}:{Self.Address:X}:{Self.Id:X8}:{Self.Generation}:{Self.Name}";
}

internal static class DurabilitySnapshot
{
    internal static readonly TimeSpan MaximumReadDuration = TimeSpan.FromSeconds(1);
    // The three repair consumers agree on these item members. A zero maximum
    // never means nonrepairable: only the client's eligibility predicate does.
    readonly record struct Slot(int Index, uint Wrapper, uint Kind, uint Item, uint Class, uint Definition,
        ulong Identity, ushort Type, ushort Location, byte Current, byte Maximum, ushort DefinitionType, uint Flags)
    {
        public bool Repairable => Wrapper != 0 && Kind == 0 && (Flags & 9) == 1;
    }
    sealed record Frame(byte[] Membership, Slot[] Slots);

    internal static bool RoutineFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or Win32Exception or
        InvalidOperationException or ArgumentException or OverflowException or NotSupportedException or System.Security.SecurityException;
    internal static DurabilityReading Unknown(string reason) => new(false, "", [], DateTime.UtcNow, reason);
    internal static void RequirePointer(long address, int count)
    {
        if (address < 0x10000 || count < 0 || (ulong)address + (uint)count > uint.MaxValue)
            throw new InvalidOperationException("An equipment pointer is unavailable");
    }
    static byte[] ExactRead(Func<long, int, byte[]> read, long address, int count)
    {
        RequirePointer(address, count);
        byte[] bytes = read(address, count);
        if (bytes.Length != count) throw new InvalidOperationException("An equipment field could not be read completely");
        return bytes;
    }

    static Frame ReadFrame(Func<long, int, byte[]> read, uint scene, int slotsOffset, uint equipmentClass)
    {
        long array = (long)scene + slotsOffset;
        byte[] membership = ExactRead(read, array, DurabilityCodeLayout.SlotCount * 4);
        var slots = new Slot[DurabilityCodeLayout.SlotCount];
        var wrappers = new HashSet<uint>();
        var items = new HashSet<uint>();
        for (int index = 0; index < slots.Length; index++)
        {
            uint wrapper = BitConverter.ToUInt32(membership, index * 4);
            if (wrapper == 0) { slots[index] = new(index, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0); continue; }
            if (!wrappers.Add(wrapper)) throw new InvalidOperationException("Equipment slots contain a duplicate wrapper");
            byte[] control = ExactRead(read, wrapper, 0x1c4);
            uint kind = BitConverter.ToUInt32(control, 0x20);
            // The actual client repair predicate excludes every non-item kind.
            if (kind != 0) { slots[index] = new(index, wrapper, kind, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0); continue; }
            uint item = BitConverter.ToUInt32(control, 0x1c0);
            if (!items.Add(item)) throw new InvalidOperationException("Equipment slots contain a duplicate item");
            byte[] value = ExactRead(read, item, 0x19);
            uint definition = BitConverter.ToUInt32(value, 4);
            byte[] details = ExactRead(read, definition, 0x2c);
            var slot = new Slot(index, wrapper, kind, item, BitConverter.ToUInt32(value), definition,
                BitConverter.ToUInt64(value, 8), BitConverter.ToUInt16(value, 0x10), BitConverter.ToUInt16(value, 0x12),
                value[0x15], value[0x18], BitConverter.ToUInt16(details), BitConverter.ToUInt32(details, 0x28));
            if (slot.Type == 0 || slot.Type != slot.DefinitionType)
                throw new InvalidOperationException("An equipped item has an inconsistent definition");
            // The dispatcher's location nibble 1 selects the verified equipped
            // array; high bits are its index. Bag and appearance arrays differ.
            if (slot.Location != (index << 4 | 1))
                throw new InvalidOperationException("An equipped item no longer belongs to its reported slot");
            if (slot.Repairable && (slot.Class != equipmentClass || slot.Maximum == 0 || slot.Current > slot.Maximum))
                throw new InvalidOperationException("An equipped item's durability class or range is unavailable");
            slots[index] = slot;
        }
        if (!membership.AsSpan().SequenceEqual(ExactRead(read, array, membership.Length)))
            throw new InvalidOperationException("Equipment membership changed during the read");
        return new(membership, slots);
    }

    internal static DurabilityReading Read(int slotsOffset, uint equipmentClass,
        Func<DurabilityReadContext> context, Func<long, int, byte[]> read, Func<TimeSpan>? elapsed = null)
    {
        var watch = Stopwatch.StartNew();
        DateTime observed = DateTime.UtcNow;
        try
        {
            if (slotsOffset is < 0x1000 or > 0x4000 || (slotsOffset & 3) != 0)
                return Unknown("The equipped-slot layout is unavailable");
            RequirePointer(equipmentClass, 0x14);
            var before = context();
            var first = ReadFrame(read, before.Scene, slotsOffset, equipmentClass);
            var second = ReadFrame(read, before.Scene, slotsOffset, equipmentClass);
            var after = context();
            if (!before.Same(after)) return Unknown("The equipment character or map context changed; waiting for a stable sample");
            if (!first.Membership.AsSpan().SequenceEqual(second.Membership) || !first.Slots.SequenceEqual(second.Slots))
                return Unknown("Equipped items or durability changed during the read; waiting for a stable sample");
            if ((elapsed?.Invoke() ?? watch.Elapsed) > MaximumReadDuration)
                return Unknown("Equipment reading took too long; waiting for a fresh sample");
            var result = first.Slots.Where(s => s.Repairable).Select(s => new DurableEquipment(
                $"Equipment slot {s.Index + 1}", $"{s.Identity:X16}:{s.Type:X4}:{s.Item:X8}", s.Current, s.Maximum)).ToArray();
            return new(true, before.Key, result, observed, result.Length == 0 ?
                "All equipped slots were read; no repairable equipment is present" :
                $"Durability read from all {DurabilityCodeLayout.SlotCount} equipped slots; {result.Length} repairable items");
        }
        catch (Exception ex) when (RoutineFailure(ex))
        { return Unknown("Equipment durability read unavailable: " + ex.Message); }
    }
}

internal static class DurabilityReaderChecks
{
    public static void Run()
    {
        ProfileDiscovery.CheckDurabilityLayouts();
        var checks = new List<string> { "unique independent repair/display/membership signatures", "missing and ambiguous signatures rejected",
            "all scene roots and equipped-array members agree", "equipment class repair method and packed-location dispatch verified",
            "loaded code and relocation validation", "scene offset discovered rather than assumed" };
        void Check(bool pass, string label)
        { if (!pass) throw new InvalidOperationException("Durability reader check failed: " + label); checks.Add(label); }
        const uint scene = 0x100000, equipmentClass = 0x849000;
        const int offset = 0x14cc;
        var context = new DurabilityReadContext("synthetic-client", 71, 0x400000, scene, 0x110000, 3,
            new Entity(0x120000, 123, "Synthetic", new(1, 2), 0, Generation: 7, Model: "PC_fixture"));
        var memory = new Dictionary<long, byte[]>();
        void U32(byte[] bytes, int at, uint value) => BitConverter.TryWriteBytes(bytes.AsSpan(at), value);
        void U16(byte[] bytes, int at, ushort value) => BitConverter.TryWriteBytes(bytes.AsSpan(at), value);
        uint Wrapper(int index) => 0x200000u + (uint)index * 0x1000;
        uint Item(int index) => Wrapper(index) + 0x400;
        uint Definition(int index) => Wrapper(index) + 0x800;
        void Reset()
        { memory.Clear(); memory.Add(scene + offset, new byte[64]); }
        void Add(int index, byte current, byte maximum, uint flags = 1, uint kind = 0)
        {
            U32(memory[scene + offset], index * 4, Wrapper(index));
            byte[] wrapper = new byte[0x1c4], value = new byte[0x19], definition = new byte[0x2c];
            U32(wrapper, 0x20, kind); U32(wrapper, 0x1c0, Item(index));
            U32(value, 0, equipmentClass); U32(value, 4, Definition(index));
            U32(value, 8, (uint)index + 500); U32(value, 12, 600);
            U16(value, 0x10, (ushort)(index + 100)); U16(value, 0x12, (ushort)(index << 4 | 1));
            value[0x15] = current; value[0x18] = maximum;
            U16(definition, 0, (ushort)(index + 100)); U32(definition, 0x28, flags);
            memory[Wrapper(index)] = wrapper; memory[Item(index)] = value; memory[Definition(index)] = definition;
        }
        byte[] Read(long address, int count) => memory.TryGetValue(address, out var bytes) && bytes.Length >= count ?
            bytes.AsSpan(0, count).ToArray() : throw new Win32Exception(299, "Synthetic unreadable memory");
        DurabilityReading Sample() => DurabilitySnapshot.Read(offset, equipmentClass, () => context, Read);
        Reset(); Add(0, 10, 20); Add(15, 20, 100);
        var reading = Sample();
        Check(reading.Known && reading.Items.Count == 2 && reading.LowestPercent == 20 &&
            reading.Items.Any(i => i.Slot == "Equipment slot 16"), "all sixteen slots including slot 16; minimum percentage rather than smallest raw value");
        Check(reading.Context == context.Key && reading.ObservedUtc.Kind == DateTimeKind.Utc, "character/client context and UTC observation");
        Add(3, 0, 0, 8); Add(7, 0, 0, 9); Add(8, 0, 0, 0); Add(9, 0, 0, 1, 1);
        Check(Sample() is { Known: true, Items.Count: 2 }, "nonrepairable flags and non-item wrappers positively excluded");
        Reset(); Add(0, 0, 100);
        Check(Sample().LowestPercent == 0, "broken equipment is a valid zero-percent reading");
        foreach (byte amount in new byte[] { 65, 66, 69, 70, 72 })
        {
            memory[Item(0)][0x15] = amount;
            if (Sample().LowestPercent != amount) throw new InvalidOperationException("Instance durability mistaken for a definition category");
        }
        checks.Add("numeric durability does not confuse definition-category bytes with instance fields");
        memory[Item(0)][0x15] = 0;
        memory[Item(0)][0x18] = 0;
        Check(!Sample().Known, "zero maximum on repairable equipment remains unknown");
        memory[Item(0)][0x18] = 10; memory[Item(0)][0x15] = 11;
        Check(!Sample().Known, "current greater than maximum rejected");
        Reset(); Add(0, 10, 100); U16(memory[Item(0)], 0x12, 2);
        Check(!Sample().Known, "bag membership rejected even with plausible durability");
        U16(memory[Item(0)], 0x12, 14);
        Check(!Sample().Known, "appearance membership rejected");
        U16(memory[Item(0)], 0x12, 0x11);
        Check(!Sample().Known, "wrong equipped-slot index rejected");
        Reset(); Add(0, 10, 100); U32(memory[Item(0)], 0, equipmentClass + 4);
        Check(!Sample().Known, "unverified item class rejected");
        Reset(); Add(0, 10, 100); U16(memory[Definition(0)], 0, 300);
        Check(!Sample().Known, "definition mismatch rejected");
        Reset(); Add(0, 10, 100); U32(memory[scene + offset], 4, Wrapper(0));
        Check(!Sample().Known, "duplicate occupied wrapper rejected");
        Reset(); Add(0, 10, 100); Add(1, 20, 100); U32(memory[Wrapper(1)], 0x1c0, Item(0));
        Check(!Sample().Known, "duplicate equipped instance rejected");
        Reset(); Add(0, 10, 100); Add(15, 20, 100); memory.Remove(Definition(15));
        Check(!Sample().Known, "one unreadable occupied slot invalidates the complete set");
        Reset(); U32(memory[scene + offset], 15 * 4, 32);
        Check(!Sample().Known, "invalid nonzero pointer is not an empty slot");
        Reset(); Add(0, 10, 100);
        Check(!DurabilitySnapshot.Read(offset, equipmentClass, () => context,
            (address, count) => address == Item(0) ? new byte[count - 1] : Read(address, count)).Known,
            "short reads remain unknown without throwing");
        int itemReads = 0;
        Check(!DurabilitySnapshot.Read(offset, equipmentClass, () => context, (address, count) =>
        {
            if (address == Item(0) && ++itemReads == 2) memory[Item(0)][0x15]++;
            return Read(address, count);
        }).Known, "durability mutation between snapshots rejected");
        Reset(); Add(0, 10, 100); int memberReads = 0;
        Check(!DurabilitySnapshot.Read(offset, equipmentClass, () => context, (address, count) =>
        {
            if (address == scene + offset && ++memberReads == 2) U32(memory[scene + offset], 0, 0);
            return Read(address, count);
        }).Known, "membership mutation within a snapshot rejected");
        Reset(); Add(0, 10, 100);
        foreach (var changed in new[] { context with { Zone = 4 }, context with { Scene = scene + 0x1000 },
            context with { Actor = context.Actor + 4 }, context with { ProcessId = 72 }, context with { Module = 0x500000 },
            context with { ClientHash = "other-client" }, context with { Self = context.Self with { Id = 124 } },
            context with { Self = context.Self with { Generation = 8 } }, context with { Self = context.Self with { Address = 0x120004 } },
            context with { Self = context.Self with { Name = "Other" } } })
        {
            int contexts = 0;
            if (DurabilitySnapshot.Read(offset, equipmentClass, () => ++contexts == 1 ? context : changed, Read).Known)
                throw new InvalidOperationException("Durability accepted a changed context");
        }
        checks.Add("zone, scene, actor, process, image and character identity transitions rejected");
        int movedContexts = 0;
        Check(DurabilitySnapshot.Read(offset, equipmentClass,
            () => ++movedContexts == 1 ? context : context with { Self = context.Self with { Position = new(3, 4), Heading = 2 } }, Read).Known,
            "ordinary character movement preserves equipment identity");
        Check(!DurabilitySnapshot.Read(offset, equipmentClass, () => context, Read,
            () => DurabilitySnapshot.MaximumReadDuration + TimeSpan.FromMilliseconds(1)).Known, "slow reads are not stamped fresh");
        Reset(); reading = Sample();
        Check(reading.Known && reading.Items.Count == 0 && reading.LowestPercent == null,
            "complete empty equipment has no actionable minimum");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "durability-reader-checks.json"),
            JsonSerializer.Serialize(new { Passed = true, LiveGameTested = false, HardwareInputEmitted = false,
                EquippedSlotCount = DurabilityCodeLayout.SlotCount, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
