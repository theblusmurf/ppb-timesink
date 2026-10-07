using System.Drawing.Drawing2D;

namespace PoteHunter;

/// <summary>A passive route instrument. All positions arrive from existing UI readings.</summary>
internal sealed class OrbitalScanner : Control
{
    (int Slot, SavedNavigationRoute Route)[] routes = [];
    Vec? player;
    Vec[] observed = [];
    Vec? farmingAnchor;
    double corridor = 10, farmingRadius = 35, heading;
    int? zone;
    bool connected;
    internal bool PlayerKnown => player.HasValue;
    internal Vec? PlayerPosition => player;
    internal int RouteCount => routes.Length;
    internal bool CompactSnapshot;
    internal IReadOnlyList<RectangleF> LastLabelBounds { get; private set; } = [];
    internal string ReadingStatus { get; private set; } = "Connect the game to read your current zone.";

    internal OrbitalScanner()
    {
        Name = "overviewRouteMap";
        Dock = DockStyle.Fill; Margin = Padding.Empty;
        BackColor = ImperialTheme.Window; ForeColor = ImperialTheme.Text;
        AccessibleName = "Circular current-zone route scanner";
        AccessibleRole = AccessibleRole.Graphic;
        TabStop = false; SetStyle(ControlStyles.Selectable, false);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    internal void SetReadings(bool isConnected, int? currentZone,
        (int Slot, SavedNavigationRoute Route)[] savedRoutes, Vec? currentPlayer, double playerHeading,
        Vec[] seenMonsters, double routeCorridor, Vec? anchor, double anchorRadius)
    {
        connected = isConnected; zone = currentZone;
        // Invalid or stale positions never produce a white player marker.
        player = isConnected && currentPlayer is Vec p && p.Finite ? p : null;
        routes = currentZone.HasValue ? savedRoutes.Where(r => r.Route.Zone == currentZone.Value &&
            r.Route.Anchor.Finite && r.Route.Points.All(p => p.Finite)).ToArray() : [];
        observed = player.HasValue ? seenMonsters.Where(p => p.Finite).Take(128).ToArray() : [];
        heading = double.IsFinite(playerHeading) ? playerHeading : 0;
        corridor = double.IsFinite(routeCorridor) ? Math.Clamp(routeCorridor, .5, 30) : 10;
        farmingAnchor = anchor is Vec a && a.Finite ? a : null;
        farmingRadius = double.IsFinite(anchorRadius) ? Math.Clamp(anchorRadius, 5, 150) : 35;
        ReadingStatus = !isConnected ? "Connect the game to read your current zone."
            : !currentZone.HasValue ? "Waiting for a known zone and player position."
            : !player.HasValue ? $"Zone {currentZone} · player position unavailable"
            : routes.Length == 0 ? $"Zone {currentZone} · no saved routes for this target"
            : $"Zone {currentZone} · {routes.Length} saved route{(routes.Length == 1 ? "" : "s")} · live player";
        AccessibleDescription = ReadingStatus + ". Lime: Primary; violet: Alternative 1; rose: Alternative 2. White: your verified position.";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
        LastLabelBounds = [];
        if (Width < 80 || Height < 80) return;
        if(CompactSnapshot) { DrawSnapshot(g); return; }
        float dpi = DeviceDpi / 96f;
        float diameter = Math.Max(1, Math.Min(Width - 42 * dpi, Height - 74 * dpi));
        var center = new PointF(Width / 2f, (Height - 22 * dpi) / 2f);
        float radius = diameter / 2, inner = Math.Max(1, radius - 22 * dpi);
        RectangleF circle = new(center.X - inner, center.Y - inner, inner * 2, inner * 2);
        using var outer = new Pen(ImperialTheme.Border, dpi);
        using var grid = new Pen(Color.FromArgb(45, 58, 63), .7f * dpi);
        using var highlight = new Pen(Color.FromArgb(104, 125, 70), dpi);
        using var fill = new SolidBrush(Color.FromArgb(20, 29, 33));
        g.FillEllipse(fill, circle);
        g.DrawEllipse(outer, center.X - radius, center.Y - radius, diameter, diameter);
        g.DrawEllipse(highlight, circle);
        for (int i = 0; i < 72; i++)
        {
            double a = i * Math.PI / 36;
            float start = radius - (i % 3 == 0 ? 11 : 6) * dpi;
            g.DrawLine(outer, center.X + (float)Math.Cos(a) * start, center.Y + (float)Math.Sin(a) * start,
                center.X + (float)Math.Cos(a) * (radius - 2 * dpi), center.Y + (float)Math.Sin(a) * (radius - 2 * dpi));
        }
        var state = g.Save();
        using (var clip = new GraphicsPath())
        {
            clip.AddEllipse(circle); g.SetClip(clip, CombineMode.Intersect);
        }
        for (float x = center.X % (28 * dpi); x < Width; x += 28 * dpi) g.DrawLine(grid, x, circle.Top, x, circle.Bottom);
        for (float y = center.Y % (28 * dpi); y < Height; y += 28 * dpi) g.DrawLine(grid, circle.Left, y, circle.Right, y);
        for (int i = 1; i < 3; i++)
        {
            float r = inner * i / 3;
            g.DrawEllipse(grid, center.X - r, center.Y - r, r * 2, r * 2);
        }

        var labels = new List<(PointF Point, string Text, Color Color)>();
        var points = routes.SelectMany(r => r.Route.Points.Append(r.Route.Anchor)).ToList();
        if (player is Vec current) points.Add(current);
        if (points.Count > 0)
        {
            // Fit the route bounds to the inscribed square. Every anchor remains inside the circle.
            double minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
            double width = Math.Max(30, maxX - minX + corridor * 2), height = Math.Max(30, maxY - minY + corridor * 2);
            float scale = (float)Math.Min(inner * 1.22 / width, inner * 1.22 / height);
            double midX = (minX + maxX) / 2, midY = (minY + maxY) / 2;
            PointF Project(Vec p) => new(center.X + (float)(p.X - midX) * scale, center.Y - (float)(p.Y - midY) * scale);
            if (farmingAnchor is Vec home)
            {
                var at = Project(home); float r = (float)farmingRadius * scale;
                using var area = new Pen(Color.FromArgb(90, 140, 191, 150), dpi) { DashStyle = DashStyle.Dash };
                g.DrawEllipse(area, at.X - r, at.Y - r, r * 2, r * 2);
            }
            Color[] colors = [ImperialTheme.Accent, ImperialTheme.RouteBlue, ImperialTheme.RouteRose];
            foreach (var (slot, route) in routes)
            {
                Color color = colors[Math.Clamp(slot, 0, 2)]; var path = route.Points.Select(Project).ToArray();
                using var line = new Pen(color, 2 * dpi) { LineJoin = LineJoin.Round };
                if (path.Length > 1)
                {
                    if (RecoveryTravel.Recorded(route))
                    {
                        using var area = new Pen(Color.FromArgb(22, color), Math.Max(dpi, (float)corridor * 2 * scale))
                        { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                        g.DrawLines(area, path);
                    }
                    if (slot > 0) line.DashStyle = DashStyle.Dash;
                    g.DrawLines(line, path);
                    var origin = Project(route.RevivalOrigin); g.DrawRectangle(line, origin.X - 3 * dpi, origin.Y - 3 * dpi, 6 * dpi, 6 * dpi);
                }
                var at = Project(route.Anchor);
                using var marker = new SolidBrush(color);
                g.FillEllipse(marker, at.X - 4 * dpi, at.Y - 4 * dpi, 8 * dpi, 8 * dpi);
                var facing = Movement.FromClientHeading(route.Heading);
                g.DrawLine(line, at, new PointF(at.X + (float)facing.X * 14 * dpi, at.Y - (float)facing.Y * 14 * dpi));
                labels.Add((at, slot == 0 ? "Primary anchor" : "Alternative " + slot, color));
            }
            using var dot = new SolidBrush(Color.FromArgb(213, 131, 98));
            foreach (var monster in observed)
            {
                var at = Project(monster); g.FillEllipse(dot, at.X - 2 * dpi, at.Y - 2 * dpi, 4 * dpi, 4 * dpi);
            }
            if (player is Vec self)
            {
                var at = Project(self);
                using var halo = new Pen(Color.FromArgb(120, Color.White), dpi);
                g.DrawEllipse(halo, at.X - 11 * dpi, at.Y - 11 * dpi, 22 * dpi, 22 * dpi);
                var pstate = g.Save(); g.TranslateTransform(at.X, at.Y);
                var forward = Movement.FromClientHeading(heading);
                g.RotateTransform((float)(Math.Atan2(-forward.Y, forward.X) * 180 / Math.PI + 90));
                g.FillPolygon(Brushes.White, new PointF[] { new(0, -7 * dpi), new(5 * dpi, 6 * dpi), new(0, 3 * dpi), new(-5 * dpi, 6 * dpi) });
                g.Restore(pstate); labels.Add((at, "You", Color.White));
            }
        }
        g.Restore(state);
        using var labelFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        var occupied = new List<RectangleF>();
        foreach (var (point, text, color) in labels)
        {
            SizeF measured = g.MeasureString(text, labelFont); float w = measured.Width + 14 * dpi, h = measured.Height + 8 * dpi;
            var allowed = RectangleF.Inflate(circle, -4 * dpi, -4 * dpi);
            RectangleF? placed = null;
            // Test candidates before accepting them. Never clamp a collided label onto another label.
            foreach (float x in new[] { point.X + 8 * dpi, point.X - w - 8 * dpi })
            {
                foreach (int offset in new[] { 0, 1, -1, 2, -2, 3, -3, 4, -4 })
                {
                    var candidate = new RectangleF(Math.Clamp(x, allowed.Left, Math.Max(allowed.Left, allowed.Right - w)),
                        point.Y - h - 4 * dpi + offset * (h + 4 * dpi), w, h);
                    if (!allowed.Contains(candidate) || occupied.Any(r => r.IntersectsWith(candidate))) continue;
                    placed = candidate; break;
                }
                if (placed.HasValue) break;
            }
            // Crowded/coincident anchors get a bounded key list in the instrument.
            if (!placed.HasValue)
                for (float y = allowed.Top; y + h <= allowed.Bottom; y += h + 4 * dpi)
                {
                    var candidate = new RectangleF(allowed.Left, y, w, h);
                    if (!allowed.Contains(candidate) || occupied.Any(r => r.IntersectsWith(candidate))) continue;
                    placed = candidate; break;
                }
            if (!placed.HasValue) continue;
            var rect = placed.Value; occupied.Add(rect);
            using var plate = new SolidBrush(ImperialTheme.Surface); using var edge = new Pen(ImperialTheme.Border);
            g.FillRectangle(plate, rect); g.DrawRectangle(edge, rect.X, rect.Y, rect.Width, rect.Height);
            using var ink = new SolidBrush(color); g.DrawString(text, labelFont, ink, rect.X + 7 * dpi, rect.Y + 4 * dpi);
        }
        LastLabelBounds = occupied.ToArray();
        if (points.Count == 0)
        {
            using var titleFont = new Font("Segoe UI Semibold", 12f);
            using var bodyFont = new Font("Segoe UI", 9f);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var ink = new SolidBrush(ImperialTheme.Text); using var muted = new SolidBrush(ImperialTheme.Muted);
            bool compact = inner < 115 * dpi;
            g.DrawString(compact ? connected ? "NO ROUTE" : "OFFLINE" : connected ? "NO ROUTE IN THIS ZONE" : "AWAITING CONNECTION", titleFont, ink,
                new RectangleF(circle.Left + 12 * dpi, center.Y - 31 * dpi, circle.Width - 24 * dpi, 28 * dpi), format);
            g.DrawString(compact ? connected ? "Home: record\nEnd: save" : "Connect to view\nyour route." : connected ? "Record with Home. Save at the anchor with End." : "Your routes and player position appear here.", bodyFont, muted,
                new RectangleF(circle.Left + 18 * dpi, center.Y, circle.Width - 36 * dpi, 48 * dpi), format);
        }
        using var small = new Font("Consolas", 8f);
        TextRenderer.DrawText(g, "N", small, new Point((int)(center.X - 4 * dpi), (int)(center.Y - radius - 18 * dpi)), ImperialTheme.Muted);
        TextRenderer.DrawText(g, "S", small, new Point((int)(center.X - 4 * dpi), (int)(center.Y + radius + 3 * dpi)), ImperialTheme.Muted);
        TextRenderer.DrawText(g, ReadingStatus, small, new Rectangle(4, Height - (int)(23 * dpi), Width - 8, (int)(20 * dpi)), ImperialTheme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    void DrawSnapshot(Graphics g)
    {
        float dpi = DeviceDpi / 96f;
        var canvas = new RectangleF(10 * dpi, 8 * dpi, Width - 20 * dpi, Height - 38 * dpi);
        using var grid = new Pen(Color.FromArgb(45, 58, 63), .7f * dpi);
        for(float x = canvas.Left; x <= canvas.Right; x += 28 * dpi) g.DrawLine(grid, x, canvas.Top, x, canvas.Bottom);
        for(float y = canvas.Top; y <= canvas.Bottom; y += 28 * dpi) g.DrawLine(grid, canvas.Left, y, canvas.Right, y);
        var points = routes.SelectMany(r => r.Route.Points.Append(r.Route.Anchor)).ToList();
        if(player is Vec current) points.Add(current);
        using var small = new Font("Segoe UI", 8.5f);
        if(points.Count == 0)
        {
            TextRenderer.DrawText(g, connected ? "No saved route in this zone" : "Connect to view your route", small,
                Rectangle.Round(canvas), ImperialTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        else
        {
            float inset = 12 * dpi;
            double minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
            double width = Math.Max(30, maxX - minX + corridor * 2), height = Math.Max(30, maxY - minY + corridor * 2);
            float scale = (float)Math.Min((canvas.Width - inset * 2) / width, (canvas.Height - inset * 2) / height);
            PointF Project(Vec p) => new(canvas.Left + canvas.Width / 2 + (float)(p.X - (minX + maxX) / 2) * scale,
                canvas.Top + canvas.Height / 2 - (float)(p.Y - (minY + maxY) / 2) * scale);
            var saved = g.Save(); g.SetClip(canvas);
            if(farmingAnchor is Vec home)
            {
                var at = Project(home); float r = (float)farmingRadius * scale;
                using var area = new Pen(Color.FromArgb(90, 140, 191, 150), dpi) { DashStyle = DashStyle.Dash };
                g.DrawEllipse(area, at.X - r, at.Y - r, r * 2, r * 2);
            }
            Color[] colors = [ImperialTheme.Accent, ImperialTheme.RouteBlue, ImperialTheme.RouteRose];
            foreach(var (slot, route) in routes)
            {
                var color = colors[Math.Clamp(slot, 0, 2)]; var path = route.Points.Select(Project).ToArray();
                using var line = new Pen(color, 2 * dpi) { LineJoin = LineJoin.Round, DashStyle = slot > 0 ? DashStyle.Dash : DashStyle.Solid };
                if(path.Length > 1)
                {
                    if(RecoveryTravel.Recorded(route))
                    {
                        using var corridorFill = new Pen(Color.FromArgb(22, color), Math.Max(dpi, (float)corridor * 2 * scale));
                        g.DrawLines(corridorFill, path);
                    }
                    g.DrawLines(line, path);
                }
                var at = Project(route.Anchor); using var marker = new SolidBrush(color);
                g.FillEllipse(marker, at.X - 4 * dpi, at.Y - 4 * dpi, 8 * dpi, 8 * dpi);
            }
            if(player is Vec self)
            {
                var at = Project(self); using var marker = new SolidBrush(Color.White);
                g.FillEllipse(marker, at.X - 4 * dpi, at.Y - 4 * dpi, 8 * dpi, 8 * dpi);
                var forward = Movement.FromClientHeading(heading); using var facing = new Pen(Color.White, 2 * dpi);
                g.DrawLine(facing, at, new PointF(at.X + (float)forward.X * 14 * dpi, at.Y - (float)forward.Y * 14 * dpi));
            }
            g.Restore(saved);
        }
        TextRenderer.DrawText(g, ReadingStatus, small, new Rectangle(4, Height - (int)(24 * dpi), Width - 8, (int)(22 * dpi)), ImperialTheme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
