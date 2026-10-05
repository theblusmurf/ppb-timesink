namespace PoteHunter;

public sealed partial class HunterForm
{
    void CheckCollapsibleOverview()
    {
        T Find<T>(string name) where T : Control => Controls.Find(name, true).OfType<T>().Single();
        var folds = new[] { "Targets", "Skills", "Movement", "Recovery", "Radii", "Routes" }
            .Select(name => Find<CollapsibleSection>("overviewFold" + name)).ToArray();
        var previous = folds.Select(fold => fold.Expanded).ToArray();
        var previousSize = Size;
        var previousMode = compactMode.SelectedIndex;
        var previousRadii = (Route: routeCorridorRadius.Value, Anchor: radius.Value, Loot: lootPickupRadius.Value);
        int previousReserve = manaReserve.Value;
        string previousTarget = filter.Text;
        var oldBusy = busy; var oldWorking = working; var oldClientRecovery = clientRecoveryRunning;
        void Layout() { PerformLayout(); Application.DoEvents(); }
        void Save(string name)
        {
            Layout();
            using var bitmap = new Bitmap(Width, Height);
            DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
            bitmap.Save(Path.Combine(AppContext.BaseDirectory, name));
        }
        try
        {
            busy = working = clientRecoveryRunning = false;
            compactMode.SelectedIndex = 0; refreshOverview?.Invoke();
            if(folds.Any(fold => fold.Expanded))
                throw new Exception("Overview initial fold states differ from the approved command layout.");
            Size = new Size(1480, 1000); Save("collapsible-overview-default.png");
            foreach(var fold in folds)
            {
                if(!fold.Header.TabStop || fold.Header.AccessibleRole != AccessibleRole.PushButton || string.IsNullOrWhiteSpace(fold.Summary))
                    throw new Exception("A fold lost keyboard accessibility or its summary: " + fold.Name);
                bool opened = fold.Expanded; fold.Header.PerformClick(); Layout();
                if(fold.Expanded == opened || fold.Content.Visible != fold.Expanded || !fold.Header.AccessibleDescription!.Contains(fold.Summary))
                    throw new Exception("Fold header did not toggle its body while retaining a summary: " + fold.Name);
                var accessible = fold.Header.AccessibilityObject;
                if((accessible.State & (fold.Expanded ? AccessibleStates.Expanded : AccessibleStates.Collapsed)) == 0)
                    throw new Exception("Fold expansion was not exposed to accessibility: " + fold.Name);
                accessible.DoDefaultAction();
                if(fold.Expanded != opened) throw new Exception("Accessible fold action failed: " + fold.Name);
            }
            Find<Button>("overviewCollapseAll").PerformClick(); Layout();
            if(folds.Any(fold => fold.Expanded || fold.Content.Visible || string.IsNullOrWhiteSpace(fold.Summary)))
                throw new Exception("Collapse all hid a summary or left a section open.");
            Save("collapsible-overview-collapsed.png");
            foreach(var fold in folds) fold.Expanded = true;
            Layout();
            var route = Find<NumericUpDown>("overviewRouteCorridorRadius");
            var anchor = Find<NumericUpDown>("overviewAnchorRadius");
            var loot = Find<NumericUpDown>("overviewLootPickupRadius");
            var routeSlider = Find<TrackBar>("overviewRouteCorridorRadiusSlider");
            var anchorSlider = Find<TrackBar>("overviewAnchorRadiusSlider");
            if(route.Minimum != .5m || route.Maximum != 30m || anchor.Minimum != 5m || anchor.Maximum != 150m ||
                loot.Minimum != .5m || loot.Maximum != 30m || route.Increment != .5m || anchor.Increment != .5m || loot.Increment != .5m)
                throw new Exception("Overview radius bounds or increments changed existing anchor semantics.");
            route.Value = 13.5m; anchor.Value = 42.5m; loot.Value = 7.5m;
            var options = CurrentOptions();
            if(options.RouteCorridorRadius != 13.5m || options.HuntRadius != 42.5m || options.LootPickupRadius != 7.5m ||
                routeSlider.Value != 27 || anchorSlider.Value != 85)
                throw new Exception("Overview radii did not update source options and sliders.");
            routeSlider.Value = 9; anchorSlider.Value = 75;
            if(routeCorridorRadius.Value != 4.5m || radius.Value != 37.5m || route.Value != 4.5m || anchor.Value != 37.5m)
                throw new Exception("Radius sliders lost half-unit two-way binding.");
            routeCorridorRadius.Value = 10; radius.Value = 35; lootPickupRadius.Value = 10;
            if(route.Value != 10 || anchor.Value != 35 || loot.Value != 10 ||
                !folds[4].Summary.Contains("Route: 10 units") || !folds[4].Summary.Contains("Anchor: 35 units"))
                throw new Exception("Detailed radius changes did not refresh Overview values and summaries.");
            Find<NumericUpDown>("overviewManaReserve").Value = 23;
            if(manaReserve.Value != 23 || CurrentOptions().ManaReservePercent != 23)
                throw new Exception("Overview mana reserve lost its existing option binding.");
            foreach(var state in new[] { "working", "busy", "client recovery", "healer" })
            {
                working = state == "working"; busy = state == "busy"; clientRecoveryRunning = state == "client recovery";
                if(state == "healer") compactMode.SelectedIndex = 3;
                refreshOverview?.Invoke();
                if(route.Enabled || anchor.Enabled || loot.Enabled || Find<Button>("overviewTargetMimic").Enabled ||
                    Find<CheckBox>("overviewPickup").Enabled || Find<CheckBox>("overviewRevive").Enabled || Find<CheckBox>("overviewClientRecovery").Enabled)
                    throw new Exception("Fold controls permit settings edits during " + state + ".");
                var sourceRadii = (routeCorridorRadius.Value, radius.Value, lootPickupRadius.Value);
                route.Value = 11.5m; anchor.Value = 43.5m; loot.Value = 8.5m;
                Find<TextBox>("overviewTargetFilter").Text = "Locked mutation";
                if(sourceRadii != (routeCorridorRadius.Value, radius.Value, lootPickupRadius.Value) || filter.Text != previousTarget ||
                    route.Value != routeCorridorRadius.Value || anchor.Value != radius.Value || loot.Value != lootPickupRadius.Value)
                    throw new Exception("Programmatic disabled edit bypassed the " + state + " guard.");
                // Folding stays usable while hunting; it does not edit game settings.
                folds[2].Header.PerformClick(); folds[2].Header.PerformClick();
                working = busy = clientRecoveryRunning = false; compactMode.SelectedIndex = 0; refreshOverview?.Invoke();
            }
            Save("collapsible-overview-expanded.png");
            for(int i = 0; i < folds.Length; i++) folds[i].Expanded = previous[i];
            Size = MinimumSize; Layout();
            var columns = Find<TableLayoutPanel>("overviewColumns");
            var left = Find<TableLayoutPanel>("overviewMapColumn");
            var right = Find<TableLayoutPanel>("overviewHuntColumn");
            if(left.Right > right.Left || right.Right > columns.ClientSize.Width || left.Width < 320 || right.Width < 200)
                throw new Exception("Collapsible Overview columns overlap or overflow at minimum size.");
            foreach(var fold in folds)
                if(fold.Header.Width < 270 || fold.Header.Height < 55 || !fold.ClientRectangle.Contains(fold.Header.Bounds))
                    throw new Exception("Fold header clips at minimum size: " + fold.Name);
            var presets = new[] { "Mimic", "Pulkhan", "Tribal", "Tower" }.Select(name => Find<Button>("overviewTarget" + name)).ToArray();
            foreach(var button in presets)
                if(!button.Parent!.ClientRectangle.Contains(button.Bounds) || button.Width < 56)
                    throw new Exception("Target preset clips at minimum size: " + button.Name);
            Save("collapsible-overview-minimum.png");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "collapsible-overview-checks.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                Passed = true,
                Checks = new[] { "approved initial folds", "native button and accessibility expansion actions", "summaries remain when collapsed",
                    "collapse all", "two-way route/anchor/pickup binding", "half-unit numeric and slider increments", "preserved 5–150 anchor bounds",
                    "mana reserve binding", "working/busy/client/healer mutation guards", "minimum-size columns and target presets", "no game input" },
                Folds = folds.Select(fold => new { fold.Name, fold.Summary, fold.Expanded, HeaderWidth = fold.Header.Width, HeaderHeight = fold.Header.Height })
            }));
        }
        finally
        {
            working = busy = clientRecoveryRunning = false;
            compactMode.SelectedIndex = previousMode; routeCorridorRadius.Value = previousRadii.Route;
            radius.Value = previousRadii.Anchor; lootPickupRadius.Value = previousRadii.Loot; manaReserve.Value = previousReserve;
            filter.Text = previousTarget; compactSaveTimer.Stop();
            for(int i = 0; i < folds.Length; i++) folds[i].Expanded = previous[i];
            working = oldWorking; busy = oldBusy; clientRecoveryRunning = oldClientRecovery;
            Size = previousSize; refreshOverview?.Invoke(); Layout();
        }
    }
}
