namespace PoteHunter;

internal interface IRevivalSurface
{
    Health Health();
    Task<VisualControl?> Find(CancellationToken token);
    // False means the screen/pointer changed before any click was sent.
    Task<bool> Open(CancellationToken token);
    Task<bool> Confirm(VisualControl button,CancellationToken token);
    Task Delay(int milliseconds,CancellationToken token);
    long Now {get;}
}

internal static class VisualRevival
{
    internal const int DeathWaitMilliseconds=2000;
    const int OpeningGapMilliseconds=1000,OpeningClicks=3;
    internal static Point OpeningPoint(Size size)=>new(size.Width/2,size.Height/2);

    public static async Task Run(IRevivalSurface surface,long deathAt,CancellationToken token)
    {
        long readyAt=Math.Max(surface.Now,deathAt+DeathWaitMilliseconds);
        long deadline=readyAt+15000,nextOpen=readyAt;
        int openings=0;
        bool dialogRecognized=false;
        while(surface.Now<deadline)
        {
            token.ThrowIfCancellationRequested();
            var hp=surface.Health();
            if(hp.Known && !hp.Dead)return;
            if(!hp.Known){await surface.Delay(100,token);continue;}
            // The death animation must finish even when Revive is already visible.
            if(surface.Now<readyAt)
            {
                await surface.Delay((int)Math.Min(100,readyAt-surface.Now),token);continue;
            }
            var button=await surface.Find(token);
            hp=surface.Health();
            if(hp.Known && !hp.Dead)return;
            if(!hp.Known){await surface.Delay(100,token);continue;}
            if(button!=null)
            {
                dialogRecognized=true;
                // Exactly one recognized confirmation per death. Unreadable
                // HP afterward waits for confirmation rather than clicking again.
                if(!await surface.Confirm(button,token))
                {
                    await surface.Delay(100,token);continue;
                }
                long aliveBy=surface.Now+15000;
                while(true)
                {
                    token.ThrowIfCancellationRequested();hp=surface.Health();
                    if(hp.Known && !hp.Dead)return;
                    if(surface.Now>=aliveBy)break;
                    await surface.Delay((int)Math.Min(200,aliveBy-surface.Now),token);
                }
                throw new InvalidOperationException("Revive was clicked once, but living HP was not confirmed. Recovery stopped.");
            }
            if(dialogRecognized)throw new InvalidOperationException("Recognized Revive dialog disappeared before confirmation. Recovery stopped without further opening clicks.");
            if(surface.Now>=nextOpen)
            {
                if(openings==OpeningClicks)throw new InvalidOperationException("Revive button was not recognized after three opening clicks. Revive manually or check Custom revival setup.");
                if(await surface.Open(token))openings++;
                // Check the dialog and HP between clicks; never click through a
                // recognized popup. Give the last opening click time to settle.
                nextOpen=surface.Now+OpeningGapMilliseconds;
            }
            await surface.Delay((int)Math.Clamp(nextOpen-surface.Now,1,100),token);
        }
        throw new InvalidOperationException("Revival timed out waiting for readable HP, a stable pointer, or the recognized dialog. Check the revival setup and keep the game in front.");
    }
}

internal sealed class LiveRevivalSurface(World world,Entity original,int processId,int zone,Action<string> status) : IRevivalSurface
{
    readonly RevivalProfile? custom=RevivalProfile.Load(world.ClientHash,RepairScreen.Bounds(world).Size);
    long nextRetryLog;
    bool Retry(string reason,object? details=null)
    {
        status("Revival pending: "+reason);
        if(Now>=nextRetryLog)
        {
            nextRetryLog=Now+1000;
            TraceLog.Record("revival click deferred",new{Reason=reason,Details=details});
        }
        return false;
    }
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
    public async Task<bool> Open(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();if(!Dead())return false;
        var bounds=RepairScreen.Bounds(world);
        Point local=VisualRevival.OpeningPoint(bounds.Size);
        var click=new Point(bounds.Left+local.X,bounds.Top+local.Y);
        Input.MovePointer(click,token);await Input.Delay(80,token);
        if(!Dead())return false;
        if(RepairScreen.Bounds(world)!=bounds)return Retry("game window moved; checking its new position");
        using(var frame=RepairScreen.Capture(world))
        {
            bool canOpen=await Task.Run(()=>custom!=null?custom.Find(frame,token)==null:RecoveryVision.Revive(frame,token)==null,token);
            if(!canOpen)return Retry("death screen changed; checking for the Revive button");
        }
        token.ThrowIfCancellationRequested();if(!Dead())return false;
        var cursor=Input.Cursor();
        if(RepairScreen.Bounds(world)!=bounds || Math.Abs(cursor.X-click.X)>2 || Math.Abs(cursor.Y-click.Y)>2)
            return Retry("pointer moved before the opening click; repositioning",new{Expected=click,Actual=cursor});
        await Input.Click(false,token);
        TraceLog.Record("revival popup opening click",new{Visual=true,Custom=custom!=null});
        return true;
    }
    public async Task<bool> Confirm(VisualControl button,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();if(!Dead())return false;
        var bounds=RepairScreen.Bounds(world);
        var screen=new Point(bounds.Left+button.Point.X,bounds.Top+button.Point.Y);
        Input.MovePointer(screen,token);await Input.Delay(80,token);
        if(!Dead())return false;
        using var frame=RepairScreen.Capture(world);
        bool stillPresent=await Task.Run(()=>custom!=null?custom.CanConfirm(frame,button,token):
            !button.Custom && RecoveryVision.ReviveStillPresent(frame,button,token),token);
        token.ThrowIfCancellationRequested();if(!Dead())return false;
        var cursor=Input.Cursor();
        if(!stillPresent || RepairScreen.Bounds(world)!=bounds || Math.Abs(cursor.X-screen.X)>2 || Math.Abs(cursor.Y-screen.Y)>2)
            return Retry("dialog or pointer changed before confirmation; checking again",new{Expected=screen,Actual=cursor,DialogPresent=stillPresent});
        await Input.Click(false,token);status("Revive clicked; waiting for living HP");
        TraceLog.Record("visual revive confirmed",new{button.Point,button.Scale,button.Custom});
        return true;
    }
    public Task Delay(int milliseconds,CancellationToken token)=>Input.Delay(milliseconds,token);
}
