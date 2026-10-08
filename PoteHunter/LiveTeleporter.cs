namespace PoteHunter;

internal sealed class TeleporterSceneUnavailableException(Exception inner):InvalidOperationException("The teleporter scene is temporarily unavailable.",inner);
internal sealed class LiveTeleporter(World world,TeleporterProfile profile,TeleporterTransaction transaction,CancellationToken token)
{
    static T Scene<T>(Func<T> read)
    {
        try{return read();}catch(InvalidOperationException ex){throw new TeleporterSceneUnavailableException(ex);}
    }
    static TeleporterObservation Context(World world,CancellationToken token)
    {
        Input.CheckSafety(token);var state=world.CheckInputWindow();
        if(!world.ConnectionVerified||!state.Allowed)throw new OperationCanceledException("The verified teleporter game window is unavailable.");
        return new(world.ClientHash,world.Pid,world.Window,RepairScreen.Bounds(world).Size,null,default,0,state.Allowed,token.IsCancellationRequested);
    }
    public static TeleporterObservation Observe(World world,CancellationToken token)
    {
        Input.CheckSafety(token);
        var state=world.CheckInputWindow();
        if(!world.ConnectionVerified||!state.Allowed)throw new OperationCanceledException("The verified teleporter game window is unavailable.");
        string hash=world.ClientHash;int pid=world.Pid,zone=Scene(world.ActiveZone);nint window=world.Window;var bounds=RepairScreen.Bounds(world);
        var before=Scene(world.LocalPlayer);var hp=Scene(()=>world.TargetHealth(before.Id));
        var self=Scene(world.LocalPlayer);var currentState=world.CheckInputWindow();
        Input.CheckSafety(token);
        if(Scene(world.ActiveZone)!=zone||world.Pid!=pid||world.Window!=window||world.ClientHash!=hash||RepairScreen.Bounds(world)!=bounds||!currentState.Allowed||
            before.Id!=self.Id||before.Name!=self.Name||before.Model!=self.Model)
            throw new InvalidOperationException("Teleporter observations changed while reading the character, health or game window.");
        if(!LocalCharacter.Same(before,self)||(before.Position-self.Position).Length>.5||Math.Abs(before.Height-self.Height)>.5)
            throw new TeleporterSceneUnavailableException(new InvalidOperationException("Character body or position changed during the scene read."));
        return new(hash,pid,window,bounds.Size,self,hp,zone,currentState.Allowed,token.IsCancellationRequested);
    }
    async Task<VisualControl> WaitControl(bool confirmation)
    {
        while(true)
        {
            transaction.CheckDeparture(Observe(world,token),Environment.TickCount64);
            using var image=RepairScreen.Capture(world);
            var control=await Task.Run(()=>confirmation?profile.FindConfirmation(image,token):profile.FindSelection(image,token),token);
            transaction.CheckDeparture(Observe(world,token),Environment.TickCount64);
            if(control!=null)return control;
            await Input.Delay(100,token);
        }
    }
    async Task Click(VisualControl control,bool confirmation)
    {
        transaction.CheckDeparture(Observe(world,token),Environment.TickCount64);
        Rectangle bounds=RepairScreen.Bounds(world);
        Point screen=new(bounds.X+control.Point.X,bounds.Y+control.Point.Y);
        Input.MovePointer(screen,token);await Input.Delay(80,token);
        using var image=RepairScreen.Capture(world);
        bool recognized=await Task.Run(()=>confirmation?profile.CanClickConfirmation(image,control,token):profile.CanClickSelection(image,control,token),token);
        transaction.CheckDeparture(Observe(world,token),Environment.TickCount64);
        if(!recognized||RepairScreen.Bounds(world)!=bounds||!Input.AlignPointer(screen,token,"teleporter pointer alignment"))
            throw new InvalidOperationException("Teleporter control, window, or pointer changed. No click was sent.");
        // Renew recognition after alignment; do not accept a stale hover frame.
        using var fresh=RepairScreen.Capture(world);
        if(!(confirmation?profile.CanClickConfirmation(fresh,control,token):profile.CanClickSelection(fresh,control,token)))
            throw new InvalidOperationException("Teleporter recognition changed before the click.");
        var observation=Observe(world,token);
        if(confirmation)transaction.BeforeConfirmation(observation,Environment.TickCount64);
        else transaction.BeforeSelection(observation,Environment.TickCount64);
        try
        {
            // A generation can change as soon as mouse-down reaches the game.
            // Acknowledge only a successfully emitted down, before yielding.
            await Input.ClickVerified(false,token,()=>
            {
                using var admitted=RepairScreen.Capture(world);
                bool marker=confirmation?profile.CanClickConfirmation(admitted,control,token):profile.CanClickSelection(admitted,control,token);
                transaction.CheckDeparture(Observe(world,token),Environment.TickCount64);
                return marker && RepairScreen.Bounds(world)==bounds && Input.Cursor()==screen;
            },()=>
            {
                if(confirmation)transaction.ConfirmationSent(Environment.TickCount64);
                else transaction.SelectionSent(Observe(world,token),Environment.TickCount64);
            });
        }
        finally {Input.HoldMouse(false,false,default);Input.Release();}
        TraceLog.Record(confirmation?"teleporter confirmation sent":"teleporter destination selected",new{profile.Destination,profile.DestinationSlot,GameResponseVerified=false});
    }
    public async Task<Entity> Run()
    {
        await Click(await WaitControl(false),false);
        await Click(await WaitControl(true),true);
        Entity? accepted=null;
        while(accepted==null)
        {
            Input.Release();Input.CheckSafety(token);
            TeleporterObservation observation;
            try{observation=Observe(world,token);}
            catch(TeleporterSceneUnavailableException)
            {
                transaction.ObserveUnavailable(Context(world,token),Environment.TickCount64);
                await Task.Delay(100,token);continue;
            }
            if(transaction.ObserveLanding(observation,Environment.TickCount64))accepted=transaction.AcceptedCharacter;
            else await Task.Delay(100,token);
        }
        await Task.Delay(120,token);Input.CheckSafety(token);
        var settled=Observe(world,token);
        var self=transaction.VerifySettledLanding(settled,Environment.TickCount64);
        if((self.Position-accepted.Position).Length>.5||Math.Abs(self.Height-accepted.Height)>.5)
            throw new InvalidOperationException("Teleporter landing did not remain settled with the verified character. Hunting stopped.");
        TraceLog.Record("teleporter landing verified",new{profile.Destination,Position=self.Position,self.Height,self.Generation,Zone=settled.Zone});
        return self;
    }
}
