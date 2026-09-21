using System.Numerics;
using System.Runtime.InteropServices;
using PoteMemoryProbe;

namespace PoteHunter;

public sealed partial class World
{
    CameraLayoutEvidence? cameraLayout;

    public bool CameraSupported=>cameraLayout!=null;
    public string CameraStatus{get;private set;}="Camera discovery has not run.";

    internal void ConfigureCamera()
    {
        cameraLayout=null;
        try
        {
            var discovered=CameraDiscovery.Resolve(PoteMemoryProbe.Program.ClientPath);
            if(discovered==null){CameraStatus="This client build has no verified camera layout.";return;}
            cameraLayout=discovered;
            ValidateCameraCode(discovered);
            _=ReadCamera();
            CameraStatus="Camera signatures and live matrices verified.";
        }
        catch(Exception ex)
        {
            cameraLayout=null;
            CameraStatus="Camera unavailable: "+ex.Message;
        }
    }

    internal void ResetCamera(){cameraLayout=null;CameraStatus="Camera is disconnected.";}

    internal Size CameraViewportSize
    {
        get
        {
            if(Window==IntPtr.Zero||!GetClientRect(Window,out var viewport)||viewport.Width<=0||viewport.Height<=0)
                throw new InvalidOperationException("The game camera viewport is unavailable.");
            return new Size(viewport.Width,viewport.Height);
        }
    }

    void ValidateCameraCode(CameraLayoutEvidence layout)
    {
        byte[] root=Native.Read(handle!,(nint)(moduleBase+layout.RootCodeRva),layout.RootPattern.Split(' ').Length);
        byte[] ctor=Native.Read(handle!,(nint)(moduleBase+layout.ConstructorCodeRva),layout.ConstructorPattern.Split(' ').Length);
        if(!ProfileDiscovery.Matches(root,layout.RootPattern)||!ProfileDiscovery.Matches(ctor,layout.ConstructorPattern))
        {
            cameraLayout=null;
            throw new InvalidOperationException("Camera discovery changed between the verified client file and loaded process.");
        }
        uint expectedGlobal=checked((uint)(moduleBase+layout.ViewCameraGlobalRva));
        uint expectedVtable=checked((uint)(moduleBase+layout.ViewCameraVtableRva));
        if(BitConverter.ToUInt32(root,1)!=expectedGlobal||BitConverter.ToUInt32(root,58)!=expectedGlobal||BitConverter.ToUInt32(ctor,2)!=expectedVtable)
        {
            cameraLayout=null;
            throw new InvalidOperationException("Camera signature captures do not match the loaded image relocation.");
        }
    }

    public CameraFrame ReadCamera()
    {
        // The render thread writes the two matrices independently. Retry a torn
        // frame rather than treating ordinary camera motion as a layout change.
        for(int attempt=0;;attempt++)
        {
            try{return ReadCameraFrame();}
            catch(CameraFrameUpdatingException) when(attempt<2){Thread.Sleep(1);}
        }
    }

    sealed class CameraFrameUpdatingException : InvalidOperationException
    {
        public CameraFrameUpdatingException():base("The camera changed repeatedly while its view matrices were being read."){}
    }

