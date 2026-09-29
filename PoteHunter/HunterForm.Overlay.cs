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
        }
        catch
        {
            showNavigationOverlay.Checked = false;
            showLootTrackerOverlay.Checked = true;
            navigationOverlaySize.Value = 450;
            navigationViewRadius.Value = 150;
        }

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
        navControls.Controls.Add(resetLootTracker);
        navControls.Controls.Add(resetLootTimer);

        showNavigationOverlay.CheckedChanged += (_, _) => OverlaySettingsChanged();
        showLootTrackerOverlay.CheckedChanged += (_, _) => OverlaySettingsChanged();
        guideTreasureChests.CheckedChanged += (_, _) => OverlaySettingsChanged();
        showTreasureChestMarkers.CheckedChanged += (_, _) => OverlaySettingsChanged();
        navigationOverlaySize.ValueChanged += (_, _) => OverlaySettingsChanged();
        navigationViewRadius.ValueChanged += (_, _) => OverlaySettingsChanged();
        fitNavigationRadius.Click += (_, _) => FitNavigationRadiusToLoaded();
        resetLootTracker.Click += (_, _) => { SaveLootLog("Reset loot"); lootTracker.Reset(); message = "Loot totals reset."; UpdateNavigationOverlay(); };
        resetLootTimer.Click += (_, _) => { SaveLootLog("Reset timer"); lootTracker.ResetTimer(); message = "Loot earning timer reset."; UpdateNavigationOverlay(); };
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
