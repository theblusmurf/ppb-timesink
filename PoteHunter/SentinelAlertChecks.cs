using System.Text.Json;

namespace PoteHunter;

internal static class SentinelAlertChecks
{
    internal static void Run()
    {
        var origin = DateTime.UnixEpoch;
        var local = new SentinelPlayerIdentity(1, 1, 100, "PC_MAN.GCMDS");
        var context = new SentinelAlertContext("client-1", local, 8);
        var enemy = new SentinelPlayerObservation(new(2, 1, 200, "PC_AKHAN_A.GCMDS"), "Opponent", 25, PlayerRelation.Enemy);
        var input = new SentinelAlertInput(true, true, true, true, context, [], true, 45);
        var policy = new SentinelAlertPolicy();
        SentinelAlertFrame At(double seconds, params SentinelPlayerObservation[] players) => policy.Update(input with { Players = players }, origin.AddSeconds(seconds));
        void Require(bool condition, string message) { if (!condition) throw new Exception("Sentinel: " + message); }

        var baseline = At(0, enemy);
        Require(baseline.Baseline && baseline.CurrentEnemies.Count == 1 && baseline.Arrivals.Count == 0 && !baseline.PlaySound, "startup must show an existing enemy without an alert");
        Require(!At(.1, enemy).PlaySound && At(.2, enemy).Arrivals.Count == 0, "stable player cannot repeat an arrival");
        At(.3);
        Require(At(1.5, enemy).Arrivals.Count == 0, "short entity hole must not repeat an arrival");
        At(1.6, enemy with { Distance = 25.1 });
        At(1.7, enemy with { Distance = 27.99 });
        Require(At(1.8, enemy).Arrivals.Count == 0, "entry radius jitter must preserve the presence");
        At(2, enemy with { Distance = 28.01 }); At(3, enemy with { Distance = 29 });
        var reentry = At(4.01, enemy);
        Require(reentry.Arrivals.Count == 1 && reentry.PlaySound, "clear sustained exit and re-entry must alert exactly once");
        var second = enemy with { Identity = enemy.Identity with { Id = 3, Address = 300 }, Name = "Second" };
        var third = enemy with { Identity = enemy.Identity with { Id = 4, Address = 400 }, Name = "Third" };
        var coalesced = At(4.1, enemy, second, third);
        Require(coalesced.Arrivals.Count == 2 && !coalesced.PlaySound, "simultaneous arrivals are coalesced and global sound spacing enforced");
        At(5.5, enemy, second, third);
        Require(At(7.2, enemy, second, third).Arrivals.Count == 0, "cooldown arrivals must not queue stale playback");
        var replacement = enemy with { Identity = enemy.Identity with { Generation = 2, Address = 201 } };
        Require(At(7.3, replacement, second, third).Arrivals.Count == 1, "generation and address replacement cannot inherit a presence");
        Require(At(7.4, replacement with { Identity = replacement.Identity with { Model = "PC_AKHAN_B.GCMDS" } }, second, third).Arrivals.Count == 1, "model identity change cannot inherit a presence");

        policy = new(); At(0);
        foreach (var relation in new[] { PlayerRelation.Party, PlayerRelation.Self, PlayerRelation.SameFaction, PlayerRelation.OpposingSafe, PlayerRelation.Unknown })
            Require(At(.1, enemy with { Relation = relation }).CurrentEnemies.Count == 0, "only an Enemy relation is eligible");
        Require(At(.2, enemy with { ConfirmedDead = true }).CurrentEnemies.Count == 0, "confirmed dead players cannot alert");
        Require(At(.3, enemy with { Distance = double.NaN }).CurrentEnemies.Count == 0 && At(.4, enemy with { Distance = -1 }).CurrentEnemies.Count == 0, "non-finite and negative range is invalid");
        Require(At(.5, enemy with { Distance = 25.001 }).Arrivals.Count == 0, "entry is bounded at exactly 25 units");
        Require(At(.6, enemy with { Identity = enemy.Identity with { Model = "PC_MAN.GCMDS" } }).CurrentEnemies.Count == 0, "same-faction model cannot acquire enemy status from a bad input relation");
        Require(At(.7, enemy with { Identity = enemy.Identity with { Model = "PC_UNKNOWN.GCMDS" } }).CurrentEnemies.Count == 0, "unknown model cannot acquire enemy status");
        Require(At(.75, enemy with { Identity = enemy.Identity with { Id = 0x40000002 } }).CurrentEnemies.Count == 0, "NPC UID category cannot acquire enemy status");
        Require(At(.8, enemy, replacement).CurrentEnemies.Count == 0, "ambiguous simultaneous UID generations are suppressed");
        foreach (var conflicting in new[] { replacement with { Relation = PlayerRelation.Party }, replacement with { Relation = PlayerRelation.Unknown }, replacement with { ConfirmedDead = true }, enemy })
            Require(At(.9, enemy, conflicting).CurrentEnemies.Count == 0 && !At(.95, enemy, conflicting).PlaySound, "mixed-status or identical duplicate UIDs are suppressed before eligibility filtering");
        Require(At(.96, enemy with { Identity = enemy.Identity with { Id = local.Id } }).CurrentEnemies.Count == 0, "a changed object using the local UID cannot alert");
        foreach (var zone in new[] { 12, 9, 6, 17 })
        {
            var frame = policy.Update(input with { Context = context with { Zone = zone }, Players = [enemy] }, origin.AddSeconds(1));
            Require(frame.CurrentEnemies.Count == 0 && !frame.PlaySound, "non-PvP or unknown zone must suppress enemy alerts");
        }

        policy = new(); At(0);
        var muted = policy.Update(input with { Players = [enemy], Volume = 0 }, origin.AddSeconds(.1));
        Require(muted.Arrivals.Count == 1 && !muted.PlaySound && !At(.2, enemy).PlaySound, "mute consumes arrivals; unmute never replays them");
        Require(!policy.Update(input with { Players = [enemy, second], SoundAllowed = false }, origin.AddSeconds(.3)).PlaySound && !At(.4, enemy, second).PlaySound, "background arrival cannot replay on focus return");
        Require(!policy.Update(input with { Players = [enemy, second, third], SoundEnabled = false }, origin.AddSeconds(.5)).PlaySound && !At(.6, enemy, second, third).PlaySound, "sound-disabled arrivals remain consumed");
        var changedContext = context with { ConnectionKey = "client-2" };
        Require(policy.Update(input with { Context = changedContext, Players = [enemy] }, origin.AddSeconds(.7)).Baseline, "connection identity resets baseline");
        Require(policy.Update(input with { Context = changedContext with { Local = local with { Generation = 2 } }, Players = [enemy] }, origin.AddSeconds(.8)).Baseline, "local character identity resets baseline");
        policy.Update(input with { Enabled = false, Players = [enemy] }, origin.AddSeconds(.9));
        Require(At(1, enemy).Baseline, "enabling after disable establishes a quiet baseline");
        policy.Update(input with { Connected = false, Players = [enemy] }, origin.AddSeconds(1.1));
        Require(At(1.2, enemy).Baseline, "reconnect establishes a quiet baseline");
        policy.Update(input with { Fresh = false, Players = [enemy] }, origin.AddSeconds(1.3));
        Require(At(1.4, enemy).Arrivals.Count == 0, "short stale read retains deduplication");
        policy.Update(input with { LocalAlive = false, Players = [enemy] }, origin.AddSeconds(1.5));
        Require(At(1.6, enemy).Arrivals.Count == 0, "short living-HP gate retains deduplication");
        policy.Update(input with { Fresh = false, Players = [enemy] }, origin.AddSeconds(4.7));
        Require(At(4.8, enemy).Baseline, "long telemetry gap resets to a quiet baseline");
        Require(At(4, enemy).Baseline, "backwards clock establishes a quiet baseline");
        var immutable = At(4.1, enemy, second);
        Require(immutable.CurrentEnemies is System.Collections.ObjectModel.ReadOnlyCollection<SentinelPlayerObservation>, "returned snapshot must be immutable");
        policy = new();
        var small = input with { Range = 1, Players = [enemy with { Distance = 1 }] };
        var smallBaseline = policy.Update(small, origin);
        Require(smallBaseline.Baseline && smallBaseline.CurrentEnemies.Count == 1 && !smallBaseline.PlaySound, "minimum one-unit range includes its exact boundary silently on startup");
        Require(policy.Update(small with { Players = [enemy with { Distance = 1.001 }] }, origin.AddSeconds(.1)).CurrentEnemies.Count == 0, "minimum one-unit range excludes beyond its boundary");
        var large = input with { Range = 100, Players = [enemy with { Distance = 100 }, second with { Distance = 80 }] };
        var expanded = policy.Update(large, origin.AddSeconds(.2));
        Require(expanded.Baseline && expanded.CurrentEnemies.Count == 2 && expanded.Arrivals.Count == 0 && !expanded.PlaySound, "expanding slider to 100 units establishes current players silently");
        Require(policy.Update(large with { Players = [enemy with { Distance = 100.001 }] }, origin.AddSeconds(.3)).CurrentEnemies.Count == 0, "maximum range excludes beyond 100 units");
        Require(policy.Update(small, origin.AddSeconds(.4)).Baseline && !policy.Update(small, origin.AddSeconds(.5)).PlaySound, "range shrink establishes a quiet baseline and consumes existing players");
        Require(SentinelAlertPolicy.NormalizeRange(0) == 1 && SentinelAlertPolicy.NormalizeRange(101) == 100 &&
            SentinelAlertPolicy.NormalizeRange(double.NaN) == 25 && SentinelAlertPolicy.NormalizeRange(double.PositiveInfinity) == 25, "invalid range must clamp or use safe default");
        policy = new(); At(0);
        Require(At(1, enemy).PlaySound, "clock reset fixture must play its first arrival");
        policy.Update(input with { Enabled = false }, origin.AddSeconds(.1));
        Require(At(.2, enemy).Baseline && !At(.3, enemy, second).PlaySound, "disabled-clock rollback preserves a bounded cooldown without startup flood");
        At(2, enemy, second);
        Require(At(3.2, enemy, second, third).PlaySound, "rollback during disable must not suppress sound until the old future clock catches up");
        CheckWave(); CheckPlayback().GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "sentinel-alert-checks.json"), JsonSerializer.Serialize(new
        {
            Passed = true, HardwareInputEmitted = false, AudioPlayed = false,
            Checks = new[] { "quiet startup and reconnect baseline", "default 25-unit entry / 28-unit exit hysteresis", "adjustable 1-100-unit range boundaries and silent slider rebaseline", "two-second missing/exit grace", "ID/generation/address/model replacement", "party/same-faction/dead/unknown/ambiguous exclusions", "actual verified PvP zone gate", "coalesced arrivals and three-second sound spacing", "mute/background/sound-disabled arrivals consumed", "short stale/dead gate deduplication", "long stale and active/disabled clock rollback quiet baseline", "immutable output", "paired PCM chirps with bounded amplitude/silence", "fake-backend nonblocking/no-overlap/cancel/dispose/error", "injected transient/permanent native cleanup and sticky fault" }
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    static void CheckWave()
    {
        byte[] quiet = SentinelSonarWave.Create(0), full = SentinelSonarWave.Create(100), half = SentinelSonarWave.Create(50);
        if (!full.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !full.AsSpan(8, 8).SequenceEqual("WAVEfmt "u8) ||
            BitConverter.ToInt32(full, 4) != full.Length - 8 || BitConverter.ToUInt16(full, 20) != 1 || BitConverter.ToUInt16(full, 22) != 1 ||
            BitConverter.ToInt32(full, 24) != SentinelSonarWave.SampleRate || BitConverter.ToUInt16(full, 34) != 16 ||
            BitConverter.ToInt32(full, 40) != full.Length - 44 || full.Length != 44 + SentinelSonarWave.SampleRate * SentinelSonarWave.DurationMilliseconds / 1000 * 2)
            throw new Exception("Sentinel PCM wave format/duration failed.");
        int first = 0, second = 0, peak = 0;
        for (int offset = 44; offset < full.Length; offset += 2)
        {
            int value = BitConverter.ToInt16(full, offset), index = (offset - 44) / 2;
            double time = index / (double)SentinelSonarWave.SampleRate;
            bool ping = time >= .03 && time < .25 || time >= .37 && time < .59;
            if (BitConverter.ToInt16(quiet, offset) != 0 || !ping && value != 0 || Math.Abs(BitConverter.ToInt16(half, offset) - value / 2.0) > 1)
                throw new Exception("Sentinel waveform mute, gain or inter-ping silence failed.");
            peak = Math.Max(peak, Math.Abs(value));
            if (value != 0) { if (time < .3) first++; else second++; }
        }
        if (first < 4500 || second < 4500 || peak > Math.Ceiling(short.MaxValue * SentinelSonarWave.MaximumAmplitude) ||
            !SentinelSonarWave.Create(-1).SequenceEqual(quiet) || !SentinelSonarWave.Create(101).SequenceEqual(full))
            throw new Exception("Sentinel paired ping bounds failed.");
        double Frequency(double from, double until)
        {
            var crossings = new List<int>();
            for (int index = (int)(from * SentinelSonarWave.SampleRate) + 1; index < until * SentinelSonarWave.SampleRate; index++)
                if (BitConverter.ToInt16(full, 44 + (index - 1) * 2) <= 0 && BitConverter.ToInt16(full, 44 + index * 2) > 0) crossings.Add(index);
            return crossings.Count < 2 ? 0 : SentinelSonarWave.SampleRate * (crossings.Count - 1.0) / (crossings[^1] - crossings[0]);
        }
        double early = Frequency(.07, .12), late = Frequency(.18, .23), secondEarly = Frequency(.41, .46);
        if (early < 720 || early > 770 || late < 655 || late > 710 || late >= early || Math.Abs(early - secondEarly) > 4)
            throw new Exception("Sentinel chirps must descend in frequency and repeat as the selected sonar preview.");
    }

    static async Task CheckPlayback()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int began = 0, released = 0;
        using var audio = new SentinelSonar(async (_, cancellation) =>
        {
            Interlocked.Increment(ref began); entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, cancellation); }
            finally { Interlocked.Increment(ref released); cancelled.TrySetResult(); }
        });
        if (audio.TryPlay(0) || !audio.TryPlay(45) || audio.TryPlay(100)) throw new Exception("Sentinel playback mute/no-overlap failed.");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        audio.Stop(); await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        for (int step = 0; audio.Playing && step < 100; step++) await Task.Delay(5);
        if (audio.Playing || began != 1 || released != 1 || !audio.TryPlay(45)) throw new Exception("Sentinel Stop did not release and re-arm its fake backend.");
        audio.Dispose();
        for (int step = 0; audio.Playing && step < 100; step++) await Task.Delay(5);
        if (audio.TryPlay(45) || audio.Playing || began != released) throw new Exception("Sentinel playback accepted audio or retained its fake backend after disposal.");
        using var broken = new SentinelSonar((_, _) => Task.FromException(new IOException("Synthetic audio failure")));
        if (!broken.TryPlay(45)) throw new Exception("Sentinel fake error backend did not start.");
        for (int step = 0; broken.Playing && step < 100; step++) await Task.Delay(5);
        if (broken.Playing || broken.LastError?.Contains("Synthetic audio failure") != true) throw new Exception("Sentinel audio error was not retained without throwing on the UI.");
        int resetAttempts = 0, closeAttempts = 0, unprepareAttempts = 0;
        var recovered = await SentinelSonar.CleanupNative(true, () => { resetAttempts++; return 0; },
            () => ++unprepareAttempts < 2 ? 33u : 0, () => ++closeAttempts < 2 ? 33u : 0);
        if (!recovered.BufferReleased || !recovered.DeviceClosed || resetAttempts != 2 || closeAttempts != 2)
            throw new Exception("Sentinel transient driver cleanup did not reset/unprepare/close again.");
        closeAttempts = 0;
        var failed = await SentinelSonar.CleanupNative(true, () => 0, () => 33, () => { closeAttempts++; return 33; });
        if (failed.BufferReleased || failed.DeviceClosed || closeAttempts != 20)
            throw new Exception("Sentinel unreleased driver buffer must retain ownership after bounded cleanup attempts.");
        closeAttempts = 0;
        var closedDespiteUnprepare = await SentinelSonar.CleanupNative(true, () => 0, () => 1, () => { closeAttempts++; return 0; });
        if (!closedDespiteUnprepare.BufferReleased || !closedDespiteUnprepare.DeviceClosed || closeAttempts != 1)
            throw new Exception("Sentinel cleanup must still close a driver after unprepare failed.");
        using var sticky = new SentinelSonar((_, _) => Task.FromException(new SentinelAudioCleanupException("Synthetic unreleased native buffer")));
        if (!sticky.TryPlay(45)) throw new Exception("Sentinel cleanup fault backend did not start.");
        for (int step = 0; sticky.Playing && step < 100; step++) await Task.Delay(5);
        if (!sticky.Faulted || sticky.Playing || sticky.TryPlay(45) || sticky.LastError?.Contains("Synthetic unreleased native buffer") != true)
            throw new Exception("Sentinel permanent cleanup failure must disable subsequent allocations and retain its error.");
    }
}
