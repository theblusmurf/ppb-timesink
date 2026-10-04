using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PoteHunter;

public sealed partial class HunterForm
{
    static TableLayoutPanel CompactCard(string title)
    {
        var card = CompactTable();
        card.BackColor = UiSurface;
        card.Padding = new Padding(14, 12, 14, 10);
        card.Margin = new Padding(0, 0, 0, 8);
        CompactAdd(card, new Label
        {
            Name = "ironboundCardHeading", Text = "◇  " + title, AutoSize = true, ForeColor = UiAccent, UseMnemonic = false,
            Font = new Font("Georgia", 12f, FontStyle.Regular),
            Margin = new Padding(0, 0, 0, 8)
        });
        card.Paint += (_, e) =>
        {
            e.Graphics.Clear(UiWindow);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var outline = RoundedPath(card.Width - 1, card.Height - 1, UiCornerRadius);
            using var fill = new SolidBrush(UiSurface);
            using var border = new Pen(UiBorder,1.3f);
            e.Graphics.FillPath(fill, outline);
            e.Graphics.DrawPath(border, outline);
            using var inner=new Pen(Color.FromArgb(65,UiAccent));
            using var inset=RoundedPath(card.Width-7,card.Height-7,UiCornerRadius);
            var state=e.Graphics.Save();e.Graphics.TranslateTransform(3,3);e.Graphics.DrawPath(inner,inset);e.Graphics.Restore(state);
            using var trim=new Pen(UiAccent,2);
            e.Graphics.DrawLine(trim,14,2,Math.Min(card.Width-14,66),2);
            DrawIronboundCorners(e.Graphics, card.ClientRectangle);
            FantasyFrame.Draw(e.Graphics,card.ClientRectangle);
        };
        card.SizeChanged += (_, _) =>
        {
            foreach(var label in card.Controls.OfType<Label>())
                label.MaximumSize = new Size(Math.Max(1,card.ClientSize.Width-card.Padding.Horizontal-label.Margin.Horizontal),0);
        };
        return card;
    }

    static GraphicsPath RoundedPath(int width, int height, int radius)
    {
        int diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(width, height)));
        var path = new GraphicsPath();
        path.AddArc(0, 0, diameter, diameter, 180, 90);
        path.AddArc(width - diameter, 0, diameter, diameter, 270, 90);
        path.AddArc(width - diameter, height - diameter, diameter, diameter, 0, 90);
        path.AddArc(0, height - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    static void RoundControl(Control control, int radius)
    {
        // Reuse the native controls, including keyboard focus and accessibility.
        // Rebuild the clipping region after layout and dispose its previous GDI handle.
        void UpdateRegion()
        {
            if (control.Width < 2 || control.Height < 2) return;
            int diameter = Math.Min(radius * 2, Math.Min(control.Width, control.Height));
            using var path = new GraphicsPath();
            path.AddArc(0, 0, diameter, diameter, 180, 90);
            path.AddArc(control.Width - diameter, 0, diameter, diameter, 270, 90);
            path.AddArc(control.Width - diameter, control.Height - diameter, diameter, diameter, 0, 90);
            path.AddArc(0, control.Height - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            var previous = control.Region;
            control.Region = new Region(path);
            previous?.Dispose();
        }
        control.SizeChanged += (_, _) => UpdateRegion();
        UpdateRegion();
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    void ApplyDarkTitleBar()
    {
        // Older Windows versions can decline these optional appearance attributes.
        int enabled = 1;
        _ = DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
        int color = ColorTranslator.ToWin32(UiSidebar);
        _ = DwmSetWindowAttribute(Handle, 35, ref color, sizeof(int));
        int text = ColorTranslator.ToWin32(UiText);
        _ = DwmSetWindowAttribute(Handle, 36, ref text, sizeof(int));
    }
}
