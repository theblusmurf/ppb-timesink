using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace PoteHunter;

internal static class SentinelRadarRenderer
{
    internal static readonly Size LogicalSize=new(460,250);
    internal const int HeaderHeight=30;

    internal static Bitmap Render(SentinelRadarSnapshot snapshot,Size? size=null)
    {
        Size dimensions=size??LogicalSize;
        var bitmap=new Bitmap(Math.Max(1,dimensions.Width),Math.Max(1,dimensions.Height));
        using var graphics=Graphics.FromImage(bitmap);Draw(graphics,bitmap.Size,snapshot);return bitmap;
    }

    internal static void Draw(Graphics graphics,Size size,SentinelRadarSnapshot snapshot)
    {
        if(size.Width<1||size.Height<1)return;
        var state=graphics.Save();
        try
        {
            graphics.ScaleTransform(size.Width/(float)LogicalSize.Width,size.Height/(float)LogicalSize.Height);
            graphics.SmoothingMode=SmoothingMode.AntiAlias;
            graphics.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
            using(var fill=new SolidBrush(ImperialTheme.Window))graphics.FillRectangle(fill,0,0,460,250);
            using(var line=new Pen(ImperialTheme.Border))graphics.DrawRectangle(line,.5f,.5f,459,249);
            DrawHeader(graphics);
            DrawRadar(graphics,snapshot);
            DrawDetails(graphics,snapshot);
            string footer=snapshot.Fresh&&!snapshot.KnownAlive?$"{snapshot.Radius:0.#}-unit north-up · living HP unconfirmed · audio paused":
                $"{snapshot.Radius:0.#}-unit north-up radar · faction inferred · attackability unknown";
            Text(graphics,footer,7.3f,
                ImperialTheme.Muted,new(14,230,438,16));
        }
        finally {graphics.Restore(state);}
    }

    internal static void DrawHeader(Graphics graphics)
    {
        using var fill=new LinearGradientBrush(new Rectangle(0,0,460,HeaderHeight),ImperialTheme.Raised,ImperialTheme.Window,0f);
        graphics.FillRectangle(fill,1,1,458,HeaderHeight-1);
        using var gold=new Pen(ImperialTheme.Gold,1);
        graphics.DrawLine(gold,1,HeaderHeight,458,HeaderHeight);
        graphics.DrawEllipse(gold,12,9,12,12);
        graphics.DrawLine(gold,18,6,18,24);graphics.DrawLine(gold,9,15,27,15);
        Text(graphics,"SENTINEL RADAR",10,ImperialTheme.Gold,new(34,6,228,20),FontStyle.Regular,"Georgia");
        Text(graphics,"drag header to move",7.5f,ImperialTheme.Muted,new(320,9,124,17));
    }

    static void DrawRadar(Graphics graphics,SentinelRadarSnapshot snapshot)
    {
        var center=new PointF(110,130);const float radius=76;
        using var fill=new SolidBrush(ImperialTheme.Surface);
        using var outer=new Pen(ImperialTheme.Gold,1);
        using var line=new Pen(ImperialTheme.Border,1);
        graphics.FillEllipse(fill,23,43,174,174);graphics.DrawEllipse(outer,23,43,174,174);
        graphics.DrawEllipse(line,center.X-radius,center.Y-radius,radius*2,radius*2);
        graphics.DrawEllipse(line,center.X-radius/2,center.Y-radius/2,radius,radius);
        graphics.DrawLine(line,center.X-radius,center.Y,center.X+radius,center.Y);
        graphics.DrawLine(line,center.X,center.Y-radius,center.X,center.Y+radius);
        Text(graphics,"N",8,ImperialTheme.Gold,new(105,32,15,15));
        if(!snapshot.Fresh)
        {
            using var shade=new SolidBrush(Color.FromArgb(180,ImperialTheme.Window));
            graphics.FillEllipse(shade,34,54,152,152);
            Text(graphics,"NO LIVE DATA",9,ImperialTheme.Muted,new(51,113,134,22),FontStyle.Bold);
            Text(graphics,"Waiting for client",7.5f,ImperialTheme.Muted,new(54,139,130,18));
            return;
        }
        var clip=graphics.Save();
        try
        {
            using var path=new GraphicsPath();path.AddEllipse(center.X-radius-6,center.Y-radius-6,(radius+6)*2,(radius+6)*2);
            graphics.SetClip(path,CombineMode.Intersect);
            // Draw enemy diamonds last, so an overlap cannot hide them under neutral markers.
            foreach(var player in snapshot.Players.OrderBy(p=>p.Relation==PlayerRelation.Enemy?1:0))
            {
                var point=SentinelRadarPresentation.RadarPoint(player.Position-snapshot.SelfPosition,center,radius,snapshot.Radius);
                DrawMarker(graphics,point,player.Relation,player.Relation==PlayerRelation.Enemy?5.5f:4f);
            }
            using var self=new SolidBrush(ImperialTheme.Text);
            using var border=new Pen(ImperialTheme.Window,2);
            graphics.FillEllipse(self,center.X-4,center.Y-4,8,8);graphics.DrawEllipse(border,center.X-5,center.Y-5,10,10);
        }
        finally {graphics.Restore(clip);}
    }

