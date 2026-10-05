using System.Security.Cryptography;

namespace PoteHunter;

internal sealed record DurabilityCodeLayout(string ClientHash, int SlotsOffset,
    uint EquipmentVtableRva, IReadOnlyList<SignatureEvidence> Evidence)
{
    public const int SlotCount = 16;
}

public static partial class ProfileDiscovery
{
    // These independent consumers establish semantics, not just plausible numbers:
    // the repair-price loop, repair acknowledgement, item-update packet handler,
    // tooltip, CEquipment constructor/price method, and packed-location dispatcher.
    // The client tests wrapper.kind == 0 and (definition.flags & 9) == 1 for repair.
    // It formats item.byte15 / item.byte18 and restores byte15 from byte18 after repair.
    static readonly Rule[] durabilityRules =
    [
        new("Repair scene", "8B 3D ?? ?? ?? ?? 33 DB 39 9F 8C 36 00 00 7E ?? C6 45 FF 01 39 9F 88 36 00 00 7F ?? 88 5D FF", 2),
        new("Repair equipped loop", "81 C7 ?? ?? ?? ?? C7 45 F8 10 00 00 00 8B 0F 85 C9 74 ?? 8B 71 20 85 F6 75 ?? 8B 91 C0 01 00 00 8B 42 04 8B 40 28 24 09 3C 01 75 ?? 8A 42 15 88 45 FE 85 F6 74 ?? E8 ?? ?? ?? ?? EB ?? 8A 42 18 38 45 FE 73 ?? 8B 07 8B 88 C0 01 00 00 8B 01 FF 50 10", -1),
        new("Repair equipped stride", "83 C7 04 83 6D F8 01 75 ?? 5F 5E 8B C3 5B 8B E5 5D C3", -1),
        new("Repair result scene", "8B 3D ?? ?? ?? ?? 8D 45 E8 8B 35 ?? ?? ?? ?? 50 8D 45 F0 89 7D EC 50 FF 75 0C E8 ?? ?? ?? ?? 89 86 74 01 00 00 83 C4 0C 8B 0D ?? ?? ?? ?? 8B 81 74 01 00 00 85 C0 0F 85 ?? ?? ?? ?? 8B 45 F0 3B 81 64 01 00 00 75 ??", 2),
        new("Repair result restores maximum", "BE 0F 00 00 00 8D 97 ?? ?? ?? ?? 32 DB 8D 7E F2 90 8B 02 85 C0 74 ?? 83 78 20 00 75 ?? 8B 88 C0 01 00 00 8B 41 04 8B 40 28 24 09 3C 01 75 ?? 84 DB 75 ?? 80 79 15 00 0F B6 DB 0F 44 DF 8A 41 18 88 41 15 83 C2 04 2B F7 75 ??", -1),
        new("Durability update scene", "8B 3D ?? ?? ?? ?? 85 FF 0F 84 ?? ?? ?? ?? 8B 35 ?? ?? ?? ?? 8D 45 FD 50 8D 45 FE 50 8D 45 FF 50 8D 45 F4 50 FF 75 0C E8 ?? ?? ?? ?? 89 86 74 01 00 00 83 C4 14 8B 1D ?? ?? ?? ?? 89 5D F8 85 DB 0F 84 ?? ?? ?? ??", 2),
        new("Durability update fields", "8A 45 FF 3C 10 0F 83 ?? ?? ?? ?? 0F B6 C0 8B 94 87 ?? ?? ?? ?? 85 D2 0F 84 ?? ?? ?? ?? 8B 8A C0 01 00 00 8A 45 FE 88 41 15 8A 4D FD 84 C9 74 ?? 83 7A 20 00 75 ?? 8B 82 C0 01 00 00 88 48 18", -1),
        new("Durability tooltip definition", "8B 80 C0 01 00 00 85 C0 8B 40 04 74 ?? 8A 48 15 80 F9 4A 72 ?? 80 F9 6B 76 ?? 80 F9 70 75 ?? B1 01 EB ?? 32 C9 F6 40 28 08 88 8D D3 F7 FF FF 0F 84 ?? ?? ?? ??", -1),
        new("Durability tooltip values", "8A 40 15 3C 48 0F 84 ?? ?? ?? ?? 3C 42 0F 84 ?? ?? ?? ?? 3C 41 0F 84 ?? ?? ?? ?? 3C 45 0F 84 ?? ?? ?? ?? 3C 46 0F 84 ?? ?? ?? ?? 8B 8D F4 F7 FF FF 0F B6 41 18 50 0F B6 41 15 50 68 ?? ?? ?? ?? 8D 45 80 6A 64 50 E8 ?? ?? ?? ??", -1),
        new("Repair eligibility", "83 79 20 00 75 ?? 8B 81 C0 01 00 00 8B 40 04 8B 40 28 24 09 3C 01 75 ?? B8 01 00 00 00 C3 33 C0 C3", -1),
        new("Equipment constructor vtable", "8D 46 2C C7 06 ?? ?? ?? ?? B9 10 00 00 00 0F 1F 00 33 D2 8D 40 02 66 89 50 FE 83 E9 01 75 ??", 5),
        new("Equipment repair price", "56 8B F1 0F B6 46 15 0F B6 56 18 2B D0 8B 46 20 66 0F 6E C8 F3 0F E6 C9 C1 E8 1F 66 0F 6E C2 F2 0F 58 0C C5 ?? ?? ?? ?? 0F 5B C0 66 0F 5A C9 F3 0F 59 05 ?? ?? ?? ?? F3 0F 59 C1 F3 0F 5E 05 ?? ?? ?? ?? F3 0F 58 05 ?? ?? ?? ?? E8 ?? ?? ?? ??", -1),
        new("Equipped location dispatch", "55 8B EC 66 8B 45 08 56 0F B7 F0 8B D6 83 E2 0F 4A 57 8B F9 83 FA 0D 0F 87 ?? ?? ?? ?? 0F B6 8A ?? ?? ?? ?? FF 24 8D ?? ?? ?? ??", 32),
        new("Equipped location branch", "55 8B EC 66 8B 45 08 56 0F B7 F0 8B D6 83 E2 0F 4A 57 8B F9 83 FA 0D 0F 87 ?? ?? ?? ?? 0F B6 8A ?? ?? ?? ?? FF 24 8D ?? ?? ?? ??", 39),
        new("Equipped location getter", "C1 EE 04 8B 84 B7 ?? ?? ?? ?? 5F 5E 5D C2 04 00 C1 EE 04 8B 84 B7 ?? ?? ?? ?? 5F 5E 5D C2 04 00", -1),
        new("Equipped location assignment", "C1 E9 04 83 7D 10 00 89 B4 8F ?? ?? ?? ?? 0F 84 ?? ?? ?? ?? 8B 86 C0 01 00 00", -1),
        new("Equipment packet fields", "0F B6 41 0C 89 02 8B 53 04 66 8B 41 08 66 3B 02 0F 85 ?? ?? ?? ?? F3 0F 7E 01 66 0F D6 43 08 8B 41 08 89 43 10 0F B7 41 0C 66 89 43 14 0F B7 43 12 66 89 43 16 0F B7 41 0E 66 C1 F8 0B 88 83 90 00 00 00 8A 41 10 C0 E0 03 C0 F8 03 02 42 18 88 83 92 00 00 00 8A 61 11 80 E4 0F 88 A3 91 00 00 00 8A 42 1A 02 41 12 88 43 18", -1)
    ];

