using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoteHunter;

internal sealed record TeleporterPosition(int Zone,Vec Position,double Height)
{
    [JsonIgnore] public bool Valid=>(Zone is >=1 and <=18 or 100) && Position.Finite && double.IsFinite(Height);
    public bool Near(Entity entity,int zone,double radius)=>Valid && entity.Position.Finite && double.IsFinite(entity.Height) &&
        zone==Zone && (entity.Position-Position).Length<=radius && Math.Abs(entity.Height-Height)<=TeleporterProfile.HeightTolerance;
}

// Only user-captured patches live in this local registry. A generic Move to
// Location dialog is usable only after the separately recognised destination.
internal sealed record TeleporterProfile(int Version,string ClientHash,int Width,int Height,string Destination,
    string Character,uint CharacterId,string TargetSelection,int DestinationSlot,
    TeleporterPosition Departure,TeleporterPosition Landing,RevivalStep Select,RevivalStep Confirm)
{
    public const double SourceRadius=3,LandingRadius=5,HeightTolerance=3;
    internal const int MaximumLinks=16,MaximumRegistryBytes=24_000_000;
    static readonly object RegistryGate=new();
    public static string PathName=>Path.Combine(AppContext.BaseDirectory,"teleporter-profiles.json");
    internal sealed record Registry(int Version,TeleporterProfile[] Links);

    public void Validate(string hash,Size size)
    {
        if(Version!=1 || string.IsNullOrWhiteSpace(ClientHash) || !string.Equals(ClientHash,hash,StringComparison.OrdinalIgnoreCase) ||
            Width is <16 or >16384 || Height is <16 or >16384 || Width!=size.Width || Height!=size.Height)
            throw new InvalidOperationException("Teleporter setup belongs to a different client build or window size. Configure the teleporter again.");
        if(string.IsNullOrWhiteSpace(Destination) || Destination.Length>120 || string.IsNullOrWhiteSpace(Character) || Character.Length>120 ||
            CharacterId==0 || (CharacterId&0xf0000000)!=0 || TargetSelection==null || TargetSelection.Length>256 ||
            DestinationSlot is <0 or >=Navigation.SavedRouteSlotCount)
            throw new InvalidOperationException("Teleporter setup has an invalid destination, character, target, or saved route slot.");
        if(Departure==null || Landing==null || !Departure.Valid || !Landing.Valid)
            throw new InvalidOperationException("Teleporter setup has an invalid map position or height. Capture both ends again.");
        if(Departure.Zone!=Landing.Zone)
            throw new InvalidOperationException("Teleporter routes between different map zones are not supported. Capture two locations in the same zone.");
        if((Departure.Position-Landing.Position).Length<=SourceRadius+LandingRadius+2)
            throw new InvalidOperationException("Teleporter departure and landing must be more than ten map units apart.");
        if(Select==null || Confirm==null || !Select.Valid(size,true) || !Confirm.Valid(size,true) || Select.Point==Confirm.Point)
            throw new InvalidOperationException("Teleporter setup needs separate destination and OK controls with valid independent recognition areas.");
    }

    bool ValidImage(Bitmap image)
    {
        if(image.Width!=Width || image.Height!=Height)return false;
        Validate(ClientHash,image.Size);return true;
    }
    static VisualControl Control(RevivalStep step)=>new(step.Point,1,step.Button!.Bounds,step.Marker.Bounds,Custom:true);
    static bool Exact(RevivalStep step,VisualControl control)=>control.Custom && control.Scale==1 && control.Point==step.Point &&
        control.Button==step.Button!.Bounds && control.Marker==step.Marker.Bounds;
    public VisualControl? FindSelection(Bitmap image,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return ValidImage(image) && !Confirm.Matches(image,false,token) && Select.Matches(image,true,token)?Control(Select):null;
    }
    public VisualControl? FindConfirmation(Bitmap image,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return ValidImage(image) && Confirm.Matches(image,true,token)?Control(Confirm):null;
    }
    public bool CanClickSelection(Bitmap image,VisualControl control,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Hover can alter the blue marker, so retain the separately captured
        // destination marker and exact previously recognised control geometry.
        return ValidImage(image) && Exact(Select,control) && Select.Matches(image,false,token) && !Confirm.Matches(image,false,token);
    }
    public bool CanClickConfirmation(Bitmap image,VisualControl control,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return ValidImage(image) && Exact(Confirm,control) && Confirm.Matches(image,false,token);
    }

    static (string Target,int Slot) Scope(TeleporterProfile profile)=>(Navigation.TargetSelectionKey(profile.TargetSelection),profile.DestinationSlot);
    static Registry ReadRegistry(string file)
    {
        if(!File.Exists(file))return new(1,[]);
        if(new FileInfo(file).Length>MaximumRegistryBytes)throw new InvalidOperationException("Teleporter setup is too large; the existing file was preserved.");
        Registry? registry;
        try{registry=JsonSerializer.Deserialize<Registry>(File.ReadAllText(file));}
        catch(JsonException){throw new InvalidOperationException("Teleporter setup could not be read; the existing file was preserved.");}
        if(registry is not {Version:1,Links:not null} || registry.Links.Length>MaximumLinks)
            throw new InvalidOperationException("Teleporter setup format or link count is invalid; the existing file was preserved.");
        var scopes=new HashSet<(string,int)>();
        foreach(var link in registry.Links)
        {
            if(link==null)throw new InvalidOperationException("Teleporter setup contains an incomplete link; the existing file was preserved.");
            link.Validate(link.ClientHash,new(link.Width,link.Height));
            if(!scopes.Add(Scope(link)))throw new InvalidOperationException("Teleporter setup contains duplicate target/slot links; the existing file was preserved.");
        }
        return registry;
    }
    public static TeleporterProfile? Load(string hash,Size size,string? target,int slot,string? path=null)
    {
        if(slot is <0 or >=Navigation.SavedRouteSlotCount)throw new InvalidOperationException("Select a valid saved route slot for the teleporter.");
        lock(RegistryGate)
        {
            var key=(Navigation.TargetSelectionKey(target),slot);
            var profile=ReadRegistry(path??PathName).Links.FirstOrDefault(link=>Scope(link)==key);
            profile?.Validate(hash,size);return profile;
        }
    }
    public void Save(string? path=null)
    {
        Validate(ClientHash,new(Width,Height));
        string file=Path.GetFullPath(path??PathName);
        lock(RegistryGate)
        {
            var current=ReadRegistry(file);
            var links=current.Links.Where(link=>Scope(link)!=Scope(this)).Append(this).ToArray();
            if(links.Length>MaximumLinks)throw new InvalidOperationException("Teleporter setup already has sixteen links. The existing links were preserved.");
            byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(new Registry(1,links));
            if(bytes.Length>MaximumRegistryBytes)throw new InvalidOperationException("Teleporter setup is too large; the existing links were preserved.");
            // A unique sibling temporary avoids cross-writer partial files. Read
            // and validate the whole newest registry before replacing any scope.
            string temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {stream.Write(bytes);stream.Flush(true);}
                File.Move(temporary,file,true);
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}

internal sealed record TeleporterObservation(string ClientHash,int ProcessId,nint Window,Size WindowSize,
    Entity? Character,Health Health,int Zone,bool Focused,bool Cancelled=false);
internal enum TeleporterStage { Ready,SelectionIssued,AwaitingConfirmation,ConfirmationIssued,AwaitingLanding,Complete,Stopped }

// Pure policy: it never presses or holds a key. The caller must acknowledge
// successfully sent mouse clicks and retain its normal input/window guards.
internal sealed class TeleporterTransaction
{
    public const int PhaseTimeoutMilliseconds=15000;
    readonly TeleporterProfile profile;
    readonly TeleporterObservation initial;
    long phaseStarted,lastObserved;
    public TeleporterStage Stage {get;private set;}=TeleporterStage.Ready;
    public bool SelectionAttempted {get;private set;}
    public bool ConfirmationAttempted {get;private set;}
    public bool ConfirmationWasSent {get;private set;}
    public Entity? AcceptedCharacter {get;private set;}
    public string? StopReason {get;private set;}
    public TeleporterTransaction(TeleporterProfile profile,TeleporterObservation initial,long now)
    {
        this.profile=profile;this.initial=initial;phaseStarted=lastObserved=now;
        profile.Validate(initial.ClientHash,initial.WindowSize);
        CheckDeparture(initial,now);
    }
    void Stop(string reason)
    {
        Stage=TeleporterStage.Stopped;StopReason??=reason;
        throw new InvalidOperationException(StopReason);
    }
    void CheckTime(long now)
    {
        if(Stage==TeleporterStage.Stopped)throw new InvalidOperationException(StopReason??"Teleporter transaction stopped.");
        if(now<lastObserved || now<phaseStarted || (ulong)(now-phaseStarted)>=PhaseTimeoutMilliseconds)
            Stop("Teleporter step timed out or its clock changed. No further clicks were sent.");
        lastObserved=now;
    }
    void CheckContext(TeleporterObservation observation,long now)
    {
        CheckTime(now);
        if(observation.Cancelled || !observation.Focused)Stop("Teleporter stopped after cancellation or game focus loss.");
        if(initial.ProcessId<=0 || initial.Window==0 || observation.ProcessId!=initial.ProcessId || observation.Window!=initial.Window ||
            !string.Equals(observation.ClientHash,initial.ClientHash,StringComparison.OrdinalIgnoreCase) || observation.WindowSize!=initial.WindowSize)
            Stop("Teleporter stopped because the client, process, or game window changed.");
    }
    Entity CheckCommon(TeleporterObservation observation,long now,bool allowUnknownHealth=false)
    {
        CheckContext(observation,now);
        var character=observation.Character;
        if(character==null || initial.Character==null || character.Id!=profile.CharacterId || character.Id!=initial.Character.Id ||
            character.Name!=initial.Character.Name || character.Model!=initial.Character.Model ||
            !string.Equals(character.Name,profile.Character,StringComparison.OrdinalIgnoreCase) ||
            !character.Model.StartsWith("PC_",StringComparison.OrdinalIgnoreCase) || character.Address<=0 ||
            !character.Position.Finite || !double.IsFinite(character.Height))
            Stop("Teleporter stopped because the logged-in character identity or position changed.");
        if(observation.Health.Dead || !observation.Health.Known && !allowUnknownHealth)Stop("Teleporter requires known living HP. Recovery or unreadable health stopped the transaction.");
        if(observation.Zone!=profile.Departure.Zone)Stop("Teleporter stopped because the map zone changed.");
        return character!;
    }
    public void CheckDeparture(TeleporterObservation observation,long now)
    {
        if(Stage is TeleporterStage.AwaitingLanding or TeleporterStage.Complete)Stop("Teleporter departure checks cannot accept a landing identity.");
        var character=CheckCommon(observation,now);
        if(!LocalCharacter.Same(initial.Character!,character) || !profile.Departure.Near(character,observation.Zone,TeleporterProfile.SourceRadius))
            Stop("Teleporter departure body or captured position changed before confirmation.");
    }
    public void BeforeSelection(TeleporterObservation observation,long now)
    {
        CheckDeparture(observation,now);
        if(Stage!=TeleporterStage.Ready || SelectionAttempted)Stop("Teleporter destination selection was already attempted.");
        SelectionAttempted=true;Stage=TeleporterStage.SelectionIssued;
    }
    public void SelectionSent(TeleporterObservation observation,long now)
    {
        CheckDeparture(observation,now);
        if(Stage!=TeleporterStage.SelectionIssued)Stop("Teleporter destination click was acknowledged out of order.");
        Stage=TeleporterStage.AwaitingConfirmation;phaseStarted=now;
    }
    public void BeforeConfirmation(TeleporterObservation observation,long now)
    {
        CheckDeparture(observation,now);
        if(Stage!=TeleporterStage.AwaitingConfirmation || ConfirmationAttempted)Stop("Teleporter OK confirmation was already attempted or not expected.");
        ConfirmationAttempted=true;Stage=TeleporterStage.ConfirmationIssued;
    }
    public void ConfirmationSent(long now)
    {
        CheckTime(now);
        if(Stage!=TeleporterStage.ConfirmationIssued || ConfirmationWasSent)Stop("Teleporter OK click was acknowledged out of order or more than once.");
        ConfirmationWasSent=true;Stage=TeleporterStage.AwaitingLanding;phaseStarted=now;
    }
    public bool ObserveLanding(TeleporterObservation observation,long now)
    {
        if(Stage!=TeleporterStage.AwaitingLanding || !ConfirmationWasSent)Stop("Teleporter cannot admit a landing before its single OK click.");
        var character=CheckCommon(observation,now,true);
        if(profile.Landing.Near(character,observation.Zone,TeleporterProfile.LandingRadius))
        {
            if(!observation.Health.Known)return false;
            AcceptedCharacter=character;Stage=TeleporterStage.Complete;return true;
        }
        if(!LocalCharacter.Same(initial.Character!,character) || !profile.Departure.Near(character,observation.Zone,TeleporterProfile.SourceRadius))
            Stop("Teleporter landed outside the saved destination or recreated the character away from that destination.");
        return false;
    }
    public void ObserveUnavailable(TeleporterObservation context,long now)
    {
        if(Stage!=TeleporterStage.AwaitingLanding || !ConfirmationWasSent)Stop("Unreadable teleport observations are allowed only after the sent OK click.");
        CheckContext(context,now); // Does not renew the original phase deadline.
    }
    public Entity VerifySettledLanding(TeleporterObservation observation,long now)
    {
        if(Stage!=TeleporterStage.Complete || AcceptedCharacter==null)Stop("A settled landing requires an already verified destination.");
        var self=CheckCommon(observation,now);
        if(!LocalCharacter.Same(AcceptedCharacter!,self)||!profile.Landing.Near(self,observation.Zone,TeleporterProfile.LandingRadius))
            Stop("Teleporter landing changed before identity adoption. Hunting stopped.");
        return self;
    }
}
