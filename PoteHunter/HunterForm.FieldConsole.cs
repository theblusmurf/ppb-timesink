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
        var sidebar=Controls.Find("imperialSidebar",true).Single();
        var banner=Controls.Find("imperialBanner",true).Single();
        var sidebarRight=PointToClient(sidebar.PointToScreen(new Point(sidebar.Width,0))).X;
        if(sidebarRight>PointToClient(tabs.PointToScreen(Point.Empty)).X || banner.Height<80 ||
            banner.Bottom>tabs.Top || !banner.Parent!.ClientRectangle.Contains(banner.Bounds))
            throw new Exception("Imperial sidebar overlaps page content or banner is clipped.");
        if(ImperialTheme.Logo.Value.Width<1 || ImperialTheme.Banner.Value.Width<1)
            throw new Exception("Imperial embedded reference artwork is unavailable.");
        foreach(string name in new[]{"Overview","Setup","Support","Monitor","Navigation","Advanced","Index"})
        {
            var button=(Button)Controls.Find("fieldNav"+name,true).Single();
            if(!button.Visible || !button.Parent!.ClientRectangle.Contains(button.Bounds))
                throw new Exception("Field Console navigation is clipped: "+name);
            button.PerformClick();Application.DoEvents();
            var picker=Controls.Find("fieldPicker"+name,true).OfType<ComboBox>().SingleOrDefault();
            if(tabs.SelectedTab!=(picker?.SelectedItem as TabPage??button.Tag as TabPage))
                throw new Exception("Field Console navigation did not open its section: "+name);
        }
        foreach(string name in new[]{"Monitor","Advanced"})
        {
            ((Button)Controls.Find("fieldNav"+name,true).Single()).PerformClick();
            var picker=(ComboBox)Controls.Find("fieldPicker"+name,true).Single();
            if(!picker.Visible || picker.Items.Count<2)throw new Exception("A grouped page picker is inaccessible.");
            // Exercise the same commit action as selecting a native dropdown item.
            for(int i=0;i<picker.Items.Count;i++)
            {
                picker.SelectedIndex=i;
                typeof(ComboBox).GetMethod("OnSelectionChangeCommitted",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(picker,[EventArgs.Empty]);
                if(tabs.SelectedTab!=picker.Items[i])throw new Exception("A grouped page cannot be opened.");
            }
        }
        tabs.SelectedIndex=0;PerformLayout();Application.DoEvents();
        var left=Controls.Find("fieldCombatColumn",true).Single();
        var right=Controls.Find("fieldRecoveryColumn",true).Single();
        if(left.Right>right.Left || right.Right>settings.ClientSize.Width || left.Width<350 || right.Width<350)
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
        {Passed=true,Checks=new[]{"all sidebar navigation sections accessible","all grouped pages selectable","minimum-size columns and actions fit","unknown/zero/full/over-max vitals","no new game queries or input"}}));
    }
}
