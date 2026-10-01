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
    readonly Font titleFont=new("Segoe UI Semibold",9f,FontStyle.Bold);
    readonly Font rowFont=new("Segoe UI",8.5f);
    readonly Font detailFont=new("Segoe UI",7.5f);
    readonly Pen framePen=new(Color.FromArgb(180,218,165,32),1f);
    bool dragging;
    Point dragOffset;
    int design=-1;
    int scalePercent=100;
    int screenScaleLimit=200;
    internal int Design=>design;
    internal int ScalePercent=>scalePercent;
    internal int EffectiveScalePercent=>Math.Min(scalePercent,screenScaleLimit);
    internal void SetDesign(int value)
    {
        value=Math.Clamp(value,0,3);if(design==value)return;
        bool changesTransparency=(design==3)!=(value==3);
        design=value;Size=value switch{1=>new Size(640,144),2=>new Size(360,390),3=>RunicStripRenderer.SizeAt(EffectiveScalePercent),_=>new Size(390,330)};
        BackColor=value==2?Color.FromArgb(221,204,164):Color.FromArgb(15,22,31);
        Opacity=value==3?1:.94;
        // SetLayeredWindowAttributes (Form.Opacity) and per-pixel composition
        // must use fresh layered-window state when switching presentations.
        if(changesTransparency && IsHandleCreated)RecreateHandle();
        RefreshSnapshot();
    }

    internal void SetScale(int value)
    {
        value=Math.Clamp(value,50,200);if(scalePercent==value)return;
        scalePercent=value;
        if(design==3){Size=RunicStripRenderer.SizeAt(EffectiveScalePercent);RefreshSnapshot();}
    }

    internal void FitToArea(Size available)
    {
        int limit=(int)Math.Min((long)available.Width*100/RunicStripRenderer.LogicalSize.Width,(long)available.Height*100/RunicStripRenderer.LogicalSize.Height);
        limit=Math.Clamp(limit,50,200);if(screenScaleLimit==limit)return;
        screenScaleLimit=limit;
        if(design==3){Size=RunicStripRenderer.SizeAt(EffectiveScalePercent);RefreshSnapshot();}
    }

    internal void RefreshSnapshot()
    {
        if(design==3 && IsHandleCreated && Visible)PresentRunicStrip();
        else Invalidate();
    }

    public LootTrackerOverlay(Func<LootTrackerSnapshot> snapshotProvider,Action<Point>? positionCommitted=null)
    {
        this.snapshotProvider=snapshotProvider??throw new ArgumentNullException(nameof(snapshotProvider));
        this.positionCommitted=positionCommitted;
        AutoScaleMode=AutoScaleMode.None;BackColor=Color.FromArgb(15,22,31);DoubleBuffered=true;
        FormBorderStyle=FormBorderStyle.None;Opacity=.94;ShowIcon=false;ShowInTaskbar=false;
        StartPosition=FormStartPosition.Manual;TopMost=false;Size=new Size(390,330);
        Cursor=Cursors.SizeAll;
        SetDesign(0);
        MouseDown+=(_,e)=>{if(e.Button!=MouseButtons.Left || e.Y>=(design==3?40*EffectiveScalePercent/100:HeaderHeight))return;dragging=true;dragOffset=e.Location;};
        MouseMove+=(_,e)=>{if(!dragging)return;Location=new Point(Left+e.X-dragOffset.X,Top+e.Y-dragOffset.Y);};
        MouseUp+=(_,e)=>{if(e.Button!=MouseButtons.Left)return;dragging=false;positionCommitted?.Invoke(Location);};
    }

    protected override bool ShowWithoutActivation=>true;

    protected override void SetVisibleCore(bool value)
    {
        if(!value){base.SetVisibleCore(false);return;}
        base.SetVisibleCore(true);
        if(IsHandleCreated)
        {
            if(design==3)PresentRunicStrip();
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
        if(design==3)
        {
            RunicStripRenderer.Draw(e.Graphics,snapshotProvider(),EffectiveScalePercent);
            return;
        }
        base.OnPaint(e);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);
        if(design!=0){DrawAlternate(e.Graphics,snapshotProvider());return;}
        e.Graphics.DrawRectangle(framePen,0,0,Math.Max(0,ClientSize.Width-1),Math.Max(0,ClientSize.Height-1));
        using var headerBrush=new SolidBrush(Color.FromArgb(42,52,43));
        e.Graphics.FillRectangle(headerBrush,1,1,ClientSize.Width-2,HeaderHeight-1);
        using var titleBrush=new SolidBrush(Color.FromArgb(255,238,160));
        e.Graphics.DrawString("DUNGEON HUD  ·  drag header to move",titleFont,titleBrush,new PointF(9,7));
        e.Graphics.DrawLine(framePen,0,HeaderHeight-1,ClientSize.Width,HeaderHeight-1);
        LootTrackerSnapshot snapshot=snapshotProvider();
        using var textBrush=new SolidBrush(Color.FromArgb(235,240,244));
        using var mutedBrush=new SolidBrush(Color.FromArgb(175,196,203,210));
        using var accentBrush=new SolidBrush(Color.FromArgb(255,215,120));
        float y=HeaderHeight+7;
        e.Graphics.DrawString($"Tracked loot  ·  Gold is amount  ·  Elapsed {FormatDuration(snapshot.Elapsed)}",detailFont,mutedBrush,new PointF(10,y));y+=16;
        e.Graphics.DrawString($"Active earning time {FormatDuration(snapshot.RateElapsed)}  ·  per hour",detailFont,mutedBrush,new PointF(10,y));y+=15;
        var hourly=snapshot.HourlyLoot.ToDictionary(item=>item.Name,StringComparer.OrdinalIgnoreCase);
        for(int i=0;i<snapshot.TrackedLoot.Count;i+=2)
        {
            var left=snapshot.TrackedLoot[i];
            double leftRate=hourly.GetValueOrDefault(left.Name)?.PerHour??0;
            e.Graphics.DrawString($"{left.Name}: {left.Count:N0} ({leftRate:N1}/h)",rowFont,textBrush,new PointF(10,y));
            if(i+1<snapshot.TrackedLoot.Count)
            {
                var right=snapshot.TrackedLoot[i+1];
                double rightRate=hourly.GetValueOrDefault(right.Name)?.PerHour??0;
                e.Graphics.DrawString($"{right.Name}: {right.Count:N0} ({rightRate:N1}/h)",rowFont,textBrush,new PointF(205,y));
            }
            y+=17;
        }
        y+=3;e.Graphics.DrawLine(Pens.DimGray,8,y,ClientSize.Width-8,y);y+=5;
        e.Graphics.DrawString("Tracked targets",detailFont,mutedBrush,new PointF(10,y));y+=16;
        foreach(var source in snapshot.Sources)
        {
            string line=$"{source.Source,-8} {source.Kills,3} kills  ·  {source.Drops,3} drops";
            e.Graphics.DrawString(line,rowFont,textBrush,new PointF(10,y));y+=18;
        }
        y+=3;e.Graphics.DrawLine(Pens.DimGray,8,y,ClientSize.Width-8,y);y+=5;
        e.Graphics.DrawString(snapshot.RecentDrops.Count==0?"Recent drops: waiting for a tracked kill":"Recent drops",detailFont,mutedBrush,new PointF(10,y));y+=16;
        if(snapshot.RecentDrops.Count>0)
        {
            foreach(var drop in snapshot.RecentDrops.Take(4))
            {
                string name=drop.Name.Length>25?drop.Name[..22]+"…":drop.Name;
                if(y>ClientSize.Height-30)break;
                e.Graphics.DrawString($"{drop.Source}: {name}",detailFont,accentBrush,new PointF(12,y));y+=15;
            }
        }
        e.Graphics.DrawString($"Zone {snapshot.Zone}  ·  pending kills {snapshot.PendingKills}",detailFont,mutedBrush,new PointF(10,ClientSize.Height-14));
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if(design!=3)base.OnPaintBackground(e);
    }

    void DrawAlternate(Graphics g,LootTrackerSnapshot snapshot)
    {
        bool parchment=design==2;
        Color ink=parchment?Color.FromArgb(62,46,29):Color.FromArgb(239,230,199);
        Color muted=parchment?Color.FromArgb(112,88,53):Color.FromArgb(177,179,170);
        Color accent=parchment?Color.FromArgb(114,66,26):Color.FromArgb(229,188,89);
        using var border=new Pen(accent);using var inkBrush=new SolidBrush(ink);using var mutedBrush=new SolidBrush(muted);
        using var accentBrush=new SolidBrush(accent);using var header=new SolidBrush(parchment?Color.FromArgb(199,176,131):Color.FromArgb(38,40,35));
        g.FillRectangle(header,0,0,Width,HeaderHeight);g.DrawRectangle(border,0,0,Width-1,Height-1);
        using var heading=new Font(parchment?"Georgia":"Segoe UI Semibold",parchment?11:9,FontStyle.Bold);
        g.DrawString(parchment?"Parchment Ledger":"COMPACT RIBBON",heading,inkBrush,new PointF(10,6));
        g.DrawString("drag header to move",detailFont,mutedBrush,new PointF(parchment?210:495,9));
        g.DrawString($"Session {FormatDuration(snapshot.Elapsed)}  ·  Active {FormatDuration(snapshot.RateElapsed)}  ·  Zone {snapshot.Zone}",detailFont,mutedBrush,new PointF(10,37));
        var rates=snapshot.HourlyLoot.ToDictionary(item=>item.Name,StringComparer.OrdinalIgnoreCase);
        if(!parchment)
        {
            for(int i=0;i<snapshot.TrackedLoot.Count;i++)
            {
                var item=snapshot.TrackedLoot[i];int x=10+i*104;
                g.DrawString(item.Name,rowFont,mutedBrush,new PointF(x,59));
                TextRenderer.DrawText(g,$"{item.Count:N0}",titleFont,new Rectangle(x,79,101,21),ink,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g,$"{rates.GetValueOrDefault(item.Name)?.PerHour??0:N1}/h",detailFont,new Rectangle(x,103,101,17),accent,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
            }
            g.DrawString($"{snapshot.Sources.Sum(source=>source.Kills):N0} kills  ·  {snapshot.Sources.Sum(source=>source.Drops):N0} drops  ·  Gold = amount",detailFont,mutedBrush,new PointF(10,127));
            return;
        }
        g.DrawString("Collected",detailFont,mutedBrush,new PointF(154,61));g.DrawString("Per hour",detailFont,mutedBrush,new PointF(270,61));
        float y=82;
        foreach(var item in snapshot.TrackedLoot)
        {
            g.DrawString(item.Name,rowFont,inkBrush,new PointF(12,y));
            TextRenderer.DrawText(g,$"{item.Count:N0}",rowFont,new Rectangle(118,(int)y,120,20),ink,TextFormatFlags.Right|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g,$"{rates.GetValueOrDefault(item.Name)?.PerHour??0:N1}",rowFont,new Rectangle(240,(int)y,104,20),accent,TextFormatFlags.Right|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);
            y+=24;
        }
        g.DrawLine(border,10,y,Width-10,y);y+=8;
        foreach(var source in snapshot.Sources)
        {g.DrawString($"{source.Source}  ·  {source.Kills:N0} kills  ·  {source.Drops:N0} drops",detailFont,inkBrush,new PointF(12,y));y+=19;}
        y+=5;
        foreach(var drop in snapshot.RecentDrops.Take(2))
        {TextRenderer.DrawText(g,$"{drop.Source}: {drop.Name}",detailFont,new Rectangle(12,(int)y,Width-24,17),accent,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPadding);y+=17;}
        g.DrawString($"Gold = amount  ·  Pending kills {snapshot.PendingKills}",detailFont,mutedBrush,new PointF(12,Height-18));
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
