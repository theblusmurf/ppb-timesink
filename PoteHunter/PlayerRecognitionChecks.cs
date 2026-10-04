using System.Text.Json;

namespace PoteHunter;

internal static class PlayerRecognitionChecks
{
    internal static void Run()
    {
        var human = new Entity(100, 1, "Local Human", new(0, 0), 0, Model: "PC_MAN.GCMDS");
        var akkan = new Entity(200, 2, "Local Akkan", new(1, 0), 0, Model: "PC_Akhan_A.GCMDS");
        void Relation(Entity other, Entity? self, ZoneCombatRule rule, PlayerRelation expected, bool party = false)
        {
            var result = PlayerRecognition.Classify(other, self, rule, party);
            if (result.Relation != expected || result.Enemy != (expected == PlayerRelation.Enemy))
                throw new Exception($"Player relation failed: {self?.Model} / {other.Model} / {rule}: {result.Relation}, expected {expected}.");
        }
        foreach (var model in new[] { "PC_MAN.GCMDS", "PC_WOMAN.GCMDS", "pc_man.gcmds", "pC_wOmAn.GcMdS" })
            if (PlayerRecognition.Faction(model) != PlayerFaction.Kartefant) throw new Exception("Known Human body classification failed.");
        foreach (var model in new[] { "PC_Akhan_A.GCMDS", "PC_Akhan_B.GCMDS", "pc_akhan_b.gcmds" })
            if (PlayerRecognition.Faction(model) != PlayerFaction.Merkhadian) throw new Exception("Known Akkan body classification failed.");
        foreach (var model in new string?[] { null, "", "PC_UNKNOWN.GCMDS", "PC_MAN2.GCMDS", "PC_Akhan_A_Mount.GCMDS", "NPC_MAN.GCMDS", "PC_MAN.GCMDS.bak", "PC_MAN", " PC_MAN.GCMDS", "CHAR/PC_MAN.GCMDS" })
        {
            if (PlayerRecognition.Faction(model) != PlayerFaction.Unknown) throw new Exception("Unverified body acquired a faction.");
            Relation(akkan with { Model = model ?? "" }, human, ZoneCombatRule.PvP, PlayerRelation.Unknown);
        }
        Relation(akkan, human, ZoneCombatRule.PvP, PlayerRelation.Enemy);
        Relation(human, akkan, ZoneCombatRule.PvP, PlayerRelation.Enemy);
        var inverted = PlayerRecognition.Classify(human, akkan, ZoneCombatRule.PvP);
        if (inverted.OwnFaction != PlayerFaction.Merkhadian || inverted.OtherFaction != PlayerFaction.Kartefant)
            throw new Exception("Faction comparison did not use the actual local player's body.");
        foreach (var rule in new[] { ZoneCombatRule.Unknown, ZoneCombatRule.Safe, ZoneCombatRule.PvP })
        {
            Relation(human with { Id = 3, Model = "PC_WOMAN.GCMDS" }, human, rule, PlayerRelation.SameFaction);
            Relation(akkan with { Id = 3, Model = "PC_Akhan_B.GCMDS" }, akkan, rule, PlayerRelation.SameFaction);
            Relation(akkan, human, rule, PlayerRelation.Party, true);
            Relation(human, human, rule, PlayerRelation.Self);
            Relation(akkan, null, rule, PlayerRelation.Unknown);
            Relation(akkan, human with { Model = "PC_UNKNOWN.GCMDS" }, rule, PlayerRelation.Unknown);
        }
        Relation(akkan, human, ZoneCombatRule.Safe, PlayerRelation.OpposingSafe);
        Relation(human, akkan, ZoneCombatRule.Safe, PlayerRelation.OpposingSafe);
        Relation(akkan, human, ZoneCombatRule.Unknown, PlayerRelation.Unknown);
        Relation(akkan, human, (ZoneCombatRule)99, PlayerRelation.Unknown);
        Relation(akkan with { Id = 0 }, human, ZoneCombatRule.PvP, PlayerRelation.Unknown, true);
        Relation(akkan with { Id = 0x40000002 }, human, ZoneCombatRule.PvP, PlayerRelation.Unknown, true);
        Relation(akkan with { Id = 0x80000002 }, human, ZoneCombatRule.PvP, PlayerRelation.Unknown, true);
        Relation(akkan, human with { Id = 0 }, ZoneCombatRule.PvP, PlayerRelation.Unknown);
        Relation(akkan, human with { Id = 0x40000001 }, ZoneCombatRule.PvP, PlayerRelation.Unknown);
        if (PlayerRecognition.Classify(akkan, human, 8).Relation != PlayerRelation.Enemy ||
            PlayerRecognition.Classify(akkan, human, 12).Relation != PlayerRelation.OpposingSafe)
            throw new Exception("Confirmed PvP/non-PvP-zone classification failed.");
        foreach (var zone in new[] { -1, 0, 6, 7, 10, 11, 13, 14, 17, 18, 100, 999 })
            if (ZoneCombatRules.For(zone).Rule != ZoneCombatRule.Unknown || PlayerRecognition.Classify(akkan, human, zone).Enemy)
                throw new Exception("Unverified zone or geometry alias inferred PvP rules.");
        if (ZoneCombatRules.Known.Select(info => info.Zone).Distinct().Count() != ZoneCombatRules.Known.Count ||
            ZoneCombatRules.Known.Any(info => info.Rule == ZoneCombatRule.Unknown || string.IsNullOrWhiteSpace(info.Evidence)))
            throw new Exception("Explicit zone policy lacks unique IDs or supporting evidence.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "player-recognition-checks.json"), JsonSerializer.Serialize(new
        {
            Passed = true,
            HardwareInputEmitted = false,
            Checks = new[] { "exact four known model families", "unrecognized bodies remain unknown", "actual local Human/Akkan faction inversion", "opposing only enemy in confirmed PvP", "non-PvP opposing player is not marked enemy", "same faction is not an attackability promise", "validated party suppresses enemy", "self/NPC/monster/zero identities excluded", "unknown zone and model do not fabricate enemy status", "terrain aliases do not imply PvP policy" }
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
