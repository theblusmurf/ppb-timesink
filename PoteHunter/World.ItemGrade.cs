using System.Buffers.Binary;
using PoteMemoryProbe;

namespace PoteHunter;

internal sealed record InventoryEntry(ulong ItemUid,ushort PrototypeId,ushort PackedPosition,bool Stackable,byte CountOrDurability,uint ItemAddress,uint BaseAddress);

/// <summary>The item whose tooltip the game is showing, read from client memory, with its tooltip stat values keyed by the grade table's labels.</summary>
/// <summary>Retry is set when the read failed transiently (tooltip changed mid-read, proof mismatch) rather than because the tooltip is not a gradeable item.</summary>
internal sealed record HoveredItemReading(bool Found, string Status, InventoryEntry? Entry, string Name,
    IReadOnlyDictionary<string, int> Stats, HoveredItemDetails? Details = null, bool Retry = false, int UpgradeLevel = 0,
    bool SuppressOverlay = false);

/// <summary>Raw facts behind a reading, for the probe and the trace log.</summary>
internal sealed record HoveredItemDetails(uint Helper, uint Wrapper, uint Base, uint Info, int GradeIndex, string GradeName,
    IReadOnlyList<int> SocketIds, IReadOnlyList<int> RawKinds, IReadOnlyList<int> DisplayedKinds, IReadOnlyDictionary<int, string> GameLabels,
    IReadOnlyDictionary<int, string> StatByKind, IReadOnlyList<string> LabelMismatches);

/// <summary>Raw bytes of one inventory item for offline layout mapping (wrapper, base record and prototype info).</summary>

/// <summary>The tooltip's stat arithmetic, kept pure so it can be checked offline.</summary>
internal static class ItemStatKinds
{
    /// <summary>Stat kind → bundled grade table label, following the client's kind → label routine (proof "stat label table").</summary>
    internal static readonly IReadOnlyDictionary<int, string> Labels = new Dictionary<int, string>
    {
        [1] = "MIN", [2] = "MAX", [3] = "DEF", [4] = "ACC", [5] = "EVAS", [6] = "MAX HP", [7] = "HP REG", [8] = "MAX MP",
        [9] = "MP REG", [10] = "CRI", [11] = "BLOCK", [13] = "MAGIC", [14] = "MR"
    };
    internal static readonly string[] GradeNames = ["Divine", "Epic", "S", "AAA", "AA", "A", "B", "C"];
    internal static string GradeName(int index) => index >= 0 && index < GradeNames.Length ? GradeNames[index] : "grade " + index;
    /// <summary>The tooltip prints kinds 6 (max health) and 8 (max mana) ten times larger than stored (proof "tooltip stat scale").</summary>
    internal static int DisplayScale(int kind) => kind is 6 or 8 ? 10 : 1;

    /// <summary>
    /// Reproduces the tooltip: raw[kind] = word at base+StatsOffset+kind*2; each socketed gem's bonus word (gem record+GemBonusOffset+kind*2)
    /// is subtracted in 16 bits, the result is sign-extended, and kinds 6 and 8 are scaled for display.
    /// </summary>
    internal static (int[] Raw, int[] Displayed) Decode(ReadOnlySpan<byte> baseBytes, ItemGradeLayout layout, IReadOnlyList<byte[]> gemRecords)
    {
        int[] raw = new int[layout.StatCount], displayed = new int[layout.StatCount];
        for (int kind = 0; kind < layout.StatCount; kind++)
        {
            ushort word = BinaryPrimitives.ReadUInt16LittleEndian(baseBytes.Slice(layout.StatsOffset + kind * 2, 2));
            raw[kind] = word;
            foreach (var gem in gemRecords)
                word = unchecked((ushort)(word - BinaryPrimitives.ReadUInt16LittleEndian(gem.AsSpan(layout.GemBonusOffset + kind * 2, 2))));
            displayed[kind] = unchecked((short)word) * DisplayScale(kind);
        }
        return (raw, displayed);
    }

    /// <summary>Grade-table labels for the kinds the tooltip would print (non-zero after gem subtraction, like the tooltip).</summary>
    internal static IReadOnlyDictionary<string, int> ToStats(int[] displayed) => ToStats(displayed, _ => null, out _, out _);

