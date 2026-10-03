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
        float left=4,top=4,right=logicalSize.Width-4,bottom=logicalSize.Height-4,radius=12;
        using var panel=new GraphicsPath();
        panel.AddArc(left,top,radius*2,radius*2,180,90);
        panel.AddArc(right-radius*2,top,radius*2,radius*2,270,90);
        panel.AddArc(right-radius*2,bottom-radius*2,radius*2,radius*2,0,90);
        panel.AddArc(left,bottom-radius*2,radius*2,radius*2,90,90);
        panel.CloseFigure();
        using var fill=new SolidBrush(Color.FromArgb(alpha,18,23,20));
        graphics.FillPath(fill,panel);
    }
}
