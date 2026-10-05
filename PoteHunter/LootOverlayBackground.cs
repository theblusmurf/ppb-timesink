using System.Drawing.Drawing2D;

namespace PoteHunter;

/// <summary>Backdrop alpha is independent of the live text, icons and frame.</summary>
internal static class LootOverlayBackground
{
    internal const int DefaultOpacityPercent=40;

    internal static void Draw(Graphics graphics,Size logicalSize,int opacityPercent)
    {
        int alpha=(int)Math.Round(Math.Clamp(opacityPercent,0,100)*255d/100);
        if(alpha==0)return;
        float left=4,top=4,right=logicalSize.Width-4,bottom=logicalSize.Height-4,cut=12;
        using var panel=new GraphicsPath();
        panel.AddPolygon(new PointF[]{new(left,top),new(right-cut,top),new(right,top+cut),new(right,bottom),new(left+cut,bottom),new(left,bottom-cut)});
        using var fill=new SolidBrush(Color.FromArgb(alpha,ImperialTheme.Window));
        graphics.FillPath(fill,panel);
    }
}

/// <summary>Original item bitmaps and the approved currency glyph, independent of theme accents.</summary>
internal static class LootOverlayArtwork
{
    internal static void DrawResource(Graphics graphics,string resource,RectangleF bounds)
    {
        if(resource!="Gold"){GameLootIcons.Draw(graphics,resource,bounds);return;}
        float size=Math.Min(bounds.Width,bounds.Height),unit=size/26f;
        var circle=new RectangleF(bounds.X+(bounds.Width-size)/2+2*unit,bounds.Y+(bounds.Height-size)/2+2*unit,22*unit,22*unit);
        using var fill=new SolidBrush(Color.FromArgb(218,167,66));
        using var rim=new Pen(Color.FromArgb(231,188,112),1.3f*unit);
        graphics.FillEllipse(fill,circle);graphics.DrawEllipse(rim,circle);
    }
}