    /// <summary>
    /// Same, but the game's own label text for a kind (read from its string table) decides the stat when it is recognisable, so a
    /// kind the fixed map has wrong still lands on the right grade-table row. Disagreements are reported, not hidden.
    /// </summary>
    internal static IReadOnlyDictionary<string, int> ToStats(int[] displayed, Func<int, string?> gameLabel,
        out IReadOnlyDictionary<int, string> statByKind, out IReadOnlyList<string> mismatches)
    {
        var stats = new Dictionary<string, int>(StringComparer.Ordinal);
        var chosen = new Dictionary<int, string>();
        var notes = new List<string>();
        for (int kind = 0; kind < displayed.Length; kind++)
        {
            if (displayed[kind] == 0) continue;
            string? label = gameLabel(kind);
            string? fromGame = LabelToStat(label), fromMap = Labels.GetValueOrDefault(kind);
            string? stat = fromGame ?? fromMap;
            if (stat == null) continue;
            if (fromGame != null && fromMap != null && fromGame != fromMap)
                notes.Add($"kind {kind}: game label \"{label}\" means {fromGame}, fixed map said {fromMap}; using the game label");
            if (stats.ContainsKey(stat)) { notes.Add($"kind {kind}: {stat} already taken, ignored"); continue; }
            stats[stat] = displayed[kind];
            chosen[kind] = stat;
        }
        statByKind = chosen; mismatches = notes;
        return stats;
    }

    /// <summary>Tooltip label text → grade-table stat label (the same words James's script matched on screen).</summary>
    internal static string? LabelToStat(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        string text = label.Trim().TrimEnd(':').ToLowerInvariant();
        return text switch
        {
            "defense" or "defence" => "DEF", "accuracy" => "ACC", "evasion" => "EVAS",
            "max health" or "maximum health" or "health" or "max hp" or "hp" => "MAX HP",
            "health regen" or "health regeneration" or "hp regen" => "HP REG",
            "max mana" or "maximum mana" or "mana" or "max mp" or "mp" => "MAX MP",
            "mana regen" or "mana regeneration" or "mp regen" => "MP REG",
            "critical" or "critical hit" or "crit" => "CRI", "block" => "BLOCK",
            "magic resist" or "magic resistance" or "magic res" => "MR",
            "magic" or "magic power" or "magic damage" => "MAGIC",
            "min damage" or "minimum damage" or "damage min" or "min dmg" => "MIN",
            "max damage" or "maximum damage" or "damage max" or "max dmg" => "MAX",
            _ => null
        };
    }

