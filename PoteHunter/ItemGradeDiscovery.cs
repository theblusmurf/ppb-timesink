using System.Buffers.Binary;
using System.Reflection.PortableExecutable;

namespace PoteHunter;

internal sealed record ItemGradeCodeProof(string Name,uint Rva,byte[] FileBytes,int[] RelocatedOperands);

/// <summary>
/// Where the client keeps the item whose tooltip is showing, and how the tooltip computes the stat values it prints.
/// Every offset is read back out of the proved instructions; nothing here is a remembered constant.
/// </summary>
internal sealed record ItemGradeLayout(uint FileImageBase, uint HoveredTooltipRva, uint HelperVtableRva, uint StatLabelsRva,
    IReadOnlyList<ItemGradeCodeProof> Proofs)
{
    /// <summary>Tooltip helper → the item wrapper it describes ([helper+0x20]).</summary>
    internal int HelperItemOffset { get; init; } = 0x20;
    /// <summary>Item wrapper → its tooltip helper ([wrapper+0x1CC]); the two must point at each other.</summary>
    internal int ItemHelperOffset { get; init; } = 0x1CC;
    internal int ItemUidOffset { get; init; } = 0x1B8;
    internal int ItemBaseOffset { get; init; } = 0x1C0;
    internal int BaseUidOffset { get; init; } = 8;
    internal int PrototypeOffset { get; init; } = 0x10;
    internal int PositionOffset { get; init; } = 0x12;
    internal int CountOffset { get; init; } = 0x15;
    internal int InfoPointerOffset { get; init; } = 4;
    internal int InfoFlagsOffset { get; init; } = 0x28;
    /// <summary>Two socket words (gem prototype ids) directly before the stat words.</summary>
    internal int SocketsOffset { get; init; } = 0x5A;
    internal int SocketCount { get; init; } = 2;
    /// <summary>Per-kind stat words; the tooltip copies <see cref="StatCount"/> of them starting at kind 0.</summary>
    internal int StatsOffset { get; init; } = 0x5E;
    internal int StatCount { get; init; } = 25;
    /// <summary>Gem bonus words inside the gem's item definition record, one per stat kind.</summary>
    internal int GemBonusOffset { get; init; } = 0x21C;
    /// <summary>Overall grade index on the base record: 0 Divine, 1 Epic, 2 S, 3 AAA, 4 AA, 5 A, 6 B, 7 C.</summary>
    internal int GradeOffset { get; init; } = 0x98;
    /// <summary>Upgrade level byte (+0..+10) on the base record; the tooltip prints it as its last line when non-zero.</summary>
    internal int UpgradeLevelOffset { get; init; } = 0x90;
    internal int BaseLength => GradeOffset + 4;
}

// Deliberately narrow instruction proofs in the style of InventoryDiscovery, found by pattern rather than fixed address so a client
// patch that only shifts code (the common case) still resolves. Hash is audit identity only.
internal static class ItemGradeDiscovery
{
    internal const string SupportedSha256 = "F3A39EA1F3687ACA418ABA17DF295043618014B681ECA4FF1DB33D4837943C90";

