namespace PoteHunter;

public enum Threat { Unknown, Grey, Green, Yellow, Orange, Red, Gold, Magenta, Cyan }
public readonly record struct MonsterDefinition(int Level, int Category, string Name = "", string Model = "")
{
    public Threat Difficulty(int playerLevel) => Category switch
    {
        5 => Threat.Gold,
        9 => Threat.Magenta,
        10 => Threat.Cyan,
        12 => Threat.Red,
        13 => Threat.Unknown,
        _ when Level is >= 1 and <= 250 && playerLevel is >= 1 and <= 250 => FromLevels(Level, playerLevel),
        _ => Threat.Unknown
    };
    public static Threat FromLevels(int monsterLevel, int playerLevel) => (monsterLevel - playerLevel) switch
    {
        >= 3 => Threat.Red,
        >= 1 => Threat.Orange,
        >= -2 => Threat.Yellow,
        >= -10 => Threat.Green,
        _ => Threat.Grey
    };
    public static Color DisplayColor(Threat value) => Color.FromArgb(unchecked((int)(value switch
    {
        Threat.Grey => 0xFFA4A4A4u,
        Threat.Green => 0xFF6EAB00u,
        Threat.Yellow => 0xFFFFFF00u,
        Threat.Orange => 0xFFFFA500u,
        Threat.Red => 0xFFFF0000u,
        Threat.Gold => 0xFFFFD700u,
        Threat.Magenta => 0xFFFF00FFu,
        Threat.Cyan => 0xFF00D2FFu,
        _ => 0xFFE0E0E0u
    })));
}
