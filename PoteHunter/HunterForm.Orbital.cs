namespace PoteHunter;

public sealed partial class HunterForm
{
    TableLayoutPanel? orbitalRailBody;
    Action<LootTrackerSnapshot>? refreshOrbitalTelemetry;

    void UpdateOrbitalScanner(OrbitalScanner scanner)
    {
        // recognitionSelf is frozen from a poll that passed the zone/identity guards.
        // navigationPosition/3D timestamps are updated earlier, before a transition can be rejected.
        bool fresh = PlayerRecognitionFresh && recognitionSelf!.Position.Finite;
        int? zone = connected && navigationZone > 0 ? navigationZone : null;
        scanner.SetReadings(connected, zone, zone.HasValue ? navigation.SavedRoutesForZone(zone.Value).ToArray() : [],
            fresh ? recognitionSelf!.Position : null, fresh ? recognitionSelf!.Heading : 0,
            [], // The mutable hunt entity list is not an accepted-poll snapshot.
            CurrentRouteCorridorRadius(), zone.HasValue ? CurrentAnchorAreaCenter() : null,
            (double)(working ? activeGuardOptions?.HuntRadius ?? radius.Value : radius.Value));
    }

    // This is composition only: the original Overview instances retain every binding and guard.
    void ApplyOrbitalOverview(TabPage page, TableLayoutPanel body, TableLayoutPanel columns,
        TableLayoutPanel left, TableLayoutPanel right, TableLayoutPanel anchor, OrbitalScanner scanner,
        TableLayoutPanel session, FlowLayoutPanel sessionHeader, TableLayoutPanel resources,
        List<CollapsibleSection> folds, ComboBox mode, TextBox targetFilter, FlowLayoutPanel mapOptions,
        TableLayoutPanel foldToolbar, Label sessionInfo)
    {
        orbitalRailBody = CompactTable(); orbitalRailBody.Name = "orbitalControlBody";
        orbitalRailBody.BackColor = UiSurface; orbitalRailBody.Padding = new Padding(0, 0, 1, 12);
        orbitalRailBody.Font = new Font("Segoe UI", 9f);
        var targetFold = folds.Single(f => f.Name == "overviewFoldTargets");
        void Field(string title, Control input)
        {
            var oldRow = input.Parent?.Parent;
            var field = CompactTable(); field.Margin = new Padding(0, 0, 0, 12);
            CompactAdd(field, new Label { Text = title, AutoSize = true, ForeColor = UiMuted,
                Font = new Font("Segoe UI", 8.5f), Margin = new Padding(0, 0, 0, 6) });
            input.Dock = DockStyle.Top; input.Margin = Padding.Empty; input.Width = 280;
            CompactAdd(field, input); CompactAdd(orbitalRailBody, field);
            if (oldRow != null && oldRow.Parent == targetFold.Content)
            { targetFold.Content.Controls.Remove(oldRow); oldRow.Dispose(); }
        }
        Field("Hunt mode", mode); Field("Target name filter", targetFilter);
        var priority = targetFold.Content.Controls.Find("overviewGamekeeper", true).Single();
        priority.Margin = new Padding(0, 0, 0, 13); CompactAdd(orbitalRailBody, priority);
        var foldHeading = foldToolbar.Controls.Find("overviewHuntHeading", true).OfType<Label>().Single();
        foldHeading.Text = "CONFIGURATION"; foldHeading.Font = new Font("Segoe UI Semibold", 8f);
        var collapse = foldToolbar.Controls.Find("overviewCollapseAll", true).Single();
        collapse.Font = new Font("Segoe UI", 8f); collapse.Margin = Padding.Empty;
        CompactAdd(orbitalRailBody, foldToolbar);
        foreach (string name in new[] { "Targets", "Skills", "Movement", "Recovery", "Radii", "Routes" })
        {
            var fold = folds.Single(f => f.Name == "overviewFold" + name);
            fold.Expanded = false; CompactAdd(orbitalRailBody, fold);
        }
        CompactAdd(folds.Single(f => f.Name == "overviewFoldRoutes").Content, mapOptions);
        // Reset buttons remain actual controls and stay on the left with all other operations.
        sessionInfo.Visible = false; sessionHeader.Margin = new Padding(0, 9, 0, 0);
        foreach (var button in sessionHeader.Controls.OfType<Button>()) button.Font = new Font("Segoe UI", 8.5f);
        CompactAdd(orbitalRailBody, sessionHeader);

        // Keep the same body and column identities for accessibility and the native UI fixtures.
        body.Controls.Clear(); body.RowStyles.Clear(); body.RowCount = 2;
        body.AutoSize = false; body.Dock = DockStyle.Top; body.Padding = new Padding(10, 0, 2, 0);
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        page.AutoScroll = true; page.Padding = Padding.Empty;
        void FitCanvas() => body.Height = Math.Max(page.ClientSize.Height, 570 * DeviceDpi / 96);
        page.SizeChanged += (_, _) => FitCanvas(); page.VisibleChanged += (_, _) => FitCanvas();
        columns.AutoSize = false; columns.Dock = DockStyle.Fill; columns.Margin = Padding.Empty;
        columns.ColumnStyles[0].Width = 64; columns.ColumnStyles[1].Width = 36;
        columns.RowStyles[0].SizeType = SizeType.Percent; columns.RowStyles[0].Height = 100;
        left.Controls.Clear(); left.RowStyles.Clear(); left.RowCount = 2;
        left.AutoSize = false; left.Dock = DockStyle.Fill; left.Margin = new Padding(0, 0, 16, 9);
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        scanner.Dock = DockStyle.Fill; left.Controls.Add(scanner, 0, 0);
        var notes = CompactTable(); notes.Margin = Padding.Empty; notes.AutoSize = false; notes.Dock = DockStyle.Fill;
        foreach (string name in new[] { "overviewAnchor", "overviewMapLegend", "overviewRouteTarget" })
        {
            var label = anchor.Controls.Find(name, true).OfType<Label>().Single();
            label.MaximumSize = Size.Empty; label.MinimumSize = Size.Empty;
            label.AutoSize = false; label.Dock = DockStyle.Fill; label.Height = 21;
            label.Font = new Font("Segoe UI", 8f); label.AutoEllipsis = true; label.Margin = Padding.Empty;
            label.TextAlign = ContentAlignment.MiddleLeft; CompactAdd(notes, label);
            label.TextChanged += (_, _) => priorityHint.SetToolTip(label, label.Text);
        }
        left.Controls.Add(notes, 0, 1);

        right.Controls.Clear(); right.RowStyles.Clear(); right.RowCount = 1;
        right.AutoSize = false; right.Dock = DockStyle.Fill; right.Margin = new Padding(10, 0, 0, 9);
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var telemetry = CreateOrbitalTelemetry(); right.Controls.Add(telemetry, 0, 0);
        session.Controls.OfType<Label>().Single(l => l.Name == "ironboundCardHeading").Text = "CARGO MANIFEST  /  SESSION RESOURCES";
        session.Padding = new Padding(10, 8, 10, 8); session.Margin = new Padding(0, 5, 0, 0);
        resources.Height = 78;
        // An implicit AutoSize row can retain each Panel's original 100px preferred
        // height, overflowing this 78px dock and the Overview's scrollable canvas.
        resources.RowStyles.Clear(); resources.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (Panel tile in resources.Controls)
        {
            tile.BackColor = UiWindow; tile.Margin = new Padding(2, 0, 2, 0);
            var value = tile.Controls.OfType<Label>().Single();
            value.Dock = DockStyle.Fill; value.Padding = new Padding(42, 0, 0, 0);
            value.TextAlign = ContentAlignment.MiddleLeft; value.Font = new Font("Segoe UI Semibold", 9.5f);
            value.ForeColor = UiText; value.AutoEllipsis = true;
        }
        body.Controls.Add(columns, 0, 0); body.Controls.Add(session, 0, 1); FitCanvas();
    }

