using System.Runtime.InteropServices;

namespace PoteHunter;

/// <summary>
/// Small always-on-top box that shows each stat's grade, the points still needed for the target grade and the gems that
/// close the gap. It never takes focus from the game: it is shown without activation and clicks hide it without activating.
/// In click-through mode the mouse passes straight through it to the game, so the cursor never leaves the hovered item.
/// </summary>
internal sealed class ItemGradeOverlay : Form
{
    const int WsExToolWindow = 0x00000080;
    const int WsExNoActivate = 0x08000000;
    const int WsExTransparent = 0x00000020, WsExLayered = 0x00080000, GwlExstyle = -20;
    const int SwShownoactivate = 4;
    const int WmMouseactivate = 0x0021;
    const int MaNoactivateandeat = 4;
    static readonly IntPtr HWndTopmost = new(-1);
    const uint SwpNoactivate = 0x0010, SwpNomove = 0x0002, SwpNosize = 0x0001;
    internal const int AutoHideMilliseconds = 20000;
    // Colours sampled from the game's own item tooltip: near-black panel, white labels, item name and values in the grade colour.
    static readonly Color Background = Color.FromArgb(10, 9, 8);
    static readonly Color Border = Color.FromArgb(82, 77, 69);
    static readonly Color TextColor = Color.White;
    static readonly Color Requirement = Color.FromArgb(255, 255, 153);
    static readonly Color Muted = Color.FromArgb(150, 150, 150);
    static readonly Color Tail = Color.FromArgb(190, 190, 190);
    internal const int LineHeight = 16, PadX = 8, PadY = 6;

    /// <summary>The game's grade colours, read from the ARGB constants its grade switch loads at 0x527d0b (S, AAA, AA, A, B, C) and matching screenshots.</summary>
    internal static Color GradeColor(ItemGrade? grade) => grade switch
    {
        ItemGrade.C => Color.FromArgb(0, 255, 0), ItemGrade.B => Color.FromArgb(0, 255, 255), ItemGrade.A => Color.FromArgb(255, 255, 153),
        ItemGrade.AA => Color.FromArgb(255, 153, 0), ItemGrade.AAA => Color.FromArgb(255, 204, 0), ItemGrade.S => Color.FromArgb(255, 255, 0),
        _ => Color.White
    };

    readonly Font gameFont = new("Tahoma", 9f);
    readonly System.Windows.Forms.Timer hideTimer = new() { Interval = AutoHideMilliseconds };

    /// <summary>One coloured run of text; a row is a list of runs. Label rows are three runs: "Label :", value, tail.</summary>
    sealed record Cell(string Text, Color Color);
    List<Cell[]> rows = [];
    int[] columnWidths = [];
    /// <summary>Rows above this index are centred single-run lines (name, type, requirement); from it on they are stat rows laid out in columns.</summary>
    int statRowsFrom;
    string? message;
    string footer = "";

    internal ItemGradePlan? Plan { get; private set; }
    internal string? Message => message;
    /// <summary>Mouse input passes through the box to the window beneath (WS_EX_TRANSPARENT on a layered window).</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool ClickThrough { get; private set; }
    /// <summary>Hide on its own after <see cref="AutoHideMilliseconds"/>; off when the caller hides it as the game's tooltip closes.</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool AutoHide { get; set; } = true;

    internal void SetClickThrough(bool on)
    {
        ClickThrough = on;
        if (!IsHandleCreated) return;
        long style = GetWindowLongPtr(Handle, GwlExstyle).ToInt64();
        style = on ? style | WsExTransparent | WsExLayered : style & ~(long)WsExTransparent;
        SetWindowLongPtr(Handle, GwlExstyle, new IntPtr(style));
    }

    internal bool HasClickThroughStyle => (GetWindowLongPtr(Handle, GwlExstyle).ToInt64() & WsExTransparent) != 0;

