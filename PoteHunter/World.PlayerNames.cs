using System.Text;
using PoteMemoryProbe;

namespace PoteHunter;

internal sealed record PlayerNameSnapshot(IReadOnlyDictionary<uint, string> Names, bool Consistent, string Status);

internal static class PlayerNameReader
{
    const int MaximumRecords = 8192, MaximumNameBytes = 128;
    internal const int MaximumReads = 2048, MaximumMilliseconds = 50;
    static readonly UTF8Encoding StrictUtf8 = new(false, true);
    sealed class BudgetExceededException : InvalidOperationException { }
    sealed record Node(uint Address, byte[] Header, uint Record, byte[] Data);
    sealed record Body(Entity Entity, byte[] Header);
    sealed record Name(uint Id, uint Record, byte[] Header, byte[] Bytes, string Text);

    internal static bool Eligible(Entity e, uint selfId) => e.Id != 0 && e.Id != selfId &&
        (e.Id & 0xf0000000) == 0 && e.Name.Length == 0 &&
        PlayerRecognition.Faction(e.Model) != PlayerFaction.Unknown;

    // All state belongs to this invocation. No successful name, body address or
    // UID-record pointer can survive a poll, character/zone change or reconnect.
    internal static PlayerNameSnapshot Read(IReadOnlyList<Entity> entities, uint selfId, uint manager,
        uint creatureVtable, Func<long, int, byte[]> read, Func<bool> contextSame, Func<long>? milliseconds = null)
    {
        milliseconds ??= () => Environment.TickCount64;
        long started = milliseconds(); int reads = 0;
        PlayerNameSnapshot Unavailable(string reason) => new(new Dictionary<uint, string>(), false, reason);
        byte[] Read(long address, int count)
        {
            if (reads >= MaximumReads || milliseconds() - started >= MaximumMilliseconds)
                throw new BudgetExceededException();
            reads++;
            if (address < 0x10000 || count < 1 || (ulong)address + (uint)count > uint.MaxValue)
                throw new InvalidOperationException("Player-name read bounds changed");
            byte[] data = read(address, count);
            if (data.Length != count) throw new InvalidOperationException("Player-name read is incomplete");
            return data;
        }
        byte[] StringBytes(byte[] header, int offset)
        {
            uint length = BitConverter.ToUInt32(header, offset + 0x10), capacity = BitConverter.ToUInt32(header, offset + 0x14);
            if (length > MaximumNameBytes || capacity < length || capacity > 4096 || capacity < 16 && length > 15)
                throw new InvalidOperationException("Player-name string bounds are invalid");
            byte[] bytes = capacity < 16 ? header.AsSpan(offset, (int)length + 1).ToArray() :
                Read(BitConverter.ToUInt32(header, offset), checked((int)length + 1));
            if (bytes[^1] != 0) throw new InvalidOperationException("Player-name string terminator changed");
            return bytes;
        }
        string Text(byte[] bytes)
        {
            string value = StrictUtf8.GetString(bytes, 0, bytes.Length - 1);
            if (value.Any(c => char.IsControl(c) || c == '\uFFFD')) throw new InvalidOperationException("Player-name text is invalid");
            return value;
        }
        bool BodyMatches(Entity e, byte[] header) => BitConverter.ToUInt32(header, 0) == creatureVtable &&
            BitConverter.ToUInt32(header, 4) == e.Id && BitConverter.ToUInt32(header, 8) == e.Generation && header[0x12] == 0 &&
            BitConverter.ToUInt32(header, 0x28) == 0 && Text(StringBytes(header, 0x50)) == e.Model;
        static bool HeaderSame(byte[] first, byte[] second) => first.AsSpan(0, 0x20).SequenceEqual(second.AsSpan(0, 0x20)) &&
            first[0x74] == second[0x74];
        static bool BodySame(byte[] first, byte[] second) => first.AsSpan(0, 12).SequenceEqual(second.AsSpan(0, 12)) &&
            first[0x12] == second[0x12] && first.AsSpan(0x18, 0x18).SequenceEqual(second.AsSpan(0x18, 0x18)) &&
            first.AsSpan(0x50, 0x18).SequenceEqual(second.AsSpan(0x50, 0x18));
        try
        {
            if (selfId == 0 || manager < 0x10000) return Unavailable("Player-name context unavailable");
            var requested = entities.GroupBy(e => e.Id).Where(g => g.Count() == 1).Select(g => g.Single())
                .Where(e => Eligible(e, selfId)).ToDictionary(e => e.Id);
            if (requested.Count == 0) return new(new Dictionary<uint, string>(), true, "No unnamed recognized other-player bodies in this poll");
            var bodies = new Dictionary<uint, Body>();
            foreach (var entity in requested.Values)
            {
                try
                {
                    var bytes = Read(entity.Address, 0x68);
                    if (BodyMatches(entity, bytes)) bodies.Add(entity.Id, new(entity, bytes));
                }
                catch (Exception ex) when (ReadFailure(ex)) { }
            }
            if (bodies.Count == 0) return Unavailable("Other-player body identities changed during the name read");
            byte[] managerHeader = Read(manager, 12);
            uint sentinel = BitConverter.ToUInt32(managerHeader, 4), count = BitConverter.ToUInt32(managerHeader, 8);
            if (sentinel < 0x10000 || count > MaximumRecords) return Unavailable("Player-name list bounds unavailable");
            byte[] sentinelHeader = Read(sentinel, 12);
            uint cursor = BitConverter.ToUInt32(sentinelHeader, 0);
            var visited = new HashSet<uint>(); var nodes = new List<Node>();
            var duplicateIds = new HashSet<uint>(); var seenIds = new HashSet<uint>(); var names = new List<Name>();
            while (cursor != sentinel)
            {
                if (cursor < 0x10000 || nodes.Count >= MaximumRecords || !visited.Add(cursor))
                    return Unavailable("Player-name list is changing or cyclic");
                byte[] nodeHeader = Read(cursor, 12); uint record = BitConverter.ToUInt32(nodeHeader, 8);
                if (record < 0x10000) return Unavailable("Player-name record unavailable");
                byte[] data = Read(record, 0x75);
                nodes.Add(new(cursor, nodeHeader, record, data));
                uint id = BitConverter.ToUInt32(data, 0);
                if (data[0x74] != 0 && id != 0)
                {
                    if (!seenIds.Add(id)) duplicateIds.Add(id);
                    if (bodies.ContainsKey(id))
                    {
                        try
                        {
                            byte[] bytes = StringBytes(data, 8); string text = Text(bytes);
                            if (!string.IsNullOrWhiteSpace(text)) names.Add(new(id, record, data, bytes, text));
                        }
                        catch (Exception ex) when (ReadFailure(ex)) { }
                    }
                }
                cursor = BitConverter.ToUInt32(nodeHeader, 0);
            }
            if (nodes.Count != count) return Unavailable("Player-name list count changed");
            var recordsChanged = new HashSet<uint>();
            foreach (var node in nodes)
            {
                if (!node.Header.AsSpan().SequenceEqual(Read(node.Address, 12))) return Unavailable("Player-name list links changed");
                byte[] second = Read(node.Record, 0x75);
                if (!HeaderSame(node.Data, second))
                {
                    // Unknown +4 is compared as an observed header byte field;
                    // it is never assumed to be the creature generation.
                    recordsChanged.Add(BitConverter.ToUInt32(node.Data, 0));
                    recordsChanged.Add(BitConverter.ToUInt32(second, 0));
                }
            }
            var accepted = new Dictionary<uint, string>();
            foreach (var name in names.Where(n => !duplicateIds.Contains(n.Id) && !recordsChanged.Contains(n.Id)))
            {
                try
                {
                    var original = name.Header;
                    if (!name.Bytes.AsSpan().SequenceEqual(StringBytes(original, 8))) continue;
                    if (!HeaderSame(original, Read(name.Record, 0x75))) continue;
                    var body = bodies[name.Id]; byte[] after = Read(body.Entity.Address, 0x68);
                    if (BodySame(body.Header, after) && BodyMatches(body.Entity, after)) accepted.TryAdd(name.Id, name.Text);
                }
                catch (Exception ex) when (ReadFailure(ex)) { }
            }
            if (!managerHeader.AsSpan().SequenceEqual(Read(manager, 12)) ||
                !sentinelHeader.AsSpan().SequenceEqual(Read(sentinel, 12)) || !contextSame())
                return Unavailable("Player-name world context changed");
            return new(accepted, true, $"Verified UID name snapshot: {accepted.Count} other-player names attached");
        }
        catch (BudgetExceededException) { return Unavailable("Player-name snapshot read budget reached; keeping observed body labels"); }
        catch (Exception ex) when (ReadFailure(ex)) { return Unavailable("Player-name snapshot unavailable; keeping observed body labels"); }
    }

