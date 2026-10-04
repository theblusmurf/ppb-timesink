using System.Text.Json;

namespace PoteHunter;

public sealed partial class HunterForm
{
    void CheckNavigation3DUi(TabControl tabs)
    {
        if(!offlinePreviewMode||connected||working)throw new InvalidOperationException("3D UI checks require a disconnected offline fixture.");
        var original=CurrentOptions();var originalJson=JsonSerializer.Serialize(original);
        tabs.SelectedTab=navigationPage;PerformLayout();Application.DoEvents();
        if(navigationCanvas.IsDisposed||savedNavigationSlot.IsDisposed||startNavigationRecording.IsDisposed||saveNavigationRoute.IsDisposed)
            throw new InvalidOperationException("Original route controls were disposed during navigation layout replacement.");
        foreach(var control in new Control[]{savedNavigationSlot,startNavigationRecording,saveNavigationRoute,useAlternativeHuntRoutes,
            assignUnassignedNavigationRoutes,clearSavedNavigationRoute,clearAllSavedNavigationRoutes,showNavigationOverlay,showRouteOverlay,
            automaticRouting,guideTreasureChests,resetLootTracker,resetLootTimer})
            if(!navigationPage.Contains(control))throw new InvalidOperationException("Original navigation action was lost: "+control.Text);
        foreach(var section in navigationPage.Controls.Find("navigationLayerSidebar",true).Single().Controls.OfType<CollapsibleSection>())
        {
            section.Expanded=true;PerformLayout();Application.DoEvents();
            if(!section.Header.Visible||!section.Content.Visible||section.Width<240)throw new InvalidOperationException("Navigation section cannot expand: "+section.Name);
            section.Expanded=section.Name is "navigation3DLayers" or "navigationSavedRoutes";
        }
        // Settings from older releases default to the new presentation, without changing their route or combat choices.
        var legacy=JsonSerializer.Deserialize<Options>("{\"Target\":\"Mimic\",\"HuntRadius\":35,\"AutoReviveAfterDeath\":true}")!;
        if(!legacy.Navigation3D||legacy.Navigation3DMapOpacity!=80||legacy.Target!="Mimic"||!legacy.AutoReviveAfterDeath)
            throw new InvalidOperationException("Older settings do not migrate to the default 3D presentation.");
        legacy.Navigation3DMapOpacity=-1;if(legacy.Navigation3DMapOpacity!=0)throw new InvalidOperationException("Map opacity lower boundary.");
        legacy.Navigation3DMapOpacity=101;if(legacy.Navigation3DMapOpacity!=100)throw new InvalidOperationException("Map opacity upper boundary.");
        navigation3DUpdating=true;
        navigation3DPreferred=true;navigation3DFallback=false;navigation3DMap.Checked=true;navigationMapOpacity.Value=60;
        navigation3DTerrain.Checked=true;navigation3DObjects.Checked=false;navigation3DRoutes.Checked=false;navigation3DAnchors.Checked=true;
        navigation3DUpdating=false;ApplyNavigation3DLayers();
        var saved=WithNavigation3DSettings(Options.Read());saved.Save();var restored=Options.Read();
        if(!restored.Navigation3D||!restored.Navigation3DMap||restored.Navigation3DMapOpacity!=60||restored.Navigation3DObjects||restored.Navigation3DRoutes||!restored.Navigation3DAnchors)
            throw new InvalidOperationException("Navigation layers and transparency did not persist.");
        var normalized=WithNavigation3DSettings(original);
        if(JsonSerializer.Serialize(normalized)!=JsonSerializer.Serialize(CurrentOptions()))
            throw new InvalidOperationException("Presentation controls changed a combat, recovery, route or loot option.");
        // A native fixture is displayed only offscreen, without connecting, registering hotkeys or emitting input.
        navigation3DScene=Navigation3DRenderChecks.SyntheticScene(false);navigation3DView.SetScene(navigation3DScene);
        navigation3DView.SetRoutes([]);navigation3DView.SetMarkers([]);navigation3DView.FitMap();UpdateNavigation3DVisibility();
        PerformLayout();Application.DoEvents();
        if(!navigation3DView.Failed)
        {
            if(!navigation3DView.Visible||navigationCanvas.Visible||navigationPlayerButton.Enabled)
                throw new InvalidOperationException("3D fixture visibility or disconnected player availability is incorrect.");
            using var frame=navigation3DView.CaptureFrame();
            frame?.Save(Path.Combine(AppContext.BaseDirectory,"navigation3d-fixture.png"));
        }
        using(var preview=new Bitmap(Width,Height)){DrawToBitmap(preview,new Rectangle(Point.Empty,Size));preview.Save(Path.Combine(AppContext.BaseDirectory,"navigation3d-ui-preview.png"));}
        // Test the same mode selection handlers that users invoke; no route recording or gameplay takes place.
        navigation2DButton.PerformClick();Application.DoEvents();
        if(!navigationCanvas.Visible||navigation3DView.Visible||navigationMapChoice.Enabled||Navigation3DVisible)
            throw new InvalidOperationException("Original 2D view is inaccessible after switching modes.");
        navigation3DPreferred=true;navigation3DFallback=true;UpdateNavigation3DVisibility();
        if(!navigationCanvas.Visible||navigation3DView.Visible)throw new InvalidOperationException("Unavailable 3D did not fall back to 2D.");
        if(!navigation3DView.Failed&&!navigationMapChoice.Enabled)throw new InvalidOperationException("A missing scene prevents selecting another supported map.");
        CheckNavigationRouteHotkeys();
        var before=JsonSerializer.Deserialize<Options>(originalJson)!;
        navigation3DUpdating=true;navigation3DPreferred=before.Navigation3D;navigation3DMap.Checked=before.Navigation3DMap;
        navigation3DTerrain.Checked=before.Navigation3DTerrain;navigation3DObjects.Checked=before.Navigation3DObjects;
        navigation3DRoutes.Checked=before.Navigation3DRoutes;navigation3DAnchors.Checked=before.Navigation3DAnchors;navigationMapOpacity.Value=before.Navigation3DMapOpacity;
        navigation3DUpdating=false;before.Save();ApplyNavigation3DLayers();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"navigation3d-ui-checks.json"),JsonSerializer.Serialize(new
        {Passed=true,HardwareInputEmitted=false,NativeRenderer=navigation3DView.Failed?"Unavailable; 2D fallback verified":"Available; offscreen frame captured",
            Checks=new[]{"old settings migration and opacity bounds","display-only settings persistence","existing controls preserved and sections expand","2D switch and unavailable 3D fallback","disconnected player hidden","existing Home/End recording guards","minimum-size native layout"}}));
    }
}
