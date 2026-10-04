namespace PoteHunter;

public sealed partial class HunterForm
{
    Entity? recognitionSelf;
    int recognitionZone;
    long recognitionSeen;
    ObservedPlayer[] recognizedPlayers=[];
    readonly Label navigationPlayerRecognitionLabel=new(){Name="navigationPlayerRecognitionLabel",AutoSize=true,
        MaximumSize=new(240,0),ForeColor=ImperialTheme.Muted,Text="Players · faction + zone",Margin=new(8,5,8,12)};

    void UpdatePlayerRecognitionLabel()
    {
        var viewed=ZoneCombatRules.For(Navigation3DZone);
        string current=PlayerRecognitionFresh?$"You: {PlayerRecognition.FactionLabel(PlayerRecognition.Faction(recognitionSelf!.Model))} · current Zone {navigationZone} ({ZonePlayerStatus()})":"Player reading unavailable";
        navigationPlayerRecognitionLabel.Text=$"Selected map: {viewed.Name} · {(viewed.Rule==ZoneCombatRule.Unknown?"PvP rules unknown":PlayerRecognition.RuleLabel(viewed.Rule))}\n{current}\nPlayer diamonds: pink enemy, blue same faction, teal party, gold non-PvP, gray unknown.";
    }

    void UpdatePlayerRecognition(Entity self)
    {
        // Freeze identities, faction and roster from the same accepted poll.
        // The asynchronous hunt loop may replace entities between UI ticks.
        recognitionSelf=self;recognitionZone=navigationZone;
        recognizedPlayers=entities.Where(e=>CombatCourtesy.IsOtherPlayer(e,self.Id)&&e.Position.Finite&&
                !latestHealth.GetValueOrDefault(e.Id).Dead)
            .OrderBy(e=>(e.Position-self.Position).Length).Take(128).Select(e=>new ObservedPlayer(e,
                PlayerRecognition.Classify(e,self,navigationZone,currentParty.Available&&
                    currentParty.Members.Any(m=>m.Id==e.Id&&m.Name.Equals(e.Name,StringComparison.OrdinalIgnoreCase))))).ToArray();
        recognitionSeen=Environment.TickCount64;
    }
    void ClearPlayerRecognition(){recognitionSelf=null;recognitionZone=-1;recognitionSeen=0;recognizedPlayers=[];}
    bool PlayerRecognitionFresh=>connected&&recognitionSelf!=null&&recognitionSelf.Id==guardSelfId&&
        recognitionZone==navigationZone&&Environment.TickCount64-recognitionSeen is >=0 and <3000;

    readonly record struct ObservedPlayer(Entity Entity,PlayerRecognitionResult Recognition);
    ObservedPlayer[] RecognizedPlayers()
    {
        if(!PlayerRecognitionFresh)return [];
        return recognizedPlayers;
    }
    object PlayerRecognitionState()
    {
        var info=ZoneCombatRules.For(navigationZone);
        var players=RecognizedPlayers();
        return new { Zone=info.Zone,info.Name,Rule=info.Rule.ToString(),RuleLabel=PlayerRecognition.RuleLabel(info.Rule),
            info.Evidence,info.Source,Fresh=PlayerRecognitionFresh,
            OwnFaction=PlayerRecognitionFresh?PlayerRecognition.FactionLabel(PlayerRecognition.Faction(recognitionSelf!.Model)):"Unknown faction",
            FactionBasis="Exact player model family; no verified server faction or attackability flag",
            EnemyCountWithin25=players.Count(p=>p.Recognition.Enemy&&(p.Entity.Position-navigationPosition).Length<=25),
            Attackability="Not determined",AlertSound="Design selection pending" };
    }
    object[] RecognizedPlayerState()=>RecognizedPlayers().Select(p=>(object)new {p.Entity.Id,p.Entity.Generation,p.Entity.Name,p.Entity.Model,p.Entity.Position,
        Faction=p.Recognition.OtherFaction.ToString(),FactionLabel=PlayerRecognition.FactionLabel(p.Recognition.OtherFaction),
        Relation=p.Recognition.Relation.ToString(),p.Recognition.Status,p.Recognition.Enemy,
        Distance=(p.Entity.Position-navigationPosition).Length,Attackability="Not determined"}).ToArray();

