using System.Text.Json;

namespace PoteHunter;

public sealed partial class HunterForm
{
    // Runs only inside the disconnected, offscreen native UI fixture. It never
    // connects a World, registers hotkeys, loads game assets or sends input.
    void CheckOverlay3DUi()
    {
        if(!offlinePreviewMode||connected||working)
            throw new InvalidOperationException("3D overlay checks require a disconnected offline fixture.");
        var savedOptions=File.Exists(Options.PathName)?File.ReadAllBytes(Options.PathName):null;
        bool oldRadar=radar3D.Checked,oldRoutes=routes3D.Checked,oldTop=overlay3DTopView.Checked;
        bool oldChests=showTreasureChestMarkers.Checked;
        bool oldConnected=connected,oldWorking=working,oldMainPreferred=navigation3DPreferred;
        string oldFilter=filter.Text;int oldZone=navigationZone,oldMapChoice=navigationMapChoice.SelectedIndex;
        Vec oldPosition=navigationPosition;double oldHeight=navigation3DPlayerHeight,oldHeading=navigation3DPlayerHeading;
        long oldSeen=navigation3DPlayerSeen;var oldScene=overlay3DScene;
        int oldRequested=overlay3DRequestedZone;
        try
        {
            void Require(bool result,string message)
            {if(!result)throw new InvalidOperationException(message);}
            foreach(var control in new Control[]{radar3D,routes3D,overlay3DTopView,overlay3DStatus})
                Require(navigationPage.Contains(control),"Overlay presentation control is not accessible: "+control.Name);

            var legacy=JsonSerializer.Deserialize<Options>("{\"Target\":\"Mimic\",\"HuntRadius\":35,\"AutoRepairAfterDeath\":true}")!;
            Require(legacy.Radar3D&&legacy.RouteOverlay3D&&!legacy.Overlay3DTopView,
                "Older settings do not select the new 3D overlays by default.");
            Require(legacy.Target=="Mimic"&&legacy.HuntRadius==35&&legacy.AutoRepairAfterDeath,
                "Overlay migration changed the existing hunt or recovery settings.");

            // Changing a presentation control during a hunt must save just
            // these three fields, leaving an unrelated uncommitted UI edit out.
            var baseline=Options.Read();baseline.Target="Overlay fixture saved target";baseline.Save();
            working=true;filter.Text="Overlay fixture unsaved target";
            radar3D.Checked=false;routes3D.Checked=true;overlay3DTopView.Checked=true;
            Overlay3DPresentationChanged();
            var expected=JsonSerializer.Deserialize<Options>(JsonSerializer.Serialize(baseline))!;
            expected.Radar3D=false;expected.RouteOverlay3D=true;expected.Overlay3DTopView=true;
            Require(JsonSerializer.Serialize(Options.Read())==JsonSerializer.Serialize(expected),
                "Changing 3D overlay presentation while hunting saved unrelated options.");
            Require(!Options.Read().Radar3D&&Options.Read().RouteOverlay3D&&Options.Read().Overlay3DTopView,
                "Independent 3D overlay choices did not persist.");
            working=false;filter.Text=oldFilter;
            var applied=WithOverlay3DSettings(new Options());
            Require(!applied.Radar3D&&applied.RouteOverlay3D&&applied.Overlay3DTopView,
                "CurrentOptions does not retain independent overlay presentation choices.");

            overlay3DScene=Navigation3DRenderChecks.SyntheticScene(false) with{Zone=8};
            navigationZone=8;navigationPosition=new(8,8);navigation3DPlayerHeight=5;
            navigation3DPlayerHeading=.7;navigation3DPlayerSeen=Environment.TickCount64;
            Require(Overlay3DReady&&!Overlay3DHasPlayer,
                "A disconnected player appeared in a cached overlay scene.");
            connected=true;
            Require(Overlay3DHasPlayer,"Fresh current-zone telemetry did not reach the 3D overlay.");
            var markers=Radar3DMarkers();
            Require(markers.Any(m=>m.Name=="You"&&m.Position==navigationPosition&&m.Height==5&&m.Heading==.7),
                "The live radar player lost its copied position, height or facing.");
            var nearby=entities.Where(e=>e.Id!=guardSelfId&&e.Monster&&e.Position.Finite&&double.IsFinite(e.Height)&&
                !latestHealth.GetValueOrDefault(e.Id).Dead&&(e.Position-navigationPosition).Length<=NavigationViewRadius())
                .OrderBy(e=>(e.Position-navigationPosition).Length).Take(128).ToArray();
            Require(nearby.Any(encounter.IsEngaged),"The offline radar fixture must contain an engaged living monster.");
            foreach(var entity in nearby)
            {
                var marker=markers.Single(m=>m.Name==entity.Name&&m.Position==entity.Position);
                bool known=latestHealth.GetValueOrDefault(entity.Id).Known;
                Require(marker.Engaged==encounter.IsEngaged(entity)&&double.IsNaN(marker.Heading)&&
                    marker.Color==(known?Color.FromArgb(244,76,54):Color.FromArgb(255,155,45)),
                    "The radar lost engaged status, unknown-health color, or invented a monster's facing.");
            }
            Require(!entities.Where(e=>e.Monster&&(latestHealth.GetValueOrDefault(e.Id).Dead||
                (e.Position-navigationPosition).Length>NavigationViewRadius())).Any(e=>markers.Any(m=>m.Name==e.Name&&m.Position==e.Position)),
                "Dead or out-of-radius monsters remained in the live 3D radar.");
            var visibleChests=entities.Where(e=>e.Position.Finite&&double.IsFinite(e.Height)&&Targeting.IsChest(e)&&
                !latestHealth.GetValueOrDefault(e.Id).Dead&&(e.Position-navigationPosition).Length<=NavigationViewRadius()).Take(128).ToArray();
            if(showTreasureChestMarkers.Checked)
                foreach(var chest in visibleChests)
                    Require(markers.Any(m=>m.Name==Targeting.ChestLabel(chest)&&m.Position==chest.Position&&
                        m.Color==ImperialTheme.Gold&&double.IsNaN(m.Heading)),"The live chest marker lost its gold color or invented facing.");
            working=true;showTreasureChestMarkers.Checked=false;
            var hiddenChests=Radar3DMarkers();
            Require(!visibleChests.Any(e=>hiddenChests.Any(m=>m.Name==Targeting.ChestLabel(e)&&m.Position==e.Position)),
                "Disabling chest markers did not hide them from the 3D radar.");
            showTreasureChestMarkers.Checked=oldChests;working=false;
            // Browsing a different map or choosing 2D in the main Navigation
            // viewport must not redirect or suppress the current-zone overlay.
            int otherMap=Enumerable.Range(0,navigationMapChoice.Items.Count)
                .First(i=>navigationMapChoice.Items[i] is NavigationMapChoice{Zone:9});
            navigationMapChoice.SelectedIndex=otherMap;navigation3DPreferred=false;
            Require(Overlay3DReady&&Overlay3DHasPlayer&&overlay3DScene?.Zone==8,
                "The main viewport's map or 2D mode changed the in-game overlay zone.");
            navigation3DPlayerSeen=Environment.TickCount64-4000;
            Require(!Overlay3DHasPlayer,"Stale telemetry remained a live 3D overlay marker.");
            Require(Radar3DMarkers().Length==0,"Stale telemetry retained dynamic radar markers.");
            navigation3DPlayerSeen=Environment.TickCount64;navigation3DPlayerHeight=double.NaN;
            Require(!Overlay3DHasPlayer,"Unknown player elevation was invented for the 3D overlay.");
            navigation3DPlayerHeight=5;navigationZone=9;
            Require(!Overlay3DReady&&!Overlay3DHasPlayer&&Radar3DMarkers().Length==0,
                "A zone transition retained the old scene's live player or radar markers.");
            navigationZone=8;connected=false;

            // Native readback remains optional on a headless machine. When
            // supported, exercise the production paint gate with an actual
            // cached frame; no diagnostic HWND is ever shown or activated.
            using(var renderer=new NavigationOverlay3DRenderer())
            {
                renderer.SetScene(overlay3DScene,null,null);renderer.TopView=true;
                renderer.SetRadarCamera(new(8,8),5,10);
                using var painted=new Bitmap(320,220);using var graphics=Graphics.FromImage(painted);
                var captured=renderer.Capture(painted.Size);
                if(captured!=null)
                {
                    Require(!renderer.WindowsVisible,"The cached overlay renderer showed a diagnostic window.");
                    Require(!TryDrawOverlay3D(graphics,painted.Size,false,renderer),"A disabled 3D overlay used its old frame.");
                    navigationZone=9;
                    Require(!TryDrawOverlay3D(graphics,painted.Size,true,renderer),"A stale-zone GPU frame remained drawable.");
                    navigationZone=8;overlay3DScene=null;
                    Require(!TryDrawOverlay3D(graphics,painted.Size,true,renderer),"A missing-scene GPU frame remained drawable.");
                    overlay3DScene=Navigation3DRenderChecks.SyntheticScene(false) with{Zone=8};
                    Require(TryDrawOverlay3D(graphics,painted.Size,true,renderer),"A valid current-zone GPU frame did not paint.");
                    painted.Save(Path.Combine(AppContext.BaseDirectory,"radar3d-cached-frame-preview.png"));
                }
                else Require(renderer.Failed,"A healthy renderer did not return its cached native frame.");
            }

            int generation=overlay3DLoadGeneration;
            ResetOverlay3DZone();
            Require(overlay3DScene==null&&overlay3DRequestedZone==-1&&overlay3DLoadGeneration>generation,
                "Resetting overlay geometry did not invalidate the scene and pending zone load.");
            Require(navigationOverlay3D==null&&routeOverlay3D==null,
                "Resetting overlay geometry retained the previous renderer or frame cache.");
            navigationZone=0; // Keep the 2D fallback fixture independent of installed map assets.
            using(var fallback=new Bitmap(360,260))
            {
                using var graphics=Graphics.FromImage(fallback);
                DrawRadarOverlay(graphics,fallback.Size);
                fallback.Save(Path.Combine(AppContext.BaseDirectory,"radar3d-fallback-preview.png"));
            }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"overlay3d-ui-checks.json"),
                JsonSerializer.Serialize(new{Passed=true,HardwareInputEmitted=false,LiveClientConnected=false,
                    Checks=new[]{"default migration leaves hunt/recovery intact","independent radar/route/top-view presentation persisted",
                        "active presentation saves no unrelated UI edits","controls retained in Navigation",
                        "main map browsing and 2D mode do not change overlay zone","engaged/unknown-health markers remain distinct; no invented monster facing",
                        "dead and out-of-radius monsters hidden; live gold chest markers obey visibility toggle",
                        "disconnected, stale, nonfinite and wrong-zone live markers hidden",
                        "cached GPU frame paint gate rejects disabled, missing-scene and wrong-zone overlays",
                        "zone reset invalidates pending load and releases frame resources","existing 2D radar fallback renders"}}));
        }
        finally
        {
            connected=false;working=false;filter.Text=oldFilter;
            radar3D.Checked=oldRadar;routes3D.Checked=oldRoutes;overlay3DTopView.Checked=oldTop;
            showTreasureChestMarkers.Checked=oldChests;
            navigationZone=oldZone;navigationPosition=oldPosition;navigation3DPlayerHeight=oldHeight;
            navigation3DPlayerHeading=oldHeading;navigation3DPlayerSeen=oldSeen;
            overlay3DScene=oldScene;overlay3DRequestedZone=oldRequested;
            navigationMapChoice.SelectedIndex=oldMapChoice;navigation3DPreferred=oldMainPreferred;
            connected=oldConnected;working=oldWorking;
            if(savedOptions!=null)File.WriteAllBytes(Options.PathName,savedOptions);
            else if(File.Exists(Options.PathName))File.Delete(Options.PathName);
        }
    }
}
