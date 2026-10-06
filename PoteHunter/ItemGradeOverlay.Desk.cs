using System.Drawing.Drawing2D;

namespace PoteHunter;

internal sealed partial class ItemGradeOverlay
{
    static readonly Color DeskText = Color.FromArgb(239, 242, 239), DeskMuted = Color.FromArgb(164, 177, 184);
    static readonly Color Lime = Color.FromArgb(214, 236, 119), Card = Color.FromArgb(32, 48, 41);
    readonly Font small = new("Segoe UI", 10, FontStyle.Regular, GraphicsUnit.Pixel);
    readonly Font body = new("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
    readonly Font strong = new("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
    readonly Font gradeFont = new("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Pixel);
    readonly Font title = new("Segoe UI", 22, FontStyle.Bold, GraphicsUnit.Pixel);
    readonly Font metric = new("Segoe UI", 25, FontStyle.Bold, GraphicsUnit.Pixel);
    string focusStat = "Auto";
    float paintScale = 1;
    internal ItemStatPlan? FocusedStat => Plan is { } plan ? ItemGradeDesk.Focus(plan, focusStat) : null;
    void DisposeDeskFonts() { small.Dispose(); body.Dispose(); strong.Dispose(); gradeFont.Dispose(); title.Dispose(); metric.Dispose(); }
    void SizeFor(Size available, int dpi)
    {
        var logical = new Size(ItemGradeDesk.Width, ItemGradeDesk.Height(Plan));
        paintScale = ItemGradeDesk.FitScale(logical, available, dpi);
        ClientSize = new(Math.Max(1, (int)(logical.Width * paintScale)), Math.Max(1, (int)(logical.Height * paintScale)));
    }
    void DrawText(Graphics g, string text, Font font, Color color, RectangleF rect, bool wrap = false, bool right = false)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter,
            Alignment = right ? StringAlignment.Far : StringAlignment.Near, FormatFlags = wrap ? 0 : StringFormatFlags.NoWrap };
        g.DrawString(text, font, brush, rect, format);
    }
    static GraphicsPath Round(RectangleF rect, float radius = 8)
    {
        var path = new GraphicsPath(); float d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
    }
    static void Box(Graphics g, RectangleF rect, Color fill, Color edge)
    {
        using var path = Round(rect); using var brush = new SolidBrush(fill); using var pen = new Pen(edge);
        g.FillPath(brush, path); g.DrawPath(pen, path);
    }
    void Chip(Graphics g, string text, ItemGrade? grade, RectangleF rect)
    {
        var color = GradeColor(grade); Box(g, rect, Color.FromArgb(26, color), color);
        DrawText(g, text, gradeFont, color, new(rect.X + 7, rect.Y + 4, rect.Width - 14, rect.Height - 5));
    }
    void Metric(Graphics g, string value, string label, int x)
    {
        Box(g, new(x, 128, 170, 67), Card, Border);
        DrawText(g, value, metric, Lime, new(x + 12, 136, 146, 32)); DrawText(g, label, small, DeskMuted, new(x + 12, 171, 146, 18));
    }
    void PaintDesk(Graphics g)
    {
        var saved = g.Save(); g.ScaleTransform(paintScale, paintScale); g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        int height = ItemGradeDesk.Height(Plan);
        Box(g, new(1, 1, ItemGradeDesk.Width - 2, height - 2), Background, Border);
        DrawText(g, "EQUIPMENT / UPGRADE DESK", small, DeskMuted, new(22, 18, 310, 18));
        DrawText(g, ClickThrough ? "● PASSIVE OVERLAY" : "● READ ONLY", small, Lime, new(380, 18, 168, 18), right: true);
        if (Plan is not { } plan)
        {
            DrawText(g, "Equipment insight", title, DeskText, new(22, 60, 526, 34));
            DrawText(g, message ?? "Hover a weapon or armor to see its stats.", body, DeskMuted, new(22, 107, 526, 100), wrap: true);
            DrawText(g, "No item changes · game focus preserved", small, DeskMuted, new(22, height - 30, 526, 18));
            g.Restore(saved); return;
        }
        var focused = FocusedStat;
        ItemGrade? best = plan.Stats.Where(s => s.Graded).Select(s => s.Grade).OrderByDescending(v => v).FirstOrDefault();
        DrawItemIcon(g, plan.Profile?.GemGroup);
        DrawText(g, plan.Profile?.Name ?? plan.Name, title, DeskText, new(84, 48, 357, 32));
        string identity = plan.Profile is { } p ? $"{p.Type} · Requires {p.Requirement} {p.RequirementStat}" : "Grade table unavailable";
        DrawText(g, identity, body, DeskMuted, new(84, 86, 357, 20));
        Chip(g, plan.ItemGradeLabel, best, new(456, 54, 92, 30)); DrawText(g, "BEST STAT", small, DeskMuted, new(456, 89, 92, 18), right: true);
        using var line = new Pen(Color.FromArgb(48, DeskMuted)); g.DrawLine(line, 22, 114, 548, 114);
        Metric(g, focused is null ? "—" : ItemGradeDesk.Number(focused.Value), (focused?.Label ?? "No stat") + " · current", 22);
        Metric(g, ItemGradeDesk.Gap(focused), focused?.Target is { } t ? $"Points to {t}" : "No eligible focus", 200);
        Metric(g, focused?.Target?.ToString() ?? plan.FixedTarget?.ToString() ?? "Auto", "Target grade", 378);
        DrawText(g, "STAT OVERVIEW · " + (plan.FixedTarget?.ToString() ?? "AUTO"), small, DeskMuted, new(22, 214, 252, 18));
        DrawText(g, "FOCUS STAT · " + (focused?.Label ?? "NONE"), small, DeskMuted, new(298, 214, 250, 18));
        int contentBottom = height - 76; g.DrawLine(line, 282, 214, 282, contentBottom - 8);
        for (int i = 0; i < plan.Stats.Count; i++)
        {
            var stat = plan.Stats[i]; int y = ItemGradeDesk.StatsTop + i * ItemGradeDesk.RowHeight; bool selected = stat == focused;
            if (selected) Box(g, new(17, y - 3, 258, 57), Color.FromArgb(29, 43, 40), Border);
            DrawText(g, stat.Label, body, selected ? Lime : DeskText, new(22, y, 132, 18));
            DrawText(g, ItemGradeDesk.Number(stat.Value), strong, DeskText, new(152, y, 65, 18), right: true);
            Chip(g, stat.Grade?.ToString() ?? "?", stat.Grade, new(219, y - 1, 52, 23));
            using var track = new SolidBrush(Color.FromArgb(46, 60, 65)); g.FillRectangle(track, 22, y + 27, 248, 4);
            if (ItemGradeDesk.Ratio(plan, stat) is double ratio)
            { using var fill = new SolidBrush(Lime); g.FillRectangle(fill, 22, y + 27, (float)(248 * ratio), 4); }
            DrawText(g, ItemGradeDesk.ProgressText(plan, stat), small, DeskMuted, new(22, y + 36, 190, 17));
            DrawText(g, ItemGradeDesk.Gap(stat), small, DeskText, new(215, y + 36, 55, 17), right: true);
        }
        if (plan.Stats.Count == 0) DrawText(g, "No readable stats for this item.", body, DeskMuted, new(22, 240, 250, 70), wrap: true);
        DrawRecipe(g, focused, plan);
        g.DrawLine(line, 22, contentBottom + 9, 548, contentBottom + 9);
        DrawText(g, "Target: " + plan.TargetLabel + " · Focus stat set in World & Tools > Item grades", small, DeskMuted, new(22, contentBottom + 18, 526, 18));
        DrawText(g, footer.Length > 0 ? footer : "Read only · no item changes", small, DeskMuted, new(22, contentBottom + 40, 526, 27), wrap: true);
        g.Restore(saved);
    }
    void DrawRecipe(Graphics g, ItemStatPlan? stat, ItemGradePlan plan)
    {
        if (stat is null) { DrawText(g, ItemGradeDesk.EmptyFocusMessage(plan), body, DeskMuted, new(298, 242, 250, 80), wrap: true); return; }
        DrawText(g, !stat.Graded ? "Grade unavailable" : stat.Needed == 0 ? "Target reached" : stat.Target is null || stat.Needed is null ? "Target unavailable" : $"A path to {stat.Target}", strong, DeskText, new(298, 240, 250, 22));
        Box(g, new(298, 273, 250, 77), Card, Border);
        using(var gem = new SolidBrush(GemColor(stat.GemName)))
            g.FillPolygon(gem, [new PointF(312, 295), new(324, 285), new(336, 295), new(324, 320)]);
        DrawText(g, ItemGradeDesk.GemSummary(stat), strong, DeskText, new(346, 284, 192, 40), wrap: true);
        DrawText(g, stat.Graded && stat.Needed is not null ? "Current target · regular gem tier" : "No recommendation", small, DeskMuted, new(346, 327, 192, 18));
        DrawText(g, stat.Projection is { AtPlusTen: true } ? "AAA socket scenario / estimate" : "+10 scenario / estimate", strong,
            Color.FromArgb(203, 183, 149), new(298, 371, 250, 22));
        DrawText(g, ItemGradeDesk.ProjectionText(stat), body, DeskText, new(298, 404, 250, 68), wrap: true);
        DrawText(g, "Estimates assume available sockets. Upgrade bonuses and sockets beyond the two mapped fields are unconfirmed. This plan applies to the focus stat.",
            small, DeskMuted, new(298, 482, 250, 66), wrap: true);
    }
    static Color GemColor(string? name) => name switch
    { "BlackMoon" => Color.FromArgb(180, 163, 207), "Sapphire" => Color.FromArgb(138, 169, 223), "Emerald" => Color.FromArgb(149, 195, 138),
        "Diamond" => Color.FromArgb(183, 219, 230), _ => Color.FromArgb(215, 127, 135) };
    static void DrawItemIcon(Graphics g, ItemGemGroup? group)
    {
        Box(g, new(22, 52, 49, 49), Card, Border); using var pen = new Pen(Color.FromArgb(231, 188, 112), 2);
        if (group is ItemGemGroup.Armor or ItemGemGroup.Shield)
            g.DrawPolygon(pen, [new Point(32, 62), new(46, 57), new(61, 62), new(58, 82), new(46, 94), new(35, 82)]);
        else { g.DrawLine(pen, 35, 88, 58, 63); g.DrawLine(pen, 34, 77, 46, 88); g.DrawLine(pen, 55, 64, 58, 58); }
    }
    // Real native rendering, without showing a window, connecting a client or registering hotkeys.
    internal Bitmap RenderPreview(ItemGradePlan? plan, string focus = "Auto", Size? available = null, int dpi = 96, string? status = null)
    {
        Plan = plan; message = status; footer = "Gem / +10 estimates · sockets beyond two fields unconfirmed";
        focusStat = ItemGradeDesk.NormalizeFocus(focus); SizeFor(available ?? new(1920, 1080), dpi);
        var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
        using var graphics = Graphics.FromImage(bitmap); graphics.Clear(Background); PaintDesk(graphics); return bitmap;
    }
}
