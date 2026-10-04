namespace PoteHunter;

/// <summary>Shared presentation colors; no settings or game state.</summary>
internal static class ImperialTheme
{
    internal static readonly Color Window = Color.FromArgb(10,19,27);
    internal static readonly Color Surface = Color.FromArgb(18,31,42);
    internal static readonly Color Raised = Color.FromArgb(29,44,55);
    internal static readonly Color Border = Color.FromArgb(126,97,52);
    internal static readonly Color Gold = Color.FromArgb(231,181,91);
    internal static readonly Color Text = Color.FromArgb(243,234,219);
    internal static readonly Color Muted = Color.FromArgb(181,166,150);
    internal static readonly Color RouteBlue = Color.FromArgb(137,175,204);
    internal static readonly Color RouteRose = Color.FromArgb(206,146,146);
    internal static readonly Lazy<Bitmap> Banner = new(()=>Load("fantasy-stone"));
    internal static readonly Lazy<Bitmap> Logo = new(()=>Load("playpotebot-compass"));
    internal static readonly Lazy<Bitmap> Targets = new(()=>Load("fantasy-targets"));
    internal static void DrawTarget(Graphics g,string name,RectangleF bounds)
    {
        int index=name switch{"Mimic"=>0,"Pulkhan"=>1,"Tribal"=>2,"Tower"=>3,_=>-1};
        if(index<0)return;
        var image=Targets.Value;int w=image.Width/2,h=image.Height/2;
        g.DrawImage(image,bounds,new RectangleF(index%2*w,index/2*h,w,h),GraphicsUnit.Pixel);
    }

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
        e.Graphics.DrawImage(image,ClientRectangle,new Rectangle(0,0,image.Width,image.Height/3),GraphicsUnit.Pixel);
        using var shade=new SolidBrush(Color.FromArgb(75,ImperialTheme.Window));
        e.Graphics.FillRectangle(shade,ClientRectangle);
        using var trim=new Pen(ImperialTheme.Gold);
        e.Graphics.DrawLine(trim,0,Height-1,Width,Height-1);
        FantasyFrame.Draw(e.Graphics,ClientRectangle);
    }
}

internal static class FantasyFrame
{
    internal static void Draw(Graphics g,Rectangle bounds)
    {
        if(bounds.Width<30 || bounds.Height<30)return;
        using var line=new Pen(ImperialTheme.Gold,1);
        foreach(int sx in new[]{0,1})foreach(int sy in new[]{0,1})
        {
            float x=sx==0?bounds.Left+3:bounds.Right-4,y=sy==0?bounds.Top+3:bounds.Bottom-4;
            int dx=sx==0?1:-1,dy=sy==0?1:-1;
            g.DrawLines(line,new PointF[]{new(x+dx*24,y),new(x+dx*11,y),new(x,y+dy*11),new(x,y+dy*24)});
            g.DrawPolygon(line,new PointF[]{new(x+dx*8,y+dy*4),new(x+dx*12,y+dy*8),new(x+dx*8,y+dy*12),new(x+dx*4,y+dy*8)});
        }
    }
}
