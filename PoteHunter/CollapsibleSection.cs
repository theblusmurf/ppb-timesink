using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace PoteHunter;

/// <summary>A presentation-only section; its body keeps the original bound controls.</summary>
internal sealed class CollapsibleSection : TableLayoutPanel
{
    internal CollapsibleSectionHeader Header { get; }
    internal TableLayoutPanel Content { get; }
    bool expanded;

    internal CollapsibleSection(string name, string title, string icon, bool initiallyExpanded = false)
    {
        Name = name; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top; ColumnCount = 1; RowCount = 2;
        Margin = new Padding(0, 0, 0, 9); Padding = new Padding(1);
        BackColor = ImperialTheme.Surface;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Header = new CollapsibleSectionHeader(this, title, icon)
        { Name = name + "Header", Dock = DockStyle.Fill, Margin = Padding.Empty };
        Content = new TableLayoutPanel
        {
            Name = name + "Body", Dock = DockStyle.Top, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1,
            Margin = Padding.Empty, Padding = new Padding(13, 8, 13, 13),
            BackColor = ImperialTheme.Surface
        };
        Content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(Header, 0, 0); Controls.Add(Content, 0, 1);
        Header.Click += (_, _) => Expanded = !Expanded;
        Expanded = initiallyExpanded;
        Content.Visible = initiallyExpanded;
        DoubleBuffered = true;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string Summary { get => Header.Summary; set => Header.Summary = value; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Expanded
    {
        get => expanded;
        set
        {
            if(expanded == value) return;
            if(!value && Content.ContainsFocus) Header.Focus();
            expanded = value; Content.Visible = value; Header.RefreshAccessibility();
            Header.Invalidate(); Invalidate(); PerformLayout(); Parent?.PerformLayout();
        }
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if(Content == null) return;
        foreach(var label in Descendants(Content).OfType<Label>())
        {
            int width = Math.Max(1, label.Parent!.ClientSize.Width - label.Parent.Padding.Horizontal - label.Margin.Horizontal);
            if(label.AutoSize && label.Dock == DockStyle.None) label.MaximumSize = new Size(width, 0);
        }
    }

    static IEnumerable<Control> Descendants(Control parent)
    {
        foreach(Control control in parent.Controls)
        {
            yield return control;
            foreach(var nested in Descendants(control)) yield return nested;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if(Width < 4 || Height < 4) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var border = new Pen(Expanded ? ImperialTheme.Gold : ImperialTheme.Border);
        using var frame = CrownfireControls.Frame(new RectangleF(.5f, .5f, Width - 2, Height - 2), 11);
        e.Graphics.DrawPath(border, frame);
    }
}

/// <summary>A native keyboard-operable button with an always-visible setting summary.</summary>
internal sealed class CollapsibleSectionHeader : Button
{
    readonly CollapsibleSection section;
    readonly string icon;
    readonly Font summaryFont = new("Segoe UI", 8.5f);
    string summary = "";
    bool hovered;

    internal CollapsibleSectionHeader(CollapsibleSection section, string title, string icon)
    {
        this.section = section; this.icon = icon; Text = title;
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        Font = new Font("Segoe UI", 10f, FontStyle.Regular);
        BackColor = ImperialTheme.Surface; ForeColor = ImperialTheme.Text;
        TabStop = true; UseMnemonic = false; AccessibleName = title;
        AccessibleRole = AccessibleRole.PushButton;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string Summary
    {
        get => summary;
        set
        {
            if(summary == value) return;
            summary = value; RefreshAccessibility(); Invalidate();
        }
    }

    internal void RefreshAccessibility()
    {
        AccessibleDescription = (section.Expanded ? "Expanded. " : "Collapsed. ") + summary + ". Enter or Space toggles this section.";
        AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new HeaderAccessibility(this, section);
    sealed class HeaderAccessibility(CollapsibleSectionHeader owner, CollapsibleSection section) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.PushButton;
        public override AccessibleStates State => base.State | (section.Expanded ? AccessibleStates.Expanded : AccessibleStates.Collapsed);
        public override string? DefaultAction => section.Expanded ? "Collapse" : "Expand";
        public override void DoDefaultAction() => owner.PerformClick();
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if(e.KeyCode is Keys.Left or Keys.Right)
        {
            section.Expanded = e.KeyCode == Keys.Right; e.Handled = e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(hovered ? ImperialTheme.Raised : ImperialTheme.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var ink = Enabled ? ImperialTheme.Text : ImperialTheme.Muted;
        CrownfireControls.Glyph(g, icon, new RectangleF(13, 15, 20, 20), ImperialTheme.Gold);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(43, 9, Math.Max(1, Width - 75), 24), ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Summary, summaryFont, new Rectangle(43, 33, Math.Max(1, Width - 75), 22), ImperialTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        float x = Width - 21, y = 28;
        using var chevron = new Pen(ImperialTheme.Muted, 1.5f);
        g.DrawLines(chevron, section.Expanded
            ? [new PointF(x - 4, y + 2), new PointF(x, y - 2), new PointF(x + 4, y + 2)]
            : [new PointF(x - 4, y - 2), new PointF(x, y + 2), new PointF(x + 4, y - 2)]);
        if(Focused) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -5, -5), ink, BackColor);
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing) summaryFont.Dispose();
        base.Dispose(disposing);
    }
}
