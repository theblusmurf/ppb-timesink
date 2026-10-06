using System.Text;
using System.Text.Json;

namespace PoteHunter;

internal static class PlayerNamesChecks
{
    sealed class Fixture
    {
        internal const uint Manager = 0x10000, Sentinel = 0x11000, Vtable = 0x900000;
        internal readonly byte[] Memory = new byte[0x300000];
        internal readonly List<Entity> Entities = [];
        internal readonly Dictionary<long, int> Reads = [];
        internal Action<long, int>? BeforeRead;
        internal bool ContextStable = true;
        internal Func<long>? Milliseconds = () => 0;
        internal uint Node(int i) => 0x12000u + (uint)i * 0x100;
        internal uint Record(int i) => 0x14000u + (uint)i * 0x200;
        internal uint Body(int i) => 0x18000u + (uint)i * 0x800;
        internal uint Heap(int i) => 0x50000u + (uint)i * 0x400;
        internal void U32(uint at, uint value) => BitConverter.TryWriteBytes(Memory.AsSpan((int)at), value);
        internal void NameAt(uint record, string value, uint heap)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            Memory.AsSpan((int)record + 8, 0x18).Clear();
            U32(record + 0x18, (uint)bytes.Length);
            if (bytes.Length <= 15)
            { U32(record + 0x1c, 15); bytes.CopyTo(Memory, (int)record + 8); }
            else
            { U32(record + 8, heap); U32(record + 0x1c, (uint)Math.Max(31, bytes.Length)); bytes.CopyTo(Memory, (int)heap); Memory[heap + bytes.Length] = 0; }
        }
        internal Entity Add(uint id, string name, string model = "PC_MAN.GCMDS")
        {
            int index = Entities.Count; uint node = Node(index), record = Record(index), body = Body(index);
            U32(Manager + 4, Sentinel); U32(Manager + 8, (uint)index + 1);
            U32(Sentinel, Node(0)); U32(Sentinel + 4, node);
            U32(node, Sentinel); U32(node + 4, index == 0 ? Sentinel : Node(index - 1)); U32(node + 8, record);
            if (index > 0) U32(Node(index - 1), node);
            U32(record, id); U32(record + 4, 700u + (uint)index); Memory[record + 0x74] = 1;
            NameAt(record, name, Heap(index));
            U32(body, Vtable); U32(body + 4, id); U32(body + 8, (uint)index + 100); U32(body + 0x2c, 15);
            byte[] modelBytes = Encoding.UTF8.GetBytes(model);
            U32(body + 0x60, (uint)modelBytes.Length); U32(body + 0x64, modelBytes.Length <= 15 ? 15u : 31u);
            if (modelBytes.Length <= 15) modelBytes.CopyTo(Memory, (int)body + 0x50);
            else
            { uint modelHeap = 0x60000u + (uint)index * 0x100; U32(body + 0x50, modelHeap); modelBytes.CopyTo(Memory, (int)modelHeap); }
            var entity = new Entity(body, id, "", new(10 + index, 20), 0, Generation: (uint)index + 100, Model: model);
            Entities.Add(entity); return entity;
        }
        byte[] Read(long address, int count)
        {
            int occurrence = Reads.GetValueOrDefault(address) + 1; Reads[address] = occurrence;
            BeforeRead?.Invoke(address, occurrence);
            if (address < 0 || address + count > Memory.Length) throw new System.ComponentModel.Win32Exception(299);
            return Memory.AsSpan((int)address, count).ToArray();
        }
        internal PlayerNameSnapshot Snapshot(uint selfId = 1)
            => PlayerNameReader.Read(Entities, selfId, Manager, Vtable, Read, () => ContextStable, Milliseconds);
    }

    internal static void Run()
    {
        var checks = new List<string>();
        void Require(bool condition, string label)
        { if (!condition) throw new Exception("Other-player names: " + label); checks.Add(label); }
        Fixture One(string name = "Scout") { var f = new Fixture(); f.Add(42, name); return f; }
        void Missing(Action<Fixture> change, string label, string name = "Scout")
        { var f = One(name); change(f); Require(!f.Snapshot().Names.ContainsKey(42), label); }

        ProfileDiscovery.CheckPlayerNameLayouts();
        checks.Add("synthetic current/old/missing/ambiguous signatures, manager roots, linked consumers and loaded relocation checks");
        var mixed = new Fixture(); mixed.Add(42, "Scout"); mixed.Add(43, "Réka長名前 player");
        var snapshot = mixed.Snapshot();
        Require(snapshot.Consistent && snapshot.Names.GetValueOrDefault(42u) == "Scout" && snapshot.Names.GetValueOrDefault(43u) == "Réka長名前 player", "inline and heap Unicode names bind to their own UIDs");
        Require(mixed.Reads[mixed.Node(0)] == 2 && mixed.Reads[mixed.Node(1)] == 2, "one bounded list traversal plus link verification for the entire poll");
        Require(mixed.Entities.All(e => e.Name == ""), "optional reader does not mutate accepted entity snapshots");
        var scope = new Fixture(); scope.Add(1, "Self"); scope.Add(0x40000002, "Guard"); scope.Add(3, "Mount", "PC_MOUNT.GCMDS"); scope.Add(4, "Other");
        Require(scope.Snapshot().Names.Keys.SequenceEqual(new[] { 4u }), "local character, NPC IDs and unknown models excluded");
        var preferred = One(); preferred.Entities[0] = preferred.Entities[0] with { Name = "Existing creature name" };
        Require(preferred.Snapshot().Names.Count == 0 && preferred.Reads.Count == 0 && preferred.Entities[0].Name == "Existing creature name", "available creature name remains preferred without a metadata traversal");
        foreach (string model in new[] { "PC_MAN.GCMDS", "PC_WOMAN.GCMDS", "PC_Akhan_A.GCMDS", "PC_Akhan_B.GCMDS" })
        { var f = new Fixture(); f.Add(42, "Scout", model); Require(f.Snapshot().Names.GetValueOrDefault(42u) == "Scout", "exact recognized body supported: " + model); }
        Missing(f => f.U32(f.Record(0) + 0x18, 129), "oversized string rejected");
        Missing(f => f.U32(f.Record(0) + 0x1c, 2), "capacity below length rejected");
        Missing(f => f.U32(f.Record(0) + 0x1c, 4097), "unbounded string capacity rejected");
        Missing(f => { f.U32(f.Record(0) + 0x18, 16); f.U32(f.Record(0) + 0x1c, 15); }, "invalid inline layout rejected");
        Missing(f => f.U32(f.Record(0) + 8, 3), "invalid heap pointer rejected", "A heap-backed player name");
        Missing(f => f.Memory[f.Record(0) + 8 + 5] = (byte)'x', "missing terminator rejected");
        Missing(f => { f.Memory[f.Record(0) + 8] = 0xc3; f.Memory[f.Record(0) + 9] = 0x28; }, "invalid UTF8 rejected");
        Missing(f => f.Memory[f.Record(0) + 8] = (byte)'\n', "control characters rejected");
        Missing(f => f.Memory[f.Record(0) + 9] = 0, "embedded null rejected");
        Missing(f => f.NameAt(f.Record(0), "   ", f.Heap(0)), "whitespace names remain unavailable");
        Missing(f => f.Memory[f.Record(0) + 0x74] = 0, "inactive deleted UID record rejected");
        var duplicate = One(); duplicate.Add(43, "Replacement"); duplicate.U32(duplicate.Record(1), 42);
        Require(duplicate.Snapshot().Names.Count == 0, "duplicate active UID records are ambiguous");
        var duplicateBody = One(); duplicateBody.Entities.Add(duplicateBody.Entities[0] with { Address = duplicateBody.Body(1) });
        Require(duplicateBody.Snapshot().Names.Count == 0, "duplicate accepted body UIDs excluded");
        Missing(f => f.BeforeRead = (address, occurrence) => { if (address == f.Record(0) && occurrence == 2) f.U32(f.Record(0) + 4, 999); }, "changed unknown record header rejected without treating it as creature generation");
        Missing(f => f.BeforeRead = (address, occurrence) => { if (address == f.Record(0) && occurrence == 2) f.U32(f.Record(0), 43); }, "reused record UID rejected");
        Missing(f => f.BeforeRead = (address, occurrence) => { if (address == f.Record(0) && occurrence == 2) f.Memory[f.Record(0) + 0x74] = 0; }, "record deletion during snapshot rejected");
        Missing(f => f.BeforeRead = (address, occurrence) => { if (address == f.Heap(0) && occurrence == 2) f.Memory[f.Heap(0)] = (byte)'Z'; }, "changing heap name bytes rejected", "A heap-backed player name");
        Missing(f => f.BeforeRead = (address, occurrence) => { if (address == f.Record(0) && occurrence == 3) f.NameAt(f.Record(0), "Different", f.Heap(0)); }, "record header rechecked after variable name reads");
        Missing(f => f.BeforeRead = (address, occurrence) => { if (address == f.Body(0) && occurrence == 2) f.U32(f.Body(0) + 8, 101); }, "reused body generation rejected");
        Missing(f => f.BeforeRead = (address, occurrence) => { if (address == f.Body(0) && occurrence == 2) f.Memory[f.Body(0) + 0x12] = 1; }, "inactive body rejected before attachment");
        Missing(f => f.BeforeRead = (address, occurrence) => { if (address == f.Body(0) && occurrence == 2) f.Memory[f.Body(0) + 0x50] = (byte)'X'; }, "changed model cannot receive an old name");
        Missing(f => f.U32(f.Body(0) + 8, 999), "body changed since accepted poll rejected before traversal");
        Missing(f => f.U32(f.Body(0) + 4, 43), "body UID/address mismatch rejected");
        Missing(f => f.ContextStable = false, "zone, local identity or manager-root change clears optional names");
        Missing(f => f.BeforeRead = (address, occurrence) => { if (address == f.Node(0) && occurrence == 2) f.U32(f.Node(0), f.Node(0)); }, "changed list node links rejected");
        Missing(f => f.BeforeRead = (address, occurrence) => { if (address == Fixture.Manager && occurrence == 2) f.U32(Fixture.Manager + 8, 2); }, "changed manager count rejected");
        Missing(f => f.U32(Fixture.Manager + 8, 8193), "list count bound enforced");
        Missing(f => f.U32(f.Node(0), f.Node(0)), "cyclic list rejected");
        Missing(f => f.BeforeRead = (address, _) => { if (address == f.Record(0)) throw new System.ComponentModel.Win32Exception(299); }, "read failure preserves accepted body with unavailable name");
        var fresh = One(); Require(fresh.Snapshot().Names.ContainsKey(42), "first fresh name observed");
        fresh.NameAt(fresh.Record(0), "", fresh.Heap(0));
        Require(fresh.Snapshot().Names.Count == 0, "no previous-poll name cache survives unavailable metadata");
        var hpChange = One(); hpChange.BeforeRead = (address, occurrence) => { if (address == hpChange.Record(0) && occurrence == 2) hpChange.U32(hpChange.Record(0) + 0x44, 500); };
        Require(hpChange.Snapshot().Names.GetValueOrDefault(42u) == "Scout", "ordinary HP changes do not change name identity");
        var newDuplicate = One(); newDuplicate.Add(43, "Other");
        newDuplicate.BeforeRead = (address, occurrence) => { if (address == newDuplicate.Record(1) && occurrence == 2) newDuplicate.U32(newDuplicate.Record(1), 42); };
        Require(newDuplicate.Snapshot().Names.Count == 0, "UID reuse into another accepted identity rejected across the snapshot");
        var deadline = One(); long elapsed = 0; deadline.Milliseconds = () => elapsed;
        deadline.BeforeRead = (_, _) => elapsed += 20;
        Require(deadline.Snapshot().Names.Count == 0 && deadline.Reads.Values.Sum() <= 3,
            "injected elapsed deadline ends optional reads before a long poll");
        var manyBodies = One();
        for (uint id = 43; id < 8234; id++)
            manyBodies.Entities.Add(manyBodies.Entities[0] with { Id = id, Address = 0x17000 + id * 0x20 });
        long bodyElapsed = 0; manyBodies.Milliseconds = () => bodyElapsed;
        manyBodies.BeforeRead = (_, _) => bodyElapsed += 20;
        Require(manyBodies.Snapshot().Names.Count == 0 && manyBodies.Reads.Values.Sum() <= 3,
            "deadline escapes per-body failure handling and stops a worst-size candidate list immediately");
        var maximum = One(); maximum.Milliseconds = () => 0;
        maximum.U32(Fixture.Manager + 8, 8192); maximum.U32(maximum.Node(0), 0x80000);
        for (int i = 0; i < 8191; i++)
        {
            uint node = 0x80000u + (uint)i * 0x20, record = 0xc0000u + (uint)i * 0x80;
            maximum.U32(node, i == 8190 ? Fixture.Sentinel : node + 0x20);
            maximum.U32(node + 4, i == 0 ? maximum.Node(0) : node - 0x20); maximum.U32(node + 8, record);
            maximum.U32(record, (uint)i + 10000); maximum.U32(record + 0x1c, 15); maximum.Memory[record + 0x74] = 1;
        }
        Require(maximum.Snapshot().Names.Count == 0 && maximum.Reads.Values.Sum() <= PlayerNameReader.MaximumReads,
            "worst bounded pooled list stops at the fixed read cap without attaching partial names");
        using (var disconnected = new World())
        { Require(!disconnected.PlayerNamesSupported && disconnected.PlayerNamesLastAttachedCount == 0, "optional name reader starts disabled and resets on disposal"); disconnected.Dispose(); Require(!disconnected.PlayerNamesSupported, "disconnected reader stays disabled"); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "player-name-reader-checks.json"), JsonSerializer.Serialize(new
        { Passed = true, HardwareInputEmitted = false, LiveClientRead = false, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