    static bool ReadFailure(Exception ex) => ex is not BudgetExceededException &&
        (ex is System.ComponentModel.Win32Exception or InvalidOperationException or DecoderFallbackException or ArgumentException or OverflowException);
}

public sealed partial class World
{
    readonly PlayerNameDisplayCache playerNameDisplay = new();
    PlayerNameCodeLayout? playerNameLayout;
    public bool PlayerNamesSupported => playerNameLayout != null;
    public string PlayerNamesStatus { get; private set; } = "Other-player name layout has not been checked";
    public int PlayerNamesLastAttachedCount { get; private set; }

    void ResetPlayerNames()
    { playerNameDisplay.Reset(); playerNameLayout = null; PlayerNamesLastAttachedCount = 0; PlayerNamesStatus = "Other-player name reader is unavailable"; }

    void ConfigurePlayerNames()
    {
        ResetPlayerNames();
        if (!ConnectionVerified || handle is null || moduleBase == 0) return;
        try
        {
            var layout = ProfileDiscovery.PlayerNameEvidence(PoteMemoryProbe.Program.ClientPath, profile.UidDataManager);
            if (layout == null || layout.ClientHash != ClientHash)
            { PlayerNamesStatus = "Other-player name signatures are missing, ambiguous or inconsistent"; return; }
            ProfileDiscovery.ValidateLoadedPlayerNames(layout, moduleBase,
                (rva, count) => Native.Read(handle, (nint)(moduleBase + rva), count));
            playerNameLayout = layout;
            PlayerNamesStatus = "Other-player UID names verified against client code; awaiting a fresh poll";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException)
        { PlayerNamesStatus = "Other-player name layout unavailable: " + ex.Message; }
    }

