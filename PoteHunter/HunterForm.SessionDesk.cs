namespace PoteHunter;

public sealed partial class HunterForm
{
    Action? refreshSessionDesk;

    // Overview mirrors the existing, guarded Farming controls; it owns no settings.
    void ApplySessionDesk(TabPage page, TableLayoutPanel body, TableLayoutPanel columns,
        TableLayoutPanel left, TableLayoutPanel right, TableLayoutPanel anchor, OrbitalScanner scanner,
        TableLayoutPanel session, FlowLayoutPanel sessionHeader,
        List<CollapsibleSection> folds, ComboBox mode, TextBox targetFilter, TableLayoutPanel presets,
        FlowLayoutPanel mapOptions, Dictionary<Control, Action> actions, Dictionary<string, Label> totals)
    {
        var synchronize = new List<Action>();
        bool copying = false;
        Control Mirror(Control source)
        {
            Control copy;
            switch (source)
            {
                case CheckBox check:
                    var toggle = new CheckBox { AutoSize = true };
                    toggle.CheckedChanged += (_, _) => { if(!copying) { check.Checked = toggle.Checked; refreshOverview?.Invoke(); } };
                    synchronize.Add(() => toggle.Checked = check.Checked); copy = toggle; break;
                case NumericUpDown number:
                    var input = Number(number.Minimum, number.Maximum, number.DecimalPlaces);
                    input.Increment = number.Increment;
                    input.ValueChanged += (_, _) => { if(!copying) { number.Value = Math.Clamp(input.Value, number.Minimum, number.Maximum); refreshOverview?.Invoke(); } };
                    synchronize.Add(() => { input.Minimum = number.Minimum; input.Maximum = number.Maximum; input.DecimalPlaces = number.DecimalPlaces; input.Increment = number.Increment; input.Value = number.Value; });
                    copy = input; break;
                case TrackBar slider:
                    var track = new TrackBar { Minimum = slider.Minimum, Maximum = slider.Maximum, TickStyle = slider.TickStyle, AutoSize = false, SmallChange = slider.SmallChange, LargeChange = slider.LargeChange };
                    track.ValueChanged += (_, _) => { if(!copying) { slider.Value = track.Value; refreshOverview?.Invoke(); } };
                    synchronize.Add(() => track.Value = slider.Value); copy = track; break;
                case ComboBox combo:
                    var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                    picker.Items.AddRange(combo.Items.Cast<object>().ToArray());
                    picker.SelectionChangeCommitted += (_, _) =>
                    {
                        if(combo.Enabled && !working && !busy && !clientRecoveryRunning && compactMode.Enabled) compactMode.SelectedIndex = picker.SelectedIndex;
                        refreshOverview?.Invoke();
                    };
                    synchronize.Add(() => picker.SelectedIndex = combo.SelectedIndex); copy = picker; break;
                case TextBox text:
                    var filterInput = new TextBox();
                    filterInput.TextChanged += (_, _) => { if(!copying) { text.Text = filterInput.Text; refreshOverview?.Invoke(); } };
                    copy = filterInput; break;
                case Button button:
                    var action = actions[button];
                    var link = new Button { AutoSize = button.AutoSize, TextAlign = button.TextAlign };
                    link.Click += (_, _) => { if(button.Enabled) action(); }; copy = link; break;
                case Label label:
                    copy = new Label { AutoSize = label.AutoSize, AutoEllipsis = label.AutoEllipsis, UseMnemonic = false, TextAlign = label.TextAlign }; break;
                case FlowLayoutPanel flow:
                    var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, FlowDirection = flow.FlowDirection };
                    foreach(Control child in flow.Controls) row.Controls.Add(Mirror(child)); copy = row; break;
                case TableLayoutPanel table:
                    var layout = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = table.ColumnCount, RowCount = table.RowCount };
                    foreach(ColumnStyle style in table.ColumnStyles) layout.ColumnStyles.Add(new ColumnStyle(style.SizeType, style.Width));
                    foreach(RowStyle style in table.RowStyles) layout.RowStyles.Add(new RowStyle(style.SizeType, style.Height));
                    foreach(Control child in table.Controls)
                    {
                        var cell = table.GetPositionFromControl(child); var mirrored = Mirror(child);
                        layout.Controls.Add(mirrored, cell.Column, cell.Row); layout.SetColumnSpan(mirrored, table.GetColumnSpan(child));
                    }
                    copy = layout; break;
                default: throw new InvalidOperationException("Unsupported Session Desk control: " + source.GetType().Name);
            }
            copy.Name = source.Name.Length == 0 ? "" : source.Name.Replace("overview", "desk");
            copy.Text = source.Text; copy.Size = source.Size; copy.Margin = source.Margin; copy.Padding = source.Padding;
            copy.Dock = source.Dock; copy.Anchor = source.Anchor; copy.Font = source.Font;
            copy.ForeColor = source.ForeColor; copy.BackColor = source.BackColor;
            copy.AccessibleName = source.AccessibleName; copy.AccessibleDescription = source.AccessibleDescription;
            priorityHint.SetToolTip(copy, priorityHint.GetToolTip(source));
            synchronize.Add(() => { copy.Enabled = source.Enabled; if(copy is Label or Button or TextBox) copy.Text = source.Text; copy.BackColor = source.BackColor; copy.ForeColor = source.ForeColor; });
            return copy;
        }

        body.SuspendLayout();
        body.Controls.Clear(); body.RowStyles.Clear(); body.RowCount = 0; body.AutoSize = true;
        body.AutoSizeMode = AutoSizeMode.GrowAndShrink; body.Padding = new Padding(12, 0, 12, 12);
        // The page scrolls the complete desk, including expanded sections.
        var quick = new TableLayoutPanel { Name = "sessionDeskQuick", Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, 8) };
        quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28)); quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38)); quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        TableLayoutPanel Field(string title, Control control)
        {
            var field = CompactTable(); field.Margin = new Padding(0, 0, 12, 0);
            CompactAdd(field, new Label { Text = title, AutoSize = true, ForeColor = UiMuted, Margin = new Padding(0, 0, 0, 5) });
            control.Dock = DockStyle.Top; control.Margin = Padding.Empty; CompactAdd(field, control);
            bool measuring = false;
            void FitField()
            {
                if(measuring) return; measuring = true;
                try { field.RowStyles[1].SizeType = SizeType.Absolute; field.RowStyles[1].Height = Math.Max(control.Height, control.GetPreferredSize(Size.Empty).Height); }
                finally { measuring = false; }
            }
            control.SizeChanged += (_, _) => FitField(); control.FontChanged += (_, _) => FitField(); FitField(); return field;
        }
        quick.Controls.Add(Field("Hunt mode", Mirror(mode)), 0, 0);
        quick.Controls.Add(Field("Target name filter", Mirror(targetFilter)), 1, 0);
        var priority = Mirror(orbitalRailBody!.Controls.Find("overviewGamekeeper", true).Single());
        priority.Margin = new Padding(0, 22, 0, 0); quick.Controls.Add(priority, 2, 0); CompactAdd(body, quick);

        var telemetry = new TableLayoutPanel { Name = "orbitalTelemetry", Dock = DockStyle.Top, Height = 110, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, 8) };
        foreach(int weight in new[] { 46, 29, 25 }) telemetry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, weight));
        telemetry.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Label Value(string name) => new() { Name = name, Dock = DockStyle.Fill, ForeColor = UiText, Font = new Font("Segoe UI Semibold", 23), AutoEllipsis = true, Margin = Padding.Empty };
        Label Note(string name) => new() { Name = name, Dock = DockStyle.Fill, ForeColor = UiMuted, AutoEllipsis = true, Margin = Padding.Empty };
        var gold = totals["Gold"]; gold.Dock = DockStyle.Fill; gold.Padding = Padding.Empty;
        gold.TextAlign = ContentAlignment.MiddleLeft; gold.Font = new Font("Segoe UI Semibold", 23);
        var rate = Note("orbitalGoldRate"); rate.ForeColor = UiAccent;
        var elapsed = Value("orbitalActiveTime"); var kills = Value("orbitalTrackedKills");
        foreach(var item in new[] { ("NET GOLD", gold, rate), ("ACTIVE TIME", elapsed, Note("deskTimeNote")), ("TRACKED KILLS", kills, Note("deskKillsNote")) }.Select((item, i) => (item, i)))
        {
            var card = CompactCard(item.item.Item1); card.Dock = DockStyle.Fill; card.AutoSize = false;
            card.Margin = new Padding(0, 0, item.i == 2 ? 0 : 10, 0); card.Padding = new Padding(12, 9, 12, 7);
            CompactAdd(card, item.item.Item2); CompactAdd(card, item.item.Item3);
            // Empty startup readings must not collapse AutoSize rows to zero.
            card.RowStyles.Clear(); card.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); card.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            telemetry.Controls.Add(card, item.i, 0);
        }
        telemetry.Controls.Find("deskTimeNote", true).Single().Text = "Active farming time";
        telemetry.Controls.Find("deskKillsNote", true).Single().Text = "Detected this session";
        refreshOrbitalTelemetry = snapshot =>
        {
            rate.Text = snapshot.RateText("Gold") + " / active hour";
            var time = snapshot.RateElapsed; elapsed.Text = $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
            kills.Text = snapshot.Sources.Sum(s => s.Kills).ToString("N0");
        };
        CompactAdd(body, telemetry);

        var routeNotes = new[] { "overviewRouteTarget", "overviewMapLegend", "overviewAnchor" }
            .Select(name => anchor.Controls.Find(name, true).OfType<Label>().Single()).ToArray();
        columns.Controls.Clear(); columns.RowStyles.Clear(); columns.ColumnStyles.Clear();
        columns.AutoSize = true; columns.AutoSizeMode = AutoSizeMode.GrowAndShrink; columns.Dock = DockStyle.Top; columns.RowCount = 1;
        columns.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Clear(); left.RowStyles.Clear(); left.RowCount = 0; left.AutoSize = true; left.Dock = DockStyle.Top; left.Margin = new Padding(0, 0, 7, 10);
        right.Controls.Clear(); right.RowStyles.Clear(); right.RowCount = 0; right.AutoSize = true; right.Dock = DockStyle.Top; right.Margin = new Padding(7, 0, 0, 10);
        session.Controls.Clear(); session.RowStyles.Clear(); session.RowCount = 0; session.AutoSize = true; session.Dock = DockStyle.Top; session.Margin = Padding.Empty;
        session.Padding = new Padding(12, 8, 12, 8);
        CompactAdd(session, new Label { Text = "SESSION RESOURCES", AutoSize = true, ForeColor = UiMuted, Font = new Font("Segoe UI Semibold", 9), Margin = new Padding(0, 0, 0, 12) });
        foreach(string name in new[] { "Silvin", "Mithril", "Iternium", "Fehu", "Gems" })
        {
            var row = new TableLayoutPanel { Name = "deskResourceRow" + name, Dock = DockStyle.Top, Height = 38, ColumnCount = 2, RowCount = 1, BackColor = UiRaised, Margin = new Padding(0, 0, 0, 5), Padding = new Padding(40, 0, 12, 0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40)); row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            row.Paint += (_, e) => GameLootIcons.Draw(e.Graphics, name, new RectangleF(9, (row.Height - 24) / 2f, 24, 24));
            row.Controls.Add(new Label { Text = name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiText, Margin = Padding.Empty }, 0, 0);
            var total = totals[name]; total.Dock = DockStyle.Fill; total.Padding = Padding.Empty; total.Margin = Padding.Empty; total.TextAlign = ContentAlignment.MiddleRight; total.Font = new Font("Segoe UI Semibold", 12);
            row.Controls.Add(total, 1, 0); CompactAdd(session, row);
        }
        CompactAdd(session, new Label { Text = "Gold is net wallet change. Resources are detected-drop estimates.", AutoSize = true, ForeColor = UiMuted, Margin = new Padding(0, 8, 0, 8) });
        var ledgerActions = (FlowLayoutPanel)Mirror(sessionHeader); ledgerActions.Name = "deskLedgerActions";
        foreach(var hidden in ledgerActions.Controls.OfType<Label>().ToArray()) ledgerActions.Controls.Remove(hidden);
        CompactAdd(session, ledgerActions); CompactAdd(left, session);

        var route = CompactCard("ROUTE SNAPSHOT"); route.Name = "deskRouteSnapshot"; route.Margin = Padding.Empty;
        route.Padding = new Padding(12, 8, 12, 8);
        route.Controls.OfType<Label>().Single().Font = new Font("Segoe UI Semibold", 9);
        scanner.CompactSnapshot = true; scanner.Dock = DockStyle.Top; scanner.Height = 145; scanner.Margin = new Padding(0, 0, 0, 5); CompactAdd(route, scanner);
        foreach(var label in routeNotes)
        {
            label.AutoSize = false; label.Dock = DockStyle.Fill; label.Height = 22; label.MaximumSize = Size.Empty;
            label.Margin = Padding.Empty; label.Font = new Font("Segoe UI", 9); label.TextAlign = ContentAlignment.MiddleLeft; label.AutoEllipsis = true;
            label.TextChanged += (_, _) => priorityHint.SetToolTip(label, label.Text);
            CompactAdd(route, label);
            route.RowStyles[^1].SizeType = SizeType.Absolute; route.RowStyles[^1].Height = 22;
        }
        CompactAdd(route, Mirror(mapOptions));
        var shortcuts = (TableLayoutPanel)Mirror(presets); shortcuts.Name = "deskTargetShortcuts"; shortcuts.AutoSize = false; shortcuts.Height = 54;
        shortcuts.RowStyles.Clear(); shortcuts.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); CompactAdd(route, shortcuts);
        CompactAdd(right, route); CompactAdd(body, columns);

        var toolbar = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 2, 0, 2) };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.Controls.Add(new Label { Text = "SESSION CONTROLS", AutoSize = true, ForeColor = UiMuted }, 0, 0);
        var groups = new TableLayoutPanel { Name = "sessionDeskGroups", Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var groupColumns = new[] { CompactTable(), CompactTable(), CompactTable() };
        foreach(var column in groupColumns) column.Margin = new Padding(0, 0, 10, 0);
        var deskFolds = new List<CollapsibleSection>();
        foreach(var item in new[] { ("Targets", "Targets & combat", "Hunt"), ("Skills", "Skills & healing", "Support"), ("Movement", "Movement & loot", "Routes"), ("Recovery", "Death & client recovery", "Recovery"), ("Radii", "Route & anchor radius", "Routes"), ("Routes", "Saved route options", "Routes") }.Select((item, i) => (item, i)))
        {
            var source = folds.Single(f => f.Name == "overviewFold" + item.item.Item1);
            var fold = new CollapsibleSection("deskFold" + item.item.Item1, item.item.Item2, item.item.Item3);
            foreach(Control child in source.Content.Controls)
                if(child != presets && child != mapOptions) CompactAdd(fold.Content, Mirror(child));
            synchronize.Add(() => fold.Summary = source.Summary);
            deskFolds.Add(fold);
        }
        var collapse = new Button { Name = "deskCollapseAll", Text = "Collapse all", AutoSize = true, Margin = Padding.Empty };
        collapse.Click += (_, _) => { foreach(var fold in deskFolds) fold.Expanded = false; };
        toolbar.Controls.Add(collapse, 1, 0); CompactAdd(body, toolbar); CompactAdd(body, groups);
        int previousColumns = 0;
        void FitDesk()
        {
            int width = Math.Max(1, page.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
            body.MinimumSize = new Size(width, 0); body.MaximumSize = new Size(width, 0); body.Width = width;
            bool stack = width < 800 * DeviceDpi / 96;
            int foldColumns = width >= 1140 * DeviceDpi / 96 ? 3 : stack ? 1 : 2;
            if(previousColumns == foldColumns) return; previousColumns = foldColumns;
            columns.SuspendLayout(); columns.Controls.Clear(); columns.ColumnStyles.Clear(); columns.RowStyles.Clear(); columns.ColumnCount = stack ? 1 : 2; columns.RowCount = stack ? 2 : 1;
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, stack ? 100 : 52)); if(!stack) columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
            columns.RowStyles.Add(new RowStyle(SizeType.AutoSize)); if(stack) columns.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            columns.Controls.Add(left, 0, 0); columns.Controls.Add(right, stack ? 0 : 1, stack ? 1 : 0); columns.ResumeLayout(true);
            groups.SuspendLayout(); groups.Controls.Clear(); groups.ColumnStyles.Clear(); groups.RowStyles.Clear(); groups.ColumnCount = foldColumns; groups.RowCount = 1;
            foreach(var column in groupColumns) { column.Controls.Clear(); column.RowStyles.Clear(); column.RowCount = 0; }
            for(int i = 0; i < foldColumns; i++) { groups.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / foldColumns)); groups.Controls.Add(groupColumns[i], i, 0); }
            for(int i = 0; i < deskFolds.Count; i++) CompactAdd(groupColumns[i % foldColumns], deskFolds[i]);
            groups.ResumeLayout(true);
        }
        refreshSessionDesk = () =>
        {
            copying = true;
            try { foreach(var update in synchronize) update(); }
            finally { copying = false; }
        };
        page.SizeChanged += (_, _) => FitDesk(); page.VisibleChanged += (_, _) => FitDesk();
        body.ResumeLayout(true); FitDesk();
    }
}
