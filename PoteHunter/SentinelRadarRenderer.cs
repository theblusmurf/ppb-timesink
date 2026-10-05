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
            bool missingNames=snapshot.Enemies.Any(p=>string.IsNullOrWhiteSpace(p.Name));
            string footer=snapshot.Fresh&&!snapshot.KnownAlive?
                (missingNames?"Names unavailable: IDs shown · HP unconfirmed · audio paused":$"{snapshot.Radius:0.#}-unit north-up · HP unconfirmed · audio paused"):
                missingNames?"Names unavailable: IDs shown · × counts players · attackability unknown":
                    $"{snapshot.Radius:0.#}-unit north-up · × overlapping players · attackability unknown";
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
            foreach(var player in snapshot.Players.Where(p=>p.Relation!=PlayerRelation.Enemy))
            {
                var point=SentinelRadarPresentation.RadarPoint(player.Position-snapshot.SelfPosition,center,radius,snapshot.Radius);
                DrawMarker(graphics,point,player.Relation,4f);
            }
            using var self=new SolidBrush(ImperialTheme.Text);
            using var border=new Pen(ImperialTheme.Text,1.5f);
            graphics.FillEllipse(self,center.X-2,center.Y-2,4,4);graphics.DrawEllipse(border,center.X-6,center.Y-6,12,12);
            // Exact enemy points follow the self marker: even a close or
            // coincident enemy must remain visible at a 100-unit watch radius.
            foreach(var player in snapshot.Enemies)
            {
                var point=SentinelRadarPresentation.RadarPoint(player.Position-snapshot.SelfPosition,center,radius,snapshot.Radius);
                DrawMarker(graphics,point,PlayerRelation.Enemy,4f);
            }
        }
        finally {graphics.Restore(clip);}
        foreach(var badge in EnemyBadges(graphics,snapshot,center,radius))
        {
            using var leader=new Pen(Color.FromArgb(190,SentinelRadarPresentation.Enemy),1);
            foreach(var member in badge.Cluster.Members)
            {
                PointF end=new(Math.Clamp(member.Point.X,badge.Bounds.Left,badge.Bounds.Right),
                    Math.Clamp(member.Point.Y,badge.Bounds.Top,badge.Bounds.Bottom));
                graphics.DrawLine(leader,member.Point,end);
            }
            using var badgeFill=new SolidBrush(ImperialTheme.Window);
            using var outline=new Pen(SentinelRadarPresentation.Enemy,1);
            graphics.FillRectangle(badgeFill,badge.Bounds);graphics.DrawRectangle(outline,
                badge.Bounds.X,badge.Bounds.Y,badge.Bounds.Width,badge.Bounds.Height);
            Text(graphics,badge.Caption,7.5f,SentinelRadarPresentation.Enemy,
                new(badge.Bounds.X+3,badge.Bounds.Y+1,badge.Bounds.Width-4,badge.Bounds.Height-1),FontStyle.Bold);
        }
    }

    static void DrawDetails(Graphics graphics,SentinelRadarSnapshot snapshot)
    {
        Text(graphics,snapshot.ZoneLabel,8,ImperialTheme.Muted,new(214,41,233,19));
        string title=snapshot.ZoneRule switch{
            ZoneCombatRule.PvP when snapshot.EnemyCount>0=>$"PvP · {snapshot.EnemyCount} enem{(snapshot.EnemyCount==1?"y":"ies")} · {snapshot.Radius:0.#}u",
            ZoneCombatRule.PvP=>"PvP · enemy watch",ZoneCombatRule.Safe=>"Non-PvP · alerts suppressed",_=>"PvP rules unverified"};
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
            Text(graphics,NearestEnemyName(snapshot)!,14,
                ImperialTheme.Text,new(213,80,234,26),FontStyle.Regular,"Georgia");
            string faction=enemy.Faction switch{PlayerFaction.Kartefant=>"Human",PlayerFaction.Merkhadian=>"Akkan",_=>"Unknown faction"};
            Text(graphics,$"Nearest #{enemy.DisplayNumber} · {faction} · inferred",8,ImperialTheme.Gold,new(214,105,232,19));
            int row=0;
            foreach(var player in snapshot.Enemies.Take(5))
            {
                float y=126+row++*18;
                Text(graphics,$"#{player.DisplayNumber}",7.5f,SentinelRadarPresentation.Enemy,new(214,y,27,18),FontStyle.Bold);
                Text(graphics,PlayerRecognition.DisplayName(player.Id,player.Name),8,ImperialTheme.Text,new(242,y,133,18));
                Vec delta=player.Position-snapshot.SelfPosition;
                string bearing=delta.Length<.05?"HERE":SentinelRadarPresentation.Bearing(delta);
                Text(graphics,$"{delta.Length:0.0}u {bearing}",7.5f,ImperialTheme.Gold,new(377,y,70,18),alignment:StringAlignment.Far);
            }
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
        Legend(graphics,new(12,222),snapshot.ZoneRule==ZoneCombatRule.Safe?PlayerRelation.OpposingSafe:PlayerRelation.Enemy,
            snapshot.ZoneRule==ZoneCombatRule.Safe?"Opposing":"Enemy");
        Legend(graphics,new(99,222),PlayerRelation.Party,"Party");
        Legend(graphics,new(168,222),PlayerRelation.SameFaction,"Same faction");
        Legend(graphics,new(299,222),PlayerRelation.Unknown,"Unknown");
    }

    internal readonly record struct EnemyProjection(SentinelPlayerMarker Player,PointF Point);
    internal sealed record EnemyCluster(IReadOnlyList<EnemyProjection> Members,PointF Anchor);
    internal readonly record struct EnemyBadge(EnemyCluster Cluster,RectangleF Bounds,string Caption);

    internal static string? NearestEnemyName(SentinelRadarSnapshot snapshot)=>
        snapshot.NearestEnemy is SentinelPlayerMarker enemy?PlayerRecognition.DisplayName(enemy.Id,enemy.Name):null;

    internal static IReadOnlyList<EnemyCluster> EnemyClusters(SentinelRadarSnapshot snapshot,PointF center,float radius)
    {
        var projected=snapshot.Enemies.Select(p=>new EnemyProjection(p,
            SentinelRadarPresentation.RadarPoint(p.Position-snapshot.SelfPosition,center,radius,snapshot.Radius))).ToArray();
        int[] parent=Enumerable.Range(0,projected.Length).ToArray();
        int Root(int index){while(parent[index]!=index){parent[index]=parent[parent[index]];index=parent[index];}return index;}
        for(int a=0;a<projected.Length;a++)for(int b=a+1;b<projected.Length;b++)
        {
            float dx=projected[a].Point.X-projected[b].Point.X,dy=projected[a].Point.Y-projected[b].Point.Y;
            if(dx*dx+dy*dy<=121)parent[Root(b)]=Root(a);
        }
        return Array.AsReadOnly(Enumerable.Range(0,projected.Length).GroupBy(Root)
            .Select(group=>
            {
                var members=group.Select(index=>projected[index]).OrderBy(p=>p.Player.DisplayNumber).ToArray();
                // Anchor at a real member's position, never relocate an enemy to
                // make a label fit. Leaders identify the complete overlap group.
                var anchor=members.OrderBy(p=>(p.Player.Position-snapshot.SelfPosition).Length).First().Point;
                return new EnemyCluster(Array.AsReadOnly(members),anchor);
            })
            .OrderBy(group=>group.Members.Min(p=>(p.Player.Position-snapshot.SelfPosition).Length)).ToArray());
    }

    internal static IReadOnlyList<EnemyBadge> EnemyBadges(Graphics graphics,SentinelRadarSnapshot snapshot,PointF center,float radius)
    {
        var area=new RectangleF(12,48,190,164);
        var occupied=snapshot.Players.Select(p=>SentinelRadarPresentation.RadarPoint(p.Position-snapshot.SelfPosition,center,radius,snapshot.Radius))
            .Select(p=>new RectangleF(p.X-6,p.Y-6,12,12)).ToList();
        occupied.Add(new(center.X-8,center.Y-8,16,16));
        var result=new List<EnemyBadge>();
        using var font=new Font("Segoe UI",7.5f,FontStyle.Bold);
        foreach(var cluster in EnemyClusters(snapshot,center,radius))
        {
            string numbers=string.Join(",",cluster.Members.Take(3).Select(p=>p.Player.DisplayNumber));
            var first=cluster.Members[0].Player;
            string caption=cluster.Members.Count==1?$"#{first.DisplayNumber} {PlayerRecognition.DisplayName(first.Id,first.Name)}":
                $"×{cluster.Members.Count} #{numbers}{(cluster.Members.Count>3?$"+{cluster.Members.Count-3}":"")}";
            // Names may be longer than the radar pane. Ellipsis keeps the badge
            // away from other points while the roster retains the same identity.
            float width=Math.Clamp(graphics.MeasureString(caption,font).Width+7,22,146),height=18;
            var point=cluster.Anchor;
            var candidates=new List<PointF>{new(point.X+9,point.Y-23),new(point.X-width-9,point.Y-23),
                new(point.X+9,point.Y+8),new(point.X-width-9,point.Y+8),new(point.X-width/2,point.Y-29),new(point.X-width/2,point.Y+13)};
            // Dense groups may need a longer leader; staying in this pane keeps
            // badges clear of the roster, compass title, legend and footer.
            for(float y=area.Top;y<=area.Bottom-height;y+=20)
                foreach(float x in new[]{area.Left,area.Right-width,area.Left+(area.Width-width)/2})candidates.Add(new(x,y));
            foreach(var candidate in candidates)
            {
                var bounds=new RectangleF(Math.Clamp(candidate.X,area.Left,area.Right-width),
                    Math.Clamp(candidate.Y,area.Top,area.Bottom-height),width,height);
                var padded=RectangleF.Inflate(bounds,2,2);
                if(occupied.Any(previous=>previous.IntersectsWith(padded)))continue;
                result.Add(new(cluster,bounds,caption));occupied.Add(padded);break;
            }
        }
        return result.AsReadOnly();
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
        FontStyle style=FontStyle.Regular,string family="Segoe UI",StringAlignment alignment=StringAlignment.Near)
    {
        using var font=new Font(family,points,style);
        using var brush=new SolidBrush(color);
        using var format=new StringFormat{Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap,Alignment=alignment};
        graphics.DrawString(text,font,brush,bounds,format);
    }
}
