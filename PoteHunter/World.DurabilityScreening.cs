using System.Diagnostics;

namespace PoteHunter;

// Reuse is limited to threshold/display screening. It still rereads one copy
// of every equipment field used by the two-pass reader, including wear, item
// identity, eligibility and all sixteen membership entries. Changed data falls
// through to the normal reader; no cached sample ever authorizes repair input.
internal sealed class DurabilityScreeningCache
{
    internal const int MaximumAgeMilliseconds = 1000;
    const int MaximumFields = 64, MaximumBytes = 32 * 1024;
    sealed record Field(long Address, int Count, byte[] Bytes);
    DurabilityReading? cached;
    DurabilityReadContext? identity;
    Field[] fields = [];
    long observed;
    public long FullReads { get; private set; }
    public long ReusedReads { get; private set; }

    public void Reset() { cached = null; identity = null; fields = []; observed = 0; }

    public DurabilityReading Read(int slotsOffset, uint equipmentClass, Func<DurabilityReadContext> context,
        Func<long, int, byte[]> read, Func<long>? milliseconds = null)
    {
        milliseconds ??= () => Environment.TickCount64;
        long started = milliseconds(); var watch = Stopwatch.StartNew();
        try
        {
            var before = context();
            if (cached is { Known: true } && identity != null && before.Same(identity) && started >= observed &&
                started - observed < MaximumAgeMilliseconds && fields.Length > 0)
            {
                bool same = true;
                foreach (var field in fields)
                    if (!field.Bytes.AsSpan().SequenceEqual(read(field.Address, field.Count))) { same = false; break; }
                if (same && before.Same(context()) && watch.Elapsed <= DurabilitySnapshot.MaximumReadDuration &&
                    milliseconds() - observed < MaximumAgeMilliseconds)
                { ReusedReads++; return cached; }
            }
            Reset(); FullReads++;
            var captured = new Dictionary<(long Address, int Count), byte[]>();
            bool bounded = true;
            byte[] Capture(long address, int count)
            {
                byte[] value = read(address, count);
                if (bounded)
                {
                    captured[(address, count)] = value.ToArray();
                    if (captured.Count > MaximumFields || captured.Values.Sum(bytes => bytes.Length) > MaximumBytes)
                    { bounded = false; captured.Clear(); }
                }
                return value;
            }
            var result = DurabilitySnapshot.Read(slotsOffset, equipmentClass, context, Capture);
            var after = context();
            if (result.Known && bounded && captured.Count > 0 && before.Same(after) && result.Context == after.Key &&
                milliseconds() >= started && milliseconds() - started < MaximumAgeMilliseconds)
            {
                cached = result; identity = after; observed = started;
                fields = captured.Select(pair => new Field(pair.Key.Address, pair.Key.Count, pair.Value)).ToArray();
            }
            return result;
        }
        catch (Exception ex) when (DurabilitySnapshot.RoutineFailure(ex))
        { Reset(); return DurabilitySnapshot.Unknown("Equipment screening unavailable: " + ex.Message); }
    }
}

public sealed partial class World
{
    readonly DurabilityScreeningCache durabilityScreening = new();

    internal DurabilityReading ReadDurabilityScreening()
    {
        var layout = durabilityLayout;
        if (!ConnectionVerified || handle is null || layout == null)
        { durabilityScreening.Reset(); return DurabilitySnapshot.Unknown(DurabilityStatus); }
        return durabilityScreening.Read(layout.SlotsOffset, checked((uint)(moduleBase + layout.EquipmentVtableRva)),
            () =>
            {
                if (!ClientProcessAlive) throw new InvalidOperationException("The connected client is no longer available");
                return DurabilityContext(layout.SlotsOffset);
            }, (address, count) => PoteMemoryProbe.Native.Read(handle!, (nint)address, count));
    }
}
