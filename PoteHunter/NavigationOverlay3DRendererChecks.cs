using System.Text.Json;
using System.Diagnostics;
using System.Reflection;

namespace PoteHunter;

internal static class NavigationOverlay3DRendererChecks
{
    // Keep this after asynchronous self-checks: WinForms control construction
    // installs a UI synchronization context on this thread.
    internal static void Run()
    {
        Require(NavigationOverlay3DRenderer.BoundedSize(new(1800,900))==new Size(900,450),"Overlay readback must bound resolution and retain aspect ratio");
        Require(NavigationOverlay3DRenderer.BoundedSize(new(300,200))==new Size(300,200),"Small overlay must retain its native dimensions");
        Require(NavigationOverlay3DRenderer.BoundedSize(new(0,500)).IsEmpty,"Empty overlay cannot allocate a native frame");
        using(var passiveView=new Navigation3DView(passive:true))
        {
            var camera=passiveView.CameraState;
            foreach(string method in new[]{"OnMouseDown","OnMouseMove","OnMouseUp","OnMouseWheel"})
                typeof(Navigation3DView).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(passiveView,[new MouseEventArgs(MouseButtons.Left,1,20,20,120)]);
            Require(!passiveView.IsHandleCreated&&!passiveView.AutomaticFrames&&passiveView.CameraState==camera,"Passive viewport must ignore mouse events without HWND, focus, capture or camera changes");
        }
        using var renderer=new NavigationOverlay3DRenderer();
        Require(!renderer.NativeWindowsCreated&&!renderer.WindowsVisible&&!renderer.AutomaticFrames,"Offscreen overlay must start without shown windows or a rendering timer");
        renderer.SetScene(Navigation3DRenderChecks.SyntheticScene(false),null,null);
        renderer.SetRoutes([new(0,new SavedNavigationRoute(3,new(3,3),0,[new(3,3),new(12,12)],DateTime.UnixEpoch))]);
        renderer.SetMarkers([new("Engaged",new(8,8),5,Color.Orange,double.NaN,true)]);
        renderer.SetRouteRadii(10,100,new(3,3));renderer.FitRoutes(10,100,new(3,3),new(8,8));
        renderer.SetAnnotations([new("Trail",[new(2,2),new(8,8)],Color.SteelBlue)],
            [new(new(5,5),2,Color.Teal,"Avoid")],new(new(8,8),0,5,45,Color.Teal));
        Require(!renderer.NativeWindowsCreated&&!renderer.WindowsVisible&&!renderer.AutomaticFrames,"Data updates cannot create, activate or continuously render a window");
        renderer.Clear();Require(renderer.Frame==null&&renderer.Capture(new(450,450))==null&&!renderer.NativeWindowsCreated,"Clearing a zone must discard cached pixels and retain 2D fallback");
    }
    internal static string RunNative(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);IntPtr foreground=NavigationOverlay.ForegroundWindow;
        using var renderer=new NavigationOverlay3DRenderer();renderer.SetScene(Navigation3DRenderChecks.SyntheticScene(false),null,null);
        renderer.SetRoutes([new(0,new SavedNavigationRoute(3,new(3,3),0,[new(3,3),new(12,12)],DateTime.UnixEpoch))]);
        renderer.SetMarkers([new("Player",new(8,8),5,Color.White,0),new("Engaged",new(10,10),5,Color.Orange,double.NaN,true),
            new("Enemy: Demo",new(5,11),5,Color.FromArgb(255,94,124),double.NaN,Player:true)]);
        renderer.SetAnnotations([new("Trail",[new(2,2),new(4,4),new(8,8)],Color.SteelBlue,2)],
            [new(new(5,5),2,Color.Teal,"Avoid")],new(new(8,8),0,5,45,Color.Teal));
        renderer.SetRadarCamera(new(8,8),5,10);var radar=renderer.Capture(new(480,320));
        if(renderer.Failed)return "SKIP: "+renderer.Status;
        Require(radar!=null&&HasGeometry(radar),"Hidden radar renderer must produce tilted 3D terrain and copied annotations");
        Require(radar!=null&&HasPlayerDiamond(radar),"The native radar omitted the pink enemy-player diamond and label.");
        int firstCaptures=renderer.CaptureCount,firstAllocations=renderer.ReadbackAllocations;
        for(int i=0;i<5;i++)Require(ReferenceEquals(radar,renderer.Capture(new(480,320))),"Unchanged optional overlay captures should retain the same borrowed frame");
        Require(renderer.CaptureCount==firstCaptures&&renderer.ReadbackAllocations==firstAllocations,"Unchanged frames must issue zero additional native readbacks or storage allocations");
        radar!.Save(Path.Combine(outputDirectory,"native-overlay-radar-tilted.png"));
        using var comparison=new Bitmap(radar);renderer.TopView=true;var top=renderer.Capture(new(480,320));
        Require(top!=null&&Different(comparison,top),"Top view must change camera while retaining the hidden native context");
        Require(renderer.CaptureCount==firstCaptures+1&&renderer.ReadbackAllocations==firstAllocations,"Changed camera must redraw immediately while borrowing the same-sized storage");
        top!.Save(Path.Combine(outputDirectory,"native-overlay-radar-top.png"));
        int beforeMove=renderer.CaptureCount;
        renderer.SetRadarCamera(new(9,8),5,10);Require(renderer.Capture(new(480,320))!=null&&renderer.CaptureCount==beforeMove+1,"Player motion must bypass the unchanged-frame gate");
        renderer.MapOpacity=55;int beforeStyle=renderer.CaptureCount;
        Require(renderer.Capture(new(480,320))!=null&&renderer.CaptureCount==beforeStyle+1,"Overlay layer/style changes must bypass the unchanged-frame gate");renderer.MapOpacity=65;
        renderer.TopView=false;renderer.SetMarkers([new("Player",new(8,8),5,Color.White,0)]);renderer.SetAnnotations([],[],null);
        renderer.FitRoutes(2,100,new(3,3),new(8,8));var route=renderer.Capture(new(640,400));Require(route!=null&&HasGeometry(route),"Full route fit must retain geometry and radius annotations");
        route!.Save(Path.Combine(outputDirectory,"native-overlay-routes-tilted.png"));
        Require(renderer.ReadbackAllocations==firstAllocations+1,"Bounded resize must replace the readback storage once");
        using var largeArea=new Bitmap(route);var fittedCamera=renderer.CameraState;renderer.FitRoutes(2,1,new(3,3),new(8,8));var smallArea=renderer.Capture(new(640,400));
        Require(smallArea!=null&&Different(largeArea,smallArea),"Farming area radius changes must affect annotation pixels");
        Require(renderer.CameraState==fittedCamera,"Farming radius must annotate the fitted route without changing camera bounds or zoom");
        Require(!renderer.WindowsVisible&&!renderer.AutomaticFrames&&foreground==NavigationOverlay.ForegroundWindow,"Hidden overlay rendering must preserve foreground window and stay timer-free");
        renderer.Clear();Require(renderer.Frame==null&&renderer.Capture(new(640,400))==null,"Zone clear must remove stale readback before any new scene");
        File.WriteAllText(Path.Combine(outputDirectory,"native-overlay-checks.json"),JsonSerializer.Serialize(new{Passed=true,HardwareInputEmitted=false,LiveGameplayVerified=false,Hidden=true,TimerFree=true,ForegroundPreserved=true,PlayerDiamondVisible=true,
            UnchangedAdditionalCaptures=0,SameSizeReadbackAllocations=firstAllocations,TotalCaptures=renderer.CaptureCount,TotalReadbackAllocations=renderer.ReadbackAllocations},new JsonSerializerOptions{WriteIndented=true}));
        return "PASS: passive hidden radar/route readback, tilted/top cameras, engagement/cone/trail/radius annotations, bounded resize, foreground and stale-frame guards";
    }
    internal static string RunLocal(string clientDirectory,string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);IntPtr foreground=NavigationOverlay.ForegroundWindow;
        using var backgrounds=new ZoneMapBackground(clientPath:Path.Combine(clientDirectory,"Client.exe"));
        using var renderer=new NavigationOverlay3DRenderer();var results=new List<object>();
        foreach(int zone in new[]{8,9,12})
        {
            var scene=MapSceneReader.Load(clientDirectory,zone,CancellationToken.None);var patch=scene.Terrain[scene.Terrain.Length/2];
            var a=patch.Vertices[6*17+6];var b=patch.Vertices[10*17+10];bool artwork=backgrounds.TryGet(zone,out var image,out var bounds);
            renderer.SetScene(scene,artwork?new Bitmap(image):null,artwork?new(bounds.MinX,bounds.MinY,bounds.MaxX,bounds.MaxY):null);
            renderer.SetRoutes([new(0,new SavedNavigationRoute(zone,new(a.X,a.Z),0,[new(a.X,a.Z),new(b.X,b.Z)],DateTime.UnixEpoch))]);
            renderer.SetMarkers([new("Player",new(a.X,a.Z),a.Y,Color.White),new("Engaged",new(b.X,b.Z),b.Y,Color.Orange,double.NaN,true)]);
            renderer.SetAnnotations([],[],new(new(a.X,a.Z),0,10,45,Color.Teal));renderer.TopView=false;renderer.MapOpacity=80;
            renderer.SetRadarCamera(new(a.X,a.Z),a.Y,80);var radar=renderer.Capture(new(500,350));
            if(renderer.Failed)return "SKIP: "+renderer.Status;
            Require(radar!=null&&HasGeometry(radar),"Actual local scene must produce a tilted passive radar frame");
            radar!.Save(Path.Combine(outputDirectory,$"native-overlay-zone{zone}-radar.png"));
            renderer.SetAnnotations([],[],null);renderer.FitRoutes(10,100,new(a.X,a.Z),new(a.X,a.Z));var routes=renderer.Capture(new(500,350));
            Require(routes!=null&&HasGeometry(routes),"Actual local routes must fit within a passive terrain overlay");
            routes!.Save(Path.Combine(outputDirectory,$"native-overlay-zone{zone}-routes.png"));
            renderer.SetRadarCamera(new(a.X,a.Z),a.Y,80);var warmedMilliseconds=new List<double>();
            for(int i=0;i<3;i++){var clock=Stopwatch.StartNew();Require(renderer.Capture(new(500,350))!=null,"Warmed local overlay must retain a native frame");warmedMilliseconds.Add(clock.Elapsed.TotalMilliseconds);}
            Require(!renderer.WindowsVisible&&!renderer.AutomaticFrames&&foreground==NavigationOverlay.ForegroundWindow,"Actual local geometry must render without activation or automatic frames");
            results.Add(new{Zone=zone,Terrain=scene.Terrain.Length,Objects=scene.Objects.Length,Artwork=artwork,Hidden=true,ForegroundPreserved=true,WarmedCaptureMilliseconds=warmedMilliseconds,scene.Status});renderer.Clear();
        }
        File.WriteAllText(Path.Combine(outputDirectory,"native-local-overlay-checks.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        return "PASS: actual local Zone8/9/12 passive 3D radar and route readback with artwork, cone, engagement, route corridor and farming area";
    }
    static bool HasGeometry(Bitmap image)
    {int count=0;int background=image.GetPixel(0,0).ToArgb();for(int y=0;y<image.Height;y+=4)for(int x=0;x<image.Width;x+=4)if(image.GetPixel(x,y).ToArgb()!=background&&++count>30)return true;return false;}
    static bool HasPlayerDiamond(Bitmap image)
    {
        int count=0;
        for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)
        {var p=image.GetPixel(x,y);if(p.R>220&&p.G is >70 and <120&&p.B is >100 and <150&&++count>=5)return true;}
        return false;
    }
    static bool Different(Bitmap a,Bitmap b)
    {if(a.Size!=b.Size)return true;for(int y=0;y<a.Height;y+=4)for(int x=0;x<a.Width;x+=4)if(a.GetPixel(x,y).ToArgb()!=b.GetPixel(x,y).ToArgb())return true;return false;}
    static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
