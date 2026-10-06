namespace PoteHunter;

public sealed partial class HunterForm
{
    void ApplyImperialShell(TableLayoutPanel shell,TableLayoutPanel masthead,FlowLayoutPanel nav,
        TableLayoutPanel header,TabControl tabs,TableLayoutPanel footer)
    {
        shell.SuspendLayout();shell.Controls.Clear();shell.ColumnStyles.Clear();shell.RowStyles.Clear();
        shell.Padding=new Padding(12,0,12,8);shell.ColumnCount=1;shell.RowCount=4;
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute,68));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute,48));shell.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        var banner=new ImperialBanner{Name="imperialBanner",Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(0,5,0,5)};
        masthead.BackColor=Color.Transparent;
        foreach(var label in masthead.Controls.OfType<Label>())if(label.Name!="fieldConnectionStatus")label.BackColor=Color.Transparent;
        banner.Controls.Add(masthead);
        var workspace=new TableLayoutPanel{Name="orbitalWorkspace",Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0,8,0,8),BackColor=UiWindow};
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,208));workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));workspace.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var rail=new TableLayoutPanel{Name="orbitalOperationRail",Dock=DockStyle.Fill,ColumnCount=1,RowCount=1,BackColor=UiSurface,Padding=new Padding(4,6,0,6),Margin=new Padding(0,0,12,0)};
        rail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));rail.RowStyles.Add(new RowStyle(SizeType.Percent,100));rail.Controls.Add(nav,0,0);
        nav.BackColor=UiSurface;nav.Margin=Padding.Empty;nav.Padding=Padding.Empty;
        var previousActions=connect.Parent!;
        var actions=new TableLayoutPanel{Name="orbitalHuntActions",Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty,Padding=new Padding(208,4,0,4)};
        foreach(int _ in Enumerable.Range(0,3))actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));actions.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        start.Text="Start  F8";stop.Text="Stop  F9";connect.Text="Connect / refresh";
        int index=0;foreach(var button in new[]{start,stop,connect}){button.Dock=DockStyle.Fill;button.Margin=new Padding(3,0,3,0);actions.Controls.Add(button,index++,0);}
        header.Controls.Remove(previousActions);previousActions.Dispose();
        var pickers=header.GetControlFromPosition(1,0);header.Controls.Remove(pickers);pickers?.Dispose();header.ColumnCount=1;header.ColumnStyles.Clear();header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        header.Margin=new Padding(12,0,0,4);
        var content=new TableLayoutPanel{Name="orbitalPageContent",Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty,BackColor=UiWindow};
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));content.RowStyles.Add(new RowStyle(SizeType.Absolute,68));content.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        content.Controls.Add(header,0,0);content.Controls.Add(tabs,0,1);
        var farming=tabs.TabPages.Cast<TabPage>().Single(p=>p.Name=="farmingPage");
        var scroll=new Panel{Name="orbitalRailScroll",Dock=DockStyle.Fill,AutoScroll=true,BackColor=UiSurface,Padding=new Padding(12,8,12,8)};
        if(orbitalRailBody==null)throw new InvalidOperationException("Farming controls are unavailable.");
        scroll.Controls.Add(orbitalRailBody);ThemeTree(orbitalRailBody);farming.Controls.Add(scroll);
        void FitFarming(){int width=Math.Max(1,scroll.ClientSize.Width-26-SystemInformation.VerticalScrollBarWidth);orbitalRailBody.MinimumSize=new Size(width,0);orbitalRailBody.MaximumSize=new Size(width,0);orbitalRailBody.Width=width;}
        scroll.SizeChanged+=(_,_)=>FitFarming();
        bool? stacked=null;
        void FitSetup()
        {
            if(content.Width<=0)return;bool stack=content.Width<860*DeviceDpi/96;if(stacked==stack)return;stacked=stack;
            var combat=settings.Controls.Find("fieldCombatColumn",false).Single();var recovery=settings.Controls.Find("fieldRecoveryColumn",false).Single();
            settings.SuspendLayout();settings.Controls.Clear();settings.ColumnStyles.Clear();settings.RowStyles.Clear();settings.ColumnCount=stack?1:2;settings.RowCount=stack?2:1;
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,stack?100:50));if(!stack)settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
            settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));if(stack)settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            combat.Margin=stack?Padding.Empty:new Padding(0,0,5,0);recovery.Margin=stack?Padding.Empty:new Padding(5,0,0,0);
            settings.Controls.Add(combat,0,0);settings.Controls.Add(recovery,stack?0:1,stack?1:0);settings.ResumeLayout(true);
        }
        content.SizeChanged+=(_,_)=>FitSetup();workspace.Controls.Add(rail,0,0);workspace.Controls.Add(content,1,0);
        shell.Controls.Add(banner,0,0);shell.Controls.Add(workspace,0,1);shell.Controls.Add(actions,0,2);shell.Controls.Add(footer,0,3);
        FitFarming();shell.ResumeLayout(true);FitSetup();
    }
}
