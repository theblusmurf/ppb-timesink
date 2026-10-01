using System.Globalization;

namespace PoteHunter;

public sealed partial class HunterForm
{
    Action? refreshOverview;

    void CheckFieldOverview(TabControl tabs)
    {
        var previousPage = tabs.SelectedTab;
        var previousFilter = filter.Text;
        var previousMode = compactMode.SelectedIndex;
        var previousRange = melee.Value;
        bool previousRepair = autoRepair.Checked;
        try
        {
            tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(p => p.Text == "Overview");
            refreshOverview?.Invoke(); PerformLayout(); Application.DoEvents();
            T Find<T>(string name) where T : Control => Controls.Find(name, true).OfType<T>().Single();
            Find<Button>("overviewTargetMimic").PerformClick();
            if(filter.Text != "Mimic" || CurrentOptions().Target != "Mimic") throw new Exception("Overview target preset did not update the bound filter.");
            Find<TextBox>("overviewTargetFilter").Text = "Custom monster";
            if(filter.Text != "Custom monster") throw new Exception("Overview custom filter was discarded.");
            filter.Text = "Tribal";
            if(Find<TextBox>("overviewTargetFilter").Text != "Tribal") throw new Exception("Overview did not follow detailed target edits.");
            var repair = Find<CheckBox>("overviewRepair"); repair.Checked = !autoRepair.Checked;
            if(repair.Checked != autoRepair.Checked) throw new Exception("Overview repair toggle did not bind.");
            working = true; refreshOverview?.Invoke();
            if(Find<Button>("overviewTargetMimic").Enabled || Find<TextBox>("overviewTargetFilter").Enabled || repair.Enabled || Find<ComboBox>("overviewMode").Enabled || Find<Button>("overviewRoute0").Enabled)
                throw new Exception("Overview permits settings changes while hunting.");
            bool lockedRepair = autoRepair.Checked; repair.Checked = !lockedRepair;
            if(autoRepair.Checked != lockedRepair) throw new Exception("Overview bypassed running-state guard.");
            working = false; compactMode.SelectedIndex = 3; refreshOverview?.Invoke();
            if(Find<ComboBox>("overviewMode").SelectedIndex != 3 || Find<CheckBox>("overviewRevive").Enabled || Find<Button>("overviewTargetMimic").Enabled)
                throw new Exception("Overview healer state differs from detailed settings.");
            compactMode.SelectedIndex = 0; refreshOverview?.Invoke();
            foreach(string name in new[] { "Gold", "Silvin", "Mithril", "Iternium", "Fehu", "Gems" })
                if(!Find<Label>("overviewResource"+name).Text.StartsWith(name+"\n")) throw new Exception("Overview resource missing: "+name);
            PerformLayout(); Application.DoEvents();
            using var preview = new Bitmap(Width, Height);
            DrawToBitmap(preview, new Rectangle(Point.Empty, Size));
            preview.Save(Path.Combine(AppContext.BaseDirectory, "field-console-ii-minimum.png"));
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "field-console-ii-checks.json"), System.Text.Json.JsonSerializer.Serialize(new
            { Passed = true, Checks = new[] { "target shortcut and custom filter binding", "two-way settings synchronization", "repair toggle binding", "running-state mutation guard", "healer exclusions", "six existing tracker totals" } }));
        }
        finally
        {
            working = false; compactMode.SelectedIndex = previousMode; filter.Text = previousFilter;
            melee.Value = previousRange; autoRepair.Checked = previousRepair;
            compactSaveTimer.Stop(); tabs.SelectedTab = previousPage; refreshOverview?.Invoke();
        }
    }

    // These shortcuts edit the existing bound controls. No extra settings,
    // game reads, input paths or independent recovery state are introduced.
    TabPage CreateFieldOverview(TabControl tabs, TabPage setupPage)
    {
        var page = new TabPage("Overview") { AutoScroll = true, BackColor = UiWindow };
        var body = CompactTable(); body.Name = "fieldOverviewBody";
        page.Controls.Add(body);
        var columns = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        columns.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var left = CompactTable(); left.Margin = new Padding(0, 0, 5, 0);
        var right = CompactTable(); right.Margin = new Padding(5, 0, 0, 0);
        columns.Controls.Add(left, 0, 0); columns.Controls.Add(right, 1, 0);
        CompactAdd(body, columns);
        var updates = new List<Action>();
        bool syncing = false;
        bool CanEdit(Control source) => !working && !busy && source.Enabled;
        void OpenSetup(Control? control = null)
        {
            tabs.SelectedTab = setupPage;
            if(control != null) setupPage.ScrollControlIntoView(control);
        }
        Button Link(string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 4, 6, 0) };
            button.Click += (_, _) => action(); return button;
        }
        CheckBox Toggle(string name, string text, CheckBox source)
        {
            var check = new CheckBox { Name = name, Text = text, AutoSize = true, Margin = new Padding(0, 5, 8, 7) };
            check.CheckedChanged += (_, _) =>
            {
                if(syncing) return;
                if(CanEdit(source)) { source.Checked = check.Checked; QueueCompactSave(); }
                refreshOverview?.Invoke();
            };
            updates.Add(() => { check.Checked = source.Checked; check.Enabled = CanEdit(source); });
            source.CheckedChanged += (_, _) => refreshOverview?.Invoke();
            return check;
        }
        Label Detail(string name)
            => new() { Name = name, AutoSize = true, ForeColor = UiMuted, Margin = new Padding(0, 2, 0, 6) };

        var targets = CompactCard("TARGET SELECTION"); CompactAdd(left, targets);
        var targetButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, WrapContents = false, Margin = Padding.Empty };
        var targetPresets = new List<(string Name, Button Button)>();
        foreach(string family in new[] { "Mimic", "Pulkhan", "Tribal", "Tower" })
        {
            var button = new Button { Name = "overviewTarget"+family, Text = family, Size = new Size(106, 36), Margin = new Padding(0, 0, 6, 6), AccessibleDescription = "Select the "+family+" target filter" };
            button.Click += (_, _) => { if(CanEdit(filter)) { filter.Text = family; QueueCompactSave(); } refreshOverview?.Invoke(); };
            targetPresets.Add((family, button)); targetButtons.Controls.Add(button);
        }
        CompactAdd(targets, targetButtons);
        var targetFilter = new TextBox { Name = "overviewTargetFilter", Width = 180, AccessibleName = "Target name filter" };
        targetFilter.TextChanged += (_, _) => { if(!syncing && CanEdit(filter)) { filter.Text = targetFilter.Text; QueueCompactSave(); } };
        filter.TextChanged += (_, _) => refreshOverview?.Invoke();
        CompactAdd(targets, CompactRow("Name filter", targetFilter));
        CompactAdd(targets, Toggle("overviewGamekeeper", "Prioritize Gamekeeper", prioritizeGamekeeper));
        priorityHint.SetToolTip(targetButtons, "Choose one target family, or enter a name filter. Gamekeeper priority is independent.");

        var combat = CompactCard("COMBAT SETTINGS"); CompactAdd(left, combat);
        var mode = new ComboBox { Name = "overviewMode", DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        mode.Items.AddRange(compactMode.Items.Cast<object>().ToArray());
        mode.SelectionChangeCommitted += (_, _) => { if(CanEdit(compactMode)) compactMode.SelectedIndex = mode.SelectedIndex; refreshOverview?.Invoke(); };
        compactMode.SelectedIndexChanged += (_, _) => refreshOverview?.Invoke();
        var range = Number(1, 30, 1); range.Name = "overviewAttackRange"; range.DecimalPlaces = melee.DecimalPlaces;
        range.ValueChanged += (_, _) => { if(!syncing && CanEdit(melee)) { melee.Value = Math.Clamp(range.Value, melee.Minimum, melee.Maximum); QueueCompactSave(); } };
        melee.ValueChanged += (_, _) => refreshOverview?.Invoke();
        CompactAdd(combat, CompactRow("Mode / range", mode, Caption("Range"), range));
        priorityHint.SetToolTip(range, "Effective attack range in map units; ranged/class limits follow detailed Hunt settings.");
        CompactAdd(combat, Toggle("overviewAutoSkills", "Detect attack skills automatically", autoSkills));
        CompactAdd(combat, new Label { Text = "Combat skill gap  1.5 s  ·  Self-heals exempt", AutoSize = true, ForeColor = UiMuted, Margin = new Padding(0, 0, 0, 5) });
        CompactAdd(combat, Link("All hunt settings", () => OpenSetup()));

        var anchor = CompactCard("ANCHOR & LOOT"); CompactAdd(left, anchor);
        var anchorText = Detail("overviewAnchor"); CompactAdd(anchor, anchorText);
        CompactAdd(anchor, Toggle("overviewPickup", "Collect loot within 10 units; return to anchor", nearbyLootPickup));
        priorityHint.SetToolTip(anchor, "Solo loot excursions use the saved activation anchor. Pickup, return, combat and recovery rules are unchanged.");

        var recovery = CompactCard("RECOVERY"); CompactAdd(right, recovery);
        CompactAdd(recovery, Toggle("overviewRevive", "Auto revive + return to anchor", autoRevive));
        CompactAdd(recovery, Toggle("overviewRepair", "Auto repair after revival", autoRepair));
        CompactAdd(recovery, Toggle("overviewResume", "Resume farming on arrival", farmOnArrival));
        var recoveryText = Detail("overviewRecoveryStatus"); CompactAdd(recovery, recoveryText);
        CompactAdd(recovery, Link("Revival & repair setup", () => OpenSetup(autoRevive)));

        var routes = CompactCard("SAVED ROUTES"); CompactAdd(right, routes);
        var routeButtons = new List<Button>();
        for(int slot = 0; slot < Navigation.SavedRouteSlotCount; slot++)
        {
            int selectedSlot = slot;
            var button = new Button { Name = "overviewRoute"+slot, Dock = DockStyle.Top, Height = 37, Margin = new Padding(0, 0, 0, 6), TextAlign = ContentAlignment.MiddleLeft };
            button.Click += (_, _) =>
            {
                if(CanEdit(savedNavigationSlot)) savedNavigationSlot.SelectedIndex = selectedSlot;
                refreshOverview?.Invoke();
            };
            CompactAdd(routes, button); routeButtons.Add(button);
        }
        savedNavigationSlot.SelectedIndexChanged += (_, _) => refreshOverview?.Invoke();
        CompactAdd(routes, new Label { Text = "Home  Start recording    /    End  Finish & save", AutoSize = true, ForeColor = UiMuted, Margin = new Padding(0, 3, 0, 7) });
        CompactAdd(routes, Link("Open navigation", () => tabs.SelectedTab = navigationPage));

        var session = CompactCard("SESSION SUMMARY"); CompactAdd(body, session);
        var sessionInfo = Detail("overviewSession");
        var sessionHeader = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, WrapContents = false, Margin = Padding.Empty };
        sessionInfo.Margin = new Padding(0, 8, 14, 0);
        sessionHeader.Controls.Add(sessionInfo);
        sessionHeader.Controls.Add(Link("Loot details", () => tabs.SelectedTab = lootPage));
        CompactAdd(session, sessionHeader);
        var resources = new TableLayoutPanel { Dock = DockStyle.Top, Height = 55, ColumnCount = 6, RowCount = 1, Margin = Padding.Empty };
        var resourceLabels = new Dictionary<string, Label>();
        foreach(string resource in new[] { "Gold", "Silvin", "Mithril", "Iternium", "Fehu", "Gems" })
        {
            resources.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f/6));
            var value = new Label { Name = "overviewResource"+resource, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 11f), ForeColor = resource == "Gold" ? UiAccent : UiText, Margin = new Padding(6, 0, 6, 0) };
            resources.Controls.Add(value); resourceLabels.Add(resource, value);
        }
        CompactAdd(session, resources);
        priorityHint.SetToolTip(session, "Detected-drop estimates from the existing session tracker; these are not verified wallet earnings. Rates use active farming time.");

        refreshOverview = () =>
        {
            if(syncing || IsDisposed) return;
            syncing = true;
            try
            {
                foreach(var update in updates) update();
                if(targetFilter.Text != filter.Text) targetFilter.Text = filter.Text;
                targetFilter.Enabled = CanEdit(filter) && !healerMode.Checked;
                foreach(var (family, button) in targetPresets)
                {
                    bool selected = string.Equals(filter.Text.Trim(), family, StringComparison.OrdinalIgnoreCase);
                    button.BackColor = selected ? UiAccentDark : UiRaised;
                    button.ForeColor = selected ? UiAccent : UiText;
                    button.FlatAppearance.BorderSize = selected ? 1 : 0;
                    button.FlatAppearance.BorderColor = UiAccent;
                    button.Enabled = CanEdit(filter) && !healerMode.Checked;
                }
                mode.SelectedIndex = compactMode.SelectedIndex; mode.Enabled = CanEdit(compactMode);
                range.Minimum = melee.Minimum; range.Maximum = melee.Maximum; range.Value = melee.Value;
                range.Enabled = CanEdit(melee) && !healerMode.Checked;
                anchorText.Text = activeHuntAnchor is { } point
                    ? $"Saved anchor  {point.X:0.0}, {point.Y:0.0}"
                    : "Start saves your hunting anchor and facing.";
                recoveryText.Text = healerMode.Checked ? "Death recovery is unavailable in healer mode."
                    : "Revive → Repair (if enabled) → Saved route";
                foreach(var check in recovery.Controls.OfType<CheckBox>()) check.Enabled &= !healerMode.Checked;
                for(int i = 0; i < routeButtons.Count; i++)
                {
                    var saved = navigation.SavedRoutes[i]; var button = routeButtons[i];
                    string title = i == 0 ? "Primary" : "Alternative "+i;
                    button.Text = saved == null ? title+"   ·   Not recorded" : $"{title}   ·   {saved.Points.Length} points  /  Zone {saved.Zone}";
                    button.BackColor = savedNavigationSlot.SelectedIndex == i ? UiAccentDark : UiRaised;
                    button.ForeColor = savedNavigationSlot.SelectedIndex == i ? UiAccent : UiText;
                    button.Enabled = CanEdit(savedNavigationSlot);
                }
                var snapshot = lootTracker.Snapshot();
                string duration = $"{(int)snapshot.RateElapsed.TotalHours:00}:{snapshot.RateElapsed.Minutes:00}:{snapshot.RateElapsed.Seconds:00}";
                double goldPerHour = snapshot.HourlyLoot.FirstOrDefault(v => v.Name == "Gold")?.PerHour ?? 0;
                sessionInfo.Text = $"Active time  {duration}     ·     Gold / hour  {goldPerHour:N0}     ·     Detected drops";
                foreach(var (name, label) in resourceLabels)
                    label.Text = name+"\n"+(snapshot.TrackedLoot.FirstOrDefault(v => v.Name == name)?.Count ?? 0).ToString("N0", CultureInfo.CurrentCulture);
            }
            finally { syncing = false; }
        };
        timer.Tick += (_, _) => { if(page.Visible) refreshOverview(); };
        page.VisibleChanged += (_, _) => { if(page.Visible) refreshOverview(); };
        refreshOverview();
        return page;
    }
}
