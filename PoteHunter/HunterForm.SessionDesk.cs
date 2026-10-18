namespace PoteHunter;

public sealed partial class HunterForm
{
    Action? refreshSessionDesk;
    Action? refreshTacticalVitals;

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

        var elapsed = new Label { Name="orbitalActiveTime", AutoSize=true, ForeColor=UiText };
        var kills = new Label { Name="orbitalTrackedKills", AutoSize=true, ForeColor=UiText };
        var rate = new Label { Name="orbitalGoldRate", AutoSize=true, ForeColor=UiAccent };
        refreshOrbitalTelemetry = snapshot =>
        {
            rate.Text = "Gold / active hour: " + snapshot.RateText("Gold");
            var time = snapshot.RateElapsed; elapsed.Text = $"Active session: {(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
            kills.Text = "Tracked kills: " + snapshot.Sources.Sum(s => s.Kills).ToString("N0");
        };

        var routeNotes = new[] { "overviewRouteTarget", "overviewMapLegend", "overviewAnchor" }
            .Select(name => anchor.Controls.Find(name, true).OfType<Label>().Single()).ToArray();
        columns.Controls.Clear(); columns.RowStyles.Clear(); columns.ColumnStyles.Clear();
        columns.AutoSize = true; columns.AutoSizeMode = AutoSizeMode.GrowAndShrink; columns.Dock = DockStyle.Top; columns.RowCount = 1;
        columns.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Clear(); left.RowStyles.Clear(); left.RowCount = 0; left.AutoSize = true; left.Dock = DockStyle.Top; left.Margin = new Padding(5, 0, 0, 10);
        right.Controls.Clear(); right.RowStyles.Clear(); right.RowCount = 0; right.AutoSize = true; right.Dock = DockStyle.Top; right.Margin = new Padding(5, 0, 5, 10);
        var statusColumn = CompactTable(); statusColumn.Name="tacticalStatusColumn";
        statusColumn.Margin=new Padding(0,0,5,10);
        var vitals=CompactCard("CHARACTER STATUS"); vitals.Name="tacticalVitals";
        var health=new TacticalVitalMeter("HP",Color.FromArgb(218,54,51));
        var mana=new TacticalVitalMeter("MP",Color.FromArgb(56,139,253));
        var live=new Label{Name="tacticalLiveState",AutoSize=true,ForeColor=UiMuted,Margin=new Padding(0,3,0,4)};
        CompactAdd(vitals,health);CompactAdd(vitals,mana);CompactAdd(vitals,live);
        CompactAdd(statusColumn,vitals);
        var protection=CompactCard("PROTECTION & RECOVERY");protection.Name="tacticalProtection";
        Label StateRow(string name){var label=new Label{Name=name,AutoSize=true,ForeColor=UiText,Margin=new Padding(0,3,0,7)};CompactAdd(protection,label);return label;}
        var selfHealState=StateRow("tacticalSelfHeal");var reviveState=StateRow("tacticalRevive");
        var repairState=StateRow("tacticalRepair");var resumeState=StateRow("tacticalResume");
        CompactAdd(statusColumn,protection);
        var signal=CompactCard("SESSION SIGNAL");signal.Name="tacticalSessionSignal";
        CompactAdd(signal,elapsed);CompactAdd(signal,kills);CompactAdd(signal,rate);
        CompactAdd(statusColumn,signal);
        refreshTacticalVitals=()=>
        {
            string reading=connected?position.Text:"";
            health.SetReading(reading);mana.SetReading(reading);
            live.Text=working?"Hunting · input guarded":busy?"Operation in progress":connected?"Game connected":"Game offline · readings unavailable";
        };
        synchronize.Add(()=>
        {
            selfHealState.Text="Self-heal rule: "+(healthSkillCondition.Checked?"ON":"OFF");
            reviveState.Text="Auto revive + return: "+(autoRevive.Checked?"ON":"OFF");
            repairState.Text="Repair after revival: "+(autoRepair.Checked?"ON":"OFF");
            resumeState.Text="Resume on arrival: "+(farmOnArrival.Checked?"ON":"OFF");
            refreshTacticalVitals?.Invoke();
        });
        session.Controls.Clear(); session.RowStyles.Clear(); session.RowCount = 0; session.AutoSize = true; session.Dock = DockStyle.Top; session.Margin = Padding.Empty;
        session.Padding = new Padding(12, 8, 12, 8);
        CompactAdd(session, new Label { Text = "LOOT & RESOURCES", AutoSize = true, ForeColor = UiAccent, Font = new Font("Segoe UI Semibold", 10), Margin = new Padding(0, 0, 0, 12) });
        var goldRow=new TableLayoutPanel { Name="deskResourceRowGold", Dock=DockStyle.Top, Height=40,ColumnCount=2,RowCount=1,BackColor=UiRaised,Margin=new Padding(0,0,0,5),Padding=new Padding(12,0,12,0) };
        goldRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,60));goldRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,40));
        goldRow.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        goldRow.Controls.Add(new Label{Text="Gold (net)",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=UiText,Margin=Padding.Empty},0,0);
        var gold=totals["Gold"];gold.Dock=DockStyle.Fill;gold.Margin=Padding.Empty;gold.Padding=Padding.Empty;gold.TextAlign=ContentAlignment.MiddleRight;
        gold.Font=new Font("Segoe UI Semibold",12);goldRow.Controls.Add(gold,1,0);CompactAdd(session,goldRow);
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

        var targeting=CompactCard("TARGETING & BEHAVIOR");targeting.Name="tacticalTargeting";
        var shortcuts = (TableLayoutPanel)Mirror(presets); shortcuts.Name = "deskTargetShortcuts"; shortcuts.AutoSize = false; shortcuts.Height = 54;
        shortcuts.RowStyles.Clear(); shortcuts.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); CompactAdd(targeting, shortcuts);
        var targetSummary=new Label{Name="tacticalTargetSummary",AutoSize=true,ForeColor=UiMuted,Margin=new Padding(0,4,0,2)};
        CompactAdd(targeting,targetSummary);
        synchronize.Add(()=>targetSummary.Text="Gamekeeper "+(prioritizeGamekeeper.Checked?"priority ON":"priority OFF")+" · "+compactMode.Text+" · Range "+melee.Value.ToString("0.#"));
        CompactAdd(right,targeting);

        var route = CompactCard("ANCHOR ROUTE"); route.Name = "deskRouteSnapshot"; route.Margin = Padding.Empty;
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
        CompactAdd(right, route);
        var huntControls=CompactCard("HUNT CONTROLS");huntControls.Name="tacticalHuntControls";
        var huntStatus=new Label{Name="tacticalHuntStatus",AutoSize=true,ForeColor=UiText,Margin=new Padding(0,2,0,6)};
        var lootStatus=new Label{Name="tacticalLootStatus",AutoSize=true,ForeColor=UiText,Margin=new Padding(0,2,0,6)};
        CompactAdd(huntControls,huntStatus);CompactAdd(huntControls,lootStatus);CompactAdd(right,huntControls);
        synchronize.Add(()=>
        {
            huntStatus.Text="Attack skills: "+(autoSkills.Checked?"automatic":"configured keys");
            lootStatus.Text="Nearby loot: "+(nearbyLootPickup.Checked?"collect and return":"pickup off");
        });
        CompactAdd(body, columns);

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
            int mainColumns=width>=1060*DeviceDpi/96?3:width>=740*DeviceDpi/96?2:1;
            int foldColumns = width >= 1140 * DeviceDpi / 96 ? 3 : mainColumns==1 ? 1 : 2;
            int configuration=mainColumns*10+foldColumns;
            if(previousColumns == configuration) return; previousColumns = configuration;
            columns.SuspendLayout(); columns.Controls.Clear(); columns.ColumnStyles.Clear(); columns.RowStyles.Clear(); columns.ColumnCount=mainColumns;
            columns.RowCount=mainColumns==3?1:mainColumns==2?2:3;
            if(mainColumns==3){foreach(int weight in new[]{28,39,33})columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,weight));}
            else if(mainColumns==2){columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,46));columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,54));}
            else columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            for(int i=0;i<columns.RowCount;i++)columns.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            columns.SetColumnSpan(left,1);
            columns.Controls.Add(statusColumn,0,0);
            columns.Controls.Add(right,mainColumns==1?0:1,mainColumns==1?1:0);
            columns.Controls.Add(left,mainColumns==3?2:0,mainColumns==3?0:mainColumns==2?1:2);
            columns.SetColumnSpan(left,mainColumns==2?2:1);
            columns.ResumeLayout(true);
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
