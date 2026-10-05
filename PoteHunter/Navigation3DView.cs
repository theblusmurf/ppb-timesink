using System.Diagnostics;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace PoteHunter;

// Read-only native map viewport. Its HWND/DC belong only to this control. No
// client window, game input, movement policy, or navigation state is changed.
internal sealed class Navigation3DView : Control
{
    const double CellStep=MapSceneGeometry.CellStep;
    readonly System.Windows.Forms.Timer frameTimer=new(){Interval=34};
    readonly Stopwatch frameClock=Stopwatch.StartNew();
    IntPtr dc,context;uint terrainList,artworkList,objectList,routeList,fontLists,texture;
    uint[] meshLists=[];MapScene3D? scene;MapRoute3D[] routes=[];MapMarker3D[] markers=[];
    Bitmap? artwork;GameMapLayout.Extent? artworkBounds;
    bool sceneDirty=true,artworkDirty=true,routesDirty=true,fontDirty=true,pending=true,disposed;
    bool showTerrain=true,showObjects=true,showRoutes=true,showAnchors=true,showMapArtwork=true,topView,passiveProjection;
    readonly bool passive;
    double corridorRadius;double? farmingRadius;Vec? farmingAnchor;
    MapPolyline3D[] annotations=[];MapArea3D[] areas=[];MapCone3D? cone;
    int mapOpacity=70,selectedSlot;double yaw=.5,pitch=.78,distance=1400,span=1200;
    Vector3 center=new(1800,0,-1800);Point dragStart;MouseButtons dragButton;
    string status="3D map ready";long lastFrame=-100;string renderer="";
    internal bool Failed {get;private set;}
    internal bool AutomaticFrames=>frameTimer.Enabled;
    internal (Vector3 Center,double Distance,double Span) CameraState=>(center,distance,span);
    internal string Status=>status;
    internal event Action<string>? StatusChanged;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowTerrain{get=>showTerrain;set{if(showTerrain!=value){showTerrain=value;RequestRender();}}}
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowObjects{get=>showObjects;set{if(showObjects!=value){showObjects=value;RequestRender();}}}
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowRoutes{get=>showRoutes;set{if(showRoutes!=value){showRoutes=value;RequestRender();}}}
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowAnchors{get=>showAnchors;set{if(showAnchors!=value){showAnchors=value;RequestRender();}}}
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ShowMapArtwork{get=>showMapArtwork;set{if(showMapArtwork!=value){showMapArtwork=value;RequestRender();}}}
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int MapOpacity{get=>mapOpacity;set{int v=Math.Clamp(value,0,100);if(mapOpacity!=v){mapOpacity=v;RequestRender();}}}
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int SelectedSlot{get=>selectedSlot;set{int v=Math.Clamp(value,0,2);if(selectedSlot!=v){selectedSlot=v;routesDirty=true;RequestRender();}}}
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool TopView{get=>topView;set{if(topView!=value){topView=value;RequestRender();}}}