    static void DrawDetails(Graphics graphics,SentinelRadarSnapshot snapshot)
    {
        Text(graphics,snapshot.ZoneLabel,8,ImperialTheme.Muted,new(214,41,233,19));
        string title=snapshot.ZoneRule switch{ZoneCombatRule.PvP=>"PvP · opposing factions",ZoneCombatRule.Safe=>"Non-PvP · alerts suppressed",_=>"PvP rules unverified"};
        Color statusColor=snapshot.ZoneRule==ZoneCombatRule.PvP&&snapshot.EnemyCount>0?SentinelRadarPresentation.Enemy:ImperialTheme.Gold;
        using(var status=new SolidBrush(statusColor))graphics.FillEllipse(status,215,65,5,5);
        Text(graphics,title,8,statusColor,new(226,59,220,18));
        if(!snapshot.Fresh)
        {
            Text(graphics,"Reading unavailable",14,ImperialTheme.Text,new(213,85,229,26),FontStyle.Regular,"Georgia");
            Text(graphics,"Stale players are cleared",8.5f,ImperialTheme.Muted,new(214,118,229,20));
        }
        else if(snapshot.NearestEnemy is SentinelPlayerMarker enemy)
        {
            string name=string.IsNullOrWhiteSpace(enemy.Name)?$"Player {enemy.Id:X8}":enemy.Name;
            Text(graphics,name,16,ImperialTheme.Text,new(213,84,230,29),FontStyle.Regular,"Georgia");
            Text(graphics,PlayerRecognition.FactionLabel(enemy.Faction),8.5f,ImperialTheme.Muted,new(214,114,231,19));
            Vec delta=enemy.Position-snapshot.SelfPosition;double distance=delta.Length;
            Text(graphics,$"{distance:0.0}",25,ImperialTheme.Gold,new(212,134,130,42),FontStyle.Regular,"Georgia");
            string bearing=SentinelRadarPresentation.Bearing(delta);
            Text(graphics,bearing,16,SentinelRadarPresentation.Enemy,new(354,144,93,30),FontStyle.Bold);
            Text(graphics,"map units",7.5f,ImperialTheme.Muted,new(214,175,125,17));
            Text(graphics,$"{snapshot.EnemyCount} enem{(snapshot.EnemyCount==1?"y":"ies")} within {snapshot.Radius:0.#}",7.5f,ImperialTheme.Muted,new(214,194,231,17));
        }
        else
        {
            string name=snapshot.ZoneRule==ZoneCombatRule.Unknown?"Status unknown":snapshot.ZoneRule==ZoneCombatRule.Safe?"Non-PvP zone":"No enemy nearby";
            Text(graphics,name,16,ImperialTheme.Text,new(213,84,230,29),FontStyle.Regular,"Georgia");
            Text(graphics,snapshot.ZoneRule==ZoneCombatRule.Unknown?"Enemy alerts wait for verified rules":snapshot.ZoneRule==ZoneCombatRule.Safe?"Opposing factions stay non-PvP":$"Watching the surrounding {snapshot.Radius:0.#} units",8,
                ImperialTheme.Muted,new(214,118,231,38));
            Text(graphics,$"{snapshot.Players.Count} nearby player{(snapshot.Players.Count==1?"":"s")}",8.5f,ImperialTheme.Gold,new(214,158,231,22));
        }
        // Status is deliberately never called Friendly: same-race Guild Wars are possible.
        Legend(graphics,new(18,221),PlayerRelation.Enemy,"Enemy");
        Legend(graphics,new(99,221),PlayerRelation.Party,"Party");
        Legend(graphics,new(168,221),PlayerRelation.SameFaction,"Same faction");
        Legend(graphics,new(299,221),PlayerRelation.Unknown,"Unknown");
    }

    static void Legend(Graphics graphics,PointF point,PlayerRelation relation,string label)
    {
        DrawMarker(graphics,point,relation,3);
        Text(graphics,label,7.2f,ImperialTheme.Muted,new(point.X+7,point.Y-8,108,17));
    }

    static void DrawMarker(Graphics graphics,PointF point,PlayerRelation relation,float radius)
    {
        using var brush=new SolidBrush(SentinelRadarPresentation.MarkerColor(relation));
        if(relation is PlayerRelation.Enemy or PlayerRelation.OpposingSafe)
            graphics.FillPolygon(brush,new PointF[]{new(point.X,point.Y-radius),new(point.X+radius,point.Y),new(point.X,point.Y+radius),new(point.X-radius,point.Y)});
        else if(relation==PlayerRelation.Party)
            graphics.FillRectangle(brush,point.X-radius,point.Y-radius,radius*2,radius*2);
        else if(relation==PlayerRelation.SameFaction)
        {
            using var pen=new Pen(ImperialTheme.RouteBlue,1.5f);
            graphics.DrawEllipse(pen,point.X-radius,point.Y-radius,radius*2,radius*2);
        }
        else graphics.FillEllipse(brush,point.X-radius,point.Y-radius,radius*2,radius*2);
    }

    static void Text(Graphics graphics,string text,float points,Color color,RectangleF bounds,
        FontStyle style=FontStyle.Regular,string family="Segoe UI")
    {
        using var font=new Font(family,points,style);
        using var brush=new SolidBrush(color);
        using var format=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap};
        graphics.DrawString(text,font,brush,bounds,format);
    }
}
