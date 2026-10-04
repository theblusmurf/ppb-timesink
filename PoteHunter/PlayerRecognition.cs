namespace PoteHunter;

public enum PlayerFaction { Unknown, Kartefant, Merkhadian }
public enum ZoneCombatRule { Unknown, Safe, PvP }
public enum PlayerRelation { Unknown, Self, Party, SameFaction, OpposingSafe, Enemy }

public readonly record struct PlayerRecognitionResult(
    PlayerFaction OwnFaction, PlayerFaction OtherFaction, ZoneCombatRule ZoneRule, PlayerRelation Relation)
{
    public bool Enemy => Relation == PlayerRelation.Enemy;
    public string Status => PlayerRecognition.Status(Relation);
}

// Observer-only model-family classification. This does not establish the actual
// server's attackability flags, start combat, change courtesy, or send an alert.
public static class PlayerRecognition
{
    // Exact known bodies only: unknown PC_* variants and separate mount objects
    // must not acquire a faction merely by resemblance or their player name.
    public static PlayerFaction Faction(string? model) => model?.ToUpperInvariant() switch
    {
        "PC_MAN.GCMDS" or "PC_WOMAN.GCMDS" => PlayerFaction.Kartefant,
        "PC_AKHAN_A.GCMDS" or "PC_AKHAN_B.GCMDS" => PlayerFaction.Merkhadian,
        _ => PlayerFaction.Unknown
    };

    public static PlayerRecognitionResult Classify(Entity other, Entity? self, int zone, bool verifiedPartyMember = false)
        => Classify(other, self, ZoneCombatRules.For(zone).Rule, verifiedPartyMember);

    // verifiedPartyMember requires a current validated roster matching this
    // entity's ID and name. An old roster or name-only friend guess is insufficient.
    public static PlayerRecognitionResult Classify(Entity other, Entity? self, ZoneCombatRule zoneRule, bool verifiedPartyMember = false)
    {
        var rule = zoneRule is ZoneCombatRule.Safe or ZoneCombatRule.PvP ? zoneRule : ZoneCombatRule.Unknown;
        var ownFaction = self != null && CombatCourtesy.IsOtherPlayer(self, 0) ? Faction(self.Model) : PlayerFaction.Unknown;
        var otherFaction = CombatCourtesy.IsOtherPlayer(other, 0) ? Faction(other.Model) : PlayerFaction.Unknown;
        PlayerRecognitionResult Result(PlayerRelation relation) => new(ownFaction, otherFaction, rule, relation);
        if (self != null && self.Id != 0 && other.Id == self.Id) return Result(PlayerRelation.Self);
        if (self == null || !CombatCourtesy.IsOtherPlayer(other, self.Id)) return Result(PlayerRelation.Unknown);
        if (verifiedPartyMember) return Result(PlayerRelation.Party);
        if (ownFaction == PlayerFaction.Unknown || otherFaction == PlayerFaction.Unknown) return Result(PlayerRelation.Unknown);
        if (ownFaction == otherFaction) return Result(PlayerRelation.SameFaction);
        return Result(rule switch
        {
            ZoneCombatRule.PvP => PlayerRelation.Enemy,
            ZoneCombatRule.Safe => PlayerRelation.OpposingSafe,
            _ => PlayerRelation.Unknown
        });
    }

    public static string FactionLabel(PlayerFaction faction) => faction switch
    {
        PlayerFaction.Kartefant => "Kartefant (Human)",
        PlayerFaction.Merkhadian => "Merkhadian (Akkan)",
        _ => "Unknown faction"
    };

    public static string RuleLabel(ZoneCombatRule rule) => rule switch
    {
        ZoneCombatRule.Safe => "Non-PvP",
        ZoneCombatRule.PvP => "PvP",
        _ => "Unverified"
    };

    public static string Status(PlayerRelation relation) => relation switch
    {
        PlayerRelation.Self => "You",
        PlayerRelation.Party => "Party member",
        // Same-faction kills can occur in guild wars; this is not a promise of safety.
        PlayerRelation.SameFaction => "Same faction",
        PlayerRelation.OpposingSafe => "Opposing faction · non-PvP",
        PlayerRelation.Enemy => "Enemy · opposing faction in PvP",
        _ => "Unknown player status"
    };
}
