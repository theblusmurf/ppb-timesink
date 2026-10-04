using System.Collections.ObjectModel;

namespace PoteHunter;

/// <summary>A copied observational view. This never authorizes player attacks.</summary>
internal readonly record struct SentinelPlayerMarker(uint Id,uint Generation,string Name,Vec Position,
    PlayerFaction Faction,PlayerRelation Relation,bool KnownAlive,bool ConfirmedDead=false);

internal sealed class SentinelRadarSnapshot
{
    internal const double Range=25;
    readonly ReadOnlyCollection<SentinelPlayerMarker> players;
    internal bool Fresh {get;}
    internal bool KnownAlive {get;}
    internal Vec SelfPosition {get;}
    internal string ZoneLabel {get;}
    internal ZoneCombatRule ZoneRule {get;}
    internal double Radius {get;}
    internal IReadOnlyList<SentinelPlayerMarker> Players=>players;
    internal SentinelPlayerMarker? NearestEnemy {get;}
    internal int EnemyCount {get;}

    internal SentinelRadarSnapshot(bool fresh,bool knownAlive,Vec selfPosition,string zoneLabel,
        ZoneCombatRule zoneRule,IEnumerable<SentinelPlayerMarker> observations,double range=Range)
    {
        Fresh=fresh&&selfPosition.Finite;KnownAlive=knownAlive;SelfPosition=selfPosition;
        ZoneLabel=string.IsNullOrWhiteSpace(zoneLabel)?"Unknown zone":zoneLabel;
        ZoneRule=zoneRule is ZoneCombatRule.PvP or ZoneCombatRule.Safe?zoneRule:ZoneCombatRule.Unknown;
        Radius=BoundRadius(range);
        var copied=Fresh?observations.GroupBy(p=>p.Id).Where(group=>group.Key!=0&&group.Count()==1).Select(group=>group.Single())
            .Where(p=>!p.ConfirmedDead&&p.Position.Finite&&p.Relation!=PlayerRelation.Self&&
                (p.Position-selfPosition).Length<=Radius)
            .Select(p=>ZoneRule!=ZoneCombatRule.PvP&&p.Relation==PlayerRelation.Enemy?
                p with{Relation=ZoneRule==ZoneCombatRule.Safe?PlayerRelation.OpposingSafe:PlayerRelation.Unknown}:p)
            .OrderBy(p=>(p.Position-selfPosition).Length).Take(128).ToArray():[];
        players=Array.AsReadOnly(copied);
        var enemies=copied.Where(p=>p.Relation==PlayerRelation.Enemy).ToArray();
        EnemyCount=enemies.Length;NearestEnemy=enemies.Length==0?null:enemies[0];
    }

    internal static SentinelRadarSnapshot Unavailable(string zoneLabel,ZoneCombatRule zoneRule,double range=Range)
        =>new(false,false,default,zoneLabel,zoneRule,[],range);
    internal static double BoundRadius(double range)=>Math.Clamp(double.IsFinite(range)?range:Range,1,100);
}

internal static class SentinelRadarPresentation
{
    internal static readonly Color Enemy=Color.FromArgb(255,94,124);
    internal static readonly Color Party=Color.FromArgb(127,199,174);
    internal static Color MarkerColor(PlayerRelation relation)=>relation switch
    {
        PlayerRelation.Enemy=>Enemy,PlayerRelation.Party=>Party,
        PlayerRelation.SameFaction=>ImperialTheme.RouteBlue,
        PlayerRelation.OpposingSafe=>ImperialTheme.Gold,_=>ImperialTheme.Muted
    };

    // The map's Y axis is north; this is a world bearing, never the character's facing.
    internal static string Bearing(Vec delta)
    {
        if(!delta.Finite)return "Unknown bearing";
        if(delta.Length<.05)return "At your position";
        double clockwise=Math.Atan2(delta.X,delta.Y)*180/Math.PI;
        int index=((int)Math.Floor((clockwise+22.5)/45)%8+8)%8;
        return new[]{"N","NE","E","SE","S","SW","W","NW"}[index];
    }

    internal static PointF RadarPoint(Vec delta,PointF center,float radius,double range=SentinelRadarSnapshot.Range)
    {
        range=SentinelRadarSnapshot.BoundRadius(range);
        return new(center.X+(float)(delta.X/range)*radius,center.Y-(float)(delta.Y/range)*radius);
    }

    internal static Size FitSize(Size available)
    {
        if(available.Width<=0||available.Height<=0)return Size.Empty;
        double scale=Math.Min(1,Math.Min(available.Width/460d,available.Height/250d));
        return new(Math.Max(1,(int)Math.Floor(460*scale)),Math.Max(1,(int)Math.Floor(250*scale)));
    }

    internal static Point ClampPosition(Point position,Size size,Rectangle available)
        =>new(Math.Clamp(position.X,available.Left,Math.Max(available.Left,available.Right-size.Width)),
            Math.Clamp(position.Y,available.Top,Math.Max(available.Top,available.Bottom-size.Height)));
}
