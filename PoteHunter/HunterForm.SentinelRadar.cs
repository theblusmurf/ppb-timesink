namespace PoteHunter;

public sealed partial class HunterForm
{
    readonly CheckBox showSentinelRadar=new(){Name="showSentinelRadar",Text="Show Sentinel Radar",AutoSize=true};
    readonly CheckBox sentinelSoundEnabled=new(){Name="sentinelSoundEnabled",Text="Sonar on enemy entry",AutoSize=true};
    readonly TrackBar sentinelRange=new(){Name="sentinelRange",Minimum=1,Maximum=100,Value=25,SmallChange=1,LargeChange=5,TickStyle=TickStyle.None,Width=226,AccessibleName="Sentinel detection range in map units"};
    readonly Label sentinelRangeLabel=new(){Name="sentinelRangeLabel",AutoSize=true,Text="Enemy alert range · 25 map units"};
    readonly NumericUpDown sentinelVolume=new(){Name="sentinelVolume",Minimum=0,Maximum=100,Value=45,Width=90,AccessibleName="Sentinel sound volume percent"};
    readonly Button resetSentinelPosition=new(){Name="resetSentinelPosition",Text="Reset radar position",AutoSize=true};
    readonly SentinelAlertPolicy sentinelPolicy=new();
    readonly SentinelDisplayNumbers sentinelNumbers=new();
    SentinelAlertContext sentinelContext;
    SentinelAlertFrame sentinelFrame=SentinelAlertFrame.Empty("Waiting for player reading");
    SentinelRadarSnapshot sentinelSnapshot=SentinelRadarSnapshot.Unavailable("Unknown zone",ZoneCombatRule.Unknown);
    SentinelRadarOverlay? sentinelOverlay;
    SentinelSonar? sentinelSonar;
    bool sentinelInitialized,sentinelPositionSaved;
    Point sentinelPosition;

    void InitializeSentinelRadar()
    {
        var saved=Options.Read();
        showSentinelRadar.Checked=saved.ShowSentinelRadar;sentinelSoundEnabled.Checked=saved.SentinelSoundEnabled;
        sentinelVolume.Value=saved.SentinelVolumePercent;sentinelPositionSaved=saved.SentinelRadarPositionSaved;
        sentinelRange.Value=saved.SentinelRange;sentinelRangeLabel.Text=$"Enemy alert range · {sentinelRange.Value} map units";
        sentinelPosition=new(saved.SentinelRadarX,saved.SentinelRadarY);
        showSentinelRadar.CheckedChanged+=(_,_)=>SentinelPresentationChanged();
        sentinelSoundEnabled.CheckedChanged+=(_,_)=>SentinelPresentationChanged();
        sentinelVolume.ValueChanged+=(_,_)=>SentinelPresentationChanged();
        sentinelRange.ValueChanged+=(_,_)=>SentinelPresentationChanged();
        resetSentinelPosition.Click+=(_,_)=>
        {
            sentinelPositionSaved=false;HideSentinelRadar();SentinelPresentationChanged();
        };
        sentinelInitialized=true;
    }

    Options WithSentinelSettings(Options saved)
    {
        if(!sentinelInitialized)return saved;
        saved.ShowSentinelRadar=showSentinelRadar.Checked;saved.SentinelSoundEnabled=sentinelSoundEnabled.Checked;
        saved.SentinelRange=sentinelRange.Value;
        saved.SentinelVolumePercent=(int)sentinelVolume.Value;saved.SentinelRadarPositionSaved=sentinelPositionSaved;
        saved.SentinelRadarX=sentinelPosition.X;saved.SentinelRadarY=sentinelPosition.Y;return saved;
    }
    void SentinelPresentationChanged()
    {
        if(!sentinelInitialized)return;
        sentinelRangeLabel.Text=$"Enemy alert range · {sentinelRange.Value} map units";
        // Save only this presentation choice, including during a hunt; leave the
        // active combat session and unrelated, uncommitted UI edits alone.
        try{WithSentinelSettings(Options.Read()).Save();}
        catch(Exception ex){message="Sentinel settings could not be saved: "+ex.Message;}
        UpdateNavigationOverlay();
    }
    void CommitSentinelPosition(Point location)
    {
        sentinelPosition=location;sentinelPositionSaved=true;
        try{WithSentinelSettings(Options.Read()).Save();}
        catch(Exception ex){message="Sentinel position could not be saved: "+ex.Message;}
    }

