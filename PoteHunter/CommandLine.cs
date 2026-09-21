using System.Runtime.InteropServices;
using System.Text.Json;

namespace PoteHunter;

static class CommandLine
{
    public static int? Run(string[] args)
    {
        if(args.Contains("--potion-catalog"))
        {
            try
            {
                using var world = new World(); world.Connect();
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"potion-catalog.json"),
                    JsonSerializer.Serialize(new {Items=world.PotionCatalog(), Effects=world.ActiveEffects()}, new JsonSerializerOptions{WriteIndented=true}));
                return 0;
            }
            catch(Exception ex)
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"potion-catalog-error.txt"),ex.Message);
                return 1;
            }
        }
        if(args.Contains("--survey-loaded-range"))return RangeSurvey.Run();
        if(args.Contains("--capture-target-state"))return TargetStateDiscovery.RunCommand();
        if(args.Contains("--capture-internal-target-state"))return TargetStateDiscovery.RunProcessWideCommand();
        if(args.Contains("--observe-internal-target-state"))return TargetStateDiscovery.ObserveProcessWideCommand();
        if(args.Contains("--verify-target-state"))return TargetStateDiscovery.VerifyCommand();
        if(args.Contains("--test-internal-target"))return InternalTargetSelectionProbe.Run();
        if(args.Contains("--test-internal-target-candidates"))return InternalTargetSelectionProbe.RunCandidateTest();
        if(args.Contains("--check-patch-compatibility"))return CompatibilityChecks.CheckPatchedFile(PoteMemoryProbe.Program.ClientPath);
        if(args.Contains("--ranged-ui-check"))return HunterForm.CheckRangedPullUi();
        if (args.Contains("--input-compat-test"))
        {
            if (args.Length != 3 || !args.Contains("--native-read-compat") || !args.Contains("--native-input-compat")) return 2;
            PoteMemoryProbe.WindowsClientRead.Enabled = true;
            WindowsClientInput.Enabled = true;
            return InputCompatibilityTest.Run().GetAwaiter().GetResult();
        }
        PoteMemoryProbe.WindowsClientRead.Enabled = args.Contains("--native-read-compat") && args.Contains("--connection-test");
        if (args.Contains("--check-game-window"))
        {
            // Inspect window metadata only; do not open a game-memory reader or send input.
            PoteMemoryProbe.WindowsClientRead.Enabled = false;
            try
            {
                ApplicationConfiguration.Initialize();
                // Match the GUI's startup delay so a transient successful lookup
                // immediately after process creation cannot hide a later failure.
                Thread.Sleep(4000);
                var games = System.Diagnostics.Process.GetProcessesByName("Client");
                try
                {
                    var checks = new List<GameWindow.Report[]>();
                    var identities = new Dictionary<int, GameWindow.Candidate>();
                    var inputChecks = new List<GameWindow.InputState>();
                    for (int check = 0; check < 3; check++)
                    {
                        if (check > 0) Thread.Sleep(6000);
                        var reports = new List<GameWindow.Report>();
                        foreach (var game in games)
                        {
                            using var identity = PoteMemoryProbe.Native.OpenProcess(0x1000, false, game.Id);
                            if (!identity.IsInvalid && PoteMemoryProbe.Native.PathOf(identity).Equals(PoteMemoryProbe.Program.ClientPath, StringComparison.OrdinalIgnoreCase))
                            {
                                var report = GameWindow.Inspect(game, verifyThreads: true);
                                reports.Add(report);
                                if (report.SelectedHandle != 0)
                                {
                                    if (!identities.ContainsKey(game.Id)) identities[game.Id] = report.Candidates.Single(c => c.Handle == report.SelectedHandle);
                                    inputChecks.Add(GameWindow.CheckInput(identities[game.Id], game.Id, World.IsProcessAlive(identity)));
                                }
                            }
                        }
                        checks.Add(reports.ToArray());
                    }
                    bool passed = checks.All(reports => reports.Length == 1 && reports[0].SelectedHandle != 0 && reports[0].Candidates.Any(c => c.Handle == reports[0].SelectedHandle && c.VerifiedThreadId != 0)) && inputChecks.Count == 3 && inputChecks.All(s => s.ProcessAlive && s.OwnershipVerified);
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "game-window-check.json"), System.Text.Json.JsonSerializer.Serialize(new { TimeUtc = DateTime.UtcNow, ReaderPid = Environment.ProcessId, Passed = passed, Checks = checks, InputChecks = inputChecks }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                    return passed ? 0 : 1;
                }
                finally { foreach (var game in games) game.Dispose(); }
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "game-window-check.json"), System.Text.Json.JsonSerializer.Serialize(new { TimeUtc = DateTime.UtcNow, ReaderPid = Environment.ProcessId, Passed = false, Error = ex.ToString() }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                return 1;
            }
        }
        if (args.Contains("--connection-test")) return ConnectionTest.Run(args);
        if (args.Contains("--inspect-effects"))
        {
            try { using var world = new World(); world.Connect(); var bar = world.Hotbar(); var effects = world.ActiveEffects(); File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "live-effects-check.json"), System.Text.Json.JsonSerializer.Serialize(new { TimeUtc = DateTime.UtcNow, world.Pid, world.ClientHash, Player = world.LocalPlayer().Name, world.ActiveEffectsSupported, SelectedSkill = world.SelectedSkill(), Effects = effects, Hotbar = bar, Decisions = new LiveBuffUpkeep().Evaluate("read-only", bar, effects, true, 0) }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); return world.ActiveEffectsSupported ? 0 : 1; }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "live-effects-error.txt"), ex.ToString()); return 1; }
        }
        if (args.Contains("--inspect-party"))
        {
            try { using var world = new World(); world.Connect(); File.WriteAllBytes(Path.Combine(AppContext.BaseDirectory, "party-scene.bin"), world.ObservePartyScene()); File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "party-live-check.json"), System.Text.Json.JsonSerializer.Serialize(world.Party(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); return 0; }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "party-inspection-error.txt"), ex.ToString()); return 1; }
        }
        if (args.Contains("--observe-group"))
        {
            try
            {
                using var world = new World(); world.Connect();
                using var writer = new StreamWriter(Path.Combine(AppContext.BaseDirectory, "group-observations.jsonl")) { AutoFlush = true };
                for (int i = 0; i < 1800; i++) { writer.WriteLine(System.Text.Json.JsonSerializer.Serialize(world.ObserveGroup())); Thread.Sleep(100); }
                return 0;
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "group-observation-error.txt"), ex.ToString()); return 1; }
        }
        if (args.Contains("--check-rest-observation"))
        {
            try { RestToggle.CheckRecorded(args[Array.IndexOf(args, "--check-rest-observation") + 1]); return 0; }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "rest-recorded-checks.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = false, Error = ex.Message })); return 1; }
        }
        if (args.Contains("--verify-rest"))
        {
            try
            {
                using var world = new World(); world.Connect(); var state = world.RestState();
                bool passed = world.RestSupported && state.Posture != RestPosture.Unknown;
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "rest-live-check.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = passed, TimeUtc = DateTime.UtcNow, world.Pid, world.ClientHash, world.RestSupported, Posture = state.Posture.ToString(), state.Flag, state.Animation }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                return passed ? 0 : 1;
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "rest-live-check.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = false, Error = ex.Message })); return 1; }
        }
        if (args.Contains("--observe-rest"))
        {
            try
            {
                using var world = new World(); world.Connect();
                using var writer = new StreamWriter(Path.Combine(AppContext.BaseDirectory, "rest-observations.jsonl")) { AutoFlush = true };
                for (int i = 0; i < 1800; i++) { writer.WriteLine(System.Text.Json.JsonSerializer.Serialize(world.ObserveRest())); Thread.Sleep(100); }
                return 0;
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "rest-observation-error.txt"), ex.ToString()); return 1; }
        }
        if (args.Contains("--bundle-info"))
        {
            // An empty core-library location is intentional evidence that it came from the single-file bundle.
#pragma warning disable IL3000
            string coreLibrary = typeof(object).Assembly.Location;
#pragma warning restore IL3000
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "bundle-info.json"), System.Text.Json.JsonSerializer.Serialize(new { Executable = Environment.ProcessPath, BaseDirectory = AppContext.BaseDirectory, Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, CoreLibraryPath = coreLibrary, BundledRuntime = coreLibrary.Length == 0 || coreLibrary.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase), Distribution = coreLibrary.Length == 0 ? "single file" : "runtime folder", Resources = typeof(Entry).Assembly.GetManifestResourceNames() }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); return 0;
        }
        if (args.Contains("--check-discovery"))
        {
            try { int index = Array.IndexOf(args, "--check-discovery"); if (index + 2 >= args.Length) throw new Exception("Supply the known and updated client snapshots."); var result = ProfileDiscovery.CheckSnapshots(args[index + 1], args[index + 2]); File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "profile-discovery-checks.json"), System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); return 0; }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "profile-discovery-checks.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = false, Error = ex.Message })); return 1; }
        }
        if (args.Contains("--discover-profile"))
        {
            try
            {
                int index = Array.IndexOf(args, "--discover-profile"); string path = index + 1 < args.Length ? args[index + 1] : PoteMemoryProbe.Program.ClientPath;
                var result = ProfileDiscovery.Discover(File.ReadAllBytes(path));
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "profile-discovery.json"), System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); return 0;
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "profile-discovery.json"), System.Text.Json.JsonSerializer.Serialize(new { Error = ex.Message })); return 1; }
        }
        if (args.Contains("--verify-profile"))
        {
            try { using var world = new World(); world.Connect(); File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "live-profile-validation.json"), System.Text.Json.JsonSerializer.Serialize(new { TimeUtc = DateTime.UtcNow, world.Pid, world.AutomaticProfile, world.ProfileStatus, Player = world.LocalPlayer(), Health = world.TargetHealth(world.LocalPlayer().Id), Level = world.PlayerLevel(), Hotbar = world.Hotbar(), Loaded = world.Poll().Count, GroundLoot = world.Loot().Count }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); return 0; }
            catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "live-profile-validation.json"), System.Text.Json.JsonSerializer.Serialize(new { Error = ex.Message })); return 1; }
        }
        if (args.Contains("--calibration-test")) return CalibrationTest().GetAwaiter().GetResult();
        if (args.Contains("--observe-combat")) return ObserveCombat().GetAwaiter().GetResult();
        if (args.Contains("--aim-probe")) return AimProbe().GetAwaiter().GetResult();
        if (args.Contains("--observe-loot")) return ObserveLoot().GetAwaiter().GetResult();
        if (args.Contains("--hotbar-test")) return HotbarTest().GetAwaiter().GetResult();
        if (args.Contains("--background-probe")) return BackgroundProbe().GetAwaiter().GetResult();
        if (args.Contains("--background-turn-probe")) return BackgroundTurnProbe().GetAwaiter().GetResult();
        if (args.Contains("--focused-move-test")) return FocusedMoveTest().GetAwaiter().GetResult();
        if (args.Contains("--item-descriptions"))
        {
            try
            {
                using var world = new World(); world.Connect();
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "item-records.json"), System.Text.Json.JsonSerializer.Serialize(new[] { 3020, 3028, 8004, 5001 }.Select(id => new { Id = id, Record = world.ItemRecord(id) })));
                return 0;
            }
            catch (Exception ex) { TraceLog.Record("item description probe failed", new { Error = ex.Message }); return 1; }
        }
        if (args.Contains("--level-test"))
        {
            using var world = new World(); world.Connect(); var hp = world.HealthSnapshot(); TraceLog.Record("level reading", new { Player = world.PlayerName, LocalUID = world.LocalPlayer().Id, LocalHealth = hp.GetValueOrDefault(world.LocalPlayer().Id), Level = world.PlayerLevel(), Position = world.PlayerPosition(), Villager = world.Definition(1), VillagerColor = world.Definition(1).Difficulty(world.PlayerLevel()).ToString(), Near = world.Poll().Where(e => e.Monster).OrderBy(e => (e.Position - world.PlayerPosition()).Length).Take(5).Select(e => new { e.Name, e.Id, e.Position, Health = hp.GetValueOrDefault(e.Id) }), GroundLoot = world.Loot() }); return 0;
        }
        if (args.Contains("--self-test")) return SelfTests.Run();
        return null;
    }

    static async Task<int> CalibrationTest()
    {
        using var world = new World();
        using var token = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        try
        {
            world.Connect();
            Input.Allowed = () => Input.GetForegroundWindow() == world.Window && !Input.IsIconic(world.Window);
            TraceLog.Record("waiting for game", new { Window = world.Window.ToInt64() });
            while (Input.GetForegroundWindow() != world.Window)
            {
                if (Input.Down(Keys.F9)) throw new OperationCanceledException("Stopped by F9.");
                await Task.Delay(100, token.Token);
            }
            await Input.Delay(500, token.Token);
            var movement = await Movement.Calibrate(world, token.Token, TraceLog.Record);
            TraceLog.Record("calibration passed", new { movement.Forward, movement.UnitsPerMs, movement.RadiansPerPixel }); return 0;
        }
        catch (Exception ex) { TraceLog.Record("calibration failed", new { Error = ex.Message }); return 1; }
        finally { Input.Release(); }
    }
    static async Task<int> ObserveCombat()
    {
        using var world = new World();
        try
        {
            world.Connect();
            Vec player = world.PlayerPosition();
            var targets = world.Poll().Where(e => e.Monster && e.Name.Contains("Ichman Villager") && (e.Position - player).Length < 30).OrderBy(e => (e.Position - player).Length).Take(12).ToArray();
            string file = Path.Combine(AppContext.BaseDirectory, "death-samples.jsonl");
            var deadline = DateTime.UtcNow.AddSeconds(50);
            File.AppendAllText(file, System.Text.Json.JsonSerializer.Serialize(new { Started = DateTime.UtcNow, Targets = targets }) + Environment.NewLine);
            while (DateTime.UtcNow < deadline)
            {
                var samples = targets.Select(e => new { e.Id, e.Name, Bytes = world.ObserveCreature(e) }).ToArray();
                File.AppendAllText(file, System.Text.Json.JsonSerializer.Serialize(new { TimeUtc = DateTime.UtcNow, Samples = samples }) + Environment.NewLine);
                await Task.Delay(100);
            }
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "observer-error.txt"), ex.Message); return 1; }
    }
    static async Task<int> AimProbe()
    {
        using var world = new World(); using var token = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        try
        {
            world.Connect(); Input.Allowed = () => Input.GetForegroundWindow() == world.Window && !Input.IsIconic(world.Window);
            TraceLog.Record("aim probe waiting", new { Player = world.PlayerName });
            while (!Input.Allowed()) await Task.Delay(100, token.Token);
            await Input.Delay(300, token.Token);
            TraceLog.Record("aim probe before", new { RawHeading = world.PlayerHeading(), Cursor = Input.Cursor(), Player = world.PlayerPosition() });
            Input.Turn(60, token.Token); await Input.Delay(200, token.Token);
            TraceLog.Record("aim probe turned", new { RawHeading = world.PlayerHeading(), Cursor = Input.Cursor(), Player = world.PlayerPosition() });
            Input.Turn(-60, token.Token); await Input.Delay(200, token.Token);
            TraceLog.Record("aim probe restored", new { RawHeading = world.PlayerHeading(), Cursor = Input.Cursor(), Player = world.PlayerPosition() }); return 0;
        }
        catch (Exception ex) { TraceLog.Record("aim probe failed", new { Error = ex.Message }); return 1; }
        finally { Input.Release(); }
    }
    static async Task<int> ObserveLoot()
    {
        using var world = new World();
        try
        {
            world.Connect();
            var previousData = world.ObserveWorldData(); var previousActive = world.ActiveIds().ToHashSet();
            TraceLog.Record("loot observation ready", new { Player = world.PlayerPosition(), DataIds = previousData.Keys, ActiveIds = previousActive });
            var until = DateTime.UtcNow.AddSeconds(120);
            while (DateTime.UtcNow < until)
            {
                var data = world.ObserveWorldData(); var active = world.ActiveIds().ToHashSet();
                var added = data.Where(kv => !previousData.ContainsKey(kv.Key)).Select(kv => new { Id = kv.Key, Record = kv.Value }).ToArray();
                var removed = previousData.Keys.Where(k => !data.ContainsKey(k)).ToArray();
                var addedActive = active.Except(previousActive).ToArray(); var removedActive = previousActive.Except(active).ToArray();
                if (added.Length + removed.Length + addedActive.Length + removedActive.Length > 0)
                    TraceLog.Record("world delta", new { Player = world.PlayerPosition(), AddedData = added, RemovedData = removed, AddedActive = addedActive, RemovedActive = removedActive });
                previousData = data; previousActive = active;
                await Task.Delay(100);
            }
            TraceLog.Record("loot observation ended", new { }); return 0;
        }
        catch (Exception ex) { TraceLog.Record("loot observation failed", new { Error = ex.Message }); return 1; }
    }
    static async Task<int> HotbarTest()
    {
        using var world = new World();
        try
        {
            world.Connect();
            for (int i = 0; i < 4; i++) { TraceLog.Record("hotbar sample", world.Hotbar()); if (i < 3) await Task.Delay(1000); }
            return 0;
        }
        catch (Exception ex) { TraceLog.Record("hotbar test failed", new { Error = ex.Message }); return 1; }
    }
    static async Task<int> BackgroundProbe()
    {
        using var world = new World();
        Keys? heldKey = null;
        try
        {
            world.Connect();
            if (Input.GetForegroundWindow() == world.Window) throw new InvalidOperationException("Keep another app in front for the background-only test.");
            var bar = world.Hotbar(); ushort selectedBefore = world.SelectedSkill();
            var testSlot = bar.Slots.First(s => s.Kind == SlotKind.Skill && s.Id != selectedBefore);
            var originalSlot = bar.Slots.FirstOrDefault(s => s.Kind == SlotKind.Skill && s.Id == selectedBefore);
            Input.PostKey(world.Window, world.Pid, (Keys)testSlot.Key[0], false); heldKey = (Keys)testSlot.Key[0];
            await Task.Delay(80); Input.PostKey(world.Window, world.Pid, heldKey.Value, true); heldKey = null; await Task.Delay(350);
            ushort selectedAfter = world.SelectedSkill();
            if (selectedAfter != selectedBefore && originalSlot != null)
            {
                Input.PostKey(world.Window, world.Pid, (Keys)originalSlot.Key[0], false); heldKey = (Keys)originalSlot.Key[0];
                await Task.Delay(80); Input.PostKey(world.Window, world.Pid, heldKey.Value, true); heldKey = null;
            }
            if (Input.GetForegroundWindow() == world.Window) throw new InvalidOperationException("The game gained focus, so the test is inconclusive.");
            Vec first = world.PlayerPosition(); await Task.Delay(200); Vec before = world.PlayerPosition();
            if ((before - first).Length > .05) throw new InvalidOperationException("Character was already moving, so background movement cannot be isolated.");
            Input.PostKey(world.Window, world.Pid, Keys.W, false); heldKey = Keys.W; await Task.Delay(250);
            Input.PostKey(world.Window, world.Pid, Keys.W, true); heldKey = null; await Task.Delay(250);
            var after = world.PlayerPosition();
            if (Input.GetForegroundWindow() == world.Window) throw new InvalidOperationException("The game gained focus, so the test is inconclusive.");
            TraceLog.Record("background window-message probe", new { RequestedSlot = testSlot.Key, RequestedSkillId = testSlot.Id, SelectedBefore = selectedBefore, SelectedAfter = selectedAfter, SelectionChanged = selectedAfter == testSlot.Id, Before = before, After = after, DistanceMoved = (after - before).Length, GameStayedUnfocused = true });
            return 0;
        }
        catch (Exception ex) { TraceLog.Record("background probe failed", new { Error = ex.Message }); return 1; }
        finally { if (heldKey.HasValue) { try { Input.PostKey(world.Window, world.Pid, heldKey.Value, true); } catch { } } }
    }
    static async Task<int> BackgroundTurnProbe()
    {
        using var world = new World();
        world.Connect();
        var character = world.LocalPlayer();
        if (Input.GetForegroundWindow() == world.Window) throw new InvalidOperationException("Keep another app in front for the background turn test.");
        if (Input.IsIconic(world.Window)) throw new InvalidOperationException("Restore the game window (it may stay unfocused) before the background turn test.");
        if (!GetClientRect(world.Window, out var rect)) throw new InvalidOperationException("The game client rectangle could not be read.");
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        TraceLog.Record("background turn probe", new { Stage = "client rect", Window = world.Window.ToInt64(), Rect = new { rect.Left, rect.Top, rect.Right, rect.Bottom } });
        int startX = width / 2, startY = height / 2;
        if (startX < 4 || startY < 4) throw new InvalidOperationException("The game window is too small for the background turn test.");
        var positions = new List<Vec> { world.PlayerPosition() };
        for (int i = 0; i < 10; i++) { await Task.Delay(80); positions.Add(world.PlayerPosition()); }
        if (positions.Max(p => (p - positions[0]).Length) > .05) throw new InvalidOperationException("The character is moving; stand still for the background turn test.");
        if (Input.GetForegroundWindow() == world.Window) throw new InvalidOperationException("The game gained focus, so the test is inconclusive.");
        double baseline = world.PlayerHeading();
        TraceLog.Record("background turn probe", new { Stage = "setup", Player = character.Name, Window = world.Window.ToInt64(), ClientSize = new { width, height }, Start = new { startX, startY }, BaselineHeading = baseline, GameUnfocused = Input.GetForegroundWindow() != world.Window });

        double WrapAngle(double a) => Math.Atan2(Math.Sin(a), Math.Cos(a));
        List<object> results = new();
        bool focusContaminated = false;
        bool anyWorks = false;
        var savedAllowed = Input.Allowed;
        Input.Allowed = () => true;

        async Task<(double Heading, bool FocusLost)> MoveAndRead(Action<int, int> send, int x, int y, double base0)
        {
            send(x, y);
            bool focusLost = true;
            double heading = base0;
            for (int i = 0; i < 24; i++)
            {
                await Task.Delay(40);
                if (Input.GetForegroundWindow() == world.Window) { focusContaminated = true; return (base0, true); }
                heading = world.PlayerHeading();
                if (Math.Abs(WrapAngle(heading - base0)) >= .01) { focusLost = false; break; }
            }
            return (heading, focusLost);
        }

        bool Works(double base0, double turn, double returned, bool focusLost) =>
            Math.Abs(WrapAngle(turn - base0)) >= .02 &&
            Math.Abs(WrapAngle(returned - turn)) >= .015 &&
            Math.Abs(WrapAngle(returned - turn)) <= Math.Max(.16, Math.Abs(WrapAngle(turn - base0)) * .75) && !focusLost;

        async Task TestRoundTrip(string mechanism, Action<int, int> send, int dx)
        {
            if (focusContaminated)
            {
                results.Add(new { Mechanism = mechanism, Works = false, Note = "Skipped: the game took foreground during an earlier test." });
                return;
            }
            await Task.Delay(250);
            double base0 = world.PlayerHeading();
            (double Heading, bool FocusLost) first = await MoveAndRead(send, dx, 0, base0);
            (double Heading, bool FocusLost) second = await MoveAndRead(send, -dx, 0, base0);
            bool works = Works(base0, first.Heading, second.Heading, first.FocusLost || second.FocusLost);
            if (works) anyWorks = true;
            results.Add(new { Mechanism = mechanism, PlusChange = WrapAngle(first.Heading - base0), ReturnChange = WrapAngle(second.Heading - first.Heading), FocusLost = first.FocusLost || second.FocusLost, Works = works });
            TraceLog.Record("background turn probe", new { Stage = mechanism, Results = results });
        }

        await TestRoundTrip("posted WM_MOUSEMOVE relative deltas (PostMessage)", (x, y) => Input.PostMove(world.Window, world.Pid, x, y), +60);
        await TestRoundTrip("posted WM_MOUSEMOVE absolute client coordinates (PostMessage)", (x, y) => Input.PostMove(world.Window, world.Pid, startX + x, startY), +60);
        await TestRoundTrip("synchronous SendMessage WM_MOUSEMOVE relative deltas", (x, y) => Input.SendMove(world.Window, world.Pid, x, y), +60);

        if (!focusContaminated)
        {
            double before = world.PlayerHeading();
            Input.Turn(+60, CancellationToken.None);
            await Task.Delay(300);
            double mid = world.PlayerHeading();
            Input.Turn(-60, CancellationToken.None);
            await Task.Delay(300);
            double after = world.PlayerHeading();
            bool focusLost = Input.GetForegroundWindow() == world.Window;
            bool works = Math.Abs(WrapAngle(mid - before)) >= .02 && Math.Abs(WrapAngle(after - mid)) <= Math.Max(.16, Math.Abs(WrapAngle(mid - before)) * .75) && !focusLost;
            if (works) anyWorks = true;
            results.Add(new { Mechanism = "SendInput relative move while unfocused", PlusChange = WrapAngle(mid - before), ReturnChange = WrapAngle(after - mid), FocusLost = focusLost, Works = works });
            TraceLog.Record("background turn probe", new { Stage = "send input while unfocused", Results = results });
        }

        Input.Allowed = savedAllowed;
        string verdict = anyWorks ? "BACKGROUND_TURN_POSSIBLE" : "NO_BACKGROUND_TURN";
        var report = new { TimeUtc = DateTime.UtcNow, Window = world.Window.ToInt64(), Player = character.Name, Verdict = verdict, FocusContaminated = focusContaminated, Results = results, Note = "Heading read from client memory; the game window must stay unfocused for a passing result." };
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "background-turn-probe.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        TraceLog.Record("background turn probe complete", report);
        return verdict == "BACKGROUND_TURN_POSSIBLE" ? 0 : 3;
    }

    static async Task<int> FocusedMoveTest()
    {
        using var world = new World();
        world.Connect();
        var character = world.LocalPlayer();
        if (!Input.SetForegroundWindow(world.Window)) throw new InvalidOperationException("Could not focus the game window for the control test.");
        await Task.Delay(400);
        if (Input.GetForegroundWindow() != world.Window) throw new InvalidOperationException("The game window did not take the foreground.");
        var positions = new List<Vec> { world.PlayerPosition() };
        for (int i = 0; i < 10; i++) { await Task.Delay(80); positions.Add(world.PlayerPosition()); }
        if (positions.Max(p => (p - positions[0]).Length) > .05) throw new InvalidOperationException("The character is moving; stand still for the focused move test.");
        double WrapAngle(double a) => Math.Atan2(Math.Sin(a), Math.Cos(a));
        double baseline = world.PlayerHeading();
        List<object> results = new();
        async Task<(double Heading, bool Changed)> MoveAndRead(Action<int, int> send, int x, int y)
        {
            send(x, y);
            bool changed = false;
            double heading = baseline;
            for (int i = 0; i < 24; i++)
            {
                await Task.Delay(40);
                heading = world.PlayerHeading();
                if (Math.Abs(WrapAngle(heading - baseline)) >= .01) { changed = true; break; }
            }
            return (heading, changed);
        }
        (double Heading, bool Changed) plus = await MoveAndRead((x, y) => Input.PostMove(world.Window, world.Pid, x, y), +60, 0);
        (double Heading, bool Changed) minus = await MoveAndRead((x, y) => Input.PostMove(world.Window, world.Pid, x, y), -60, 0);
        bool postedWorks = plus.Changed && Math.Abs(WrapAngle(minus.Heading - plus.Heading)) <= Math.Max(.16, Math.Abs(WrapAngle(plus.Heading - baseline)) * .75);
        results.Add(new { Mechanism = "posted WM_MOUSEMOVE relative deltas while FOCUSED", PlusChange = WrapAngle(plus.Heading - baseline), ReturnChange = WrapAngle(minus.Heading - plus.Heading), Works = postedWorks });
        Input.Release();
        var report = new { TimeUtc = DateTime.UtcNow, Window = world.Window.ToInt64(), Player = character.Name, Results = results, Note = "Control test: same posted moves while the game owns the foreground." };
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "focused-move-test.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return postedWorks ? 0 : 3;
    }

    [StructLayout(LayoutKind.Sequential)] struct ProbeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetClientRect(nint window, out ProbeRect rect);
}
