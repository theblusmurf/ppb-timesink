using System.Drawing.Drawing2D;

namespace PoteHunter;

/// <summary>Transparent, outlined loot text; coordinates and fonts scale together.</summary>
internal static class RunicStripRenderer
{
    internal static readonly Size LogicalSize=new(680,184);
    static readonly string[] Resources=["Gold","Silvin","Mithril","Iternium","Fehu","Gems"];
    static readonly Color Ink=ImperialTheme.Text;
    static readonly Color Muted=ImperialTheme.Muted;
    static readonly Color Accent=ImperialTheme.Accent;

    internal static Size SizeAt(int percent)=>LootOverlayBitmap.SizeAt(LogicalSize,percent);

    internal static Bitmap Render(LootTrackerSnapshot snapshot,int percent,int backgroundOpacityPercent=0)
        =>LootOverlayBitmap.Render(LogicalSize,percent,graphics=>Draw(graphics,snapshot,percent,backgroundOpacityPercent));

    internal static void Draw(Graphics graphics,LootTrackerSnapshot snapshot,int percent,int backgroundOpacityPercent=0)
    {
        var state=graphics.Save();
        try
        {
            float scale=Math.Clamp(percent,50,200)/100f;
            graphics.ScaleTransform(scale,scale);
            graphics.SmoothingMode=SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
            LootOverlayBackground.Draw(graphics,LogicalSize,backgroundOpacityPercent);
            graphics.DrawImage(ImperialTheme.Logo.Value,new Rectangle(12,8,22,23));
            Text(graphics,"CARGO MANIFEST","Segoe UI Semibold",15,new(44,7,280,28),Ink);
            Text(graphics,"Session "+Duration(snapshot.Elapsed),"Consolas",12,new(449,9,219,24),Muted,true);
            using var rule=new Pen(ImperialTheme.Border,1);
            using var accent=new Pen(Accent,1.5f);
            graphics.DrawLine(rule,10,37,670,37);graphics.DrawLine(accent,10,37,120,37);
            for(int i=0;i<Resources.Length;i++)
            {
                string name=Resources[i];float left=10+i*110;
                using var socket=new GraphicsPath();
                socket.AddPolygon(new PointF[]{new(left+35,43),new(left+64,43),new(left+72,51),new(left+72,75),new(left+35,75)});
                graphics.DrawPath(rule,socket);
                LootOverlayArtwork.DrawResource(graphics,name,new(left+39,46,29,26));
                Text(graphics,snapshot.AmountText(name),"Consolas",24,new(left,77,108,35),Ink,true);
                Text(graphics,LootTrackerSnapshot.DisplayName(name),"Segoe UI",13,new(left,111,108,24),Ink,true);
                Text(graphics,$"{snapshot.RateText(name)} / hr","Consolas",11,new(left,137,108,21),Accent,true);
            }
            Text(graphics,$"Active {Duration(snapshot.RateElapsed)} · {snapshot.Sources.Sum(source=>source.Kills):N0} kills · {snapshot.Sources.Sum(source=>source.Drops):N0} detected drops","Segoe UI",11,new(151,159,378,24),Muted,true);
            LootOverlayResetButtons.Draw(graphics,3,LogicalSize);
        }
        finally {graphics.Restore(state);}
    }

    static void Text(Graphics graphics,string value,string family,float size,RectangleF bounds,Color color,bool centered=false)
    {
        using var fontFamily=new FontFamily(family);
        using var format=new StringFormat(StringFormat.GenericTypographic){Alignment=centered?StringAlignment.Center:StringAlignment.Near,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap};
        // Keep full amounts visible when counts grow; do not abbreviate gold.
        while(size>5)
        {
            using var measuredFont=new Font(fontFamily,size,FontStyle.Regular,GraphicsUnit.Pixel);
            if(graphics.MeasureString(value,measuredFont,PointF.Empty,format).Width<=bounds.Width-4)break;
            size-=.5f;
        }
        using var path=new GraphicsPath();path.AddString(value,fontFamily,(int)FontStyle.Regular,size,bounds,format);
        using var outline=new Pen(Color.FromArgb(215,ImperialTheme.Window),2.2f){LineJoin=LineJoin.Round};
        using var fill=new SolidBrush(color);
        graphics.DrawPath(outline,path);graphics.FillPath(fill,path);
    }

    static string Duration(TimeSpan value)
    {
        value=value<TimeSpan.Zero?TimeSpan.Zero:value;
        return $"{(long)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    }
}