    void UpdateSentinelRadar(Rectangle? gameBounds,bool gameForeground)
    {
        if(!sentinelInitialized||IsDisposed)return;
        bool fresh=PlayerRecognitionFresh;
        var self=recognitionSelf;
        var players=RecognizedPlayers();
        var rule=ZoneCombatRules.For(navigationZone);
        bool alive=fresh&&recognitionHealth.Known&&!recognitionHealth.Dead;
        if(self!=null)sentinelContext=new(world.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SentinelPlayerIdentity.Of(self),navigationZone);
        sentinelNumbers.Update(connected,fresh,sentinelContext,players.Where(p=>p.Recognition.Enemy).Select(p=>p.Entity),Environment.TickCount64);
        sentinelSnapshot=new(fresh,alive,self?.Position??default,rule.Name,rule.Rule,
            players.Select(p=>new SentinelPlayerMarker(p.Entity.Id,p.Entity.Generation,p.Entity.Name,p.Entity.Position,
                p.Recognition.OtherFaction,p.Recognition.Relation,p.Health.Known&&!p.Health.Dead,p.Health.Dead,sentinelNumbers.Number(p.Entity))),sentinelRange.Value);
        // Offline fixtures must never reach an audio device or a game operation.
        bool audioAllowed=!offlinePreviewMode&&gameBounds.HasValue&&gameForeground;
        sentinelFrame=sentinelPolicy.Update(new(showSentinelRadar.Checked,connected,fresh,alive,
            sentinelContext,
            players.Select(p=>new SentinelPlayerObservation(SentinelPlayerIdentity.Of(p.Entity),p.Entity.Name,
                (p.Entity.Position-(self?.Position??default)).Length,p.Recognition.Relation,p.Health.Dead)).ToArray(),
            sentinelSoundEnabled.Checked,(int)sentinelVolume.Value,audioAllowed,sentinelRange.Value),DateTime.UtcNow);
        if(!audioAllowed||!alive||!showSentinelRadar.Checked||!sentinelSoundEnabled.Checked||sentinelVolume.Value==0)
            sentinelSonar?.Stop();
        else if(sentinelFrame.PlaySound)
        {
            sentinelSonar??=new SentinelSonar();sentinelSonar.TryPlay(sentinelFrame.Volume);
        }
        if(!showSentinelRadar.Checked||gameBounds is not Rectangle area||area.Width<280||area.Height<160)
        {HideSentinelRadar();return;}
        sentinelOverlay??=new SentinelRadarOverlay(()=>sentinelSnapshot,CommitSentinelPosition);
        sentinelOverlay.FitToArea(area);
        if(!sentinelOverlay.Visible)
        {
            Point position=sentinelPositionSaved?sentinelPosition:new(area.Left+NavigationOverlayMargin,
                lootTrackerOverlay is {Visible:true}?lootTrackerOverlay.Bottom+NavigationOverlayMargin:area.Top+NavigationOverlayMargin);
            sentinelOverlay.MoveTo(position);sentinelOverlay.Show();
        }
        sentinelOverlay.RefreshSnapshot();
    }
    object SentinelRadarState()=>new
    {
        Enabled=showSentinelRadar.Checked,Visible=sentinelOverlay is {Visible:true},Range=sentinelRange.Value,
        SoundEnabled=sentinelSoundEnabled.Checked,Volume=(int)sentinelVolume.Value,Sound="Paired sonar",
        IntendedPlayback=sentinelSonar?.Playing??false,AudioError=sentinelSonar?.LastError,Status=sentinelFrame.Status,
        NearestEnemy=sentinelSnapshot.NearestEnemy is SentinelPlayerMarker nearest?PlayerRecognition.DisplayName(nearest.Id,nearest.Name):null,
        NearestEnemyName=sentinelSnapshot.NearestEnemy?.Name,EnemyCount=sentinelSnapshot.EnemyCount,
        Enemies=sentinelSnapshot.Players.Where(p=>p.Relation==PlayerRelation.Enemy).Select(p=>new
        {
            p.Id,p.Generation,p.DisplayNumber,DisplayName=PlayerRecognition.DisplayName(p.Id,p.Name),NameAvailable=!string.IsNullOrWhiteSpace(p.Name),
            Distance=(p.Position-sentinelSnapshot.SelfPosition).Length,Bearing=SentinelRadarPresentation.Bearing(p.Position-sentinelSnapshot.SelfPosition)
        }).ToArray(),LastPollArrivals=sentinelFrame.Arrivals.Select(p=>PlayerRecognition.DisplayName(p.Identity.Id,p.Name)).ToArray(),Position=sentinelOverlay?.Location
    };
    void HideSentinelRadar(){if(sentinelOverlay is {IsDisposed:false,Visible:true})sentinelOverlay.Hide();}
    void DisposeSentinelRadar()
    {
        sentinelOverlay?.Dispose();sentinelOverlay=null;sentinelSonar?.Dispose();sentinelSonar=null;sentinelPolicy.Reset();sentinelNumbers.Reset();
    }
}
