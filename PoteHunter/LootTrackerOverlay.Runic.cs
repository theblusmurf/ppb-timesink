using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PoteHunter;

internal sealed partial class LootTrackerOverlay
{
    internal bool HasNonActivatingStyles=>(CreateParams.ExStyle&(WsExNoActivate|WsExToolWindow|WsExLayered))==(WsExNoActivate|WsExToolWindow|WsExLayered);
    internal int LayeredPresentationCount {get;private set;}

    internal void PresentTransparentOverlay() => PresentTransparentOverlay(presentedSnapshot = snapshotProvider());
    internal void PresentTransparentOverlay(LootTrackerSnapshot snapshot)
    {
        if(!IsTransparentDesign || !IsHandleCreated || IsDisposed)return;
        IntPtr screen=GetDC(IntPtr.Zero);
        try
        {
            if(screen==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
            layeredSurface.Ensure(TransparentSize,screen);
            layeredSurface.Draw(graphics=>
            {
                if(design==1)RunicFoldRenderer.Draw(graphics,snapshot,EffectiveScalePercent,backgroundOpacityPercent);
                else RunicStripRenderer.Draw(graphics,snapshot,EffectiveScalePercent,backgroundOpacityPercent);
            });
            var position=new NativePoint(Left,Top);var origin=new NativePoint(0,0);var size=new NativeSize(TransparentSize.Width,TransparentSize.Height);
            var blend=new AlphaBlend {Operation=0,Flags=0,ConstantAlpha=255,Format=1};
            if(!UpdateLayeredWindow(Handle,screen,ref position,ref size,layeredSurface.DC,ref origin,0,ref blend,2))
                throw new Win32Exception(Marshal.GetLastWin32Error(),"Unable to draw the transparent loot overlay.");
            LayeredPresentationCount++;
        }
        finally {if(screen!=IntPtr.Zero)ReleaseDC(IntPtr.Zero,screen);}
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativePoint(int x,int y){public int X=x;public int Y=y;}
    [StructLayout(LayoutKind.Sequential)]
    struct NativeSize(int width,int height){public int Width=width;public int Height=height;}
    [StructLayout(LayoutKind.Sequential,Pack=1)]
    struct AlphaBlend{public byte Operation,Flags,ConstantAlpha,Format;}
    [DllImport("user32.dll",SetLastError=true)]static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr window,IntPtr dc);
    [DllImport("user32.dll",SetLastError=true)]static extern bool UpdateLayeredWindow(IntPtr window,IntPtr destination,ref NativePoint position,ref NativeSize size,IntPtr source,ref NativePoint origin,uint colorKey,ref AlphaBlend blend,uint flags);
}