    internal Navigation3DView(bool passive=false)
    {
        this.passive=passive;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.Opaque|ControlStyles.Selectable,true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer,false);SetStyle(ControlStyles.Selectable,!passive);TabStop=!passive;BackColor=Color.FromArgb(9,20,30);
        AccessibleName="3D navigation map";AccessibleDescription="Read-only map. Drag to orbit, right-drag to pan, mouse wheel to zoom. Top view faces north.";
        frameTimer.Tick+=(_,_)=>{if(!Visible||disposed||!pending||Failed){frameTimer.Stop();return;}Invalidate();};
    }
    protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ClassStyle|=0x23;return p;}}

    internal void SetScene(MapScene3D? value)
    {
        if(ReferenceEquals(scene,value))return;
        bool refit=scene==null||value==null||scene.Zone!=value.Zone||scene.Source!=value.Source;
        scene=value;sceneDirty=artworkDirty=routesDirty=true;
        if(refit&&scene!=null)FitMap();else RequestRender();
        SetStatus(scene?.Status??"No verified 3D terrain for this map");
    }
    // Caller transfers an independent image clone; this control owns its lifetime.
    internal void SetMapArtwork(Bitmap? ownedClone,GameMapLayout.Extent? bounds)
    {
        if(ReferenceEquals(artwork,ownedClone)&&artworkBounds==bounds)return;
        if(!ReferenceEquals(artwork,ownedClone))artwork?.Dispose();artwork=ownedClone;artworkBounds=bounds;artworkDirty=true;RequestRender();
    }
    internal void SetRoutes(MapRoute3D[] value)
    {
        value??=[];
        if(routes.Length==value.Length&&routes.Zip(value).All(p=>p.First.Slot==p.Second.Slot&&ReferenceEquals(p.First.Route,p.Second.Route)))return;
        routes=value.ToArray();routesDirty=true;RequestRender();
    }
    internal void SetMarkers(MapMarker3D[] value){markers=(value??[]).ToArray();RequestRender();}
    internal void SetAnnotations(MapPolyline3D[] lines,MapArea3D[] rings,MapCone3D? direction)
    {
        annotations=(lines??[]).ToArray();areas=(rings??[]).ToArray();cone=direction;RequestRender();
    }
    internal void SetRouteRadii(double corridor,double? farming,Vec? anchor)
    {
        double radius=double.IsFinite(corridor)?Math.Clamp(corridor,0,2000):0;
        double? area=farming is >0 and <=2000?farming:null;
        Vec? home=anchor is Vec p&&p.Finite?p:null;
        if(corridorRadius==radius&&farmingRadius==area&&farmingAnchor==home)return;
        corridorRadius=radius;farmingRadius=area;farmingAnchor=home;routesDirty=true;RequestRender();
    }
    internal void SetPassiveRadarCamera(Vec point,double height,double radius)
    {
        if(!point.Finite||!double.IsFinite(height)||!double.IsFinite(radius))return;
        passiveProjection=true;yaw=0;pitch=1.10;center=Reflect(new((float)point.X,(float)height,(float)point.Y));
        span=Math.Clamp(radius,10,2000)*2;
        double aspect=ClientSize.Height>0?(double)ClientSize.Width/ClientSize.Height:1;
        distance=Math.Clamp(span*.5*1.12/.41421356237/Math.Min(1,Math.Max(.2,aspect)),3,50000);RequestRender();
    }
    internal void FitPassiveRoutes(Vec? player)
    {
        passiveProjection=true;yaw=0;pitch=1.10;
        var points=routes.SelectMany(r=>r.Route.Points.Append(r.Route.Anchor)).ToList();
        if(player is Vec p&&p.Finite)points.Add(p);
        var positions=points.Where(p=>p.Finite).Select(p=>(p,h:MapSceneGeometry.Height(scene,p))).Where(p=>p.h.HasValue)
            .Select(p=>Reflect(new((float)p.p.X,(float)p.h!.Value,(float)p.p.Y))).ToArray();
        if(positions.Length==0){FitMap();return;}
        var min=positions.Aggregate(Vector3.Min);var max=positions.Aggregate(Vector3.Max);
        center=(min+max)/2;double margin=Math.Max(12,corridorRadius+2);
        double width=max.X-min.X+margin*2,depth=max.Z-min.Z+margin*2,height=max.Y-min.Y;
        double aspect=ClientSize.Height>0?(double)ClientSize.Width/ClientSize.Height:1;
        double halfSpan=Math.Max(width/Math.Max(.2,aspect),topView?depth:depth*Math.Sin(pitch)+height*Math.Cos(pitch))*.5;
        span=Math.Max(width,Math.Max(depth,height));distance=Math.Clamp(halfSpan*1.06/.41421356237,3,50000);RequestRender();
    }
    internal void CenterOn(Vec point,double height)
    {
        if(!point.Finite||!double.IsFinite(height))return;
        center=Reflect(new((float)point.X,(float)height,(float)point.Y));RequestRender();
    }
    internal void FocusAnchor(int slot)
    {
        var route=routes.FirstOrDefault(r=>r.Slot==slot);if(route==null){SetStatus("No saved anchor in this route slot");return;}
        var point=route.Route.Anchor;double? h=MapSceneGeometry.Height(scene,point);
        if(h==null){SetStatus("Saved anchor is outside verified terrain; use the 2D map");return;}
        selectedSlot=slot;routesDirty=true;CenterOn(point,h.Value);distance=Math.Clamp(distance,20,300);
        SetStatus($"{SlotName(slot)} anchor · {point.X:0.0}, {point.Y:0.0}");RequestRender();
    }
    internal void FitMap()
    {
        if(!TryBounds(out var min,out var max)){SetStatus("No verified scene geometry to fit");return;}
        Fit(min,max);SetStatus(scene?.Status??"3D map");
    }
    internal void FitRoutes()
    {
        var points=routes.SelectMany(r=>r.Route.Points.Append(r.Route.Anchor)).Where(p=>p.Finite)
            .Select(p=>(p,h:MapSceneGeometry.Height(scene,p))).Where(p=>p.h.HasValue)
            .Select(p=>Reflect(new((float)p.p.X,(float)p.h!.Value,(float)p.p.Y))).ToArray();
        if(points.Length==0){SetStatus("No saved route lies on verified terrain");return;}
        var min=points.Aggregate(Vector3.Min);var max=points.Aggregate(Vector3.Max);
        Fit(min,max);SetStatus("Saved routes fitted · gaps without terrain are omitted");
    }
    void Fit(Vector3 min,Vector3 max)
    {
        center=(min+max)/2;span=Math.Max(10,(max-min).Length());
        double aspect=ClientSize.Height>0?(double)ClientSize.Width/ClientSize.Height:1;
        distance=Math.Clamp(span*.6/Math.Sin(Math.PI/8)/Math.Min(1,Math.Max(.2,aspect)),20,50000);RequestRender();
    }
    internal static Vector3 Reflect(Vector3 p)=>new(p.X,p.Y,-p.Z);
    internal static PointF ArtworkUv(Vector3 p,GameMapLayout.Extent b)=>new((float)((p.X-b.MinX)/(b.MaxX-b.MinX)),(float)((b.MaxY-p.Z)/(b.MaxY-b.MinY)));
    static string SlotName(int slot)=>slot switch{0=>"Primary",1=>"Alternative 1",_=>"Alternative 2"};
    static Color SlotColor(int slot)=>slot switch{0=>Color.FromArgb(231,188,112),1=>Color.FromArgb(116,176,228),_=>Color.FromArgb(217,142,162)};
    void SetStatus(string value){if(Failed&&context==IntPtr.Zero&&!value.StartsWith("3D unavailable",StringComparison.Ordinal))return;if(status==value)return;status=value;StatusChanged?.Invoke(value);}
    void RequestRender()
    {
        if(disposed)return;pending=true;
        if(!passive&&IsHandleCreated&&Visible&&!Failed){Invalidate();if(!frameTimer.Enabled)frameTimer.Start();}
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);Failed=false;
        try
        {
            dc=Native.GetDC(Handle);if(dc==IntPtr.Zero)throw new InvalidOperationException("Window device context could not be created");
            var pfd=PixelFormat.Desired();int format=Native.ChoosePixelFormat(dc,ref pfd);
            if(format==0||!Native.SetPixelFormat(dc,format,ref pfd))throw new InvalidOperationException("OpenGL pixel format is unsupported");
            var actual=new PixelFormat();if(Native.DescribePixelFormat(dc,format,(uint)Marshal.SizeOf<PixelFormat>(),ref actual)==0)throw new InvalidOperationException("Pixel format could not be verified");
            if((actual.Flags&0x40)!=0&&(actual.Flags&0x1000)==0)throw new InvalidOperationException("A hardware 3D driver is unavailable");
            context=Native.wglCreateContext(dc);if(context==IntPtr.Zero||!Native.wglMakeCurrent(dc,context))throw new InvalidOperationException("OpenGL context is unsupported");
            renderer=Marshal.PtrToStringAnsi(GL.glGetString(0x1F01))??"OpenGL";
            GL.glClearColor(9f/255,20f/255,30f/255,1);GL.glEnable(GL.DEPTH_TEST);GL.glEnable(GL.NORMALIZE);
            GL.glEnable(GL.COLOR_MATERIAL);GL.glColorMaterial(GL.FRONT_AND_BACK,GL.AMBIENT_AND_DIFFUSE);
            GL.glEnable(GL.LIGHT0);GL.glLightfv(GL.LIGHT0,GL.DIFFUSE,[.78f,.81f,.8f,1]);
            GL.glLightModelfv(GL.LIGHT_MODEL_AMBIENT,[.4f,.4f,.43f,1]);
            GL.glDepthFunc(GL.LEQUAL);GL.glDisable(GL.CULL_FACE);GL.glHint(GL.PERSPECTIVE_CORRECTION_HINT,GL.NICEST);
            sceneDirty=artworkDirty=routesDirty=fontDirty=true;RequestRender();
            SetStatus(scene?.Status??$"3D ready · {renderer}");
        }
        catch(Exception error) when(error is InvalidOperationException or ExternalException or DllNotFoundException or EntryPointNotFoundException)
        {Fail(error.Message);}
        finally{Native.wglMakeCurrent(IntPtr.Zero,IntPtr.Zero);}
    }
    void Fail(string reason)
    {
        Failed=true;pending=false;frameTimer.Stop();ReleaseContext();
        status="3D unavailable · "+reason+". The 2D map remains available.";StatusChanged?.Invoke(status);if(!passive)Invalidate();
    }
    protected override void OnHandleDestroyed(EventArgs e){frameTimer.Stop();ReleaseContext();base.OnHandleDestroyed(e);}
    protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(Visible)RequestRender();else frameTimer.Stop();}
    protected override void OnResize(EventArgs e){base.OnResize(e);RequestRender();}
    protected override void OnDpiChangedAfterParent(EventArgs e){base.OnDpiChangedAfterParent(e);fontDirty=true;RequestRender();}
    protected override void OnPaintBackground(PaintEventArgs e){ }
    protected override void OnPaint(PaintEventArgs e)
    {
        if(Failed||context==IntPtr.Zero)
        {e.Graphics.Clear(BackColor);TextRenderer.DrawText(e.Graphics,status,Font,ClientRectangle,Color.FromArgb(184,190,192),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);return;}
        if(!passive&&frameClock.ElapsedMilliseconds-lastFrame<34){pending=true;frameTimer.Start();return;}
        try{Render(true);pending=false;frameTimer.Stop();lastFrame=frameClock.ElapsedMilliseconds;}
        catch(Exception error) when(error is InvalidOperationException or ExternalException or OutOfMemoryException){Fail(error.Message);}
    }
    internal Bitmap? CaptureFrame()
    {
        if(Failed||!IsHandleCreated||context==IntPtr.Zero||ClientSize.Width<1||ClientSize.Height<1)return null;
        if(InvokeRequired)throw new InvalidOperationException("Capture the 3D map on its UI thread");
        Render(false);if(!Native.wglMakeCurrent(dc,context))return null;
        try
        {
            int w=ClientSize.Width,h=ClientSize.Height;var rgb=new byte[checked(w*h*4)];var pinned=GCHandle.Alloc(rgb,GCHandleType.Pinned);
            try{GL.glReadBuffer(GL.BACK);GL.glPixelStorei(GL.PACK_ALIGNMENT,1);GL.glReadPixels(0,0,w,h,GL.RGBA,GL.UNSIGNED_BYTE,pinned.AddrOfPinnedObject());GL.glFinish();}
            finally{pinned.Free();}
            var image=new Bitmap(w,h,PixelFormat32);var bits=image.LockBits(new Rectangle(0,0,w,h),ImageLockMode.WriteOnly,PixelFormat32);
            try
            {
                var row=new byte[w*4];for(int y=0;y<h;y++){for(int x=0;x<w;x++){int a=((h-y-1)*w+x)*4,b=x*4;row[b]=rgb[a+2];row[b+1]=rgb[a+1];row[b+2]=rgb[a];row[b+3]=255;}Marshal.Copy(row,0,bits.Scan0+y*bits.Stride,row.Length);}
            }
            finally{image.UnlockBits(bits);}return image;
        }
        finally{Native.wglMakeCurrent(IntPtr.Zero,IntPtr.Zero);}
    }
    const System.Drawing.Imaging.PixelFormat PixelFormat32=System.Drawing.Imaging.PixelFormat.Format32bppArgb;
    void Render(bool swap)
    {
        if(!Native.wglMakeCurrent(dc,context))throw new InvalidOperationException("3D context was lost");
        try
        {
            if(sceneDirty)BuildScene();if(artworkDirty)BuildArtwork();if(routesDirty)BuildRoutes();if(fontDirty)BuildFont();
            int w=Math.Max(1,ClientSize.Width),h=Math.Max(1,ClientSize.Height);GL.glViewport(0,0,w,h);GL.glClear(GL.COLOR_BUFFER_BIT|GL.DEPTH_BUFFER_BIT);
            GL.glMatrixMode(GL.PROJECTION);GL.glLoadIdentity();double near=Math.Max(.05,distance/10000),far=distance+span*5+10000;
            if(topView||passiveProjection){double sy=distance*.41421356237;GL.glOrtho(-sy*w/h,sy*w/h,-sy,sy,near,far);}
            else{double sy=near*Math.Tan(Math.PI/8);GL.glFrustum(-sy*w/h,sy*w/h,-sy,sy,near,far);}
            Camera(out var eye,out var right,out var up);var forward=Vector3.Normalize(center-eye);
            GL.glMatrixMode(GL.MODELVIEW);GL.glLoadMatrixd([right.X,up.X,-forward.X,0,right.Y,up.Y,-forward.Y,0,right.Z,up.Z,-forward.Z,0,-Vector3.Dot(right,eye),-Vector3.Dot(up,eye),Vector3.Dot(forward,eye),1]);
            GL.glLightfv(GL.LIGHT0,GL.POSITION,[-.3f,.8f,.45f,0]);GL.glEnable(GL.LIGHTING);GL.glDepthMask(true);
            if(showTerrain&&terrainList!=0)GL.glCallList(terrainList);
            if(showMapArtwork&&mapOpacity>0&&artworkList!=0&&texture!=0)
            {
                GL.glDisable(GL.LIGHTING);GL.glEnable(GL.TEXTURE_2D);GL.glBindTexture(GL.TEXTURE_2D,texture);GL.glEnable(GL.BLEND);GL.glBlendFunc(GL.SRC_ALPHA,GL.ONE_MINUS_SRC_ALPHA);
                GL.glDepthMask(false);GL.glEnable(GL.POLYGON_OFFSET_FILL);GL.glPolygonOffset(-1,-1);GL.glColor4ub(255,255,255,(byte)(mapOpacity*255/100));GL.glCallList(artworkList);
                GL.glDisable(GL.POLYGON_OFFSET_FILL);GL.glDepthMask(true);GL.glDisable(GL.BLEND);GL.glDisable(GL.TEXTURE_2D);GL.glEnable(GL.LIGHTING);
            }
            if(showObjects&&objectList!=0)GL.glCallList(objectList);
            GL.glDisable(GL.LIGHTING);GL.glDisable(GL.TEXTURE_2D);
            if(showRoutes&&routeList!=0){if(passive)GL.glDisable(GL.DEPTH_TEST);GL.glCallList(routeList);if(passive)GL.glEnable(GL.DEPTH_TEST);}
            if(showAnchors)DrawAnchors();DrawAnnotations();DrawMarkers();GL.glFlush();
            if(swap&&!Native.SwapBuffers(dc))throw new InvalidOperationException("3D frame could not be displayed");
        }
        finally{Native.wglMakeCurrent(IntPtr.Zero,IntPtr.Zero);}
    }
    void Camera(out Vector3 eye,out Vector3 right,out Vector3 up)
    {
        if(topView){eye=center+new Vector3(0,(float)distance,0);right=Vector3.UnitX;up=-Vector3.UnitZ;return;}
        eye=center+new Vector3((float)(Math.Sin(yaw)*Math.Cos(pitch)*distance),(float)(Math.Sin(pitch)*distance),(float)(Math.Cos(yaw)*Math.Cos(pitch)*distance));
        var f=Vector3.Normalize(center-eye);right=Vector3.Normalize(Vector3.Cross(f,Vector3.UnitY));up=Vector3.Cross(right,f);
    }
    bool TryBounds(out Vector3 min,out Vector3 max)
    {
        min=new(float.PositiveInfinity);max=new(float.NegativeInfinity);bool found=false;
        if(scene==null)return false;
        foreach(var patch in scene.Terrain)foreach(var v in patch.Vertices){var p=Reflect(v);min=Vector3.Min(min,p);max=Vector3.Max(max,p);found=true;}
        var meshBounds=scene.Meshes.Select(m=>(min:m.Vertices.Length==0?Vector3.Zero:m.Vertices.Aggregate(Vector3.Min),max:m.Vertices.Length==0?Vector3.Zero:m.Vertices.Aggregate(Vector3.Max))).ToArray();
        foreach(var item in scene.Objects)
        {
            if(item.Mesh<0||item.Mesh>=meshBounds.Length)continue;var b=meshBounds[item.Mesh];
            for(int i=0;i<8;i++){var v=new Vector3((i&1)==0?b.min.X:b.max.X,(i&2)==0?b.min.Y:b.max.Y,(i&4)==0?b.min.Z:b.max.Z);var p=Reflect(Vector3.Transform(v,item.Transform));min=Vector3.Min(min,p);max=Vector3.Max(max,p);found=true;}
        }
        return found;
    }
    void BuildScene()
    {
        DeleteList(ref terrainList);DeleteList(ref objectList);foreach(var id in meshLists)if(id!=0)GL.glDeleteLists(id,1);meshLists=[];
        if(scene!=null)
        {
            var v=new List<Vector3>();var indices=new List<uint>();var colors=new List<byte>();
            float min=scene.Terrain.SelectMany(p=>p.Vertices).Select(p=>p.Y).DefaultIfEmpty(0).Min(),max=scene.Terrain.SelectMany(p=>p.Vertices).Select(p=>p.Y).DefaultIfEmpty(0).Max();
            foreach(var patch in scene.Terrain)
            {
                if(patch.Vertices.Length!=289)continue;uint start=(uint)v.Count;
                foreach(var point in patch.Vertices){v.Add(Reflect(point));var c=ElevationColor((point.Y-min)/Math.Max(1,max-min));colors.Add(c.R);colors.Add(c.G);colors.Add(c.B);colors.Add(255);}
                for(uint row=0;row<16;row++)for(uint col=0;col<16;col++){uint a=start+row*17+col,b=a+1,c=a+17,d=c+1;indices.AddRange([a,b,c,b,d,c]);}
            }
            if(v.Count>0)terrainList=ArrayList(v.ToArray(),indices.ToArray(),colors.ToArray());
            meshLists=new uint[scene.Meshes.Length];
            for(int i=0;i<meshLists.Length;i++)
            {
                var mesh=scene.Meshes[i];if(mesh.Vertices.Length==0||mesh.Indices.Length==0)continue;
                if(mesh.Indices.Any(x=>x<0||x>=mesh.Vertices.Length))throw new InvalidOperationException("3D mesh index is invalid");
                meshLists[i]=ArrayList(mesh.Vertices,mesh.Indices.Select(x=>(uint)x).ToArray(),null);
            }
            objectList=NewList();GL.glNewList(objectList,GL.COMPILE);GL.glPushMatrix();GL.glScaled(1,1,-1);
            foreach(var item in scene.Objects)
            {
                if(item.Mesh<0||item.Mesh>=meshLists.Length||meshLists[item.Mesh]==0)continue;
                string name=scene.Meshes[item.Mesh].Name;var c=name.Contains("stone",StringComparison.OrdinalIgnoreCase)?Color.FromArgb(159,161,157):Color.FromArgb(161,145,111);
                GL.glColor4ub(c.R,c.G,c.B,255);GL.glPushMatrix();GL.glMultMatrixf(MatrixValues(item.Transform));GL.glCallList(meshLists[item.Mesh]);GL.glPopMatrix();
            }
            GL.glPopMatrix();GL.glEndList();
        }
        sceneDirty=false;
    }
    static float[] MatrixValues(Matrix4x4 m)=>[m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
    static Color ElevationColor(float t)
    {
        t=Math.Clamp(t,0,1);var a=t<.55f?Color.FromArgb(41,79,81):Color.FromArgb(114,124,100);var b=t<.55f?Color.FromArgb(114,124,100):Color.FromArgb(214,211,189);float u=t<.55f?t/.55f:(t-.55f)/.45f;
        return Color.FromArgb((int)(a.R+(b.R-a.R)*u),(int)(a.G+(b.G-a.G)*u),(int)(a.B+(b.B-a.B)*u));
    }
    uint ArrayList(Vector3[] vertices,uint[] indices,byte[]? colors)
    {
        var positions=new float[vertices.Length*3];var normals=new Vector3[vertices.Length];
        for(int i=0;i<vertices.Length;i++){positions[i*3]=vertices[i].X;positions[i*3+1]=vertices[i].Y;positions[i*3+2]=vertices[i].Z;}
        for(int i=0;i+2<indices.Length;i+=3){uint a=indices[i],b=indices[i+1],c=indices[i+2];var n=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);if(float.IsFinite(n.LengthSquared())){normals[a]+=n;normals[b]+=n;normals[c]+=n;}}
        var normalData=new float[positions.Length];for(int i=0;i<normals.Length;i++){var n=normals[i].LengthSquared()>1e-12?Vector3.Normalize(normals[i]):Vector3.UnitY;normalData[i*3]=n.X;normalData[i*3+1]=n.Y;normalData[i*3+2]=n.Z;}
        var pins=new List<GCHandle>();uint id=NewList();
        IntPtr Pin(Array data){var p=GCHandle.Alloc(data,GCHandleType.Pinned);pins.Add(p);return p.AddrOfPinnedObject();}
        try
        {
            GL.glEnableClientState(GL.VERTEX_ARRAY);GL.glVertexPointer(3,GL.FLOAT,0,Pin(positions));GL.glEnableClientState(GL.NORMAL_ARRAY);GL.glNormalPointer(GL.FLOAT,0,Pin(normalData));
            if(colors!=null){GL.glEnableClientState(GL.COLOR_ARRAY);GL.glColorPointer(4,GL.UNSIGNED_BYTE,0,Pin(colors));}else GL.glDisableClientState(GL.COLOR_ARRAY);
            IntPtr indexPtr=Pin(indices);GL.glNewList(id,GL.COMPILE);GL.glDrawElements(GL.TRIANGLES,indices.Length,GL.UNSIGNED_INT,indexPtr);GL.glEndList();
            if(GL.glGetError()!=0)throw new InvalidOperationException("3D geometry could not be cached by the graphics driver");
            return id;
        }
        catch{GL.glDeleteLists(id,1);throw;}
        finally{GL.glDisableClientState(GL.VERTEX_ARRAY);GL.glDisableClientState(GL.NORMAL_ARRAY);GL.glDisableClientState(GL.COLOR_ARRAY);foreach(var pin in pins)pin.Free();}
    }
    void BuildArtwork()
    {
        DeleteList(ref artworkList);if(texture!=0){GL.glDeleteTextures(1,ref texture);texture=0;}
        artworkDirty=false;if(scene==null||artwork==null||artworkBounds is not {} bounds||bounds.MaxX<=bounds.MinX||bounds.MaxY<=bounds.MinY)return;
        int Pot(int n){int p=1;while(p<Math.Min(n,2048))p*=2;return p;}
        using var bitmap=new Bitmap(Pot(artwork.Width),Pot(artwork.Height),PixelFormat32);using(var g=Graphics.FromImage(bitmap))g.DrawImage(artwork,new Rectangle(0,0,bitmap.Width,bitmap.Height));
        var bits=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat32);var rgba=new byte[bitmap.Width*bitmap.Height*4];
        try{var line=new byte[bitmap.Width*4];for(int row=0;row<bitmap.Height;row++){Marshal.Copy(bits.Scan0+row*bits.Stride,line,0,line.Length);for(int col=0;col<bitmap.Width;col++){int a=col*4,b=(row*bitmap.Width+col)*4;rgba[b]=line[a+2];rgba[b+1]=line[a+1];rgba[b+2]=line[a];rgba[b+3]=line[a+3];}}}
        finally{bitmap.UnlockBits(bits);}
        GL.glGenTextures(1,out texture);GL.glBindTexture(GL.TEXTURE_2D,texture);GL.glTexParameteri(GL.TEXTURE_2D,GL.TEXTURE_MIN_FILTER,GL.LINEAR);GL.glTexParameteri(GL.TEXTURE_2D,GL.TEXTURE_MAG_FILTER,GL.LINEAR);GL.glTexParameteri(GL.TEXTURE_2D,GL.TEXTURE_WRAP_S,GL.CLAMP);GL.glTexParameteri(GL.TEXTURE_2D,GL.TEXTURE_WRAP_T,GL.CLAMP);
        var pinned=GCHandle.Alloc(rgba,GCHandleType.Pinned);try{GL.glPixelStorei(GL.UNPACK_ALIGNMENT,1);GL.glTexImage2D(GL.TEXTURE_2D,0,GL.RGBA,bitmap.Width,bitmap.Height,0,GL.RGBA,GL.UNSIGNED_BYTE,pinned.AddrOfPinnedObject());}finally{pinned.Free();}
        artworkList=NewList();GL.glNewList(artworkList,GL.COMPILE);GL.glBegin(GL.TRIANGLES);
        foreach(var patch in scene.Terrain)
        {
            if(patch.Vertices.Length!=289)continue;
            for(int row=0;row<16;row++)for(int col=0;col<16;col++)
            {
                int a=row*17+col,b=a+1,c=a+17,d=c+1;
                foreach(var triangle in new[]{new[]{patch.Vertices[a],patch.Vertices[b],patch.Vertices[c]},new[]{patch.Vertices[b],patch.Vertices[d],patch.Vertices[c]}})
                {
                    var polygon=ClipArtworkTriangle(triangle,bounds);for(int i=1;i+1<polygon.Length;i++)foreach(var v in new[]{polygon[0],polygon[i],polygon[i+1]}){var uv=ArtworkUv(v,bounds);GL.glTexCoord2f(uv.X,uv.Y);GL.glVertex3d(v.X,v.Y+.35,-v.Z);}
                }
            }
        }
        GL.glEnd();GL.glEndList();if(GL.glGetError()!=0)throw new InvalidOperationException("Map artwork could not be drawn by the graphics driver");
    }
    internal static Vector3[] ClipArtworkTriangle(Vector3[] triangle,GameMapLayout.Extent bounds)
    {
        var polygon=triangle.ToList();
        foreach(int edge in Enumerable.Range(0,4))
        {
            var next=new List<Vector3>();if(polygon.Count==0)break;
            double Distance(Vector3 v)=>edge switch{0=>v.X-bounds.MinX,1=>bounds.MaxX-v.X,2=>v.Z-bounds.MinY,_=>bounds.MaxY-v.Z};
            var previous=polygon[^1];double previousDistance=Distance(previous);
            foreach(var current in polygon)
            {
                double currentDistance=Distance(current);bool a=previousDistance>=0,b=currentDistance>=0;
                if(a!=b){double t=previousDistance/(previousDistance-currentDistance);next.Add(Vector3.Lerp(previous,current,(float)t));}
                if(b)next.Add(current);previous=current;previousDistance=currentDistance;
            }
            polygon=next;
        }
        return polygon.ToArray();
    }
    void BuildRoutes()
    {
        DeleteList(ref routeList);routesDirty=false;routeList=NewList();GL.glNewList(routeList,GL.COMPILE);
        foreach(var item in routes)
        {
            var c=SlotColor(item.Slot);if(corridorRadius>0&&RecoveryTravel.Recorded(item.Route))DrawCorridor(item.Route.Points,c);
            GL.glColor4ub(c.R,c.G,c.B,255);GL.glLineWidth(item.Slot==selectedSlot?3:2);
            foreach(var part in DrapeRoute(scene,item.Route.Points)){GL.glBegin(GL.LINE_STRIP);foreach(var point in part)GL.glVertex3d(point.X,point.Y+.8,-point.Z);GL.glEnd();}
        }
        GL.glLineWidth(1);GL.glEndList();
    }
    void DrawCorridor(Vec[] points,Color color)
    {
        GL.glEnable(GL.BLEND);GL.glBlendFunc(GL.SRC_ALPHA,GL.ONE_MINUS_SRC_ALPHA);GL.glDepthMask(false);
        GL.glColor4ub(color.R,color.G,color.B,45);
        foreach(var part in DrapeRoute(scene,points))
        {
            (Vector3 Left,Vector3 Right)? previous=null;
            for(int i=0;i<part.Length;i++)
            {
                var before=part[Math.Max(0,i-1)];var after=part[Math.Min(part.Length-1,i+1)];
                var direction=new Vec(after.X-before.X,after.Z-before.Z);
                if(direction.Length<=1e-6){previous=null;continue;}
                var side=new Vec(-direction.Y,direction.X)*(corridorRadius/direction.Length);
                var point=new Vec(part[i].X,part[i].Z);var left=point+side;var right=point-side;
                double? lh=MapSceneGeometry.Height(scene,left),rh=MapSceneGeometry.Height(scene,right);
                if(lh==null||rh==null){previous=null;continue;}
                var section=(Left:new Vector3((float)left.X,(float)lh.Value+.6f,(float)left.Y),Right:new Vector3((float)right.X,(float)rh.Value+.6f,(float)right.Y));
                if(previous is {} last)
                {
                    GL.glBegin(GL.TRIANGLES);foreach(var v in new[]{last.Left,last.Right,section.Left,last.Right,section.Right,section.Left})GL.glVertex3d(v.X,v.Y,-v.Z);GL.glEnd();
                }
                previous=section;
            }
        }
        GL.glDepthMask(true);GL.glDisable(GL.BLEND);
    }
    internal static Vector3[][] DrapeRoute(MapScene3D? data,Vec[] points)
    {
        var parts=new List<Vector3[]>();var current=new List<Vector3>();
        void Sample(Vec point)
        {
            var h=MapSceneGeometry.Height(data,point);
            if(h==null){if(current.Count>1)parts.Add(current.ToArray());current.Clear();return;}
            current.Add(new((float)point.X,(float)h.Value,(float)point.Y));
        }
        if(points.Length==0)return [];
        Sample(points[0]);
        for(int i=1;i<points.Length;i++)
        {
            if(!points[i-1].Finite||!points[i].Finite){Sample(new(double.NaN,double.NaN));continue;}
            // Sample inside each saved segment as well as at its endpoints, so
            // lines cannot bridge an unverified missing terrain tile.
            int steps=Math.Clamp((int)Math.Ceiling((points[i]-points[i-1]).Length/(CellStep*2)),1,4096);
            for(int j=1;j<=steps;j++)Sample(points[i-1]+(points[i]-points[i-1])*(j/(double)steps));
        }
        if(current.Count>1)parts.Add(current.ToArray());return parts.ToArray();
    }
    void DrawAnchors()
    {
        foreach(var item in routes)
        {
            var p=item.Route.Anchor;double? h=MapSceneGeometry.Height(scene,p);if(h==null)continue;
            DrawMarker(SlotName(item.Slot),p,h.Value,SlotColor(item.Slot),item.Route.Heading,item.Slot==selectedSlot);
        }
    }
    void DrawAnnotations()
    {
        foreach(var line in annotations.Take(8))DrawPolyline(line.Points.Take(4096).ToArray(),line.Color,line.Width);
        foreach(var area in areas.Take(32))DrawArea(area);
        if(showAnchors&&farmingRadius is double radius&&farmingAnchor is Vec anchor)DrawArea(new(anchor,radius,Color.FromArgb(127,199,174),$"Anchor area · {radius:0.#}m"));
        if(cone is {} c&&c.Position.Finite&&double.IsFinite(c.Heading)&&double.IsFinite(c.Range)&&c.Range>0)
        {
            var facing=Movement.FromClientHeading(c.Heading);double heading=Math.Atan2(facing.Y,facing.X),half=Math.Clamp(c.HalfAngleDegrees,1,89)*Math.PI/180,range=Math.Clamp(c.Range,.1,2000);
            var vertices=new List<Vec>{c.Position};for(int i=0;i<=24;i++){double angle=heading-half+half*2*i/24;vertices.Add(c.Position+new Vec(Math.Cos(angle),Math.Sin(angle))*range);}vertices.Add(c.Position);
            DrawPolyline(vertices.ToArray(),c.Color,2);
        }
    }
    void DrawArea(MapArea3D area)
    {
        if(!area.Center.Finite||!double.IsFinite(area.Radius)||area.Radius<=0||area.Radius>2000)return;
        var points=new Vec[65];for(int i=0;i<points.Length;i++){double a=i*Math.PI/32;points[i]=area.Center+new Vec(Math.Cos(a),Math.Sin(a))*area.Radius;}
        DrawPolyline(points,area.Color,1.5f);
        if(area.Name.Length>0&&MapSceneGeometry.Height(scene,area.Center) is double h)DrawLabel(area.Name,area.Center,h);
    }
    void DrawPolyline(Vec[] points,Color color,float width)
    {
        if(points.Length<2)return;
        GL.glColor4ub(color.R,color.G,color.B,color.A);GL.glLineWidth(float.IsFinite(width)?Math.Clamp(width,1,5):1);
        GL.glDisable(GL.DEPTH_TEST);GL.glEnable(GL.BLEND);GL.glBlendFunc(GL.SRC_ALPHA,GL.ONE_MINUS_SRC_ALPHA);
        foreach(var part in DrapeRoute(scene,points)){GL.glBegin(GL.LINE_STRIP);foreach(var p in part)GL.glVertex3d(p.X,p.Y+1.2,-p.Z);GL.glEnd();}
        GL.glDisable(GL.BLEND);GL.glEnable(GL.DEPTH_TEST);GL.glLineWidth(1);
    }
    void DrawMarkers(){foreach(var marker in markers)if(marker.Position.Finite&&double.IsFinite(marker.Height))DrawMarker(marker.ShowLabel?marker.Name:"",marker.Position,marker.Height,marker.Color,marker.Heading,true,marker.Engaged,marker.Player);}
    void DrawMarker(string name,Vec point,double height,Color color,double heading,bool prominent,bool engaged=false,bool player=false)
    {
        if(passive)GL.glDisable(GL.DEPTH_TEST);
        double radius=Math.Clamp(distance*.003,1,10)*(prominent?1.2:1);GL.glColor4ub(color.R,color.G,color.B,255);GL.glLineWidth(prominent?3:2);
        int corners=player?4:24;
        GL.glBegin(GL.LINE_LOOP);for(int i=0;i<corners;i++){double a=i*Math.PI*2/corners;GL.glVertex3d(point.X+Math.Cos(a)*radius,height+1,-point.Y-Math.Sin(a)*radius);}GL.glEnd();
        if(engaged){GL.glColor4ub(127,199,174,255);GL.glBegin(GL.LINE_LOOP);for(int i=0;i<24;i++){double a=i*Math.PI/12;GL.glVertex3d(point.X+Math.Cos(a)*radius*1.5,height+1,-point.Y-Math.Sin(a)*radius*1.5);}GL.glEnd();GL.glColor4ub(color.R,color.G,color.B,255);}
        if(double.IsFinite(heading)){var d=Movement.FromClientHeading(heading);GL.glBegin(GL.LINES);GL.glVertex3d(point.X,height+1,-point.Y);GL.glVertex3d(point.X+d.X*radius*2.8,height+1,-point.Y-d.Y*radius*2.8);GL.glEnd();}
        DrawLabel(name,point,height+radius+1,radius);if(passive)GL.glEnable(GL.DEPTH_TEST);GL.glLineWidth(1);
    }
    void DrawLabel(string name,Vec point,double height,double offset=0)
    {
        if(fontLists==0||name.Length==0)return;
        var ascii=Encoding.ASCII.GetBytes(new string(name.Take(36).Select(c=>c is >= ' ' and <= '~'?c:'?').ToArray()));
        int halo=Math.Max(1,(int)Math.Round(DeviceDpi/96d));
        GL.glPushAttrib(GL.CURRENT_BIT|GL.ENABLE_BIT|GL.LIST_BIT);
        try
        {
            GL.glDisable(GL.DEPTH_TEST);GL.glDisable(GL.LIGHTING);GL.glDisable(GL.TEXTURE_2D);GL.glDisable(GL.BLEND);
            GL.glListBase(fontLists-32);
            void Paint(Color ink,int dx,int dy)
            {
                // Bitmap glyph lists advance the raster cursor. Reset its
                // position and color for every pass before a pixel-space shift.
                GL.glColor4ub(ink.R,ink.G,ink.B,255);GL.glRasterPos3d(point.X+offset,height+1,-point.Y);
                if(dx!=0||dy!=0)GL.glBitmap(0,0,0,0,dx,dy,IntPtr.Zero);
                GL.glCallLists(ascii.Length,GL.UNSIGNED_BYTE,ascii);
            }
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                if(dx!=0||dy!=0)Paint(MapOverlayText.Background,dx*halo,dy*halo);
            Paint(MapOverlayText.Foreground,0,0);
        }
        finally {GL.glPopAttrib();}
    }
    void BuildFont()
    {
        if(fontLists!=0){GL.glDeleteLists(fontLists,95);fontLists=0;}fontDirty=false;
        var font=Native.CreateFont(-(int)Math.Round(12*DeviceDpi/96.0),0,0,0,500,0,0,0,1,0,0,4,0,"Segoe UI");if(font==IntPtr.Zero)return;
        var old=Native.SelectObject(dc,font);try{fontLists=GL.glGenLists(95);if(fontLists!=0&&!Native.wglUseFontBitmaps(dc,32,95,fontLists)){GL.glDeleteLists(fontLists,95);fontLists=0;}}
        finally{Native.SelectObject(dc,old);Native.DeleteObject(font);}
    }
    static uint NewList(){uint id=GL.glGenLists(1);if(id==0)throw new InvalidOperationException("Graphics memory could not be allocated for the map");return id;}
    static void DeleteList(ref uint id){if(id!=0){GL.glDeleteLists(id,1);id=0;}}
    void ReleaseContext()
    {
        if(context!=IntPtr.Zero)
        {
            if(Native.wglMakeCurrent(dc,context))
            {
                DeleteList(ref terrainList);DeleteList(ref artworkList);DeleteList(ref objectList);DeleteList(ref routeList);
                foreach(var id in meshLists)if(id!=0)GL.glDeleteLists(id,1);meshLists=[];
                if(fontLists!=0){GL.glDeleteLists(fontLists,95);fontLists=0;}if(texture!=0){GL.glDeleteTextures(1,ref texture);texture=0;}
            }
            Native.wglMakeCurrent(IntPtr.Zero,IntPtr.Zero);Native.wglDeleteContext(context);context=IntPtr.Zero;
        }
        if(dc!=IntPtr.Zero){Native.ReleaseDC(Handle,dc);dc=IntPtr.Zero;}
        sceneDirty=artworkDirty=routesDirty=fontDirty=true;
    }
    protected override void OnMouseDown(MouseEventArgs e){if(passive)return;base.OnMouseDown(e);if(e.Button is MouseButtons.Left or MouseButtons.Right){Focus();Capture=true;dragButton=e.Button;dragStart=e.Location;}}
    protected override void OnMouseMove(MouseEventArgs e)
    {
        if(passive)return;base.OnMouseMove(e);if(dragButton==MouseButtons.None)return;int dx=e.X-dragStart.X,dy=e.Y-dragStart.Y;dragStart=e.Location;
        if(dragButton==MouseButtons.Left&&!topView){yaw+=dx*.008;pitch=Math.Clamp(pitch+dy*.006,.16,1.48);}
        else{Camera(out _,out var right,out var up);right.Y=up.Y=0;if(right.LengthSquared()>0)right=Vector3.Normalize(right);if(up.LengthSquared()>0)up=Vector3.Normalize(up);float s=(float)(distance*.828427/Math.Max(1,ClientSize.Height));center+=(-dx*right+dy*up)*s;}
        RequestRender();
    }
    protected override void OnMouseUp(MouseEventArgs e){if(passive)return;base.OnMouseUp(e);dragButton=MouseButtons.None;Capture=false;}
    protected override void OnMouseCaptureChanged(EventArgs e){if(passive)return;base.OnMouseCaptureChanged(e);if(!Capture)dragButton=MouseButtons.None;}
    protected override void OnMouseWheel(MouseEventArgs e){if(passive)return;base.OnMouseWheel(e);distance=Math.Clamp(distance*Math.Pow(.84,e.Delta/120.0),3,50000);RequestRender();}
    protected override void Dispose(bool disposing){if(disposing&&!disposed){disposed=true;frameTimer.Stop();frameTimer.Dispose();ReleaseContext();artwork?.Dispose();artwork=null;}base.Dispose(disposing);}

    [StructLayout(LayoutKind.Sequential)]struct PixelFormat
    {
        internal ushort Size,Version;internal uint Flags;internal byte Type,ColorBits,RedBits,RedShift,GreenBits,GreenShift,BlueBits,BlueShift,AlphaBits,AlphaShift,AccumBits,AccumRedBits,AccumGreenBits,AccumBlueBits,AccumAlphaBits,DepthBits,StencilBits,AuxBuffers,LayerType,Reserved;internal uint LayerMask,VisibleMask,DamageMask;
        internal static PixelFormat Desired()=>new(){Size=(ushort)Marshal.SizeOf<PixelFormat>(),Version=1,Flags=0x25,Type=0,ColorBits=24,AlphaBits=8,DepthBits=24,StencilBits=8,LayerType=0};
    }
    static class Native
    {
        [DllImport("user32.dll")]internal static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")]internal static extern int ReleaseDC(IntPtr window,IntPtr dc);
        [DllImport("gdi32.dll",SetLastError=true)]internal static extern int ChoosePixelFormat(IntPtr dc,ref PixelFormat p);
        [DllImport("gdi32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool SetPixelFormat(IntPtr dc,int format,ref PixelFormat p);
        [DllImport("gdi32.dll")]internal static extern int DescribePixelFormat(IntPtr dc,int format,uint size,ref PixelFormat p);
        [DllImport("gdi32.dll")][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool SwapBuffers(IntPtr dc);
        [DllImport("gdi32.dll",CharSet=CharSet.Unicode,EntryPoint="CreateFontW")]internal static extern IntPtr CreateFont(int height,int width,int escape,int orientation,int weight,uint italic,uint underline,uint strike,uint charset,uint precision,uint clip,uint quality,uint family,string face);
        [DllImport("gdi32.dll")]internal static extern IntPtr SelectObject(IntPtr dc,IntPtr value);
        [DllImport("gdi32.dll")][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool DeleteObject(IntPtr value);
        [DllImport("opengl32.dll")]internal static extern IntPtr wglCreateContext(IntPtr dc);
        [DllImport("opengl32.dll")][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool wglMakeCurrent(IntPtr dc,IntPtr context);
        [DllImport("opengl32.dll")][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool wglDeleteContext(IntPtr context);
        [DllImport("opengl32.dll",EntryPoint="wglUseFontBitmapsW")][return:MarshalAs(UnmanagedType.Bool)]internal static extern bool wglUseFontBitmaps(IntPtr dc,uint first,uint count,uint listBase);
    }
    static class GL
    {
        internal const uint DEPTH_TEST=0x0B71,NORMALIZE=0x0BA1,COLOR_MATERIAL=0x0B57,FRONT_AND_BACK=0x0408,AMBIENT_AND_DIFFUSE=0x1602,LIGHT0=0x4000,DIFFUSE=0x1201,LIGHT_MODEL_AMBIENT=0x0B53,LEQUAL=0x0203,CULL_FACE=0x0B44,PERSPECTIVE_CORRECTION_HINT=0x0C50,NICEST=0x1102,COLOR_BUFFER_BIT=0x4000,DEPTH_BUFFER_BIT=0x100,PROJECTION=0x1701,MODELVIEW=0x1700,POSITION=0x1203,LIGHTING=0x0B50,TEXTURE_2D=0x0DE1,BLEND=0x0BE2,SRC_ALPHA=0x0302,ONE_MINUS_SRC_ALPHA=0x0303,POLYGON_OFFSET_FILL=0x8037,COMPILE=0x1300,VERTEX_ARRAY=0x8074,NORMAL_ARRAY=0x8075,COLOR_ARRAY=0x8076,FLOAT=0x1406,UNSIGNED_BYTE=0x1401,UNSIGNED_INT=0x1405,TRIANGLES=4,LINE_STRIP=3,LINE_LOOP=2,LINES=1,RGBA=0x1908,LINEAR=0x2601,CLAMP=0x2900,TEXTURE_MIN_FILTER=0x2801,TEXTURE_MAG_FILTER=0x2800,TEXTURE_WRAP_S=0x2802,TEXTURE_WRAP_T=0x2803,UNPACK_ALIGNMENT=0x0CF5,PACK_ALIGNMENT=0x0D05,BACK=0x0405;
        internal const uint CURRENT_BIT=1,ENABLE_BIT=0x2000,LIST_BIT=0x20000;
        [DllImport("opengl32.dll")]internal static extern IntPtr glGetString(uint name);
        [DllImport("opengl32.dll")]internal static extern uint glGetError();
        [DllImport("opengl32.dll")]internal static extern void glClearColor(float r,float g,float b,float a);
        [DllImport("opengl32.dll")]internal static extern void glClear(uint mask);
        [DllImport("opengl32.dll")]internal static extern void glEnable(uint value);
        [DllImport("opengl32.dll")]internal static extern void glDisable(uint value);
        [DllImport("opengl32.dll")]internal static extern void glDepthFunc(uint value);
        [DllImport("opengl32.dll")]internal static extern void glDepthMask([MarshalAs(UnmanagedType.I1)]bool value);
        [DllImport("opengl32.dll")]internal static extern void glColorMaterial(uint face,uint mode);
        [DllImport("opengl32.dll")]internal static extern void glLightfv(uint light,uint name,float[] values);
        [DllImport("opengl32.dll")]internal static extern void glLightModelfv(uint name,float[] values);
        [DllImport("opengl32.dll")]internal static extern void glHint(uint target,uint mode);
        [DllImport("opengl32.dll")]internal static extern void glViewport(int x,int y,int width,int height);
        [DllImport("opengl32.dll")]internal static extern void glMatrixMode(uint mode);
        [DllImport("opengl32.dll")]internal static extern void glLoadIdentity();
        [DllImport("opengl32.dll")]internal static extern void glLoadMatrixd(double[] values);
        [DllImport("opengl32.dll")]internal static extern void glMultMatrixf(float[] values);
        [DllImport("opengl32.dll")]internal static extern void glFrustum(double left,double right,double bottom,double top,double near,double far);
        [DllImport("opengl32.dll")]internal static extern void glOrtho(double left,double right,double bottom,double top,double near,double far);
        [DllImport("opengl32.dll")]internal static extern void glPushMatrix();
        [DllImport("opengl32.dll")]internal static extern void glPopMatrix();
        [DllImport("opengl32.dll")]internal static extern void glScaled(double x,double y,double z);
        [DllImport("opengl32.dll")]internal static extern void glBegin(uint mode);
        [DllImport("opengl32.dll")]internal static extern void glEnd();
        [DllImport("opengl32.dll")]internal static extern void glVertex3d(double x,double y,double z);
        [DllImport("opengl32.dll")]internal static extern void glTexCoord2f(float x,float y);
        [DllImport("opengl32.dll")]internal static extern void glColor4ub(byte r,byte g,byte b,byte a);
        [DllImport("opengl32.dll")]internal static extern void glLineWidth(float width);
        [DllImport("opengl32.dll")]internal static extern void glBlendFunc(uint source,uint destination);
        [DllImport("opengl32.dll")]internal static extern void glPolygonOffset(float factor,float units);
        [DllImport("opengl32.dll")]internal static extern void glFlush();
        [DllImport("opengl32.dll")]internal static extern void glFinish();
        [DllImport("opengl32.dll")]internal static extern uint glGenLists(int count);
        [DllImport("opengl32.dll")]internal static extern void glDeleteLists(uint id,int count);
        [DllImport("opengl32.dll")]internal static extern void glNewList(uint id,uint mode);
        [DllImport("opengl32.dll")]internal static extern void glEndList();
        [DllImport("opengl32.dll")]internal static extern void glCallList(uint id);
        [DllImport("opengl32.dll")]internal static extern void glEnableClientState(uint value);
        [DllImport("opengl32.dll")]internal static extern void glDisableClientState(uint value);
        [DllImport("opengl32.dll")]internal static extern void glVertexPointer(int size,uint type,int stride,IntPtr value);
        [DllImport("opengl32.dll")]internal static extern void glNormalPointer(uint type,int stride,IntPtr value);
        [DllImport("opengl32.dll")]internal static extern void glColorPointer(int size,uint type,int stride,IntPtr value);
        [DllImport("opengl32.dll")]internal static extern void glDrawElements(uint mode,int count,uint type,IntPtr indices);
        [DllImport("opengl32.dll")]internal static extern void glGenTextures(int count,out uint value);
        [DllImport("opengl32.dll")]internal static extern void glDeleteTextures(int count,ref uint value);
        [DllImport("opengl32.dll")]internal static extern void glBindTexture(uint target,uint value);
        [DllImport("opengl32.dll")]internal static extern void glTexParameteri(uint target,uint name,uint value);
        [DllImport("opengl32.dll")]internal static extern void glPixelStorei(uint name,int value);
        [DllImport("opengl32.dll")]internal static extern void glTexImage2D(uint target,int level,uint format,int width,int height,int border,uint sourceFormat,uint type,IntPtr bytes);
        [DllImport("opengl32.dll")]internal static extern void glReadBuffer(uint value);
        [DllImport("opengl32.dll")]internal static extern void glReadPixels(int x,int y,int width,int height,uint format,uint type,IntPtr bytes);
        [DllImport("opengl32.dll")]internal static extern void glRasterPos3d(double x,double y,double z);
        [DllImport("opengl32.dll")]internal static extern void glBitmap(int width,int height,float xorig,float yorig,float xmove,float ymove,IntPtr bytes);
        [DllImport("opengl32.dll")]internal static extern void glPushAttrib(uint mask);
        [DllImport("opengl32.dll")]internal static extern void glPopAttrib();
        [DllImport("opengl32.dll")]internal static extern void glListBase(uint id);
        [DllImport("opengl32.dll")]internal static extern void glCallLists(int count,uint type,byte[] bytes);
    }
}