    readonly record struct PlayerNameContext(uint Manager, uint Scene, uint Actor, uint SelfId, uint Zone);
    PlayerNameContext ReadPlayerNameContext()
    {
        uint actor = Pointer(moduleBase + profile.LocalActor);
        if (actor < 0x10000) throw new InvalidOperationException("Local character unavailable for the other-player name snapshot");
        return new(Pointer(moduleBase + profile.UidDataManager), Pointer(moduleBase + profile.Scene), actor,
            Pointer(actor + 0x164), Pointer(actor + 0x168));
    }
    void AttachPlayerNames(List<Entity> accepted)
    {
        PlayerNamesLastAttachedCount = 0;
        if (playerNameLayout == null || handle is null || !ConnectionVerified) return;
        try
        {
            var context = ReadPlayerNameContext();
            long started = Environment.TickCount64;
            Interlocked.Increment(ref playerNameScanCount);
            var snapshot = PlayerNameReader.Read(accepted, context.SelfId, context.Manager, checked((uint)(moduleBase + CreatureRva)),
                (address, count) => Native.Read(handle, (nint)address, count), () => context == ReadPlayerNameContext());
            PlayerNamesStatus = snapshot.Status;
            if (!snapshot.Consistent) { playerNameDisplay.Reset(); return; }
            playerNameDisplay.Store(PlayerNameDisplayContext(context), accepted, snapshot, started);
            for (int i = 0; i < accepted.Count; i++)
                if (PlayerNameReader.Eligible(accepted[i], context.SelfId) && snapshot.Names.TryGetValue(accepted[i].Id, out string? name))
                { accepted[i] = accepted[i] with { VerifiedPlayerName = name }; PlayerNamesLastAttachedCount++; }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or OverflowException)
        { playerNameDisplay.Reset(); PlayerNamesStatus = "Other-player name snapshot unavailable; keeping observed body labels"; }
    }

    string PlayerNameDisplayContext(PlayerNameContext context) => $"{ClientHash}:{Pid}:{moduleBase:X}:{context}";

    void AttachObservedPlayerNames(List<Entity> accepted)
    {
        PlayerNamesLastAttachedCount = 0;
        if (playerNameLayout == null || handle is null || !ConnectionVerified) { playerNameDisplay.Reset(); return; }
        try
        {
            var context = ReadPlayerNameContext();
            int count = playerNameDisplay.Apply(PlayerNameDisplayContext(context), accepted, context.SelfId, Environment.TickCount64);
            if (context != ReadPlayerNameContext())
            {
                playerNameDisplay.Reset();
                for (int i = 0; i < accepted.Count; i++) accepted[i] = accepted[i] with { VerifiedPlayerName = "" };
                return;
            }
            PlayerNamesLastAttachedCount = count;
            if (count > 0) Interlocked.Increment(ref playerNameReuseCount);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or ArgumentException or OverflowException)
        {
            playerNameDisplay.Reset();
            for (int i = 0; i < accepted.Count; i++) accepted[i] = accepted[i] with { VerifiedPlayerName = "" };
        }
    }
}