    static readonly (string Name, int Offset, int Delta)[] durabilityMembers =
    [
        ("Repair equipped loop", 2, 0),
        ("Repair result restores maximum", 7, 0),
        ("Durability update fields", 17, 0),
        ("Equipped location getter", 6, -0x100),
        ("Equipped location getter", 22, -0xc0),
        ("Equipped location assignment", 10, -0x100)
    ];

    internal static DurabilityCodeLayout? DurabilityEvidence(string path, uint sceneRva)
        => DurabilityEvidence(File.ReadAllBytes(path), sceneRva);

    internal static DurabilityCodeLayout? DurabilityEvidence(byte[] bytes, uint sceneRva)
    {
        var image = new PeImage(bytes);
        var result = new List<SignatureEvidence>();
        foreach (var rule in durabilityRules)
        {
            var found = image.Find(new Pattern(rule.Pattern));
            if (found.Count != 1) return null;
            uint? captured = null;
            if (rule.CaptureOffset >= 0)
            {
                uint address = image.U32Rva(found[0] + (uint)rule.CaptureOffset);
                if (address < image.ImageBase || address - image.ImageBase >= image.ImageSize) return null;
                captured = address - image.ImageBase;
            }
            result.Add(new(rule.Name, found[0], rule.Pattern, rule.CaptureOffset, captured));
        }
        SignatureEvidence Proof(string name) => result.Single(e => e.Name == name);
        foreach (string name in new[] { "Repair scene", "Repair result scene", "Durability update scene" })
            if (Proof(name).CapturedRva != sceneRva) return null;
        bool Near(string a, string b, uint maximum) => Proof(b).CodeRva > Proof(a).CodeRva &&
            Proof(b).CodeRva - Proof(a).CodeRva <= maximum;
        if (!Near("Repair scene", "Repair equipped loop", 0x80) ||
            !Near("Repair equipped loop", "Repair equipped stride", 0x100) ||
            !Near("Repair result scene", "Repair result restores maximum", 0x100) ||
            !Near("Durability update scene", "Durability update fields", 0x100) ||
            !Near("Durability tooltip definition", "Durability tooltip values", 0x100)) return null;
        uint slots = image.U32Rva(Proof("Repair equipped loop").CodeRva + 2);
        if (slots is < 0x1000 or > 0x4000 || (slots & 3) != 0) return null;
        foreach (var member in durabilityMembers)
            if (image.U32Rva(Proof(member.Name).CodeRva + (uint)member.Offset) != slots + member.Delta)
                return null;
        uint vtable = Proof("Equipment constructor vtable").CapturedRva!.Value;
        var section = image.SectionAt(vtable, 0x14);
        if (section == null || section.Writable || section.Executable ||
            image.U32Rva(vtable + 0x10) != image.ImageBase + Proof("Equipment repair price").CodeRva)
            return null;
        // Location nibble 1 (the first switch case after decrement) selects the
        // same equipped array. Nibble 2 selects the bag lookup instead.
        uint dispatch = Proof("Equipped location dispatch").CapturedRva!.Value;
        uint branches = Proof("Equipped location branch").CapturedRva!.Value;
        if ((image.U32Rva(dispatch) & 0xff) != 0 ||
            image.U32Rva(branches) != image.ImageBase + Proof("Equipped location getter").CodeRva)
            return null;
        return new(Convert.ToHexStringLower(SHA256.HashData(bytes)), (int)slots, vtable, result.AsReadOnly());
    }

