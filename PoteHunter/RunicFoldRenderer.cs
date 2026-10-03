using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PoteHunter;

/// <summary>Two rows of live loot totals, with a transparent medieval frame.</summary>
internal static class RunicFoldRenderer
{
    internal static readonly Size LogicalSize=new(740,280);
    static readonly string[] Resources=["Gold","Silvin","Mithril","Iternium","Fehu","Gems"];
    static readonly Color Brass=ImperialTheme.Gold;
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
            frame.AddPolygon([new(14,8),new(220,8),new(236,27),new(220,46),new(14,46),new(4,27)]);
            frame.StartFigure();frame.AddLines([new(226,8),new(716,8),new(736,27),new(716,46),new(226,46)]);
            frame.StartFigure();frame.AddLine(20,141,720,141);
            frame.StartFigure();frame.AddLine(20,258,274,258);
            frame.StartFigure();frame.AddLine(466,258,720,258);
            for(int row=0;row<2;row++)for(int col=1;col<3;col++)
            {frame.StartFigure();frame.AddLine(16+col*236,62+row*92,16+col*236,125+row*92);}
            Stroke(g,frame,Brass,1.4f);
            foreach(var point in new[]{new PointF(18,141),new PointF(370,141),new PointF(722,141),new PointF(18,258),new PointF(722,258)})
                Diamond(g,point,6);
            using var corners=new GraphicsPath();
            corners.AddLines([new(5,243),new(17,231),new(29,258),new(43,270),new(16,268),new(5,243)]);
            corners.StartFigure();corners.AddLines([new(735,243),new(723,231),new(711,258),new(697,270),new(724,268),new(735,243)]);
            Stroke(g,corners,Brass,2);
            Text(g,"LOOT TRACKER","Georgia",19,new(26,12,191,30),Ivory);
            Text(g,$"Session {Duration(snapshot.Elapsed)}  ·  Active {Duration(snapshot.RateElapsed)}  ·  Zone {snapshot.Zone}","Segoe UI",14,new(244,13,375,28),Ivory);
            Text(g,"drag to move","Segoe UI",11,new(636,14,90,25),Brass);
            for(int row=0;row<3;row++)for(int col=0;col<3;col++)
            {using var brush=new SolidBrush(Brass);g.FillEllipse(brush,620+col*4,22+row*4,2,2);}
            for(int i=0;i<Resources.Length;i++)
            {
                string name=Resources[i];float x=22+i%3*236,y=65+i/3*92;
                using(var tile=CrownfireControls.Frame(new RectangleF(x-6,y-4,226,86)))
                    Stroke(g,tile,Color.FromArgb(146,106,56),1);
                Icon(g,name,new(x,y,58,62));
                Text(g,LootTrackerSnapshot.DisplayName(name),"Georgia",18,new(x+68,y+1,155,25),Ivory);
                Text(g,snapshot.AmountText(name),"Consolas",27,new(x+68,y+24,155,30),Ivory);
                Text(g,$"{snapshot.RateText(name)}/h","Consolas",15,new(x+68,y+55,155,22),Brass);
            }
            using var footer=new GraphicsPath();footer.AddPolygon([new(280,244),new(460,244),new(474,258),new(460,272),new(280,272),new(266,258)]);
            Stroke(g,footer,Brass,1.4f);
            Text(g,$"{snapshot.Sources.Sum(source=>source.Kills):N0} kills  ·  {snapshot.Sources.Sum(source=>source.Drops):N0} drops","Segoe UI",14,new(280,245,180,25),Ivory,true);
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

    static void Diamond(Graphics g,PointF center,float radius)
    {
        using var path=new GraphicsPath();path.AddPolygon(new PointF[]{new(center.X,center.Y-radius),new(center.X+radius,center.Y),new(center.X,center.Y+radius),new(center.X-radius,center.Y)});
        Stroke(g,path,Brass,1.4f);
    }

    static void Icon(Graphics g,string resource,RectangleF bounds)
    {
        if(resource!="Gold")
        {
            GameLootIcons.Draw(g,resource,bounds);
            return;
        }
        var state=g.Save();g.TranslateTransform(bounds.X,bounds.Y);g.ScaleTransform(bounds.Width/58,bounds.Height/62);
        try
        {
            for(int i=0;i<3;i++)
            {
                using var fill=new SolidBrush(Color.FromArgb(168+i*17,119+i*12,38));
                using var rim=new Pen(Brass,1.6f);using var dark=new Pen(Shadow,4);
                var rect=new RectangleF(3,31-i*10,36,18);g.FillEllipse(fill,rect);g.DrawEllipse(dark,rect);g.DrawEllipse(rim,rect);
            }
            using var coin=new GraphicsPath();coin.AddEllipse(26,28,26,27);using var gold=new SolidBrush(Color.FromArgb(218,167,66));
            g.FillPath(gold,coin);Stroke(g,coin,Brass,1.3f);
            using var mark=new GraphicsPath();mark.AddLines([new(39,33),new(39,49),new(35,44),new(43,38)]);Stroke(g,mark,Color.FromArgb(113,76,24),1.2f);
        }
        finally {g.Restore(state);}
    }
}
