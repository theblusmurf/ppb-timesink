using System.Numerics;
using System.Text.Json;

namespace PoteHunter;

internal static class OverlayPerformanceChecks
{
    // WinForms/DIB checks belong after the asynchronous self-test suite. No
    // window is shown, selected or activated and no input dispatcher is called.
    internal static void Run()
    {
        var checks = new List<string>();
        void Check(bool passed, string message)
        { if(!passed)throw new InvalidOperationException("Overlay performance: " + message); checks.Add(message); }
        var lists = CheckLists(Check);
        int readbacks = CheckReadback(Check);
        int layered = CheckLayeredSurface(Check);
        int lootFrames = CheckLootGate(Check);
        var terrain = CheckTerrain(Check);
        int routeBuilds = CheckRouteBounds(Check);
        var report = new { Passed=true, HardwareInputEmitted=false, LiveGameplayVerified=false,
            ListView=lists, SameSizeReadbackAllocations=readbacks, SameSizeLayeredSurfaceAllocations=layered,
            TimerOnlyPresentationsPerSecond=lootFrames, Terrain=terrain, UnchangedStaticRouteBoundsBuilds=routeBuilds, Checks=checks };
        if(!DiagnosticIo.TryAtomicWrite("write overlay performance checks", Path.Combine(AppContext.BaseDirectory,"overlay-performance-checks.json"),
            JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true})))throw new IOException("Could not save overlay performance checks.");
    }

    static object CheckLists(Action<bool,string> check)
    {
        using var view = new ListView { View=View.Details, ForeColor=Color.Gainsboro, BackColor=Color.FromArgb(20,30,40), MultiSelect=false };
        view.Columns.Add("Identity"); view.Columns.Add("Health");
        ListViewItem Row(string key,string health="100") => new([key,health]) { Name=key, Tag=key };
        var first = ListViewPresentation.Reconcile(view,[Row("A"),Row("B")],r=>r.Name);
        var retained = view.Items[0]; retained.Selected=true;
        var unchanged = ListViewPresentation.Reconcile(view,[Row("A"),Row("B")],r=>r.Name);
        check(first.Added==2 && unchanged==default && ReferenceEquals(retained,view.Items[0]),"unchanged keyed rows issue zero adds/removes/updates/moves and retain row objects");
        var changed = ListViewPresentation.Reconcile(view,[Row("A","90"),Row("B")],r=>r.Name);
        check(changed==new ListViewPresentationWork(0,0,1,0) && view.Items[0].SubItems[1].Text=="90","one health change updates one existing row");
        var reordered = ListViewPresentation.Reconcile(view,[Row("B"),Row("A","90"),Row("C")],r=>r.Name);
        check(reordered.Added==1 && reordered.Removed==0 && reordered.Moved==1 && ReferenceEquals(retained,view.Items[1]) && retained.Selected,
            "reordering retains stable identity, selection and existing rows while adding only the new row");
        var styled = Row("A","80"); styled.ForeColor=Color.Orange; styled.BackColor=Color.Navy; styled.ToolTipText="Observed reason";
        ListViewPresentation.Reconcile(view,[styled,Row("C")],r=>r.Name);
        check(view.Items.Count==2 && ReferenceEquals(retained,view.Items[0]) && retained.ForeColor==Color.Orange && retained.BackColor==Color.Navy && retained.ToolTipText=="Observed reason",
            "pruning and current threat/selection presentation stay synchronized");
        ListViewPresentation.Reconcile(view,[Row("A","80"),Row("C")],r=>r.Name);
        check(retained.ForeColor==view.ForeColor && retained.BackColor==view.BackColor && retained.ToolTipText.Length==0,
            "cleared row styles return to the destination theme without retaining stale highlights");
        var duplicate = ListViewPresentation.Reconcile(view,[Row("X"),Row("X")],r=>r.Name);
        check(duplicate.Added==2 && duplicate.Removed==2 && view.Items.Count==2,"ambiguous keys use complete replacement rather than displaying stale identity data");
        check(!view.IsHandleCreated && !view.Focused,"list presentation does not create, show or activate a native window");
        return new { Initial=first, Unchanged=unchanged, OneHealthChange=changed, Reordered=reordered };
    }

    static int CheckReadback(Action<bool,string> check)
    {
        using var buffer = new OverlayReadbackBuffer(); buffer.Ensure(new(2,2));
        var pixels=buffer.Pixels; var image=buffer.Image;
        for(int i=0;i<50;i++)buffer.Ensure(new(2,2));
        int sameSize=buffer.Allocations;
        check(sameSize==1 && ReferenceEquals(pixels,buffer.Pixels) && ReferenceEquals(image,buffer.Image),"fifty same-size captures retain one pixel array and one owned bitmap");
        // Bottom row first, RGBA input; the displayed bitmap is top-down BGRA.
        byte[] input=[255,0,0,17,0,255,0,29,0,0,255,41,255,255,255,53]; input.CopyTo(pixels,0); buffer.CopyBottomUpRgba();
        check(image!.GetPixel(0,0).ToArgb()==Color.Blue.ToArgb() && image.GetPixel(1,0).ToArgb()==Color.White.ToArgb() &&
            image.GetPixel(0,1).ToArgb()==Color.Red.ToArgb() && image.GetPixel(1,1).ToArgb()==Color.Lime.ToArgb(),"reused readback preserves orientation, color channels and the original opaque alpha");
        using(var copy=new Bitmap(image))
        {
            Array.Clear(pixels); buffer.CopyBottomUpRgba();
            check(copy.GetPixel(0,0).ToArgb()==Color.Blue.ToArgb(),"caller-owned frame copies remain independent from the borrowed frame's next capture");
            buffer.Dispose(); check(copy.GetPixel(0,0).ToArgb()==Color.Blue.ToArgb(),"disposing a viewport's borrowed image does not dispose the caller-owned diagnostic copy");
        }
        buffer.Ensure(new(3,2));check(buffer.Allocations==2 && buffer.Pixels.Length==24,"resize releases previous storage and allocates exactly the bounded new frame");
        bool bound=false;try{buffer.Ensure(new(4097,2));}catch(ArgumentOutOfRangeException){bound=true;}
        check(bound && buffer.Pixels.Length==24,"oversized readbacks are rejected without replacing the valid owned frame");
        buffer.Dispose();check(buffer.Image==null && buffer.Pixels.Length==0,"readback disposal releases retained pixel and bitmap storage");
        return sameSize;
    }

    static int CheckLayeredSurface(Action<bool,string> check)
    {
        using var surface = new LootLayeredSurface(); surface.Ensure(new(160,80),IntPtr.Zero); IntPtr dc=surface.DC;
        for(int i=0;i<50;i++)
        {
            surface.Ensure(new(160,80),IntPtr.Zero);
            surface.Draw(g=>{using var brush=new SolidBrush(Color.FromArgb(100,40,80,120));g.FillRectangle(brush,0,0,30,20);});
        }
        int sameSize=surface.Allocations;
        check(sameSize==1 && surface.DC==dc && surface.Allocated,"fifty same-size layered draws reuse one premultiplied DIB and one memory DC");
        surface.Ensure(new(180,90),IntPtr.Zero);check(surface.Allocations==2 && surface.Allocated,"layered resize releases and replaces its owned native surface once");
        bool bound=false;try{surface.Ensure(new(2049,1),IntPtr.Zero);}catch(ArgumentOutOfRangeException){bound=true;}
        check(bound && surface.Allocations==2,"layered storage stays bounded on an invalid requested size");
        surface.Dispose();surface.Dispose();check(!surface.Allocated && surface.DC==IntPtr.Zero,"repeated layered disposal restores selection and releases the DIB/DC once");
        return sameSize;
    }

    static int CheckLootGate(Action<bool,string> check)
    {
        var snapshot = new LootTrackerSnapshot(8,0,[new("Mimic",1,1,[new("Gold",100)])],
            [new("Mimic","Gold",new(3,4),DateTime.UnixEpoch)], [new("Gold",100),new("Silvin",2)],
            DateTime.UnixEpoch,TimeSpan.FromSeconds(10),DateTime.UnixEpoch,TimeSpan.FromSeconds(10),[new("Gold",36000)])
            { Wallet=new(true,"Synthetic",1100,1000,100,DateTime.UnixEpoch,"Verified") };
        var gate = new LootPresentationGate();check(gate.Changed(snapshot,0,100,20),"first loot snapshot presents");
        for(int i=1;i<5;i++)check(!gate.Changed(snapshot with{Elapsed=TimeSpan.FromSeconds(10+i*.2),RateElapsed=TimeSpan.FromSeconds(10+i*.2),
            Wallet=snapshot.Wallet with{ReadUtc=DateTime.UnixEpoch.AddMilliseconds(i*200)},HourlyLoot=[new("Gold",36000/(1+i*.02))]},0,100,20),
            "timer-only subsecond polls reuse the displayed second while tracking continues");
        int firstSecond=gate.Presentations;
        check(gate.Changed(snapshot with{Elapsed=TimeSpan.FromSeconds(11),RateElapsed=TimeSpan.FromSeconds(11)},0,100,20),"the next displayed second redraws elapsed time and current rates");
        gate.Changed(snapshot,0,100,20);
        foreach(var wallet in new[]{snapshot.Wallet with{Current=1200},snapshot.Wallet with{Baseline=900},snapshot.Wallet with{Net=200},
            snapshot.Wallet with{Known=false},snapshot.Wallet with{Status="Unknown balance"},snapshot.Wallet with{Character="Another identity"}})
        {
            check(gate.Changed(snapshot with{Wallet=wallet},0,100,20),"wallet value/baseline/net/validity/status/identity changes redraw immediately");gate.Changed(snapshot,0,100,20);
        }
        check(gate.Changed(snapshot with{TrackedLoot=[new("Gold",100),new("Silvin",3)]},0,100,20),"new metal totals redraw immediately");gate.Changed(snapshot,0,100,20);
        check(gate.Changed(snapshot with{Sources=[new("Mimic",2,1,[new("Gold",100)])]},0,100,20),"source kills redraw detailed designs immediately");gate.Changed(snapshot,0,100,20);
        check(gate.Changed(snapshot with{Sources=[new("Mimic",1,1,[new("Gold",101)])]},0,100,20),"detailed source item totals redraw immediately");gate.Changed(snapshot,0,100,20);
        check(gate.Changed(snapshot with{RecentDrops=[snapshot.RecentDrops[0] with{SeenUtc=DateTime.UnixEpoch.AddSeconds(1)}]},0,100,20),"another same-name drop redraws immediately using its timestamp");gate.Changed(snapshot,0,100,20);
        check(gate.Changed(snapshot with{RecentDrops=[snapshot.RecentDrops[0] with{Position=new(4,4)}]},0,100,20),"recent-drop coordinates redraw immediately");gate.Changed(snapshot,0,100,20);
        check(gate.Changed(snapshot with{SessionStartedUtc=DateTime.UnixEpoch.AddSeconds(1)},0,100,20),"session reset redraws immediately");
        check(gate.Changed(snapshot,1,100,20) && gate.Changed(snapshot,1,125,20) && gate.Changed(snapshot,1,125,30) && gate.Changed(snapshot,1,125,30,true),
            "design, scale, transparency and explicit reset force a fresh presentation");
        return firstSecond;
    }

    static object CheckTerrain(Action<bool,string> check)
    {
        MapTerrainPatch Patch(float x,float z,float height=1)
        {
            var vertices=new Vector3[17*17];
            for(int r=0;r<17;r++)for(int c=0;c<17;c++)
            {float px=x+(float)(c*MapSceneGeometry.CellStep),pz=z+(float)(r*MapSceneGeometry.CellStep);vertices[r*17+c]=new(px,height+px*.25f+pz*.5f,pz);}
            return new((int)x,(int)z,vertices);
        }
        var patches=(from x in Enumerable.Range(0,16) from z in Enumerable.Range(0,16) select Patch(x*100,z*100)).ToArray();
        var scene=new MapScene3D(8,"Synthetic indexed terrain",patches,[],[],"Synthetic");
        var point=new Vec(1507,1509); int candidates=MapTerrainHeightIndex.Candidates(scene,point).Count;
        check(candidates==1 && Math.Abs(MapSceneGeometry.Height(scene,point)!.Value-(1+point.X*.25+point.Y*.5))<.001,
            "far-patch height examines one indexed patch rather than all 256 while preserving planar interpolation");
        foreach(var patch in patches.Where((_,i)=>i%17==0))foreach(var vertex in new[]{patch.Vertices[0],patch.Vertices[8*17+8],patch.Vertices[^1]})
            check(Math.Abs(MapSceneGeometry.Height(scene,new(vertex.X,vertex.Z))!.Value-vertex.Y)<.001,"terrain start, interior and epsilon-inclusive end retain the exact original elevation");
        check(MapSceneGeometry.Height(scene,new(90,90))==null && MapSceneGeometry.Height(scene,new(double.NaN,1))==null,
            "terrain gaps and unknown coordinates remain unknown");
        var negative=scene with{Terrain=[Patch(-100,-100)]};
        check(MapSceneGeometry.Height(negative,new(-95,-95)) is double h && Math.Abs(h-(1-95*.75))<.001,"negative terrain coordinates use the matching signed index cell");
        var overlapping=scene with{Terrain=[Patch(0,0,100),Patch(0,0,200)]};
        check(MapSceneGeometry.Height(overlapping,new(5,5)) is double first && Math.Abs(first-103.75)<.001,"overlapping patches preserve first-patch precedence");
        var malformed=scene with{Terrain=[Patch(0,0),new(0,0,Enumerable.Range(0,289).Select(i=>new Vector3(i%17*1e20f,0,i/17*1e20f)).ToArray())]};
        check(MapTerrainHeightIndex.Candidates(malformed,new(5,5)).Count==2 && MapSceneGeometry.Height(malformed,new(5,5)) is double fallback && Math.Abs(fallback-4.75)<.001,
            "oversized/malformed patch bounds safely fall back to original ordered terrain lookup");
        return new { LinearCandidates=patches.Length, IndexedCandidates=candidates };
    }

    static int CheckRouteBounds(Action<bool,string> check)
    {
        var scene=Navigation3DRenderChecks.SyntheticScene(false);
        MapRoute3D[] routes=[new(0,new SavedNavigationRoute(3,new(8,8),0,[new(3,3),new(8,8)],DateTime.UnixEpoch))];
        var bounds=new PassiveRouteBounds();
        check(bounds.Get(scene,routes,null,out var min,out var max) && min==new Vector3(3,5,-8) && max==new Vector3(8,5,-3),"static route fitting preserves exact route elevation and reflected extents");
        for(int i=0;i<50;i++)bounds.Get(scene,routes,null,out _,out _);int unchanged=bounds.Builds;
        check(unchanged==1,"fifty unchanged passive route fits build static point/elevation bounds once");
        check(bounds.Get(scene,routes,new(12,12),out _,out var moved) && moved.X==12 && bounds.Builds==1,
            "fresh moving-player bounds update immediately without rebuilding the static route");
        bounds.Invalidate();bounds.Get(scene,routes,null,out _,out _);check(bounds.Builds==2,"explicit scene/route invalidation rebuilds static extents");
        return unchanged;
    }
}
