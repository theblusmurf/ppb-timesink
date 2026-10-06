using System.ComponentModel;
namespace PoteHunter;

internal sealed class SidebarButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Selected { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool GroupHeader { get; init; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Expanded { get; set; } = true;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string Glyph { get; init; } = "Settings";
    public SidebarButton()
    {
        FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;
        Height=32;Margin=Padding.Empty;Cursor=Cursors.Hand;UseMnemonic=false;
        Font=new Font("Segoe UI",9f);BackColor=ImperialTheme.Surface;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
    }
    protected override AccessibleObject CreateAccessibilityInstance()=>new MenuAccessibility(this);
    sealed class MenuAccessibility(SidebarButton owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role=>AccessibleRole.PushButton;
        public override string DefaultAction=>owner.GroupHeader?owner.Expanded?"Collapse":"Expand":"Open";
        public override void DoDefaultAction()=>owner.PerformClick();
        public override AccessibleStates State=>base.State | (owner.GroupHeader?
            owner.Expanded?AccessibleStates.Expanded:AccessibleStates.Collapsed:owner.Selected?AccessibleStates.Selected:0);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.Clear(Selected?ImperialTheme.AccentDark:BackColor);
        Color color=Selected?ImperialTheme.Accent:GroupHeader?ImperialTheme.Muted:ImperialTheme.Text;
        if(Selected){using var bar=new SolidBrush(ImperialTheme.Accent);g.FillRectangle(bar,0,2,3,Height-4);}
        if(GroupHeader)TextRenderer.DrawText(g,Expanded?"⌄":"›",Font,new Rectangle(Width-20,0,18,Height),color,TextFormatFlags.VerticalCenter);
        else CrownfireControls.Glyph(g,Glyph,new RectangleF(13,(Height-14)/2f,14,14),color);
        TextRenderer.DrawText(g,Text,Font,new Rectangle(GroupHeader?10:35,0,Width-(GroupHeader?32:40),Height),color,
            TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
        if(Focused)ControlPaint.DrawFocusRectangle(g,new Rectangle(5,2,Width-10,Height-4),color,BackColor);
    }
}

internal sealed class SidebarGroup : TableLayoutPanel
{
    internal readonly SidebarButton Header;
    internal readonly FlowLayoutPanel Items;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Expanded {get=>Header.Expanded;set{Header.Expanded=value;Items.Visible=value;Header.Invalidate();PerformLayout();}}
    internal SidebarGroup(string key,string caption)
    {
        Name="menuGroup"+key;AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;
        ColumnCount=1;RowCount=2;Margin=new Padding(0,8,0,0);Padding=Padding.Empty;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));RowStyles.Add(new RowStyle(SizeType.AutoSize));RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Header=new SidebarButton{Name="menuToggle"+key,Text=caption.ToUpperInvariant(),GroupHeader=true,Dock=DockStyle.Top,
            Font=new Font("Segoe UI Semibold",8f),AccessibleName=caption+" menu group",AccessibleDescription="Expand or collapse this group. Current page stays open."};
        Items=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,
            Dock=DockStyle.Top,Margin=Padding.Empty,Padding=Padding.Empty};
        Controls.Add(Header,0,0);Controls.Add(Items,0,1);Header.Click+=(_,_)=>Expanded=!Expanded;
    }
    internal void Fit(int width){MinimumSize=new Size(width,0);MaximumSize=new Size(width,0);Width=width;foreach(Control entry in Items.Controls)entry.Width=width;}
}

public sealed partial class HunterForm
{
    Action BuildGroupedNavigation(FlowLayoutPanel nav,TabControl tabs,TabPage overview,TabPage farming,TabPage setup,
        TabPage? packs,TabPage[] features,TabPage help,TabPage overlays,Action<string> heading)
    {
        nav.FlowDirection=FlowDirection.TopDown;nav.WrapContents=false;nav.AutoScroll=true;
        var entries=new List<(SidebarButton Button,TabPage Page,SidebarGroup? Group)>();
        SidebarButton? selected=null;
        void Entry(string key,string text,TabPage page,SidebarGroup? group=null,Action? anchor=null,string glyph="Settings")
        {
            var b=new SidebarButton{Name="fieldNav"+key,Text=text,Tag=page,Glyph=glyph,AccessibleName=text};
            (group?.Items??nav).Controls.Add(b);entries.Add((b,page,group));
            b.Click+=(_,_)=>{selected=b;tabs.SelectedTab=page;anchor?.Invoke();Refresh();};
        }
        SidebarGroup Group(string key,string title){var group=new SidebarGroup(key,title);nav.Controls.Add(group);return group;}
        Entry("Overview","Overview",overview,glyph:"Overview");
        var hunting=Group("Hunting","Hunting");
        Entry("Farming","Farming",farming,hunting,glyph:"Hunt");Entry("Hunt","Hunt setup",setup,hunting,glyph:"Hunt");
        if(packs!=null)Entry("RangedPacks","Ranged packs",packs,hunting,glyph:"Hunt");
        var party=Group("Party","Party");Entry("Support","Support & buffs",supportPage,party,glyph:"Recovery");Entry("Group","Group",groupPage,party,glyph:"Hunt");
        var world=Group("World","World & tools");
        Entry("Routes","Navigation",navigationPage,world,glyph:"Routes");Entry("Targets","Targets",monstersPageFor(tabs),world,glyph:"Hunt");
        Entry("Loot","Ground loot",lootPage,world);Entry("Hotbar","Hotbar",hotbarPage,world);Entry("ItemGrades","Item grades",features[2],world);
        var safety=Group("Safety","Safety");Entry("Protection","Protection",protectionPage,safety,glyph:"Recovery");
        Entry("Recovery","Death recovery",setup,safety,()=>setup.ScrollControlIntoView(autoRevive),"Recovery");
        var preferences=Group("Settings","Settings");Entry("Settings","Options",features[0],preferences);Entry("Profiles","Profiles",features[1],preferences);
        Entry("Overlays","Overlays",overlays,preferences);Entry("Help","Help & logs",help,preferences);
        void Fit()
        {
            int width=Math.Max(140,nav.ClientSize.Width-SystemInformation.VerticalScrollBarWidth-2);
            foreach(Control child in nav.Controls)if(child is SidebarGroup group)group.Fit(width);else child.Width=width;
        }
        nav.SizeChanged+=(_,_)=>Fit();Fit();
        void Refresh()
        {
            if(tabs.SelectedTab is not TabPage page)return;
            if(selected==null || selected.Tag!=page){var item=entries.First(e=>e.Page==page);selected=item.Button;if(item.Group!=null)item.Group.Expanded=true;}
            foreach(var entry in entries){entry.Button.Selected=entry.Button==selected;entry.Button.Invalidate();
                if(entry.Group!=null){bool active=entries.Any(e=>e.Group==entry.Group&&e.Button==selected);entry.Group.Header.Selected=active;entry.Group.Header.Invalidate();}}
            heading(selected.Text);
        }
        return Refresh;
    }
}


