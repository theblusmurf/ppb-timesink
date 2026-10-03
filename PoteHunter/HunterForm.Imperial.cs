namespace PoteHunter;

public sealed partial class HunterForm
{
    // Reparent the same navigation/actions/pages: bindings and operation locks remain intact.
    void ApplyImperialShell(TableLayoutPanel shell, TableLayoutPanel masthead, FlowLayoutPanel nav,
        TableLayoutPanel header, TabControl tabs, TableLayoutPanel footer)
    {
        shell.SuspendLayout();
        shell.Controls.Clear(); shell.ColumnStyles.Clear(); shell.RowStyles.Clear();
        shell.Padding=new Padding(0,0,14,10); shell.ColumnCount=2; shell.RowCount=4;
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,176));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute,96));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute,72));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute,36));

        var sidebar=new TableLayoutPanel {Name="imperialSidebar",Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,
            BackColor=UiSidebar,Margin=new Padding(0,0,16,0),Padding=new Padding(12,12,12,10)};
        sidebar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,112));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute,110));
        var brand=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        brand.RowStyles.Add(new RowStyle(SizeType.Absolute,70)); brand.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        brand.Controls.Add(new PictureBox {Name="imperialLogo",Image=ImperialTheme.Logo.Value,SizeMode=PictureBoxSizeMode.Zoom,
            Dock=DockStyle.Fill,Margin=new Padding(5,0,5,4),AccessibleName="Priston Tale logo"},0,0);
        brand.Controls.Add(new Label {Text="POTEHUNTER",Dock=DockStyle.Fill,Font=new Font("Georgia",11f),
            ForeColor=UiAccent,TextAlign=ContentAlignment.TopCenter,Margin=Padding.Empty},0,1);
        sidebar.Controls.Add(brand,0,0);
        nav.FlowDirection=FlowDirection.TopDown; nav.WrapContents=false; nav.AutoScroll=true;
        nav.BackColor=UiSidebar; nav.Padding=Padding.Empty;
        foreach(var button in nav.Controls.OfType<Button>())
        {
            button.Size=new Size(124,40);button.Margin=new Padding(0,0,0,6);
            button.TextAlign=ContentAlignment.MiddleLeft;button.Padding=new Padding(10,0,0,0);
        }
        sidebar.Controls.Add(nav,0,1);
        sidebar.Controls.Add(new Label {Text="IMPERIAL COMMAND\n\nLocal settings & profiles\nSaved target routes\nF8 start · F9 stop",
            Dock=DockStyle.Fill,ForeColor=UiMuted,Font=new Font("Segoe UI",8f),Margin=Padding.Empty},0,2);
        shell.Controls.Add(sidebar,0,0);shell.SetRowSpan(sidebar,4);

        var banner=new ImperialBanner {Name="imperialBanner",Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(4,8,12,8)};
        masthead.BackColor=Color.Transparent;
        foreach(var label in masthead.Controls.OfType<Label>())
            if(label.Name!="fieldConnectionStatus")label.BackColor=Color.Transparent;
        banner.Controls.Add(masthead);
        shell.Controls.Add(banner,1,0);shell.Controls.Add(header,1,1);
        shell.Controls.Add(tabs,1,2);shell.Controls.Add(footer,1,3);
        shell.ResumeLayout(true);
    }
}
