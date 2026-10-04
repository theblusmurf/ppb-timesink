using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PoteHunter;

internal sealed record MapPolyline3D(string Name,Vec[] Points,Color Color,float Width=1);
internal sealed record MapArea3D(Vec Center,double Radius,Color Color,string Name="");
internal sealed record MapCone3D(Vec Position,double Heading,double Range,double HalfAngleDegrees,Color Color);

// A layered, click-through overlay cannot safely host an interactive GL child.
// Render only on the UI thread into an owned bitmap from private hidden HWNDs;
// the existing passive overlay paints that bitmap with its ordinary GDI frame.
internal sealed class NavigationOverlay3DRenderer : IDisposable
{
    readonly Form host;
    readonly Navigation3DView view;
    readonly int threadId=Environment.CurrentManagedThreadId;
    Bitmap? frame;bool disposed,failed,sceneReady;string failure="";
    Vec? radarCenter;double radarHeight,radarRadius;bool fitRoutes;Vec? fitPlayer;
    internal bool Failed=>failed||view.Failed;
    internal string Status=>failed?failure:view.Status;
    internal bool WindowsVisible=>host.Visible||view.Visible;
    internal bool AutomaticFrames=>view.AutomaticFrames;
    internal bool NativeWindowsCreated=>host.IsHandleCreated||view.IsHandleCreated;
    internal (System.Numerics.Vector3 Center,double Distance,double Span) CameraState=>view.CameraState;
    internal Bitmap? Frame=>frame;
    internal int SelectedSlot{get=>view.SelectedSlot;set=>view.SelectedSlot=value;}
    internal bool ShowTerrain{get=>view.ShowTerrain;set=>view.ShowTerrain=value;}
    internal bool ShowObjects{get=>view.ShowObjects;set=>view.ShowObjects=value;}
    internal bool ShowRoutes{get=>view.ShowRoutes;set=>view.ShowRoutes=value;}
    internal bool ShowAnchors{get=>view.ShowAnchors;set=>view.ShowAnchors=value;}
    internal bool ShowMapArtwork{get=>view.ShowMapArtwork;set=>view.ShowMapArtwork=value;}
    internal int MapOpacity{get=>view.MapOpacity;set=>view.MapOpacity=value;}
    internal bool TopView{get=>view.TopView;set=>view.TopView=value;}
    internal NavigationOverlay3DRenderer()
    {
        host=new Form{AutoScaleMode=AutoScaleMode.None,ClientSize=new(450,450),ShowInTaskbar=false,FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,Location=new(-32000,-32000)};
        view=new Navigation3DView(passive:true){Dock=DockStyle.Fill,Visible=false,TabStop=false};host.Controls.Add(view);
    }
    // The independent clone is transferred to this renderer, including on failure.
    internal void SetScene(MapScene3D? scene,Bitmap? ownedArtworkClone,GameMapLayout.Extent? bounds)
    {
        CheckThread();sceneReady=scene?.Terrain.Length>0;view.SetScene(scene);view.SetMapArtwork(ownedArtworkClone,bounds);DropFrame();
    }
    internal void Clear(){CheckThread();sceneReady=false;view.SetScene(null);view.SetMapArtwork(null,null);view.SetRoutes([]);view.SetMarkers([]);view.SetAnnotations([],[],null);radarCenter=null;fitRoutes=false;DropFrame();}
    internal void SetRoutes(MapRoute3D[] routes){CheckThread();view.SetRoutes(routes);}
    internal void SetMarkers(MapMarker3D[] markers){CheckThread();view.SetMarkers(markers);}
    internal void SetAnnotations(MapPolyline3D[] lines,MapArea3D[] areas,MapCone3D? cone){CheckThread();view.SetAnnotations(lines,areas,cone);}
    internal void SetRouteRadii(double corridorRadius,double? farmingRadius=null,Vec? farmingAnchor=null){CheckThread();view.SetRouteRadii(corridorRadius,farmingRadius,farmingAnchor);}
    internal void SetRadarCamera(Vec center,double height,double radius)
    {CheckThread();radarCenter=center;radarHeight=height;radarRadius=radius;fitRoutes=false;}
    internal void FitRoutes(double corridorRadius,double? farmingRadius=null,Vec? farmingAnchor=null,Vec? player=null)
    {CheckThread();view.SetRouteRadii(corridorRadius,farmingRadius,farmingAnchor);fitRoutes=true;fitPlayer=player;radarCenter=null;}
    internal static Size BoundedSize(Size requested)
    {
        if(requested.Width<1||requested.Height<1)return Size.Empty;
        double scale=Math.Min(1,900.0/Math.Max(requested.Width,requested.Height));
        return new(Math.Max(1,(int)Math.Round(requested.Width*scale)),Math.Max(1,(int)Math.Round(requested.Height*scale)));
    }
    // The bitmap remains renderer-owned, valid until the next capture/clear/dispose.
    internal Bitmap? Capture(Size requested)
    {
        CheckThread();if(Failed||!sceneReady)return null;var size=BoundedSize(requested);if(size.IsEmpty)return null;
        try
        {
            host.ClientSize=size;view.Size=size;_=host.Handle;_=view.Handle;
            if(view.Failed){DropFrame();return null;}
            if(fitRoutes)view.FitPassiveRoutes(fitPlayer);
            else if(radarCenter is Vec point)view.SetPassiveRadarCamera(point,radarHeight,radarRadius);
            var next=view.CaptureFrame();if(next==null){DropFrame();return null;}
            DropFrame();frame=next;return frame;
        }
        catch(Exception error) when(error is InvalidOperationException or ExternalException or OutOfMemoryException or Win32Exception)
        {failed=true;failure="3D overlay unavailable · "+error.Message;DropFrame();return null;}
    }
    void CheckThread(){ObjectDisposedException.ThrowIf(disposed,this);if(Environment.CurrentManagedThreadId!=threadId)throw new InvalidOperationException("Use the 3D overlay renderer on its UI thread");}
    void DropFrame(){frame?.Dispose();frame=null;}
    public void Dispose(){if(disposed)return;CheckThread();disposed=true;DropFrame();host.Dispose();}
}
