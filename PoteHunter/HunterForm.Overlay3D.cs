namespace PoteHunter;

public sealed partial class HunterForm
{
    readonly CheckBox radar3D=new(){Text="3D radar",AutoSize=true,Checked=true};
    readonly CheckBox routes3D=new(){Text="3D route overlay",AutoSize=true,Checked=true};
    readonly CheckBox overlay3DTopView=new(){Text="Overlays: north-up top view",AutoSize=true};
    readonly Label overlay3DStatus=new(){AutoSize=true,MaximumSize=new(240,0),ForeColor=ImperialTheme.Muted,Text="3D overlays use the current game map."};
    NavigationOverlay3DRenderer? navigationOverlay3D,routeOverlay3D;
    MapScene3D? overlay3DScene;
    CancellationTokenSource? overlay3DLoad;
    int overlay3DRequestedZone=-1,overlay3DLoadGeneration;
    bool overlay3DInitialized,overlay3DUpdating;
    long radar3DFrameTime,route3DFrameTime;
    string overlay3DProblem="";

    void InitializeOverlay3D()
    {
        overlay3DUpdating=true;
        try
        {
            var o=Options.Read();radar3D.Checked=o.Radar3D;routes3D.Checked=o.RouteOverlay3D;overlay3DTopView.Checked=o.Overlay3DTopView;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException){ }
        foreach(var check in new[]{radar3D,routes3D,overlay3DTopView})check.CheckedChanged+=(_,_)=>Overlay3DPresentationChanged();
        priorityHint.SetToolTip(radar3D,"Show recovered local terrain and buildings in the passive radar. Unavailable 3D falls back to 2D. Radar radius controls zoom; north stays fixed.");
        priorityHint.SetToolTip(routes3D,"Show the selected target's current-zone saved routes, start corridor and anchor area over 3D terrain. Does not change route travel.");
        priorityHint.SetToolTip(overlay3DTopView,"Use a north-up overhead camera instead of the tilted 3D camera. Change display controls here; overlays stay click-through.");
        overlay3DInitialized=true;overlay3DUpdating=false;
    }
    Options WithOverlay3DSettings(Options o)
    {
        if(!overlay3DInitialized)return o;
        o.Radar3D=radar3D.Checked;o.RouteOverlay3D=routes3D.Checked;o.Overlay3DTopView=overlay3DTopView.Checked;return o;
    }
    void Overlay3DPresentationChanged()
    {
        if(overlay3DUpdating||!overlay3DInitialized)return;
        // Only presentation fields are persisted, including during hunting.
        try{WithOverlay3DSettings(Options.Read()).Save();}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException){overlay3DStatus.Text="Overlay settings could not be saved: "+ex.Message;}
        ResetOverlay3DZone();UpdateNavigationOverlay();
    }
    bool Overlay3DReady=>overlay3DScene?.Zone==navigationZone;
    bool Overlay3DHasPlayer=>connected&&Overlay3DReady&&navigationPosition.Finite&&double.IsFinite(navigation3DPlayerHeight)&&
        Environment.TickCount64-navigation3DPlayerSeen is >=0 and <3000;
    string Overlay3DMode(bool enabled,NavigationOverlay3DRenderer? renderer)=>!enabled?"2D":Overlay3DReady&&renderer?.Frame!=null&&!renderer.Failed?"3D":
        overlay3DLoad!=null&&!overlay3DLoad.IsCancellationRequested&&overlay3DScene==null&&overlay3DProblem.Length==0?"2D · loading 3D":"2D · 3D unavailable";
    static Size OverlayMapSize(Size outer)=>new(outer.Width,Math.Max(0,outer.Height-46));

    async Task EnsureOverlay3DLoaded()
    {
        if(offlinePreviewMode||IsDisposed||!connected||!overlay3DInitialized||navigationZone<=0||
            !(showNavigationOverlay.Checked&&radar3D.Checked||showRouteOverlay.Checked&&routes3D.Checked))return;
        int zone=navigationZone;if(overlay3DRequestedZone==zone)return;
        ResetOverlay3DZone();overlay3DRequestedZone=zone;int generation=overlay3DLoadGeneration;
        var cancel=overlay3DLoad=new();var token=cancel.Token;overlay3DProblem="";overlay3DStatus.Text=$"Loading current Zone {zone} for 3D overlays…";
        navigation3DCache.TryGetValue(zone,out var cached);
        try
        {
            var loaded=await Task.Run(()=>
            {
                if(!GameMapLayout.ClientSupported(PoteMemoryProbe.Program.ClientPath))throw new InvalidDataException("Unverified client build");
                var scene=cached??MapSceneReader.Load(Path.GetDirectoryName(PoteMemoryProbe.Program.ClientPath)!,zone,token);
                token.ThrowIfCancellationRequested();Bitmap? artwork=null;GameMapLayout.Extent? extent=null;
                using var maps=new ZoneMapBackground();
                if(maps.TryGet(zone,out var image,out var b)){artwork=new Bitmap(image);extent=new(b.MinX,b.MinY,b.MaxX,b.MaxY);}
                return(Scene:scene,Artwork:artwork,Extent:extent);
            },token);
            if(IsDisposed||token.IsCancellationRequested||generation!=overlay3DLoadGeneration||!connected||navigationZone!=zone){loaded.Artwork?.Dispose();return;}
            if(navigation3DCache.Count>=2&&!navigation3DCache.ContainsKey(zone))navigation3DCache.Remove(navigation3DCache.Keys.First());
            navigation3DCache[zone]=loaded.Scene;overlay3DScene=loaded.Scene;
            using var artwork=loaded.Artwork;
            navigationOverlay3D??=new();routeOverlay3D??=new();
            navigationOverlay3D.SetScene(loaded.Scene,artwork==null?null:new Bitmap(artwork),loaded.Extent);
            routeOverlay3D.SetScene(loaded.Scene,artwork==null?null:new Bitmap(artwork),loaded.Extent);
            radar3DFrameTime=route3DFrameTime=0;overlay3DStatus.Text=$"Zone {zone} · passive 3D overlays · layers above apply";
            // The next normal overlay tick owns rendering/visibility. Loading never shows or activates a window.
        }
        catch(OperationCanceledException){ }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or OverflowException or System.Runtime.InteropServices.ExternalException)
        {
            if(IsDisposed||generation!=overlay3DLoadGeneration)return;
            overlay3DScene=null;overlay3DProblem=ex.Message;overlay3DStatus.Text="3D overlays unavailable: "+ex.Message+" · Using 2D.";
        }
    }
    void ResetOverlay3DZone()
    {
        ++overlay3DLoadGeneration;overlay3DLoad?.Cancel();overlay3DLoad?.Dispose();overlay3DLoad=null;
        overlay3DRequestedZone=-1;overlay3DScene=null;overlay3DProblem="";radar3DFrameTime=route3DFrameTime=0;
        navigationOverlay3D?.Dispose();navigationOverlay3D=null;routeOverlay3D?.Dispose();routeOverlay3D=null;
    }
    void RefreshOverlay3DFrames(Size radarSize,Size routeSize)
    {
        // Always reject a previous-zone frame before applying the frame-rate cap.
        if(overlay3DScene!=null&&overlay3DScene.Zone!=navigationZone)ResetOverlay3DZone();
        _ = EnsureOverlay3DLoaded();if(!Overlay3DReady)return;
        long now=Environment.TickCount64;
        var routes=navigation.SavedRoutesForZone(navigationZone).Select(r=>new MapRoute3D(r.Slot,r.Route)).ToArray();
        void Layers(NavigationOverlay3DRenderer renderer,bool radar)
        {
            renderer.SelectedSlot=SelectedSavedNavigationSlot();renderer.TopView=overlay3DTopView.Checked;
            renderer.ShowMapArtwork=navigation3DMap.Checked;renderer.MapOpacity=navigationMapOpacity.Value;
            renderer.ShowTerrain=navigation3DTerrain.Checked;renderer.ShowObjects=navigation3DObjects.Checked;
            renderer.ShowRoutes=navigation3DRoutes.Checked&&(!radar||showNavigationRoutes.Checked);
            renderer.ShowAnchors=navigation3DAnchors.Checked&&(!radar||showNavigationRoutes.Checked);
            renderer.SetRoutes(routes);
        }
        if(showNavigationOverlay.Checked&&radar3D.Checked&&navigationOverlay3D is {Failed:false} radar&&radarSize.Width>=100&&radarSize.Height>=100)
        {
            Size size=OverlayMapSize(radarSize);
            if(radar.Frame?.Size!=size||now-radar3DFrameTime>=200)
            {
                Layers(radar,true);radar.SetMarkers(Radar3DMarkers());
                SetRadar3DAnnotations(radar);
                radar.SetRadarCamera(navigationPosition,Overlay3DHasPlayer?navigation3DPlayerHeight:MapSceneGeometry.Height(overlay3DScene,navigationPosition)??0,NavigationViewRadius());
                radar.Capture(size);radar3DFrameTime=now;
            }
        }
        if(showRouteOverlay.Checked&&routes3D.Checked&&routeOverlay3D is {Failed:false} route&&routeSize.Width>=100&&routeSize.Height>=100)
        {
            Size size=OverlayMapSize(routeSize);
            if(route.Frame?.Size!=size||now-route3DFrameTime>=400)
            {
                Layers(route,false);route.SetMarkers(Overlay3DHasPlayer?[new("You",navigationPosition,navigation3DPlayerHeight,Color.White,navigation3DPlayerHeading)]:[]);
                route.FitRoutes(CurrentRouteCorridorRadius(),(double)(working?activeGuardOptions?.HuntRadius??radius.Value:radius.Value),CurrentAnchorAreaCenter(),Overlay3DHasPlayer?navigationPosition:null);
                route.Capture(size);route3DFrameTime=now;
            }
        }
        if(navigationOverlay3D is {Failed:true}||routeOverlay3D is {Failed:true})
            overlay3DStatus.Text="3D graphics unavailable · Using 2D. "+(navigationOverlay3D?.Failed==true?navigationOverlay3D.Status:routeOverlay3D?.Status);
    }
    MapMarker3D[] Radar3DMarkers()
    {
        if(!Overlay3DHasPlayer)return [];
        double span=NavigationViewRadius();var markers=new List<MapMarker3D>{new("You",navigationPosition,navigation3DPlayerHeight,Color.White,navigation3DPlayerHeading)};
        var liveChests=entities.Where(e=>e.Position.Finite&&Targeting.IsChest(e)&&!latestHealth.GetValueOrDefault(e.Id).Dead).ToArray();
        markers.AddRange(entities.Where(e=>e.Id!=guardSelfId&&e.Monster&&e.Position.Finite&&double.IsFinite(e.Height)&&!latestHealth.GetValueOrDefault(e.Id).Dead&&(e.Position-navigationPosition).Length<=span)
            .OrderBy(e=>(e.Position-navigationPosition).Length).Take(128).Select(e=>new MapMarker3D(e.Name,e.Position,e.Height,
                latestHealth.GetValueOrDefault(e.Id).Known?Color.FromArgb(244,76,54):Color.FromArgb(255,155,45),double.NaN,encounter.IsEngaged(e),false)));
        if(showTreasureChestMarkers.Checked)
        {
            markers.AddRange(liveChests.Where(e=>double.IsFinite(e.Height)&&(e.Position-navigationPosition).Length<=span).Take(128)
                .Select(e=>new MapMarker3D(Targeting.ChestLabel(e),e.Position,e.Height,ImperialTheme.Gold,double.NaN)));
            var liveIds=liveChests.Select(e=>e.Id).ToHashSet();
            markers.AddRange(chestCatalog.ForZone(navigationZone).Where(c=>!liveIds.Contains(c.Id)&&c.Position.Finite&&(c.Position-navigationPosition).Length<=span)
                .Take(128).Select(c=>(c,h:MapSceneGeometry.Height(overlay3DScene,c.Position))).Where(p=>p.h.HasValue)
                .Select(p=>new MapMarker3D(p.c.Label+" seen",p.c.Position,p.h!.Value,Color.FromArgb(215,165,32),double.NaN)));
        }
        markers.AddRange(Player3DMarkers(span));
        return markers.ToArray();
    }
    void SetRadar3DAnnotations(NavigationOverlay3DRenderer radar)
    {
        if(!Overlay3DHasPlayer){radar.SetAnnotations([],[],null);return;}
        var lines=new List<MapPolyline3D>{new("Trail",navigation.Trail.Where(p=>(p-navigationPosition).Length<NavigationViewRadius()*2).TakeLast(512).ToArray(),Color.SeaGreen,1.5f)};
        if(showNavigationRoutes.Checked)lines.Add(new("Current route",new[]{navigationPosition}.Concat(navigation.Route).Take(1024).ToArray(),Color.DeepSkyBlue,2));
        var areas=navigation.Blocked.Select(b=>new MapArea3D(b.Center,b.Radius,Color.Orange,"Blocked"))
            .Concat(avoidZones.Select(a=>new MapArea3D(a.Center,a.Radius+1,Color.IndianRed,"Avoid"))).Take(128).ToArray();
        radar.SetAnnotations(lines.ToArray(),areas,new(navigationPosition,navigation3DPlayerHeading,Math.Min(NavigationViewRadius(),25),60,Color.FromArgb(150,90,205,255)));
    }
    bool TryDrawOverlay3D(Graphics g,Size size,bool enabled,NavigationOverlay3DRenderer? renderer)
    {
        if(!enabled||!Overlay3DReady||renderer is not {Failed:false,Frame:{} frame})return false;
        g.DrawImage(frame,new Rectangle(Point.Empty,size));return true;
    }
    void DrawRadarOverlay(Graphics g,Size size)
    {
        if(!TryDrawOverlay3D(g,size,radar3D.Checked,navigationOverlay3D))DrawNavigation(g,size);
    }
    void DisposeOverlay3D()
    {
        ResetOverlay3DZone();navigationOverlay3D?.Dispose();navigationOverlay3D=null;routeOverlay3D?.Dispose();routeOverlay3D=null;
    }
}