    static readonly ProofPattern[] Evidence =
    [
        // Inventory slot hit test: cursor inside the slot rectangle selects the item's tooltip helper into the hovered-tooltip global.
        new("hovered tooltip hit test", "83BFA8010000 00 7462 8B9FCC010000 85DB 7458 8B97C4010000 85D2 744E 0FB74F14 8B35???????? 8BC1 3BC6 7F3E 668B4210 662B4208 6603C1 0FB7C0 3BF0 7F2C 0FB74F16 8B35???????? 8BC1 3BC6 7F1C 662B4A0C 66034A14 8B15???????? 0FB7C9 3BF1 0F4ED3 8915????????", [35, 69, 89, 103]),
        // UI frame end: the selected helper's third virtual slot draws the tooltip.
        new("hovered tooltip draw", "8B0D???????? 85C9 740D 83790800 7407 8B01 FF500C", [2]),
        // UI frame start: the global is cleared before the windows are hit-tested again.
        new("hovered tooltip reset", "C705???????? 00000000 8DB1E4020000 BB10000000", [2]),
        // Tooltip helper constructor stores the vtable the hovered helper must carry.
        new("tooltip helper constructor", "558BEC51568BF18975FC E8???????? C706???????? 8BC6 C7462000000000 C7462400000000 C7462800000000 5E8BE55DC3", [17]),
        // Tooltip builder: helper+0x20 → wrapper, wrapper+0x1C0 → base, base+4 → info, info+0x28 bit0 = equipment, base+0x5A = sockets.
        new("tooltip item resolve", "8B4620 8B88C0010000 85C9 0F84???????? 8B4104 BF00000000 F6402801 0F45F9 89BD???????? 85FF 0F84???????? 0FB7475A 8BCF 8985????????", []),
        // Stat buffer: 25 words from base+0x5E, minus each socketed gem's bonus words (gem record+0x21C).
        new("tooltip stat values", "558BEC 0F10415E 53 56 8B7508 BB02000000 57 8D795A 0F1106 0F10416E 0F114610 0F10417E 0F114620 668B818E000000 66894630 0FB707 6685C0 7439 50 E8???????? 8BC8 E8???????? 85C0 7428 8BCE 8D901C020000 BE19000000 0F1F8000000000 668B02 8D5202 662901 8D4902 83EE01 75EF 8B7508 83C702 83EB01 75B7 5F5E5B5DC20400CC", []),
        // Kinds 6 and 8 are printed ten times larger.
        new("tooltip stat scale", "83F806 740E 83F808 7409 8B85???????? 98 EB0C 8B85???????? 98 8D0480 03C0", []),
        // Gem lookup loop: two socket words before the stat words.
        new("gem bonus lookup", "8B450C 53 57 BB02000000 0FB744465E 83C65A", []),
        // Item grade index at base+0x98.
        new("tooltip item grade", "8B4620 8B88C0010000 85C9 7419 8B4104 F6402801 B800000000 0F45C1 85C0 7406 8B9098000000", []),
        // Tooltip text: after the stat lines, a non-zero upgrade level byte adds the "Upgrade Level" line.
        new("tooltip upgrade level", "8BB5???????? 80BE9000000000 7642 66FF03 0FB68690000000 50 68???????? 8D85???????? 6804010000 50 E8????????", [27]),
        // Grade panel: stat kind → string index, then string table base + index*0x400.
        new("stat label table", "56 0FB647F9 50 E8???????? 83C404 85C0 780A C1E00A 05????????", [22]),
    ];

