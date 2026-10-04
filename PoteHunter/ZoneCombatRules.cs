namespace PoteHunter;

public sealed record ZoneCombatInfo(int Zone, string Name, ZoneCombatRule Rule, string Evidence, string Source);

// Explicit verified-zone policy, separate from terrain aliases. Geometry sharing
// does not prove that two zones share their server-side PvP or safe-zone rules.
public static class ZoneCombatRules
{
    static readonly IReadOnlyList<ZoneCombatInfo> known = Array.AsReadOnly(new[]
    {
        new ZoneCombatInfo(8, "Caernarvon", ZoneCombatRule.PvP,
            "Official Caernarvon rules permit faction PvP; guild wars also allow same-faction kills. Zone ID established from the observed client map.", "https://forum.playpote.com/threads/level-up-guide-starting-your-journey.160/"),
        new ZoneCombatInfo(12, "Almighty Land", ZoneCombatRule.Safe,
            "User confirmed on 2026-09-25 (DEVELOPMENT-HISTORY.md): opposing-faction non-PvP. This is not independent official verification or a town safe-region/individual attackability flag.", "DEVELOPMENT-HISTORY.md")
    });

    public static IReadOnlyList<ZoneCombatInfo> Known => known;
    public static ZoneCombatInfo For(int zone) => known.FirstOrDefault(info => info.Zone == zone) ??
        new(zone, $"Zone {zone}", ZoneCombatRule.Unknown, "Zone combat rules have not been verified.", "");
}
