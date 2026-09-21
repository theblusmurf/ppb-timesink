namespace PoteHunter;

public sealed partial class HunterForm
{
    void DrawDirectionCone(Graphics g,Size canvasSize)
    {
        if(canvasSize.Width<=0 || canvasSize.Height<=0)return;
        double heading=0;
        try{if(connected)heading=world.PlayerHeading();}catch{ }
        Vec forward=Movement.FromClientHeading(heading);
        float span=(float)NavigationViewRadius(),scale=Math.Min(canvasSize.Width,canvasSize.Height)/(span*2);
        float cx=canvasSize.Width/2f,cy=canvasSize.Height/2f;
        float radius=(float)Math.Min(span,25)*scale;
        Vec left=Movement.Rotate(forward,CombatPositioning.ConeHalfAngle),right=Movement.Rotate(forward,-CombatPositioning.ConeHalfAngle);
        PointF Point(Vec v)=>new(cx+(float)(v.X*radius),cy-(float)(v.Y*radius));
        var tip=new PointF(cx,cy);
        var polygon=new[]{tip,Point(left),Point(right)};
        using var fill=new SolidBrush(Color.FromArgb(38,70,180,255));
        using var edge=new Pen(Color.FromArgb(150,90,205,255),1.5f);
        g.FillPolygon(fill,polygon);g.DrawPolygon(edge,polygon);
        using var font=new Font("Segoe UI",7.5f,FontStyle.Bold);
        g.DrawString("Attack cone",font,Brushes.LightSkyBlue,cx+5,cy+5);
    }
}

