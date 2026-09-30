namespace PoteHunter;

internal interface IRevivalSurface
{
    Health Health();
    Task<VisualControl?> Find(CancellationToken token);
    Task Open(CancellationToken token);
    Task Confirm(VisualControl button,CancellationToken token);
    Task Delay(int milliseconds,CancellationToken token);
    long Now {get;}
}

internal static class VisualRevival
{
    public static async Task Run(IRevivalSurface surface,long deathAt,CancellationToken token)
    {
        long deadline=surface.Now+15000,nextOpen=Math.Max(surface.Now,deathAt+2000);
        int openings=0;
        while(surface.Now<deadline)
        {
            token.ThrowIfCancellationRequested();
            var hp=surface.Health();
            if(hp.Known && !hp.Dead)return;
            if(!hp.Known){await surface.Delay(100,token);continue;}
            var button=await surface.Find(token);
            hp=surface.Health();
            if(hp.Known && !hp.Dead)return;
            if(!hp.Known){await surface.Delay(100,token);continue;}
            if(button!=null)
            {
                // Exactly one recognized confirmation per death. Unreadable
                // HP afterward waits for confirmation rather than clicking again.
                await surface.Confirm(button,token);
                long aliveBy=surface.Now+15000;
                while(surface.Now<aliveBy)
                {
                    token.ThrowIfCancellationRequested();hp=surface.Health();
                    if(hp.Known && !hp.Dead)return;
                    await surface.Delay(100,token);
                }
                throw new InvalidOperationException("Revive was clicked once, but living HP was not confirmed. Recovery stopped.");
            }
            if(surface.Now>=nextOpen)
            {
                if(openings==3)throw new InvalidOperationException("Revive button was not recognized after three popup-opening attempts. Revive manually or use key mode.");
                await surface.Open(token);openings++;nextOpen=surface.Now+1000;
            }
            await surface.Delay(100,token);
        }
        throw new InvalidOperationException("Health or the Revive dialog remained unavailable. Recovery stopped without another confirmation.");
    }
}

internal sealed class LiveRevivalSurface(World world,Entity original,int processId,int zone,Action<string> status) : IRevivalSurface
{
    readonly RevivalProfile? custom=RevivalProfile.Load(world.ClientHash,RepairScreen.Bounds(world).Size);
    public long Now=>Environment.TickCount64;
    public Health Health()
    {
        Entity self;
        try{self=world.LocalPlayer();}catch(InvalidOperationException){return default;}
        if(world.Pid!=processId || world.ActiveZone()!=zone || !RecoveryRouting.SameCharacter(original,self))
            throw new OperationCanceledException("Revival stopped: character, process, or map changed.");
        return world.TargetHealth(self.Id);
    }
    bool Dead(){var hp=Health();return hp.Known && hp.Dead;}
    public async Task<VisualControl?> Find(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();if(!Dead())return null;
        status(custom==null?"Recognizing the Revive button":"Recognizing the saved revival setup");
        using var frame=RepairScreen.Capture(world);
        var found=await Task.Run(()=>custom!=null?custom.Find(frame,token):RecoveryVision.Revive(frame,token),token);
        token.ThrowIfCancellationRequested();return Dead()?found:null;
    }
    public async Task Open(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();if(!Dead())return;
        var bounds=RepairScreen.Bounds(world);
        Point local=new(bounds.Width/2,bounds.Height/2);
        if(custom!=null)
        {
            using var frame=RepairScreen.Capture(world);
            if(!await Task.Run(()=>custom.CanOpen(frame,token),token))
                throw new InvalidOperationException("The saved Revive dialog or opening step was not recognized. Open the dialog manually, redo Custom revival setup, or choose Use automatic.");
            local=custom.Opening!.Point;
        }
        var click=new Point(bounds.Left+local.X,bounds.Top+local.Y);
        Input.MovePointer(click,token);await Input.Delay(80,token);
        if(!Dead() || RepairScreen.Bounds(world)!=bounds)return;
        if(custom!=null)
        {
            using var frame=RepairScreen.Capture(world);
            if(!await Task.Run(()=>custom.CanOpen(frame,token),token))
                throw new InvalidOperationException("The death screen changed before opening Revive. No click was sent.");
        }
        token.ThrowIfCancellationRequested();if(!Dead())return;
        var cursor=Input.Cursor();
        if(RepairScreen.Bounds(world)!=bounds || Math.Abs(cursor.X-click.X)>2 || Math.Abs(cursor.Y-click.Y)>2)
            throw new OperationCanceledException("Pointer moved before opening the revival dialog.");
        await Input.Click(false,token);
        TraceLog.Record("revival popup opening click",new{Visual=true,Custom=custom!=null});
    }
    public async Task Confirm(VisualControl button,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();if(!Dead())return;
        var bounds=RepairScreen.Bounds(world);
        var screen=new Point(bounds.Left+button.Point.X,bounds.Top+button.Point.Y);
        Input.MovePointer(screen,token);await Input.Delay(80,token);
        if(!Dead())return;
        using var frame=RepairScreen.Capture(world);
        bool stillPresent=await Task.Run(()=>custom!=null?custom.CanConfirm(frame,button,token):
            !button.Custom && RecoveryVision.ReviveStillPresent(frame,button,token),token);
        token.ThrowIfCancellationRequested();if(!Dead())return;
        var cursor=Input.Cursor();
        if(!stillPresent || RepairScreen.Bounds(world)!=bounds || Math.Abs(cursor.X-screen.X)>2 || Math.Abs(cursor.Y-screen.Y)>2)
            throw new InvalidOperationException("Revive dialog or pointer changed before confirmation. No click was sent.");
        await Input.Click(false,token);status("Revive clicked; waiting for living HP");
        TraceLog.Record("visual revive confirmed",new{button.Point,button.Scale,button.Custom});
    }
    public Task Delay(int milliseconds,CancellationToken token)=>Input.Delay(milliseconds,token);
}
