namespace PoteHunter;

public sealed partial class HunterForm
{
    readonly Navigation3DView navigation3DView=new(){Dock=DockStyle.Fill,Name="navigation3DView"};
    readonly ComboBox navigationMapChoice=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=150,Name="navigationMapChoice"};
    readonly Button navigation3DButton=new(){Text="3D",AutoSize=true,Name="navigation3DButton"};
    readonly Button navigation2DButton=new(){Text="2D",AutoSize=true,Name="navigation2DButton"};
    readonly Button navigationTopButton=new(){Text="Top view",AutoSize=true};
    readonly Button navigationFitButton=new(){Text="Fit map",AutoSize=true};
    readonly Button navigationRouteFitButton=new(){Text="Fit routes",AutoSize=true};
    readonly Button navigationAnchorButton=new(){Text="View anchor",AutoSize=true};
    readonly Button navigationPlayerButton=new(){Text="Center player",AutoSize=true};
    readonly CheckBox navigation3DMap=new(){Text="Game map",Checked=true,AutoSize=true};
    readonly CheckBox navigation3DTerrain=new(){Text="Terrain",Checked=true,AutoSize=true};
    readonly CheckBox navigation3DObjects=new(){Text="Buildings & objects",Checked=true,AutoSize=true};
    readonly CheckBox navigation3DRoutes=new(){Text="Saved routes",Checked=true,AutoSize=true};
    readonly CheckBox navigation3DAnchors=new(){Text="Anchor markers",Checked=true,AutoSize=true};
    readonly TrackBar navigationMapOpacity=new(){Minimum=0,Maximum=100,Value=80,TickStyle=TickStyle.None,SmallChange=5,LargeChange=10,Width=226,AccessibleName="Game map opacity"};
    readonly Label navigationMapOpacityLabel=new(){Text="Map opacity · 80%",AutoSize=true};
    readonly Label navigation3DStatus=new(){Dock=DockStyle.Fill,AutoEllipsis=true,Text="Select a map to view local terrain.",Padding=new(5),ForeColor=ImperialTheme.Muted};
    readonly Label navigation3DTarget=new(){AutoSize=true,ForeColor=ImperialTheme.Gold};
    readonly Label navigation3DHint=new(){AutoSize=true,MaximumSize=new(240,0),Text="Drag to orbit · Right-drag to pan · Scroll to zoom.\n\nRoutes are projected onto terrain for viewing. Bridge heights and walkability are not confirmed.",ForeColor=ImperialTheme.Muted};
    readonly Dictionary<int,MapScene3D> navigation3DCache=new();
    MapScene3D? navigation3DScene;
    CancellationTokenSource? navigation3DLoad;
    int navigation3DRequestedZone=-1,navigation3DLoadGeneration;
    bool navigation3DInitialized,navigation3DPreferred=true,navigation3DFallback,navigation3DUpdating;
    double navigation3DPlayerHeight,navigation3DPlayerHeading;
    long navigation3DPlayerSeen;
    string navigation3DRouteSignature="";
    sealed record NavigationMapChoice(int Zone,string Text){public override string ToString()=>Text;}

    void InitializeNavigation3DPage(TableLayoutPanel layout,FlowLayoutPanel legacy)
    {
        navigation3DUpdating=true;
        try
        {
            var o=Options.Read();navigation3DPreferred=o.Navigation3D;
            navigation3DMap.Checked=o.Navigation3DMap;navigation3DTerrain.Checked=o.Navigation3DTerrain;
            navigation3DObjects.Checked=o.Navigation3DObjects;navigation3DRoutes.Checked=o.Navigation3DRoutes;
            navigation3DAnchors.Checked=o.Navigation3DAnchors;navigationMapOpacity.Value=o.Navigation3DMapOpacity;
        }
        catch(Exception ex) when(ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException){ }
        InitializeOverlay3D();
        InitializeSentinelRadar();
        layout.SuspendLayout();layout.Controls.Clear();layout.RowCount=2;layout.ColumnCount=2;layout.RowStyles.Clear();layout.ColumnStyles.Clear();
        layout.ColumnStyles.Add(new(SizeType.Percent,100));layout.ColumnStyles.Add(new(SizeType.Absolute,284));
        layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.Absolute,54));
        layout.BackColor=ImperialTheme.Window;layout.Padding=new(0);layout.Margin=Padding.Empty;
        var mapCard=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,BackColor=ImperialTheme.Surface,Margin=new(0,0,12,0)};
        mapCard.RowStyles.Add(new(SizeType.AutoSize));mapCard.RowStyles.Add(new(SizeType.Percent,100));mapCard.ColumnStyles.Add(new(SizeType.Percent,100));
        var toolbar=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Padding=new(6),Margin=Padding.Empty,BackColor=ImperialTheme.Surface};
        navigationMapChoice.Items.Add(new NavigationMapChoice(0,"Current map"));
        foreach(int zone in new[]{1,2,3,4,5,6,8,9,12,15,16,17,18})navigationMapChoice.Items.Add(new NavigationMapChoice(zone,"Zone "+zone));
        navigationMapChoice.SelectedIndex=0;
        toolbar.Controls.AddRange([navigationMapChoice,navigation3DButton,navigation2DButton,navigationTopButton,navigationFitButton,navigationRouteFitButton,navigationAnchorButton,navigationPlayerButton]);
        var viewHost=new Panel{Dock=DockStyle.Fill,BackColor=ImperialTheme.Window,Margin=Padding.Empty,Name="navigationViewHost"};
        navigationCanvas.Dock=DockStyle.Fill;viewHost.Controls.Add(navigationCanvas);viewHost.Controls.Add(navigation3DView);
        mapCard.Controls.Add(toolbar,0,0);mapCard.Controls.Add(viewHost,0,1);layout.Controls.Add(mapCard,0,0);
        var side=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,BackColor=ImperialTheme.Window,Margin=Padding.Empty,Name="navigationLayerSidebar"};
        side.Controls.Add(navigationPlayerRecognitionLabel);
        CollapsibleSection Section(string name,string title,bool expanded,params Control[] controls)
        {
            var section=new CollapsibleSection(name,title,"compass",expanded){Width=264,Summary=title=="Map layers"?"Artwork, terrain and route visibility":""};
            section.Content.Padding=new(10,6,10,10);
            if(string.IsNullOrEmpty(section.Summary))section.RowStyles[0].Height=42;
            foreach(var control in controls)
            {
                control.Margin=new(0,3,0,4);control.Dock=DockStyle.Top;control.MaximumSize=new(240,0);
                Control row=control;
                if(control is ComboBox combo)
                {
                    // Native owner-drawn combos grow after theming, while an
                    // AutoSize table cell can keep the old preferred height.
                    // Let a plain row reserve the actual control height.
                    var host=new Panel{Name="navigationComboRow"+combo.Name,Dock=DockStyle.Top,
                        AutoSize=false,Height=combo.PreferredHeight,Margin=control.Margin,MaximumSize=new(240,0)};
                    combo.Margin=Padding.Empty;host.Controls.Add(combo);
                    void FitHeight()
                    {
                        int height=Math.Max(combo.Height,combo.PreferredHeight);
                        if(host.Height!=height)host.Height=height;
                    }
                    combo.SizeChanged+=(_,_)=>FitHeight();combo.FontChanged+=(_,_)=>FitHeight();
                    host.Layout+=(_,_)=>FitHeight();FitHeight();row=host;
                }
                section.Content.RowStyles.Add(new(SizeType.AutoSize));section.Content.Controls.Add(row,0,section.Content.RowCount++);
            }
            side.Controls.Add(section);return section;
        }
        Section("navigation3DLayers","Map layers",true,navigation3DMap,navigationMapOpacityLabel,navigationMapOpacity,navigation3DTerrain,navigation3DObjects,navigation3DRoutes,navigation3DAnchors);
        savedNavigationSlot.Name="savedNavigationSlot";savedNavigationSlot.Width=235;
        navigationRecordingStatus.MaximumSize=new(235,0);savedNavigationRoutesStatus.MaximumSize=new(235,0);
        useAlternativeHuntRoutes.Text="Use alternative routes";
        var alternativesHelp=new Label{Name="navigationAlternativesHelp",AutoSize=true,
            Text="When the saved spot is occupied.",ForeColor=ImperialTheme.Muted,Font=new Font("Segoe UI",8.5f)};
        Section("navigationSavedRoutes","Saved routes",true,navigation3DTarget,savedNavigationSlot,startNavigationRecording,saveNavigationRoute,navigationRecordingStatus,useAlternativeHuntRoutes,alternativesHelp,savedNavigationRoutesStatus);
        AddTeleporterNavigationSection(side);
        Section("navigationRouteManagement","Route management",false,saveNavigationSpot,assignUnassignedNavigationRoutes,clearSavedNavigationRoute,clearAllSavedNavigationRoutes);
        Section("navigationOverlayOptions","Overlay options",false,showNavigationOverlay,radar3D,showNavigationRoutes,showRouteOverlay,routes3D,overlay3DTopView,showTreasureChestMarkers,overlay3DStatus,
            new Label{AutoSize=true,Text="Overlay size"},navigationOverlaySize,new Label{AutoSize=true,Text="MiniMap radius (map units)"},navigationViewRadius,fitNavigationRadius,fitGameMap,followMapPlayer);
        Section("navigationLootPresentation","Loot overlay",false,showLootTrackerOverlay,lootTrackerDesign,lootTrackerScaleLabel,lootTrackerScale,lootTrackerBackgroundLabel,lootTrackerBackgroundOpacity,resetLootTracker,resetLootTimer);
        Section("navigationSentinelRadar","Sentinel MiniMap",false,showSentinelRadar,sentinelSoundEnabled,
            sentinelRangeLabel,sentinelRange,
            new Label{AutoSize=true,MaximumSize=new(240,0),Text="Auto display in Zone 8 · opposing factions only\nPaired sonar on new enemy entries"},
            new Label{AutoSize=true,Text="Sound volume (%)"},sentinelVolume,resetSentinelPosition);
        Section("navigationRoutingOptions","Routing options",false,automaticRouting,guideTreasureChests,clearNavigation);
        side.Controls.Add(navigation3DHint);layout.Controls.Add(side,1,0);
        layout.Controls.Add(navigation3DStatus,0,1);layout.SetColumnSpan(navigation3DStatus,2);
        navigationLabel.Visible=false;legacy.Dispose();layout.ResumeLayout(true);
        foreach(var button in toolbar.Controls.OfType<Button>())CrownfireControls.Button(button);
        foreach(var check in new[]{navigation3DMap,navigation3DTerrain,navigation3DObjects,navigation3DRoutes,navigation3DAnchors})check.CheckedChanged+=(_,_)=>Navigation3DPresentationChanged();
        navigationMapOpacity.ValueChanged+=(_,_)=>Navigation3DPresentationChanged();
        navigation3DButton.Click+=(_,_)=>{navigation3DPreferred=true;navigation3DFallback=false;Navigation3DPresentationChanged();_ = EnsureNavigation3DLoaded(true);};
        navigation2DButton.Click+=(_,_)=>{navigation3DPreferred=false;navigationMapChoice.SelectedIndex=0;Navigation3DPresentationChanged();};
        navigationMapChoice.SelectedIndexChanged+=(_,_)=>{navigation3DRequestedZone=-1;navigation3DFallback=false;_ = EnsureNavigation3DLoaded(true);};
        navigationTopButton.Click+=(_,_)=>{navigation3DView.TopView=!navigation3DView.TopView;navigationTopButton.Text=navigation3DView.TopView?"Orbit view":"Top view";};
        navigationFitButton.Click+=(_,_)=>{if(Navigation3DVisible)navigation3DView.FitMap();else FitLocalGameMap();};
        navigationRouteFitButton.Click+=(_,_)=>navigation3DView.FitRoutes();
        navigationAnchorButton.Click+=(_,_)=>navigation3DView.FocusAnchor(SelectedSavedNavigationSlot());
        navigationPlayerButton.Click+=(_,_)=>{if(Navigation3DVisible&&Navigation3DHasPlayer)navigation3DView.CenterOn(navigationPosition,navigation3DPlayerHeight);};
        savedNavigationSlot.SelectedIndexChanged+=(_,_)=>RefreshNavigation3DState();
        navigation3DView.StatusChanged+=text=>
        {
            if(IsDisposed)return;navigation3DStatus.Text=text;
            if(navigation3DView.Failed){navigation3DFallback=true;UpdateNavigation3DVisibility();}
        };
        navigationPage.VisibleChanged+=(_,_)=>{if(navigationPage.Visible){_ = EnsureNavigation3DLoaded();RefreshNavigation3DState();}};
        navigationCanvas.Invalidated+=(_,_)=>{if(navigationPage.Visible)RefreshNavigation3DRoutes();};
        FormClosed+=(_,_)=>DisposeNavigation3D();
        navigation3DInitialized=true;navigation3DUpdating=false;ApplyNavigation3DLayers();UpdateNavigation3DVisibility();
    }

    int Navigation3DZone=>navigationMapChoice.SelectedItem is NavigationMapChoice {Zone:>0} choice?choice.Zone:
        connected&&navigationZone>0?navigationZone:navigation.GetSavedRoute(SelectedSavedNavigationSlot())?.Zone??8;
    bool Navigation3DVisible=>navigation3DPreferred&&!navigation3DFallback&&!navigation3DView.Failed;
    bool Navigation3DHasPlayer=>connected&&navigation3DScene?.Zone==navigationZone&&Environment.TickCount64-navigation3DPlayerSeen<3000;
    void UpdateNavigation3DVisibility()
    {
        if(!navigation3DInitialized)return;
        navigation3DView.Visible=Navigation3DVisible;navigationCanvas.Visible=!Navigation3DVisible;
        if(Navigation3DVisible)navigation3DView.BringToFront();else navigationCanvas.BringToFront();
        // Asset fallback can recover when the user chooses a supported map.
        navigationMapChoice.Enabled=navigation3DPreferred&&!navigation3DView.Failed;
        navigation3DButton.BackColor=Navigation3DVisible?Color.FromArgb(52,43,30):ImperialTheme.Raised;
        navigation2DButton.BackColor=Navigation3DVisible?ImperialTheme.Raised:Color.FromArgb(52,43,30);
        navigation3DButton.ForeColor=Navigation3DVisible?ImperialTheme.Gold:ImperialTheme.Text;
        navigation2DButton.ForeColor=Navigation3DVisible?ImperialTheme.Text:ImperialTheme.Gold;
        bool available=Navigation3DVisible&&navigation3DScene!=null&&!navigation3DView.Failed;
        navigationTopButton.Enabled=available;navigationRouteFitButton.Enabled=available&&navigation.SavedRoutesForZone(Navigation3DZone).Any();
        navigationAnchorButton.Enabled=available&&navigation.GetSavedRoute(SelectedSavedNavigationSlot())?.Zone==Navigation3DZone;
        navigationPlayerButton.Enabled=available&&Navigation3DHasPlayer;
        if(!Navigation3DVisible&&!navigation3DFallback)navigation3DStatus.Text=navigationLabel.Text;
    }
    void ApplyNavigation3DLayers()
    {
        navigation3DView.ShowMapArtwork=navigation3DMap.Checked;navigation3DView.ShowTerrain=navigation3DTerrain.Checked;
        navigation3DView.ShowObjects=navigation3DObjects.Checked;navigation3DView.ShowRoutes=navigation3DRoutes.Checked;
        navigation3DView.ShowAnchors=navigation3DAnchors.Checked;navigation3DView.MapOpacity=navigationMapOpacity.Value;
        navigationMapOpacityLabel.Text=$"Map opacity · {navigationMapOpacity.Value}%";
    }
    Options WithNavigation3DSettings(Options o)
    {
        if(!navigation3DInitialized)return o;
        o.Navigation3D=navigation3DPreferred;o.Navigation3DMap=navigation3DMap.Checked;o.Navigation3DTerrain=navigation3DTerrain.Checked;
        o.Navigation3DObjects=navigation3DObjects.Checked;o.Navigation3DRoutes=navigation3DRoutes.Checked;
        o.Navigation3DAnchors=navigation3DAnchors.Checked;o.Navigation3DMapOpacity=navigationMapOpacity.Value;return WithOverlay3DSettings(o);
    }
    void Navigation3DPresentationChanged()
    {
        if(navigation3DUpdating||!navigation3DInitialized)return;ApplyNavigation3DLayers();UpdateNavigation3DVisibility();
        try{WithNavigation3DSettings(Options.Read()).Save();}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException){navigation3DStatus.Text="View settings could not be saved: "+ex.Message;}
    }
    async Task EnsureNavigation3DLoaded(bool force=false)
    {
        // Offline UI fixtures never read the installed client or local game models.
        if(offlinePreviewMode||!navigation3DInitialized||IsDisposed||!navigationPage.Visible||!navigation3DPreferred)return;
        int zone=Navigation3DZone;if(!force&&navigation3DRequestedZone==zone)return;
        navigation3DRequestedZone=zone;int generation=++navigation3DLoadGeneration;navigation3DLoad?.Cancel();navigation3DLoad?.Dispose();
        var cancel=navigation3DLoad=new();var token=cancel.Token;
        navigation3DFallback=false;navigation3DStatus.Text=$"Loading Zone {zone} from your game installation…";
        // Never leave old-zone geometry or live markers visible under a new-zone label.
        navigation3DScene=null;navigation3DView.SetScene(null);navigation3DView.SetMapArtwork(null,null);navigation3DView.SetMarkers([]);UpdateNavigation3DVisibility();
        navigation3DCache.TryGetValue(zone,out var cached);
        try
        {
            var loaded=await Task.Run(()=>
            {
                if(!GameMapLayout.ClientSupported(PoteMemoryProbe.Program.ClientPath))throw new InvalidDataException("The client build has not been verified for local 3D maps.");
                var scene=cached??MapSceneReader.Load(Path.GetDirectoryName(PoteMemoryProbe.Program.ClientPath)!,zone,token);
                token.ThrowIfCancellationRequested();Bitmap? artwork=null;GameMapLayout.Extent? extent=null;
                using var maps=new ZoneMapBackground();
                if(maps.TryGet(zone,out var image,out var b)){artwork=new Bitmap(image);extent=new(b.MinX,b.MinY,b.MaxX,b.MaxY);}
                return(Scene:scene,Artwork:artwork,Extent:extent);
            },token);
            if(IsDisposed||token.IsCancellationRequested||generation!=navigation3DLoadGeneration){loaded.Artwork?.Dispose();return;}
            if(navigation3DCache.Count>=2&&!navigation3DCache.ContainsKey(zone))navigation3DCache.Remove(navigation3DCache.Keys.First());
            navigation3DCache[zone]=loaded.Scene;navigation3DScene=loaded.Scene;
            navigation3DView.SetScene(loaded.Scene);navigation3DView.SetMapArtwork(loaded.Artwork,loaded.Extent);
            navigation3DRouteSignature="";RefreshNavigation3DRoutes();RefreshNavigation3DState();ApplyNavigation3DLayers();navigation3DView.FitMap();
            navigation3DStatus.Text=navigation3DView.Failed?navigation3DView.Status+" · Showing existing 2D view.":
                $"Zone {zone} · {loaded.Scene.Objects.Length:N0} placed objects · {loaded.Scene.Status}"+
                (loaded.Artwork==null?" · Map artwork unavailable":" · Drag to orbit; right-drag to pan; scroll to zoom");
            UpdateNavigation3DVisibility();
        }
        catch(OperationCanceledException){ }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or OverflowException)
        {
            if(IsDisposed||generation!=navigation3DLoadGeneration)return;
            navigation3DFallback=true;navigation3DStatus.Text="3D unavailable: "+ex.Message+" · Showing existing 2D view.";UpdateNavigation3DVisibility();
        }
    }
    void RefreshNavigation3DRoutes()
    {
        if(!navigation3DInitialized||navigation3DScene==null)return;
        var routes=navigation.SavedRoutesForZone(navigation3DScene.Zone).Select(r=>new MapRoute3D(r.Slot,r.Route)).ToArray();
        string signature=navigation.RouteTargetKey+"|"+SelectedSavedNavigationSlot()+"|"+string.Join(";",routes.Select(r=>$"{r.Slot}:{r.Route.SavedUtc.Ticks}:{r.Route.Points.Length}"));
        if(signature!=navigation3DRouteSignature){navigation3DRouteSignature=signature;navigation3DView.SelectedSlot=SelectedSavedNavigationSlot();navigation3DView.SetRoutes(routes);}
        UpdateNavigation3DVisibility();
    }
    void RefreshNavigation3DState()
    {
        if(!navigation3DInitialized||IsDisposed||!navigationPage.Visible)return;
        UpdatePlayerRecognitionLabel();
        _ = EnsureNavigation3DLoaded();RefreshNavigation3DRoutes();
        if(Navigation3DHasPlayer)
        {
            var markers=new List<MapMarker3D>{new("You",navigationPosition,navigation3DPlayerHeight,Color.White,navigation3DPlayerHeading)};
            markers.AddRange(entities.Where(e=>e.Position.Finite&&double.IsFinite(e.Height)&&e.Id!=guardSelfId&&!latestHealth.GetValueOrDefault(e.Id).Dead&&(e.Monster||showTreasureChestMarkers.Checked&&Targeting.IsChest(e)))
                .OrderBy(e=>(e.Position-navigationPosition).Length).Take(128).Select(e=>new MapMarker3D(e.Name,e.Position,e.Height,Targeting.IsChest(e)?ImperialTheme.Gold:Color.FromArgb(222,131,115))));
            navigation3DView.SetMarkers(markers.Concat(Player3DMarkers()).ToArray());
        }
        else navigation3DView.SetMarkers([]);
        UpdateNavigation3DVisibility();
    }
    void DisposeNavigation3D()
    {
        ++navigation3DLoadGeneration;navigation3DLoad?.Cancel();navigation3DLoad?.Dispose();navigation3DLoad=null;navigation3DCache.Clear();
    }
}