    internal static void SelfTest()
    {
        var layout = new ItemGradeLayout(0x400000, 0, 0, 0, Array.Empty<ItemGradeCodeProof>());
        byte[] baseBytes = new byte[layout.BaseLength];
        void Word(int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(baseBytes.AsSpan(offset, 2), unchecked((ushort)value));
        Word(layout.StatsOffset + 3 * 2, 120);   // DEF 120 incl. a +20 gem
        Word(layout.StatsOffset + 6 * 2, 250);   // MAX HP stored 250, shown 2500
        Word(layout.StatsOffset + 14 * 2, 7);    // MR 7
        Word(layout.StatsOffset + 1 * 2, 3);     // MIN 3 with a +5 gem: the tooltip wraps to -2
        byte[] gem = new byte[0x26c];
        BinaryPrimitives.WriteUInt16LittleEndian(gem.AsSpan(layout.GemBonusOffset + 3 * 2, 2), 20);
        BinaryPrimitives.WriteUInt16LittleEndian(gem.AsSpan(layout.GemBonusOffset + 1 * 2, 2), 5);
        var (raw, displayed) = Decode(baseBytes, layout, [gem]);
        if (raw[3] != 120 || displayed[3] != 100 || raw[6] != 250 || displayed[6] != 2500 || displayed[14] != 7 || displayed[1] != -2)
            throw new Exception("Tooltip stat arithmetic (gem subtraction, sign extension, x10 kinds) is wrong.");
        var stats = ToStats(displayed);
        if (stats["DEF"] != 100 || stats["MAX HP"] != 2500 || stats["MR"] != 7 || stats["MIN"] != -2 || stats.ContainsKey("MAX") || stats.Count != 4)
            throw new Exception("Stat kinds were mapped to the wrong grade table labels.");
        if (GradeName(3) != "AAA" || GradeName(2) != "S" || GradeName(7) != "C" || GradeName(9) != "grade 9")
            throw new Exception("Item grade index names are wrong.");
        var none = Decode(baseBytes, layout, []);
        if (none.Displayed[3] != 120 || none.Displayed[1] != 3) throw new Exception("An item without gems must show its raw words.");
        if (LabelToStat("Max Health") != "MAX HP" || LabelToStat("Magic Resist:") != "MR" || LabelToStat("Min Damage") != "MIN" || LabelToStat("Durability") != null)
            throw new Exception("Tooltip label text was not mapped to grade table stats.");
        // The game's label wins over the fixed map, and the disagreement is reported.
        var swapped = ToStats(displayed, kind => kind == 6 ? "Max Mana" : kind == 3 ? "Defense" : null, out var byKind, out var notes);
        if (swapped["MAX MP"] != 2500 || swapped.ContainsKey("MAX HP") || byKind[6] != "MAX MP" || byKind[3] != "DEF" || notes.Count != 1 || !notes[0].Contains("kind 6"))
            throw new Exception("The game's own stat label did not override the fixed kind map: " + string.Join("; ", notes));
    }
}

public sealed partial class World
{
    internal const int ItemBaseDumpLength = 0x110, ItemInfoDumpLength = 0x60, ItemDefinitionLength = 0x26c;
    ItemGradeLayout? itemGradeLayout;
    string itemGradeStatus = "Item tooltip state has not been discovered.";

