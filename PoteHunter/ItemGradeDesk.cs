using System.Globalization;

namespace PoteHunter;

/// <summary>Presentation only; grades, gaps, gem counts and projections use the existing planner.</summary>
internal static class ItemGradeDesk
{
    internal sealed record FocusChoice(string Key, string Label) { public override string ToString() => Label; }
    internal static readonly FocusChoice[] FocusChoices = [new("Auto", "Auto · closest target"),
        .. ItemGradeTable.StatOrder.Select(key => new FocusChoice(key, ItemGradeTable.DisplayName(key)))];
    internal static string NormalizeFocus(string? key) => FocusChoices.FirstOrDefault(c => c.Key == key)?.Key ?? "Auto";
    // Compare all supported unfinished targets by percentage. Reached targets never become Auto focus.
    // A saved stat absent on this item uses Auto without changing the saved preference.
    internal static ItemStatPlan? Focus(ItemGradePlan plan, string? preference) =>
        plan.Stats.FirstOrDefault(s => s.Stat == NormalizeFocus(preference)) ??
        plan.Stats.Where(s => s.Graded && s.Needed > 0 && Ratio(plan, s).HasValue)
            .OrderByDescending(s => Ratio(plan, s)).FirstOrDefault();
    internal static string EmptyFocusMessage(ItemGradePlan plan) => plan.Stats.Count == 0 ? "No readable stats for this item."
        : plan.Stats.Any(s => s.Graded && Ratio(plan, s).HasValue) &&
          plan.Stats.Where(s => s.Graded && Ratio(plan, s).HasValue).All(s => s.Needed == 0)
            ? "All supported stats have reached the selected target."
            : "No supported stat below the target is available.";
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
    internal const int Width = 324, RowHeight = 28, StatsTop = 182;
    internal const int MinimumScalePercent = 75, MaximumScalePercent = 200;
    internal static int NormalizeScale(int percent) => Math.Clamp(percent, MinimumScalePercent, MaximumScalePercent);
    internal static int Height(ItemGradePlan? plan) => plan == null ? 196 : StatsTop + Math.Max(1, plan.Stats.Count) * RowHeight + 148;
    internal static float FitScale(Size logical, Size available, int dpi, int percent = 100) => (float)Math.Max(.01,
        Math.Min(Math.Max(1, dpi / 96.0) * NormalizeScale(percent) / 100.0,
            Math.Min((double)available.Width / logical.Width, (double)available.Height / logical.Height)));
}
