namespace PoteHunter;

/// <summary>Shared presentation colors; no settings or game state.</summary>
internal static class ImperialTheme
{
    internal static readonly Color Window = Color.FromArgb(14,13,14);
    internal static readonly Color Surface = Color.FromArgb(26,23,24);
    internal static readonly Color Raised = Color.FromArgb(36,30,29);
    internal static readonly Color Border = Color.FromArgb(84,58,37);
    internal static readonly Color Gold = Color.FromArgb(231,181,91);
    internal static readonly Color Text = Color.FromArgb(243,234,219);
    internal static readonly Color Muted = Color.FromArgb(181,166,150);
    internal static readonly Color RouteBlue = Color.FromArgb(137,175,204);
    internal static readonly Color RouteRose = Color.FromArgb(206,146,146);
    internal static readonly Lazy<Bitmap> Banner = new(()=>Load("news-bg"));
    internal static readonly Lazy<Bitmap> Logo = new(()=>Load("playpotebot-compass"));

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
        using var shade=new SolidBrush(Color.FromArgb(140,ImperialTheme.Window));
        e.Graphics.FillRectangle(shade,ClientRectangle);
        using var trim=new Pen(ImperialTheme.Gold);
        e.Graphics.DrawLine(trim,0,Height-1,Width,Height-1);
    }
}
