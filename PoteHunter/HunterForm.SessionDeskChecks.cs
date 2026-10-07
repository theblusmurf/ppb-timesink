namespace PoteHunter;

public sealed partial class HunterForm
{
    void CheckSessionDesk(TabControl tabs)
    {
        if(!offlinePreviewMode || connected || working) throw new InvalidOperationException("Session Desk checks require the disconnected fixture.");
        T Find<T>(string name) where T : Control => Controls.Find(name, true).OfType<T>().Single();
        var old = (Page: tabs.SelectedTab, Target: filter.Text, Mode: compactMode.SelectedIndex, Repair: autoRepair.Checked, Route: routeCorridorRadius.Value, Reserve: manaReserve.Value);
        var folds = new[] { "Targets", "Skills", "Movement", "Recovery", "Radii", "Routes" }.Select(name => Find<CollapsibleSection>("deskFold" + name)).ToArray();
        var expanded = folds.Select(f => f.Expanded).ToArray();
        var oldSize = Size;
        void Layout() { PerformLayout(); Application.DoEvents(); }
        void Capture(string name)
        {
            Layout(); using var image = new Bitmap(Width, Height); DrawToBitmap(image, new Rectangle(Point.Empty, Size));
            image.Save(Path.Combine(AppContext.BaseDirectory, name));
        }
        try
        {
            tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().Single(p => p.Text == "Overview");
            compactMode.SelectedIndex = 0; refreshOverview?.Invoke();
            Size = new Size(1480, 1000); Layout();
            if(folds.Any(f => f.Expanded || string.IsNullOrWhiteSpace(f.Summary))) throw new Exception("Session Desk must start with six collapsed summaries.");
            Find<Button>("deskTargetMimic").PerformClick();
            if(filter.Text != "Mimic" || Find<TextBox>("overviewTargetFilter").Text != "Mimic") throw new Exception("Session Desk shortcut lost its shared target binding.");
            Find<TextBox>("deskTargetFilter").Text = "Tribal";
            if(filter.Text != "Tribal" || navigation.RouteTargetKey != "TRIBAL") throw new Exception("Session Desk custom filter lost its route binding.");
            foreach(var fold in folds)
            {
                fold.Header.AccessibilityObject.DoDefaultAction(); Layout();
                if(!fold.Expanded || !fold.Content.Visible || (fold.Header.AccessibilityObject.State & AccessibleStates.Expanded) == 0)
                    throw new Exception("Session Desk section lost accessible expansion: " + fold.Name);
            }
            Find<CheckBox>("deskRepair").Checked = !autoRepair.Checked;
            if(Find<CheckBox>("deskRepair").Checked != autoRepair.Checked || Find<CheckBox>("overviewRepair").Checked != autoRepair.Checked)
                throw new Exception("Session Desk repair edits diverged from Farming.");
            Find<NumericUpDown>("deskRouteCorridorRadius").Value = 13.5m;
            Find<NumericUpDown>("deskManaReserve").Value = 23;
            if(routeCorridorRadius.Value != 13.5m || manaReserve.Value != 23 || Find<TrackBar>("deskRouteCorridorRadiusSlider").Value != 27)
                throw new Exception("Session Desk radius or reserve edits lost their guarded bindings.");
            filter.Text = "Pulkhan"; routeCorridorRadius.Value = 10;
            if(Find<TextBox>("deskTargetFilter").Text != "Pulkhan" || Find<NumericUpDown>("deskRouteCorridorRadius").Value != 10)
                throw new Exception("Session Desk did not follow edits from another page.");
            foreach(string state in new[] { "working", "busy", "client recovery", "healer" })
            {
                working = state == "working"; busy = state == "busy"; clientRecoveryRunning = state == "client recovery";
                if(state == "healer") compactMode.SelectedIndex = 3;
                refreshOverview?.Invoke();
                var edit = Find<TextBox>("deskTargetFilter"); var range = Find<NumericUpDown>("deskRouteCorridorRadius"); var repair = Find<CheckBox>("deskRepair");
                if(edit.Enabled || range.Enabled || repair.Enabled || Find<Button>("deskTargetMimic").Enabled || (state != "healer" && Find<Button>("deskRoute0").Enabled))
                    throw new Exception("Session Desk permits protected edits during " + state);
                bool savedRepair = autoRepair.Checked;
                edit.Text = "Rejected edit"; range.Value = 11.5m; repair.Checked = !savedRepair;
                if(filter.Text != "Pulkhan" || routeCorridorRadius.Value != 10 || autoRepair.Checked != savedRepair ||
                    edit.Text != filter.Text || range.Value != routeCorridorRadius.Value || repair.Checked != autoRepair.Checked)
                    throw new Exception("A programmatic Session Desk edit bypassed the " + state + " guard.");
                working = busy = clientRecoveryRunning = false; compactMode.SelectedIndex = 0; refreshOverview?.Invoke();
            }
            Find<Button>("deskCollapseAll").PerformClick(); Layout();
            if(folds.Any(f => f.Expanded || f.Content.Visible || string.IsNullOrWhiteSpace(f.Summary))) throw new Exception("Session Desk collapse all discarded a summary.");
            tabs.SelectedTab!.AutoScrollPosition = Point.Empty; Capture("session-desk-overview.png");
            folds[3].Expanded = true; tabs.SelectedTab.ScrollControlIntoView(folds[3]); Capture("session-desk-recovery-expanded.png");
            foreach(var size in new[] { new Size(1480, 1000), MinimumSize })
            {
                Size = size; Layout();
                foreach(var fold in folds)
                {
                    tabs.SelectedTab.ScrollControlIntoView(fold); Layout();
                    if(fold.Header.Height < 55 || fold.Header.Width < 270 || !fold.ClientRectangle.Contains(fold.Header.Bounds))
                        throw new Exception("Session Desk group clips at " + size + ": " + fold.Name);
                }
                var quick = Find<TableLayoutPanel>("sessionDeskQuick");
                foreach(string name in new[] { "deskMode", "deskTargetFilter", "deskGamekeeper" })
                {
                    var control = Find<Control>(name); var rect = quick.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                    if(!quick.ClientRectangle.Contains(rect)) throw new Exception($"Session Desk quick field clips: {name}; child={rect}; quick={quick.ClientRectangle}; field={control.Parent!.ClientRectangle}; control={control.Bounds}");
                }
                foreach(string name in new[] { "overviewAnchor", "overviewMapLegend", "overviewRouteTarget" })
                {
                    var note = Find<Label>(name);
                    if(note.Height < note.Font.Height || !note.Parent!.ClientRectangle.Contains(note.Bounds)) throw new Exception("Session Desk route note clips: " + name);
                }
            }
            Size = MinimumSize; foreach(var fold in folds) fold.Expanded = false;
            tabs.SelectedTab.AutoScrollPosition = Point.Empty; Capture("session-desk-minimum.png");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "session-desk-checks.json"), System.Text.Json.JsonSerializer.Serialize(new
            { Passed = true, Checks = new[] { "six accessible collapsed sections", "target presets and shared route filtering", "two-way Farming synchronization", "radius slider and mana bindings", "working/busy/client/healer mutation guards", "collapse all preserves summaries", "minimum-size quick fields, group headings and route notes", "offline native screenshots; no game input" } }));
        }
        finally
        {
            working = busy = clientRecoveryRunning = false;
            compactMode.SelectedIndex = old.Mode; filter.Text = old.Target; autoRepair.Checked = old.Repair;
            routeCorridorRadius.Value = old.Route; manaReserve.Value = old.Reserve;
            for(int i = 0; i < folds.Length; i++) folds[i].Expanded = expanded[i];
            compactSaveTimer.Stop(); Size = oldSize; tabs.SelectedTab = old.Page; refreshOverview?.Invoke(); Layout();
        }
    }
}
