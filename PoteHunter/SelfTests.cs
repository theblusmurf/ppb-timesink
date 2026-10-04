using System.Text.Json;

namespace PoteHunter;

static class SelfTests
{
    public static int Run()
    {
        try
        {
            HuntingSessionLogChecks.Run();
            ClientRecoveryChecks.Run();
            PotionChecks.Run();
            GroupHealerChecks.Run();
            ZoneMapBackground.SelfTest();
            GameMapChecks.Run();
            MapSceneReaderChecks.Run();
            MapObjectReaderChecks.Run();
            SkillHealthRuleChecks.Run(); SkillTargeting.SelfTest();
            GameWindow.SelfTest();
            CompatibilityChecks.Run();
            AppUpdates.Checks();
            if (Math.Abs(Movement.Angle(new Vec(1, 0), new Vec(0, 1)) - Math.PI / 2) > 1e-9) throw new Exception("Quarter-turn angle");
            if (Math.Abs(Movement.Angle(new Vec(0, 1), new Vec(1, 0)) + Math.PI / 2) > 1e-9) throw new Exception("Reverse angle");
            if ((Movement.Rotate(new Vec(1, 0), Math.PI / 2) - new Vec(0, 1)).Length > 1e-9) throw new Exception("Rotation");
            if ((Movement.FromClientHeading(0) - new Vec(0, -1)).Length > 1e-9 || (Movement.FromClientHeading(Math.PI / 2) - new Vec(-1, 0)).Length > 1e-9) throw new Exception("Client heading axes");
            CheckTracking();
            MovementSmoothingChecks.Run().GetAwaiter().GetResult();
            ProfileDiscovery.SelfTest();
            DataRecorder.SelfTest();
            Avoidance.SelfTest();
            CombatCourtesy.SelfTest();
            PlayerRecognitionChecks.Run();
            SentinelAlertChecks.Run();
            SentinelDisplayNumberChecks.Run();
            PlayerGreeting.SelfTest();
            Encounter.SelfTest();
            CombatPressure.SelfTest();
            PauseRecoveryChecks.Run();
            TurnResponse.SelfTest();
            SkillRotation.RetrySelfTest();
            SkillGroupGate.SelfTest();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "pause-recovery-checks.json"), JsonSerializer.Serialize(new
            {
                Passed = true,
                HardwareInputEmitted = false,
                Checks = new[] { "recorded 5482 to 5413 player HP drop", "fresh full-health nearby Mimic defense", "initial low health is not an attack", "stand before resuming defense", "existing engagements retained", "Gamekeeper overrides defense", "confirmed deaths clear defense", "quiet period permits full-health recovery", "unknown, dead, distant, protected and ambiguous candidates excluded", "damage without a candidate exits sitting" }
            }, new JsonSerializerOptions { WriteIndented = true }));
            GamekeeperPriority.SelfTest();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "gamekeeper-override-checks.json"), JsonSerializer.Serialize(new
            {
                Passed = true,
                HardwareInputEmitted = false,
                Checks = new[] { "Gamekeeper before existing engagements", "unfinished fights preserved for afterward", "priority independent of low player health", "sitting interrupts and stands before full health", "disabled priority preserves rest", "normal recovery resumes after priority and engagements", "configured response radius", "retained Gamekeeper completion allowance", "fresh Gamekeepers excluded outside response radius", "dead and unknown targets excluded", "exact Gamekeeper identity and model" }
            }, new JsonSerializerOptions { WriteIndented = true }));
            Targeting.CompletionSelfTest();
            RangedPullChecks.Run();
            Aim3DChecks.RunAll();
            AimResponsivenessChecks.RunAll();
            AimSmoothingChecks.RunAll();
            CameraCalibrationChecks.RunAll();
            CameraAimPrecisionChecks.RunAll();
            var savedAim=new SavedRangedAimCalibration(DateTime.UnixEpoch,"client-hash","Boombastic",new RangedAimCalibration(-.0032,.0033,-.0035));
            var restoredAim=JsonSerializer.Deserialize<SavedRangedAimCalibration>(JsonSerializer.Serialize(savedAim));
            if(!savedAim.Calibration.Valid || new RangedAimCalibration(0,.0033,-.0035).Valid || restoredAim!=savedAim)
                throw new Exception("Saved 3D aim sensitivity validation or persistence failed.");
            TargetStateDiscoveryChecks.RunAll();
            RangedTargetVisibilityChecks.RunAll();
            ManaRecoveryChecks.Run();
            var reserveSkill=new HotbarSlot("1",SlotKind.Skill,1,"Strike",0,0,false,0,SkillUse:SkillUseKind.Instance,ManaCost:20);
            if(!ManaReserveRule.Allows(reserveSkill,new(true,50,100),30)||ManaReserveRule.Allows(reserveSkill,new(true,49,100),30)||
                !ManaReserveRule.Allows(reserveSkill,default,0)||ManaReserveRule.Allows(reserveSkill,default,30))
                throw new Exception("Mana reserve did not preserve its post-cast boundary or disabled state.");
            Input.CheckHeldRangedFire().GetAwaiter().GetResult();
            TargetSearch.SelfTest();
            TargetTraversalChecks.Run();
            HuntingArea.SelfTest();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "home-excursion-checks.json"), JsonSerializer.Serialize(new
            {
                Passed = true,
                HardwareInputEmitted = false,
                Checks = new[] { "direct traversal through excluded monsters", "explicit avoidance and observed walls retained", "inside approved target outranks outside Gamekeeper", "unknown or protected inside target blocks departure", "nearest outside fallback within double home radius", "engaged adds before return", "return inside original area before fresh selection", "inside target priority restored after return", "immutable original hunt center", "target wait reasons distinguish range, HP and protection" }
            }, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "fight-continuation-checks.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                Passed = true,
                HardwareInputEmitted = false,
                Checks = new[] { "engaged sidestep beyond fresh boundary retained", "fresh target outside hunt radius excluded", "completion pursuit bounded", "return steps allowed and outward steps blocked", "collateral survives Gamekeeper death in shared completion area" }
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "engagement-checks.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                Passed = true,
                HardwareInputEmitted = false,
                Checks = new[] { "two engaged enemies before fresh priority target", "off-filter collateral damage", "recorded side hit beyond three units survives primary death", "held combos refresh damage evidence during charged skills", "collateral reach independent of nearby clearing radius", "moving entrants use current positions", "final hit after release retained", "pre-wounded and untouched neighbors not claimed", "damage without attack evidence rejected", "untouched neighbors not engaged", "engagement retained outside nearby radius", "missing health and entities block fresh pulls", "blocked collateral remains unresolved", "expired attack evidence rejected", "ambiguous identity pauses selection", "replacement identity clears old engagement", "death and reset release engagement" }
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            RoutePlanner.SelfTest();
            Navigation.SelfTest();
            DeathRecoveryChecks.Run();
            Input.CheckFaultWatchSafety();
            AutoRepairChecks.Run().GetAwaiter().GetResult();
            VisualRecoveryChecks.Run().GetAwaiter().GetResult();
            RecoveryTravelChecks.Run().GetAwaiter().GetResult();
            AdjustableRadiusChecks.Run();
            Input.CheckRepairPointer();
            Input.CheckReviveInput().GetAwaiter().GetResult();
            ChestCatalog.SelfTest();
            RetreatRecovery.SelfTest();
            RestToggle.SelfTest();
            HealingRest.SelfTest();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "healing-rest-checks.json"), JsonSerializer.Serialize(new
            {
                Passed = true,
                HardwareInputEmitted = false,
                Checks = new[] { "automatic healing percentage triggers resting", "above-threshold deferred request cancelled before sitting", "missing supplies distinguished from cooldown", "no sitting during engagement or return", "pending fight and pickup finish first", "three-second quiet observation before sitting", "recover through 99 percent to full", "full health plus confirmed standing before resume", "damage during sitting cancels rest and stands first", "unknown or dead HP rejected", "single C per verified posture transition" }
            }, new JsonSerializerOptions { WriteIndented = true }));
            PartySnapshot.SelfTest();
            GroupPolicy.SelfTest();
            LocalCharacter.SelfTest();
            HealerPolicy.SelfTest();
            BuffUpkeepPolicy.SelfTest(Path.Combine(AppContext.BaseDirectory, "buff-checks.json"));
            ActiveEffectSnapshot.SelfTest();
            LiveBuffUpkeep.SelfTest();
            Input.CheckSkillKinds().GetAwaiter().GetResult();
            AimCalibration.SelfTest().GetAwaiter().GetResult();
            var sparseBar = new HotbarSnapshot(0, [new("1", SlotKind.Skill, 1, "Attack", 1000, 0, false, 0), new("2", SlotKind.Empty, 0, "", 0, 0, false, 0), new("3", SlotKind.Item, 3020, "Bread", 0, 0, false, 0)]);
            if (SkillRotation.AvailableKeys("123", sparseBar) != "1" || SkillRotation.AvailableKeys("23", sparseBar) != "") throw new Exception("New-character skill filtering failed");
            if (SkillRotation.DetectKeys(sparseBar) != "1" || SkillRotation.DetectKeys(new(0, [sparseBar.Slots[0] with { Key = "0" }, sparseBar.Slots[0] with { Key = "4" }, sparseBar.Slots[2]])) != "40" || SkillRotation.DetectKeys(new(0, [])) != "") throw new Exception("Automatic skill key discovery failed");
            var autoRoundTrip = System.Text.Json.JsonSerializer.Deserialize<Options>(System.Text.Json.JsonSerializer.Serialize(new Options { AutoDetectSkills = false }));
            if (autoRoundTrip == null || autoRoundTrip.AutoDetectSkills) throw new Exception("Manual skill mode was not preserved");
            CombatPickup.SelfTest();
            NearbyLootPickup.SelfTest();
            LootTracker.SelfTest();
            LootTrackerLog.SelfTest();
            Input.CheckNearbyLootInput().GetAwaiter().GetResult();
            var pickupDefault = JsonSerializer.Deserialize<Options>("{}");
            var pickupRoundTrip = JsonSerializer.Deserialize<Options>(JsonSerializer.Serialize(new Options { AutoPickupNearbyLoot = false, NearbyEnemyRadius = 7 }));
            if (pickupDefault?.AutoPickupNearbyLoot != true || pickupRoundTrip?.AutoPickupNearbyLoot != false || pickupRoundTrip.NearbyEnemyRadius != 7)
                throw new Exception("Nearby ground pickup defaults or saved radius setting were lost.");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "nearby-loot-checks.json"), JsonSerializer.Serialize(new
            {
                Passed = true,
                HardwareInputEmitted = false,
                Checks = new[] { "inclusive player-centered Nearby Enemy Radius", "radius setting changes detection", "player movement updates detection", "last local drop releases E despite distant loot", "multiple local drops keep E held", "pre-existing loot included", "invalid observations rejected", "new setting defaults on and persists" }
            }, new JsonSerializerOptions { WriteIndented = true }));
            Input.CheckCombatPickup().GetAwaiter().GetResult();
            var mimic = new Entity(1, 0x813d1753, "Mimic", new(), 0, Model: "MON_mimic.GCMDS");
            var gamekeeper = new Entity(2, 0x80251752, "Gamekeeper", new(), 0, Model: "MON_SnowGun2.GCMDS");
            var tribal = new Entity(4, 0x80001754, "Tribal", new(2, 0), 0, Model: "MON_SsangNom.GCMDS");
            var tower = new Entity(6, 0x80001756, "Tower", new(2, 0), 0, Model: "Mon_Tower.GCMDS");
            if (!tribal.Monster || !tribal.Targetable || tribal.PriorityLootObject || Targeting.IsGamekeeper(tribal) ||
                (tribal with { Id = 0x40001754 }).Monster || (tribal with { Model = "MON_mimic.GCMDS" }).Monster ||
                !(tribal with { Model = "mon_ssangnom.gcmds" }).Monster)
                throw new Exception("Tribal exact identity classification failed.");
            var tribalHp = new Dictionary<uint, Health> { { tribal.Id, new(100, 100) } };
            if (Targeting.Choose([tribal], tribalHp, new(), new(), 20, _ => Threat.Yellow, "", ["Yellow"]) != tribal ||
                Targeting.Choose([tribal], tribalHp, new(), new(), 20, _ => Threat.Yellow, "", ["Green"]) != null ||
                Targeting.Choose([tribal], tribalHp, new(), new(), 20, _ => Threat.Yellow, "Mimic", ["Yellow"]) != null ||
                Targeting.Eligible(tribal, new(0, 100), Threat.Yellow, "", ["Yellow"]) ||
                Targeting.Eligible(tribal, default, Threat.Yellow, "", ["Yellow"]))
                throw new Exception("Tribal selection bypassed name/color/health rules.");
            var pulkhan = new Entity(5, 0x80001755, "Pulkhan", new(2, 0), 0, Model: "mon_pulkhan02.GCMDS");
            if (!pulkhan.Monster || !pulkhan.Targetable || pulkhan.PriorityLootObject || Targeting.IsGamekeeper(pulkhan) ||
                (pulkhan with { Id = 0x40001755 }).Monster || (pulkhan with { Model = "MON_mimic.GCMDS" }).Monster ||
                !(pulkhan with { Model = "MON_PULKHAN02.gcmds" }).Monster)
                throw new Exception("Pulkhan exact identity classification failed.");
            var pulkhanHp = new Dictionary<uint, Health> { { pulkhan.Id, new(100, 100) } };
            if (Targeting.Choose([pulkhan], pulkhanHp, new(), new(), 20, _ => Threat.Yellow, "", ["Yellow"]) != pulkhan ||
                Targeting.Choose([pulkhan], pulkhanHp, new(), new(), 20, _ => Threat.Yellow, "", ["Green"]) != null ||
                Targeting.Choose([pulkhan], pulkhanHp, new(), new(), 20, _ => Threat.Yellow, "Pulkan", ["Yellow"]) != pulkhan ||
                Targeting.Choose([pulkhan], pulkhanHp, new(), new(), 20, _ => Threat.Yellow, "Pulkans", ["Yellow"]) != pulkhan ||
                Targeting.Choose([pulkhan], pulkhanHp, new(), new(), 20, _ => Threat.Yellow, "Pulkhan", ["Yellow"]) != pulkhan ||
                Targeting.Choose([pulkhan], pulkhanHp, new(), new(), 20, _ => Threat.Yellow, "Pulkhans", ["Yellow"]) != pulkhan ||
                Targeting.Choose([pulkhan], pulkhanHp, new(), new(), 20, _ => Threat.Yellow, "Mimic", ["Yellow"]) != null ||
                Targeting.Eligible(pulkhan, new(0, 100), Threat.Yellow, "Pulkan", ["Yellow"]) ||
                Targeting.Eligible(pulkhan, default, Threat.Yellow, "Pulkan", ["Yellow"]))
                throw new Exception("Pulkhan selection bypassed name/color/health rules.");
            if (!mimic.Monster || !mimic.Targetable || !gamekeeper.Monster || !gamekeeper.Targetable || !Targeting.IsGamekeeper(gamekeeper with { Model = "models\\MON_SnowGun2.GCMDS" }) || !pulkhan.Monster || !pulkhan.Targetable || !tower.Monster || !tower.Targetable || !Targeting.IsStationaryHuntTarget(mimic) || !Targeting.IsStationaryHuntTarget(pulkhan) || !Targeting.IsStationaryHuntTarget(tribal) || !Targeting.IsStationaryHuntTarget(tower) || Targeting.IsStationaryHuntTarget(gamekeeper) || mimic.PriorityLootObject || gamekeeper.PriorityLootObject || pulkhan.PriorityLootObject)
                throw new Exception("Verified unprefixed farming monsters were not classified correctly.");
            if ((mimic with { Id = 0x40001753 }).Monster || (mimic with { Model = "NPC_AG_TreasureBox.GCMDS" }).Monster ||
                (gamekeeper with { Id = 0x80250001 }).Monster || new Entity(3, 0x8000009d, "Mimic", new(), 0, Model: "NPC_AG_TreasureBox.GCMDS").Monster)
                throw new Exception("Unprefixed classification accepted unverified NPC/scenery/model pairs.");
            if (new MonsterDefinition(100, 10).Difficulty(100) != Threat.Cyan ||
                !Targeting.Eligible(gamekeeper, new Health(50, 50), Threat.Cyan, "", ["Cyan"]) ||
                Targeting.Eligible(gamekeeper, new Health(50, 50), Threat.Cyan, "", ["Yellow"]) ||
                Targeting.Eligible(mimic, new Health(0, 3359), Threat.Yellow, "", ["Yellow"]))
                throw new Exception("Farming monsters bypassed color/death restrictions.");
            var keeper = gamekeeper with { Position = new(8, 0) };
            var encounterHp = new Dictionary<uint, Health> { { mimic.Id, new(3000, 3359) }, { keeper.Id, new(50, 50) } };
            var rangedKeeper = keeper with { Position = new(42, 0) };
            double responseBoundary = Targeting.ResponseRadius(20, 60);
            if (Targeting.ChooseUrgent([rangedKeeper], encounterHp, new(), new(), 20, true) != null ||
                Targeting.ChooseUrgent([rangedKeeper], encounterHp, new(), new(), responseBoundary, true) != rangedKeeper ||
                Targeting.ChooseEncounter([rangedKeeper], [mimic], encounterHp, new(), new(), responseBoundary, true) != rangedKeeper ||
                Targeting.ChooseUrgent([rangedKeeper with { Position = new(61, 0) }], encounterHp, new(), new(), responseBoundary, true) != null ||
                Targeting.TargetRadius(mimic, true, 20, 60) != 20 || Targeting.TargetRadius(rangedKeeper, true, 20, 60) != 60 ||
                Targeting.TargetRadius(rangedKeeper, false, 20, 60) != 20)
                throw new Exception("Ranged Gamekeeper response did not separate threat and farming boundaries");
            if (RoutePlanner.Plan(new(), rangedKeeper.Position, new(), 20, []) != null ||
                RoutePlanner.Plan(new(), rangedKeeper.Position, new(), responseBoundary, []) == null ||
                RoutePlanner.Plan(rangedKeeper.Position, new(), new(), responseBoundary, []) == null)
                throw new Exception("Gamekeeper outbound or return route was blocked by the farming radius");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "gamekeeper-response-checks.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                Passed = true,
                HuntRadius = 20,
                ResponseRadius = 60,
                RecordedFailureDistance = 42,
                Checks = new[] { "ranged target outside farm area selected", "active encounter overridden", "ordinary target boundary unchanged", "response limit enforced", "outbound route allowed", "return route allowed" }
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            // Regression: a Gamekeeper outside the 6-unit encounter still preempts inside the 20-unit hunt.
            Entity? EncounterPick(IEnumerable<Entity> protectedActors, bool enabled = true, double huntRadius = 20) =>
                Targeting.ChooseEncounter(protectedActors, [mimic], encounterHp, new(), new(), huntRadius, enabled);
            if (EncounterPick([mimic, keeper]) != keeper || EncounterPick([mimic, keeper], false) != mimic ||
                EncounterPick([mimic]) != mimic || EncounterPick([mimic, keeper], huntRadius: 6) != mimic ||
                Targeting.ChooseUrgent([mimic, keeper], encounterHp, new(), new(), 20, true) != keeper)
                throw new Exception("Gamekeeper preemption or next selection was trapped in the encounter radius.");
            encounterHp[keeper.Id] = new(0, 50);
            if (EncounterPick([mimic, keeper]) != mimic) throw new Exception("Dead Gamekeeper preempted encounter");
            encounterHp.Remove(keeper.Id);
            if (EncounterPick([mimic, keeper]) != mimic) throw new Exception("Unknown HP Gamekeeper preempted encounter");
            var container = new Entity(3, 0x8000175f, "", new(1, 0), 0, Model: "NPC_AG_Container.gcmds");
            var farmHp = new Dictionary<uint, Health> { { mimic.Id, new(3359, 3359) }, { keeper.Id, new(50, 50) }, { container.Id, new(1, 1) } };
            Entity? FarmPick(bool enabled, IEnumerable<Entity>? candidates = null, double range = 35) => Targeting.Choose(candidates ?? [mimic, keeper, container], farmHp, new(), new(), range, _ => Threat.Cyan, "unrelated", [], prioritizeGamekeeper: enabled);
            if (FarmPick(true) != keeper || FarmPick(false) != container || FarmPick(true, [mimic, container]) != container || FarmPick(true, range: 5) != container)
                throw new Exception("Gamekeeper priority order, toggle, protection-filtered candidates, or radius failed.");
            farmHp[keeper.Id] = new(0, 50); if (FarmPick(true) != container) throw new Exception("Priority chose a dead Gamekeeper");
            farmHp.Remove(keeper.Id); if (FarmPick(true) != container) throw new Exception("Priority chose unknown Gamekeeper HP");
            if (Targeting.PriorityRank(keeper, true) <= Targeting.PriorityRank(container, true) || Targeting.PriorityRank(keeper, false) != 0 ||
                Targeting.Eligible(keeper with { Model = "NPC_AG_TreasureBox.GCMDS" }, new(50, 50), Threat.Cyan, "", [], true))
                throw new Exception("Gamekeeper priority identity failed.");
            var optionRoundTrip = System.Text.Json.JsonSerializer.Deserialize<Options>(System.Text.Json.JsonSerializer.Serialize(new Options { PrioritizeGamekeeper = false, PrioritizeBreakables=false }));
            if (optionRoundTrip == null || optionRoundTrip.PrioritizeGamekeeper || optionRoundTrip.PrioritizeBreakables) throw new Exception("Target-priority toggles did not persist");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "farming-monster-checks.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = true, Checks = new[] { "exact Mimic 5971 model", "exact Gamekeeper 5970 model", "exact Pulkhan 5973 model", "exact Tribal 5972 model", "exact Tower 5974 model", "stationary farm target classification", "qualified model paths", "NPC and wrong-model rejection", "unchanged priority props", "Gamekeeper Cyan filter", "dead monster exclusion", "Gamekeeper above monsters and props", "explicit priority overrides name/color only", "toggle persistence", "radius and upstream protections", "unknown HP excluded" } }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            var fungus = new HotbarSlot("8", SlotKind.Item, 3204, "Fungus Recovery Potion", 12000, 0, false, 0, "Potion", "Restores health points.");
            var fungusBar = new HotbarSnapshot(0, [fungus]);
            if (!RecoveryItems.Recognized(fungus) || RecoveryItems.Choose(fungusBar, 0) != fungus || fungus.RestoresHealth != 0)
                throw new Exception("The observed Fungus Recovery Potion was not recognized without inventing an amount.");
            if (RecoveryItems.Choose(new HotbarSnapshot(0, [fungus with { RemainingCooldown = 1000 }]), 0) != null ||
                RecoveryItems.Choose(new HotbarSnapshot(0, [fungus with { Locked = true }]), 0) != null ||
                RecoveryItems.Recognized(fungus with { Kind = SlotKind.Skill }) ||
                RecoveryItems.Recognized(fungus with { Category = "Sword" }) ||
                RecoveryItems.Recognized(fungus with { Description = "Restores mana points." }) ||
                RecoveryItems.Recognized(fungus with { Description = "Does not restore health points." }))
                throw new Exception("Amountless potion detection bypassed cooldown/type/effect guards.");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "potion-detection-checks.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = true, Item = fungus, Recognized = RecoveryItems.Recognized(fungus), SelectedKey = RecoveryItems.Choose(fungusBar, 0)?.Key, Checks = new[] { "observed slot 8 amountless HP potion", "amount stays unknown", "cooldown and lock guards", "nonitem and unrelated category rejected", "mana-only and unrelated text rejected" } }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Input.CheckRetreatInterruptions().GetAwaiter().GetResult();
            var recordedStart = new Vec(3140.7425, 1964.33109375);
            var recordedGoal = recordedStart + new Vec(-.8243749999996908, -10.026406250000036);
            var replay = new Navigation(); replay.Observe("recorded-stall-fixture", recordedStart, 0); replay.BeginGoal("recorded target");
            replay.RecordBlock(recordedStart, new Vec(-.11334470578399307, -.9935557244919583), 0);
            _ = replay.Waypoint(recordedStart, recordedGoal, recordedStart, 20, []);
            Vec replayPrevious = recordedStart;
            foreach (var point in replay.Route) { if (!RoutePlanner.SegmentClear(replayPrevious, point, replay.Obstacles([]))) throw new Exception("Recorded stall replay crossed its learned obstacle"); replayPrevious = point; }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "navigation-checks.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = true, Method = "Recorded stalled approach replay; route geometry only, not a live terrain trial", Start = recordedStart, Goal = recordedGoal, Map = replay.Snapshot() }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            if (typeof(Entry).Assembly.GetManifestResourceStream("PoteHunter.DefaultSettings.json") == null) throw new Exception("Bundled default settings are missing");
            var savedRules = new Options();
            if (!savedRules.AntiKillSteal || !savedRules.AvoidNames.Any(r => r.Name == "Mad Katz" && r.Radius == 15)) throw new Exception("Requested protection defaults are missing");
            var restoredRules = System.Text.Json.JsonSerializer.Deserialize<Options>(System.Text.Json.JsonSerializer.Serialize(savedRules));
            if (restoredRules?.AvoidNames.Count != 1 || restoredRules.AvoidNames[0].Name != "Mad Katz" || restoredRules.OtherPlayerRadius != 15) throw new Exception("Protection settings do not round-trip");
            if (new Entity(1, 0x40001234, "Guard", new(), 0).Monster) throw new Exception("NPC exclusion");
            if (new Entity(1, 0x1234, "Gimp", new(), 0).Monster) throw new Exception("Player exclusion");
            if (!new Entity(1, 0x803d0001, "Lv. 1 Ichman Villager", new(), 0).Monster) throw new Exception("Monster classification");
            if (new Entity(1, 0xa0000001, "Lv. 1 Summon", new(), 0).Monster) throw new Exception("Separate summoned/object UID category must be excluded");
            var box = new Entity(10, 0x8019175f, " ", new Vec(12, 0), 0, Model: "NPC_AG_Container.gcmds");
            var barrel = new Entity(11, 0x800d1760, "", new Vec(20, 0), 0, Model: "NPC_AG_Barrel.GCMDS");
            var ordinary = new Entity(12, 0x803d0001, "Lv. 1 Ichman Villager", new Vec(1, 0), 0);
            var scenery = new Entity(13, 0x800100a0, "Box", new Vec(.5, 0), 0, Model: "NPC_AG_Container.gcmds");
            if (!box.PriorityLootObject || !barrel.PriorityLootObject || scenery.Targetable ||
                (box with { Model = "NPC_AG_Barrel.gcmds" }).PriorityLootObject || (box with { Id = 0x4000175f }).PriorityLootObject)
                throw new Exception("Priority containers must match both the exact prototype and model, excluding scenery and NPCs");
            var targetHealth = new Dictionary<uint, Health> { [box.Id] = new(1, 1), [barrel.Id] = new(1, 1), [ordinary.Id] = new(100, 100), [scenery.Id] = new(3333, 3333) };
            Entity? Pick(IEnumerable<Entity> candidates, string name = "", string[]? colors = null, bool priorityOnly = false) => Targeting.Choose(candidates, targetHealth, new(), new(), 35, _ => Threat.Green, name, colors ?? ["Green"], priorityOnly);
            if (Pick([ordinary, scenery, barrel, box]) != box || Pick([ordinary, barrel, box], "Does not match", []) != box ||
                Pick([ordinary, box with { Position = new Vec(36, 0) }]) != ordinary || Pick([ordinary], priorityOnly: true) != null)
                throw new Exception("Priority selection must beat nearer monsters, bypass filters, and respect the hunt boundary");
            if (Targeting.Choose([ordinary, barrel, box], targetHealth, new(), new(), 35, _ => Threat.Green, "", ["Green"], prioritizeBreakables:false) != ordinary ||
                Targeting.Choose([barrel, box], targetHealth, new(), new(), 35, _ => Threat.Green, "", ["Green"], prioritizeBreakables:false) != box)
                throw new Exception("Breakable-priority toggle did not prefer monsters while retaining a breakable-only fallback");
            targetHealth[box.Id] = new(0, 1);
            if (Pick([ordinary, box, barrel]) != barrel) throw new Exception("Dead priority object retained");
            targetHealth[barrel.Id] = default;
            if (Pick([ordinary, box, barrel]) != ordinary || Pick([ordinary, box, barrel], priorityOnly: true) != null)
                throw new Exception("Unknown/dead priority HP must not block ordinary hunting or trigger preemption");
            if (Targeting.LootHoldMilliseconds(box, 0) != 800 || Targeting.LootHoldMilliseconds(barrel, 1200) != 1200 || Targeting.LootHoldMilliseconds(ordinary, 0) != 0)
                throw new Exception("Priority loot must stay enabled while preserving ordinary pickup settings");
            foreach (uint prototype in new uint[] { 3007, 3008, 3009 })
            {
                var chest = new Entity(14, 0x80000000 | prototype, "Treasure Box", new Vec(18, 0), 0, Model: "MON_luckybag.GCMDS");
                targetHealth[chest.Id] = new(10, 10);
                if (!chest.PriorityLootObject || Pick([ordinary, chest], "Other monster", []) != chest || Targeting.LootHoldMilliseconds(chest, 0) != 800)
                    throw new Exception("Verified Treasure Box definitions must be priority targets with pickup enabled");
            }
            if (new Entity(15, 0x8000009d, "Mimic", new(), 0, Model: "NPC_AG_TreasureBox.GCMDS").PriorityLootObject ||
                new Entity(16, 0x80001761, "", new(), 0, Model: "NPC_AG_Container.gcmds").PriorityLootObject)
                throw new Exception("Mimics and unverified props must not be promoted by a chest-like model");
            var luckyBag = new Entity(14, 0x80000bc0, "Treasure Box", new Vec(18, 0), 0, Model: "MON_luckybag.GCMDS");
            var keyboxHydra = new Entity(20, 0x40020017, "<Hydra's Treasure>", new Vec(25, 10), 0, Model: "Keybox_gold.GCMDS");
            var keyboxDiablo = new Entity(21, 0x40020018, "<Diablo's Treasure>", new Vec(30, 15), 0, Model: "Keybox_gold.GCMDS");
            var containerChest = new Entity(22, 0x8000175f, " ", new Vec(5, 5), 0, Model: "NPC_AG_Container.gcmds");
            var barrelChest = new Entity(23, 0x80001760, "", new Vec(8, 8), 0, Model: "NPC_AG_Barrel.gcmds");
            var gnollMob = new Entity(24, 0x803d0001, "Lv. 85 Gnoll Warrior", new Vec(1, 0), 0, Model: "Mon_Gnoll_Warrior.GCMDS");
            var mimicMob = new Entity(25, 0x80001753, "Mimic", new Vec(2, 0), 0, Model: "MON_mimic.GCMDS");
            var playerEntity = new Entity(26, 3447, "Gimp", new Vec(0, 0), 0, Model: "PC_Akhan_A.GCMDS");
            var npcGuard = new Entity(27, 0x40001234, "Trejan\\<Roche Camp Guard>", new Vec(10, 0), 0, Model: "NPC_A_guardian_soldier.GCMDS");
            if (!Targeting.IsChest(luckyBag) || !Targeting.IsChest(keyboxHydra) || !Targeting.IsChest(keyboxDiablo) ||
                !Targeting.IsChest(containerChest) || !Targeting.IsChest(barrelChest))
                throw new Exception("Chests and treasure boxes must be recognized for radar map marking");
            if (Targeting.IsChest(gnollMob) || Targeting.IsChest(mimicMob) || Targeting.IsChest(playerEntity) || Targeting.IsChest(npcGuard))
                throw new Exception("Monsters, mimics, players, and town NPCs must not be classified as chests");
            if (Targeting.ChestLabel(keyboxHydra) != "<Hydra's Treasure>" || Targeting.ChestLabel(luckyBag) != "Treasure Box")
                throw new Exception("Chest labels must accurately reflect the treasure name");
            var events = new List<(Keys Key, bool Down)>();
            var held = new HeldInputs<Keys>((key, down) => events.Add((key, down)));
            for (int i = 0; i < 50; i++) held.Set(Keys.W, true);
            if (events.Count != 1 || events[0] != (Keys.W, true)) throw new Exception("Continuous W emitted repeated keydowns");
            held.Set(Keys.W, false); held.ReleaseAll();
            if (events.Count != 2 || events[1] != (Keys.W, false)) throw new Exception("W did not release exactly once");
            held.Set(Keys.W, true); held.Set(Keys.E, true); held.ReleaseAll();
            if (!events.TakeLast(2).Contains((Keys.W, false)) || !events.TakeLast(2).Contains((Keys.E, false))) throw new Exception("Stop did not release movement and loot keys");
            int failures = 0, releases = 0;
            var retry = new HeldInputs<Keys>((_, down) => { if (!down) { if (failures++ == 0) throw new Exception("Simulated input failure"); releases++; } });
            retry.Set(Keys.W, true); retry.ReleaseAll(); retry.ReleaseAll();
            if (releases != 1) throw new Exception("Failed key-up was not retained for retry");
            var mouseEvents = new List<(bool Right, bool Down)>();
            var heldMouse = new HeldInputs<bool>((right, down) => mouseEvents.Add((right, down)));
            for (int i = 0; i < 40; i++) heldMouse.Set(false, true);
            if (mouseEvents.Count != 1) throw new Exception("Combo hold emitted repeated mouse downs");
            heldMouse.Set(false, false); heldMouse.Set(true, true); heldMouse.Set(true, false); heldMouse.Set(false, true); heldMouse.ReleaseAll();
            if (!mouseEvents.SequenceEqual(new[] { (false, true), (false, false), (true, true), (true, false), (false, true), (false, false) })) throw new Exception("Combo / skill / stop button sequence");
            var cases = new[] { (-11, Threat.Grey), (-10, Threat.Green), (-3, Threat.Green), (-2, Threat.Yellow), (0, Threat.Yellow), (1, Threat.Orange), (2, Threat.Orange), (3, Threat.Red) };
            foreach (var (delta, expected) in cases) if (MonsterDefinition.FromLevels(20 + delta, 20) != expected) throw new Exception("Difficulty boundary " + delta);
            if (new MonsterDefinition(1, 2).Difficulty(8) != Threat.Green) throw new Exception("Gimp/Villager live example");
            if (new MonsterDefinition(1, 12).Difficulty(8) != Threat.Red || new MonsterDefinition(1, 9).Difficulty(8) != Threat.Magenta || new MonsterDefinition(1, 13).Difficulty(8) != Threat.Unknown) throw new Exception("Special category overrides");
            if (new Health(1, 160).Dead || !new Health(0, 160).Dead || !new Health(-1, 160).Dead || new Health(0, 0).Dead) throw new Exception("Death must require known max HP and nonpositive current HP");
            if (!HunterForm.ShouldHeal(new Health(50, 100), 50, 5000, 5000) || HunterForm.ShouldHeal(new Health(51, 100), 50, 5000, 5000) || HunterForm.ShouldHeal(new Health(0, 100), 50, 5000, 0) || HunterForm.ShouldHeal(new Health(0, 0), 50, 5000, 0) || HunterForm.ShouldHeal(new Health(20, 100), 50, 4999, 5000)) throw new Exception("Healing threshold, death, unknown HP or cooldown handling");
            var bar = new HotbarSnapshot(0, [new HotbarSlot("1", SlotKind.Skill, 1, "Bash", 12000, 500, false, 0), new HotbarSlot("2", SlotKind.Skill, 2, "Fast Hit", 12000, 0, false, 0), new HotbarSlot("3", SlotKind.Skill, 3, "Drain", 12000, 0, false, 0)]);
            if (SkillRotation.Choose("123", 0, bar, new Dictionary<char, long>(), 0) != 1 || SkillRotation.Choose("123", 2, bar, new Dictionary<char, long>(), 0) != 2 || SkillRotation.Choose("", 0, bar, new Dictionary<char, long>(), 0) != -1) throw new Exception("Round-robin must skip cooling skills without starving ready ones");
            if (new HotbarSlot("9", SlotKind.Item, 1, "Bread", 12000, 0, true, 3000).Ready || new HotbarSlot("9", SlotKind.Empty, 0, "Empty", 0, 0, false, 0).Ready || new HotbarSlot("1", SlotKind.Skill, 1, "Cooling skill", 12000, 500, false, 0).Ready || !new HotbarSlot("2", SlotKind.Skill, 2, "Ready skill", 12000, 0, false, 0).Ready) throw new Exception("Locked, cooling and empty hotbar slots must not be ready");
            var foods = new HotbarSnapshot(0, [new HotbarSlot("4", SlotKind.Item, 3020, "Bread", 12000, 3000, false, 0, "Food", "Restores 400 health points.", 400), new HotbarSlot("0", SlotKind.Item, 3028, "Redbean Soup", 12000, 0, false, 0, "Food", "Restores 300 health and 100 mana points.", 300, 100), new HotbarSlot("9", SlotKind.Item, 8004, "Bulte Leg", 0, 0, false, 0)]);
            if (RecoveryItems.Choose(foods, 0)?.Key != "0" || RecoveryItems.Recognized(foods.Slot('9')) || !RecoveryItems.Recognized(foods.Slot('4'))) throw new Exception("Recovery detection must follow item IDs across keys and honor cooldowns");
            if (RecoveryItems.Parse("Food", "Restores 400 health points.") != (400, 0) || RecoveryItems.Parse("Food", "Restores 300 health and 100 mana points.") != (300, 100) || RecoveryItems.Parse("Food", "Restores 500 mana points.") != (0, 500) || RecoveryItems.Parse("Sword", "Restores 400 health points.") != (0, 0) || RecoveryItems.Parse("Food", "A tasty meal.") != (0, 0)) throw new Exception("Recovery description parsing must preserve effect type and avoid unrelated items");
            bool blockedUnknown = false; try { BuildProfile.Resolve(new string('0', 64)); } catch (InvalidOperationException) { blockedUnknown = true; }
            if (!blockedUnknown || BuildProfile.Resolve(BuildProfile.September10Update.Sha256).ImageSize != 0x14c4f000) throw new Exception("Build profile validation");
            // Creating a WinForms control installs its synchronization context;
            // run these CPU-only viewport checks after the asynchronous suite.
            Navigation3DRenderChecks.Run();
            NavigationOverlay3DRendererChecks.Run();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test.txt"), "PASS: anti-kill-stealing damaged/nearby-player/own-fight handling; name avoidance and point/segment geometry; protection settings persistence; movement/heading math and faster steering convergence; held-input and release lifecycle; monster classification and HP death; exact priority box/barrel/Treasure Box detection, selection, filter override, radius and HP exclusions, mandatory pickup; persistent object recording, throttling, log rotation and damaged-catalog preservation; difficulty boundaries; hotbar cooldown/rotation; recovery descriptions and healing thresholds; unknown-build rejection."); return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test.txt"), "FAIL: " + ex.Message); return 1; }
    }
    static void CheckTracking()
    {
        double calibration = -.0029167493184407553;
        var results = new List<object>();
        foreach (bool walking in new[] { false, true })
            foreach (double degrees in new[] { 90.0, 180.0 })
            {
                int Simulate(bool faster)
                {
                    double error = degrees * Math.PI / 180;
                    for (int step = 0; step < 1000; step++)
                    {
                        if (Math.Abs(error) < .10) return step * (faster ? 25 : 35);
                        int limit = walking ? 12 : 24;
                        int pixels = faster ? Movement.CalculateTurn(error, calibration, walking) : Math.Clamp((int)Math.Round(error / calibration * (walking ? .4 : .55)), -limit, limit);
                        error -= pixels * calibration;
                    }
                    throw new Exception("Steering did not converge");
                }
                int before = Simulate(false), after = Simulate(true);
                if (after >= before * .5) throw new Exception("Large turns did not improve sufficiently in the calibrated controller simulation");
                results.Add(new { Scenario = walking ? "Walking" : "Stationary", Degrees = degrees, PreviousSimulatedMs = before, UpdatedSimulatedMs = after });
            }
        foreach (double sensitivity in new[] { -.02, -.0029167493184407553, -.00025, .00025, .02 })
            foreach (double start in new[] { -Math.PI, -.5, -.04, .04, .5, Math.PI })
            {
                double error = start, previous = start;
                for (int step = 0; step < 300; step++)
                {
                    int pixels = Movement.CalculateTurn(previous, sensitivity, false);
                    previous = error; error -= pixels * sensitivity;
                }
                if (Math.Abs(error) > .10) throw new Exception("Delayed heading feedback did not settle");
            }
        bool invalidRejected = false;
        try { Movement.CalculateTurn(1, 0, false); } catch (InvalidOperationException) { invalidRejected = true; }
        if (!invalidRejected || Movement.CalculateTurn(.02, calibration, false) != 0) throw new Exception("Steering calibration/dead-zone handling");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "steering-checks.json"), System.Text.Json.JsonSerializer.Serialize(new { Method = "Controller simulation using the observed calibration; excludes game/render latency.", RadiansPerPixel = calibration, Results = results }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
