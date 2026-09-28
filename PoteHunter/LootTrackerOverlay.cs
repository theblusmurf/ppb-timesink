using System.Drawing.Drawing2D;

namespace PoteHunter;

/// <summary>A small draggable, non-activating overlay for tracked farm loot.</summary>
internal sealed class LootTrackerOverlay : Form
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

    public LootTrackerOverlay(Func<LootTrackerSnapshot> snapshotProvider,Action<Point>? positionCommitted=null)
    {
        this.snapshotProvider=snapshotProvider??throw new ArgumentNullException(nameof(snapshotProvider));
        this.positionCommitted=positionCommitted;
        AutoScaleMode=AutoScaleMode.None;BackColor=Color.FromArgb(15,22,31);DoubleBuffered=true;
        FormBorderStyle=FormBorderStyle.None;Opacity=.94;ShowIcon=false;ShowInTaskbar=false;
        StartPosition=FormStartPosition.Manual;TopMost=false;Size=new Size(330,300);
        Cursor=Cursors.SizeAll;
        MouseDown+=(_,e)=>{if(e.Button!=MouseButtons.Left)return;dragging=true;dragOffset=e.Location;};
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
        base.OnPaint(e);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);
        e.Graphics.DrawRectangle(framePen,0,0,Math.Max(0,ClientSize.Width-1),Math.Max(0,ClientSize.Height-1));
        using var headerBrush=new SolidBrush(Color.FromArgb(42,52,43));
        e.Graphics.FillRectangle(headerBrush,1,1,ClientSize.Width-2,HeaderHeight-1);
        using var titleBrush=new SolidBrush(Color.FromArgb(255,238,160));
        e.Graphics.DrawString("LOOT TRACKER  ·  drag header to move",titleFont,titleBrush,new PointF(9,7));
        e.Graphics.DrawLine(framePen,0,HeaderHeight-1,ClientSize.Width,HeaderHeight-1);
        LootTrackerSnapshot snapshot=snapshotProvider();
        using var textBrush=new SolidBrush(Color.FromArgb(235,240,244));
        using var mutedBrush=new SolidBrush(Color.FromArgb(175,196,203,210));
        using var accentBrush=new SolidBrush(Color.FromArgb(255,215,120));
        float y=HeaderHeight+7;
        e.Graphics.DrawString("Tracked loot",detailFont,mutedBrush,new PointF(10,y));y+=16;
        for(int i=0;i<snapshot.TrackedLoot.Count;i+=2)
        {
            var left=snapshot.TrackedLoot[i];
            e.Graphics.DrawString($"{left.Name}: {left.Count}",rowFont,textBrush,new PointF(10,y));
            if(i+1<snapshot.TrackedLoot.Count)
            {
                var right=snapshot.TrackedLoot[i+1];
                e.Graphics.DrawString($"{right.Name}: {right.Count}",rowFont,textBrush,new PointF(170,y));
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
