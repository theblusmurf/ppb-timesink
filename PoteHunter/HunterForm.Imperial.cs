namespace PoteHunter;

public sealed partial class HunterForm
{
    // Reparent original controls; bindings, locks, shortcuts and page state stay intact.
    void ApplyImperialShell(TableLayoutPanel shell, TableLayoutPanel masthead, FlowLayoutPanel nav,
        TableLayoutPanel header, TabControl tabs, TableLayoutPanel footer)
    {
        shell.SuspendLayout();
        shell.Controls.Clear(); shell.ColumnStyles.Clear(); shell.RowStyles.Clear();
        shell.Padding = new Padding(16, 0, 16, 10); shell.ColumnCount = 1; shell.RowCount = 3;
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var banner = new ImperialBanner { Name = "imperialBanner", Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(0, 5, 0, 5) };
        masthead.BackColor = Color.Transparent;
        foreach (var label in masthead.Controls.OfType<Label>())
            if (label.Name != "fieldConnectionStatus") label.BackColor = Color.Transparent;
        banner.Controls.Add(masthead);

        var workspace = new TableLayoutPanel { Name = "orbitalWorkspace", Dock = DockStyle.Fill,
            ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 16, 0, 10), BackColor = UiWindow };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 352));
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var rail = new TableLayoutPanel { Name = "orbitalOperationRail", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            BackColor = UiSurface, Padding = new Padding(14, 12, 12, 5), Margin = new Padding(0, 0, 12, 0) };
        rail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
        rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        rail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rail.Controls.Add(new Label { Text = "MISSION CONTROL", Dock = DockStyle.Fill, Font = new Font("Segoe UI Semibold", 8.5f),
            ForeColor = UiMuted, Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        var previousActions = connect.Parent!;
        connect.Dock = DockStyle.Fill; connect.Margin = new Padding(0, 0, 0, 7); connect.Text = "Connect";
        rail.Controls.Add(connect, 0, 2);
        var actions = new TableLayoutPanel { Name = "orbitalHuntActions", Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = new Padding(0, 0, 0, 9) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        start.Text = "Start  F8"; stop.Text = "Stop  F9";
        start.Dock = stop.Dock = DockStyle.Fill; start.Margin = new Padding(0, 0, 7, 0); stop.Margin = Padding.Empty;
        actions.Controls.Add(start, 0, 0); actions.Controls.Add(stop, 1, 0); rail.Controls.Add(actions, 0, 3);
        header.Controls.Remove(previousActions); previousActions.Dispose();
        header.ColumnCount = 2; header.ColumnStyles.RemoveAt(2);
        header.ColumnStyles[0].SizeType = SizeType.Percent; header.ColumnStyles[0].Width = 100;
        header.ColumnStyles[1].SizeType = SizeType.Absolute; header.ColumnStyles[1].Width = 180;
        header.Margin = new Padding(12, 0, 0, 10);
        var scroll = new Panel { Name = "orbitalRailScroll", Dock = DockStyle.Fill, AutoScroll = true,
            BackColor = UiSurface, Margin = new Padding(0, 8, 0, 0), Padding = Padding.Empty };
        if (orbitalRailBody == null) throw new InvalidOperationException("Orbital controls were not composed before the shell.");
        scroll.Controls.Add(orbitalRailBody);
        ThemeTree(orbitalRailBody);
        void FitRail()
        {
            int width = Math.Max(1, scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
            orbitalRailBody.MinimumSize = new Size(width, 0); orbitalRailBody.MaximumSize = new Size(width, 0);
            orbitalRailBody.Width = width;
        }
        scroll.SizeChanged += (_, _) => FitRail(); rail.Controls.Add(scroll, 0, 4);
        rail.Paint += (_, e) => { using var accent = new Pen(UiAccent, 2); e.Graphics.DrawLine(accent, 1, 0, 1, rail.Height - 1); };
        var content = new TableLayoutPanel { Name = "orbitalPageContent", Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty, BackColor = UiWindow };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 68)); content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(header, 0, 0); content.Controls.Add(tabs, 0, 1);
        bool? setupStacked = null;
        void FitSetupCards()
        {
            if (content.ClientSize.Width <= 0) return;
            bool stack = content.ClientSize.Width < 860 * DeviceDpi / 96;
            if (setupStacked == stack) return;
            setupStacked = stack;
            var combat = settings.Controls.Find("fieldCombatColumn", false).Single();
            var recovery = settings.Controls.Find("fieldRecoveryColumn", false).Single();
            settings.SuspendLayout(); settings.Controls.Clear(); settings.ColumnStyles.Clear(); settings.RowStyles.Clear();
            settings.ColumnCount = stack ? 1 : 2; settings.RowCount = stack ? 2 : 1;
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, stack ? 100 : 50));
            if (!stack) settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (stack) settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            combat.Margin = stack ? Padding.Empty : new Padding(0, 0, 5, 0);
            recovery.Margin = stack ? Padding.Empty : new Padding(5, 0, 0, 0);
            settings.Controls.Add(combat, 0, 0); settings.Controls.Add(recovery, stack ? 0 : 1, stack ? 1 : 0);
            settings.ResumeLayout(true);
        }
        content.SizeChanged += (_, _) => FitSetupCards();
        workspace.Controls.Add(rail, 0, 0); workspace.Controls.Add(content, 1, 0);

        nav.FlowDirection=FlowDirection.TopDown;nav.WrapContents=false;nav.AutoScroll=true;
        nav.BackColor=UiSurface;nav.Padding=Padding.Empty;
        void FitMenu(){foreach(var button in nav.Controls.OfType<Button>()){
            button.Size=new Size(Math.Max(100,nav.ClientSize.Width-SystemInformation.VerticalScrollBarWidth-2),40);
            button.Margin=new Padding(0,0,0,5);button.TextAlign=ContentAlignment.MiddleLeft;button.Padding=new Padding(12,0,0,0);}}
        nav.SizeChanged+=(_,_)=>FitMenu();rail.Controls.Add(nav,0,0);FitMenu();
        shell.Controls.Add(banner,0,0);shell.Controls.Add(workspace,0,1);shell.Controls.Add(footer,0,2);
        FitRail(); shell.ResumeLayout(true); FitSetupCards();
    }
}
