using System.Drawing.Drawing2D;

namespace PoteHunter;

/// <summary>A small draggable, non-activating overlay for tracked farm loot.</summary>
internal sealed partial class LootTrackerOverlay : Form
{
    const int HeaderHeight=30;
    const int WsExToolWindow=0x00000080;
    const int WsExLayered=0x00080000;
    const int WsExNoActivate=0x08000000;
    const int SwShownoactivate=4;
    const int WmMouseactivate=0x0021;
    const int MaNoactivate=3;
    const int HWndTopmost=-1;
    const uint SwpNoactivate=0x0010;
    const uint SwpNomove=0x0002;
    const uint SwpNosize=0x0001;
    readonly Func<LootTrackerSnapshot> snapshotProvider;
    readonly Action<Point>? positionCommitted;
    readonly Action? resetLoot;
    readonly Action? resetTimer;
    int pressedReset;
    readonly Font titleFont=new("Georgia",10f,FontStyle.Regular);
    readonly Font rowFont=new("Segoe UI",8.5f);
    readonly Font detailFont=new("Segoe UI",7.5f);
    readonly Pen framePen=new(ImperialTheme.Gold,1f);
    bool dragging;
    Point dragOffset;
    int design=-1;
    int scalePercent=100;
    int backgroundOpacityPercent=LootOverlayBackground.DefaultOpacityPercent;
    internal int BackgroundOpacityPercent=>backgroundOpacityPercent;
    int screenScaleLimit=200;
    internal bool IsTransparentDesign=>design is 1 or 3;
    Size availableArea=new(10000,10000);
    Size TransparentLogicalSize=>design==1?RunicFoldRenderer.LogicalSize:RunicStripRenderer.LogicalSize;
    Size TransparentSize=>design==1?RunicFoldRenderer.SizeAt(EffectiveScalePercent):RunicStripRenderer.SizeAt(EffectiveScalePercent);
    internal int Design=>design;
    internal int ScalePercent=>scalePercent;
    internal int EffectiveScalePercent=>Math.Min(scalePercent,screenScaleLimit);
    internal void SetDesign(int value)
    {
        value=Math.Clamp(value,0,3);if(design==value)return;
        bool changesTransparency=IsTransparentDesign!=(value is 1 or 3);
        design=value;
        screenScaleLimit=AreaScaleLimit(availableArea);
        Size=IsTransparentDesign?TransparentSize:value==2?new Size(360,422):new Size(390,362);
        BackColor=value==2?Color.FromArgb(221,204,164):ImperialTheme.Window;
        Opacity=IsTransparentDesign?1:.94;
        // SetLayeredWindowAttributes (Form.Opacity) and per-pixel composition
        // must use fresh layered-window state when switching presentations.
        if(changesTransparency && IsHandleCreated)RecreateHandle();
        RefreshSnapshot();
    }

    internal void SetBackgroundOpacity(int value)
    {
        value=Math.Clamp(value,0,100);if(backgroundOpacityPercent==value)return;
        backgroundOpacityPercent=value;RefreshSnapshot();
    }

    internal void SetScale(int value)
    {
        value=Math.Clamp(value,50,200);if(scalePercent==value)return;
        scalePercent=value;
        if(IsTransparentDesign){Size=TransparentSize;RefreshSnapshot();}
    }

    internal void FitToArea(Size available)
    {
        availableArea=available;
        int limit=AreaScaleLimit(available);if(screenScaleLimit==limit)return;
        screenScaleLimit=limit;
        if(IsTransparentDesign){Size=TransparentSize;RefreshSnapshot();}
    }

    int AreaScaleLimit(Size available)=>Math.Clamp((int)Math.Min((long)available.Width*100/TransparentLogicalSize.Width,(long)available.Height*100/TransparentLogicalSize.Height),50,200);

    internal void RefreshSnapshot()
    {
        if(IsTransparentDesign && IsHandleCreated && Visible)PresentTransparentOverlay();
        else Invalidate();
    }

    public LootTrackerOverlay(Func<LootTrackerSnapshot> snapshotProvider,Action<Point>? positionCommitted=null,Action? resetLoot=null,Action? resetTimer=null)
    {
        this.snapshotProvider=snapshotProvider??throw new ArgumentNullException(nameof(snapshotProvider));
        this.positionCommitted=positionCommitted;
        this.resetLoot=resetLoot;this.resetTimer=resetTimer;
        AutoScaleMode=AutoScaleMode.None;BackColor=ImperialTheme.Window;DoubleBuffered=true;
        FormBorderStyle=FormBorderStyle.None;Opacity=.94;ShowIcon=false;ShowInTaskbar=false;
        StartPosition=FormStartPosition.Manual;TopMost=false;Size=new Size(390,330);
        Cursor=Cursors.SizeAll;
        SetDesign(0);
        MouseDown+=(_,e)=>
        {
            if(e.Button!=MouseButtons.Left)return;
            pressedReset=ResetActionAt(e.Location);
            if(pressedReset!=0){Capture=true;return;}
            if(e.Y>=(IsTransparentDesign?(design==1?46:40)*EffectiveScalePercent/100:HeaderHeight))return;
            dragging=true;dragOffset=e.Location;Capture=true;
        };
        MouseMove+=(_,e)=>
        {
            if(dragging)Location=new Point(Left+e.X-dragOffset.X,Top+e.Y-dragOffset.Y);
            else Cursor=ResetActionAt(e.Location)!=0?Cursors.Hand:Cursors.SizeAll;
        };
        MouseUp+=(_,e)=>
        {
            if(e.Button!=MouseButtons.Left)return;
            int action=pressedReset;bool moved=dragging;pressedReset=0;dragging=false;Capture=false;
            if(action!=0 && action==ResetActionAt(e.Location))InvokeReset(action);
            else if(moved)positionCommitted?.Invoke(Location);
        };
        MouseCaptureChanged+=(_,_)=>{if(!Capture){pressedReset=0;dragging=false;}};
    }

