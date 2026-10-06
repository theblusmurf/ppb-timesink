using System.Globalization;
using System.Text.RegularExpressions;

namespace PoteHunter;

/// <summary>Formats the existing HP/MP reading without extra game access.</summary>
internal sealed class FieldVitalMeter : Control
{
    readonly string resource;
    readonly Color fill;
    double fraction;
    public string ReadingText { get; private set; }

    public FieldVitalMeter(string resource, Color fill)
    {
        this.resource=resource;this.fill=fill;
        ReadingText=$"{resource} unknown";
        Dock=DockStyle.Fill;Margin=new Padding(7,3,7,3);
        Font=new Font("Consolas",8.5f);ForeColor=ImperialTheme.Text;
        AccessibleName=$"{resource} reading";AccessibleRole=AccessibleRole.StaticText;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
    }

    internal static (string Text,double Fraction) Read(string resource,string text)
    {
        var match=Regex.Match(text,@"(?:^|\s)"+Regex.Escape(resource)+@" (\d+)/(\d+)(?:\s|$)");
        if(!match.Success || !long.TryParse(match.Groups[1].Value,NumberStyles.None,CultureInfo.InvariantCulture,out var current) ||
            !long.TryParse(match.Groups[2].Value,NumberStyles.None,CultureInfo.InvariantCulture,out var maximum) || maximum<=0)
            return ($"{resource} unknown",0);
        return ($"{resource} {current}/{maximum}",Math.Clamp((double)current/maximum,0,1));
    }

    public void SetReading(string text)
    {
        var reading=Read(resource,text);
        if(ReadingText==reading.Text && fraction==reading.Fraction)return;
        ReadingText=reading.Text;fraction=reading.Fraction;AccessibleDescription=ReadingText;Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var track=new Rectangle(0,Math.Max(0,Height-4),Width,3);
        using var background=new SolidBrush(ImperialTheme.Raised);
        using var bar=new SolidBrush(fill);
        e.Graphics.FillRectangle(background,track);
        e.Graphics.FillRectangle(bar,new Rectangle(track.X,track.Y,(int)(track.Width*fraction),track.Height));
        TextRenderer.DrawText(e.Graphics,ReadingText,Font,new Rectangle(0,0,Width,Math.Max(0,Height-4)),ForeColor,
            TextFormatFlags.VerticalCenter|TextFormatFlags.Left|TextFormatFlags.EndEllipsis);
    }
}