    internal static ItemGradeLayout Resolve(byte[] image, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var h = pe.PEHeaders;
        if (h.CoffHeader.Machine != System.Reflection.PortableExecutable.Machine.I386 || h.PEHeader?.Magic != PEMagic.PE32)
            throw new InvalidDataException("Item grade proof requires PE32 x86.");
        byte[] At(uint rva, int length)
        {
            foreach (var section in h.SectionHeaders)
                if (rva >= section.VirtualAddress && (ulong)rva + (uint)length <= (ulong)section.VirtualAddress + (uint)section.SizeOfRawData)
                    return image.AsSpan(checked(section.PointerToRawData + (int)rva - section.VirtualAddress), length).ToArray();
            throw new InvalidDataException("Item grade proof is outside file-backed image data.");
        }
        // Each pattern must occur exactly once in executable code; its address is wherever this build put it.
        var found = new Dictionary<string, uint>();
        var proofs = new List<ItemGradeCodeProof>();
        foreach (var pattern in Evidence)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (bytes, mask) = pattern.Parse();
            uint? rva = null; int matches = 0;
            foreach (var section in h.SectionHeaders.Where(s => (s.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0))
            {
                var code = image.AsSpan(section.PointerToRawData, section.SizeOfRawData);
                for (int start = 0; start <= code.Length - bytes.Length; start++)
                {
                    if (code[start] != bytes[0]) continue;
                    bool match = true;
                    for (int i = 1; i < bytes.Length && match; i++) match = mask[i] || code[start + i] == bytes[i];
                    if (!match) continue;
                    matches++;
                    rva ??= checked((uint)section.VirtualAddress + (uint)start);
                }
            }
            if (matches != 1 || rva is not uint at) throw new InvalidDataException(pattern.Name + (matches == 0 ? " no longer matches the inspected instructions." : " is not unique in executable sections."));
            found[pattern.Name] = at;
            proofs.Add(new ItemGradeCodeProof(pattern.Name, at, At(at, bytes.Length), pattern.RelocatedOperands.ToArray()));
        }
        uint R(string name) => found[name];
        uint imageBase = checked((uint)h.PEHeader.ImageBase);
        uint Address(uint rva) => BinaryPrimitives.ReadUInt32LittleEndian(At(rva, 4));
        int Member(uint rva) => checked((int)Address(rva));
        byte Small(uint rva) => At(rva, 1)[0];
        uint hitTest = R("hovered tooltip hit test"), resolveItem = R("tooltip item resolve"), statValues = R("tooltip stat values"),
            gradeRead = R("tooltip item grade"), gemLookup = R("gem bonus lookup"), upgradeLine = R("tooltip upgrade level");
        uint hovered = Address(hitTest + 89);
        if (hovered != Address(hitTest + 103) || hovered != Address(R("hovered tooltip draw") + 2) || hovered != Address(R("hovered tooltip reset") + 2))
            throw new InvalidDataException("The hovered-tooltip global disagrees between its hit test, draw and reset.");
        uint helperVtable = Address(R("tooltip helper constructor") + 17);
        uint labels = Address(R("stat label table") + 22);
        int itemHelper = Member(hitTest + 11);             // mov ebx,[edi+0x1CC]
        int helperItem = Small(resolveItem + 2);           // mov eax,[esi+0x20]
        int itemBase = Member(resolveItem + 5);            // mov ecx,[eax+0x1C0]
        int infoPointer = Small(resolveItem + 19);         // mov eax,[ecx+4]
        int infoFlags = Small(resolveItem + 27);           // test byte [eax+0x28],1
        int sockets = Small(resolveItem + 49);             // movzx eax,word [edi+0x5A]
        int stats = Small(statValues + 6);                 // movups xmm0,[ecx+0x5E]
        int statCount = Member(statValues + 0x55);         // mov esi,0x19
        int gemBonus = Member(statValues + 0x50);          // lea edx,[eax+0x21C]
        int grade = Member(gradeRead + 0x22);              // mov edx,[eax+0x98]
        int upgradeLevel = Member(upgradeLine + 8);        // cmp byte [esi+0x90],0
        if (upgradeLevel != Member(upgradeLine + 21) || upgradeLevel >= grade) throw new InvalidDataException("The upgrade level operands disagree.");
        if (itemBase != Member(gradeRead + 5) || infoPointer != Small(gradeRead + 15) || infoFlags != Small(gradeRead + 18) ||
            helperItem != Small(gradeRead + 2) || stats != Small(gemLookup + 14) || sockets != Small(gemLookup + 0x11) ||
            sockets != Small(statValues + 0x14) || statCount != 25 || stats != sockets + 4 || Small(statValues + 27) != stats + 0x10 ||
            Small(statValues + 35) != stats + 0x20 || Member(statValues + 43) != stats + 0x30)
            throw new InvalidDataException("Independent item grade layout operands disagree.");
        bool Writable(uint address) => h.SectionHeaders.Any(section => (section.SectionCharacteristics & SectionCharacteristics.MemWrite) != 0 &&
            address - imageBase >= section.VirtualAddress && (ulong)(address - imageBase) + 4 <= (ulong)section.VirtualAddress + (uint)section.VirtualSize);
        if (hovered < imageBase || !Writable(hovered) || labels < imageBase || !Writable(labels))
            throw new InvalidDataException("The hovered-tooltip global or label table is not writable client data.");
        bool ReadOnlyData(uint address) => h.SectionHeaders.Any(section => (section.SectionCharacteristics & SectionCharacteristics.MemWrite) == 0 &&
            (section.SectionCharacteristics & SectionCharacteristics.MemRead) != 0 &&
            address - imageBase >= section.VirtualAddress && (ulong)(address - imageBase) + 4 <= (ulong)section.VirtualAddress + (uint)section.VirtualSize);
        if (helperVtable < imageBase || !ReadOnlyData(helperVtable))
            throw new InvalidDataException("The tooltip helper vtable is not read-only client data.");
        return new(imageBase, checked(hovered - imageBase), checked(helperVtable - imageBase), checked(labels - imageBase), proofs)
        {
            ItemHelperOffset = itemHelper, HelperItemOffset = helperItem, ItemBaseOffset = itemBase, InfoPointerOffset = infoPointer,
            InfoFlagsOffset = infoFlags, SocketsOffset = sockets, StatsOffset = stats, StatCount = statCount, GemBonusOffset = gemBonus,
            GradeOffset = grade, UpgradeLevelOffset = upgradeLevel
        };
    }

