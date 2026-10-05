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
        Require(pvp.Players.Count==2&&pvp.EnemyCount==2&&pvp.NearestEnemy?.Name=="Ashen Rival"&&
            pvp.Players.All(p=>p.Relation==PlayerRelation.Enemy),
            "Only in-range opposing-faction enemies may appear; same-faction, party and unknown players must stay off Sentinel, including when closer");
        Require(pvp.Players.Single(p=>p.Id==0x1005).Name=="At boundary",
            "The exact 25-unit boundary must remain included while non-finite and outside enemies are excluded");
        markers[0]=markers[0] with{Name="Replaced"};
        Require(pvp.NearestEnemy?.Name=="Ashen Rival","An accepted snapshot must not retain a mutable observations array");
        Require(SentinelRadarRenderer.NearestEnemyName(pvp)=="Ashen Rival",
            "The primary enemy heading must use the nearest player's accepted name rather than a faction label");
        var uncertain=new SentinelPlayerMarker(0x2001,1,"Unknown HP",new(1,1),PlayerFaction.Kartefant,PlayerRelation.Enemy,false);
        var invalidIdentity=new SentinelRadarSnapshot(true,true,default,"Zone 8",ZoneCombatRule.PvP,
            [uncertain,uncertain with{Id=0x2002,Name="Dead",ConfirmedDead=true},
                uncertain with{Id=0x2003,Generation=1},uncertain with{Id=0x2003,Generation=2}]);
        Require(invalidIdentity.Players.Count==1&&invalidIdentity.NearestEnemy?.Id==uncertain.Id,
            "Unknown-HP players may remain visible; confirmed-dead and duplicate/ambiguous UID bodies must be excluded");
        var mixedIdentity=new SentinelRadarSnapshot(true,true,default,"Zone 8",ZoneCombatRule.PvP,
            [uncertain,uncertain with{Relation=PlayerRelation.SameFaction,Name="Ambiguous friendly"},
                uncertain with{Id=0,Name="Invalid UID"},uncertain with{Id=0x2004,Name="Verified rival"}]);
        Require(mixedIdentity.Players.Count==1&&mixedIdentity.NearestEnemy?.Id==0x2004,
            "An enemy sharing its UID with a friendly body must be rejected before relation filtering; zero UID remains invalid");
        var safe=new SentinelRadarSnapshot(true,true,default,"Almighty Land · Zone 12",ZoneCombatRule.Safe,pvp.Players);
        Require(safe.EnemyCount==0&&safe.NearestEnemy==null&&safe.Players.Count==0,
            "A non-PvP snapshot must clear all players rather than retain prior enemies as opposing-faction markers");
        var unknown=new SentinelRadarSnapshot(true,true,default,"Unknown zone",ZoneCombatRule.Unknown,pvp.Players);
        Require(unknown.EnemyCount==0&&unknown.NearestEnemy==null&&unknown.Players.Count==0,
            "An unverified zone cannot retain player markers or names based on prior PvP status");
        var stale=new SentinelRadarSnapshot(false,true,default,"Caernarvon · Zone 8",ZoneCombatRule.PvP,pvp.Players);
        Require(!stale.Fresh&&stale.Players.Count==0&&stale.EnemyCount==0&&stale.NearestEnemy==null,"Stale observations must clear the radar and nearest enemy");
        Require(new[]{safe,unknown,stale}.All(snapshot=>SentinelRadarRenderer.NearestEnemyName(snapshot)==null),
            "Safe, unverified and stale states must not retain a prior enemy's name heading");
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
        Require(SentinelRadarRenderer.NearestEnemyName(crowded)=="Player 000003B2",
            "An unavailable nearest name must remain an explicit UID fallback in the primary heading");
        using(var image=SentinelRadarRenderer.Render(crowded))
        using(var graphics=Graphics.FromImage(image))
        {
            var badges=SentinelRadarRenderer.EnemyBadges(graphics,crowded,center,76);
            Require(badges.Count==3&&badges.Sum(p=>p.Cluster.Members.Count)==5&&badges[0].Caption.StartsWith("×3 #1,2,3",StringComparison.Ordinal),
                "Every overlap badge must report its exact current member count and corresponding stable numbers");
            Require(badges.Single(p=>p.Cluster.Members.Count==1&&p.Cluster.Members[0].Player.Id==0x088a).Caption=="#4 Gale"&&
                badges.Single(p=>p.Cluster.Members.Count==1&&p.Cluster.Members[0].Player.Id==0x0912).Caption=="#5 Player 00000912",
                "Individual badges must use the real name or explicit UID fallback alongside their stable number");
            Require(badges.All(p=>new RectangleF(12,48,190,164).Contains(p.Bounds)&&!p.Bounds.IntersectsWith(new(102,122,16,16))),
                "Number/count badges must stay in the radar pane and clear the self position");
            Color nearSelf=image.GetPixel(110,130);
            Require(nearSelf.R>230&&nearSelf.G<140&&nearSelf.B>70,
                "The self marker must not cover an enemy at its exact position at the largest watch range");
            image.Save(Path.Combine(outputDirectory,"sentinel-radar-five-enemies-clustered.png"));
        }
        const string longName="Σκιὰ・雪狼・The Eternal Nightwarden";
        var namedMarkers=new[]{
            new SentinelPlayerMarker(0x3101,1,"Ashen Rival",new(-12,15),PlayerFaction.Kartefant,PlayerRelation.Enemy,true,DisplayNumber:1),
            new SentinelPlayerMarker(0x3102,1,"Vex",new(14,15),PlayerFaction.Kartefant,PlayerRelation.Enemy,true,DisplayNumber:2),
            new SentinelPlayerMarker(0x3103,1,longName,new(-18,-12),PlayerFaction.Kartefant,PlayerRelation.Enemy,true,DisplayNumber:3),
            new SentinelPlayerMarker(0x3104,1,"Morgana",new(18,-12),PlayerFaction.Kartefant,PlayerRelation.Enemy,true,DisplayNumber:4),
            new SentinelPlayerMarker(0x3105,1,"Iron Warden",new(0,-24),PlayerFaction.Kartefant,PlayerRelation.Enemy,true,DisplayNumber:5)};
        var fiveNamed=new SentinelRadarSnapshot(true,true,default,"Caernarvon · Zone 8",ZoneCombatRule.PvP,namedMarkers);
        var mixedFaction=new SentinelRadarSnapshot(true,true,default,"Caernarvon · Zone 8",ZoneCombatRule.PvP,
            namedMarkers.Concat(new[]{
                namedMarkers[0] with{Id=0x4101,Name="Friendly name must not render",Position=new(0,.1),Relation=PlayerRelation.SameFaction,Faction=PlayerFaction.Merkhadian},
                namedMarkers[0] with{Id=0x4102,Name="Party name must not render",Position=new(1,0),Relation=PlayerRelation.Party,Faction=PlayerFaction.Merkhadian},
                namedMarkers[0] with{Id=0x4103,Name="Unknown name must not render",Position=new(-1,0),Relation=PlayerRelation.Unknown,Faction=PlayerFaction.Unknown},
                namedMarkers[0] with{Id=0x4104,Name="Non-PvP relation must not render",Position=new(0,-1),Relation=PlayerRelation.OpposingSafe},
                namedMarkers[0] with{Id=0x4105,Name="Self must not render",Position=default,Relation=PlayerRelation.Self,Faction=PlayerFaction.Merkhadian}}));
        Require(mixedFaction.Players.SequenceEqual(fiveNamed.Players)&&mixedFaction.Enemies.SequenceEqual(fiveNamed.Enemies)&&
            SentinelRadarRenderer.NearestEnemyName(mixedFaction)=="Ashen Rival",
            "Closer same-faction, party, unknown, safe-opposing and self bodies must not affect enemy names, numbering or nearest selection");
        using(var mixedImage=SentinelRadarRenderer.Render(mixedFaction))
        using(var enemyImage=SentinelRadarRenderer.Render(fiveNamed))
        {
            for(int y=0;y<mixedImage.Height;y++)for(int x=0;x<mixedImage.Width;x++)
                Require(mixedImage.GetPixel(x,y)==enemyImage.GetPixel(x,y),
                    "Mixed-faction input must render identically to enemy-only input, with no hidden friendly markers or labels");
            mixedImage.Save(Path.Combine(outputDirectory,"sentinel-radar-mixed-faction-enemies-only.png"));
        }
        var namesCrossing=new SentinelRadarSnapshot(true,true,default,"Caernarvon · Zone 8",ZoneCombatRule.PvP,
            namedMarkers.Select(p=>p.Id==0x3103?p with{Position=new(0,.5)}:p));
        Require(SentinelRadarRenderer.NearestEnemyName(fiveNamed)=="Ashen Rival"&&
            SentinelRadarRenderer.NearestEnemyName(namesCrossing)==longName&&namesCrossing.NearestEnemy?.DisplayNumber==3&&
            namesCrossing.Enemies.All(p=>p.Name==fiveNamed.Enemies.Single(original=>original.Id==p.Id).Name),
            "Distance-order changes must promote the current nearest name without swapping names or stable numbers between identities");
        using(var image=SentinelRadarRenderer.Render(fiveNamed))image.Save(Path.Combine(outputDirectory,"sentinel-radar-five-named-enemies.png"));
        using(var image=SentinelRadarRenderer.Render(namesCrossing))image.Save(Path.Combine(outputDirectory,"sentinel-radar-nearest-unicode-name.png"));
        using(var image=SentinelRadarRenderer.Render(fiveNamed,new Size(320,174)))image.Save(Path.Combine(outputDirectory,"sentinel-radar-five-names-small.png"));
        var longNamed=new SentinelRadarSnapshot(true,true,default,"Zone 8",ZoneCombatRule.PvP,[namedMarkers[2]]);
        using(var image=SentinelRadarRenderer.Render(longNamed))
        using(var graphics=Graphics.FromImage(image))
        {
            var badge=SentinelRadarRenderer.EnemyBadges(graphics,longNamed,center,76).Single();
            Require(badge.Caption=="#3 "+longName&&badge.Bounds.Width<=146&&new RectangleF(12,48,190,164).Contains(badge.Bounds)&&
                badge.Cluster.Members.Single().Point==SentinelRadarPresentation.RadarPoint(namedMarkers[2].Position,center,76),
                "Long Unicode names must retain their accepted text and exact marker location while the visible badge remains bounded");
            image.Save(Path.Combine(outputDirectory,"sentinel-radar-long-name.png"));
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
            Require(sample.Item2.Players.Count==0&&sample.Item2.Enemies.Count==0&&SentinelRadarRenderer.EnemyClusters(sample.Item2,center,76).Count==0,
                "Non-PvP, unverified and stale states cannot retain previous player markers, names, enemy numbers or cluster groups");
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
            OpposingEnemiesOnly=true,SameFactionNamesHidden=true,MixedFactionRenderUnchanged=true,AmbiguousCrossFactionUidRejected=true,
            StableEnemyNumbers=true,CoincidentIdentityCounts=true,ExactProjectedPositions=true,SelfDoesNotHideEnemy=true,BlankNamesUseIds=true,
            NamesPrimary=true,NamedIndividualBadges=true,NearestNameTracksIdentity=true,LongUnicodeNameBounded=true,
            BodyClickThrough=true,HeaderPassive=true,ForegroundPreserved=true,HardwareInputEmitted=false},new JsonSerializerOptions{WriteIndented=true}));
    }

    static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
