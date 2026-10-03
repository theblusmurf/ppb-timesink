using System.Drawing.Drawing2D;

namespace PoteHunter;

public sealed partial class HunterForm
{
    // Decorative drawing stays outside control text and hit targets. Native
    // controls continue to own focus, accessibility, sizing and keyboard input.
    static void DrawIronboundShield(Graphics graphics, Rectangle bounds)
    {
        var state=graphics.Save();
        try
        {
            graphics.SmoothingMode=SmoothingMode.AntiAlias;
            float x=bounds.X,y=bounds.Y,w=bounds.Width,h=bounds.Height;
            using var shape=new GraphicsPath();
            shape.AddPolygon(new[]{new PointF(x+w*.5f,y),new PointF(x+w,y+h*.18f),
                new PointF(x+w*.88f,y+h*.68f),new PointF(x+w*.5f,y+h),
                new PointF(x+w*.12f,y+h*.68f),new PointF(x,y+h*.18f)});
            using var fill=new SolidBrush(UiAccentDark);
            using var edge=new Pen(UiAccent,1.4f);
            graphics.FillPath(fill,shape);graphics.DrawPath(edge,shape);
            graphics.DrawLine(edge,x+w*.5f,y+h*.22f,x+w*.5f,y+h*.73f);
            graphics.DrawLine(edge,x+w*.29f,y+h*.38f,x+w*.71f,y+h*.38f);
        }
        finally {graphics.Restore(state);}
    }

    static void DrawIronboundCorners(Graphics graphics, Rectangle bounds)
    {
        if(bounds.Width<40 || bounds.Height<40)return;
        using var engraving=new Pen(UiBorder);
        // Small, low-contrast marks fit within the existing card padding.
        foreach(int side in new[]{0,1})
        {
            int x=side==0?5:bounds.Right-6,sign=side==0?1:-1;
            int y=bounds.Bottom-6;
            graphics.DrawLine(engraving,x,y,x+sign*12,y);
            graphics.DrawLine(engraving,x,y,x,y-12);
            graphics.DrawPolygon(engraving,new[]{new Point(x+sign*4,y-4),new Point(x+sign*7,y-7),
                new Point(x+sign*4,y-10),new Point(x+sign,y-7)});
        }
    }
}