    CameraFrame ReadCameraFrame()
    {
        var layout=cameraLayout??throw new InvalidOperationException("This client build does not expose a verified camera layout.");
        uint global=checked((uint)(moduleBase+layout.ViewCameraGlobalRva));
        uint camera=BitConverter.ToUInt32(Native.Read(handle!,(nint)global,4));
        if(camera<0x10000)throw new InvalidOperationException("The game camera is not initialized.");
        byte[] first=Native.Read(handle!,(nint)camera,4);
        if(BitConverter.ToUInt32(first)!=checked((uint)(moduleBase+layout.ViewCameraVtableRva)))
            throw new InvalidOperationException("The game camera object did not match its discovered type.");
        byte[] data=Native.Read(handle!,(nint)(camera+layout.HorizontalAspectOffset),0x9C);
        float horizontalAspect=Single(data,0);
        float horizontalFov=Single(data,4);
        Matrix4x4 view=Matrix(data,layout.ViewMatrixOffset-layout.HorizontalAspectOffset);
        Matrix4x4 inverse=Matrix(data,layout.InverseViewMatrixOffset-layout.HorizontalAspectOffset);
        byte[] recheck=Native.Read(handle!,(nint)global,4);
        if(BitConverter.ToUInt32(recheck)!=camera)throw new CameraFrameUpdatingException();
        RequireFinite(horizontalAspect,"camera aspect");RequireFinite(horizontalFov,"camera field of view");
        if(horizontalAspect<=0||horizontalAspect>4||horizontalFov<=0||horizontalFov>=MathF.PI)
            throw new InvalidOperationException("The game camera projection is outside verified bounds.");
        if(Window==IntPtr.Zero||!GetClientRect(Window,out var viewport)||viewport.Width<=0||viewport.Height<=0)
            throw new InvalidOperationException("The game camera viewport is unavailable.");
        float viewportAspect=(float)viewport.Height/viewport.Width;
        float aspectTolerance=MathF.Max(0.005f,2f/viewport.Width);
        if(MathF.Abs(horizontalAspect-viewportAspect)>aspectTolerance)
            throw new InvalidOperationException("The game camera aspect does not match the live client viewport.");
        Matrix4x4 identity=view*inverse;
        float determinant=view.GetDeterminant();
        if(!float.IsFinite(determinant)||MathF.Abs(determinant)<0.0001f||!NearIdentity(identity,0.006f,0.15f))
            throw new CameraFrameUpdatingException();
        Vector3 position=new(inverse.M41/100f,inverse.M42/100f,inverse.M43/100f);
        Vector3 forward=Vector3.Normalize(new(inverse.M31,inverse.M32,inverse.M33));
        Vector3 up=Vector3.Normalize(new(inverse.M21,inverse.M22,inverse.M23));
        float verticalFov=2*MathF.Atan(MathF.Tan(horizontalFov*.5f)*horizontalAspect);
        var frame=new CameraFrame(position,forward,up,verticalFov,1/horizontalAspect);
        Aim3D.ValidateCamera(frame);
        var self=LocalPlayer();
        Vector3 selfPosition=new((float)self.Position.X,(float)self.Height,(float)self.Position.Y);
        float distance=Vector3.Distance(position,selfPosition);
        if(!float.IsFinite(distance)||distance>1000)throw new InvalidOperationException("The verified camera is implausibly far from the local character.");
        return frame;
    }

    static float Single(byte[] bytes,int offset)=>BitConverter.ToSingle(bytes,offset);
    static Matrix4x4 Matrix(byte[] b,int o)=>new(
        Single(b,o),Single(b,o+4),Single(b,o+8),Single(b,o+12),
        Single(b,o+16),Single(b,o+20),Single(b,o+24),Single(b,o+28),
        Single(b,o+32),Single(b,o+36),Single(b,o+40),Single(b,o+44),
        Single(b,o+48),Single(b,o+52),Single(b,o+56),Single(b,o+60));
    static void RequireFinite(float value,string name){if(!float.IsFinite(value))throw new InvalidOperationException(name+" is not finite.");}
    static bool NearIdentity(Matrix4x4 m,float basisEpsilon,float translationEpsilon)=>
        Near(m.M11,1,basisEpsilon)&&Near(m.M22,1,basisEpsilon)&&Near(m.M33,1,basisEpsilon)&&Near(m.M44,1,basisEpsilon)&&
        Near(m.M12,0,basisEpsilon)&&Near(m.M13,0,basisEpsilon)&&Near(m.M14,0,basisEpsilon)&&Near(m.M21,0,basisEpsilon)&&Near(m.M23,0,basisEpsilon)&&Near(m.M24,0,basisEpsilon)&&
        Near(m.M31,0,basisEpsilon)&&Near(m.M32,0,basisEpsilon)&&Near(m.M34,0,basisEpsilon)&&Near(m.M41,0,translationEpsilon)&&Near(m.M42,0,translationEpsilon)&&Near(m.M43,0,translationEpsilon);
    static bool Near(float value,float expected,float epsilon)=>float.IsFinite(value)&&MathF.Abs(value-expected)<=epsilon;

    [StructLayout(LayoutKind.Sequential)]
    struct CameraRect{public int Left,Top,Right,Bottom;public int Width=>Right-Left;public int Height=>Bottom-Top;}
    [DllImport("user32.dll",SetLastError=true)]static extern bool GetClientRect(IntPtr window,out CameraRect rect);
}
