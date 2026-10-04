namespace PoteHunter;

public sealed partial class HunterForm
{
    // Reparent the same navigation/actions/pages: bindings and operation locks remain intact.
    void ApplyImperialShell(TableLayoutPanel shell, TableLayoutPanel masthead, FlowLayoutPanel nav,
        TableLayoutPanel header, TabControl tabs, TableLayoutPanel footer)
    {
        shell.SuspendLayout();
        shell.Controls.Clear(); shell.ColumnStyles.Clear(); shell.RowStyles.Clear();
        shell.Padding=new Padding(16,0,16,10);shell.ColumnCount=1;shell.RowCount=5;
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(int height in new[]{76,76})shell.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute,66));
        var banner=new ImperialBanner {Name="imperialBanner",Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(8)};
        masthead.BackColor=Color.Transparent;
        foreach(var label in masthead.Controls.OfType<Label>())
            if(label.Name!="fieldConnectionStatus")label.BackColor=Color.Transparent;
        banner.Controls.Add(masthead);
        nav.FlowDirection=FlowDirection.LeftToRight;nav.WrapContents=false;nav.AutoScroll=true;
        nav.BackColor=UiSurface;nav.Padding=new Padding(12,12,12,6);
        foreach(var button in nav.Controls.OfType<Button>())
        {button.Size=new Size(140,40);button.Margin=new Padding(0,0,8,0);button.TextAlign=ContentAlignment.MiddleCenter;}
        void CenterTabs()
        {
            int content=nav.Controls.Cast<Control>().Sum(c=>c.Width+c.Margin.Horizontal);
            nav.Padding=new Padding(Math.Max(12,(nav.ClientSize.Width-content)/2),12,12,6);
        }
        nav.SizeChanged+=(_,_)=>CenterTabs();CenterTabs();
        shell.Controls.Add(banner,0,0);shell.Controls.Add(header,0,1);
        shell.Controls.Add(tabs,0,2);shell.Controls.Add(footer,0,3);shell.Controls.Add(nav,0,4);
        shell.ResumeLayout(true);
    }
}
