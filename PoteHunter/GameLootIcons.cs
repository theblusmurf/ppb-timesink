using System.Drawing.Drawing2D;

namespace PoteHunter;

/// <summary>Approved inventory icons bundled with the app, independent of the game installation.</summary>
internal static class GameLootIcons
{
    static readonly Lazy<IReadOnlyDictionary<string,Bitmap>> Images=new(Load);

    static IReadOnlyDictionary<string,Bitmap> Load()
    {
        var images=new Dictionary<string,Bitmap>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach(string name in new[]{"Silvin","Mithril","Iternium","Fehu","Diamond"})
            {
                using var stream=typeof(GameLootIcons).Assembly.GetManifestResourceStream($"PoteHunter.LootIcons.{name}.png")
                    ?? throw new InvalidOperationException($"Bundled loot icon missing: {name}.");
                using var image=Image.FromStream(stream);
                // Detach from the disposed resource stream; reuse the small bitmaps on redraw.
                images.Add(name=="Diamond"?"Gems":name,new Bitmap(image));
            }
            return images;
        }
        catch {foreach(var image in images.Values)image.Dispose();throw;}
    }

    internal static void Draw(Graphics graphics,string resource,RectangleF bounds)
    {
        var image=Images.Value[resource];
        var state=graphics.Save();
        try
        {
            graphics.InterpolationMode=InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode=PixelOffsetMode.Half;
            int factor=Math.Max(1,(int)Math.Floor(Math.Min(bounds.Width/image.Width,bounds.Height/image.Height)));
            float width=image.Width*factor,height=image.Height*factor;
            graphics.DrawImage(image,new RectangleF(bounds.X+(bounds.Width-width)/2,bounds.Y+(bounds.Height-height)/2,width,height),
                new RectangleF(0,0,image.Width,image.Height),GraphicsUnit.Pixel);
        }
        finally {graphics.Restore(state);}
    }
}
