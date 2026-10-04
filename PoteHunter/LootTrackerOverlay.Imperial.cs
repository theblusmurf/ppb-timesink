using System.Drawing.Drawing2D;

namespace PoteHunter;

internal sealed partial class LootTrackerOverlay
{
    void DrawImperialHud(Graphics g,LootTrackerSnapshot snapshot)
    {
        using var gold=new SolidBrush(ImperialTheme.Gold);
        using var header=new LinearGradientBrush(new Rectangle(0,0,Width,HeaderHeight),ImperialTheme.Raised,ImperialTheme.Window,90);
        g.FillRectangle(header,0,0,Width,HeaderHeight);
        g.DrawRectangle(framePen,0,0,Width-1,Height-1);
        g.DrawLine(framePen,0,HeaderHeight,Width,HeaderHeight);
        g.DrawString("PlayPoteBot · LOOT TRACKER",titleFont,gold,new PointF(10,6));
        Text($"Session {FormatDuration(snapshot.Elapsed)}  ·  Active {FormatDuration(snapshot.RateElapsed)}",detailFont,ImperialTheme.Muted,new(10,37,Width-20,16));
        Text("Gold: net wallet · other totals are detected drops",detailFont,ImperialTheme.Muted,new(10,53,Width-20,16));
        string[] names=["Gold","Silvin","Mithril","Iternium","Fehu","Gems"];
        for(int i=0;i<names.Length;i++)
        {
            string name=names[i];int x=10+i%3*125,y=76+i/3*83;
            using var shape=CrownfireControls.Frame(new RectangleF(x,y,119,77));
            using var fill=new SolidBrush(ImperialTheme.Raised);
            using var edge=new Pen(ImperialTheme.Border);
            g.FillPath(fill,shape);g.DrawPath(edge,shape);
            if(name=="Gold"){g.FillEllipse(gold,x+9,y+8,20,20);g.DrawEllipse(framePen,x+6,y+11,20,20);}
            else GameLootIcons.Draw(g,name,new RectangleF(x+7,y+6,30,28));
            Text(LootTrackerSnapshot.DisplayName(name),rowFont,ImperialTheme.Text,new(x+41,y+5,74,25));
            Text(snapshot.AmountText(name),rowFont,ImperialTheme.Gold,new(x+7,y+34,105,22));
            Text(snapshot.RateText(name,"N1")+" /h",detailFont,ImperialTheme.Muted,new(x+7,y+57,105,16));
        }
        int row=245;
        foreach(var source in snapshot.Sources.Take(4))
        {
            Text($"{source.Source} · {source.Kills:N0} kills · {source.Drops:N0} drops",detailFont,ImperialTheme.Text,new(10,row,Width-20,14));row+=14;
        }
        Text($"Zone {snapshot.Zone} · Pending kills {snapshot.PendingKills} · drag header to move",detailFont,ImperialTheme.Muted,new(10,Height-19,Width-20,16));

        void Text(string value,Font font,Color color,Rectangle bounds) =>
            TextRenderer.DrawText(g,value,font,bounds,color,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|
                TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding|TextFormatFlags.Left);
    }
}
