using System.Diagnostics;

namespace PoteHunter;

public sealed partial class HunterForm
{
    const int NavigationOverlayMargin = 24;
    readonly CheckBox showNavigationOverlay = new() { Text = "Show radar overlay", AutoSize = true };
    readonly CheckBox showNavigationRoutes = new() { Text = "Show routes on radar", AutoSize = true, Checked = true };
    readonly CheckBox showRouteOverlay = new() { Text = "Show route overlay", AutoSize = true };
    readonly ComboBox lootTrackerDesign = new() { DropDownStyle=ComboBoxStyle.DropDownList,Width=175 };
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
    readonly CheckBox useAlternativeHuntRoutes = new() { Text = "Use alternatives when the saved spot is occupied", AutoSize = true, Checked = true };
    readonly ComboBox savedNavigationSlot = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };
    readonly Label savedNavigationRoutesStatus = new() { AutoSize = true, ForeColor = Color.Silver };
    readonly Button startNavigationRecording = new() { Text = "Start route · Home", AutoSize = true };
    readonly Label navigationRecordingStatus = new() { AutoSize = true, ForeColor = Color.Silver };
    readonly Button saveNavigationSpot = new() { Text = "Save current spot", AutoSize = true };
    readonly Button saveNavigationRoute = new() { Text = "Finish & save · End", AutoSize = true };
    readonly Button clearSavedNavigationRoute = new() { Text = "Clear selected", AutoSize = true };
    readonly Button clearAllSavedNavigationRoutes = new() { Text = "Clear all routes", AutoSize = true };
    readonly Button resetLootTracker = new() { Text = "Reset loot", AutoSize = true };
    readonly Button resetLootTimer = new() { Text = "Reset timer", AutoSize = true };
    NavigationOverlay? navigationOverlay;
    NavigationOverlay? routeOverlay;
    LootTrackerOverlay? lootTrackerOverlay;

    /// <summary>Adds and restores the passive radar controls in the Navigation header.</summary>
    void InitializeNavigationOverlay(FlowLayoutPanel navControls)
    {
        ArgumentNullException.ThrowIfNull(navControls);
        lootTrackerDesign.Items.AddRange(["Dungeon HUD","Compact Ribbon","Parchment Ledger"]);
        lootTrackerDesign.SelectedIndex=0;
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
            showNavigationRoutes.Checked = options.ShowNavigationRoutes;
            showRouteOverlay.Checked = options.ShowRouteOverlay;
            lootTrackerDesign.SelectedIndex=Math.Clamp(options.LootTrackerDesign,0,2);
            showLootTrackerOverlay.Checked = options.ShowLootTrackerOverlay;
            navigationOverlaySize.Value = Math.Clamp(options.NavigationOverlaySize,
                (int)navigationOverlaySize.Minimum, (int)navigationOverlaySize.Maximum);
            navigationViewRadius.Value = Math.Clamp(options.NavigationViewRadius,
                (int)navigationViewRadius.Minimum, (int)navigationViewRadius.Maximum);
            guideTreasureChests.Checked = options.GuideTreasureChests;
            showTreasureChestMarkers.Checked = options.ShowTreasureChestMarkers;
            useAlternativeHuntRoutes.Checked = options.UseAlternativeHuntRoutes;
            navigation.LoadSavedRoutes();
        }
        catch
        {
            showNavigationOverlay.Checked = false;
            showNavigationRoutes.Checked = true;
            showLootTrackerOverlay.Checked = true;
            navigationOverlaySize.Value = 450;
            navigationViewRadius.Value = 150;
            useAlternativeHuntRoutes.Checked = true;
        }

        savedNavigationSlot.Items.AddRange(["Primary hunt route", "Alternative route 1", "Alternative route 2"]);
        savedNavigationSlot.SelectedIndex = 0;
        RefreshSavedNavigationRouteStatus();

        var sizeLabel = new Label { Text = "Size:", AutoSize = true, Padding = new Padding(5, 5, 0, 0) };
        navControls.Controls.Add(showNavigationOverlay);
        navControls.Controls.Add(showNavigationRoutes);
        navControls.Controls.Add(showRouteOverlay);
        navControls.Controls.Add(showLootTrackerOverlay);
        navControls.Controls.Add(lootTrackerDesign);
        priorityHint.SetToolTip(showRouteOverlay,"Independent click-through route map at the bottom right of the game. Shows all saved paths, anchors, facing and the 10-unit start corridor; auto-fits the full routes.");
        priorityHint.SetToolTip(lootTrackerDesign,"Three game overlay designs with identical tracked totals and rates. Drag the header to reposition; your selection is saved.");
        navControls.Controls.Add(guideTreasureChests);
        navControls.Controls.Add(showTreasureChestMarkers);
        navControls.Controls.Add(sizeLabel);
        navControls.Controls.Add(navigationOverlaySize);
        navControls.Controls.Add(new Label { Text = "View:", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
        navControls.Controls.Add(navigationViewRadius);
        navControls.Controls.Add(new Label { Text = "m", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
        navControls.Controls.Add(fitNavigationRadius);
        navControls.Controls.Add(useAlternativeHuntRoutes);
        priorityHint.SetToolTip(useAlternativeHuntRoutes,"Check occupancy at startup, after revival, and during return. Record all three routes from the same revival point. Switch along saved paths; when every compatible spot is occupied, wait at the route start for 10 minutes and retry. Only loaded players can be detected.");
        navControls.Controls.Add(new Label { Text = "Save slot:", AutoSize = true, Padding = new Padding(8, 5, 0, 0) });
        navControls.Controls.Add(savedNavigationSlot);
        navControls.Controls.Add(startNavigationRecording);
        navControls.Controls.Add(navigationRecordingStatus);
        navControls.Controls.Add(saveNavigationSpot);
        navControls.Controls.Add(saveNavigationRoute);
        navControls.Controls.Add(clearSavedNavigationRoute);
        navControls.Controls.Add(clearAllSavedNavigationRoutes);
        navControls.Controls.Add(savedNavigationRoutesStatus);
        navControls.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(650,0),Text="F8 within 10 map units of a saved path: follow route to anchor, restore facing, then hunt. Selected slot preferred. See Index for all hotkeys."});
        navControls.Controls.Add(resetLootTracker);
        navControls.Controls.Add(resetLootTimer);

        showNavigationOverlay.CheckedChanged += (_, _) => OverlaySettingsChanged();
        showNavigationRoutes.CheckedChanged += (_, _) => OverlaySettingsChanged();
        showRouteOverlay.CheckedChanged += (_, _) => OverlaySettingsChanged();
        lootTrackerDesign.SelectedIndexChanged += (_, _) => OverlaySettingsChanged();
        showLootTrackerOverlay.CheckedChanged += (_, _) => OverlaySettingsChanged();
        guideTreasureChests.CheckedChanged += (_, _) => OverlaySettingsChanged();
        showTreasureChestMarkers.CheckedChanged += (_, _) => OverlaySettingsChanged();
        navigationOverlaySize.ValueChanged += (_, _) => OverlaySettingsChanged();
        navigationViewRadius.ValueChanged += (_, _) => OverlaySettingsChanged();
        fitNavigationRadius.Click += (_, _) => FitNavigationRadiusToLoaded();
        useAlternativeHuntRoutes.CheckedChanged += (_, _) => OverlaySettingsChanged();
        savedNavigationSlot.SelectedIndexChanged += (_, _) => RefreshSavedNavigationRouteStatus();
        startNavigationRecording.Click += (_, _) => StartNavigationRouteRecording();
        saveNavigationSpot.Click += (_, _) => SaveNavigationRouteFromNavigationTab(true);
        saveNavigationRoute.Click += (_, _) => SaveNavigationRouteFromNavigationTab(false,true);
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
        RefreshNavigationRecordingControls();
    }

    void StartNavigationRouteRecording()
    {
        if (busy || working) { message="Stop the hunt before recording a route manually."; return; }
        if (!connected || !navigationPosition.Finite) { message="Connect to the game before starting route recording."; return; }
        if (navigation.Recording) { message="A route is already recording. Press End to finish and save it."; return; }
        try
        {
            double heading=world.PlayerHeading();
            navigation.BeginRecording(navigationPosition);
            message=$"Route recording started at zone {navigationZone}. Walk to the destination, then press End to finish and save.";
            TraceLog.Record("manual navigation route recording started",new {Zone=navigationZone,Anchor=navigationPosition,Heading=heading});
        }
        catch(Exception ex) { message="Could not start navigation route: "+ex.Message; }
        RefreshNavigationRecordingControls();navigationCanvas.Invalidate();
    }

    void RefreshNavigationRecordingControls()
    {
        if (startNavigationRecording.IsDisposed) return;
        bool manualAvailable=connected && !busy && !working;
        startNavigationRecording.Enabled=manualAvailable && !navigation.Recording;
        saveNavigationRoute.Enabled=manualAvailable && navigation.Recording;
        saveNavigationSpot.Enabled=manualAvailable;
        navigationRecordingStatus.Text=navigation.Recording
            ? $"Recording {navigation.RecordingTrail.Count} point(s) · End saves"
            : navigation.RecordingCancelled
                ? "Recording cancelled; start again"
                : "Ready · Home starts";
        navigationRecordingStatus.ForeColor=navigation.RecordingCancelled ? Color.Orange : Color.Silver;
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
        options.ShowNavigationRoutes = showNavigationRoutes.Checked;
        options.ShowRouteOverlay = showRouteOverlay.Checked;
        options.LootTrackerDesign = Math.Clamp(lootTrackerDesign.SelectedIndex,0,2);
        options.ShowLootTrackerOverlay = showLootTrackerOverlay.Checked;
        options.NavigationOverlaySize = (int)navigationOverlaySize.Value;
        options.NavigationViewRadius = (int)navigationViewRadius.Value;
        options.GuideTreasureChests = guideTreasureChests.Checked;
        options.ShowTreasureChestMarkers = showTreasureChestMarkers.Checked;
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

    void SaveNavigationRouteFromNavigationTab(bool spotOnly,bool finishRecording=false)
    {
        try
        {
            if(busy || working) { message="Stop the hunt before saving a route manually."; return; }
            if(!connected || !navigationPosition.Finite) { message="Connect to the game before saving a route."; return; }
            if(finishRecording && !navigation.Recording)
            {
                message=navigation.RecordingCancelled ? "Route recording was cancelled after an unsafe movement gap; start again." : "Start route recording before finishing a route.";
                RefreshNavigationRecordingControls();
                return;
            }
            if(finishRecording && navigation.RecordingCancelled)
            {
                message="Route recording was cancelled after an unsafe movement gap; start again.";
                navigation.EndRecording();RefreshNavigationRecordingControls();return;
            }
            int slot=SelectedSavedNavigationSlot();
            Vec anchor=navigationPosition;
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
            routeOptions.RevivalDelaySeconds,routeOptions.FarmOnArrival,routeOptions.AutoRepairAfterDeath);
            bool saved=spotOnly
                ? navigation.SaveCurrentSpot(navigationZone,anchor,heading,slot,profile:profile)
                : navigation.SaveCurrentRoute(navigationZone,anchor,heading,slot,profile:profile);
            if(saved)
            {
                if(finishRecording)
                {
                    navigation.EndRecording();
                    TraceLog.Record("manual navigation route recording finished",new {Slot=slot,Zone=navigationZone,Anchor=anchor,PointCount=navigation.GetSavedRoute(slot)?.Points.Length ?? 0});
                }
                message=$"Saved {SavedNavigationSlotName(slot)} {(spotOnly?"spot":"route")} ({navigation.GetSavedRoute(slot)?.Points.Length ?? 0} points, {profile.Character} / radius {profile.HuntRadius:0.#}).";
            }
            else message="Save the current position or walk a route first; no valid navigation point was found.";
            RefreshSavedNavigationRouteStatus();
            RefreshNavigationRecordingControls();
            navigationCanvas.Invalidate();
        }
        catch(Exception ex){message="Could not save navigation route: "+ex.Message;RefreshNavigationRecordingControls();}
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
            HideRouteOverlay();
            HideLootTrackerOverlay();
            return;
        }

        IntPtr foreground = NavigationOverlay.ForegroundWindow;
        if (foreground != world.Window && foreground != Handle)
        {
            HideNavigationOverlay();
            HideRouteOverlay();
            HideLootTrackerOverlay();
            return;
        }

        if (showNavigationOverlay.Checked)
        {
            int requested = (int)navigationOverlaySize.Value;
            int width = Math.Min(requested, Math.Max(1, clientBounds.Width - NavigationOverlayMargin * 2));
            int height = Math.Min(requested, Math.Max(1, clientBounds.Height - NavigationOverlayMargin * 2));
            if(showRouteOverlay.Checked)height=Math.Min(height,(clientBounds.Height-NavigationOverlayMargin*3)/2);
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

        if(showRouteOverlay.Checked)
        {
            int width=Math.Min((int)navigationOverlaySize.Value,clientBounds.Width-NavigationOverlayMargin*2);
            int height=Math.Min(width,clientBounds.Height-NavigationOverlayMargin*2);
            if(showNavigationOverlay.Checked)height=Math.Min(height,(clientBounds.Height-NavigationOverlayMargin*3)/2);
            if(width<100 || height<100)HideRouteOverlay();
            else
            {
                routeOverlay??=new NavigationOverlay(DrawRouteOverlay,"SAVED ROUTES  ·  10m start corridor","Primary: violet  ·  Alt 1: gold  ·  Alt 2: coral  ·  You: white");
                routeOverlay.Bounds=new(clientBounds.Right-NavigationOverlayMargin-width,clientBounds.Bottom-NavigationOverlayMargin-height,width,height);
                if(!routeOverlay.Visible)routeOverlay.Show();
                routeOverlay.Invalidate();
            }
        }
        else HideRouteOverlay();

        if (showLootTrackerOverlay.Checked)
        {
            lootTrackerOverlay ??= new LootTrackerOverlay(lootTracker.Snapshot, CommitLootTrackerPosition);
            lootTrackerOverlay.SetDesign(lootTrackerDesign.SelectedIndex);
            if(lootTrackerOverlay.Visible)
            {
                Rectangle area=Screen.FromRectangle(clientBounds).WorkingArea;
                lootTrackerOverlay.Location=new(Math.Clamp(lootTrackerOverlay.Left,area.Left,Math.Max(area.Left,area.Right-lootTrackerOverlay.Width)),
                    Math.Clamp(lootTrackerOverlay.Top,area.Top,Math.Max(area.Top,area.Bottom-lootTrackerOverlay.Height)));
            }
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
    void HideRouteOverlay()
    {
        if(routeOverlay is {IsDisposed:false,Visible:true})routeOverlay.Hide();
    }

    void DrawRouteOverlay(Graphics g,Size size)
        =>DrawSavedRouteOverlay(g,size,navigation.SavedRoutesForZone(navigationZone).ToArray(),navigationPosition);

    static void DrawSavedRouteOverlay(Graphics g,Size size,(int Slot,SavedNavigationRoute Route)[] routes,Vec position)
    {
        g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.FromArgb(19,25,30));
        if(routes.Length==0){using var emptyFont=new Font("Segoe UI",9);g.DrawString("No saved routes in this zone.\nHome: start · End: save at anchor",emptyFont,Brushes.Wheat,new PointF(12,15));return;}
        var points=routes.SelectMany(r=>r.Route.Points.Append(r.Route.Anchor)).Append(position).Where(p=>p.Finite).ToArray();
        double minX=points.Min(p=>p.X)-12,maxX=points.Max(p=>p.X)+12,minY=points.Min(p=>p.Y)-12,maxY=points.Max(p=>p.Y)+12;
        float scale=(float)Math.Min((size.Width-32)/Math.Max(1,maxX-minX),(size.Height-32)/Math.Max(1,maxY-minY));
        float left=(size.Width-(float)(maxX-minX)*scale)/2,top=(size.Height-(float)(maxY-minY)*scale)/2;
        PointF Project(Vec p)=>new(left+(float)(p.X-minX)*scale,top+(float)(maxY-p.Y)*scale);
        Color[] colors=[Color.MediumPurple,Color.Gold,Color.Coral];
        using var labelFont=new Font("Segoe UI",8f,FontStyle.Bold);
        foreach(var (slot,route) in routes)
        {
            var projected=route.Points.Select(Project).ToArray();
            using var pen=new Pen(colors[slot],2);
            if(projected.Length>1)
            {
                using var corridor=new Pen(Color.FromArgb(35,colors[slot]),(float)(RecoveryTravel.StartupRadius*2)*scale){StartCap=System.Drawing.Drawing2D.LineCap.Round,EndCap=System.Drawing.Drawing2D.LineCap.Round,LineJoin=System.Drawing.Drawing2D.LineJoin.Round};
                if(RecoveryTravel.Recorded(route))g.DrawLines(corridor,projected);
                g.DrawLines(pen,projected);
                var start=Project(route.RevivalOrigin);g.DrawRectangle(pen,start.X-3,start.Y-3,6,6);
            }
            var anchor=Project(route.Anchor);using var brush=new SolidBrush(colors[slot]);
            g.FillEllipse(brush,anchor.X-4,anchor.Y-4,8,8);
            Vec facing=Movement.FromClientHeading(route.Heading);g.DrawLine(pen,anchor,new PointF(anchor.X+(float)facing.X*14,anchor.Y-(float)facing.Y*14));
            g.DrawString(slot==0?"Primary":$"Alt {slot}",labelFont,brush,new PointF(Math.Clamp(anchor.X+6,2,size.Width-66),Math.Clamp(anchor.Y-15,2,size.Height-20)));
        }
        var self=Project(position);g.FillEllipse(Brushes.White,self.X-4,self.Y-4,8,8);
    }

    void CheckOverlayDesigns()
    {
        showRouteOverlay.Checked=true;lootTrackerDesign.SelectedIndex=2;
        CurrentOptions().Save();var restored=Options.Read();
        if(!restored.ShowRouteOverlay || restored.LootTrackerDesign!=2)throw new Exception("Overlay controls did not persist.");
        showRouteOverlay.Checked=false;CurrentOptions().Save();
        if(Options.Read().ShowRouteOverlay || Options.Read().LootTrackerDesign!=2)throw new Exception("Route visibility changed the loot design.");
        string[] names=["Silvin","Mithril","Iternium","Fehu","Gold","Gems"];
        long[] totals=[34,21,8,12,4613,19];
        var sample=new LootTrackerSnapshot(8,0,[new("Mimic",12,38,[]),new("Tribal",9,24,[]),new("Pulkhan",7,19,[]),new("Tower",5,12,[])],
            [new("Mimic","Gold (215)",new(0,0),DateTime.UnixEpoch),new("Tribal","Emerald",new(0,0),DateTime.UnixEpoch)],
            names.Select((name,i)=>new LootTrackerItemSummary(name,totals[i])).ToArray(),DateTime.UnixEpoch,TimeSpan.FromMinutes(32),DateTime.UnixEpoch,TimeSpan.FromMinutes(30),
            names.Select((name,i)=>new LootTrackerRateSummary(name,totals[i]*2)).ToArray());
        using var gallery=new Bitmap(790,580);using var galleryGraphics=Graphics.FromImage(gallery);galleryGraphics.Clear(Color.FromArgb(27,25,23));
        galleryGraphics.DrawString("Loot overlay choices · preview data",Font,Brushes.Wheat,new PointF(15,5));
        using(var preview=new LootTrackerOverlay(()=>sample))
        {
            Point[] positions=[new(15,32),new(75,430),new(415,32)];
            for(int style=0;style<3;style++)
            {
                preview.SetDesign(style);using var bitmap=new Bitmap(preview.Width,preview.Height);
                preview.DrawToBitmap(bitmap,new Rectangle(Point.Empty,preview.Size));bitmap.Save(Path.Combine(AppContext.BaseDirectory,$"loot-overlay-design-{style}.png"));
                galleryGraphics.DrawImageUnscaled(bitmap,positions[style]);
            }
            preview.SetDesign(99);if(preview.Design!=2)throw new Exception("Invalid loot design was not bounded.");
        }
        gallery.Save(Path.Combine(AppContext.BaseDirectory,"loot-overlay-choices.png"));
        var routes=new (int Slot,SavedNavigationRoute Route)[3];
        for(int i=0;i<3;i++)
        {
            var points=Enumerable.Range(0,21).Select(n=>new Vec((i-1)*n*1.7,n*3)).Reverse().ToArray();
            routes[i]=(i,new SavedNavigationRoute(8,points[0],i,points,DateTime.UnixEpoch));
        }
        using var routePreview=new NavigationOverlay((graphics,size)=>DrawSavedRouteOverlay(graphics,size,routes,new(0,10)),"SAVED ROUTES · 10m start corridor","Primary: violet · Alt 1: gold · Alt 2: coral · You: white"){Size=new(450,450)};
        if(!routePreview.HasPassiveWindowStyles)throw new Exception("Route overlay can intercept input or activate the game.");
        using var routeBitmap=new Bitmap(450,450);routePreview.DrawToBitmap(routeBitmap,new Rectangle(0,0,450,450));routeBitmap.Save(Path.Combine(AppContext.BaseDirectory,"route-overlay-preview.png"));
        lootTrackerDesign.SelectedIndex=0;
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
        routeOverlay?.Dispose();routeOverlay=null;
        lootTrackerOverlay?.Dispose();lootTrackerOverlay=null;
    }
}
