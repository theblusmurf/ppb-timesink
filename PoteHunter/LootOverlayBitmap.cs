using System.Drawing.Imaging;

namespace PoteHunter;

/// <summary>Shared size and bitmap ownership for the transparent loot layouts.</summary>
internal static class LootOverlayBitmap
{
    internal static Size SizeAt(Size logicalSize,int percent)
    {
        double scale=Math.Clamp(percent,50,200)/100d;
        return new((int)Math.Round(logicalSize.Width*scale),(int)Math.Round(logicalSize.Height*scale));
    }

    internal static Bitmap Render(Size logicalSize,int percent,Action<Graphics> draw)
    {
        Size size=SizeAt(logicalSize,percent);
        var bitmap=new Bitmap(size.Width,size.Height,PixelFormat.Format32bppPArgb);
        try
        {
            using var graphics=Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            draw(graphics);
            return bitmap;
        }
        catch {bitmap.Dispose();throw;}
    }
}
