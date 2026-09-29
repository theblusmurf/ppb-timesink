using System.Diagnostics;

namespace PoteHunter;

public sealed partial class HunterForm
{
    const int NavigationOverlayMargin = 24;
    readonly CheckBox showNavigationOverlay = new() { Text = "Show radar overlay", AutoSize = true };
    readonly CheckBox showLootTrackerOverlay = new() { Text = "Show loot tracker", AutoSize = true, Checked = true };
    readonly CheckBox guideTreasureChests = new() { Text = "Guide to treasure chests", AutoSize = true, Checked = true };
    readonly CheckBox showTreasureChestMarkers = new() { Text = "Show treasure boxes on map", AutoSize = true, Checked = true };
    readonly NumericUpDown navigationOverlaySize = new()
    {
        Minimum = 200,
        Maximum = 900,
        Increment = 25,
        Value = 450,
        Width = 70
    };
    readonly NumericUpDown navigationViewRadius = new() { Minimum = 10, Maximum = 2000, Increment = 10, Value = 150, Width = 70 };
    readonly Button fitNavigationRadius = new() { Text = "Fit loaded", AutoSize = true };
    readonly CheckBox useSavedRecoveryRoute = new() { Text = "Use saved death route", AutoSize = true, Checked = true };
    readonly CheckBox useAlternativeHuntRoutes = new() { Text = "Use alternatives when a player occupies the primary spot", AutoSize = true, Checked = true };
    readonly ComboBox savedNavigationSlot = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
    readonly Label savedNavigationRoutesStatus = new() { AutoSize = true, ForeColor = Color.Silver };
    readonly Button saveNavigationSpot = new() { Text = "Save current spot", AutoSize = true };
    readonly Button saveNavigationRoute = new() { Text = "Finish route & save spot", AutoSize = true };
    readonly Button clearSavedNavigationRoute = new() { Text = "Clear selected", AutoSize = true };
    readonly Button clearAllSavedNavigationRoutes = new() { Text = "Clear all routes", AutoSize = true };
    readonly Button resetLootTracker = new() { Text = "Reset loot", AutoSize = true };
    readonly Button resetLootTimer = new() { Text = "Reset timer", AutoSize = true };
    NavigationOverlay? navigationOverlay;
    LootTrackerOverlay? lootTrackerOverlay;