    /// <summary>Every pattern parses, and every operand Resolve reads lies inside its pattern, so a typo cannot read past a match.</summary>
    internal static void SelfTest()
    {
        var lengths = new Dictionary<string, int>();
        foreach (var pattern in Evidence)
        {
            var (bytes, mask) = pattern.Parse();
            if (bytes.Length < 8 || mask[0]) throw new Exception(pattern.Name + " must start with a fixed byte and be long enough to be unique.");
            foreach (int offset in pattern.RelocatedOperands) if (offset + 4 > bytes.Length) throw new Exception(pattern.Name + " relocation lies outside the pattern.");
            if (lengths.ContainsKey(pattern.Name)) throw new Exception("Duplicate proof name " + pattern.Name);
            lengths[pattern.Name] = bytes.Length;
        }
        (string Name, int Offset, int Size)[] reads =
        [
            ("hovered tooltip hit test", 89, 4), ("hovered tooltip hit test", 103, 4), ("hovered tooltip hit test", 11, 4), ("hovered tooltip draw", 2, 4),
            ("hovered tooltip reset", 2, 4), ("tooltip helper constructor", 17, 4), ("stat label table", 22, 4), ("tooltip item resolve", 49, 1),
            ("tooltip stat values", 0x55, 4), ("tooltip stat values", 0x50, 4), ("tooltip stat values", 43, 4), ("tooltip item grade", 0x22, 4),
            ("tooltip upgrade level", 21, 4), ("gem bonus lookup", 0x11, 1),
        ];
        foreach (var (name, offset, size) in reads)
            if (!lengths.TryGetValue(name, out int length) || offset + size > length) throw new Exception($"{name}+{offset} is read outside the proved bytes.");
        if (Evidence.Length != 11) throw new Exception("Unexpected number of item grade proofs: " + Evidence.Length);
    }

    // read receives an RVA, matching InventoryDiscovery.LoadedProofsMatch.
    internal static bool LoadedProofsMatch(ItemGradeLayout layout, uint moduleBase, Func<uint, int, byte[]> read, out string reason)
    {
        foreach (var proof in layout.Proofs)
        {
            byte[] expected = proof.FileBytes.ToArray();
            foreach (int offset in proof.RelocatedOperands)
            {
                uint original = BinaryPrimitives.ReadUInt32LittleEndian(expected.AsSpan(offset, 4));
                if (original < layout.FileImageBase || (ulong)moduleBase + original - layout.FileImageBase > uint.MaxValue)
                { reason = proof.Name + " has an invalid relocation."; return false; }
                BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(offset, 4), checked(moduleBase + original - layout.FileImageBase));
            }
            if (!read(proof.Rva, expected.Length).AsSpan().SequenceEqual(expected))
            { reason = proof.Name + " differs in the loaded client."; return false; }
        }
        reason = "Loaded tooltip selection, helper, stat value, gem and grade code match static proof.";
        return true;
    }
}
