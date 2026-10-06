using PoteMemoryProbe;

namespace PoteHunter;

public readonly record struct ManaReading(bool Known, int Current, uint Maximum)
{
    public double Percent => Known && Maximum > 0 ? Math.Clamp(Current * 100.0 / Maximum, 0, 100) : 0;
}

public sealed partial class World
{
    const string ManaPattern = "F3 0F 7E ?? 30 66 0F D6 42 44 8B ?? 38 89 42 4C 66 8B ?? 3C 66 89 42 50";
    // Current client packet-copy routine: copies CurrHP/CurrMP as one qword to +44/+48,
    // then MaxHP to +4c and MaxMP to +50. Wildcards cover the source base register.
    static readonly int ManaCodeLength = ManaPattern.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    int manaCurrentOffset = -1;
    int manaMaximumOffset = -1;
    public string ManaStatus { get; private set; } = "Mana layout has not been checked";

    public void ConfigureMana()
    {
        ResetMana();
        if (handle is null || handle.IsInvalid || moduleBase == 0) { ManaStatus = "Connect to a validated client before checking mana"; return; }
        try
        {
            var evidence = ProfileDiscovery.OptionalEvidence(PoteMemoryProbe.Program.ClientPath, "Current/maximum mana fields", ManaPattern);
            if (evidence is null) { ManaStatus = "Mana code signature was missing or ambiguous"; return; }
            var live = Native.Read(handle!, (nint)(moduleBase + evidence.CodeRva), ManaCodeLength);
            if (!ProfileDiscovery.Matches(live, evidence.Pattern)) { ManaStatus = "Loaded mana code does not match the client file"; return; }
            ConfigureMana(0x48, 0x50);
            ManaStatus = $"Mana layout validated at code RVA 0x{evidence.CodeRva:X}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ManaStatus = "Mana layout unavailable: " + ex.Message;
        }
    }

    /// <summary>
    /// Enables mana reads after the caller has validated the two fields for the connected build.
    /// The current source layout places them at UID-data +0x48 and +0x50, beside the
    /// already validated HP fields at +0x44 and +0x4c.
    /// </summary>
    public void ConfigureMana(int currentOffset, int maximumOffset)
    {
        if (currentOffset < 0 || maximumOffset < 0 || currentOffset > 0x1000 || maximumOffset > 0x1000 ||
            (currentOffset & 3) != 0 || (maximumOffset & 3) != 0 || currentOffset == maximumOffset)
            throw new ArgumentOutOfRangeException(nameof(currentOffset), "Mana offsets must be distinct aligned fields in the UID-data record.");
        manaCurrentOffset = currentOffset;
        manaMaximumOffset = maximumOffset;
        ManaStatus = $"Mana fields configured at +0x{currentOffset:X}/+0x{maximumOffset:X}";
    }

    public void ResetMana()
    {
        manaCurrentOffset = -1;
        manaMaximumOffset = -1;
        ManaStatus = "Mana layout is not enabled";
    }

    public bool ManaSupported => manaCurrentOffset >= 0 && manaMaximumOffset >= 0;

    public ManaReading ReadMana()
    {
        if (!ManaSupported) return default;
        try { return TargetMana(LocalPlayer().Id); }
        catch (InvalidOperationException) { return default; }
        catch (System.ComponentModel.Win32Exception) { return default; }
    }

    public ManaReading TargetMana(uint id)
    {
        if (!ManaSupported || id == 0 || handle is null) return default;
        _ = TargetHealth(id); // Refreshes the UID-data address cache and validates UID/alive state.
        if (!healthRecords.TryGetValue(id, out uint record)) return default;
        try
        {
            int length = Math.Max(Math.Max(manaCurrentOffset + 4, manaMaximumOffset + 2), 0x75);
            var first = Native.Read(handle, (nint)record, length);
            var second = Native.Read(handle, (nint)record, length);
            if (!first.AsSpan().SequenceEqual(second) || BitConverter.ToUInt32(first, 0) != id || first[0x74] == 0)
                return default;
            return DecodeManaRecord(first, id, manaCurrentOffset, manaMaximumOffset);
        }
        catch (System.ComponentModel.Win32Exception) { return default; }
        catch (ArgumentException) { return default; }
    }

    internal static ManaReading DecodeManaRecord(byte[] bytes, uint id, int currentOffset, int maximumOffset)
    {
        if (bytes.Length <= 0x74 || currentOffset < 0 || maximumOffset < 0 || currentOffset > bytes.Length - 4 || maximumOffset > bytes.Length - 2 ||
            BitConverter.ToUInt32(bytes, 0) != id || bytes[0x74] == 0) return default;
        int current = BitConverter.ToInt32(bytes, currentOffset);
        uint maximum = BitConverter.ToUInt16(bytes, maximumOffset);
        return maximum > 0 && current >= 0 && (uint)current <= maximum ? new(true, current, maximum) : default;
    }
}
