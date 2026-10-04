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
        try
        {
            void Require(bool value,string error){if(!value)throw new Exception(error);}
            var self=new Entity(100,1,"Human demo",new(8,8),5,Model:"PC_MAN.GCMDS");
            var enemy=new Entity(200,2,"Akkan demo",new(12,8),5,Model:"PC_Akhan_A.GCMDS");
            var same=new Entity(300,3,"Human ally",new(4,8),5,Model:"PC_WOMAN.GCMDS");
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
                markers.Any(m=>m.Name.StartsWith("Enemy:")&&m.Color==PlayerMarkerColor(PlayerRelation.Enemy)),"3D player markers lost their shape/status or invented facing.");
            Require(!Player3DMarkers(3).Any(),"3D player distance filtering escaped the radar radius.");
            void Capture(string name)
            {
                using var bitmap=new Bitmap(360,260);using var graphics=Graphics.FromImage(bitmap);
                graphics.Clear(ImperialTheme.Window);DrawRadarPlayers(graphics,bitmap.Size);
                Color color=PlayerMarkerColor(RecognizedPlayers().First().Recognition.Relation);
                int pixels=0;for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)if(bitmap.GetPixel(x,y).ToArgb()==color.ToArgb())pixels++;
                Require(pixels>=20,"2D player status color was omitted from the radar.");
                bitmap.Save(Path.Combine(AppContext.BaseDirectory,name),System.Drawing.Imaging.ImageFormat.Png);
            }
            Capture("player-radar-pvp.png");
            // Mutating the hunt loop's list or roster cannot mutate the copied poll.
            entities=[enemy with{Model="PC_WOMAN.GCMDS"}];currentParty=new(true,[new(2,"Akkan demo",true)],"Changed roster");
            Require(RecognizedPlayers().Single(p=>p.Entity.Id==2).Recognition.Enemy,"A newer entity/roster rewrote an older accepted poll.");
            entities=[self,enemy];UpdatePlayerRecognition(self);
            Require(RecognizedPlayers().Single().Recognition.Relation==PlayerRelation.Party,"Verified party member retained enemy status.");
            currentParty=new(true,[new(2,"Different identity",true)],"Different roster");UpdatePlayerRecognition(self);
            Require(RecognizedPlayers().Single().Recognition.Enemy,"Name-mismatched roster suppressed a player identity.");
            currentParty=new(false,[new(2,"Akkan demo",true)],"Unavailable roster");UpdatePlayerRecognition(self);
            Require(RecognizedPlayers().Single().Recognition.Enemy,"Unavailable roster was trusted for party identity.");
            navigationZone=12;
            Require(RecognizedPlayers().Length==0,"A prior-zone enemy survived a zone transition.");
            UpdatePlayerRecognition(self);Require(RecognizedPlayers().Single().Recognition.Relation==PlayerRelation.OpposingSafe,"Almighty Land retained an enemy label.");
            Capture("player-radar-non-pvp.png");
            navigationZone=9;UpdatePlayerRecognition(self);Require(!RecognizedPlayers().Single().Recognition.Enemy&&ZonePlayerStatus()=="PvP rules unknown","Unverified zone inferred PvP.");
            Capture("player-radar-unknown.png");
            recognitionSeen=Environment.TickCount64-3001;Require(RecognizedPlayers().Length==0,"Stale enemy markers persisted.");
            UpdatePlayerRecognitionLabel();Require(navigationPlayerRecognitionLabel.Text.Contains("Player reading unavailable"),"Stale player faction remained in the visible legend.");
            UpdatePlayerRecognition(self);guardSelfId=77;Require(RecognizedPlayers().Length==0,"Old local identity retained faction markers.");
            guardSelfId=self.Id;connected=false;Require(RecognizedPlayers().Length==0&&RecognizedPlayerState().Length==0,"Disconnected players remained in telemetry.");
            UpdatePlayerRecognitionLabel();Require(navigationPlayerRecognitionLabel.Text.Contains("Player reading unavailable"),"Disconnected player faction remained in the visible legend.");
            connected=true;ClearPlayerRecognition();Require(!PlayerRecognitionFresh&&RecognizedPlayers().Length==0,"Reset retained player status.");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"player-recognition-ui-checks.json"),JsonSerializer.Serialize(new{Passed=true,HardwareInputEmitted=false,
                Checks=new[]{"immutable accepted poll","actual self faction","PvP enemy diamond/status","non-PvP and unknown-zone suppression","party ID/name validation","dead/NPC/self exclusions","freshness/zone/identity/disconnection gates","2D preview and 3D marker parity"}},new JsonSerializerOptions{WriteIndented=true}));
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
