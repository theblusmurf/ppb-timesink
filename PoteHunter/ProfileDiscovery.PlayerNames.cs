using System.Security.Cryptography;

namespace PoteHunter;

internal sealed record PlayerNameCodeLayout(string ClientHash, uint UidManagerRva,
    uint GetterRva, uint LookupRva, IReadOnlyList<SignatureEvidence> Evidence);

public static partial class ProfileDiscovery
{
    // The body-ID text consumer and health consumer must use the same manager
    // getter and active record lookup. The independent name lookup and record
    // copy prove the +8 string, +18 length, +1c capacity and list node layout.
    static readonly Rule[] playerNameRules =
    [
        new("Player name manager getter", "55 8B EC 6A FF 68 ?? ?? ?? ?? 64 A1 00 00 00 00 50 51 A1 ?? ?? ?? ?? 33 C5 50 8D 45 F4 64 A3 00 00 00 00 A1 ?? ?? ?? ?? 85 C0 75 ?? 6A 34 E8 ?? ?? ?? ?? 83 C4 04 89 45 F0 C7 45 FC 00 00 00 00 85 C0 74 ?? 6A 1E 8B C8 E8 ?? ?? ?? ?? A3 ?? ?? ?? ??", 36),
        new("Player name active UID lookup", "55 8B EC 51 56 8B F1 8D 45 08 50 8D 45 FC 50 8D 4E 0C E8 ?? ?? ?? ?? 8B 45 FC 3B 46 10 5E 74 ?? 8B 40 0C 80 78 74 00 75 ?? 33 C0 8B E5 5D C2 04 00"),
        new("Player name equality lookup", "55 8B EC 8B 49 04 53 56 57 8B 01 3B C1 74 ?? 90 8B 78 08 83 7F 1C 10 8D 57 08 72 ?? 8B 12 8B 75 08 8A 1A 3A 1E"),
        new("Player body UID name text", "FF 70 04 E8 ?? ?? ?? ?? 8B C8 E8 ?? ?? ?? ?? 85 C0 0F 84 ?? ?? ?? ?? 83 78 18 00 0F 84 ?? ?? ?? ?? 83 78 1C 10 8D 48 08 72 ?? 8B 09"),
        new("Player body UID health text", "FF 70 04 E8 ?? ?? ?? ?? 8B C8 E8 ?? ?? ?? ?? 85 C0 74 ?? 8B 48 4C 85 C9 74 ?? 8B 40 44 33 D2 85 C0 89 8D C8 FE FF FF 0F 48 C2 89 85 DC FE FF FF 56 57"),
        new("Player UID and name record copy", "8B 5E 08 8B 07 89 03 8D 4B 08 8B 47 04 89 43 04 8D 47 08 3B C8 74 ?? 83 78 14 10 8B D0 72 ?? 8B 10 FF 70 10 52 E8 ?? ?? ?? ??")
    ];

    internal static PlayerNameCodeLayout? PlayerNameEvidence(string path, uint uidManagerRva)
        => PlayerNameEvidence(File.ReadAllBytes(path), uidManagerRva);

    internal static PlayerNameCodeLayout? PlayerNameEvidence(byte[] bytes, uint uidManagerRva)
    {
        var image = new PeImage(bytes);
        var result = new List<SignatureEvidence>();
        foreach (var rule in playerNameRules)
        {
            var matches = image.Find(new Pattern(rule.Pattern));
            if (matches.Count != 1) return null;
            uint? capture = null;
            if (rule.CaptureOffset >= 0)
            {
                uint address = image.U32Rva(matches[0] + (uint)rule.CaptureOffset);
                if (address < image.ImageBase || address - image.ImageBase != uidManagerRva) return null;
                capture = uidManagerRva;
            }
            result.Add(new(rule.Name, matches[0], rule.Pattern, rule.CaptureOffset, capture));
        }
        SignatureEvidence Proof(string name) => result.Single(e => e.Name == name);
        uint getter = Proof("Player name manager getter").CodeRva;
        uint lookup = Proof("Player name active UID lookup").CodeRva;
        bool Call(uint at, uint target) => (long)at + 5 + unchecked((int)image.U32Rva(at + 1)) == target;
        foreach (string name in new[] { "Player body UID name text", "Player body UID health text" })
        {
            uint start = Proof(name).CodeRva;
            if (!Call(start + 3, getter) || !Call(start + 10, lookup)) return null;
        }
        // The getter writes back the same singleton it read, rather than an
        // unrelated global that merely had a plausible live pointer.
        var getterProof = Proof("Player name manager getter");
        if (image.U32Rva(getter + (uint)getterProof.Pattern.Split(' ').Length - 4) != image.ImageBase + uidManagerRva)
            return null;
        var rootSection = image.SectionAt(uidManagerRva, 4);
        if (rootSection == null || !rootSection.Writable || rootSection.Executable) return null;
        return new(Convert.ToHexStringLower(SHA256.HashData(bytes)), uidManagerRva, getter, lookup, result.AsReadOnly());
    }

