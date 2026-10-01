namespace PoteHunter;

internal static class RepairScreen
{
    public static Rectangle Bounds(World world)
    {
        if(!NavigationOverlay.TryGetClientScreenBounds(world.Window,out var bounds) || !SystemInformation.VirtualScreen.Contains(bounds))
            throw new InvalidOperationException("Keep the complete game window visible for visual recovery.");
        return bounds;
    }
    public static Bitmap Capture(World world)
    {
        if(!Input.Allowed() || !world.CheckInputWindow().Allowed)
            throw new OperationCanceledException("Visual recovery needs the game in the foreground.");
        var bounds=Bounds(world);
        var frame=new Bitmap(bounds.Width,bounds.Height);
        try
        {
            using(var g=Graphics.FromImage(frame))g.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size,CopyPixelOperation.SourceCopy);
            int nonBlack=0;var levels=new HashSet<int>();
            for(int y=0;y<frame.Height;y+=Math.Max(1,frame.Height/20))for(int x=0;x<frame.Width;x+=Math.Max(1,frame.Width/30))
            {
                var pixel=frame.GetPixel(x,y);int light=(pixel.R+pixel.G+pixel.B)/3;
                if(light>15)nonBlack++;levels.Add(light/8);
            }
            if(nonBlack<30 || levels.Count<5)throw new InvalidOperationException("The game image is unavailable. Use a visible windowed/borderless game for visual recovery.");
            return frame;
        }
        catch {frame.Dispose();throw;}
    }
}

internal sealed class LiveRepairSurface(World world,RepairProfile? profile,Action validate,CancellationToken runToken) : IRepairSurface
{
    readonly HashSet<RepairAction> attempted=new();
    internal sealed record View(RepairObservation State,Point? Hammer,Point? Confirm,VisualControl? HammerVisual,VisualControl? ConfirmVisual);
    internal static View Recognize(Bitmap image,RepairProfile? profile,CancellationToken token,Func<RepairVisuals> automatic)
    {
        token.ThrowIfCancellationRequested();
        if(profile!=null && image.Width==profile.Width && image.Height==profile.Height)
        {
            // A validated user setup is authoritative. Do not delay its small
            // patch checks behind a scan of every pixel at every UI scale.
            bool inventory=profile.Inventory.MatchesText(image)||profile.Inventory.MatchesControl(image);
            bool hammer=inventory && profile.Hammer.MatchesControl(image);
            bool prompt=profile.Prompt.MatchesText(image),confirm=prompt && profile.Confirm.MatchesText(image);
            token.ThrowIfCancellationRequested();
            return new(new(inventory,hammer,prompt,confirm),hammer?profile.Hammer.Center:null,
                confirm?profile.Confirm.Center:null,null,null);
        }
        var visual=automatic();token.ThrowIfCancellationRequested();
        return new(new(visual.Hammer!=null,visual.Hammer!=null,visual.Confirm!=null,visual.Confirm!=null),
            visual.Hammer?.Point,visual.Confirm?.Point,visual.Hammer,visual.Confirm);
    }
    async Task<View> Read(CancellationToken token)
    {
        runToken.ThrowIfCancellationRequested();token.ThrowIfCancellationRequested();validate();
        using var image=RepairScreen.Capture(world);
        var view=await Task.Run(()=>Recognize(image,profile,token,()=>RecoveryVision.Repair(image,token)),token);
        runToken.ThrowIfCancellationRequested();validate();return view;
    }
    public async Task<RepairObservation> Observe(CancellationToken token)=>(await Read(token)).State;
    public async Task Perform(RepairAction action,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(!attempted.Add(action))throw new InvalidOperationException("Repair input was already attempted; refusing a duplicate.");
        var view=await Read(token);var state=view.State;
        if(action is RepairAction.OpenInventory or RepairAction.CloseInventory)
        {
            if(state.Prompt || state.Inventory!=(action==RepairAction.CloseInventory))
                throw new InvalidOperationException("Inventory changed before its repair input.");
            TraceLog.Record("repair inventory key requested",new{Action=action.ToString(),Key="I",Recognition=profile!=null?"Custom setup":"Automatic templates"});
            await Input.Key(Keys.I,70,token);
            TraceLog.Record("repair inventory key sent",new{Action=action.ToString(),Key="I",GameResponseVerified=false});
            return;
        }
        bool confirm=action==RepairAction.Confirm;
        if(confirm ? !state.Prompt||!state.Confirm : !state.Inventory||!state.Hammer||state.Prompt)
            throw new InvalidOperationException("The expected repair control changed before clicking.");
        Rectangle bounds=RepairScreen.Bounds(world);
        Point local=(confirm?view.Confirm:view.Hammer) ?? throw new InvalidOperationException("Repair button position is unavailable.");
        var screen=new Point(bounds.X+local.X,bounds.Y+local.Y);
        Input.MovePointer(screen,token);
        await Input.Delay(80,token);
        // Hover may change the button appearance. Recheck the independent
        // dialog/inventory marker immediately before clicking its button.
        bool marker;
        using(var image=RepairScreen.Capture(world))
        {
            var visual=confirm?view.ConfirmVisual:view.HammerVisual;
            marker=await Task.Run(()=>visual!=null ? RecoveryVision.RepairMarker(image,visual,confirm,token) :
                profile!=null && (confirm?profile.Prompt.MatchesText(image):
                    (profile.Inventory.MatchesText(image)||profile.Inventory.MatchesControl(image))&&!profile.Prompt.MatchesText(image)),token);
        }
        validate();token.ThrowIfCancellationRequested();
        var cursor=Input.Cursor();
        if(RepairScreen.Bounds(world)!=bounds || Math.Abs(cursor.X-screen.X)>2 || Math.Abs(cursor.Y-screen.Y)>2 ||
            !marker)
            throw new InvalidOperationException("Repair focus, dialog, or pointer position changed. No click was sent.");
        await Input.Click(false,token);
        TraceLog.Record("repair control clicked",new{Control=action.ToString()});
    }
    public Task Delay(CancellationToken token)=>Input.Delay(120,token);
}
