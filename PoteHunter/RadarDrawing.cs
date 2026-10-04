namespace PoteHunter;

public sealed partial class HunterForm
{
    /// <summary>Draws currently loaded living monsters in the shared navigation projection.</summary>
    void DrawRadarMonsters(Graphics g, Size canvasSize)
    {
        if (canvasSize.Width <= 0 || canvasSize.Height <= 0) return;
        double span = Math.Max(10, NavigationViewRadius());
        float scale = Math.Min(canvasSize.Width, canvasSize.Height) / (float)(span * 2);
        float cx = canvasSize.Width / 2f, cy = canvasSize.Height / 2f;
        Vec center=NavigationViewCenter();
        PointF Project(Vec v) => new(cx + (float)((v.X - center.X) * scale), cy - (float)((v.Y - center.Y) * scale));
        using var normal = new SolidBrush(Color.FromArgb(235, 244, 76, 54));
        using var unknown = new SolidBrush(Color.FromArgb(235, 255, 155, 45));
        using var engagedPen = new Pen(Color.FromArgb(235, 55, 220, 205), 1.5f);
        using var engagedFill = new SolidBrush(Color.FromArgb(245, 55, 220, 205));
        int count = 0;
        foreach (var entity in entities)
        {
            if (!entity.Monster || !entity.Position.Finite) continue;
            var hp = latestHealth.GetValueOrDefault(entity.Id);
            if (hp.Dead) continue;
            double distance = (entity.Position - navigationPosition).Length;
            if (!double.IsFinite(distance) || distance > span) continue;
            PointF point = Project(entity.Position);
            g.FillEllipse(hp.Known ? normal : unknown, point.X - 2.5f, point.Y - 2.5f, 5f, 5f);
            if (encounter.IsEngaged(entity))
            {
                g.DrawEllipse(engagedPen, point.X - 5f, point.Y - 5f, 10f, 10f);
                g.FillEllipse(engagedFill, point.X - 1f, point.Y - 1f, 2f, 2f);
            }
            count++;
        }
        if (count > 0)
        {
            using var font = new Font("Segoe UI", 7.5f);
            using var background = new SolidBrush(Color.FromArgb(175, 10, 20, 34));
            string text = $"Monsters in view: {count}";
            SizeF size = g.MeasureString(text, font);
            g.FillRectangle(background, 5, canvasSize.Height - size.Height - 4, size.Width + 6, size.Height + 3);
            g.DrawString(text, font, Brushes.Gainsboro, 8, canvasSize.Height - size.Height - 2);
        }
    }
}
