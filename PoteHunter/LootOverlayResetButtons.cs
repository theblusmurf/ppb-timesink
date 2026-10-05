using System.Drawing.Drawing2D;

namespace PoteHunter;

/// <summary>Shared painted controls, including opaque hit areas on layered overlays.</summary>
internal static class LootOverlayResetButtons
{
    internal static RectangleF LootBounds(int design,Size size)=>design switch
    {
        1=>new(48,244,150,28),
        3=>new(10,160,130,22),
        _=>new(10,size.Height-51,130,26)
    };
    internal static RectangleF TimerBounds(int design,Size size)=>design switch
    {
        1=>new(542,244,150,28),
        3=>new(540,160,130,22),
        _=>new(size.Width-140,size.Height-51,130,26)
    };

    internal static void Draw(Graphics graphics,int design,Size size)
    {
        // Always opaque: transparent layered pixels pass mouse clicks through to the game.
        using var fill=new SolidBrush(ImperialTheme.Raised);
        using var border=new Pen(ImperialTheme.Border,1);
        using var accent=new Pen(ImperialTheme.Accent,2);
        using var text=new SolidBrush(ImperialTheme.Text);
        using var font=new Font("Segoe UI",design==3?11:12,FontStyle.Regular,GraphicsUnit.Pixel);
        using var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};
        foreach(var item in new[]{(LootBounds(design,size),"Reset loot"),(TimerBounds(design,size),"Reset timer")})
        {
            graphics.FillRectangle(fill,item.Item1);
            graphics.DrawRectangle(border,item.Item1.X,item.Item1.Y,item.Item1.Width,item.Item1.Height);
            graphics.DrawLine(accent,item.Item1.X+1,item.Item1.Y+5,item.Item1.X+1,item.Item1.Bottom-5);
            graphics.DrawString(item.Item2,font,text,item.Item1,format);
        }
    }
}