public sealed partial class HunterForm
{
    void CheckFieldConsoleUi(TabControl tabs)
    {
        var banner=Controls.Find("imperialBanner",true).Single();
        var nav=Controls.Find("fieldNavigation",true).Single();
        var rail=Controls.Find("orbitalOperationRail",true).Single();
        if(banner.Height<60||nav.Parent!=rail||!rail.ClientRectangle.Contains(nav.Bounds)||
            PointToClient(nav.PointToScreen(new Point(nav.Width,0))).X>PointToClient(tabs.PointToScreen(Point.Empty)).X||
            !banner.Parent!.ClientRectangle.Contains(banner.Bounds))
            throw new Exception("Left navigation overlaps content or banner is clipped.");
        if(ImperialTheme.Logo.Value.Width<1)
            throw new Exception("Embedded application branding is unavailable.");
        var menu=(FlowLayoutPanel)nav;
        var menuWindow=Size;
        foreach(var menuSize in new[]{Size,MinimumSize})
        {
        Size=menuSize;PerformLayout();Application.DoEvents();
        foreach(var group in menu.Controls.OfType<SidebarGroup>())
        {
            menu.ScrollControlIntoView(group);Application.DoEvents();
            var page=tabs.SelectedTab;bool expanded=group.Expanded;
            group.Header.AccessibilityObject.DoDefaultAction();Application.DoEvents();
            if(group.Expanded==expanded || group.Items.Visible==expanded || tabs.SelectedTab!=page ||
                (group.Header.AccessibilityObject.State&AccessibleStates.Collapsed)==0)
                throw new Exception("Collapsing a menu group changed the page or lost accessibility.");
            if(group.Header.Width<140 || group.Width!=menu.Controls.OfType<SidebarButton>().Single().Width)
                throw new Exception("Collapsed menu heading shrank or clips its label: "+group.Name);
            group.Header.PerformClick();Application.DoEvents();
            if(!group.Expanded || (group.Header.AccessibilityObject.State&AccessibleStates.Expanded)==0)
                throw new Exception("Menu group cannot be reopened.");
        }
        foreach(var button in menu.Controls.OfType<SidebarButton>().Concat(menu.Controls.OfType<SidebarGroup>().SelectMany(g=>g.Items.Controls.OfType<SidebarButton>())))
        {
            menu.ScrollControlIntoView(button);Application.DoEvents();
            var rect=menu.RectangleToClient(button.RectangleToScreen(button.ClientRectangle));
            if(!button.Visible || !menu.ClientRectangle.Contains(rect) || button.Width<140)
                throw new Exception("Grouped navigation is unreachable: "+button.Text+" "+rect+" "+menu.ClientRectangle);
            button.PerformClick();Application.DoEvents();
            if(tabs.SelectedTab!=button.Tag || !button.Selected)
                throw new Exception("Direct navigation did not select its feature: "+button.Text);
        }
        menu.AutoScrollPosition=Point.Empty;
        }
        var retained=tabs.SelectedTab;
        foreach(var group in menu.Controls.OfType<SidebarGroup>())group.Expanded=false;
        PerformLayout();Application.DoEvents();
        if(menu.Controls.OfType<SidebarGroup>().Any(g=>g.Header.Width<140))throw new Exception("Collapsed group headings are clipped.");
        if(tabs.SelectedTab!=retained)throw new Exception("Collapsing all menu groups changed the selected page.");
        using(var collapsed=new Bitmap(Width,Height)){DrawToBitmap(collapsed,new Rectangle(Point.Empty,Size));collapsed.Save(Path.Combine(AppContext.BaseDirectory,"grouped-menu-collapsed-minimum.png"));}
        foreach(var group in menu.Controls.OfType<SidebarGroup>())group.Expanded=true;
        Size=menuWindow;PerformLayout();Application.DoEvents();
        tabs.SelectedIndex=0;PerformLayout();Application.DoEvents();
        var left=Controls.Find("fieldCombatColumn",true).Single();
        var right=Controls.Find("fieldRecoveryColumn",true).Single();
        bool stack=settings.ColumnCount==1;
        if((stack ? left.Bottom>right.Top : left.Right>right.Left) || right.Right>settings.ClientSize.Width || left.Width<350 || right.Width<350)
            throw new Exception("Field Console columns overlap or overflow.");
        foreach(var action in new[]{connect,start,stop})
            if(!action.Visible || !action.Parent!.ClientRectangle.Contains(action.Bounds))
                throw new Exception("A header action is clipped.");
        foreach(var (input,expected) in new[]{("HP 0/100 · MP 25/50",0d),("HP 100/100 · MP 25/50",1d),
            ("HP unknown · MP 25/50",0d),("HP 50/0 · MP unknown",0d),("HP 500/100 · MP unknown",1d)})
            if(FieldVitalMeter.Read("HP",input).Fraction!=expected)throw new Exception("Invalid vital bar reading.");
        if(FieldVitalMeter.Read("MP","HP unknown · MP 25/50")!=("MP 25/50",.5d) ||
            FieldVitalMeter.Read("HP","").Text!="HP unknown")throw new Exception("Unknown/disconnected vitals were shown as known.");
        CheckFieldOverview(tabs);
        CheckOrbitalUi(tabs);
        int originalMode=compactMode.SelectedIndex;
        compactMode.SelectedIndex=3;PerformLayout();Application.DoEvents();
        if(!tankPicker.Visible || !healingSkillKeys.Visible || autoRevive.Visible)
            throw new Exception("Group healer did not adapt the Field Console cards.");
        using(var preview=new Bitmap(Width,Height))
        {
            DrawToBitmap(preview,new Rectangle(Point.Empty,Size));
            preview.Save(Path.Combine(AppContext.BaseDirectory,"field-console-healer.png"));
        }
        compactMode.SelectedIndex=originalMode;PerformLayout();Application.DoEvents();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"field-console-ui-checks.json"),System.Text.Json.JsonSerializer.Serialize(new
        {Passed=true,Checks=new[]{"all left navigation sections accessible","all grouped pages selectable","minimum-size columns and actions fit","unknown/zero/full/over-max vitals","no new game queries or input"}}));
    }
}