    internal static void ValidateLoadedDurability(DurabilityCodeLayout layout, long module,
        Func<uint, int, byte[]> readRva)
    {
        var code = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var proof in layout.Evidence)
        {
            byte[] bytes = readRva(proof.CodeRva, proof.Pattern.Split(' ').Length);
            if (!Matches(bytes, proof.Pattern))
                throw new InvalidOperationException("Loaded durability code differs: " + proof.Name);
            if (proof.CapturedRva.HasValue &&
                BitConverter.ToUInt32(bytes, proof.CaptureOffset) != (ulong)module + proof.CapturedRva.Value)
                throw new InvalidOperationException("Loaded durability address differs: " + proof.Name);
            code.Add(proof.Name, bytes);
        }
        foreach (var member in durabilityMembers)
            if (BitConverter.ToUInt32(code[member.Name], member.Offset) != layout.SlotsOffset + member.Delta)
                throw new InvalidOperationException("Loaded equipped array fields disagree");
        SignatureEvidence Proof(string name) => layout.Evidence.Single(e => e.Name == name);
        if (BitConverter.ToUInt32(readRva(layout.EquipmentVtableRva + 0x10, 4)) !=
            (ulong)module + Proof("Equipment repair price").CodeRva)
            throw new InvalidOperationException("Loaded equipment class has a different repair method");
        if (readRva(Proof("Equipped location dispatch").CapturedRva!.Value, 1)[0] != 0 ||
            BitConverter.ToUInt32(readRva(Proof("Equipped location branch").CapturedRva!.Value, 4)) !=
            (ulong)module + Proof("Equipped location getter").CodeRva)
            throw new InvalidOperationException("Loaded equipped location dispatch differs");
    }

    internal static void CheckDurabilityLayouts()
    {
        // Synthetic PE/code only: no installed executable or live process is used.
        byte[] fixture = new byte[0xc000];
        void U16(int at, ushort value) => BitConverter.TryWriteBytes(fixture.AsSpan(at), value);
        void U32(int at, uint value) => BitConverter.TryWriteBytes(fixture.AsSpan(at), value);
        U16(0, 0x5a4d); U32(0x3c, 0x80); U32(0x80, 0x4550); U16(0x84, 0x14c); U16(0x86, 3);
        U16(0x94, 0xe0); U16(0x98, 0x10b); U32(0xb4, 0x400000); U32(0xd0, 0xc000);
        void Section(int at, uint rva, uint size, uint flags)
        { U32(at + 8, size); U32(at + 12, rva); U32(at + 16, size); U32(at + 20, rva); U32(at + 36, flags); }
        Section(0x178, 0x1000, 0x7000, 0x60000020);
        Section(0x1a0, 0x9000, 0x1000, 0x40000040);
        Section(0x1c8, 0xa000, 0x2000, 0xc0000040);
        var at = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Repair scene"] = 0x1100, ["Repair equipped loop"] = 0x111f,
            ["Repair equipped stride"] = 0x118e, ["Repair result scene"] = 0x1300,
            ["Repair result restores maximum"] = 0x1347, ["Durability update scene"] = 0x1500,
            ["Durability update fields"] = 0x1546, ["Durability tooltip definition"] = 0x162b,
            ["Durability tooltip values"] = 0x1700,
            ["Repair eligibility"] = 0x1800, ["Equipment constructor vtable"] = 0x1900,
            ["Equipment repair price"] = 0x1a00, ["Equipped location dispatch"] = 0x1c00,
            ["Equipped location branch"] = 0x1c00, ["Equipped location getter"] = 0x1c40,
            ["Equipped location assignment"] = 0x1d00, ["Equipment packet fields"] = 0x1f00
        };
        foreach (var rule in durabilityRules.GroupBy(r => r.Pattern).Select(g => g.First()))
        {
            byte[] data = rule.Pattern.Split(' ').Select(s => s == "??" ? (byte)0 : Convert.ToByte(s, 16)).ToArray();
            data.CopyTo(fixture, at[rule.Name]);
        }
        foreach (var rule in durabilityRules.Where(r => r.CaptureOffset >= 0))
        {
            uint target = rule.Name switch
            {
                "Equipment constructor vtable" => 0x9000,
                "Equipped location dispatch" => 0x2200,
                "Equipped location branch" => 0x2240,
                _ => 0xa100
            };
            U32(at[rule.Name] + rule.CaptureOffset, 0x400000 + target);
        }
        void SetMembers(int slots)
        { foreach (var member in durabilityMembers) U32(at[member.Name] + member.Offset, (uint)(slots + member.Delta)); }
        SetMembers(0x14cc);
        U32(0x9010, 0x400000 + (uint)at["Equipment repair price"]);
        U32(0x2240, 0x400000 + (uint)at["Equipped location getter"]);
        var valid = DurabilityEvidence(fixture, 0xa100) ?? throw new Exception("Complete durability code proof was rejected");
        if (valid.SlotsOffset != 0x14cc || valid.Evidence.Count != durabilityRules.Length)
            throw new Exception("Durability catalog omitted a required consumer");
        void Reject(Action<byte[]> mutate, string label)
        {
            byte[] broken = (byte[])fixture.Clone(); mutate(broken);
            if (DurabilityEvidence(broken, 0xa100) != null) throw new Exception("Durability catalog accepted " + label);
        }
        Reject(b => b[at["Durability tooltip values"]] ^= 1, "missing display proof");
        Reject(b => Array.Copy(b, at["Repair eligibility"], b, 0x3100, 33), "ambiguous eligibility code");
        Reject(b => BitConverter.TryWriteBytes(b.AsSpan(at["Durability update fields"] + 17), 0x14d0), "conflicting equipped array");
        Reject(b => BitConverter.TryWriteBytes(b.AsSpan(at["Repair result scene"] + 2), 0x40a104u), "different scene roots");
        Reject(b => BitConverter.TryWriteBytes(b.AsSpan(0x9010), 0x401b00u), "different equipment virtual method");
        Reject(b => b[0x2200] = 1, "bag/equipped dispatch confusion");
        Reject(b => BitConverter.TryWriteBytes(b.AsSpan(0x2240), 0x401c50u), "wrong equipped branch");
        ValidateLoadedDurability(valid, 0x400000, (rva, count) => fixture.AsSpan((int)rva, count).ToArray());
        byte[] moved = (byte[])fixture.Clone();
        foreach (var proof in valid.Evidence.Where(e => e.CapturedRva.HasValue))
            BitConverter.TryWriteBytes(moved.AsSpan((int)proof.CodeRva + proof.CaptureOffset), 0x600000u + proof.CapturedRva!.Value);
        BitConverter.TryWriteBytes(moved.AsSpan(0x9010), 0x600000u + (uint)at["Equipment repair price"]);
        BitConverter.TryWriteBytes(moved.AsSpan(0x2240), 0x600000u + (uint)at["Equipped location getter"]);
        ValidateLoadedDurability(valid, 0x600000, (rva, count) => moved.AsSpan((int)rva, count).ToArray());
        bool mismatch = false;
        moved[at["Durability update fields"] + 17] ^= 4;
        try { ValidateLoadedDurability(valid, 0x600000, (rva, count) => moved.AsSpan((int)rva, count).ToArray()); }
        catch (InvalidOperationException) { mismatch = true; }
        if (!mismatch) throw new Exception("Loaded durability fields did not reject a mismatch");
        SetMembers(0x14a4);
        if (DurabilityEvidence(fixture, 0xa100)?.SlotsOffset != 0x14a4)
            throw new Exception("Durability discovery reused a fixed scene offset");
    }
}