    internal static void ValidateLoadedPlayerNames(PlayerNameCodeLayout layout, long module,
        Func<uint, int, byte[]> readRva)
    {
        foreach (var proof in layout.Evidence)
        {
            byte[] code = readRva(proof.CodeRva, proof.Pattern.Split(' ').Length);
            if (!Matches(code, proof.Pattern)) throw new InvalidOperationException("Loaded player-name code differs: " + proof.Name);
            if (proof.CapturedRva.HasValue && BitConverter.ToUInt32(code, proof.CaptureOffset) != (ulong)module + layout.UidManagerRva)
                throw new InvalidOperationException("Loaded player-name manager differs");
            if (proof.Name == "Player name manager getter" && BitConverter.ToUInt32(code, code.Length - 4) != (ulong)module + layout.UidManagerRva)
                throw new InvalidOperationException("Loaded player-name singleton writeback differs");
            if (proof.Name is "Player body UID name text" or "Player body UID health text")
                foreach (var call in new[] { (Offset: 3, Target: layout.GetterRva), (Offset: 10, Target: layout.LookupRva) })
                    if ((long)proof.CodeRva + call.Offset + 5 + BitConverter.ToInt32(code, call.Offset + 1) != call.Target)
                        throw new InvalidOperationException("Loaded player-name consumer points to a different lookup");
        }
    }

    internal static void CheckPlayerNameLayouts()
    {
        byte[] fixture = new byte[0x8000];
        void U16(int at, ushort value) => BitConverter.TryWriteBytes(fixture.AsSpan(at), value);
        void U32(int at, uint value) => BitConverter.TryWriteBytes(fixture.AsSpan(at), value);
        U16(0, 0x5a4d); U32(0x3c, 0x80); U32(0x80, 0x4550); U16(0x84, 0x14c); U16(0x86, 2);
        U16(0x94, 0xe0); U16(0x98, 0x10b); U32(0xb4, 0x400000); U32(0xd0, 0x8000);
        void Section(int at, uint rva, uint size, uint flags)
        { U32(at + 8, size); U32(at + 12, rva); U32(at + 16, size); U32(at + 20, rva); U32(at + 36, flags); }
        Section(0x178, 0x1000, 0x5000, 0x60000020); Section(0x1a0, 0x6000, 0x2000, 0xc0000040);
        var locations = playerNameRules.Select((r, i) => (r.Name, At: 0x1100 + i * 0x200)).ToDictionary(p => p.Name, p => p.At);
        foreach (var rule in playerNameRules)
            rule.Pattern.Split(' ').Select(s => s == "??" ? (byte)0 : Convert.ToByte(s, 16)).ToArray().CopyTo(fixture, locations[rule.Name]);
        int getter = locations["Player name manager getter"], lookup = locations["Player name active UID lookup"];
        U32(getter + 36, 0x406100); U32(getter + playerNameRules[0].Pattern.Split(' ').Length - 4, 0x406100);
        foreach (string name in new[] { "Player body UID name text", "Player body UID health text" })
            foreach (var call in new[] { (Offset: 3, Target: getter), (Offset: 10, Target: lookup) })
                U32(locations[name] + call.Offset + 1, unchecked((uint)(call.Target - (locations[name] + call.Offset + 5))));
        var valid = PlayerNameEvidence(fixture, 0x6100) ?? throw new Exception("Verified player-name layout rejected");
        ValidateLoadedPlayerNames(valid, 0x400000, (rva, count) => fixture.AsSpan((int)rva, count).ToArray());
        void Reject(Action<byte[]> change, string reason)
        {
            var broken = (byte[])fixture.Clone(); change(broken);
            if (PlayerNameEvidence(broken, 0x6100) != null) throw new Exception("Player-name discovery accepted " + reason);
        }
        Reject(b => b[locations["Player body UID name text"] + 24] ^= 1, "changed name field");
        Reject(b => Array.Copy(b, lookup, b, 0x3500, playerNameRules[1].Pattern.Split(' ').Length), "ambiguous UID lookup");
        Reject(b => BitConverter.TryWriteBytes(b.AsSpan(getter + 36), 0x406104u), "different manager root");
        Reject(b => b[locations["Player body UID health text"] + 4] ^= 1, "unlinked health lookup");
        Reject(b => b[locations["Player body UID name text"] + 11] ^= 1, "unlinked name lookup");
        Reject(b => b[locations["Player name equality lookup"] + 22] ^= 1, "old string layout");
        var relocated = (byte[])fixture.Clone();
        BitConverter.TryWriteBytes(relocated.AsSpan(getter + 36), 0x606100u);
        BitConverter.TryWriteBytes(relocated.AsSpan(getter + playerNameRules[0].Pattern.Split(' ').Length - 4), 0x606100u);
        ValidateLoadedPlayerNames(valid, 0x600000, (rva, count) => relocated.AsSpan((int)rva, count).ToArray());
        relocated[locations["Player body UID name text"] + 11] ^= 1;
        bool rejected = false;
        try { ValidateLoadedPlayerNames(valid, 0x600000, (rva, count) => relocated.AsSpan((int)rva, count).ToArray()); }
        catch (InvalidOperationException) { rejected = true; }
        if (!rejected) throw new Exception("Loaded player-name call mismatch accepted");
    }
}