    /// <summary>Adds and restores the passive radar controls in the Navigation header.</summary>
    void InitializeNavigationOverlay(FlowLayoutPanel navControls)
    {
        ArgumentNullException.ThrowIfNull(navControls);
        navControls.WrapContents=true;navControls.AutoSize=true;navControls.Dock=DockStyle.Top;
        navControls.ParentChanged+=(_,_)=>
        {
            if(navControls.Parent is TableLayoutPanel layout && layout.RowStyles.Count>0)
                layout.RowStyles[0].SizeType=SizeType.AutoSize;
        };
        try
        {
            var options = Options.Read();
            showNavigationOverlay.Checked = options.ShowNavigationOverlay;
            showLootTrackerOverlay.Checked = options.ShowLootTrackerOverlay;
            navigationOverlaySize.Value = Math.Clamp(options.NavigationOverlaySize,
                (int)navigationOverlaySize.Minimum, (int)navigationOverlaySize.Maximum);
            navigationViewRadius.Value = Math.Clamp(options.NavigationViewRadius,
                (int)navigationViewRadius.Minimum, (int)navigationViewRadius.Maximum);
            guideTreasureChests.Checked = options.GuideTreasureChests;
            showTreasureChestMarkers.Checked = options.ShowTreasureChestMarkers;
            useSavedRecoveryRoute.Checked = options.UseSavedRecoveryRoute;
            useAlternativeHuntRoutes.Checked = options.UseAlternativeHuntRoutes;
            navigation.LoadSavedRoutes();
        }
        catch
        {
            showNavigationOverlay.Checked = false;
            showLootTrackerOverlay.Checked = true;
            navigationOverlaySize.Value = 450;
            navigationViewRadius.Value = 150;
            useSavedRecoveryRoute.Checked = true;
            useAlternativeHuntRoutes.Checked = true;
        }

        savedNavigationSlot.Items.AddRange(["Primary hunt route", "Alternative route 1", "Alternative route 2"]);
        savedNavigationSlot.SelectedIndex = 0;
        RefreshSavedNavigationRouteStatus();

        var sizeLabel = new Label { Text = "Size:", AutoSize = true, Padding = new Padding(5, 5, 0, 0) };
        navControls.Controls.Add(showNavigationOverlay);
        navControls.Controls.Add(showLootTrackerOverlay);
        navControls.Controls.Add(guideTreasureChests);
        navControls.Controls.Add(showTreasureChestMarkers);
        navControls.Controls.Add(sizeLabel);
        navControls.Controls.Add(navigationOverlaySize);
        navControls.Controls.Add(new Label { Text = "View:", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
        navControls.Controls.Add(navigationViewRadius);
        navControls.Controls.Add(new Label { Text = "m", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        navControls.Controls.Add(fitNavigationRadius);
        navControls.Controls.Add(useSavedRecoveryRoute);
        navControls.Controls.Add(useAlternativeHuntRoutes);
        navControls.Controls.Add(new Label { Text = "Save slot:", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
        navControls.Controls.Add(savedNavigationSlot);
        navControls.Controls.Add(saveNavigationSpot);
        navControls.Controls.Add(saveNavigationRoute);
        navControls.Controls.Add(clearSavedNavigationRoute);
        navControls.Controls.Add(clearAllSavedNavigationRoutes);
        navControls.Controls.Add(savedNavigationRoutesStatus);
        navControls.Controls.Add(resetLootTracker);
        navControls.Controls.Add(resetLootTimer);

        showNavigationOverlay.CheckedChanged += (_, _) => OverlaySettingsChanged();
        showLootTrackerOverlay.CheckedChanged += (_, _) => OverlaySettingsChanged();
        guideTreasureChests.CheckedChanged += (_, _) => OverlaySettingsChanged();
        showTreasureChestMarkers.CheckedChanged += (_, _) => OverlaySettingsChanged();
        navigationOverlaySize.ValueChanged += (_, _) => OverlaySettingsChanged();
        navigationViewRadius.ValueChanged += (_, _) => OverlaySettingsChanged();
        fitNavigationRadius.Click += (_, _) => FitNavigationRadiusToLoaded();
        useSavedRecoveryRoute.CheckedChanged += (_, _) => OverlaySettingsChanged();
        useAlternativeHuntRoutes.CheckedChanged += (_, _) => OverlaySettingsChanged();
        savedNavigationSlot.SelectedIndexChanged += (_, _) => RefreshSavedNavigationRouteStatus();
        saveNavigationSpot.Click += (_, _) => SaveNavigationRouteFromNavigationTab(true);
        saveNavigationRoute.Click += (_, _) => SaveNavigationRouteFromNavigationTab(false);
        clearSavedNavigationRoute.Click += (_, _) =>
        {
            int slot=SelectedSavedNavigationSlot();
            navigation.ClearSavedRoute(slot);
            message=$"{SavedNavigationSlotName(slot)} cleared.";
            RefreshSavedNavigationRouteStatus(); navigationCanvas.Invalidate();
        };
        clearAllSavedNavigationRoutes.Click += (_, _) =>
        {
            navigation.ClearSavedRoute();
            message="All saved hunt routes cleared.";
            RefreshSavedNavigationRouteStatus(); navigationCanvas.Invalidate();
        };
        resetLootTracker.Click += (_, _) => { bool saved = SaveLootLog("Reset loot"); lootTracker.Reset(); if (saved) message = "Loot totals reset."; UpdateNavigationOverlay(); };
        resetLootTimer.Click += (_, _) => { bool saved = SaveLootLog("Reset timer"); lootTracker.ResetTimer(); if (saved) message = "Loot earning timer reset."; UpdateNavigationOverlay(); };
    }

    void OverlaySettingsChanged()
    {
        if (!busy && !working)
        {
            try { CurrentOptions().Save(); }
            catch (Exception ex) { message = ex.Message; }
        }
        UpdateNavigationOverlay();
    }

    /// <summary>Applies the overlay controls to a newly assembled options object.</summary>
    Options WithOverlaySettings(Options options)
    {
        options.ShowNavigationOverlay = showNavigationOverlay.Checked;
        options.ShowLootTrackerOverlay = showLootTrackerOverlay.Checked;
        options.NavigationOverlaySize = (int)navigationOverlaySize.Value;
        options.NavigationViewRadius = (int)navigationViewRadius.Value;
        options.GuideTreasureChests = guideTreasureChests.Checked;
        options.ShowTreasureChestMarkers = showTreasureChestMarkers.Checked;
        options.UseSavedRecoveryRoute = useSavedRecoveryRoute.Checked;
        options.UseAlternativeHuntRoutes = useAlternativeHuntRoutes.Checked;
        try
        {
            var saved=Options.Read();
            options.LootTrackerOverlayX=saved.LootTrackerOverlayX;options.LootTrackerOverlayY=saved.LootTrackerOverlayY;
        }
        catch { }
        if(lootTrackerOverlay is { IsDisposed:false })
        {
            options.LootTrackerOverlayX=lootTrackerOverlay.Left;options.LootTrackerOverlayY=lootTrackerOverlay.Top;
        }
        return options;
    }

    void SaveNavigationRouteFromNavigationTab(bool spotOnly)
    {
        try
        {
            int slot=SelectedSavedNavigationSlot();
            // The primary slot follows the immutable hunt anchor while a run
            // is active. Alternative slots intentionally capture the current
            // position so they can be separate fallback farming locations.
            Vec anchor=slot==0 && activeHuntAnchor is Vec runningAnchor ? runningAnchor : navigationPosition;
            double heading=connected ? world.PlayerHeading() : 0;
            Options routeOptions=Options.Read();
            string routeCharacter=routeOptions.Player;
            double routeHeight=0;
            if(connected)
            {
                try { var current=world.LocalPlayer(); routeCharacter=current.Name; routeHeight=current.Height; }
                catch { }
            }
            var profile=new NavigationRouteProfile(routeCharacter,routeHeight,(double)routeOptions.HuntRadius,
                routeOptions.RevivalDelaySeconds,routeOptions.FarmOnArrival,false);
            bool saved=spotOnly
                ? navigation.SaveCurrentSpot(navigationZone,anchor,heading,slot,profile:profile)
                : navigation.SaveCurrentRoute(navigationZone,anchor,heading,slot,profile:profile);
            if(saved)
                message=$"Saved {SavedNavigationSlotName(slot)} {(spotOnly?"spot":"route")} ({navigation.GetSavedRoute(slot)?.Points.Length ?? 0} points, {profile.Character} / radius {profile.HuntRadius:0.#}).";
            else message="Save the current position or walk a route first; no valid navigation point was found.";
            RefreshSavedNavigationRouteStatus();
            navigationCanvas.Invalidate();
        }
        catch(Exception ex){message="Could not save navigation route: "+ex.Message;}
    }

    int SelectedSavedNavigationSlot() => Math.Clamp(savedNavigationSlot.SelectedIndex,0,Navigation.SavedRouteSlotCount-1);
    static string SavedNavigationSlotName(int slot) => slot switch
    {
        1 => "Alternative route 1",
        2 => "Alternative route 2",
        _ => "Primary hunt route"
    };
    void RefreshSavedNavigationRouteStatus()
    {
        if(savedNavigationRoutesStatus.IsDisposed)return;
        double defaultRadius=0;
        try { defaultRadius=(double)Options.Read().HuntRadius; } catch { }
        savedNavigationRoutesStatus.Text=string.Join("  ·  ",navigation.SavedRoutes.Select((route,slot)=>
            $"{(slot==0?"Primary":$"Alt {slot}")}: {(route==null?"—":$"Z{route.Zone} · {route.Points.Length} pts · {(string.IsNullOrWhiteSpace(route.Character)?"any":route.Character)} · R{(route.HuntRadius>0?route.HuntRadius:defaultRadius):0.#}")}"));
    }

    double NavigationViewRadius() => (double)navigationViewRadius.Value;

    void FitNavigationRadiusToLoaded()
    {
        double furthest = 0;
        foreach (var entity in entities)
        {
            if (!entity.Position.Finite || !(entity.Monster || Targeting.IsChest(entity)) || latestHealth.GetValueOrDefault(entity.Id).Dead) continue;
            double distance = (entity.Position - navigationPosition).Length;
            if (double.IsFinite(distance)) furthest = Math.Max(furthest, distance);
        }
        navigationViewRadius.Value = (decimal)Math.Clamp(Math.Ceiling(furthest + 10),
            (double)navigationViewRadius.Minimum, (double)navigationViewRadius.Maximum);
        OverlaySettingsChanged();
    }

    /// <summary>Refreshes visibility, position and pixels without activating the overlay.</summary>
    void UpdateNavigationOverlay()
    {
        if (IsDisposed || !connected || world.Window == IntPtr.Zero ||
            !GameProcessAlive() || !NavigationOverlay.TryGetClientScreenBounds(world.Window, out var clientBounds))
        {
            HideNavigationOverlay();
            HideLootTrackerOverlay();
            return;
        }

        IntPtr foreground = NavigationOverlay.ForegroundWindow;
        if (foreground != world.Window && foreground != Handle)
        {
            HideNavigationOverlay();
            HideLootTrackerOverlay();
            return;
        }

        if (showNavigationOverlay.Checked)
        {
            int requested = (int)navigationOverlaySize.Value;
            int width = Math.Min(requested, Math.Max(1, clientBounds.Width - NavigationOverlayMargin * 2));
            int height = Math.Min(requested, Math.Max(1, clientBounds.Height - NavigationOverlayMargin * 2));
            if (width < 100 || height < 100) HideNavigationOverlay();
            else
            {
                navigationOverlay ??= new NavigationOverlay(DrawNavigation);
                navigationOverlay.Bounds = new Rectangle(
                    clientBounds.Right - NavigationOverlayMargin - width,
                    clientBounds.Top + NavigationOverlayMargin,
                    width,
                    height);
                if (!navigationOverlay.Visible) navigationOverlay.Show();
                navigationOverlay.Invalidate();
            }
        }
        else HideNavigationOverlay();

        if (showLootTrackerOverlay.Checked)
        {
            lootTrackerOverlay ??= new LootTrackerOverlay(lootTracker.Snapshot, CommitLootTrackerPosition);
            if (!lootTrackerOverlay.Visible)
            {
                Options options=Options.Read();
                Rectangle workingArea=Screen.FromRectangle(clientBounds).WorkingArea;
                int x=options.LootTrackerOverlayX>=0 ? options.LootTrackerOverlayX : clientBounds.Left+NavigationOverlayMargin;
                int y=options.LootTrackerOverlayY>=0 ? options.LootTrackerOverlayY : clientBounds.Top+NavigationOverlayMargin;
                x=Math.Clamp(x,workingArea.Left,Math.Max(workingArea.Left,workingArea.Right-lootTrackerOverlay.Width));
                y=Math.Clamp(y,workingArea.Top,Math.Max(workingArea.Top,workingArea.Bottom-lootTrackerOverlay.Height));
                lootTrackerOverlay.Location=new Point(x,y);
                lootTrackerOverlay.Show();
            }
            lootTrackerOverlay.Invalidate();
        }
        else HideLootTrackerOverlay();
    }

    bool GameProcessAlive()
    {
        int pid = world.Pid;
        if (pid <= 0) return false;
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    void HideNavigationOverlay()
    {
        if (navigationOverlay is { IsDisposed: false, Visible: true }) navigationOverlay.Hide();
    }

    void HideLootTrackerOverlay()
    {
        if (lootTrackerOverlay is { IsDisposed: false, Visible: true }) lootTrackerOverlay.Hide();
    }

    void CommitLootTrackerPosition(Point location)
    {
        try
        {
            var options=Options.Read();options.LootTrackerOverlayX=location.X;options.LootTrackerOverlayY=location.Y;options.Save();
        }
        catch(Exception ex){message=ex.Message;}
    }

    void DisposeNavigationOverlay()
    {
        navigationOverlay?.Dispose();navigationOverlay=null;
        lootTrackerOverlay?.Dispose();lootTrackerOverlay=null;
    }
}
