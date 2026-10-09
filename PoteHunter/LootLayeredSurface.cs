using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PoteHunter;

// A bounded, top-down premultiplied DIB. Bitmap and DC are owned together and
// reused until size changes; callers never receive or dispose the bitmap.
internal sealed class LootLayeredSurface : IDisposable
{
    IntPtr memory, handle, original;
    Bitmap? image;
    internal IntPtr DC => memory;
    internal int Allocations { get; private set; }
    internal bool Allocated => image != null;
    internal void Ensure(Size size, IntPtr screen)
    {
        if(size.Width is <1 or >2048 || size.Height is <1 or >2048)throw new ArgumentOutOfRangeException(nameof(size));
        if(image?.Size == size)return;
        Dispose();
        try
        {
            memory = CreateCompatibleDC(screen); if(memory == IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
            var info = new BitmapInfo { Size = 40, Width = size.Width, Height = -size.Height, Planes = 1, Bits = 32 };
            handle = CreateDIBSection(screen, ref info, 0, out var pixels, IntPtr.Zero, 0);
            if(handle == IntPtr.Zero || pixels == IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
            original = SelectObject(memory, handle);
            if(original == IntPtr.Zero || original == new IntPtr(-1))throw new Win32Exception(Marshal.GetLastWin32Error());
            image = new Bitmap(size.Width, size.Height, checked(size.Width * 4), PixelFormat.Format32bppPArgb, pixels);
            Allocations++;
        }
        catch { Dispose(); throw; }
    }
    internal void Draw(Action<Graphics> draw)
    {
        using var graphics = Graphics.FromImage(image ?? throw new InvalidOperationException("Layered surface was not sized."));
        graphics.Clear(Color.Transparent); draw(graphics);
    }
    public void Dispose()
    {
        image?.Dispose(); image = null;
        if(memory != IntPtr.Zero && original != IntPtr.Zero && original != new IntPtr(-1))SelectObject(memory, original);
        if(handle != IntPtr.Zero)DeleteObject(handle);
        if(memory != IntPtr.Zero)DeleteDC(memory);
        memory = handle = original = IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfo { internal uint Size; internal int Width, Height; internal ushort Planes, Bits; internal uint Compression, ImageSize; internal int XPixels, YPixels; internal uint ColorsUsed, Important, Color; }
    [DllImport("gdi32.dll", SetLastError = true)]static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)]static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)]static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
}
