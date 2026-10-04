using System.Text.Json;

namespace PoteHunter;

internal static class SentinelRadarChecks
{
    internal static void Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var markers=new[]{
            new SentinelPlayerMarker(0x1001,1,"Ashen Rival",new(-13,13),PlayerFaction.Kartefant,PlayerRelation.Enemy,true),
            new SentinelPlayerMarker(0x1002,1,"Party member",new(-8,-10),PlayerFaction.Merkhadian,PlayerRelation.Party,true),
            new SentinelPlayerMarker(0x1003,1,"Same faction",new(10,3),PlayerFaction.Merkhadian,PlayerRelation.SameFaction,true),
            new SentinelPlayerMarker(0x1004,1,"Unknown player",new(13,-8),PlayerFaction.Unknown,PlayerRelation.Unknown,false),
            new SentinelPlayerMarker(0x1005,1,"At boundary",new(25,0),PlayerFaction.Kartefant,PlayerRelation.Enemy,true),
            new SentinelPlayerMarker(0x1006,1,"Outside",new(25.01,0),PlayerFaction.Kartefant,PlayerRelation.Enemy,true),
            new SentinelPlayerMarker(0x1007,1,"Invalid",new(double.NaN,0),PlayerFaction.Kartefant,PlayerRelation.Enemy,true)};
        var pvp=new SentinelRadarSnapshot(true,true,new(0,0),"Caernarvon · Zone 8",ZoneCombatRule.PvP,markers);
        Require(pvp.Players.Count==5&&pvp.EnemyCount==2&&pvp.NearestEnemy?.Name=="Ashen Rival","In-range nearest enemy must exclude non-finite/outside markers and include the 25-unit boundary");
        markers[0]=markers[0] with{Name="Replaced"};
        Require(pvp.NearestEnemy?.Name=="Ashen Rival","An accepted snapshot must not retain a mutable observations array");
        var uncertain=new SentinelPlayerMarker(0x2001,1,"Unknown HP",new(1,1),PlayerFaction.Kartefant,PlayerRelation.Enemy,false);
        var invalidIdentity=new SentinelRadarSnapshot(true,true,default,"Zone 8",ZoneCombatRule.PvP,
            [uncertain,uncertain with{Id=0x2002,Name="Dead",ConfirmedDead=true},
                uncertain with{Id=0x2003,Generation=1},uncertain with{Id=0x2003,Generation=2}]);
        Require(invalidIdentity.Players.Count==1&&invalidIdentity.NearestEnemy?.Id==uncertain.Id,
            "Unknown-HP players may remain visible; confirmed-dead and duplicate/ambiguous UID bodies must be excluded");
        var safe=new SentinelRadarSnapshot(true,true,default,"Almighty Land · Zone 12",ZoneCombatRule.Safe,pvp.Players);
        Require(safe.EnemyCount==0&&safe.NearestEnemy==null&&safe.Players.Count(p=>p.Relation==PlayerRelation.OpposingSafe)==2,"Non-PvP must suppress enemy status without losing visible opposing players");
        var unknown=new SentinelRadarSnapshot(true,true,default,"Unknown zone",ZoneCombatRule.Unknown,pvp.Players);
        Require(unknown.EnemyCount==0&&unknown.NearestEnemy==null,"Unverified zone cannot display an enemy based on prior PvP status");
        var stale=new SentinelRadarSnapshot(false,true,default,"Caernarvon · Zone 8",ZoneCombatRule.PvP,pvp.Players);
        Require(!stale.Fresh&&stale.Players.Count==0&&stale.EnemyCount==0&&stale.NearestEnemy==null,"Stale observations must clear the radar and nearest enemy");
        var invalidSelf=new SentinelRadarSnapshot(true,true,new(double.PositiveInfinity,0),"Zone 8",ZoneCombatRule.PvP,pvp.Players);
        Require(!invalidSelf.Fresh&&invalidSelf.Players.Count==0,"Non-finite self position cannot project player markers");
        foreach(var pair in new[]{(new Vec(0,1),"N"),(new Vec(1,1),"NE"),(new Vec(1,0),"E"),(new Vec(1,-1),"SE"),
            (new Vec(0,-1),"S"),(new Vec(-1,-1),"SW"),(new Vec(-1,0),"W"),(new Vec(-1,1),"NW")})
            Require(SentinelRadarPresentation.Bearing(pair.Item1)==pair.Item2,"World bearing must retain north-up coordinate signs");
        Require(SentinelRadarPresentation.Bearing(default)=="At your position"&&SentinelRadarPresentation.Bearing(new(double.NaN,0))=="Unknown bearing","Coincident and invalid bearings must not invent a heading");
        var center=new PointF(110,130);
        Require(SentinelRadarPresentation.RadarPoint(new(25,0),center,76)==new PointF(186,130)&&
            SentinelRadarPresentation.RadarPoint(new(0,25),center,76)==new PointF(110,54),"Radar projection must map east right and north up");
        var narrow=new SentinelRadarSnapshot(true,true,default,"Zone 8",ZoneCombatRule.PvP,pvp.Players,10);
        var wide=new SentinelRadarSnapshot(true,true,default,"Zone 8",ZoneCombatRule.PvP,
            [markers[4],markers[5],markers[4] with{Id=0x1008,Position=new(100,0)},markers[4] with{Id=0x1009,Position=new(100.01,0)}],100);
        Require(narrow.Radius==10&&narrow.EnemyCount==0&&wide.Radius==100&&wide.EnemyCount==3,
            "Adjustable radius must include its exact boundary and exclude players just outside");
        Require(SentinelRadarPresentation.RadarPoint(new(100,0),center,76,100)==new PointF(186,130)&&
            SentinelRadarPresentation.RadarPoint(new(0,10),center,76,10)==new PointF(110,54),
            "Changing watch radius must scale radar projection without changing cardinal directions");
        foreach(var bounds in new[]{(0d,1d),(101d,100d),(double.NaN,25d),(double.PositiveInfinity,25d)})
            Require(SentinelRadarSnapshot.Unavailable("Zone 8",ZoneCombatRule.PvP,bounds.Item1).Radius==bounds.Item2,
                "Invalid or non-finite watch radius must remain inside the supported 1–100 map-unit range");
        Require(SentinelRadarPresentation.FitSize(new(460,250))==new Size(460,250)&&SentinelRadarPresentation.FitSize(new(230,125))==new Size(230,125),"Small screen fit must preserve proportions");
        Rectangle monitor=new(-1920,-200,1200,800);
        Require(SentinelRadarPresentation.ClampPosition(new(-5000,-1000),new(460,250),monitor)==new Point(-1920,-200)&&
            SentinelRadarPresentation.ClampPosition(new(1000,1000),new(460,250),monitor)==new Point(-1180,350),"Clamping must preserve negative multi-monitor coordinates");
        var crowdedMarkers=new[]{
            new SentinelPlayerMarker(0x03b2,1,"",default,PlayerFaction.Kartefant,PlayerRelation.Enemy,true,DisplayNumber:1),
            new SentinelPlayerMarker(0x0676,1,"",new(1.5,0),PlayerFaction.Kartefant,PlayerRelation.Enemy,true,DisplayNumber:2),
            new SentinelPlayerMarker(0x07bd,1,"",new(1.5,0),PlayerFaction.Kartefant,PlayerRelation.Enemy,false,DisplayNumber:3),
            new SentinelPlayerMarker(0x088a,1,"Gale",new(-48,44),PlayerFaction.Kartefant,PlayerRelation.Enemy,true,DisplayNumber:4),
            new SentinelPlayerMarker(0x0912,1,"",new(42,-48),PlayerFaction.Kartefant,PlayerRelation.Enemy,true,DisplayNumber:5)};
        var crowded=new SentinelRadarSnapshot(true,true,default,"Caernarvon · Zone 8",ZoneCombatRule.PvP,crowdedMarkers,100);
        Require(crowded.EnemyCount==5&&crowded.Enemies.Select(p=>p.Id).Distinct().Count()==5&&crowded.NearestEnemy?.DisplayNumber==1,
            "Five nearby identities, including exact self/coincident positions and unknown HP, must remain individual enemies");
        var clusters=SentinelRadarRenderer.EnemyClusters(crowded,center,76);
        Require(clusters.Count==3&&clusters.Sum(p=>p.Members.Count)==5&&clusters[0].Members.Count==3,
            "Coincident and close projected enemies must form one accurate three-player overlap badge without losing member identities");
        Require(clusters.All(group=>group.Members.Any(member=>member.Point==group.Anchor))&&
            clusters.SelectMany(group=>group.Members).All(member=>member.Point==
                SentinelRadarPresentation.RadarPoint(member.Player.Position-crowded.SelfPosition,center,76,100)),
            "Clusters must retain actual coordinates and anchor on a real member rather than move enemies for label placement");
        var crossing=new SentinelRadarSnapshot(true,true,default,"Zone 8",ZoneCombatRule.PvP,
            crowdedMarkers.Select(p=>p with{Position=new(-p.Position.X,-p.Position.Y)}),100);
        Require(crossing.Enemies.All(p=>p.DisplayNumber==crowded.Enemies.Single(original=>original.Id==p.Id).DisplayNumber),
            "Supplied stable numbers must remain attached to UID/generation when distances or order change");
        var fallback=new SentinelRadarSnapshot(true,true,default,"Zone 8",ZoneCombatRule.PvP,
            crowdedMarkers.Select(p=>p with{DisplayNumber=0}).Reverse(),100);
        var fallbackCrossing=new SentinelRadarSnapshot(true,true,default,"Zone 8",ZoneCombatRule.PvP,
            crowdedMarkers.Select(p=>p with{DisplayNumber=0,Position=p.Id==0x03b2?new(90,0):p.Position}),100);
        Require(fallback.Enemies.All(p=>p.DisplayNumber==fallbackCrossing.Enemies.Single(other=>other.Id==p.Id).DisplayNumber),
            "Unassigned test numbers must follow deterministic UID ordering rather than nearest-distance ordering");
        Require(PlayerRecognition.DisplayName(0x03b2,"")=="Player 000003B2"&&PlayerRecognition.DisplayName(0x088a," Gale ")=="Gale"&&
            crowded.Enemies.Single(p=>p.Id==0x03b2).Name=="",
            "Blank names must remain raw blank data, with explicit UID display fallback rather than invented character names");
        using(var image=SentinelRadarRenderer.Render(crowded))
        using(var graphics=Graphics.FromImage(image))
        {
            var badges=SentinelRadarRenderer.EnemyBadges(graphics,crowded,center,76);
            Require(badges.Count==3&&badges.Sum(p=>p.Cluster.Members.Count)==5&&badges[0].Caption.StartsWith("×3 #1,2,3",StringComparison.Ordinal),
                "Every overlap badge must report its exact current member count and corresponding stable numbers");
            Require(badges.All(p=>new RectangleF(12,48,190,164).Contains(p.Bounds)&&!p.Bounds.IntersectsWith(new(102,122,16,16))),
                "Number/count badges must stay in the radar pane and clear the self position");
            Color nearSelf=image.GetPixel(110,130);
            Require(nearSelf.R>230&&nearSelf.G<140&&nearSelf.B>70,
                "The self marker must not cover an enemy at its exact position at the largest watch range");
            image.Save(Path.Combine(outputDirectory,"sentinel-radar-five-enemies-clustered.png"));
        }
        var many=new SentinelRadarSnapshot(true,true,default,"Zone 8",ZoneCombatRule.PvP,
            Enumerable.Range(1,20).Select(index=>new SentinelPlayerMarker((uint)index,1,"",new(1.5,0),PlayerFaction.Kartefant,PlayerRelation.Enemy,true,DisplayNumber:index)),100);
        Require(SentinelRadarRenderer.EnemyClusters(many,center,76).Single().Members.Count==20&&many.EnemyCount==20,
            "A dense badge and total count must retain all identities beyond the five-row visible list");
        using(var image=SentinelRadarRenderer.Render(many))image.Save(Path.Combine(outputDirectory,"sentinel-radar-twenty-enemies.png"));
        foreach(var sample in new[]{("clustered-non-pvp",new SentinelRadarSnapshot(true,true,default,"Almighty Land · Zone 12",ZoneCombatRule.Safe,crowdedMarkers,100)),
            ("clustered-unknown",new SentinelRadarSnapshot(true,true,default,"Unverified map",ZoneCombatRule.Unknown,crowdedMarkers,100)),
            ("clustered-stale",new SentinelRadarSnapshot(false,true,default,"Caernarvon · Zone 8",ZoneCombatRule.PvP,crowdedMarkers,100))})
        {
            Require(sample.Item2.Enemies.Count==0&&SentinelRadarRenderer.EnemyClusters(sample.Item2,center,76).Count==0,
                "Non-PvP, unverified and stale states cannot retain previous enemy numbers or cluster groups");
            using var image=SentinelRadarRenderer.Render(sample.Item2);image.Save(Path.Combine(outputDirectory,$"sentinel-radar-{sample.Item1}.png"));
        }
        foreach(var sample in new[]{("pvp",pvp),("non-pvp",safe),("unknown",unknown),("no-data",stale),
            ("no-enemy",new SentinelRadarSnapshot(true,true,default,"Caernarvon · Zone 8",ZoneCombatRule.PvP,[]))})
        {
            using var image=SentinelRadarRenderer.Render(sample.Item2);
            Require(image.Size==new Size(460,250),"Sentinel logical size changed unexpectedly");
            image.Save(Path.Combine(outputDirectory,$"sentinel-radar-{sample.Item1}.png"));
        }
        using var small=SentinelRadarRenderer.Render(pvp,new Size(320,174));small.Save(Path.Combine(outputDirectory,"sentinel-radar-small.png"));
        using var overlay=new SentinelRadarOverlay(()=>pvp){Location=new(-20000,-20000)};
        IntPtr before=NavigationOverlay.ForegroundWindow;_=overlay.Handle;
        Require(overlay.HasPassiveWindowStyles&&overlay.HeaderHasPassiveWindowStyles&&!overlay.Visible&&!overlay.HeaderVisible,
            "Body must be native click-through; hidden independent header must be passive and draggable");
        Require(before==NavigationOverlay.ForegroundWindow,"Constructing hidden passive windows must preserve foreground");
        overlay.Show();Application.DoEvents();
        Require(overlay.Visible&&overlay.HeaderVisible&&overlay.HeaderBounds.Left==overlay.Left+1&&overlay.HeaderBounds.Top==overlay.Top+1,
            "Showing the offscreen body must place its independent drag header over the painted header");
        Require(before==NavigationOverlay.ForegroundWindow,"Showing passive radar/header windows must preserve foreground");
        overlay.Hide();Application.DoEvents();
        Require(!overlay.Visible&&!overlay.HeaderVisible&&before==NavigationOverlay.ForegroundWindow,
            "Hiding the radar must hide the drag header and preserve foreground");
        File.WriteAllText(Path.Combine(outputDirectory,"sentinel-radar-checks.json"),JsonSerializer.Serialize(new{
            Passed=true,ImmutableSnapshots=true,StalePlayersCleared=true,ZoneStatusGated=true,NorthUp=true,
            StableEnemyNumbers=true,CoincidentIdentityCounts=true,ExactProjectedPositions=true,SelfDoesNotHideEnemy=true,BlankNamesUseIds=true,
            BodyClickThrough=true,HeaderPassive=true,ForegroundPreserved=true,HardwareInputEmitted=false},new JsonSerializerOptions{WriteIndented=true}));
    }

    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
