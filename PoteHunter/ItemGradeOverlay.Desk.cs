using System.Drawing.Drawing2D;

namespace PoteHunter;

internal sealed partial class ItemGradeOverlay
{
    static readonly Color DeskText = Color.FromArgb(239, 242, 239), DeskMuted = Color.FromArgb(164, 177, 184);
    static readonly Color Lime = Color.FromArgb(214, 236, 119), Card = Color.FromArgb(32, 48, 41);
    readonly Font small = new("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel);
    readonly Font body = new("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);
    readonly Font strong = new("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
    readonly Font gradeFont = new("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Pixel);
    readonly Font title = new("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
    readonly Font metric = new("Segoe UI", 23, FontStyle.Bold, GraphicsUnit.Pixel);
    string focusStat = "Auto";
    float paintScale = 1;
    int scalePercent = 100;
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int ScalePercent { get => scalePercent; set => scalePercent = ItemGradeDesk.NormalizeScale(value); }
    internal ItemStatPlan? FocusedStat => Plan is { } plan ? ItemGradeDesk.Focus(plan, focusStat) : null;
    void DisposeDeskFonts() { small.Dispose(); body.Dispose(); strong.Dispose(); gradeFont.Dispose(); title.Dispose(); metric.Dispose(); }
    void SizeFor(Size available, int dpi)
    {
        var logical = new Size(ItemGradeDesk.Width, ItemGradeDesk.Height(Plan));
        paintScale = ItemGradeDesk.FitScale(logical, available, dpi, ScalePercent);
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
        var color = GradeColor(grade); Box(g, rect, Color.FromArgb(26, color), Color.FromArgb(45, color));
        DrawText(g, text, gradeFont, color, new(rect.X + 3, rect.Y + 2, rect.Width - 6, rect.Height - 2), right: true);
    }
    void PaintDesk(Graphics g)
    {
        var saved = g.Save(); g.ScaleTransform(paintScale, paintScale); g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        int height = ItemGradeDesk.Height(Plan);
        Box(g, new(1, 1, ItemGradeDesk.Width - 2, height - 2), Background, Border);
        if (Plan is not { } plan)
        {
            DrawText(g, "Equipment insight", title, DeskText, new(14, 17, 296, 26));
            DrawText(g, message ?? "Hover a weapon or armor to see its stats.", body, DeskMuted, new(14, 53, 296, 105), wrap: true);
            DrawText(g, "Read only · game focus preserved", small, DeskMuted, new(14, height - 26, 296, 18));
            g.Restore(saved); return;
        }
        var focused = FocusedStat;
        ItemGrade? best = plan.Stats.Where(s => s.Graded).Select(s => s.Grade).OrderByDescending(v => v).FirstOrDefault();
        DrawItemIcon(g, plan.Profile?.GemGroup);
        DrawText(g, plan.Profile?.Name ?? plan.Name, title, DeskText, new(52, 13, 182, 38), wrap: true);
        string identity = plan.Profile is { } p ? $"{p.Type} · {p.Requirement} {p.RequirementStat}" : "Grade table unavailable";
        DrawText(g, identity, small, DeskMuted, new(52, 53, 182, 29), wrap: true);
        DrawText(g, "Best stat", small, DeskMuted, new(242, 14, 69, 18), right: true);
        Chip(g, plan.ItemGradeLabel, best, new(242, 35, 69, 24));
        Box(g, new(12, 88, 300, 65), Card, Color.FromArgb(48, Lime));
        using (var accent = new SolidBrush(Lime)) g.FillRectangle(accent, 12, 95, 3, 51);
        string focusLabel = (focusStat == "Auto" ? "AUTO · " : "FOCUS · ") + (focused?.Label ?? "NONE");
        DrawText(g, focusLabel, small, Lime, new(24, 94, 210, 18));
        DrawText(g, focused?.Target is { } t ? t + " target" : plan.TargetLabel, small, DeskMuted, new(234, 94, 66, 18), right: true);
        if (focused is not null)
        {
            DrawText(g, ItemGradeDesk.Number(focused.Value), metric, DeskText, new(24, 117, 106, 31));
            string threshold = ItemGradeDesk.Threshold(plan, focused) is int value ? "→ " + ItemGradeDesk.Number(value) : "→ —";
            DrawText(g, threshold, strong, Lime, new(133, 124, 83, 20));
            DrawText(g, ItemGradeDesk.Gap(focused), small, Lime, new(215, 125, 85, 18), right: true);
        }
        else DrawText(g, ItemGradeDesk.EmptyFocusMessage(plan), small, DeskMuted, new(24, 116, 276, 31), wrap: true);
        DrawText(g, "Stat", small, DeskMuted, new(14, 163, 92, 18));
        DrawText(g, "Value", small, DeskMuted, new(108, 163, 67, 18), right: true);
        DrawText(g, "Grade", small, DeskMuted, new(181, 163, 39, 18));
        DrawText(g, "Target gap", small, DeskMuted, new(225, 163, 85, 18), right: true);
        using var line = new Pen(Color.FromArgb(43, 55, 61));
        for (int i = 0; i < plan.Stats.Count; i++)
        {
            var stat = plan.Stats[i]; int y = ItemGradeDesk.StatsTop + i * ItemGradeDesk.RowHeight; bool selected = stat == focused;
            g.DrawLine(line, 14, y, 310, y);
            DrawText(g, stat.Label, body, selected ? Lime : DeskText, new(14, y + 5, 93, 20));
            DrawText(g, ItemGradeDesk.Number(stat.Value), strong, DeskText, new(108, y + 5, 67, 20), right: true);
            Chip(g, stat.Grade?.ToString() ?? "?", stat.Grade, new(181, y + 4, 34, 20));
            string gap = stat.Needed == 0 ? "Reached" : (stat.Target?.ToString() ?? "?") + " " + ItemGradeDesk.Gap(stat);
            DrawText(g, gap, small, selected ? Lime : DeskMuted, new(221, y + 6, 89, 18), right: true);
        }
        if (plan.Stats.Count == 0) DrawText(g, "No readable item stats", body, DeskMuted, new(14, ItemGradeDesk.StatsTop + 5, 296, 20));
        int recipeTop = ItemGradeDesk.StatsTop + Math.Max(1, plan.Stats.Count) * ItemGradeDesk.RowHeight + 7;
        g.DrawLine(line, 14, recipeTop, 310, recipeTop);
        DrawRecipe(g, focused, recipeTop + 10);
        g.Restore(saved);
    }
    void DrawRecipe(Graphics g, ItemStatPlan? stat, int top)
    {
        using (var gem = new SolidBrush(GemColor(stat?.GemName)))
            g.FillPolygon(gem, [new PointF(15, top + 7), new(21, top + 2), new(27, top + 7), new(21, top + 16)]);
        DrawText(g, stat is null ? "No focus gem estimate" : ItemGradeDesk.GemSummary(stat), strong, DeskText, new(32, top, 278, 35), wrap: true);
        DrawText(g, "Current target · estimate", small, DeskMuted, new(14, top + 37, 296, 18));
        string projection = stat is null ? "No +10 scenario available." : ItemGradeDesk.ProjectionText(stat);
        DrawText(g, projection + " · estimate", small, DeskText, new(14, top + 57, 296, 34), wrap: true);
        DrawText(g, footer.Length > 0 ? footer : "Focus stat only. Available sockets and upgrade bonuses unconfirmed.", small, DeskMuted, new(14, top + 93, 296, 29), wrap: true);
    }
    static Color GemColor(string? name) => name switch
    { "BlackMoon" => Color.FromArgb(180, 163, 207), "Sapphire" => Color.FromArgb(138, 169, 223), "Emerald" => Color.FromArgb(149, 195, 138),
        "Diamond" => Color.FromArgb(183, 219, 230), _ => Color.FromArgb(215, 127, 135) };
    static void DrawItemIcon(Graphics g, ItemGemGroup? group)
    {
        Box(g, new(12, 16, 31, 42), Color.FromArgb(32, 43, 49), Color.FromArgb(32, 43, 49));
        using var pen = new Pen(Color.FromArgb(231, 188, 112), 1.5f);
        if (group is ItemGemGroup.Armor or ItemGemGroup.Shield)
            g.DrawPolygon(pen, [new Point(18, 26), new(27, 22), new(37, 26), new(35, 40), new(27, 49), new(20, 40)]);
        else { g.DrawLine(pen, 21, 48, 35, 28); g.DrawLine(pen, 19, 39, 28, 46); g.DrawLine(pen, 32, 29, 35, 24); }
    }
    // Native rendering without showing a window, connecting a client or registering hotkeys.
    internal Bitmap RenderPreview(ItemGradePlan? plan, string focus = "Auto", Size? available = null, int dpi = 96, string? status = null)
    {
        Plan = plan; message = status;
        focusStat = ItemGradeDesk.NormalizeFocus(focus); SizeFor(available ?? new(1920, 1080), dpi);
        var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
        using var graphics = Graphics.FromImage(bitmap); graphics.Clear(Background); PaintDesk(graphics); return bitmap;
    }
}
