namespace PoteHunter;

internal static class RepairScreen
{
    public static Rectangle Bounds(World world)
    {
        if(!NavigationOverlay.TryGetClientScreenBounds(world.Window,out var bounds) || !SystemInformation.VirtualScreen.Contains(bounds))
            throw new InvalidOperationException("Keep the complete game window visible for repair.");
        return bounds;
    }
    public static Bitmap Capture(World world)
    {
        if(!Input.Allowed() || !world.CheckInputWindow().Allowed)
            throw new OperationCanceledException("Repair needs the game in the foreground.");
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
            if(nonBlack<30 || levels.Count<5)throw new InvalidOperationException("The game image is unavailable. Use a visible windowed/borderless game for repair setup.");
            return frame;
        }
        catch {frame.Dispose();throw;}
    }
}

internal sealed class LiveRepairSurface(World world,RepairProfile profile,Action validate,CancellationToken runToken) : IRepairSurface
{
    readonly HashSet<RepairAction> attempted=new();
    public RepairObservation Observe()
    {
        runToken.ThrowIfCancellationRequested();validate();
        using var image=RepairScreen.Capture(world);
        if(image.Width!=profile.Width || image.Height!=profile.Height)
            throw new InvalidOperationException("The game window size changed during repair. Configure repair again.");
        return new(profile.Inventory.Matches(image),profile.Hammer.Matches(image),profile.Prompt.Matches(image),profile.Confirm.Matches(image));
    }
    public async Task Perform(RepairAction action,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(!attempted.Add(action))throw new InvalidOperationException("Repair input was already attempted; refusing a duplicate.");
        var state=Observe();
        if(action is RepairAction.OpenInventory or RepairAction.CloseInventory)
        {
            if(state.Prompt || state.Inventory!=(action==RepairAction.CloseInventory))
                throw new InvalidOperationException("Inventory changed before its repair input.");
            await Input.Key(Keys.I,70,token);return;
        }
        bool confirm=action==RepairAction.Confirm;
        if(confirm ? !state.Prompt||!state.Confirm : !state.Inventory||!state.Hammer||state.Prompt)
            throw new InvalidOperationException("The expected repair control changed before clicking.");
        Rectangle bounds=RepairScreen.Bounds(world);
        Point local=confirm?profile.Confirm.Center:profile.Hammer.Center;
        var screen=new Point(bounds.X+local.X,bounds.Y+local.Y);
        Input.MovePointer(screen,token);
        await Input.Delay(80,token);
        // Hover may change the button appearance. Recheck the independent
        // dialog/inventory marker immediately before clicking its button.
        state=Observe();
        var cursor=Input.Cursor();
        if(RepairScreen.Bounds(world)!=bounds || Math.Abs(cursor.X-screen.X)>2 || Math.Abs(cursor.Y-screen.Y)>2 ||
            (confirm?!state.Prompt:!state.Inventory||state.Prompt))
            throw new InvalidOperationException("Repair focus, dialog, or pointer position changed. No click was sent.");
        await Input.Click(false,token);
        TraceLog.Record("repair control clicked",new{Control=action.ToString()});
    }
    public Task Delay(CancellationToken token)=>Input.Delay(120,token);
}
