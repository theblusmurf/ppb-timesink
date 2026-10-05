using System.Text.Json;

namespace PoteHunter;

public sealed partial class HunterForm
{
    void CheckSentinelRadarUi()
    {
        if(!offlinePreviewMode||connected||working)throw new InvalidOperationException("Sentinel checks require a disconnected offline fixture.");
        var saved=Options.Read();var oldSelf=recognitionSelf;var oldHealth=recognitionHealth;
        var oldPlayers=recognizedPlayers;var oldLatest=latestHealth;var oldEntities=entities;var oldParty=currentParty;
        var oldContext=sentinelContext;int oldZone=navigationZone,oldRecognitionZone=recognitionZone;
        uint oldId=guardSelfId;long oldSeen=recognitionSeen;string oldTarget=filter.Text;
        try
        {
            void Require(bool value,string error){if(!value)throw new InvalidOperationException(error);}
            Require(navigationPage.Contains(showSentinelRadar)&&navigationPage.Contains(sentinelSoundEnabled)&&
                navigationPage.Contains(sentinelVolume)&&navigationPage.Contains(sentinelRange),"Sentinel controls must remain accessible in Navigation.");
            working=true;filter.Text="UNCOMMITTED COMBAT EDIT";
            showSentinelRadar.Checked=!saved.ShowSentinelRadar;sentinelSoundEnabled.Checked=false;sentinelVolume.Value=22;
            sentinelRange.Value=40;
            var restored=Options.Read();
            Require(restored.ShowSentinelRadar==!saved.ShowSentinelRadar&&!restored.SentinelSoundEnabled&&restored.SentinelVolumePercent==22&&
                restored.SentinelRange==40&&restored.Target==saved.Target,"Sentinel presentation changes must persist during hunting without saving unrelated combat edits.");
            var section=(CollapsibleSection)navigationPage.Controls.Find("navigationSentinelRadar",true).Single();
            section.Expanded=true;PerformLayout();Application.DoEvents();
            ((ScrollableControl)section.Parent!).ScrollControlIntoView(section);Application.DoEvents();
            using(var image=new Bitmap(Width,Height)){DrawToBitmap(image,new Rectangle(Point.Empty,Size));image.Save(Path.Combine(AppContext.BaseDirectory,"sentinel-navigation-controls.png"));}
            section.Expanded=false;
            CommitSentinelPosition(new(-1500,-200));restored=Options.Read();
            Require(restored.SentinelRadarPositionSaved&&restored.SentinelRadarX==-1500&&restored.SentinelRadarY==-200,
                "Negative multi-monitor drag positions were discarded.");
            var assembled=WithSentinelSettings(new Options());
            Require(assembled.SentinelRadarPositionSaved&&assembled.SentinelRadarX==-1500&&assembled.SentinelRadarY==-200,
                "A later combat save discarded the Sentinel position.");
            var bounds=JsonSerializer.Deserialize<Options>("{\"SentinelVolumePercent\":500}")!;
            Require(bounds.SentinelVolumePercent==100&&new Options{SentinelVolumePercent=-1}.SentinelVolumePercent==0,
                "Invalid saved sound volume escaped the playback bounds.");
            Require(new Options{SentinelRange=500}.SentinelRange==100&&new Options{SentinelRange=-5}.SentinelRange==1,
                "Invalid saved detection radius escaped the slider bounds.");
            working=false;showSentinelRadar.Checked=true;sentinelSoundEnabled.Checked=true;sentinelVolume.Value=45;
            sentinelRange.Value=25;
            var self=new Entity(100,1,"Local demo",new(0,0),0,Model:"PC_MAN.GCMDS");
            var other=new Entity(200,2,"Akkan demo",new(3,4),0,Model:"PC_Akhan_A.GCMDS");
            var sameFaction=new Entity(300,3,"Hidden same faction",new(0,1),0,Model:"PC_WOMAN.GCMDS");
            var unknownFaction=new Entity(400,4,"Hidden unknown faction",new(1,0),0,Model:"PC_UNKNOWN.GCMDS");
            connected=true;navigationZone=8;guardSelfId=self.Id;entities=[self,other,sameFaction,unknownFaction];
            currentParty=new(true,[],"Not in a party");latestHealth=new(){{self.Id,new(100,100)},{other.Id,new(90,100)}};
            UpdatePlayerRecognition(self);UpdateSentinelRadar(null,false);
            Require(sentinelSnapshot.Fresh&&sentinelSnapshot.KnownAlive&&sentinelSnapshot.EnemyCount==1&&sentinelFrame.Baseline,
                "Fresh live PvP observations lost their initial silent baseline.");
            Require(RecognizedPlayers().Length==3&&sentinelSnapshot.Players.Count==1&&sentinelSnapshot.Players[0].Name==other.Name,
                "Closer same-faction and unknown players must stay recognized for general navigation while absent from Sentinel.");
            int stableNumber=sentinelSnapshot.NearestEnemy!.Value.DisplayNumber;
            Require(stableNumber>0,"Accepted player did not acquire an identity label.");
            sentinelRange.Value=4;UpdateSentinelRadar(null,false);
            Require(sentinelSnapshot.EnemyCount==0&&sentinelSnapshot.Radius==4&&!sentinelFrame.PlaySound,
                "Slider radius failed to change the radar and arrival detection together.");
            sentinelRange.Value=25;UpdateSentinelRadar(null,false);
            Require(sentinelSnapshot.EnemyCount==1&&sentinelFrame.Arrivals.Count==0&&!sentinelFrame.PlaySound,
                "Increasing range replayed current players as arrivals.");
            Require(sentinelSnapshot.NearestEnemy!.Value.DisplayNumber==stableNumber,"Changing display range renumbered an enemy.");
            entities=[self,other with{Name=""}];UpdatePlayerRecognition(self);UpdateSentinelRadar(null,false);
            using(var state=JsonDocument.Parse(JsonSerializer.Serialize(SentinelRadarState())))
                Require(state.RootElement.GetProperty("NearestEnemy").GetString()=="Player 00000002"&&
                    !state.RootElement.GetProperty("Enemies")[0].GetProperty("NameAvailable").GetBoolean(),"Blank-name telemetry lost its explicit UID fallback.");
            var akkanSelf=self with{Model="PC_Akhan_B.GCMDS"};
            entities=[akkanSelf,other with{Model="PC_WOMAN.GCMDS"},sameFaction with{Model="PC_Akhan_A.GCMDS"}];
            UpdatePlayerRecognition(akkanSelf);UpdateSentinelRadar(null,false);
            Require(sentinelSnapshot.EnemyCount==1&&sentinelSnapshot.Players[0].Id==other.Id&&
                sentinelSnapshot.Players[0].Faction==PlayerFaction.Kartefant,
                "Opposing-faction filtering must follow the local body for Akkan as well as Human characters.");
            entities=[self,other,other with{Generation=1}];UpdatePlayerRecognition(self);UpdateSentinelRadar(null,false);
            Require(RecognizedPlayers().Length==0&&sentinelSnapshot.EnemyCount==0&&sentinelFrame.CurrentEnemies.Count==0,
                "Ambiguous duplicate UID observations disagreed across recognition, radar and sound.");
            entities=[self,other];UpdatePlayerRecognition(self);UpdateSentinelRadar(null,false);
            latestHealth=new(){{self.Id,new(0,100)},{other.Id,new(0,100)}};entities=[];
            UpdateSentinelRadar(null,false);
            Require(sentinelSnapshot.KnownAlive&&sentinelSnapshot.EnemyCount==1,
                "A later hunt-loop health mutation rewrote an accepted recognition poll.");
            recognitionHealth=new(0,100);UpdateSentinelRadar(null,false);
            Require(!sentinelSnapshot.KnownAlive&&!sentinelFrame.PlaySound&&sentinelFrame.CurrentEnemies.Count==0,
                "Dead local character allowed an arrival sound.");
            recognitionSeen=Environment.TickCount64-3001;UpdateSentinelRadar(null,false);
            Require(!sentinelSnapshot.Fresh&&sentinelSnapshot.Players.Count==0&&!sentinelFrame.PlaySound,
                "Stale observations retained player markers or audio permission.");
            Require(sentinelSonar==null&&sentinelOverlay==null,"Offline UI checks created a playback device or visible radar.");
            // Exercise real passive visibility transitions offscreen, without
            // using a client window, audio device, mouse or keyboard input.
            var previewArea=new Rectangle(-20000,-20000,800,600);
            IntPtr foreground=NavigationOverlay.ForegroundWindow;
            entities=[self,other,sameFaction,unknownFaction];latestHealth=new(){{self.Id,new(100,100)},{other.Id,new(90,100)}};
            navigationZone=12;UpdatePlayerRecognition(self);UpdateSentinelRadar(previewArea,false);
            navigationZone=8;
            UpdatePlayerRecognition(self);UpdateSentinelRadar(previewArea,false);
            Require(!working&&SentinelRadarActive&&sentinelOverlay is {Visible:true,HeaderVisible:true}&&sentinelFrame.Baseline,
                "Zone 8 must show a passive Sentinel with a silent baseline without starting a hunt.");
            navigationZone=12;UpdateSentinelRadar(previewArea,false);
            Require(!SentinelRadarActive&&sentinelOverlay is {Visible:false,HeaderVisible:false}&&sentinelSnapshot.Players.Count==0,
                "Leaving Zone 8 must immediately hide and clear Sentinel before a new recognition poll.");
            UpdatePlayerRecognition(self);UpdateSentinelRadar(previewArea,false);
            Require(!SentinelRadarActive&&sentinelSnapshot.Players.Count==0&&!sentinelFrame.PlaySound,
                "Fresh safe-zone readings cannot activate Sentinel.");
            navigationZone=9;UpdatePlayerRecognition(self);UpdateSentinelRadar(previewArea,false);
            Require(!SentinelRadarActive&&sentinelOverlay is {Visible:false}&&sentinelSnapshot.Players.Count==0,
                "An unverified zone cannot activate Sentinel.");
            navigationZone=8;UpdateSentinelRadar(previewArea,false);
            Require(!SentinelRadarActive&&sentinelOverlay is {Visible:false},"Prior-zone data cannot activate Sentinel on entry.");
            UpdatePlayerRecognition(self);UpdateSentinelRadar(previewArea,false);
            Require(SentinelRadarActive&&sentinelOverlay is {Visible:true}&&sentinelFrame.Baseline&&sentinelFrame.Arrivals.Count==0,
                "Reentering Zone 8 must reopen automatically with no sound for existing enemies.");
            showSentinelRadar.Checked=false;UpdateSentinelRadar(previewArea,false);
            Require(!Options.Read().ShowSentinelRadar&&!SentinelRadarActive&&sentinelOverlay is {Visible:false}&&sentinelSnapshot.Players.Count==0,
                "A saved automatic opt-out must hide and clear Sentinel in Zone 8.");
            showSentinelRadar.Checked=true;UpdateSentinelRadar(previewArea,false);
            Require(SentinelRadarActive&&sentinelOverlay is {Visible:true}&&!sentinelFrame.PlaySound&&sentinelFrame.Arrivals.Count==0,
                "Reenabling automatic Sentinel must not replay existing players as arrivals.");
            connected=false;UpdateSentinelRadar(previewArea,false);
            Require(!SentinelRadarActive&&sentinelOverlay is {Visible:false}&&sentinelSnapshot.Players.Count==0,
                "Disconnecting must hide and clear Sentinel even with a previous Zone 8 poll.");
            connected=true;UpdatePlayerRecognition(self);UpdateSentinelRadar(previewArea,false);
            Require(SentinelRadarActive&&sentinelOverlay is {Visible:true}&&sentinelFrame.Baseline,
                "Reconnecting in Zone 8 must establish a new silent baseline.");
            UpdateSentinelRadar(null,false);
            Require(sentinelOverlay is {Visible:false}&&!sentinelFrame.PlaySound,"Unavailable/background game bounds must hide the overlay and suppress sound.");
            UpdateSentinelRadar(previewArea,false);guardSelfId=999;UpdateSentinelRadar(previewArea,false);
            Require(!SentinelRadarActive&&sentinelOverlay is {Visible:false}&&sentinelSnapshot.Players.Count==0,
                "A changed local identity must clear and hide Sentinel.");
            guardSelfId=self.Id;UpdatePlayerRecognition(self);UpdateSentinelRadar(previewArea,false);
            recognitionSeen=Environment.TickCount64-3001;UpdateSentinelRadar(previewArea,false);
            Require(!SentinelRadarActive&&sentinelOverlay is {Visible:false}&&sentinelSnapshot.Players.Count==0&&sentinelSonar==null,
                "Stale observations must hide the overlay and clear detections without opening an audio device.");
            Require(foreground==NavigationOverlay.ForegroundWindow,"Automatic Sentinel transitions stole foreground focus.");
            SentinelRadarChecks.Run(AppContext.BaseDirectory);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"sentinel-integration-checks.json"),JsonSerializer.Serialize(new
            {Passed=true,HardwareInputEmitted=false,AudioPlayed=false,Checks=new[]{"Navigation controls","active presentation isolation",
                "negative monitor position persistence","volume/range bounds","slider and sound radius parity","same-poll self and other health","initial silent baseline",
                "dead/stale sound gates","Zone 8 automatic entry/exit/reentry without hunting","saved automatic opt-out",
                "disconnected/stale/identity/background visibility","both local factions and enemy-only names",
                "offline audio-device exclusion","native passive body/header and rendering"}},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally
        {
            connected=false;working=false;filter.Text=oldTarget;recognitionSelf=oldSelf;recognitionHealth=oldHealth;
            sentinelOverlay?.Dispose();sentinelOverlay=null;
            recognizedPlayers=oldPlayers;latestHealth=oldLatest;entities=oldEntities;currentParty=oldParty;
            sentinelContext=oldContext;sentinelPolicy.Reset();sentinelNumbers.Reset();navigationZone=oldZone;recognitionZone=oldRecognitionZone;
            guardSelfId=oldId;recognitionSeen=oldSeen;sentinelInitialized=false;
            showSentinelRadar.Checked=saved.ShowSentinelRadar;sentinelSoundEnabled.Checked=saved.SentinelSoundEnabled;
            sentinelVolume.Value=saved.SentinelVolumePercent;sentinelPositionSaved=saved.SentinelRadarPositionSaved;
            sentinelRange.Value=saved.SentinelRange;sentinelRangeLabel.Text=$"Enemy alert range · {sentinelRange.Value} map units";
            sentinelPosition=new(saved.SentinelRadarX,saved.SentinelRadarY);sentinelInitialized=true;saved.Save();
        }
    }
}
