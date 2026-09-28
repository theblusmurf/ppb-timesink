using System.Text.Json;

namespace PoteHunter;

public sealed class Options
{
    public string Player { get; set; } = ""; // Last detected name; never an identity selector.
    public string Target { get; set; } = "";
    public decimal HuntRadius { get; set; } = 35;
    public bool LeaveAreaWhenEmpty { get; set; }
    public decimal MeleeRange { get; set; } = 2;
    public bool Ranged { get; set; }
    // Class-8 archer profile. Existing settings remain the generic bow/crossbow profile.
    public bool ArcherClass { get; set; }
    public bool RangedPullEnabled { get; set; }
    public int RangedPullCount { get; set; } = 5;
    public decimal RangedGatherRadius { get; set; } = 2;
    public decimal RangedMeleeAttackRange { get; set; } = 2;
    public decimal RangedVerticalAimOffset { get; set; } = 1;
    public int RangedPullTimeoutSeconds { get; set; } = 15;
    public int RangedGatherTimeoutSeconds { get; set; } = 20; // Legacy settings compatibility; arrival is distance-driven.
    public bool ExperimentalInternalTargeting { get; set; }
    public string SkillKeys { get; set; } = "";
    public bool AutoDetectSkills { get; set; } = true;
    public bool SmartSkillTargeting { get; set; } = true;
    public bool CenterAreaSkills { get; set; } = true;
    public bool RetargetSingleTargetSkills { get; set; } = true;
    public decimal SkillSeconds { get; set; } = 8;
    public decimal LootHoldMs { get; set; } = 800;
    public string[] AllowedDifficulties { get; set; } = ["Green", "Yellow"];
    public bool PrioritizeGamekeeper { get; set; } = true;
    public bool StationaryGamekeeperPriority { get; set; }
    public bool ReturnToHuntLocationAfterGamekeeper { get; set; } = true;
    public bool PrioritizeBreakables { get; set; } = true;
    public decimal GamekeeperResponseRadius { get; set; } = 60;
    public bool LootDuringSkillCooldowns { get; set; } = true;
    public bool AutoPickupNearbyLoot { get; set; } = true;
    public bool HealerMode { get; set; }
    public bool MaintainAreaBuffs { get; set; } = true;
    public bool UseAttackPotions { get; set; }
    public bool UseDefensePotions { get; set; }
    public decimal EncourageDurationSeconds { get; set; } = 47;
    public decimal HardenSkinDurationSeconds { get; set; } = 47;
    public bool AutoDetectHealingSkills { get; set; } = true;
    public string HealingSkillKeys { get; set; } = "";
    public decimal HealChargeMilliseconds { get; set; } = 1000;
    public decimal PartyHealBelowPercent { get; set; } = 80;
    public decimal PartyHealRange { get; set; } = 40;
    public bool AutoHeal { get; set; } = true;
    public decimal HealBelowPercent { get; set; } = 50;
    public decimal HealDelaySeconds { get; set; } = 5;
    public bool AutoRestoreMana { get; set; } = true;
    public bool HealthSkillCondition { get; set; } = true;
    public decimal HealthSkillPercent { get; set; } = 50;
    public string HealthConditionKeys { get; set; } = "";
    public decimal ManaBelowPercent { get; set; } = 30;
    public decimal ManaDelaySeconds { get; set; } = 5;
    public int ManaReservePercent { get; set; }
    public bool ContinuousCombatPositioning { get; set; } = true;
    // Read settings supplied by the original Continuous-Positioning build.
    [System.Text.Json.Serialization.JsonPropertyName("ContinuousPositioning")]
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyContinuousPositioning { get=>null; set { if(value.HasValue)ContinuousCombatPositioning=value.Value; } }
    public bool TightGathering { get; set; } = true;
    public decimal GatherRadius { get; set; } = .5m;
    public bool ShowNavigationOverlay { get; set; }
    public int NavigationOverlaySize { get; set; } = 450;
    public int NavigationViewRadius { get; set; } = 150;
    public bool ShowTreasureChestMarkers { get; set; } = true;
    public bool AntiKillSteal { get; set; } = true;
    public decimal OtherPlayerRadius { get; set; } = 15;
    public bool GreetPlayers { get; set; } = true;
    public decimal GreetingRadius { get; set; } = 25;
    public List<AvoidRule> AvoidNames { get; set; } = [new("Mad Katz", 15)];
    public bool ClearNearbyEnemies { get; set; } = true;
    public decimal NearbyEnemyRadius { get; set; } = 6;
    public bool AutomaticRouting { get; set; } = true;
    public bool GuideTreasureChests { get; set; } = true;
    public string GroupTankName { get; set; } = "";
    public bool GroupMode { get; set; }
    public decimal GroupFollowDistance { get; set; } = 4;
    public decimal GroupAttackRadius { get; set; } = 6;
    public decimal GroupFollowLimit { get; set; } = 150;
    public static string PathName => Path.Combine(AppContext.BaseDirectory, "settings.json");
    public static Options Read()
    {
        if (File.Exists(PathName)) return JsonSerializer.Deserialize<Options>(File.ReadAllText(PathName)) ?? new();
        using var defaults = typeof(Options).Assembly.GetManifestResourceStream("PoteHunter.DefaultSettings.json");
        return defaults == null ? new() : JsonSerializer.Deserialize<Options>(defaults) ?? new();
    }
    public void Save() { File.WriteAllText(PathName + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(PathName + ".tmp", PathName, true); }
}


