using System.Numerics;
using System.Diagnostics;
using System.Text.Json;

namespace PoteHunter;

internal static class Navigation3DRenderChecks
{
    internal static void Run()
    {
        var point=new Vector3(120,7,80);var reflected=Navigation3DView.Reflect(point);
        Require(reflected==new Vector3(120,7,-80)&&Navigation3DView.Reflect(reflected)==point,"Map reflection must occur once without changing units or height");
        var bounds=new GameMapLayout.Extent(0,0,10,10);
        Require(Navigation3DView.ArtworkUv(new(0,0,10),bounds)==new PointF(0,0),"North-west artwork corner must remain top-left");
        Require(Navigation3DView.ArtworkUv(new(10,0,0),bounds)==new PointF(1,1),"South-east artwork corner must remain bottom-right");
        var clipped=Navigation3DView.ClipArtworkTriangle([new(-5,0,-5),new(15,20,-5),new(5,10,15)],bounds);
        Require(clipped.Length>=3&&clipped.All(v=>v.X>=-1e-5&&v.X<=10.00001&&v.Z>=-1e-5&&v.Z<=10.00001),"Artwork triangles must be clipped to calibrated bounds");
        Require(clipped.All(v=>Math.Abs(v.Y-v.X-5)<1e-4),"Artwork clipping must retain the original terrain plane");
        Require(Navigation3DView.ClipArtworkTriangle([new(-20,0,-20),new(-10,0,-20),new(-20,0,-10)],bounds).Length==0,"Unrelated outside artwork must not stretch across map edges");
        var source=SyntheticScene(true);var parts=Navigation3DView.DrapeRoute(source,[new(2,2),new(44,2)]);
        Require(parts.Length==2,"A saved segment crossing missing terrain must be split");
        Require(parts.SelectMany(x=>x).All(v=>v.X<=16.001||v.X>=31.999),"Unknown terrain heights must not be interpolated into the route");
        Require(Navigation3DView.DrapeRoute(null,[new(2,2),new(44,2)]).Length==0,"Missing scenes must not produce invented route heights");
        using var view=new Navigation3DView();view.MapOpacity=-5;Require(view.MapOpacity==0,"Map opacity lower bound");view.MapOpacity=120;Require(view.MapOpacity==100,"Map opacity upper bound");
        view.SelectedSlot=8;Require(view.SelectedSlot==2,"Route slot remains bounded");
        view.SetScene(source);view.SetRoutes([new(0,new SavedNavigationRoute(3,new(2,2),0,[new(2,2),new(8,8)],DateTime.UnixEpoch))]);
        view.FitRoutes();view.FocusAnchor(0);view.TopView=true;Require(view.TopView,"North-up top view can be selected without game input");
        Require(!view.Failed&&!view.IsHandleCreated,"Pure map checks must not create a native window or game connection");
    }
    // Optional local GPU diagnostic. Both the parent and child HWNDs stay hidden;
    // it never calls Show, Activate, client APIs, or input APIs. A headless/remote
    // machine may legitimately have no hardware pixel format and report SKIP.
    internal static string RunNative(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var parent=new Form{ClientSize=new(640,480),ShowInTaskbar=false};
        using var view=new Navigation3DView{Dock=DockStyle.Fill};parent.Controls.Add(view);
        var scene=SyntheticScene(false);view.SetScene(scene);view.TopView=true;
        using(var map=new Bitmap(32,32))
        {
            using(var g=Graphics.FromImage(map)){g.Clear(Color.FromArgb(219,164,52));g.FillRectangle(Brushes.Red,0,0,16,16);g.FillRectangle(Brushes.Blue,16,16,16,16);}
            view.SetMapArtwork(new Bitmap(map),new(0,0,16,16));
        }
        view.SetRoutes([new(0,new SavedNavigationRoute(3,new(3,3),0,[new(3,3),new(12,12)],DateTime.UnixEpoch))]);
        view.SetMarkers([new("Player",new(8,8),5,Color.White)]);view.FitMap();
        _=parent.Handle;_=view.Handle;
        if(view.Failed)return "SKIP: "+view.Status;
        using var capture=view.CaptureFrame();Require(capture!=null,"Supported native renderer must return a frame");
        int varied=0;var background=capture!.GetPixel(0,0);for(int y=0;y<capture.Height;y+=8)for(int x=0;x<capture.Width;x+=8)if(capture.GetPixel(x,y).ToArgb()!=background.ToArgb())varied++;
        Require(varied>20,"Native map frame must contain rendered terrain and overlays");
        capture.Save(Path.Combine(outputDirectory,"native-navigation3d-synthetic.png"));
        view.MapOpacity=0;using var zero=view.CaptureFrame();Require(zero!=null,"Opacity changes must retain a drawable native context");
        bool different=false;for(int y=0;y<zero!.Height&&!different;y+=8)for(int x=0;x<zero.Width;x+=8)if(zero.GetPixel(x,y).ToArgb()!=capture.GetPixel(x,y).ToArgb()){different=true;break;}
        Require(different,"Map artwork opacity must affect GPU readback without rebuilding the scene");
        view.ShowObjects=false;using var objectsOff=view.CaptureFrame();Require(objectsOff!=null&&Different(zero,objectsOff),"Instanced static meshes must affect GPU readback at their map-unit positions");
        view.ShowRoutes=false;view.ShowAnchors=false;view.ShowMapArtwork=false;
        view.Size=new(480,320);using var resized=view.CaptureFrame();Require(resized!=null&&resized.Width==view.ClientSize.Width&&resized.Height==view.ClientSize.Height,"Native context must survive viewport resize");
        var recreate=typeof(Control).GetMethod("RecreateHandle",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        Require(recreate!=null,"Native context recreation check must be available");recreate!.Invoke(view,null);
        using var recreated=view.CaptureFrame();Require(!view.Failed&&recreated!=null&&HasGeometry(recreated),"Native resources must rebuild after HWND/context recreation");
        return "PASS: native hidden 3D readback, artwork opacity, instanced meshes, resize, HWND recreation, resource disposal";
    }
    // Explicit optional local-asset check. It uses the same verified scene and
    // artwork readers as Navigation, while keeping every diagnostic HWND hidden.
    internal static string RunLocal(string clientDirectory,string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var parent=new Form{ClientSize=new(1000,700),ShowInTaskbar=false};
        using var view=new Navigation3DView{Dock=DockStyle.Fill};parent.Controls.Add(view);
        _=parent.Handle;_=view.Handle;
        if(view.Failed)return "SKIP: "+view.Status;
        using var backgrounds=new ZoneMapBackground(clientPath:Path.Combine(clientDirectory,"Client.exe"));
        var results=new List<object>();
        foreach(int zone in new[]{8,9,12})
        {
            var clock=Stopwatch.StartNew();var scene=MapSceneReader.Load(clientDirectory,zone,CancellationToken.None);
            view.SetScene(scene);view.SetRoutes([]);view.SetMarkers([]);view.TopView=true;view.ShowObjects=true;view.ShowTerrain=true;view.ShowMapArtwork=true;view.MapOpacity=70;
            bool artwork=backgrounds.TryGet(zone,out var image,out var bounds);
            view.SetMapArtwork(artwork?new Bitmap(image):null,artwork?new GameMapLayout.Extent(bounds.MinX,bounds.MinY,bounds.MaxX,bounds.MaxY):null);
            var patch=scene.Terrain[scene.Terrain.Length/2];var a=patch.Vertices[4*17+4];var b=patch.Vertices[12*17+12];
            view.SetRoutes([new(0,new SavedNavigationRoute(zone,new(a.X,a.Z),0,[new(a.X,a.Z),new(b.X,b.Z)],DateTime.UnixEpoch))]);
            view.SetMarkers([new("Diagnostic marker",new(b.X,b.Z),b.Y,Color.White)]);view.FitMap();
            using var top=view.CaptureFrame();Require(top!=null,"Local 3D scene must produce a native frame");Require(HasGeometry(top!),"Local scene frame must contain geometry");
            top!.Save(Path.Combine(outputDirectory,$"native-zone{zone}-top.png"));
            view.MapOpacity=0;view.ShowObjects=false;view.ShowRoutes=false;view.ShowAnchors=false;view.SetMarkers([]);
            using var terrain=view.CaptureFrame();Require(terrain!=null,"Terrain layer must retain native context");
            if(artwork)Require(Different(top,terrain!),"Local calibrated artwork and overlay layers must affect native pixels");
            view.MapOpacity=70;view.ShowObjects=true;view.ShowRoutes=true;view.ShowAnchors=true;view.TopView=false;view.FitMap();
            using var orbit=view.CaptureFrame();Require(orbit!=null&&HasGeometry(orbit),"Local scene must render in perspective orbit view");
            orbit!.Save(Path.Combine(outputDirectory,$"native-zone{zone}-orbit.png"));
            results.Add(new{Zone=zone,scene.Source,Terrain=scene.Terrain.Length,Models=scene.Meshes.Length,Objects=scene.Objects.Length,Artwork=artwork,Milliseconds=clock.ElapsedMilliseconds,scene.Status});
        }
        File.WriteAllText(Path.Combine(outputDirectory,"native-local-map-checks.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        return "PASS: hidden native local Zone8, Zone9, Zone12 geometry, calibrated artwork, layers, top and orbit views";
    }
    static bool HasGeometry(Bitmap image)
    {
        int count=0;var background=image.GetPixel(0,0).ToArgb();
        for(int y=0;y<image.Height;y+=8)for(int x=0;x<image.Width;x+=8)if(image.GetPixel(x,y).ToArgb()!=background&&++count>20)return true;
        return false;
    }
    static bool Different(Bitmap a,Bitmap b)
    {
        if(a.Size!=b.Size)return true;
        for(int y=0;y<a.Height;y+=8)for(int x=0;x<a.Width;x+=8)if(a.GetPixel(x,y).ToArgb()!=b.GetPixel(x,y).ToArgb())return true;
        return false;
    }
    internal static MapScene3D SyntheticScene(bool gap)
    {
        MapTerrainPatch Patch(int origin,float height)
        {
            var v=new Vector3[289];for(int row=0;row<17;row++)for(int col=0;col<17;col++)v[row*17+col]=new(origin+col,height,row);
            return new(origin,0,v);
        }
        var mesh=new MapObjectMesh("Synthetic stone pyramid",[new(-2,0,-2),new(2,0,-2),new(2,0,2),new(-2,0,2),new(0,4,0)],
            [0,2,1,0,3,2,0,1,4,1,2,4,2,3,4,3,0,4]);
        var placement=new MapObjectPlacement(0,Matrix4x4.CreateRotationY(.6f)*Matrix4x4.CreateTranslation(12,5,6));
        return new(3,"Synthetic map only",gap?[Patch(0,5),Patch(32,9)]:[Patch(0,5)],gap?[]:[mesh],gap?[]:[placement],"Synthetic map · no game data");
    }
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
