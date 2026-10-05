namespace PoteHunter;

// Map artwork varies from pale parchment to dark terrain. Keep text independent
// of marker colors; the small opaque plate gives every label a stable background.
internal static class MapOverlayText
{
    internal static readonly Color Foreground=Color.FromArgb(247,249,250);
    internal static readonly Color Background=Color.FromArgb(12,21,28);

    internal static void Draw(Graphics g,string text,Font font,PointF origin,Color? accent=null)
    {
        var size=g.MeasureString(text,font);
        var plate=new RectangleF(origin.X-2,origin.Y-1,size.Width+4,size.Height+2);
        using var back=new SolidBrush(Background);g.FillRectangle(back,plate);
        if(accent is Color color)
        {
            using var key=new SolidBrush(color);g.FillRectangle(key,plate.X,plate.Y,2,plate.Height);
        }
        using var ink=new SolidBrush(Foreground);g.DrawString(text,font,ink,origin);
    }
}
