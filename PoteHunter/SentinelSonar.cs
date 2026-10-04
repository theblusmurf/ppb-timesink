using System.Runtime.InteropServices;

namespace PoteHunter;

public static class SentinelSonarWave
{
    public const int SampleRate = 22050;
    public const int DurationMilliseconds = 720;
    public const double MaximumAmplitude = .14;
    public static byte[] Create(int volume)
    {
        int samples = SampleRate * DurationMilliseconds / 1000;
        var wave = new byte[44 + samples * 2];
        using var writer = new BinaryWriter(new MemoryStream(wave, writable: true));
        writer.Write("RIFF"u8); writer.Write(wave.Length - 8); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)1); writer.Write(SampleRate);
        writer.Write(SampleRate * 2); writer.Write((ushort)2); writer.Write((ushort)16);
        writer.Write("data"u8); writer.Write(samples * 2);
        double gain = Math.Clamp(volume, 0, 100) / 100.0 * MaximumAmplitude;
        for (int index = 0; index < samples; index++)
        {
            double time = index / (double)SampleRate;
            double pingTime = time - (time < .30 ? .03 : .37);
            double signal = 0;
            const double length = .22;
            if (pingTime >= 0 && pingTime < length)
            {
                double attack = Math.Min(1, pingTime / .012);
                double release = Math.Min(1, (length - pingTime) / .075);
                double envelope = Math.Pow(Math.Sin(Math.PI / 2 * attack), 2) * Math.Pow(Math.Sin(Math.PI / 2 * release), 2);
                double phase = 2 * Math.PI * (780 * pingTime + (650 - 780) * pingTime * pingTime / (2 * length));
                signal = gain * envelope * Math.Sin(phase);
            }
            writer.Write((short)Math.Round(signal * short.MaxValue));
        }
        return wave;
    }
}

// One PCM stream on a worker; never a blocking beep or a queued series of clips.
// waveOut has no default-system-sound fallback when a device is unavailable.
public sealed class SentinelSonar : IDisposable
{
    readonly object gate = new();
    readonly Func<byte[], CancellationToken, Task> playback;
    CancellationTokenSource? activeCancellation;
    Task? active;
    bool disposed;
    bool faulted;
    string? lastError;
    public SentinelSonar() : this(PlayWaveOut) { }
    internal SentinelSonar(Func<byte[], CancellationToken, Task> playback) => this.playback = playback;
    public bool Playing { get { lock (gate) return active != null; } }
    public bool Faulted { get { lock (gate) return faulted; } }
    public string? LastError { get { lock (gate) return lastError; } }

