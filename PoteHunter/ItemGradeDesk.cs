using System.Globalization;

namespace PoteHunter;

/// <summary>Presentation only; grades, gaps, gem counts and projections use the existing planner.</summary>
internal static class ItemGradeDesk
{
    internal sealed record FocusChoice(string Key, string Label) { public override string ToString() => Label; }
    internal static readonly FocusChoice[] FocusChoices = [new("Auto", "Auto · first upgradeable stat"),
        .. ItemGradeTable.StatOrder.Select(key => new FocusChoice(key, ItemGradeTable.DisplayName(key)))];
    internal static string NormalizeFocus(string? key) => FocusChoices.FirstOrDefault(c => c.Key == key)?.Key ?? "Auto";
    // Saved preferences absent on the next item fall back to an actionable stat, then a graded stat, then raw data.
    internal static ItemStatPlan? Focus(ItemGradePlan plan, string? preference) =>
        plan.Stats.FirstOrDefault(s => s.Stat == NormalizeFocus(preference)) ??
        plan.Stats.FirstOrDefault(s => s.Graded && s.Needed > 0 && s.Gems is { Count: > 0 }) ??
        plan.Stats.FirstOrDefault(s => s.Graded) ?? plan.Stats.FirstOrDefault();
    internal static string Number(int value) => value.ToString("N0", CultureInfo.CurrentCulture);
    internal static int? Threshold(ItemGradePlan plan, ItemStatPlan stat) =>
        stat.Target is { } target ? plan.Profile?.Stats.GetValueOrDefault(stat.Stat)?.Minimum(target) : null;
    internal static double? Ratio(ItemGradePlan plan, ItemStatPlan stat) =>
        Threshold(plan, stat) is int threshold && threshold > 0 ? Math.Clamp((double)stat.Value / threshold, 0, 1) : null;
    internal static string ProgressText(ItemGradePlan plan, ItemStatPlan stat) => !stat.Graded ? "Grade unavailable"
        : stat.Needed == 0 ? $"{stat.Target} target reached"
        : Ratio(plan, stat) is double ratio ? $"{ratio:P0} of {stat.Target} threshold" : "Target unavailable";
    internal static string Gap(ItemStatPlan? stat) => stat?.Needed is int need ? need == 0 ? "Reached" : "+" + Number(need) : "—";
    internal static ItemGemOption? RegularGem(ItemStatPlan stat) => stat.Gems?.FirstOrDefault(g => g.Name == stat.GemName);
    internal static string GemSummary(ItemStatPlan stat) => !stat.Graded ? "No grade data" : stat.Needed == 0 ? "No gems needed"
        : RegularGem(stat) is { } gem ? $"{gem.Count} regular {gem.Name} gem{(gem.Count == 1 ? "" : "s")}" : "No gem estimate available";
    internal static string ProjectionText(ItemStatPlan stat) => !stat.Graded ? "No upgrade recommendation without grade data."
        : stat.Projection is { } projection ? projection.Describe()
        : stat.Grade >= ItemGrade.AAA ? "Already AAA or better. See the current target above." : "No +10 scenario available for this stat.";
    internal const int Width = 570, RowHeight = 60, StatsTop = 238;
    internal static int Height(ItemGradePlan? plan) => plan == null ? 250 : StatsTop + Math.Max(310, plan.Stats.Count * RowHeight) + 76;
    internal static float FitScale(Size logical, Size available, int dpi) => (float)Math.Max(.01,
        Math.Min(Math.Max(1, dpi / 96.0), Math.Min((double)available.Width / logical.Width, (double)available.Height / logical.Height)));
}
