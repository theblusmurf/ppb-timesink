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
    readonly LootPresentationGate presentationGate=new();
    readonly LootLayeredSurface layeredSurface=new();
    LootTrackerSnapshot? presentedSnapshot;
    internal int LayeredSurfaceAllocations=>layeredSurface.Allocations;
    readonly Action<Point>? positionCommitted;
    readonly Action? resetLoot;
    readonly Action? resetTimer;
    int pressedReset;
    readonly Font titleFont=new("Segoe UI Semibold",9.5f,FontStyle.Regular);
    readonly Font rowFont=new("Segoe UI",8.5f);
    readonly Font detailFont=new("Segoe UI",7.5f);
    readonly Pen framePen=new(ImperialTheme.Border,1f);
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
        Size=IsTransparentDesign?TransparentSize:value==2?new Size(360,422):new Size(310,550);
        BackColor=ImperialTheme.Window;
        Opacity=IsTransparentDesign?1:.94;
        // SetLayeredWindowAttributes (Form.Opacity) and per-pixel composition
        // must use fresh layered-window state when switching presentations.
        if(changesTransparency && IsHandleCreated)RecreateHandle();
        RefreshSnapshot();
    }

    internal void SetBackgroundOpacity(int value)
    {
        value=Math.Clamp(value,0,100);if(backgroundOpacityPercent==value)return;
        backgroundOpacityPercent=value;RefreshSnapshot(force:true);
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

    internal void RefreshSnapshot(bool force=false)
    {
        var snapshot=snapshotProvider();
        if(!presentationGate.Changed(snapshot,design,EffectiveScalePercent,backgroundOpacityPercent,force))return;
        presentedSnapshot=snapshot;
        if(IsTransparentDesign && IsHandleCreated && Visible)PresentTransparentOverlay(snapshot);
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
            if(IsTransparentDesign)PresentTransparentOverlay(presentedSnapshot=snapshotProvider());
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
            if(design==1)RunicFoldRenderer.Draw(e.Graphics,presentedSnapshot??snapshotProvider(),EffectiveScalePercent,backgroundOpacityPercent);
            else RunicStripRenderer.Draw(e.Graphics,presentedSnapshot??snapshotProvider(),EffectiveScalePercent,backgroundOpacityPercent);
            return;
        }
        base.OnPaint(e);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);
        if(design!=0)DrawParchment(e.Graphics,presentedSnapshot??snapshotProvider());
        else DrawImperialHud(e.Graphics,presentedSnapshot??snapshotProvider());
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
        RefreshSnapshot(force:true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if(!IsTransparentDesign)base.OnPaintBackground(e);
    }

    void DrawParchment(Graphics g,LootTrackerSnapshot snapshot)
    {
        DrawOverlayHeader(g,"ORBITAL / LEDGER");
        OverlayText(g,"drag header",detailFont,ImperialTheme.Muted,new(252,5,96,22),TextFormatFlags.Right);
        OverlayText(g,$"Session {FormatDuration(snapshot.Elapsed)} · Active {FormatDuration(snapshot.RateElapsed)}",detailFont,ImperialTheme.Text,new(12,36,Width-24,16));
        OverlayText(g,"Gold: net wallet · items: detected drops",detailFont,ImperialTheme.Muted,new(12,52,Width-24,15));
        OverlayText(g,"RESOURCE",detailFont,ImperialTheme.Muted,new(12,68,106,16));
        OverlayText(g,"TOTAL",detailFont,ImperialTheme.Muted,new(118,68,120,16),TextFormatFlags.Right);
        OverlayText(g,"/ ACTIVE HOUR",detailFont,ImperialTheme.Muted,new(244,68,102,16),TextFormatFlags.Right);
        string[] resources=["Gold","Silvin","Mithril","Iternium","Fehu","Gems"];
        for(int i=0;i<resources.Length;i++)
        {
            string name=resources[i];int y=87+i*26;
            LootOverlayArtwork.DrawResource(g,name,new(12,y,24,24));
            OverlayText(g,LootTrackerSnapshot.DisplayName(name),rowFont,ImperialTheme.Text,new(44,y,76,24));
            OverlayText(g,snapshot.AmountText(name),rowFont,ImperialTheme.Text,new(122,y,116,24),TextFormatFlags.Right,true);
            OverlayText(g,snapshot.RateText(name,"N1"),rowFont,ImperialTheme.Accent,new(244,y,102,24),TextFormatFlags.Right,true);
        }
        g.DrawLine(framePen,12,246,Width-12,246);
        for(int i=0;i<Math.Min(4,snapshot.Sources.Count);i++)
        {
            var source=snapshot.Sources[i];int y=252+i*17;
            OverlayText(g,source.Source,detailFont,ImperialTheme.Text,new(12,y,110,17));
            OverlayText(g,$"{source.Kills:N0} kills · {source.Drops:N0} drops",detailFont,ImperialTheme.Muted,new(125,y,Width-137,17),TextFormatFlags.Right);
        }
        for(int i=0;i<Math.Min(2,snapshot.RecentDrops.Count);i++)
        {
            var drop=snapshot.RecentDrops[i];
            OverlayText(g,$"{drop.Source}: {drop.Name}",detailFont,ImperialTheme.Muted,new(12,329+i*17,Width-24,17));
        }
        OverlayText(g,$"Zone {snapshot.Zone} · Pending kills {snapshot.PendingKills}",detailFont,ImperialTheme.Muted,new(12,Height-19,Width-24,16));
    }

    void DrawOverlayHeader(Graphics g,string title)
    {
        using var header=new SolidBrush(ImperialTheme.Surface);
        using var accent=new Pen(ImperialTheme.Accent,2);
        g.FillRectangle(header,0,0,Width,HeaderHeight);
        g.DrawRectangle(framePen,0,0,Width-1,Height-1);
        g.DrawLine(framePen,0,HeaderHeight,Width,HeaderHeight);
        g.DrawLine(accent,1,1,1,HeaderHeight-1);
        g.DrawImage(ImperialTheme.Logo.Value,new Rectangle(10,3,23,24));
        OverlayText(g,title,titleFont,ImperialTheme.Text,new(41,3,Width-53,24));
    }

    static void OverlayText(Graphics graphics,string value,Font font,Color color,Rectangle bounds,TextFormatFlags alignment=TextFormatFlags.Left,bool fit=false)
    {
        var flags=alignment|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding|TextFormatFlags.SingleLine;
        if(!fit){TextRenderer.DrawText(graphics,value,font,bounds,color,flags);return;}
        float size=font.Size;
        while(size>5)
        {
            using var measured=new Font(font.FontFamily,size,font.Style);
            if(TextRenderer.MeasureText(graphics,value,measured,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width<=bounds.Width)break;
            size-=.25f;
        }
        using var fitted=new Font(font.FontFamily,size,font.Style);
        TextRenderer.DrawText(graphics,value,fitted,bounds,color,flags);
    }

    static string FormatDuration(TimeSpan duration)
    {
        duration=duration<TimeSpan.Zero?TimeSpan.Zero:duration;
        int hours=(int)Math.Floor(duration.TotalHours);
        return hours>0?$"{hours:00}:{duration.Minutes:00}:{duration.Seconds:00}":$"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing){layeredSurface.Dispose();titleFont.Dispose();rowFont.Dispose();detailFont.Dispose();framePen.Dispose();}
        base.Dispose(disposing);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr window,int command);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr window,IntPtr insertAfter,int x,int y,int width,int height,uint flags);
}
