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

    // Keep Farming intact while Overview shares its guarded bindings.
    void PrepareFarmingControls(FlowLayoutPanel sessionHeader,
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

    }
}
