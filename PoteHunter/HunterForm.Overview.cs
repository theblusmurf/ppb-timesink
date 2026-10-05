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
        bool previousDurabilityRepair=durabilityRepair.Checked;
        decimal previousDurabilityThreshold=durabilityThreshold.Value;
        var previousFolds = Controls.Find("orbitalControlBody", true).Single()
            .Controls.OfType<CollapsibleSection>().Select(fold => (Fold: fold, Expanded: fold.Expanded)).ToArray();
        try
        {
            tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(p => p.Text == "Overview");
            refreshOverview?.Invoke(); PerformLayout(); Application.DoEvents();
            CheckCollapsibleOverview();
            var oldSize=Size;Size=new Size(1480,1000);PerformLayout();Application.DoEvents();
            using(var full=new Bitmap(Width,Height)){DrawToBitmap(full,new Rectangle(Point.Empty,Size));full.Save(Path.Combine(AppContext.BaseDirectory,"orbital-overview.png"));}
            Size=oldSize;PerformLayout();Application.DoEvents();
            T Find<T>(string name) where T : Control => Controls.Find(name, true).OfType<T>().Single();
            // Native PerformClick correctly ignores hidden controls. Open the actual
            // target section first, exactly as a user does from the operation rail.
            Find<CollapsibleSection>("overviewFoldTargets").Expanded = true;
            PerformLayout(); Application.DoEvents();
            Find<Button>("overviewTargetMimic").PerformClick();
            if(filter.Text != "Mimic" || CurrentOptions().Target != "Mimic") throw new Exception("Overview target preset did not update the bound filter.");
            Find<TextBox>("overviewTargetFilter").Text = "Custom monster";
            if(filter.Text != "Custom monster") throw new Exception("Overview custom filter was discarded.");
            if(navigation.RouteTargetKey!="CUSTOM MONSTER")throw new Exception("Custom target route set did not follow the filter.");
            filter.Text = "Tribal";
            if(Find<TextBox>("overviewTargetFilter").Text != "Tribal") throw new Exception("Overview did not follow detailed target edits.");
            var repair = Find<CheckBox>("overviewRepair"); repair.Checked = !autoRepair.Checked;
            if(repair.Checked != autoRepair.Checked) throw new Exception("Overview repair toggle did not bind.");
            var lowRepair=Find<CheckBox>("overviewDurabilityRepair");var lowThreshold=Find<NumericUpDown>("overviewDurabilityThreshold");
            lowRepair.Checked=true;lowThreshold.Value=23;
            if(!durabilityRepair.Checked || durabilityThreshold.Value!=23 || !CurrentOptions().AutoRepairLowDurability || CurrentOptions().RepairDurabilityPercent!=23)
                throw new Exception("Overview durability threshold did not bind to persisted settings.");
            for(int modeIndex=0;modeIndex<4;modeIndex++)
            {
                compactMode.SelectedIndex=modeIndex;refreshOverview?.Invoke();
                if(!lowRepair.Enabled || !lowThreshold.Enabled)
                    throw new Exception("Durability repair was unavailable in a stopped operating mode.");
            }
            compactMode.SelectedIndex=0;
            if(navigation.RouteTargetKey!="TRIBAL" || !Find<Label>("overviewRouteTarget").Text.Contains("Tribal"))throw new Exception("Route set label did not follow target selection.");
            navigation.BeginRecording(new(0,0));filter.Text="Mimic";
            if(navigation.Recording || !navigation.RecordingCancelled || navigation.RouteTargetKey!="MIMIC")throw new Exception("Changing target did not cancel the old recording.");
            filter.Text="Tribal";
            working = true; refreshOverview?.Invoke();RefreshNavigationRecordingControls();
            if(clearSavedNavigationRoute.Enabled || clearAllSavedNavigationRoutes.Enabled || assignUnassignedNavigationRoutes.Enabled)throw new Exception("Route mutations allowed during hunting.");
            if(Find<Button>("overviewTargetMimic").Enabled || Find<TextBox>("overviewTargetFilter").Enabled || repair.Enabled || Find<ComboBox>("overviewMode").Enabled || Find<Button>("overviewRoute0").Enabled)
                throw new Exception("Overview permits settings changes while hunting.");
            bool lockedRepair = autoRepair.Checked; repair.Checked = !lockedRepair;
            if(autoRepair.Checked != lockedRepair) throw new Exception("Overview bypassed running-state guard.");
            if(lowRepair.Enabled || lowThreshold.Enabled)throw new Exception("Durability settings remained enabled during hunting.");
            lowRepair.Checked=false;lowThreshold.Value=24;
            if(!durabilityRepair.Checked || durabilityThreshold.Value!=23)throw new Exception("Overview durability settings bypassed the running-state guard.");
            working = false;RefreshNavigationRecordingControls(); compactMode.SelectedIndex = 3; refreshOverview?.Invoke();
            if(Find<ComboBox>("overviewMode").SelectedIndex != 3 || Find<CheckBox>("overviewRevive").Enabled || Find<Button>("overviewTargetMimic").Enabled)
                throw new Exception("Overview healer state differs from detailed settings.");
            compactMode.SelectedIndex = 0; refreshOverview?.Invoke();
            foreach(string name in new[] { "Gold", "Silvin", "Mithril", "Iternium", "Fehu", "Gems" })
                if(!Find<Label>("overviewResource"+name).Text.StartsWith(LootTrackerSnapshot.DisplayName(name)+"\n")) throw new Exception("Overview resource missing: "+name);
            if(Find<Label>("overviewResourceGold").Text!="Gold (net)\n—")throw new Exception("Unavailable Overview wallet was shown as earnings");
            PerformLayout(); Application.DoEvents();
            if(Text!="PlayPoteBot · Orbital Ops")throw new Exception("PlayPoteBot title branding was lost.");
            Find<CollapsibleSection>("overviewFoldRecovery").Expanded = true;
            PerformLayout(); Application.DoEvents();
            foreach(string name in new[]{"overviewGamekeeper","overviewRevive","overviewRepair"})
            {
                var check=Find<CheckBox>(name);
                int needed=TextRenderer.MeasureText(check.Text,check.Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width+47;
                if(check.Width<needed || check.Height<25)throw new Exception("Crownfire switch caption is clipped: "+name);
            }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"crownfire-caption-checks.json"),System.Text.Json.JsonSerializer.Serialize(
                new[]{"overviewGamekeeper","overviewRevive","overviewRepair"}.Select(name=>{
                    var check=Controls.Find(name,true).OfType<CheckBox>().SingleOrDefault();
                    return new{Name=name,Text=check?.Text,Width=check?.Width,Height=check?.Height,AutoSize=check?.AutoSize,
                        Measured=check==null?0:TextRenderer.MeasureText(check.Text,check.Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width};})));
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
            durabilityRepair.Checked=previousDurabilityRepair;durabilityThreshold.Value=previousDurabilityThreshold;
            foreach(var (fold, expanded) in previousFolds) fold.Expanded = expanded;
            compactSaveTimer.Stop(); tabs.SelectedTab = previousPage; refreshOverview?.Invoke();
        }
    }

    // Overview controls edit the existing settings. Folds store presentation state only.
    TabPage CreateFieldOverview(TabControl tabs, TabPage setupPage)
    {
        var page = new TabPage("Overview") { AutoScroll = true, BackColor = UiWindow };
        var body = CompactTable(); body.Name = "fieldOverviewBody";
        page.Controls.Add(body);
        var columns = new TableLayoutPanel
        {
            Name = "overviewColumns", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty
        };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        columns.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var left = CompactTable(); left.Name = "overviewMapColumn"; left.Margin = new Padding(0, 0, 7, 0);
        var right = CompactTable(); right.Name = "overviewHuntColumn"; right.Margin = new Padding(7, 0, 0, 0);
        columns.Controls.Add(left, 0, 0); columns.Controls.Add(right, 1, 0);
        CompactAdd(body, columns);
        var updates = new List<Action>();
        bool syncing = false;
        bool CanEdit(Control source, bool combatOnly = false) => !working && !busy && !clientRecoveryRunning && source.Enabled && (!combatOnly || !healerMode.Checked);
        void OpenSetup(Control? control = null)
        {
            tabs.SelectedTab = setupPage;
            if(control != null) setupPage.ScrollControlIntoView(control);
        }
        Button Link(string text, Action action, string name = "")
        {
            var button = new Button { Name = name, Text = text, AutoSize = true, Margin = new Padding(0, 3, 6, 3) };
            button.Click += (_, _) => action(); return button;
        }
        CheckBox Toggle(string name, string text, CheckBox source, bool combatOnly = false)
        {
            var check = new CheckBox { Name = name, Text = text, AutoSize = true, Margin = new Padding(0, 4, 8, 6) };
            check.CheckedChanged += (_, _) =>
            {
                if(syncing) return;
                if(CanEdit(source, combatOnly)) { source.Checked = check.Checked; QueueCompactSave(); }
                refreshOverview?.Invoke();
            };
            updates.Add(() => { check.Checked = source.Checked; check.Enabled = CanEdit(source, combatOnly); });
            source.CheckedChanged += (_, _) => refreshOverview?.Invoke();
            source.EnabledChanged += (_, _) => refreshOverview?.Invoke();
            return check;
        }
        NumericUpDown BoundNumber(string name, NumericUpDown source, bool combatOnly = false)
        {
            var number = Number(source.Minimum, source.Maximum, source.DecimalPlaces);
            number.Name = name; number.Increment = source.Increment; number.Width = 72;
            number.AccessibleName = name.Replace("overview", "");
            number.ValueChanged += (_, _) =>
            {
                if(syncing) return;
                if(CanEdit(source, combatOnly)) { source.Value = Math.Clamp(number.Value, source.Minimum, source.Maximum); QueueCompactSave(); }
                refreshOverview?.Invoke();
            };
            updates.Add(() =>
            {
                number.Minimum = source.Minimum; number.Maximum = source.Maximum;
                number.DecimalPlaces = source.DecimalPlaces; number.Increment = source.Increment;
                number.Value = source.Value; number.Enabled = CanEdit(source, combatOnly);
            });
            source.ValueChanged += (_, _) => refreshOverview?.Invoke();
            source.EnabledChanged += (_, _) => refreshOverview?.Invoke();
            return number;
        }
        Label Detail(string name, string text = "") => new()
        { Name = name, Text = text, AutoSize = true, ForeColor = UiMuted, Margin = new Padding(0, 3, 0, 7), UseMnemonic = false };
        var folds = new List<CollapsibleSection>();
        CollapsibleSection Fold(TableLayoutPanel column, string name, string title, string icon, bool expanded = false)
        {
            var fold = new CollapsibleSection(name, title, icon, expanded);
            folds.Add(fold); CompactAdd(column, fold); return fold;
        }
        void RadiusControls(TableLayoutPanel content, string title, string name, NumericUpDown source, string explanation)
        {
            var number = BoundNumber(name, source, true); number.AccessibleName = title + " in map units";
            var heading = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var titleLabel = Detail(name + "Label", title); titleLabel.Margin = new Padding(0, 6, 8, 3);
            heading.Controls.Add(titleLabel, 0, 0);
            heading.Controls.Add(CompactFlow(number, Caption("map units")), 1, 0);
            CompactAdd(content, heading);
            CompactAdd(content, Detail(name + "Hint", explanation));
            var slider = new TrackBar
            {
                Name = name + "Slider", Minimum = (int)(source.Minimum * 2), Maximum = (int)(source.Maximum * 2),
                SmallChange = 1, LargeChange = 10, TickStyle = TickStyle.None, AutoSize = false,
                Height = 27, Dock = DockStyle.Top, Margin = Padding.Empty, AccessibleName = title
            };
            slider.ValueChanged += (_, _) =>
            {
                if(syncing) return;
                if(CanEdit(source, true)) { source.Value = Math.Clamp(slider.Value / 2m, source.Minimum, source.Maximum); QueueCompactSave(); }
                refreshOverview?.Invoke();
            };
            updates.Add(() =>
            {
                slider.Minimum = (int)(source.Minimum * 2); slider.Maximum = (int)(source.Maximum * 2);
                slider.Value = Math.Clamp((int)decimal.Round(source.Value * 2), slider.Minimum, slider.Maximum);
                slider.Enabled = CanEdit(source, true);
            });
            CompactAdd(content, slider);
        }

        var anchor = CompactCard("ANCHOR & ROUTE"); CompactAdd(left, anchor);
        var anchorText = Detail("overviewAnchor"); CompactAdd(anchor, anchorText);
        var routeMap = new OrbitalScanner { Height = 285 };
        updates.Add(() => UpdateOrbitalScanner(routeMap));
        CompactAdd(anchor, routeMap);
        var mapOptions = CompactFlow(Toggle("overviewShowRoutes", "Show route overlay", showRouteOverlay));
        var mapFollow = Link("Follow player", () => followMapPlayer.PerformClick(), "overviewFollowPlayer");
        mapOptions.Controls.Add(mapFollow); CompactAdd(anchor, mapOptions);
        var mapLegend = Detail("overviewMapLegend"); CompactAdd(anchor, mapLegend);
        var routeTargetText = Detail("overviewRouteTarget"); CompactAdd(anchor, routeTargetText);
        var radii = Fold(left, "overviewFoldRadii", "Route & anchor radius", "Routes", true);
        RadiusControls(radii.Content, "Route corridor", "overviewRouteCorridorRadius", routeCorridorRadius, "Distance from the recorded path when joining a saved route.");
        RadiusControls(radii.Content, "Anchor area", "overviewAnchorRadius", radius, "Hunting area measured from the saved activation anchor.");
        CompactAdd(radii.Content, Detail("overviewRadiusNote", "Shared across route slots. Loot pickup has its own radius."));

        var routes = Fold(left, "overviewFoldRoutes", "Saved route options", "Routes");
        var routeButtons = new List<Button>();
        for(int slot = 0; slot < Navigation.SavedRouteSlotCount; slot++)
        {
            int selectedSlot = slot;
            var button = new Button { Name = "overviewRoute" + slot, Dock = DockStyle.Top, Height = 35, Margin = new Padding(0, 0, 0, 5), TextAlign = ContentAlignment.MiddleLeft };
            button.Click += (_, _) =>
            {
                if(CanEdit(savedNavigationSlot)) savedNavigationSlot.SelectedIndex = selectedSlot;
                refreshOverview?.Invoke();
            };
            CompactAdd(routes.Content, button); routeButtons.Add(button);
        }
        savedNavigationSlot.SelectedIndexChanged += (_, _) => refreshOverview?.Invoke();
        var routeFallback = Detail("overviewRouteFallback"); CompactAdd(routes.Content, routeFallback);
        var record = Link("Record · Home", () => { if(CanEdit(startNavigationRecording)) startNavigationRecording.PerformClick(); refreshOverview?.Invoke(); }, "overviewRecordRoute");
        var save = Link("Save finish · End", () => { if(CanEdit(saveNavigationRoute)) saveNavigationRoute.PerformClick(); refreshOverview?.Invoke(); }, "overviewSaveRoute");
        CompactAdd(routes.Content, CompactFlow(record, save));
        var recordStatus = Detail("overviewRecordingStatus"); CompactAdd(routes.Content, recordStatus);
        CompactAdd(routes.Content, Link("All navigation options", () => tabs.SelectedTab = navigationPage));
        updates.Add(() => { record.Enabled = CanEdit(startNavigationRecording); save.Enabled = CanEdit(saveNavigationRoute); recordStatus.Text = navigationRecordingStatus.Text; });

        var foldToolbar = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 7) };
        foldToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); foldToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foldToolbar.Controls.Add(Detail("overviewHuntHeading", "HUNT CONFIGURATION"), 0, 0);
        foldToolbar.Controls.Add(Link("Collapse all", () => { foreach(var fold in folds) fold.Expanded = false; }, "overviewCollapseAll"), 1, 0);
        CompactAdd(right, foldToolbar);
        var targets = Fold(right, "overviewFoldTargets", "Targets & combat", "Hunt", true);
        var targetButtons = new TableLayoutPanel { Dock = DockStyle.Top, Height = 76, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 0, 0, 5) };
        var targetPresets = new List<(string Name, Button Button)>();
        foreach(string family in new[] { "Mimic", "Pulkhan", "Tribal", "Tower" })
        {
            targetButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var button = new Button { Name = "overviewTarget" + family, Text = family, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 6, 0), AccessibleDescription = "Select only the " + family + " target filter" };
            button.Click += (_, _) => { if(CanEdit(filter, true)) { filter.Text = family; QueueCompactSave(); } refreshOverview?.Invoke(); };
            targetPresets.Add((family, button)); targetButtons.Controls.Add(button);
        }
        CompactAdd(targets.Content, targetButtons);
        var targetFilter = new TextBox { Name = "overviewTargetFilter", Width = 170, AccessibleName = "Target name filter" };
        targetFilter.TextChanged += (_, _) => { if(!syncing) { if(CanEdit(filter, true)) { filter.Text = targetFilter.Text; QueueCompactSave(); } refreshOverview?.Invoke(); } };
        filter.TextChanged += (_, _) => refreshOverview?.Invoke();
        CompactAdd(targets.Content, CompactRow("Name filter", targetFilter));
        CompactAdd(targets.Content, Toggle("overviewGamekeeper", "Prioritize Gamekeeper", prioritizeGamekeeper, true));
        var mode = new ComboBox { Name = "overviewMode", DropDownStyle = ComboBoxStyle.DropDownList, Width = 175, AccessibleName = "Operating mode" };
        mode.Items.AddRange(compactMode.Items.Cast<object>().ToArray());
        mode.SelectionChangeCommitted += (_, _) => { if(CanEdit(compactMode)) compactMode.SelectedIndex = mode.SelectedIndex; refreshOverview?.Invoke(); };
        compactMode.SelectedIndexChanged += (_, _) => refreshOverview?.Invoke();
        CompactAdd(targets.Content, CompactRow("Mode", mode));
        var range = BoundNumber("overviewAttackRange", melee, true); range.AccessibleName = "Attack range in map units";
        CompactAdd(targets.Content, CompactRow("Attack range", range, Caption("map units")));
        priorityHint.SetToolTip(targetButtons, "Choose one target family, or enter a name filter. Gamekeeper priority is independent.");
        priorityHint.SetToolTip(range, "Effective attack range; ranged and class limits follow detailed Hunt settings.");
        CompactAdd(targets.Content, Link("Additional targeting options", () => { tabs.SelectedTab = protectionPage; }));

        var skills = Fold(right, "overviewFoldSkills", "Skills & self-healing", "Support");
        CompactAdd(skills.Content, Toggle("overviewAutoSkills", "Detect attack skills automatically", autoSkills, true));
        CompactAdd(skills.Content, Detail("overviewSkillRule", $"Attack skills: {SkillGroupStatus.MinimumTargets} targets in range · highest HP below {SkillGroupStatus.MaximumHighestHealthPercent:0}% · {SkillGroupStatus.DelayMilliseconds / 1000d:0.0} s gap."));
        CompactAdd(skills.Content, Toggle("overviewSelfHealRule", "Self-heal health rule", healthSkillCondition, true));
        var healThreshold = BoundNumber("overviewSelfHealPercent", healthSkillPercent, true); healThreshold.AccessibleName = "Self-heal health threshold";
        CompactAdd(skills.Content, CompactRow("Self-heal below", healThreshold, Caption("% HP")));
        var reserve = new NumericUpDown { Name = "overviewManaReserve", Minimum = 0, Maximum = 100, Width = 72, AccessibleName = "Mana reserve percent" };
        reserve.ValueChanged += (_, _) =>
        {
            if(syncing) return;
            if(CanEdit(manaReserve)) { manaReserve.Value = (int)reserve.Value; QueueCompactSave(); }
            refreshOverview?.Invoke();
        };
        updates.Add(() => { reserve.Value = manaReserve.Value; reserve.Enabled = CanEdit(manaReserve); });
        manaReserve.ValueChanged += (_, _) => refreshOverview?.Invoke();
        CompactAdd(skills.Content, CompactRow("Mana reserve", reserve, Caption("%")));
        CompactAdd(skills.Content, Detail("overviewSkillExemptions", "Recognized self-heals bypass the mana reserve and attack skill gap."));
        CompactAdd(skills.Content, Link("All skill and healing settings", () => OpenSetup(healthSkillCondition)));

        var movement = Fold(right, "overviewFoldMovement", "Movement & loot", "Routes");
        var stationaryText = Detail("overviewStationaryStatus"); CompactAdd(movement.Content, stationaryText);
        CompactAdd(movement.Content, Toggle("overviewStationaryGamekeeper", "Hold position for Gamekeeper", stationaryGamekeeperPriority, true));
        CompactAdd(movement.Content, Toggle("overviewPickup", "Collect nearby loot", nearbyLootPickup, true));
        var pickupRadius = BoundNumber("overviewLootPickupRadius", lootPickupRadius, true); pickupRadius.AccessibleName = "Loot pickup radius in map units";
        CompactAdd(movement.Content, CompactRow("Pickup radius", pickupRadius, Caption("map units")));
        CompactAdd(movement.Content, Toggle("overviewReturnGamekeeper", "Return after Gamekeeper", returnToHuntLocation, true));
        CompactAdd(movement.Content, Toggle("overviewLeaveArea", "Leave empty area, then return", leaveAreaWhenEmpty, true));
        CompactAdd(movement.Content, Detail("overviewMovementNote", "Solo loot excursions restore the saved location and facing."));
        CompactAdd(movement.Content, Link("Movement and protection settings", () => tabs.SelectedTab = protectionPage));

        var recovery = Fold(right, "overviewFoldRecovery", "Death & client recovery", "Recovery");
        CompactAdd(recovery.Content, Toggle("overviewRevive", "Auto revive + return to anchor", autoRevive, true));
        CompactAdd(recovery.Content, Toggle("overviewRepair", "Auto repair after revival", autoRepair, true));
        CompactAdd(recovery.Content, Toggle("overviewDurabilityRepair", "Repair low durability", durabilityRepair));
        CompactAdd(recovery.Content, CompactRow("Repair at", BoundNumber("overviewDurabilityThreshold",durabilityThreshold),Caption("% or lower")));
        CompactAdd(recovery.Content, Toggle("overviewResume", "Resume farming on arrival", farmOnArrival, true));
        var recoveryText = Detail("overviewRecoveryStatus"); CompactAdd(recovery.Content, recoveryText);
        CompactAdd(recovery.Content, Link("Revival & repair setup", () => OpenSetup(durabilityRepair.Checked?durabilityRepair:autoRevive), "overviewRecoverySetup"));
        CompactAdd(recovery.Content, Toggle("overviewClientRecovery", "Recover client closure", autoClientRecovery, true));
        var clientText = Detail("overviewClientRecoveryStatus"); CompactAdd(recovery.Content, clientText);
        CompactAdd(recovery.Content, Link("Client crash and login setup", () => OpenSetup(autoClientRecovery), "overviewClientRecoverySetup"));

        var session = CompactCard("COMPASS LEDGER"); CompactAdd(body, session);
        var sessionInfo = Detail("overviewSession");
        var sessionHeader = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        sessionInfo.Margin = new Padding(0, 8, 14, 0); sessionHeader.Controls.Add(sessionInfo);
        sessionHeader.Controls.Add(Link("Reset loot", ResetTrackedLoot, "overviewResetLoot"));
        sessionHeader.Controls.Add(Link("Reset timer", ResetTrackedLootTimer, "overviewResetTimer"));
        sessionHeader.Controls.Add(Link("Loot details", () => tabs.SelectedTab = lootPage));
        CompactAdd(session, sessionHeader);
        var resources = new TableLayoutPanel { Dock = DockStyle.Top, Height = 108, ColumnCount = 6, RowCount = 1, Margin = Padding.Empty };
        var resourceLabels = new Dictionary<string, Label>();
        foreach(string resource in new[] { "Gold", "Silvin", "Mithril", "Iternium", "Fehu", "Gems" })
        {
            resources.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 6));
            var tile = new Panel { Dock = DockStyle.Fill, Margin = new Padding(5, 0, 5, 0), BackColor = UiRaised };
            tile.Paint += (_, e) =>
            {
                var icon = new RectangleF(7, (tile.Height - 28) / 2f, 28, 28);
                if(resource == "Gold") CrownfireControls.Glyph(e.Graphics, "Gold", icon, ImperialTheme.Gold);
                else GameLootIcons.Draw(e.Graphics, resource, icon);
            };
            var value = new Label { Name = "overviewResource" + resource, Dock = DockStyle.Bottom, Height = 52, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI Semibold", 10f), ForeColor = UiText, BackColor = Color.Transparent, Margin = Padding.Empty };
            tile.Controls.Add(value); resources.Controls.Add(tile); resourceLabels.Add(resource, value);
        }
        CompactAdd(session, resources);
        priorityHint.SetToolTip(session, "Gold is actual wallet change from the session baseline, including costs and other income. Other resources are detected-drop estimates. Rates use active farming time.");
        ApplyOrbitalOverview(page, body, columns, left, right, anchor, routeMap, session, sessionHeader, resources,
            folds, mode, targetFilter, mapOptions, foldToolbar, sessionInfo);
        string NumberText(decimal value) => value.ToString("0.#", CultureInfo.InvariantCulture);
        refreshOverview = () =>
        {
            if(syncing || IsDisposed) return;
            syncing = true;
            try
            {
                foreach(var update in updates) update();
                routeMap.Invalidate();
                if(targetFilter.Text != filter.Text) targetFilter.Text = filter.Text;
                targetFilter.Enabled = CanEdit(filter, true);
                foreach(var (family, button) in targetPresets)
                {
                    bool selected = string.Equals(filter.Text.Trim(), family, StringComparison.OrdinalIgnoreCase);
                    button.BackColor = selected ? UiAccentDark : UiRaised;
                    button.ForeColor = selected ? UiAccent : UiText;
                    button.FlatAppearance.BorderSize = selected ? 1 : 0; button.FlatAppearance.BorderColor = UiAccent;
                    button.AccessibleDescription = (selected ? "Selected. " : "Not selected. ") + "Select only the " + family + " target filter.";
                    button.Enabled = CanEdit(filter, true);
                }
                mode.SelectedIndex = compactMode.SelectedIndex; mode.Enabled = CanEdit(compactMode);
                string selectedTitle = SelectedSavedNavigationSlot() == 0 ? "Primary" : "Alternative " + SelectedSavedNavigationSlot();
                bool stationary = Targeting.IsStationaryHuntFilter(filter.Text);
                targets.Summary = healerMode.Checked ? compactMode.Text + " · attack targets unused"
                    : (string.IsNullOrWhiteSpace(filter.Text) ? "All allowed targets" : filter.Text.Trim()) + " · " + NumberText(melee.Value) + " units" + (prioritizeGamekeeper.Checked ? " · Gamekeeper first" : "");
                skills.Summary = healerMode.Checked ? "Party healing · " + manaReserve.Value + "% mana reserve"
                    : (healthSkillCondition.Checked ? "Heal below " + NumberText(healthSkillPercent.Value) + "% HP" : "Self-heal rule off") + (autoSkills.Checked ? " · auto attack skills" : " · manual skill keys");
                movement.Summary = healerMode.Checked ? "Healer movement · pickup off" : (stationary ? "Hold anchor" : "Hunting movement") + (nearbyLootPickup.Checked ? " · loot " + NumberText(lootPickupRadius.Value) + " units" : " · pickup off");
                stationaryText.Text = healerMode.Checked ? "Healer mode uses its existing follow and healing rules."
                    : stationary ? "Selected target family holds the saved anchor for combat." : "Selected targets use the existing hunting movement rules.";
                recovery.Summary = healerMode.Checked ? "Recovery unavailable in healer mode" : autoRevive.Checked
                    ? "Revive → " + (autoRepair.Checked ? "repair → " : "") + selectedTitle + (farmOnArrival.Checked ? " → hunt" : " → stop") + (autoClientRecovery.Checked ? " · client recovery" : "")
                    : "Automatic revival off" + (autoClientRecovery.Checked ? " · client recovery enabled" : "");
                radii.Summary = "Route: " + NumberText(routeCorridorRadius.Value) + " units · Anchor: " + NumberText(radius.Value) + " units";
                mapLegend.Text = "Route corridor · " + NumberText(routeCorridorRadius.Value) + " units     Anchor area · " + NumberText(radius.Value) + " units";
                anchorText.Text = activeHuntAnchor is { } point ? $"Saved anchor  {point.X:0.0}, {point.Y:0.0} · {selectedTitle}" : "Start saves your hunting anchor and facing. · " + selectedTitle;
                recoveryText.Text = durabilityRepair.Checked ? "Lowest equipped durability: " + (latestDurability.LowestPercent is decimal lowest ? NumberText(lowest)+"%" : "unknown") + " · repair at " + NumberText(durabilityThreshold.Value) + "% or lower, including combat." : healerMode.Checked ? "Death recovery is unavailable in healer mode."
                    : "Uses recognized revival and configured repair, then the target’s saved route.";
                clientText.Text = clientRecoveryStatus.Text;
                routeTargetText.Text = $"Targets: {navigation.RouteTargetLabel}" + (navigation.UnassignedRouteCount > 0 ? " · Assign existing routes in Navigation" : "");
                int recorded = 0;
                for(int i = 0; i < routeButtons.Count; i++)
                {
                    var saved = navigation.SavedRoutes[i]; var button = routeButtons[i];
                    string title = i == 0 ? "Primary" : "Alternative " + i;
                    if(saved != null && RecoveryTravel.Recorded(saved)) recorded++;
                    button.Text = saved == null ? title + "   ·   Not recorded" : $"{title}   ·   {saved.Points.Length} points / Zone {saved.Zone}";
                    button.BackColor = savedNavigationSlot.SelectedIndex == i ? UiAccentDark : UiRaised;
                    button.ForeColor = savedNavigationSlot.SelectedIndex == i ? UiAccent : UiText;
                    button.Enabled = CanEdit(savedNavigationSlot);
                }
                routes.Summary = selectedTitle + " · " + navigation.RouteTargetLabel + " · " + recorded + " recorded";
                routeFallback.Text = recorded > 1 ? "Recovery can choose a free compatible alternative using the shared origin." : "Record alternative routes for occupied-spot fallback.";
                var snapshot = lootTracker.Snapshot();
                refreshOrbitalTelemetry?.Invoke(snapshot);
                string duration = $"{(int)snapshot.RateElapsed.TotalHours:00}:{snapshot.RateElapsed.Minutes:00}:{snapshot.RateElapsed.Seconds:00}";
                sessionInfo.Text = $"Active time  {duration}     ·     Gold / hour  {snapshot.RateText("Gold")}     ·     Net wallet gold";
                foreach(var (name, label) in resourceLabels) label.Text = LootTrackerSnapshot.DisplayName(name) + "\n" + snapshot.AmountText(name);
                priorityHint.SetToolTip(resourceLabels["Gold"], snapshot.Wallet.Known ? $"Wallet {snapshot.Wallet.Current:N0} · Baseline {snapshot.Wallet.Baseline:N0} · {snapshot.Wallet.Status}" : snapshot.Wallet.Status);
            }
            finally { syncing = false; }
        };
        // The left operation rail remains visible on every page.
        timer.Tick += (_, _) => refreshOverview();
        page.VisibleChanged += (_, _) => { if(page.Visible) refreshOverview(); };
        refreshOverview();
        return page;
    }
}