    internal ItemGradeOverlay()
    {
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Background;
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        Opacity = .94;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = false;
        hideTimer.Tick += (_, _) => Hide();
        MouseDown += (_, _) => Hide();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate | WsExToolWindow;
            if (ClickThrough) parameters.ExStyle |= WsExTransparent | WsExLayered;
            return parameters;
        }
    }

    internal bool HasPassiveWindowStyles => (GetWindowLongPtr(Handle, -20).ToInt64() & (WsExNoActivate | WsExToolWindow)) == (WsExNoActivate | WsExToolWindow);

    /// <summary>Shows a graded item near anchor (screen pixels), kept inside the screen that contains the anchor.</summary>
    /// <param name="footerText">Hint under the table, e.g. "click to close".</param>
    /// <param name="preferLeft">Open left of the anchor first, so the game's own tooltip (right/below the cursor) stays visible.</param>
    internal void ShowPlan(ItemGradePlan plan, string footerText, Point anchor, bool preferLeft = false)
    {
        Plan = plan; message = null;
        rows = BuildRows(plan);
        footer = footerText;
        Present(anchor, preferLeft);
    }

    internal void ShowMessage(string text, Point anchor, bool preferLeft = false)
    {
        Plan = null; message = text;
        rows = [[new Cell(text, TextColor)]]; statRowsFrom = 1;
        footer = "";
        Present(anchor, preferLeft);
    }

    List<Cell[]> BuildRows(ItemGradePlan plan)
    {
        var result = new List<Cell[]>();
        var itemGrade = plan.Stats.Where(s => s.Graded).Select(s => s.Grade!.Value).DefaultIfEmpty(ItemGrade.C).Max();
        string name = plan.Profile?.Name ?? plan.Name;
        result.Add([new Cell(plan.ItemGradeLabel == "?" ? name : name + " " + plan.ItemGradeLabel, plan.ItemGradeLabel == "?" ? TextColor : GradeColor(itemGrade))]);
        if (plan.Profile is { } profile)
        {
            result.Add([new Cell(profile.Type, TextColor)]);
            result.Add([new Cell("", TextColor)]);
            result.Add([new Cell("Require " + profile.Requirement + " " + profile.RequirementStat, Requirement)]);
        }
        else result.Add([new Cell("No grade data for #" + plan.PrototypeId, Muted)]);
        result.Add([new Cell("", TextColor)]);
        statRowsFrom = result.Count;
        foreach (var s in plan.Stats)
        {
            string tail = s.Needed switch
            {
                null => s.Graded ? "" : "not in grade table",
                0 => s.Target + " reached",
                int need => "+" + need + " to " + s.Target + (s.Gems is { Count: > 0 } ? "  " + string.Join(" · ", s.Gems.Select(g => g.Name + " ×" + g.Count))
                    : s.Gems == null ? "  no gem" : "")
            };
            if (s.Projection is { } projection) tail += (tail.Length > 0 ? " · " : "") + projection.Describe();
            // A stat that five regular gems take to AAA on a +10 item is the cheap win, so its label is highlighted in the AAA colour.
            var labelColor = s.Projection is { FiveRegular: true } ? GradeColor(ItemGrade.AAA) : TextColor;
            result.Add([new Cell(s.Label + " : ", labelColor), new Cell(s.Value.ToString(), GradeColor(s.Grade)), new Cell(tail, Tail)]);
        }
        return result;
    }

    internal static string Subtitle(ItemGradePlan plan) => plan.Profile is { } it
        ? $"{it.Requirement} {it.RequirementStat} · target {plan.TargetLabel} · item {plan.ItemGradeLabel}"
        : $"No grade data for: {plan.Name} (#{plan.PrototypeId})";

    void Present(Point anchor, bool preferLeft)
    {
        var size = Measure();
        ClientSize = size;
        var screen = Screen.FromPoint(anchor).WorkingArea;
        Location = PlaceNear(anchor, size, screen, preferLeft);
        Invalidate();
        if (!Visible) Show(); else Refresh();
        if (IsHandleCreated) SetWindowPos(Handle, HWndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate);
        hideTimer.Stop(); if (AutoHide) hideTimer.Start();
    }

    /// <summary>Right/below the anchor when it fits (left/below with preferLeft), otherwise flipped, and always inside the screen.</summary>
    internal static Point PlaceNear(Point anchor, Size size, Rectangle screen, bool preferLeft = false)
    {
        int x = preferLeft ? anchor.X - 20 - size.Width : anchor.X + 20, y = anchor.Y + 20;
        if (preferLeft && x < screen.Left) x = anchor.X + 20;
        else if (!preferLeft && x + size.Width > screen.Right) x = anchor.X - 20 - size.Width;
        if (y + size.Height > screen.Bottom) y = anchor.Y - 20 - size.Height;
        x = Math.Max(screen.Left, Math.Min(x, screen.Right - size.Width));
        y = Math.Max(screen.Top, Math.Min(y, screen.Bottom - size.Height));
        return new Point(x, y);
    }

    Size Measure()
    {
        int width = 0;
        columnWidths = new int[3];
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            if (r < statRowsFrom) { width = Math.Max(width, TextWidth(row[0].Text)); continue; }
            for (int c = 0; c < row.Length; c++) columnWidths[c] = Math.Max(columnWidths[c], TextWidth(row[c].Text));
        }
        if (rows.Count > statRowsFrom) width = Math.Max(width, columnWidths[0] + columnWidths[1] + (columnWidths[2] > 0 ? 8 + columnWidths[2] : 0));
        int height = rows.Count * LineHeight;
        if (footer.Length > 0) { width = Math.Max(width, TextWidth(footer)); height += LineHeight; }
        return new Size(width + PadX * 2, height + PadY * 2);
    }

    int TextWidth(string text) => text.Length == 0 ? 0 : TextRenderer.MeasureText(text, gameFont, Size.Empty, TextFormatFlags.NoPadding).Width;

    protected override void SetVisibleCore(bool value)
    {
        if (!value) { hideTimer.Stop(); base.SetVisibleCore(false); return; }
        base.SetVisibleCore(true);
        if (IsHandleCreated)
        {
            ShowWindow(Handle, SwShownoactivate);
            SetWindowPos(Handle, HWndTopmost, 0, 0, 0, 0, SwpNomove | SwpNosize | SwpNoactivate);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmMouseactivate) { m.Result = (IntPtr)MaNoactivateandeat; BeginInvoke(Hide); return; }
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        using (var border = new Pen(Border, 1f)) g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        int y = PadY, inner = ClientSize.Width - PadX * 2;
        // Stat rows are a centred block: labels end at the colon, values start right after it, the gem tail follows.
        int block = columnWidths[0] + columnWidths[1] + (columnWidths[2] > 0 ? 8 + columnWidths[2] : 0), blockX = PadX + Math.Max(0, (inner - block) / 2);
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            if (r < statRowsFrom)
                TextRenderer.DrawText(g, row[0].Text, gameFont, new Rectangle(PadX, y, inner, LineHeight), row[0].Color, flags | TextFormatFlags.HorizontalCenter);
            else
            {
                TextRenderer.DrawText(g, row[0].Text, gameFont, new Rectangle(blockX, y, columnWidths[0], LineHeight), row[0].Color, flags | TextFormatFlags.Right);
                TextRenderer.DrawText(g, row[1].Text, gameFont, new Rectangle(blockX + columnWidths[0], y, columnWidths[1], LineHeight), row[1].Color, flags | TextFormatFlags.Left);
                if (row.Length > 2 && row[2].Text.Length > 0)
                    TextRenderer.DrawText(g, row[2].Text, gameFont, new Rectangle(blockX + columnWidths[0] + columnWidths[1] + 8, y, columnWidths[2], LineHeight), row[2].Color, flags | TextFormatFlags.Left);
            }
            y += LineHeight;
        }
        if (footer.Length > 0) TextRenderer.DrawText(g, footer, gameFont, new Rectangle(PadX, y, inner, LineHeight), Muted, flags | TextFormatFlags.HorizontalCenter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { hideTimer.Dispose(); gameFont.Dispose(); }
        base.Dispose(disposing);
    }

    internal static void SelfTest()
    {
        var screen = new Rectangle(0, 0, 1920, 1080);
        var size = new Size(300, 200);
        if (PlaceNear(new Point(100, 100), size, screen) != new Point(120, 120)) throw new Exception("The box should open right of and below the cursor when it fits.");
        if (PlaceNear(new Point(1800, 100), size, screen) != new Point(1480, 120)) throw new Exception("The box should flip left of the cursor at the right edge.");
        if (PlaceNear(new Point(100, 1000), size, screen) != new Point(120, 780)) throw new Exception("The box should flip above the cursor at the bottom edge.");
        if (PlaceNear(new Point(1000, 100), size, screen, preferLeft: true) != new Point(680, 120) || PlaceNear(new Point(100, 100), size, screen, preferLeft: true) != new Point(120, 120))
            throw new Exception("With preferLeft the box should open left of the cursor and fall back to the right at the left edge.");
        if (PlaceNear(new Point(5, 5), size, screen) != new Point(25, 25) || PlaceNear(new Point(-500, -500), size, screen) != new Point(0, 0))
            throw new Exception("The box must stay inside the screen.");
        var plan = ItemGradePlanner.Plan(ItemGradeTable.Find(101), "Leather Helmet", 101, new Dictionary<string, int> { ["DEF"] = 100 }, null);
        if (Subtitle(plan) != "20 CON · target AAA, then S · item AA") throw new Exception("Unexpected subtitle: " + Subtitle(plan));
        if (GradeColor(ItemGrade.AAA) != Color.FromArgb(255, 204, 0) || GradeColor(null) != Color.White) throw new Exception("Grade colours should follow the game's tooltip.");
        var unknown = ItemGradePlanner.Plan(null, "Odd Thing", 77, new Dictionary<string, int>(), ItemGrade.S);
        if (!Subtitle(unknown).StartsWith("No grade data for: Odd Thing", StringComparison.Ordinal)) throw new Exception("Unknown items should say there is no grade data.");
    }

    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
}
