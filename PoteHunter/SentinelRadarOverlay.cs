using System.Runtime.InteropServices;

namespace PoteHunter;

/// <summary>Passive click-through body, with a separate non-activating drag header.</summary>
internal sealed class SentinelRadarOverlay : Form
{
    const int WsExTransparent=0x20,WsExToolWindow=0x80,WsExLayered=0x80000,WsExNoActivate=0x08000000;
    const int WmMouseActivate=0x21,MaNoActivate=3,SwShowNoActivate=4,HwndTopmost=-1;
    const uint SwpNoActivate=0x10,SwpNoMove=2,SwpNoSize=1;
    readonly Func<SentinelRadarSnapshot> snapshotProvider;
    readonly Action<Point>? positionCommitted;
    readonly DragHeader header;
    Rectangle availableArea;

    internal SentinelRadarOverlay(Func<SentinelRadarSnapshot> snapshotProvider,Action<Point>? positionCommitted=null)
    {
        this.snapshotProvider=snapshotProvider??throw new ArgumentNullException(nameof(snapshotProvider));
        this.positionCommitted=positionCommitted;
        AutoScaleMode=AutoScaleMode.None;BackColor=ImperialTheme.Window;DoubleBuffered=true;
        FormBorderStyle=FormBorderStyle.None;Opacity=.94;ShowIcon=false;ShowInTaskbar=false;
        StartPosition=FormStartPosition.Manual;TopMost=false;ClientSize=SentinelRadarRenderer.LogicalSize;
        header=new DragHeader(this);LocationChanged+=(_,_)=>SyncHeader();SizeChanged+=(_,_)=>SyncHeader();
    }

    internal void RefreshSnapshot(){Invalidate();if(header.Visible)header.Invalidate();}
    internal void FitToArea(Rectangle bounds)
    {
        if(bounds.Width<=0||bounds.Height<=0)return;
        availableArea=bounds;Size=SentinelRadarPresentation.FitSize(bounds.Size);
        Location=SentinelRadarPresentation.ClampPosition(Location,Size,bounds);SyncHeader();
    }

    internal void MoveTo(Point position,bool commit=false)
    {
        Rectangle bounds=availableArea.Width>0&&availableArea.Height>0?availableArea:Screen.FromPoint(position).WorkingArea;
        Location=SentinelRadarPresentation.ClampPosition(position,Size,bounds);
        if(commit)positionCommitted?.Invoke(Location);
    }

    internal bool HasPassiveWindowStyles=>StyleContains(Handle,WsExLayered|WsExTransparent|WsExToolWindow|WsExNoActivate);
    internal bool HeaderHasPassiveWindowStyles=>StyleContains(header.Handle,WsExLayered|WsExToolWindow|WsExNoActivate)&&
        (GetWindowLongPtr(header.Handle,-20).ToInt64()&WsExTransparent)==0;
    internal bool HeaderVisible=>header.Visible;
    internal Rectangle HeaderBounds=>header.Bounds;
    protected override bool ShowWithoutActivation=>true;
    protected override CreateParams CreateParams
    {
        get{var parameters=base.CreateParams;parameters.ExStyle|=WsExLayered|WsExTransparent|WsExToolWindow|WsExNoActivate;return parameters;}
    }

    protected override void SetVisibleCore(bool value)
    {
        if(!value){if(header is not null&&!header.IsDisposed)header.Hide();base.SetVisibleCore(false);return;}
        base.SetVisibleCore(true);PromoteWithoutActivation(this);SyncHeader();
        if(!header.Visible)header.Show(this);
        PromoteWithoutActivation(header);
    }

    protected override void WndProc(ref Message message)
    {
        if(message.Msg==WmMouseActivate){message.Result=(IntPtr)MaNoActivate;return;}
        base.WndProc(ref message);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);SentinelRadarRenderer.Draw(e.Graphics,ClientSize,snapshotProvider());
    }

    void SyncHeader()
    {
        if(header.IsDisposed)return;
        int height=Math.Max(1,(int)Math.Round(SentinelRadarRenderer.HeaderHeight*ClientSize.Height/250d));
        header.Bounds=new Rectangle(Left+1,Top+1,Math.Max(1,ClientSize.Width-2),Math.Max(1,height-1));
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)header.Dispose();base.Dispose(disposing);
    }

    static void PromoteWithoutActivation(Form form)
    {
        if(!form.IsHandleCreated)return;
        ShowWindow(form.Handle,SwShowNoActivate);
        SetWindowPos(form.Handle,new IntPtr(HwndTopmost),0,0,0,0,SwpNoMove|SwpNoSize|SwpNoActivate);
    }
    static bool StyleContains(IntPtr handle,long flags)=>(GetWindowLongPtr(handle,-20).ToInt64()&flags)==flags;

    // HTTRANSPARENT alone forwards hit-testing only between same-thread windows.
    // A separate header lets the body use WS_EX_TRANSPARENT for the game process.
    sealed class DragHeader : Form
    {
        readonly SentinelRadarOverlay radar;
        bool dragging;
        Point originPointer,originWindow;
        internal DragHeader(SentinelRadarOverlay radar)
        {
            this.radar=radar;AutoScaleMode=AutoScaleMode.None;BackColor=ImperialTheme.Window;DoubleBuffered=true;
            FormBorderStyle=FormBorderStyle.None;Opacity=.94;ShowIcon=false;ShowInTaskbar=false;
            StartPosition=FormStartPosition.Manual;TopMost=false;Cursor=Cursors.SizeAll;
        }
        protected override bool ShowWithoutActivation=>true;
        protected override CreateParams CreateParams
        {
            get{var parameters=base.CreateParams;parameters.ExStyle|=WsExLayered|WsExToolWindow|WsExNoActivate;return parameters;}
        }
        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(value);if(value)PromoteWithoutActivation(this);
        }
        protected override void WndProc(ref Message message)
        {
            if(message.Msg==WmMouseActivate){message.Result=(IntPtr)MaNoActivate;return;}base.WndProc(ref message);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var state=e.Graphics.Save();
            try
            {
                e.Graphics.ScaleTransform(radar.ClientSize.Width/460f,radar.ClientSize.Height/250f);
                e.Graphics.TranslateTransform(-1,-1);
                SentinelRadarRenderer.DrawHeader(e.Graphics);
            }
            finally {e.Graphics.Restore(state);}
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;
            dragging=true;originPointer=PointToScreen(e.Location);originWindow=radar.Location;Capture=true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);if(!dragging)return;
            Point current=PointToScreen(e.Location);
            radar.MoveTo(new Point(originWindow.X+current.X-originPointer.X,originWindow.Y+current.Y-originPointer.Y));
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);if(e.Button!=MouseButtons.Left||!dragging)return;
            dragging=false;Capture=false;radar.MoveTo(radar.Location,commit:true);
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);if(!Capture)dragging=false;
        }
    }

    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr window,IntPtr insertAfter,int x,int y,int width,int height,uint flags);
}
