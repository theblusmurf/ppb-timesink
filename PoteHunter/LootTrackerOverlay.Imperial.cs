using System.Drawing.Drawing2D;

namespace PoteHunter;

internal sealed partial class LootTrackerOverlay
{
    void DrawImperialHud(Graphics g,LootTrackerSnapshot snapshot)
    {
        using var gold=new SolidBrush(ImperialTheme.Gold);
        g.DrawImage(ImperialTheme.Banner.Value,new Rectangle(0,0,Width,76),new Rectangle(0,0,ImperialTheme.Banner.Value.Width,ImperialTheme.Banner.Value.Height/3),GraphicsUnit.Pixel);
        using var shade=new SolidBrush(Color.FromArgb(100,ImperialTheme.Window));g.FillRectangle(shade,0,0,Width,76);
        g.DrawRectangle(framePen,0,0,Width-1,Height-1);
        g.DrawLine(framePen,0,HeaderHeight,Width,HeaderHeight);
        g.DrawImage(ImperialTheme.Logo.Value,new Rectangle(12,4,22,24));
        g.DrawString("PlayPoteBot · Loot Ledger",titleFont,gold,new PointF(41,6));
        Text($"Session {FormatDuration(snapshot.Elapsed)} · Active {FormatDuration(snapshot.RateElapsed)}",detailFont,ImperialTheme.Muted,new(14,37,Width-28,16));
        Text("Gold: net wallet · items: detected drops",detailFont,ImperialTheme.Muted,new(14,54,Width-28,16));
        string[] names=["Gold","Silvin","Mithril","Iternium","Fehu","Gems"];
        for(int i=0;i<names.Length;i++)
        {
            string name=names[i];int x=12,y=82+i*59;
            using var shape=CrownfireControls.Frame(new RectangleF(x,y,Width-24,54),5);
            using var fill=new SolidBrush(ImperialTheme.Raised);
            using var edge=new Pen(ImperialTheme.Border);
            g.FillPath(fill,shape);g.DrawPath(edge,shape);
            if(name=="Gold")CrownfireControls.Glyph(g,"Gold",new RectangleF(x+8,y+7,38,38),ImperialTheme.Gold);
            else GameLootIcons.Draw(g,name,new RectangleF(x+6,y+5,42,42));
            Text(LootTrackerSnapshot.DisplayName(name),rowFont,ImperialTheme.Text,new(x+56,y+4,96,24));
            Text(snapshot.AmountText(name),rowFont,ImperialTheme.Gold,new(x+154,y+4,Width-180,24));
            Text(snapshot.RateText(name,"N1")+" /h",detailFont,ImperialTheme.Muted,new(x+56,y+30,Width-88,18));
        }
        Text($"{snapshot.Sources.Sum(s=>s.Kills):N0} kills · {snapshot.Sources.Sum(s=>s.Drops):N0} drops",detailFont,ImperialTheme.Text,new(14,443,Width-28,20));
        Text("Active  "+FormatDuration(snapshot.RateElapsed),titleFont,ImperialTheme.Gold,new(14,472,Width-28,24));
        Text($"Zone {snapshot.Zone} · Pending kills {snapshot.PendingKills} · drag header to move",detailFont,ImperialTheme.Muted,new(10,Height-19,Width-20,16));
        FantasyFrame.Draw(g,ClientRectangle);

        void Text(string value,Font font,Color color,Rectangle bounds) =>
            TextRenderer.DrawText(g,value,font,bounds,color,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|
                TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding|TextFormatFlags.Left);
    }
}
