using System.Text.Json;

namespace PoteHunter;

public sealed partial class HunterForm
{
    void CheckPlayerRecognitionUi()
    {
        if(!offlinePreviewMode||connected||working)throw new InvalidOperationException("Player checks require a disconnected offline fixture.");
        var oldEntities=entities;var oldParty=currentParty;var oldHealth=latestHealth;
        var oldSelf=recognitionSelf;var oldPlayers=recognizedPlayers;
        int oldZone=navigationZone,oldRecognitionZone=recognitionZone,oldMapChoice=navigationMapChoice.SelectedIndex;uint oldId=guardSelfId;
        long oldSeen=recognitionSeen;Vec oldPosition=navigationPosition;
        decimal oldRadius=navigationViewRadius.Value;bool oldOverview=navigationMapOverview;
        var captures=new List<object>();
        try
        {
            void Require(bool value,string error){if(!value)throw new Exception(error);}
            var self=new Entity(100,1,"Human demo",new(8,8),5,Model:"PC_MAN.GCMDS");
            var enemy=new Entity(200,2,"Akkan demo",new(12,8),5,Model:"PC_Akhan_A.GCMDS");
            var same=new Entity(300,3,"Silva",new(4,8),5,Model:"PC_WOMAN.GCMDS");
            var unknown=new Entity(400,4,"Unknown body",new(8,14),5,Model:"PC_FUTURE.GCMDS");
            var guard=new Entity(500,0x40000005,"Guard",new(8,6),5,Model:"PC_Akhan_A.GCMDS");
            var dead=enemy with{Id=6,Name="Dead demo"};
            connected=true;navigationZone=8;guardSelfId=self.Id;navigationPosition=self.Position;
            navigationMapOverview=false;navigationViewRadius.Value=10;
            entities=[self,enemy,same,unknown,guard,dead];latestHealth=new(){{dead.Id,new(0,100)}};
            currentParty=new(true,[],"Not in a party");UpdatePlayerRecognition(self);
            UpdatePlayerRecognitionLabel();
            Require(navigationPage.Contains(navigationPlayerRecognitionLabel)&&navigationPlayerRecognitionLabel.Text.Contains("current Zone 8 (PvP)"),
                "Navigation hid the current zone rule from the player classification legend.");
            navigationMapChoice.SelectedItem=navigationMapChoice.Items.Cast<NavigationMapChoice>().Single(c=>c.Zone==12);
            UpdatePlayerRecognitionLabel();
            Require(navigationPlayerRecognitionLabel.Text.Contains("Selected map: Almighty Land · Non-PvP")&&
                navigationPlayerRecognitionLabel.Text.Contains("current Zone 8 (PvP)"),"Browsing a non-PvP map replaced the current live player's zone rule.");
            Require(RecognizedPlayers().Length==3,"Players must exclude self, NPC guards and confirmed-dead bodies.");
            Require(RecognizedPlayers().Single(p=>p.Entity.Id==2).Recognition.Enemy,"Opposing player lost PvP enemy status.");
            Require(RecognizedPlayers().Single(p=>p.Entity.Id==3).Recognition.Relation==PlayerRelation.SameFaction,"Same-faction body was mislabeled.");
            Require(RecognizedPlayers().Single(p=>p.Entity.Id==4).Recognition.Relation==PlayerRelation.Unknown,"Future body invented a faction.");
            var markers=Player3DMarkers(25);
            Require(markers.Length==3&&markers.All(m=>m.Player&&double.IsNaN(m.Heading))&&
                markers.Any(m=>m.Name=="Akkan demo"&&m.Color==PlayerMarkerColor(PlayerRelation.Enemy)),"3D player markers lost their name, shape/status or invented facing.");
            Require(PlayerMarkerName(RecognizedPlayers().Single(p=>p.Entity.Id==same.Id))=="Silva"&&
                markers.Any(m=>m.Name=="Silva"&&m.Position==same.Position&&m.Color==PlayerMarkerColor(PlayerRelation.SameFaction)),
                "Same-faction map captions replaced the accepted player name with a relationship label.");
            Require(RecognizedPlayers().All(p=>markers.Any(m=>m.Position==p.Entity.Position&&m.Name==PlayerMarkerName(p)&&
                m.Color==PlayerMarkerColor(p.Recognition.Relation))),"2D and 3D player name helpers disagreed or detached names from their accepted positions/status.");
            Require(!Player3DMarkers(3).Any(),"3D player distance filtering escaped the radar radius.");
            void Capture(string name,params (uint Id,PlayerRelation Relation)[] expected)
            {
                using var bitmap=new Bitmap(360,260);using var graphics=Graphics.FromImage(bitmap);
                graphics.Clear(ImperialTheme.Window);
                // Native graphics/font fallback can outlive the real poll's
                // three-second TTL on a cold CI runner. Refresh this explicit
                // offline scene once immediately before drawing, then verify
                // its exact accepted rows and freeze expected colors BEFORE
                // rendering. The production renderer freezes those same rows
                // at entry; a post-render live lookup would test elapsed time
                // instead of the image that was actually drawn.
                UpdatePlayerRecognition(self);
                var accepted=RecognizedPlayers();
                Require(expected.Length>0 && accepted.Length==expected.Length &&
                    accepted.Select(p=>(p.Entity.Id,p.Recognition.Relation)).OrderBy(p=>p.Id)
                        .SequenceEqual(expected.OrderBy(p=>p.Id)),
                    $"{name}: offline capture did not accept the exact expected player identities and relations.");
                var colors=accepted.Select(p=>PlayerMarkerColor(p.Recognition.Relation).ToArgb()).Distinct().ToArray();
                long began=Environment.TickCount64;
                DrawRadarPlayers(graphics,bitmap.Size);
                var counts=colors.ToDictionary(color=>color,_=>0);
                for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)
                {
                    int color=bitmap.GetPixel(x,y).ToArgb();
                    if(counts.TryGetValue(color,out int pixels))counts[color]=pixels+1;
                }
                foreach(var count in counts)
                    Require(count.Value>=20,$"{name}: expected 2D player status color {count.Key:X8} was omitted from the radar.");
                captures.Add(new{Name=name,AcceptedPlayers=accepted.Select(p=>new{p.Entity.Id,Relation=p.Recognition.Relation.ToString()}),
                    StatusColorPixels=counts,RenderAndInspectionMilliseconds=Environment.TickCount64-began,
                    PollAgeAfterMilliseconds=Environment.TickCount64-recognitionSeen});
                bitmap.Save(Path.Combine(AppContext.BaseDirectory,name),System.Drawing.Imaging.ImageFormat.Png);
            }
            Capture("player-radar-pvp.png",(enemy.Id,PlayerRelation.Enemy),(same.Id,PlayerRelation.SameFaction),(unknown.Id,PlayerRelation.Unknown));
            var alternate=same with{Name="",VerifiedPlayerName="Silva"};
            entities=[self,alternate];UpdatePlayerRecognition(self);
            Require(entities.Single(e=>e.Id==same.Id).Name==""&&
                PlayerMarkerName(RecognizedPlayers().Single())=="Silva"&&Player3DMarkers(25).Single().Name=="Silva",
                "Verified UID name did not reach the observer or changed shared combat names.");
            Capture("player-minimap-verified-name.png",(same.Id,PlayerRelation.SameFaction));
            entities=[self,same with{VerifiedPlayerName="Alternate record"}];UpdatePlayerRecognition(self);
            Require(PlayerMarkerName(RecognizedPlayers().Single())=="Silva","Alternate name overrode a valid creature name.");
            entities=[self,same with{Name=""}];UpdatePlayerRecognition(self);
            Require(PlayerMarkerName(RecognizedPlayers().Single())=="Player 00000003",
                "An unavailable next-poll UID name retained a prior player label.");
            var blank=same with{Id=7,Name="",Position=new(6,6)};
            var whitespace=same with{Id=8,Name=" \t ",Position=new(10,5)};
            var unicode=same with{Id=9,Name="  Éowyn星の旅人 · "+new string('界',90)+"  ",Position=new(5,11)};
            entities=[self,enemy,same,blank,whitespace,unicode];UpdatePlayerRecognition(self);
            Require(PlayerMarkerName(RecognizedPlayers().Single(p=>p.Entity.Id==7))=="Player 00000007"&&
                PlayerMarkerName(RecognizedPlayers().Single(p=>p.Entity.Id==8))=="Player 00000008",
                "Missing player names did not use their own accepted identity as an explicit fallback.");
            Require(PlayerMarkerName(RecognizedPlayers().Single(p=>p.Entity.Id==9))==unicode.Name.Trim()&&
                Player3DMarkers(25).Any(m=>m.Name==unicode.Name.Trim()&&m.Position==unicode.Position),
                "Unicode player identity was lost before bounded label rendering.");
            Capture("player-radar-name-edge-cases.png",(enemy.Id,PlayerRelation.Enemy),(same.Id,PlayerRelation.SameFaction),
                (blank.Id,PlayerRelation.SameFaction),(whitespace.Id,PlayerRelation.SameFaction),(unicode.Id,PlayerRelation.SameFaction));
            entities=[self,enemy,same,unknown,guard,dead];UpdatePlayerRecognition(self);
            // Mutating the hunt loop's list or roster cannot mutate the copied poll.
            entities=[enemy with{Name="Replacement identity",Model="PC_WOMAN.GCMDS"}];currentParty=new(true,[new(2,"Akkan demo",true)],"Changed roster");
            Require(RecognizedPlayers().Single(p=>p.Entity.Id==2).Recognition.Enemy&&
                PlayerMarkerName(RecognizedPlayers().Single(p=>p.Entity.Id==2))=="Akkan demo","A newer entity/roster rewrote an older accepted name or faction poll.");
            entities=[self,enemy];UpdatePlayerRecognition(self);
            Require(RecognizedPlayers().Single().Recognition.Relation==PlayerRelation.Party,"Verified party member retained enemy status.");
            Require(PlayerMarkerName(RecognizedPlayers().Single())=="Akkan demo"&&
                Player3DMarkers(25).Single().Color==PlayerMarkerColor(PlayerRelation.Party),"Party map captions changed the player name or lost party color.");
            currentParty=new(true,[new(2,"Different identity",true)],"Different roster");UpdatePlayerRecognition(self);
            Require(RecognizedPlayers().Single().Recognition.Enemy,"Name-mismatched roster suppressed a player identity.");
            currentParty=new(false,[new(2,"Akkan demo",true)],"Unavailable roster");UpdatePlayerRecognition(self);
            Require(RecognizedPlayers().Single().Recognition.Enemy,"Unavailable roster was trusted for party identity.");
            navigationZone=12;
            Require(RecognizedPlayers().Length==0,"A prior-zone enemy survived a zone transition.");
            UpdatePlayerRecognition(self);Require(RecognizedPlayers().Single().Recognition.Relation==PlayerRelation.OpposingSafe,"Almighty Land retained an enemy label.");
            Capture("player-radar-non-pvp.png",(enemy.Id,PlayerRelation.OpposingSafe));
            navigationZone=9;UpdatePlayerRecognition(self);Require(!RecognizedPlayers().Single().Recognition.Enemy&&ZonePlayerStatus()=="PvP rules unknown","Unverified zone inferred PvP.");
            Capture("player-radar-unknown.png",(enemy.Id,PlayerRelation.Unknown));
            recognitionSeen=Environment.TickCount64-3001;Require(RecognizedPlayers().Length==0,"Stale enemy markers persisted.");
            UpdatePlayerRecognitionLabel();Require(navigationPlayerRecognitionLabel.Text.Contains("Player reading unavailable"),"Stale player faction remained in the visible legend.");
            UpdatePlayerRecognition(self);guardSelfId=77;Require(RecognizedPlayers().Length==0,"Old local identity retained faction markers.");
            guardSelfId=self.Id;connected=false;Require(RecognizedPlayers().Length==0&&RecognizedPlayerState().Length==0,"Disconnected players remained in telemetry.");
            UpdatePlayerRecognitionLabel();Require(navigationPlayerRecognitionLabel.Text.Contains("Player reading unavailable"),"Disconnected player faction remained in the visible legend.");
            connected=true;ClearPlayerRecognition();Require(!PlayerRecognitionFresh&&RecognizedPlayers().Length==0,"Reset retained player status.");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"player-recognition-ui-checks.json"),JsonSerializer.Serialize(new{Passed=true,HardwareInputEmitted=false,Captures=captures,
                Checks=new[]{"immutable accepted name/faction poll","actual self faction","name-only same-faction/enemy/party captions","verified UID names on frozen observer only","valid creature name priority","no next-poll name retention","blank/whitespace identity fallback","bounded Unicode 2D label preview","PvP enemy diamond/status","non-PvP and unknown-zone suppression","party ID/name validation","dead/NPC/self exclusions","freshness/zone/identity/disconnection gates","2D preview and 3D name/color/position parity","capture asserts exact frozen player identities/relations and every expected status color"}},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally
        {
            connected=false;entities=oldEntities;currentParty=oldParty;latestHealth=oldHealth;
            recognitionSelf=oldSelf;recognizedPlayers=oldPlayers;recognitionZone=oldRecognitionZone;recognitionSeen=oldSeen;
            navigationZone=oldZone;guardSelfId=oldId;navigationPosition=oldPosition;navigationMapChoice.SelectedIndex=oldMapChoice;
            navigationViewRadius.Value=oldRadius;navigationMapOverview=oldOverview;
        }
    }
}
