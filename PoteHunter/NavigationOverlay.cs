using System.Runtime.InteropServices;

namespace PoteHunter;

/// <summary>A passive, click-through radar hosted over the game client.</summary>
internal sealed class NavigationOverlay : Form
{
    const int HeaderHeight = 28;
    const int FooterHeight = 18;
    const int WsExTransparent = 0x00000020;
    const int WsExToolWindow = 0x00000080;
    const int WsExLayered = 0x00080000;
    const int WsExNoActivate = 0x08000000;
    const int SwShownoactivate = 4;
    const int WmMouseactivate = 0x0021;
    const int MaNoactivate = 3;
    const int HWndTopmost = -1;
    const uint SwpNoactivate = 0x0010;
    const uint SwpNomove = 0x0002;
    const uint SwpNosize = 0x0001;

    readonly Action<Graphics, Size> drawNavigation;
    string title;
    readonly string legend;
    readonly Font titleFont = new("Georgia", 10f, FontStyle.Regular);
    readonly Pen framePen = new(ImperialTheme.Gold, 1f);

    public NavigationOverlay(Action<Graphics, Size> drawNavigation,string title="PlayPoteBot RADAR",string legend="Player diamonds · enemy: pink · status on label")
    {
        this.title=title;this.legend=legend;
        this.drawNavigation = drawNavigation ?? throw new ArgumentNullException(nameof(drawNavigation));
        AutoScaleMode = AutoScaleMode.None;
        BackColor = ImperialTheme.Window;
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        Opacity = .85;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        // Keep the managed property false: WinForms' Form.SetVisibleCore uses
        // TopMost as a reason to focus the active control during Show(). The
        // native HWND is promoted after visibility is established instead.
        TopMost = false;
    }

    protected override bool ShowWithoutActivation => true;

    internal void SetTitle(string value)
    {
        if(title==value)return;
        title=value;Invalidate();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!value)
        {
            base.SetVisibleCore(false);
            return;
        }

        base.SetVisibleCore(true);
        if (IsHandleCreated)
        {
            ShowWindow(Handle, SwShownoactivate);
            SetWindowPos(Handle, new IntPtr(HWndTopmost), 0, 0, 0, 0,
                SwpNomove | SwpNosize | SwpNoactivate);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmMouseactivate)
        {
            m.Result = (IntPtr)MaNoactivate;
            return;
        }
        base.WndProc(ref m);
    }

    internal long ExtendedWindowStyles => GetWindowLongPtr(Handle,-20).ToInt64();
    internal bool HasPassiveWindowStyles =>
        (ExtendedWindowStyles & (WsExLayered|WsExTransparent|WsExNoActivate|WsExToolWindow)) ==
        (WsExLayered|WsExTransparent|WsExNoActivate|WsExToolWindow);

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExLayered | WsExTransparent | WsExNoActivate | WsExToolWindow;
            return parameters;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.DrawRectangle(framePen, 0, 0, Math.Max(0, ClientSize.Width - 1), Math.Max(0, ClientSize.Height - 1));
        using var headerFill=new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(1,1,ClientSize.Width-2,HeaderHeight-2),ImperialTheme.Raised,ImperialTheme.Window,90f);
        e.Graphics.FillRectangle(headerFill,1,1,ClientSize.Width-2,HeaderHeight-2);
        using var titleBrush = new SolidBrush(ImperialTheme.Gold);
        e.Graphics.DrawString(title, titleFont, titleBrush, new PointF(10, 5));
        e.Graphics.DrawLine(framePen, 0, HeaderHeight - 1, ClientSize.Width, HeaderHeight - 1);

        var mapSize = new Size(ClientSize.Width, Math.Max(0, ClientSize.Height - HeaderHeight - FooterHeight));
        if (mapSize.Width <= 0 || mapSize.Height <= 0) return;
        var state = e.Graphics.Save();
        try
        {
            e.Graphics.SetClip(new Rectangle(0, HeaderHeight, mapSize.Width, mapSize.Height));
            e.Graphics.TranslateTransform(0, HeaderHeight);
            drawNavigation(e.Graphics, mapSize);
        }
        finally
        {
            e.Graphics.Restore(state);
        }
        using var footerFont = new Font("Segoe UI", 7f);
        using var footerBrush = new SolidBrush(ImperialTheme.Text);
        e.Graphics.DrawString(legend,
            footerFont, footerBrush, new PointF(7, ClientSize.Height - FooterHeight + 2));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            titleFont.Dispose();
            framePen.Dispose();
        }
        base.Dispose(disposing);
    }

    internal static bool TryGetClientScreenBounds(IntPtr window, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (window == IntPtr.Zero || !IsWindow(window) || IsIconic(window) || !GetClientRect(window, out var client)) return false;
        var origin = new NativePoint();
        if (!ClientToScreen(window, ref origin)) return false;
        int width = client.Right - client.Left, height = client.Bottom - client.Top;
        if (width <= 0 || height <= 0) return false;
        bounds = new Rectangle(origin.X, origin.Y, width, height);
        return true;
    }

    internal static IntPtr ForegroundWindow => GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct NativePoint { public int X, Y; }

    [DllImport("user32.dll")]
    static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr window, out NativeRect rectangle);

    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr window,int index);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