    public bool TryPlay(int volume)
    {
        lock (gate)
        {
            if (disposed || faulted || active != null || volume <= 0) return false;
            activeCancellation = new();
            var cancellation = activeCancellation;
            var wave = SentinelSonarWave.Create(volume);
            lastError = null;
            active = Task.Run(async () =>
            {
                try { await playback(wave, cancellation.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    lock (gate)
                    {
                        lastError = ex.GetType().Name + ": " + ex.Message;
                        // A driver that cannot release its own buffer/handle must
                        // never cause each following arrival to allocate another.
                        if (ex is SentinelAudioCleanupException) faulted = true;
                    }
                }
                finally
                {
                    lock (gate)
                    {
                        active = null; activeCancellation = null;
                        cancellation.Dispose();
                    }
                }
            });
            return true;
        }
    }

    public void Stop() { lock (gate) activeCancellation?.Cancel(); }
    public void Dispose() { lock (gate) { disposed = true; activeCancellation?.Cancel(); } }

    static async Task PlayWaveOut(byte[] wave, CancellationToken cancellation)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Sentinel audio requires Windows.");
        cancellation.ThrowIfCancellationRequested();
        var format = new WaveFormat { FormatTag = 1, Channels = 1, SamplesPerSecond = SentinelSonarWave.SampleRate,
            BytesPerSecond = SentinelSonarWave.SampleRate * 2, BlockAlign = 2, BitsPerSample = 16 };
        IntPtr handle = IntPtr.Zero, header = IntPtr.Zero;
        GCHandle pinned = default;
        bool prepared = false;
        try
        {
            uint opened = waveOutOpen(out handle, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0);
            if (opened != 0) { handle = IntPtr.Zero; Check(opened, "open audio device"); }
            var pcm = wave.AsSpan(44).ToArray();
            pinned = GCHandle.Alloc(pcm, GCHandleType.Pinned);
            header = Marshal.AllocHGlobal(Marshal.SizeOf<WaveHeader>());
            Marshal.StructureToPtr(new WaveHeader { Data = pinned.AddrOfPinnedObject(), Length = (uint)pcm.Length }, header, false);
            Check(waveOutPrepareHeader(handle, header, (uint)Marshal.SizeOf<WaveHeader>()), "prepare sonar"); prepared = true;
            cancellation.ThrowIfCancellationRequested();
            Check(waveOutWrite(handle, header, (uint)Marshal.SizeOf<WaveHeader>()), "play sonar");
            // Polling does not affect the UI thread; cancellation resets only this owned handle.
            var timeout = Environment.TickCount64 + SentinelSonarWave.DurationMilliseconds + 2000;
            while ((Marshal.PtrToStructure<WaveHeader>(header).Flags & 1) == 0)
            {
                cancellation.ThrowIfCancellationRequested();
                if (Environment.TickCount64 >= timeout) throw new TimeoutException("Sonar device did not finish the bounded clip.");
                await Task.Delay(15, cancellation).ConfigureAwait(false);
            }
        }
        finally
        {
            NativeCleanup result = new(true, true, 0, 0, 0);
            if (handle != IntPtr.Zero)
            {
                result = await CleanupNative(prepared, () => waveOutReset(handle),
                    () => waveOutUnprepareHeader(handle, header, (uint)Marshal.SizeOf<WaveHeader>()),
                    () => waveOutClose(handle)).ConfigureAwait(false);
            }
            if (result.BufferReleased)
            {
                if (header != IntPtr.Zero) Marshal.FreeHGlobal(header);
                if (pinned.IsAllocated) pinned.Free();
            }
            // A still-owned driver buffer cannot safely be freed. On this exceptional
            // path the instance enters a sticky fault, retaining at most this one
            // allocation/handle until process exit rather than risking use-after-free.
            if (!result.DeviceClosed || !result.BufferReleased)
                throw new SentinelAudioCleanupException($"Sonar device release failed (reset {result.Reset}, unprepare {result.Unprepare}, close {result.Close}); sound is disabled until the application restarts.");
        }
    }

    internal readonly record struct NativeCleanup(bool BufferReleased, bool DeviceClosed, uint Reset, uint Unprepare, uint Close);
    internal static async Task<NativeCleanup> CleanupNative(bool prepared, Func<uint> reset, Func<uint> unprepare, Func<uint> close)
    {
        uint resetResult = 0, unprepareResult = prepared ? 33u : 0u, closeResult = 33;
        for (int attempt = 0; attempt < 20; attempt++)
        {
            resetResult = reset();
            if (prepared && unprepareResult != 0) unprepareResult = unprepare();
            // Always attempt close, even after a failed unprepare. A successful
            // close establishes that the driver no longer owns any queued buffer.
            closeResult = close();
            if (closeResult == 0) break;
            if (attempt < 19) await Task.Delay(10).ConfigureAwait(false);
        }
        return new(unprepareResult == 0 || closeResult == 0, closeResult == 0, resetResult, unprepareResult, closeResult);
    }

    static void Check(uint error, string stage) { if (error != 0) throw new InvalidOperationException($"Could not {stage} ({error})."); }
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    struct WaveFormat
    {
        internal ushort FormatTag, Channels;
        internal uint SamplesPerSecond, BytesPerSecond;
        internal ushort BlockAlign, BitsPerSample, ExtraSize;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct WaveHeader
    {
        internal IntPtr Data;
        internal uint Length, Recorded;
        internal UIntPtr User;
        internal uint Flags, Loops;
        internal IntPtr Next;
        internal UIntPtr Reserved;
    }
    [DllImport("winmm.dll")] static extern uint waveOutOpen(out IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] static extern uint waveOutPrepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveOutWrite(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveOutReset(IntPtr handle);
    [DllImport("winmm.dll")] static extern uint waveOutUnprepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveOutClose(IntPtr handle);
}

internal sealed class SentinelAudioCleanupException(string message) : IOException(message);