    protected override bool ShowWithoutActivation=>true;

    protected override void SetVisibleCore(bool value)
    {
        if(!value){base.SetVisibleCore(false);return;}
        base.SetVisibleCore(true);
        if(IsHandleCreated)
        {
            if(IsTransparentDesign)PresentTransparentOverlay();
            ShowWindow(Handle,SwShownoactivate);
            SetWindowPos(Handle,new IntPtr(HWndTopmost),0,0,0,0,SwpNomove|SwpNosize|SwpNoactivate);
        }
    }

    protected override CreateParams CreateParams
    {
        get { var parameters=base.CreateParams;parameters.ExStyle|=WsExLayered|WsExNoActivate|WsExToolWindow;return parameters; }
    }

    protected override void WndProc(ref Message message)
    {
        if(message.Msg==WmMouseactivate){message.Result=(IntPtr)MaNoactivate;return;}
        base.WndProc(ref message);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if(IsTransparentDesign)
        {
            if(design==1)RunicFoldRenderer.Draw(e.Graphics,snapshotProvider(),EffectiveScalePercent,backgroundOpacityPercent);
            else RunicStripRenderer.Draw(e.Graphics,snapshotProvider(),EffectiveScalePercent,backgroundOpacityPercent);
            return;
        }
        base.OnPaint(e);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);
        if(design!=0)DrawParchment(e.Graphics,snapshotProvider());
        else DrawImperialHud(e.Graphics,snapshotProvider());
        LootOverlayResetButtons.Draw(e.Graphics,design,ClientSize);
    }

    internal int ResetActionAt(Point point)
    {
        float scale=IsTransparentDesign?EffectiveScalePercent/100f:1;
        Size logical=IsTransparentDesign?TransparentLogicalSize:ClientSize;
        var position=new PointF(point.X/scale,point.Y/scale);
        return LootOverlayResetButtons.LootBounds(design,logical).Contains(position)?1:
            LootOverlayResetButtons.TimerBounds(design,logical).Contains(position)?2:0;
    }

    internal void InvokeReset(int action)
    {
        if(action==1)resetLoot?.Invoke();else if(action==2)resetTimer?.Invoke();
        RefreshSnapshot();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if(!IsTransparentDesign)base.OnPaintBackground(e);
    }

    void DrawParchment(Graphics g,LootTrackerSnapshot snapshot)
    {
        
        Color ink=Color.FromArgb(62,46,29);
        Color muted=Color.FromArgb(112,88,53);
        Color accent=Color.FromArgb(114,66,26);
        using var border=new Pen(accent);using var inkBrush=new SolidBrush(ink);using var mutedBrush=new SolidBrush(muted);
        using var accentBrush=new SolidBrush(accent);using var header=new SolidBrush(Color.FromArgb(199,176,131));
        g.FillRectangle(header,0,0,Width,HeaderHeight);g.DrawRectangle(border,0,0,Width-1,Height-1);
        using var heading=new Font("Georgia",11,FontStyle.Bold);
        g.DrawString("Parchment Ledger",heading,inkBrush,new PointF(10,6));
        g.DrawString("drag header to move",detailFont,mutedBrush,new PointF(210,9));
        g.DrawString($"Session {FormatDuration(snapshot.Elapsed)}  Â·  Active {FormatDuration(snapshot.RateElapsed)}  Â·  Zone {snapshot.Zone}",detailFont,mutedBrush,new PointF(10,37));
        g.DrawString("Collected",detailFont,mutedBrush,new PointF(154,61));g.DrawString("Per hour",detailFont,mutedBrush,new PointF(270,61));
        float y=82;
        foreach(var item in snapshot.TrackedLoot)
        {
            g.DrawString(LootTrackerSnapshot.DisplayName(item.Name),rowFont,inkBrush,new PointF(12,y));
            TextRenderer.DrawText(g,snapshot.AmountText(item.Name),rowFont,new Rectangle(118,(int)y,120,20),ink,TextFormatFlags.Right|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g,snapshot.RateText(item.Name,"N1"),rowFont,new Rectangle(240,(int)y,104,20),accent,TextFormatFlags.Right|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
            y+=24;
        }
        g.DrawLine(border,10,y,Width-10,y);y+=8;
        foreach(var source in snapshot.Sources)
        {g.DrawString($"{source.Source}  Â·  {source.Kills:N0} kills  Â·  {source.Drops:N0} drops",detailFont,inkBrush,new PointF(12,y));y+=19;}
        y+=5;
        foreach(var drop in snapshot.RecentDrops.Take(2))
        {TextRenderer.DrawText(g,$"{drop.Source}: {drop.Name}",detailFont,new Rectangle(12,(int)y,Width-24,17),accent,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);y+=17;}
        g.DrawString($"Gold = net wallet  Â·  Pending kills {snapshot.PendingKills}",detailFont,mutedBrush,new PointF(12,Height-18));
    }

    static string FormatDuration(TimeSpan duration)
    {
        duration=duration<TimeSpan.Zero?TimeSpan.Zero:duration;
        int hours=(int)Math.Floor(duration.TotalHours);
        return hours>0?$"{hours:00}:{duration.Minutes:00}:{duration.Seconds:00}":$"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing){titleFont.Dispose();rowFont.Dispose();detailFont.Dispose();framePen.Dispose();}
        base.Dispose(disposing);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr window,int command);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr window,IntPtr insertAfter,int x,int y,int width,int height,uint flags);
}
