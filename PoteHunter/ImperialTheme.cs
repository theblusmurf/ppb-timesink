namespace PoteHunter;

/// <summary>Shared presentation colors; no settings or game state.</summary>
internal static class ImperialTheme
{
    internal static readonly Color Window = Color.FromArgb(11,11,18);
    internal static readonly Color Surface = Color.FromArgb(21,21,31);
    internal static readonly Color Raised = Color.FromArgb(28,28,42);
    internal static readonly Color Border = Color.FromArgb(42,42,58);
    internal static readonly Color Gold = Color.FromArgb(207,174,106);
    internal static readonly Color Text = Color.FromArgb(232,232,240);
    internal static readonly Color Muted = Color.FromArgb(154,154,171);
    internal static readonly Color RouteBlue = Color.FromArgb(137,175,204);
    internal static readonly Color RouteRose = Color.FromArgb(206,146,146);
    internal static readonly Lazy<Bitmap> Banner = new(()=>Load("news-bg"));
    internal static readonly Lazy<Bitmap> Logo = new(()=>Load("logo"));

    static Bitmap Load(string name)
    {
        using var stream=typeof(ImperialTheme).Assembly.GetManifestResourceStream($"PoteHunter.ThemeAssets.{name}.png")
            ?? throw new InvalidOperationException($"Bundled theme image missing: {name}.");
        using var image=Image.FromStream(stream);
        return new Bitmap(image);
    }
}

internal sealed class ImperialBanner : Panel
{
    internal ImperialBanner() => DoubleBuffered=true;
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if(Width<=0 || Height<=0)return;
        var image=ImperialTheme.Banner.Value;
        float scale=Math.Max((float)Width/image.Width,(float)Height/image.Height);
        var bounds=new RectangleF((Width-image.Width*scale)/2,(Height-image.Height*scale)/2,image.Width*scale,image.Height*scale);
        e.Graphics.DrawImage(image,bounds);
        using var shade=new SolidBrush(Color.FromArgb(205,ImperialTheme.Window));
        e.Graphics.FillRectangle(shade,ClientRectangle);
        using var trim=new Pen(ImperialTheme.Gold);
        e.Graphics.DrawLine(trim,0,Height-1,Width,Height-1);
    }
}
