namespace PoteHunter;

/// <summary>Only item category metadata establishes jewelry; display names are never used.</summary>
internal static class ItemGradeEligibility
{
    static readonly HashSet<string> JewelryCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ring", "Rings", "Necklace", "Necklaces", "Amulet", "Amulets", "Earring", "Earrings",
        "Bracelet", "Bracelets", "Jewelry", "Jewellery"
    };
    static readonly char[] CategorySeparators = ['/', '\\', '|', ',', ';', '&', '+'];

    internal static bool IsJewelry(string? gameCategory, string? tableType = null, string? tableCategory = null)
        => IsJewelryCategory(gameCategory) || IsJewelryCategory(tableType) || IsJewelryCategory(tableCategory);

    static bool IsJewelryCategory(string? category)
        => !string.IsNullOrWhiteSpace(category) && category.Split(CategorySeparators,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(JewelryCategories.Contains);
}
