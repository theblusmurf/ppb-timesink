using System.Diagnostics;

namespace PoteHunter;

public sealed partial class HunterForm
{
    const int NavigationOverlayMargin = 24;
    readonly CheckBox showNavigationOverlay = new() { Text = "Show radar overlay", AutoSize = true };
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
    NavigationOverlay? navigationOverlay;

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
            navigationOverlaySize.Value = Math.Clamp(options.NavigationOverlaySize,
                (int)navigationOverlaySize.Minimum, (int)navigationOverlaySize.Maximum);
            navigationViewRadius.Value = Math.Clamp(options.NavigationViewRadius,
                (int)navigationViewRadius.Minimum, (int)navigationViewRadius.Maximum);
        }
        catch
        {
            showNavigationOverlay.Checked = false;
            navigationOverlaySize.Value = 450;
            navigationViewRadius.Value = 150;
        }

        var sizeLabel = new Label { Text = "Size:", AutoSize = true, Padding = new Padding(5, 5, 0, 0) };
        navControls.Controls.Add(showNavigationOverlay);
        navControls.Controls.Add(sizeLabel);
        navControls.Controls.Add(navigationOverlaySize);
        navControls.Controls.Add(new Label { Text = "View:", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
        navControls.Controls.Add(navigationViewRadius);
        navControls.Controls.Add(new Label { Text = "m", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        navControls.Controls.Add(fitNavigationRadius);

        showNavigationOverlay.CheckedChanged += (_, _) => OverlaySettingsChanged();
        navigationOverlaySize.ValueChanged += (_, _) => OverlaySettingsChanged();
        navigationViewRadius.ValueChanged += (_, _) => OverlaySettingsChanged();
        fitNavigationRadius.Click += (_, _) => FitNavigationRadiusToLoaded();
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
        options.NavigationOverlaySize = (int)navigationOverlaySize.Value;
        options.NavigationViewRadius = (int)navigationViewRadius.Value;
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
        if (IsDisposed || !showNavigationOverlay.Checked || !connected || world.Window == IntPtr.Zero ||
            !GameProcessAlive() || !NavigationOverlay.TryGetClientScreenBounds(world.Window, out var clientBounds))
        {
            HideNavigationOverlay();
            return;
        }

        IntPtr foreground = NavigationOverlay.ForegroundWindow;
        if (foreground != world.Window && foreground != Handle)
        {
            HideNavigationOverlay();
            return;
        }

        int requested = (int)navigationOverlaySize.Value;
        int width = Math.Min(requested, Math.Max(1, clientBounds.Width - NavigationOverlayMargin * 2));
        int height = Math.Min(requested, Math.Max(1, clientBounds.Height - NavigationOverlayMargin * 2));
        if (width < 100 || height < 100)
        {
            HideNavigationOverlay();
            return;
        }

        navigationOverlay ??= new NavigationOverlay(DrawNavigation);
        navigationOverlay.Bounds = new Rectangle(
            clientBounds.Right - NavigationOverlayMargin - width,
            clientBounds.Top + NavigationOverlayMargin,
            width,
            height);
        if (!navigationOverlay.Visible) navigationOverlay.Show();
        navigationOverlay.Invalidate();
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

    void DisposeNavigationOverlay()
    {
        if (navigationOverlay == null) return;
        navigationOverlay.Dispose();
        navigationOverlay = null;
    }
}