    void ConfigureItemGradeFromClient()
    {
        try{ConfigureItemGrade(File.ReadAllBytes(PoteMemoryProbe.Program.ClientPath));}
        catch(Exception ex)when(ex is not OutOfMemoryException and not AccessViolationException){ResetItemGrade();itemGradeStatus="Item grades unavailable: "+ex.Message;}
    }
    internal void ConfigureItemGrade(byte[] clientImage, CancellationToken cancellationToken = default)
    {
        itemGradeLayout = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ConnectionVerified || handle is null || !IsProcessAlive(handle))
                throw new InvalidOperationException("A verified connected client is required.");
            var layout = ItemGradeDiscovery.Resolve(clientImage, cancellationToken);
            if (!ItemGradeDiscovery.LoadedProofsMatch(layout, checked((uint)moduleBase),
                (rva, count) => Native.Read(handle, (nint)(moduleBase + rva), count), out string reason))
                throw new InvalidDataException(reason);
            itemGradeLayout = layout;
            itemGradeStatus = "Item tooltip selection, stat value and gem instructions verified.";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is not OutOfMemoryException and not AccessViolationException)
        { itemGradeStatus = "Item grades unavailable: " + ex.Message; }
    }

    /// <summary>
    /// The tooltip helper the game selected this frame (0 when no tooltip is open or item grades are unavailable). The game clears
    /// the pointer at the end of every UI frame and sets it again in the next frame's hit test, so one read can land in the cleared
    /// gap; the read is repeated a few times a few milliseconds apart and the first non-zero value wins. Cheap 4-byte reads; safe to poll.
    /// </summary>
    internal uint HoveredTooltipHandle()
    {
        try
        {
            if (!ConnectionVerified || handle is null || itemGradeLayout is not { } layout || !IsProcessAlive(handle)) return 0;
            return SampleHoveredTooltip(layout);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not AccessViolationException) { return 0; }
    }

    /// <summary>The hovered tooltip global, sampled across more than one frame so the per-frame cleared gap reads as "no tooltip" only when no tooltip is open.</summary>
    uint SampleHoveredTooltip(ItemGradeLayout layout)
    {
        for (int attempt = 0; ; attempt++)
        {
            uint helper = Pointer(moduleBase + layout.HoveredTooltipRva);
            if (helper != 0 || attempt == HoveredTooltipSamples - 1) return helper;
            Thread.Sleep(HoveredTooltipSampleGapMilliseconds);
        }
    }

    /// <summary>Samples per poll and their spacing: together they span more than one 60 Hz frame, so a cleared gap cannot hide a live tooltip.</summary>
    internal const int HoveredTooltipSamples = 5, HoveredTooltipSampleGapMilliseconds = 5;

    internal void ResetItemGrade()
    { itemGradeLayout = null; itemGradeStatus = "Item grades are disconnected."; }

    /// <summary>
    /// Reads the item whose tooltip the game is showing right now. The game's own inventory hit test stores that item's tooltip helper
    /// in one global every frame (proof "hovered tooltip hit test"), so this follows the same pointer instead of guessing from the cursor.
    /// The stat values are computed exactly as the tooltip computes them: stored words minus socketed gem bonuses, kinds 6 and 8 x10.
    /// </summary>
    internal HoveredItemReading ItemUnderCursor(Point cursorScreen)
    {
        var empty = new Dictionary<string, int>();
        try
        {
            if (!ConnectionVerified || handle is null || !IsProcessAlive(handle) || itemGradeLayout is not { } layout)
                throw new InvalidOperationException(itemGradeStatus);
            if (!ItemGradeDiscovery.LoadedProofsMatch(layout, checked((uint)moduleBase),
                (rva, count) => Native.Read(handle, (nint)(moduleBase + rva), count), out string reason))
            { itemGradeLayout = null; throw new InvalidDataException(reason); }
            byte[] Read(uint address, int length)
            {
                DurabilitySnapshot.RequirePointer(address, length);
                byte[] bytes = Native.Read(handle, (nint)address, length);
                if (bytes.Length != length) throw new InvalidDataException("Item read was incomplete.");
                return bytes;
            }
            static uint U32(byte[] bytes, int offset = 0) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
            static ushort U16(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));

            uint helper = SampleHoveredTooltip(layout);
            if (helper == 0)
                return new(false, "No item tooltip is open. Hover an item in the game until its tooltip shows, then try again.", null, "", empty);
            byte[] helperBytes = Read(helper, layout.HelperItemOffset + 4);
            if (U32(helperBytes) != checked((uint)moduleBase + layout.HelperVtableRva))
                return new(false, "The tooltip that is open is not an item tooltip. Hover an item in a bag, your equipment or a shop.", null, "", empty);
            uint wrapper = U32(helperBytes, layout.HelperItemOffset);
            if (U32(Read(checked(wrapper + (uint)layout.ItemHelperOffset), 4)) != helper)
                throw new InvalidDataException("The tooltip and its item do not point at each other.");
            byte[] identity = Read(checked(wrapper + (uint)layout.ItemUidOffset), 12);
            ulong uid = BinaryPrimitives.ReadUInt64LittleEndian(identity);
            uint itemBase = U32(identity, 8);
            if (layout.ItemBaseOffset != layout.ItemUidOffset + 8 || uid == 0) throw new InvalidDataException("The hovered item's identity is invalid.");
            byte[] baseBytes = Read(itemBase, layout.BaseLength);
            if (BinaryPrimitives.ReadUInt64LittleEndian(baseBytes.AsSpan(layout.BaseUidOffset, 8)) != uid)
                throw new InvalidDataException("The hovered item's wrapper and base record disagree.");
            ushort prototype = U16(baseBytes, layout.PrototypeOffset);
            ushort position = U16(baseBytes, layout.PositionOffset);
            byte count = baseBytes[layout.CountOffset];
            uint info = U32(baseBytes, layout.InfoPointerOffset);
            byte[] infoRecord = Read(info, ItemDefinitionLength);
            byte[] definition = ItemRecord(prototype);
            if (prototype == 0 || definition.Length != ItemDefinitionLength || !infoRecord.AsSpan().SequenceEqual(definition))
                throw new InvalidDataException("The hovered item's prototype record does not match the item definition table.");
            uint flags = U32(infoRecord, layout.InfoFlagsOffset);
            var details = DescribeItem(prototype);
            string name = string.IsNullOrWhiteSpace(details.Name) ? "item " + prototype : details.Name;
            var entry = new InventoryEntry(uid, prototype, position, (flags & 8) != 0, count, wrapper, itemBase);
            var gradeProfile = ItemGradeTable.Find(prototype);
            if (ItemGradeEligibility.IsJewelry(details.Category, gradeProfile?.Type, gradeProfile?.Category))
                return new(false, "Jewelry is excluded from item grade calculations.", entry, name, empty, SuppressOverlay: true);
            if ((flags & 1) == 0)
                return new(false, name + " is not equipment, so it has no stat grades.", entry, name, empty);
            var sockets = new List<int>();
            var gems = new List<byte[]>();
            for (int socket = 0; socket < layout.SocketCount; socket++)
            {
                int gemId = U16(baseBytes, layout.SocketsOffset + socket * 2);
                if (gemId == 0) continue;
                byte[] gem = ItemRecord(gemId);
                if (gem.Length != ItemDefinitionLength) throw new InvalidDataException("A socketed gem (" + gemId + ") is missing from the item definition table.");
                sockets.Add(gemId);
                gems.Add(gem);
            }
            // Stable reread: the game rewrites the selection every frame (and clears it at each frame start, hence the sampled read), so
            // the same item must still be selected and unchanged.
            if (SampleHoveredTooltip(layout) != helper || !Read(helper, helperBytes.Length).AsSpan().SequenceEqual(helperBytes) ||
                !Read(itemBase, layout.BaseLength).AsSpan().SequenceEqual(baseBytes))
                throw new InvalidDataException("The hovered item changed during the read; keep the mouse still and try again.");
            var (raw, displayed) = ItemStatKinds.Decode(baseBytes, layout, gems);
            int gradeIndex = checked((int)U32(baseBytes, layout.GradeOffset));
            var labels = new Dictionary<int, string>();
            for (int kind = 0; kind < displayed.Length; kind++)
                if (displayed[kind] != 0 && GameStatLabel(layout, kind) is { Length: > 0 } label) labels[kind] = label;
            var stats = ItemStatKinds.ToStats(displayed, kind => labels.GetValueOrDefault(kind), out var statByKind, out var mismatches);
            var result = new HoveredItemDetails(helper, wrapper, itemBase, info, gradeIndex, ItemStatKinds.GradeName(gradeIndex), sockets, raw, displayed, labels, statByKind, mismatches);
            itemGradeStatus = "Read " + name + " from the open tooltip." + (mismatches.Count > 0 ? " Label notes: " + string.Join("; ", mismatches) : "");
            return new(true, itemGradeStatus, entry, name, stats, result, UpgradeLevel: baseBytes[layout.UpgradeLevelOffset]);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not AccessViolationException)
        {
            itemGradeStatus = "Item grade read failed: " + ex.Message;
            return new(false, itemGradeStatus, null, "", empty, null, Retry: true);
        }
    }

    // The grade panel's label index per kind (proof "stat label table"): kind → string index → labels + index*0x400. Diagnostic only.
    static readonly IReadOnlyDictionary<int, int> StatLabelIndex = new Dictionary<int, int>
    {
        [1] = 0x8F1, [2] = 0x8F2, [3] = 0x790, [4] = 0x791, [5] = 0x792, [6] = 0x8F3, [7] = 0x8F4, [8] = 0x8F5, [9] = 0x8F6, [10] = 0x795,
        [11] = 0x794, [13] = 0x793, [14] = 0x796
    };

    string? GameStatLabel(ItemGradeLayout layout, int kind)
    {
        if (!StatLabelIndex.TryGetValue(kind, out int index) || handle is null) return null;
        try
        {
            byte[] bytes = Native.Read(handle, (nint)(moduleBase + layout.StatLabelsRva + index * 0x400L), 64);
            int end = Array.IndexOf(bytes, (byte)0); if (end < 0) end = bytes.Length;
            string text = System.Text.Encoding.UTF8.GetString(bytes, 0, end);
            return text.Any(c => char.IsControl(c) || c == '�') ? null : text;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not AccessViolationException) { return null; }
    }

}