    string ZonePlayerStatus()
    {
        var rule=ZoneCombatRules.For(navigationZone).Rule;
        return rule==ZoneCombatRule.Unknown?"PvP rules unknown":PlayerRecognition.RuleLabel(rule);
    }
    static Color PlayerMarkerColor(PlayerRelation relation)=>relation switch
    {
        PlayerRelation.Enemy=>Color.FromArgb(255,94,124),
        PlayerRelation.Party=>Color.FromArgb(127,199,174),
        PlayerRelation.SameFaction=>ImperialTheme.RouteBlue,
        PlayerRelation.OpposingSafe=>ImperialTheme.Gold,
        _=>Color.Silver
    };
    static string PlayerMarkerName(ObservedPlayer player)
    {
        string status=player.Recognition.Relation switch
        {
            PlayerRelation.Enemy=>"Enemy",PlayerRelation.Party=>"Party",PlayerRelation.SameFaction=>"Same faction",
            PlayerRelation.OpposingSafe=>"Non-PvP opponent",_=>"Unknown"
        };
        string name=string.IsNullOrWhiteSpace(player.Entity.Name)?$"{player.Entity.Id:X8}":player.Entity.Name;
        return status+": "+name;
    }
    MapMarker3D[] Player3DMarkers(double range=double.PositiveInfinity)=>RecognizedPlayers()
        .Where(p=>double.IsFinite(p.Entity.Height)&&(p.Entity.Position-navigationPosition).Length<=range)
        .Select(p=>new MapMarker3D(PlayerMarkerName(p),p.Entity.Position,p.Entity.Height,
            PlayerMarkerColor(p.Recognition.Relation),double.NaN,Player:true)).ToArray();

    void DrawRadarPlayers(Graphics g,Size size)
    {
        var players=RecognizedPlayers();if(players.Length==0)return;
        float span=(float)NavigationViewRadius(),scale=Math.Min(size.Width,size.Height)/(span*2);
        float cx=size.Width/2f,cy=size.Height/2f;Vec center=NavigationViewCenter();
        using var font=new Font("Segoe UI",8f);using var back=new SolidBrush(Color.FromArgb(225,ImperialTheme.Window));
        var visible=new List<(ObservedPlayer Player,PointF Point)>();
        foreach(var player in players.Where(p=>(p.Entity.Position-center).Length<=span))
        {
            var pos=player.Entity.Position;var p=new PointF(cx+(float)(pos.X-center.X)*scale,cy-(float)(pos.Y-center.Y)*scale);
            if(p.X<8||p.Y<8||p.X>size.Width-8||p.Y>size.Height-8)continue;
            using var brush=new SolidBrush(PlayerMarkerColor(player.Recognition.Relation));
            g.FillPolygon(brush,new PointF[]{new(p.X,p.Y-5),new(p.X+5,p.Y),new(p.X,p.Y+5),new(p.X-5,p.Y)});
            visible.Add((player,p));
        }
        // Markers always remain visible. Give enemy labels first choice of a
        // bounded nearby slot, without painting later labels over their text.
        var occupied=visible.Select(item=>new RectangleF(item.Point.X-5,item.Point.Y-5,10,10)).ToList();
        foreach(var item in visible.OrderBy(item=>item.Player.Recognition.Relation==PlayerRelation.Enemy?0:
            item.Player.Recognition.Relation==PlayerRelation.Party?1:2))
        {
            string label=PlayerMarkerName(item.Player);SizeF bounds=g.MeasureString(label,font);
            while(bounds.Width>Math.Max(10,size.Width-20)&&label.Length>2){label=label[..^2]+"…";bounds=g.MeasureString(label,font);}
            float x=Math.Clamp(item.Point.X+8,5,Math.Max(5,size.Width-bounds.Width-5));
            float originY=Math.Clamp(item.Point.Y+6,5,Math.Max(5,size.Height-bounds.Height-5));
            for(int attempt=0;attempt<17;attempt++)
            {
                int offset=attempt==0?0:(attempt+1)/2*(attempt%2==1?1:-1);
                float y=originY+offset*(bounds.Height+5);
                var rectangle=new RectangleF(x-2,y-1,bounds.Width+4,bounds.Height+2);
                if(rectangle.Left<3||rectangle.Top<3||rectangle.Right>size.Width-3||rectangle.Bottom>size.Height-3||
                    occupied.Any(prior=>prior.IntersectsWith(rectangle)))continue;
                occupied.Add(rectangle);using var brush=new SolidBrush(PlayerMarkerColor(item.Player.Recognition.Relation));
                g.FillRectangle(back,rectangle);g.DrawString(label,font,brush,x,y);break;
            }
        }
    }
}
