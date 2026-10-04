using System.Drawing.Drawing2D;

namespace PoteHunter;

/// <summary>Presentation only. Native controls retain their input, bindings and accessibility.</summary>
internal static class CrownfireControls
{
    internal static GraphicsPath Frame(RectangleF r,float radius=13)
    {
        float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
        var p=new GraphicsPath();
        p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);
        p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }

    internal static void Button(Button button)
    {
        bool hover=false,pressed=false;
        button.MouseEnter+=(_,_)=>{hover=true;button.Invalidate();};
        button.MouseLeave+=(_,_)=>{hover=false;pressed=false;button.Invalidate();};
        button.MouseDown+=(_,_)=>{pressed=true;button.Invalidate();};
        button.MouseUp+=(_,_)=>{pressed=false;button.Invalidate();};
        button.EnabledChanged+=(_,_)=>button.Invalidate();
        button.Paint+=(_,e)=>
        {
            if(button.Width<3 || button.Height<3)return;
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            g.Clear(button.Parent?.BackColor??ImperialTheme.Surface);
            bool selected=button.BackColor==Color.FromArgb(55,39,23) || button.BackColor==ImperialTheme.Gold;
            Color face=button.Enabled?button.BackColor:ImperialTheme.Surface;
            using var shape=Frame(new RectangleF(1,1,button.Width-3,button.Height-3));
            using var fill=new LinearGradientBrush(button.ClientRectangle,ControlPaint.Light(face,hover?.18f:.08f),
                pressed?ControlPaint.Dark(face,.18f):face,90f);
            g.FillPath(fill,shape);
            using var edge=new Pen(button.Enabled&&(selected||hover)?ImperialTheme.Gold:ImperialTheme.Border,selected?1.7f:1);
            g.DrawPath(edge,shape);
            var textBounds=new Rectangle(9,2,Math.Max(1,button.Width-18),Math.Max(1,button.Height-4));
            string icon=button.Name.StartsWith("fieldNav")?button.Text:
                button.Name.StartsWith("overviewTarget")?button.Text:
                button.Text.StartsWith("Start")?"Play":button.Text.StartsWith("Stop")?"Stop":
                button.Text=="Connect"?"Connect":button.Name.StartsWith("overviewRoute")?"Route":"";
            Color ink=button.Enabled?button.ForeColor:ImperialTheme.Muted;
            bool target=button.Name.StartsWith("overviewTarget");
            if(icon.Length>0)
            {
                var rect=target?new RectangleF((button.Width-48)/2f,5,48,48):new RectangleF(10,(button.Height-19)/2f,19,19);
                if(target)ImperialTheme.DrawTarget(g,icon,rect);else Glyph(g,icon,rect,ink);
                if(target)textBounds=new(4,53,button.Width-8,button.Height-55);
                else {textBounds.X+=26;textBounds.Width=Math.Max(1,textBounds.Width-26);}
            }
            var flags=TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix;
            flags|=target || button.TextAlign==ContentAlignment.MiddleCenter?TextFormatFlags.HorizontalCenter:TextFormatFlags.Left;
            TextRenderer.DrawText(g,button.Text,button.Font,textBounds,ink,flags);
            if(button.Focused)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(button.ClientRectangle,-5,-5),ink,face);
        };
    }

    internal static void Toggle(CheckBox check)
    {
        if(check.Appearance==Appearance.Button)return;
        // Native CheckBox.AutoSize only reserves its square glyph. Reserve the
        // actual switch width, so no setting caption wraps into a clipped row.
        if(check.AutoSize)
        {
            check.AutoSize=false;
            void MeasureCaption()=>check.Size=new Size(TextRenderer.MeasureText(check.Text,check.Font,
                new Size(int.MaxValue,int.MaxValue),TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width+52,Math.Max(25,check.Font.Height+4));
            check.FontChanged+=(_,_)=>MeasureCaption();check.TextChanged+=(_,_)=>MeasureCaption();MeasureCaption();
        }
        check.Paint+=(_,e)=>
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            using var background=new SolidBrush(check.BackColor);g.FillRectangle(background,check.ClientRectangle);
            float y=(check.Height-20)/2f;
            using var track=Frame(new RectangleF(1,y,38,20),10);
            using var fill=new LinearGradientBrush(new RectangleF(1,y,38,20),
                check.Checked&&check.Enabled?ImperialTheme.Gold:ImperialTheme.Raised,
                check.Checked&&check.Enabled?Color.FromArgb(144,91,33):ImperialTheme.Surface,90);
            using var border=new Pen(check.Enabled&&check.Checked?ImperialTheme.Gold:ImperialTheme.Border);
            g.FillPath(fill,track);g.DrawPath(border,track);
            using var knob=new SolidBrush(check.Enabled?ImperialTheme.Text:ImperialTheme.Muted);
            g.FillEllipse(knob,check.Checked?21:4,y+3,14,14);
            TextRenderer.DrawText(g,check.Text,check.Font,new Rectangle(47,0,Math.Max(1,check.Width-47),check.Height),
                check.Enabled?check.ForeColor:ImperialTheme.Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            if(check.Focused)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(check.ClientRectangle,-1,-1));
        };
    }

    internal static void Glyph(Graphics g,string name,RectangleF box,Color color)
    {
        var state=g.Save();g.TranslateTransform(box.X,box.Y);g.ScaleTransform(box.Width/24,box.Height/24);
        using var pen=new Pen(color,1.7f){LineJoin=LineJoin.Round,StartCap=LineCap.Round,EndCap=LineCap.Round};
        using var brush=new SolidBrush(color);
        switch(name)
        {
            case "Gold":g.DrawEllipse(pen,3,3,15,7);g.DrawArc(pen,3,5,15,8,0,180);g.DrawArc(pen,3,8,15,8,0,180);g.DrawEllipse(pen,13,12,8,10);break;
            case "Play":g.FillPolygon(brush,new PointF[]{new(6,3),new(21,12),new(6,21)});break;
            case "Stop":g.FillRectangle(brush,5,5,14,14);break;
            case "Overview":g.DrawLines(pen,new PointF[]{new(2,10),new(12,2),new(22,10)});g.DrawRectangle(pen,5,10,14,12);break;
            case "Mimic":g.DrawRectangle(pen,2,7,20,14);g.DrawArc(pen,2,2,20,13,180,180);g.DrawLine(pen,2,12,22,12);g.FillRectangle(brush,10,10,4,6);break;
            case "Tower":g.DrawRectangle(pen,6,6,12,16);g.DrawLines(pen,new PointF[]{new(4,6),new(4,2),new(9,2),new(9,6),new(15,6),new(15,2),new(20,2),new(20,6)});g.DrawRectangle(pen,10,15,4,7);break;
            case "Pulkhan":g.DrawEllipse(pen,5,7,14,14);g.DrawLines(pen,new PointF[]{new(6,9),new(2,5),new(3,1)});g.DrawLines(pen,new PointF[]{new(18,9),new(22,5),new(21,1)});g.DrawLine(pen,8,13,10,14);g.DrawLine(pen,14,14,16,13);break;
            case "Tribal":g.DrawPolygon(pen,new PointF[]{new(12,2),new(21,7),new(18,19),new(12,23),new(6,19),new(3,7)});g.DrawLine(pen,7,10,10,12);g.DrawLine(pen,14,12,17,10);g.DrawLine(pen,12,12,12,18);break;
            case "Monitor":for(int i=0;i<3;i++)g.FillRectangle(brush,3+i*7,14-i*5,4,8+i*5);break;
            case "Routes":case "Navigation":case "Route":g.DrawEllipse(pen,2,2,20,20);g.DrawPolygon(pen,new PointF[]{new(17,6),new(14,15),new(6,18),new(9,9)});break;
            case "Recovery":case "Support":g.DrawEllipse(pen,2,3,20,16);g.DrawLine(pen,7,18,5,23);break;
            case "Settings":case "Index":g.DrawRectangle(pen,3,3,18,18);g.DrawLine(pen,12,3,12,21);break;
            case "Connect":g.DrawEllipse(pen,2,4,12,8);g.DrawEllipse(pen,10,12,12,8);g.DrawLine(pen,9,10,15,14);break;
            default:g.DrawEllipse(pen,5,5,14,14);g.DrawLine(pen,12,1,12,6);g.DrawLine(pen,12,18,12,23);g.DrawLine(pen,1,12,6,12);g.DrawLine(pen,18,12,23,12);break;
        }
        g.Restore(state);
    }
}
