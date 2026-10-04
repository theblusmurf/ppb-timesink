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
            var oldSize=Size;Size=new Size(1280,1120);PerformLayout();Application.DoEvents();
            using(var full=new Bitmap(Width,Height)){DrawToBitmap(full,new Rectangle(Point.Empty,Size));full.Save(Path.Combine(AppContext.BaseDirectory,"fantasy-overview.png"));}
            Size=oldSize;PerformLayout();Application.DoEvents();
            T Find<T>(string name) where T : Control => Controls.Find(name, true).OfType<T>().Single();
            Find<Button>("overviewTargetMimic").PerformClick();
            if(filter.Text != "Mimic" || CurrentOptions().Target != "Mimic") throw new Exception("Overview target preset did not update the bound filter.");
            Find<TextBox>("overviewTargetFilter").Text = "Custom monster";
            if(filter.Text != "Custom monster") throw new Exception("Overview custom filter was discarded.");
            if(navigation.RouteTargetKey!="CUSTOM MONSTER")throw new Exception("Custom target route set did not follow the filter.");
            filter.Text = "Tribal";
            if(Find<TextBox>("overviewTargetFilter").Text != "Tribal") throw new Exception("Overview did not follow detailed target edits.");
            var repair = Find<CheckBox>("overviewRepair"); repair.Checked = !autoRepair.Checked;
            if(repair.Checked != autoRepair.Checked) throw new Exception("Overview repair toggle did not bind.");
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
            working = false;RefreshNavigationRecordingControls(); compactMode.SelectedIndex = 3; refreshOverview?.Invoke();
            if(Find<ComboBox>("overviewMode").SelectedIndex != 3 || Find<CheckBox>("overviewRevive").Enabled || Find<Button>("overviewTargetMimic").Enabled)
                throw new Exception("Overview healer state differs from detailed settings.");
            compactMode.SelectedIndex = 0; refreshOverview?.Invoke();
            foreach(string name in new[] { "Gold", "Silvin", "Mithril", "Iternium", "Fehu", "Gems" })
                if(!Find<Label>("overviewResource"+name).Text.StartsWith(LootTrackerSnapshot.DisplayName(name)+"\n")) throw new Exception("Overview resource missing: "+name);
            if(Find<Label>("overviewResourceGold").Text!="Gold (net)\n—")throw new Exception("Unavailable Overview wallet was shown as earnings");
            PerformLayout(); Application.DoEvents();
            if(Text!="PlayPoteBot · Adventurer’s Compass")throw new Exception("PlayPoteBot title branding was lost.");
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
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
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

        var targets = CompactCard("HUNT PREFERENCES"); CompactAdd(right, targets);
        var targetButtons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 84, WrapContents = false, Margin = Padding.Empty };
        var targetPresets = new List<(string Name, Button Button)>();
        foreach(string family in new[] { "Mimic", "Pulkhan", "Tribal", "Tower" })
        {
            var button = new Button { Name = "overviewTarget"+family, Text = family, Size = new Size(100, 76), Margin = new Padding(0, 0, 6, 6), AccessibleDescription = "Select the "+family+" target filter" };
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

        var combat = CompactCard("SKILLS & COMBAT"); CompactAdd(right, combat);
        var mode = new ComboBox { Name = "overviewMode", DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        mode.Items.AddRange(compactMode.Items.Cast<object>().ToArray());
        mode.SelectionChangeCommitted += (_, _) => { if(CanEdit(compactMode)) compactMode.SelectedIndex = mode.SelectedIndex; refreshOverview?.Invoke(); };
        compactMode.SelectedIndexChanged += (_, _) => refreshOverview?.Invoke();
        var range = Number(1, 30, 1); range.Name = "overviewAttackRange"; range.DecimalPlaces = melee.DecimalPlaces;
        range.ValueChanged += (_, _) => { if(!syncing && CanEdit(melee)) { melee.Value = Math.Clamp(range.Value, melee.Minimum, melee.Maximum); QueueCompactSave(); } };
        melee.ValueChanged += (_, _) => refreshOverview?.Invoke();
        CompactAdd(targets, CompactRow("Mode / range", mode, Caption("Range"), range));
        priorityHint.SetToolTip(range, "Effective attack range in map units; ranged/class limits follow detailed Hunt settings.");
        CompactAdd(combat, Toggle("overviewAutoSkills", "Detect attack skills automatically", autoSkills));
        CompactAdd(combat, new Label { Text = "Combat skill gap  1.5 s  ·  Self-heals exempt", AutoSize = true, ForeColor = UiMuted, Margin = new Padding(0, 0, 0, 5) });
        var skillsToggle=Link("Skills and combat ▸",()=>{combat.Visible=!combat.Visible;});
        skillsToggle.Name="overviewSkillsToggle";CompactAdd(targets,skillsToggle);
        combat.Visible=false;
        CompactAdd(combat, Link("All hunt settings", () => OpenSetup()));

        var anchor = CompactCard("ANCHOR ROUTE"); CompactAdd(left, anchor);
        var anchorText = Detail("overviewAnchor"); CompactAdd(anchor, anchorText);
        var routeMap=new Panel {Name="overviewRouteMap",Dock=DockStyle.Top,Height=300,Margin=new Padding(0,4,0,8),BackColor=UiWindow};
        routeMap.Paint+=(_,e)=>DrawRouteOverlay(e.Graphics,routeMap.ClientSize);
        CompactAdd(anchor,routeMap);
        CompactAdd(anchor,Toggle("overviewShowRoutes","Show route overlay",showRouteOverlay));
        CompactAdd(anchor, Toggle("overviewPickup", "Collect loot within 10 units; return to anchor", nearbyLootPickup));
        priorityHint.SetToolTip(anchor, "Solo loot excursions use the saved activation anchor. Pickup, return, combat and recovery rules are unchanged.");

        var recovery = CompactCard("PROTECTION & RECOVERY"); CompactAdd(right, recovery);
        CompactAdd(recovery,Toggle("overviewSelfHealRule","Self-heal health rule",healthSkillCondition));
        var healThreshold=Number(1,100);healThreshold.Name="overviewSelfHealPercent";
        healThreshold.ValueChanged+=(_,_)=>{if(!syncing && CanEdit(healthSkillPercent)){healthSkillPercent.Value=healThreshold.Value;QueueCompactSave();}};
        updates.Add(()=>{healThreshold.Value=healthSkillPercent.Value;healThreshold.Enabled=CanEdit(healthSkillPercent);});
        healthSkillPercent.ValueChanged+=(_,_)=>refreshOverview?.Invoke();
        CompactAdd(recovery,CompactRow("Self-heal below",healThreshold,Caption("% HP")));
        CompactAdd(recovery, Toggle("overviewRevive", "Auto revive + return to anchor", autoRevive));
        CompactAdd(recovery, Toggle("overviewRepair", "Auto repair after revival", autoRepair));
        CompactAdd(recovery, Toggle("overviewResume", "Resume farming on arrival", farmOnArrival));
        var recoveryText = Detail("overviewRecoveryStatus"); CompactAdd(recovery, recoveryText);
        CompactAdd(recovery, Link("Revival & repair setup", () => OpenSetup(autoRevive)));

        var routes = CompactCard("SAVED ROUTES"); CompactAdd(left, routes);
        routes.Visible=false;
        CompactAdd(anchor,Link("Saved routes ▸",()=>routes.Visible=!routes.Visible));
        var routeTargetText=Detail("overviewRouteTarget");CompactAdd(anchor,routeTargetText);
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

        var session = CompactCard("LOOT SESSION"); CompactAdd(body, session);
        var sessionInfo = Detail("overviewSession");
        var sessionHeader = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, AutoSize=true, WrapContents = true, Margin = Padding.Empty };
        sessionInfo.Margin = new Padding(0, 8, 14, 0);
        sessionHeader.Controls.Add(sessionInfo);
        sessionHeader.Controls.Add(Link("Reset loot", ResetTrackedLoot));
        sessionHeader.Controls.Add(Link("Reset timer", ResetTrackedLootTimer));
        sessionHeader.Controls.Add(Link("Loot details", () => tabs.SelectedTab = lootPage));
        CompactAdd(session, sessionHeader);
        var resources = new TableLayoutPanel { Dock = DockStyle.Top, Height = 108, ColumnCount = 6, RowCount = 1, Margin = Padding.Empty };
        var resourceLabels = new Dictionary<string, Label>();
        foreach(string resource in new[] { "Gold", "Silvin", "Mithril", "Iternium", "Fehu", "Gems" })
        {
            resources.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f/6));
            var tile=new Panel{Dock=DockStyle.Fill,Margin=new Padding(6,0,6,0),BackColor=UiRaised};
            tile.Paint+=(_,e)=>
            {
                var icon=new RectangleF((tile.Width-48)/2f,7,48,42);
                if(resource=="Gold")CrownfireControls.Glyph(e.Graphics,"Gold",icon,UiAccent);
                else GameLootIcons.Draw(e.Graphics,resource,icon);
                FantasyFrame.Draw(e.Graphics,tile.ClientRectangle);
            };
            var value = new Label { Name = "overviewResource"+resource, Dock = DockStyle.Bottom,Height=52, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Georgia", 11f), ForeColor = resource == "Gold" ? UiAccent : UiText, Margin = Padding.Empty };
            tile.Controls.Add(value);resources.Controls.Add(tile); resourceLabels.Add(resource, value);
        }
        CompactAdd(session, resources);
        priorityHint.SetToolTip(session, "Gold is actual wallet change from the session baseline, including costs and other income. Other resources are detected-drop estimates. Rates use active farming time.");

        refreshOverview = () =>
        {
            if(syncing || IsDisposed) return;
            syncing = true;
            try
            {
                foreach(var update in updates) update();
                routeMap.Invalidate();
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
                routeTargetText.Text=$"Targets: {navigation.RouteTargetLabel}"+(navigation.UnassignedRouteCount>0?" · Assign existing routes in Navigation":"");
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
                sessionInfo.Text = $"Active time  {duration}     ·     Gold / hour  {snapshot.RateText("Gold")}     ·     Net wallet gold";
                foreach(var (name, label) in resourceLabels)
                    label.Text = LootTrackerSnapshot.DisplayName(name)+"\n"+snapshot.AmountText(name);
                priorityHint.SetToolTip(resourceLabels["Gold"],snapshot.Wallet.Known?$"Wallet {snapshot.Wallet.Current:N0} · Baseline {snapshot.Wallet.Baseline:N0} · {snapshot.Wallet.Status}":snapshot.Wallet.Status);
            }
            finally { syncing = false; }
        };
        timer.Tick += (_, _) => { if(page.Visible) refreshOverview(); };
        page.VisibleChanged += (_, _) => { if(page.Visible) refreshOverview(); };
        refreshOverview();
        return page;
    }
}