    TableLayoutPanel CreateOrbitalTelemetry()
    {
        var panel = new TableLayoutPanel { Name = "orbitalTelemetry", Dock = DockStyle.Fill, Margin = Padding.Empty,
            ColumnCount = 1, RowCount = 9, BackColor = UiWindow, Padding = new Padding(0, 14, 4, 0) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 24, 64, 24, 1, 62 }) panel.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 22, 80, 32 }) panel.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        Label TextLabel(string name, string text, float size, Color color) => new()
        {
            Name = name, Text = text, Dock = DockStyle.Fill, Margin = Padding.Empty, ForeColor = color,
            Font = new Font(size >= 18 ? "Segoe UI Semibold" : "Segoe UI", size),
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, UseMnemonic = false
        };
        panel.Controls.Add(TextLabel("orbitalSessionHeading", "SESSION TELEMETRY  /  NET GOLD", 8.5f, UiMuted), 0, 0);
        var gold = TextLabel("orbitalSessionGold", "—", 43f, UiText); panel.Controls.Add(gold, 0, 1);
        var rate = TextLabel("orbitalGoldRate", "— gold / active hour", 9f, UiAccent); panel.Controls.Add(rate, 0, 2);
        panel.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = UiBorder, Margin = Padding.Empty }, 0, 3);
        var metrics = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, RowCount = 2 };
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63)); metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        metrics.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        metrics.Controls.Add(TextLabel("orbitalTimeHeading", "ACTIVE TIME", 8f, UiMuted), 0, 0);
        metrics.Controls.Add(TextLabel("orbitalKillsHeading", "TRACKED KILLS", 8f, UiMuted), 1, 0);
        var elapsed = TextLabel("orbitalActiveTime", "00:00:00", 18f, UiText);
        var kills = TextLabel("orbitalTrackedKills", "0", 20f, UiText);
        metrics.Controls.Add(elapsed, 0, 1); metrics.Controls.Add(kills, 1, 1); panel.Controls.Add(metrics, 0, 4);
        var targetHeading = TextLabel("orbitalTargetHeading", "TARGET FAMILIES", 8f, UiMuted); panel.Controls.Add(targetHeading, 0, 6);
        var roster = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        roster.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (string family in new[] { "Mimic", "Pulkhan", "Tribal", "Tower" })
        {
            roster.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var portrait = new Panel { Name = "orbitalPortrait" + family, Dock = DockStyle.Fill, Margin = new Padding(0, 3, 5, 3),
                AccessibleName = family + " target family artwork", AccessibleRole = AccessibleRole.Graphic };
            portrait.Controls.Add(new Label { Name = "orbitalTargetLabel" + family, Text = family, Dock = DockStyle.Bottom,
                Height = 20, TextAlign = ContentAlignment.MiddleCenter, BackColor = UiWindow, ForeColor = UiMuted,
                Font = new Font("Segoe UI", 7.5f), AutoEllipsis = true, Margin = Padding.Empty });
            portrait.Paint += (_, e) =>
            {
                bool selected = string.Equals(filter.Text.Trim(), family, StringComparison.OrdinalIgnoreCase);
                using var fill = new SolidBrush(UiRaised); e.Graphics.FillRectangle(fill, new Rectangle(0, 0, portrait.Width - 1, Math.Max(1, portrait.Height - 21)));
                using var border = new Pen(selected ? UiAccent : UiBorder); e.Graphics.DrawRectangle(border, 0, 0, Math.Max(1, portrait.Width - 1), Math.Max(1, portrait.Height - 22));
                float size = Math.Max(1, Math.Min(portrait.Width - 8, portrait.Height - 26));
                ImperialTheme.DrawTarget(e.Graphics, family, new RectangleF((portrait.Width - size) / 2, 3, size, size));
            };
            roster.Controls.Add(portrait);
        }
        panel.Controls.Add(roster, 0, 7);
        var selection = TextLabel("orbitalTargetSelection", "", 8f, UiMuted); panel.Controls.Add(selection, 0, 8);
        refreshOrbitalTelemetry = snapshot =>
        {
            gold.Text = snapshot.AmountText("Gold"); rate.Text = snapshot.RateText("Gold") + " gold / active hour";
            var time = snapshot.RateElapsed; elapsed.Text = $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
            kills.Text = snapshot.Sources.Sum(s => s.Kills).ToString("N0");
            selection.Text = healerMode.Checked ? "Healer mode · attack targets unused" :
                string.IsNullOrWhiteSpace(filter.Text) ? "All allowed targets · no name filter" : "Name filter: " + filter.Text.Trim();
            priorityHint.SetToolTip(gold, snapshot.Wallet.Status + "\nNet wallet change includes spending and other income.");
            priorityHint.SetToolTip(selection, selection.Text); roster.Invalidate(true);
        };
        return panel;
    }
}
