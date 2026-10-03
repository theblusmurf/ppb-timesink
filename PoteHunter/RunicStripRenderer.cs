using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PoteHunter;

/// <summary>Transparent, outlined loot text; coordinates and fonts scale together.</summary>
internal static class RunicStripRenderer
{
    internal static readonly Size LogicalSize=new(680,184);
    static readonly string[] Resources=["Gold","Silvin","Mithril","Iternium","Fehu","Gems"];
    static readonly Color Ink=Color.FromArgb(247,239,217);
    static readonly Color Muted=Color.FromArgb(205,191,163);
    static readonly Color Brass=Color.FromArgb(237,200,125);

    internal static Size SizeAt(int percent)
    {
        double scale=Math.Clamp(percent,50,200)/100d;
        return new((int)Math.Round(LogicalSize.Width*scale),(int)Math.Round(LogicalSize.Height*scale));
    }

    internal static Bitmap Render(LootTrackerSnapshot snapshot,int percent,int backgroundOpacityPercent=0)
    {
        Size size=SizeAt(percent);
        var bitmap=new Bitmap(size.Width,size.Height,PixelFormat.Format32bppPArgb);
        try
        {
            using var graphics=Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            Draw(graphics,snapshot,percent,backgroundOpacityPercent);
            return bitmap;
        }
        catch {bitmap.Dispose();throw;}
    }

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
            Text(graphics,"SPOILS OF THE HUNT","Georgia",16,new(10,7,425,28),Brass);
            Text(graphics,Duration(snapshot.Elapsed),"Consolas",13,new(525,9,145,24),Muted,true);
            for(int i=0;i<Resources.Length;i++)
            {
                string name=Resources[i];float left=10+i*110;
                Icon(graphics,name,new(left+43,47,22,22));
                Text(graphics,snapshot.AmountText(name),"Consolas",24,new(left,77,108,35),name=="Gold"?Brass:Ink,true);
                Text(graphics,LootTrackerSnapshot.DisplayName(name),"Georgia",14,new(left,111,108,24),Ink,true);
                Text(graphics,$"{snapshot.RateText(name)} / hr","Consolas",11,new(left,137,108,21),Muted,true);
            }
            using var line=new Pen(Color.FromArgb(160,Brass),1);
            graphics.DrawLine(line,258,173,322,173);graphics.DrawLine(line,354,173,418,173);
            Icon(graphics,"Gems",new(331,166,14,14));
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
        using var outline=new Pen(Color.FromArgb(215,18,17,13),2.2f){LineJoin=LineJoin.Round};
        using var fill=new SolidBrush(color);
        graphics.DrawPath(outline,path);graphics.FillPath(fill,path);
    }

    static void Icon(Graphics graphics,string resource,RectangleF bounds)
    {
        var state=graphics.Save();graphics.TranslateTransform(bounds.X,bounds.Y);graphics.ScaleTransform(bounds.Width/22,bounds.Height/22);
        try
        {
            using var path=new GraphicsPath();
            switch(resource)
            {
                case "Gold":
                    path.AddEllipse(3,2,14,5);path.StartFigure();path.AddArc(3,3,14,8,0,180);path.StartFigure();path.AddArc(3,7,14,8,0,180);
                    path.StartFigure();path.AddLine(3,5,3,12);path.StartFigure();path.AddLine(17,5,17,12);break;
                case "Silvin":
                    path.AddPolygon([new(11,2),new(21,7),new(11,12),new(1,7)]);
                    path.StartFigure();path.AddLines([new(1,12),new(11,17),new(21,12)]);path.StartFigure();path.AddLines([new(1,16),new(11,21),new(21,16)]);break;
                case "Mithril":
                    path.AddPolygon([new(1,5),new(21,5),new(17,11),new(13,11),new(13,17),new(18,20),new(4,20),new(9,17),new(9,11),new(4,10)]);break;
                case "Iternium":
                    path.AddPolygon([new(11,1),new(20,6),new(20,16),new(11,21),new(2,16),new(2,6)]);break;
                case "Fehu":
                    path.AddBezier(11,1,13,10,23,10,18,18);path.AddBezier(18,18,10,26,1,17,4,12);path.AddBezier(4,12,4,7,8,5,11,1);path.CloseFigure();break;
                default:
                    path.AddPolygon([new(5,2),new(17,2),new(21,8),new(11,21),new(1,8)]);
                    path.StartFigure();path.AddLine(1,8,21,8);path.StartFigure();path.AddLines([new(5,2),new(8,8),new(11,21),new(14,8),new(17,2)]);break;
            }
            using var outline=new Pen(Color.FromArgb(200,18,17,13),3.8f){LineJoin=LineJoin.Round,StartCap=LineCap.Round,EndCap=LineCap.Round};
            using var pen=new Pen(Brass,1.5f){LineJoin=LineJoin.Round,StartCap=LineCap.Round,EndCap=LineCap.Round};
            graphics.DrawPath(outline,path);graphics.DrawPath(pen,path);
        }
        finally {graphics.Restore(state);}
    }

    static string Duration(TimeSpan value)
    {
        value=value<TimeSpan.Zero?TimeSpan.Zero:value;
        return $"{(long)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    }
}
