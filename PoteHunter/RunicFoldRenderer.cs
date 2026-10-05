using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PoteHunter;

/// <summary>Two rows of live loot totals with transparent Orbital instrument chrome.</summary>
internal static class RunicFoldRenderer
{
    internal static readonly Size LogicalSize=new(740,280);
    static readonly string[] Resources=["Gold","Silvin","Mithril","Iternium","Fehu","Gems"];
    static readonly Color Accent=ImperialTheme.Accent;
    static readonly Color Ivory=ImperialTheme.Text;
    static readonly Color Shadow=Color.FromArgb(220,ImperialTheme.Window);

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

    internal static void Draw(Graphics g,LootTrackerSnapshot snapshot,int percent,int backgroundOpacityPercent=0)
    {
        var state=g.Save();
        try
        {
            g.ScaleTransform(Math.Clamp(percent,50,200)/100f,Math.Clamp(percent,50,200)/100f);
            g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
            LootOverlayBackground.Draw(g,LogicalSize,backgroundOpacityPercent);
            using var frame=new GraphicsPath();
            frame.AddLines([new(14,8),new(718,8),new(730,20),new(730,46),new(14,46)]);
            frame.StartFigure();frame.AddLine(20,141,720,141);
            for(int row=0;row<2;row++)for(int col=1;col<3;col++)
            {frame.StartFigure();frame.AddLine(16+col*236,64+row*92,16+col*236,131+row*92);}
            Stroke(g,frame,ImperialTheme.Border,1);
            using var accentRail=new GraphicsPath();
            accentRail.AddLine(10,8,10,46);accentRail.StartFigure();accentRail.AddLine(20,233,720,233);
            Stroke(g,accentRail,Accent,1.4f);
            g.DrawImage(ImperialTheme.Logo.Value,new Rectangle(20,14,23,24));
            Text(g,"CARGO MANIFEST","Segoe UI Semibold",17,new(51,12,177,30),Ivory);
            Text(g,$"Session {Duration(snapshot.Elapsed)} · Active {Duration(snapshot.RateElapsed)} · Zone {snapshot.Zone}","Segoe UI",13,new(244,13,375,28),Ivory);
            Text(g,"drag header","Segoe UI",10,new(638,14,88,25),ImperialTheme.Muted);
            for(int row=0;row<3;row++)for(int col=0;col<3;col++)
            {using var brush=new SolidBrush(ImperialTheme.Muted);g.FillRectangle(brush,620+col*4,22+row*4,1,1);}
            for(int i=0;i<Resources.Length;i++)
            {
                string name=Resources[i];float x=22+i%3*236,y=65+i/3*92;
                using var socket=new GraphicsPath();
                socket.AddLines(new PointF[]{new(x-2,y),new(x+50,y),new(x+58,y+8),new(x+58,y+62),new(x-2,y+62),new(x-2,y)});
                Stroke(g,socket,ImperialTheme.Border,1);
                LootOverlayArtwork.DrawResource(g,name,new(x+3,y+3,50,56));
                Text(g,LootTrackerSnapshot.DisplayName(name),"Segoe UI",16,new(x+68,y+1,155,25),Ivory);
                Text(g,snapshot.AmountText(name),"Consolas",27,new(x+68,y+24,155,30),Ivory);
                Text(g,$"{snapshot.RateText(name)}/h","Consolas",14,new(x+68,y+55,155,22),Accent);
            }
            Text(g,$"{snapshot.Sources.Sum(source=>source.Kills):N0} kills · {snapshot.Sources.Sum(source=>source.Drops):N0} detected drops","Segoe UI",13,new(217,244,306,28),Ivory,true);
            LootOverlayResetButtons.Draw(g,1,LogicalSize);
        }
        finally {g.Restore(state);}
    }

    static string Duration(TimeSpan value)
    {value=value<TimeSpan.Zero?TimeSpan.Zero:value;return value.TotalHours>=1?$"{(long)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}":$"{value.Minutes:00}:{value.Seconds:00}";}

    static void Text(Graphics g,string value,string family,float size,RectangleF bounds,Color color,bool centered=false)
    {
        using var fontFamily=new FontFamily(family);
        using var format=new StringFormat(StringFormat.GenericTypographic){Alignment=centered?StringAlignment.Center:StringAlignment.Near,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap};
        while(size>5)
        {
            using var font=new Font(fontFamily,size,FontStyle.Regular,GraphicsUnit.Pixel);
            if(g.MeasureString(value,font,PointF.Empty,format).Width<=bounds.Width-5)break;
            size-=.5f;
        }
        using var path=new GraphicsPath();path.AddString(value,fontFamily,0,size,bounds,format);
        using var outline=new Pen(Shadow,3){LineJoin=LineJoin.Round};using var brush=new SolidBrush(color);
        g.DrawPath(outline,path);g.FillPath(brush,path);
    }

    static void Stroke(Graphics g,GraphicsPath path,Color color,float width)
    {
        using var outline=new Pen(Shadow,width+3){LineJoin=LineJoin.Round,StartCap=LineCap.Round,EndCap=LineCap.Round};
        using var pen=new Pen(color,width){LineJoin=LineJoin.Round,StartCap=LineCap.Round,EndCap=LineCap.Round};
        g.DrawPath(outline,path);g.DrawPath(pen,path);
    }

}
