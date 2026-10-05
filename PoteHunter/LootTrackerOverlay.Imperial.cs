namespace PoteHunter;

internal sealed partial class LootTrackerOverlay
{
    void DrawImperialHud(Graphics g,LootTrackerSnapshot snapshot)
    {
        DrawOverlayHeader(g,"ORBITAL / CARGO");
        OverlayText(g,$"Session {FormatDuration(snapshot.Elapsed)} · Active {FormatDuration(snapshot.RateElapsed)}",detailFont,ImperialTheme.Text,new(12,38,Width-24,16));
        OverlayText(g,"Gold: net wallet · items: detected drops",detailFont,ImperialTheme.Muted,new(12,55,Width-24,16));
        OverlayText(g,"RESOURCE",detailFont,ImperialTheme.Muted,new(12,79,100,16));
        OverlayText(g,"TOTAL",detailFont,ImperialTheme.Muted,new(109,79,101,16),TextFormatFlags.Right);
        OverlayText(g,"/ ACTIVE H",detailFont,ImperialTheme.Muted,new(218,79,80,16),TextFormatFlags.Right);
        string[] names=["Gold","Silvin","Mithril","Iternium","Fehu","Gems"];
        for(int i=0;i<names.Length;i++)
        {
            string name=names[i];int y=101+i*36;
            if(i%2==0)
            {
                using var row=new SolidBrush(ImperialTheme.Surface);
                g.FillRectangle(row,8,y-1,Width-16,35);
            }
            LootOverlayArtwork.DrawResource(g,name,new(12,y+2,28,28));
            OverlayText(g,LootTrackerSnapshot.DisplayName(name),rowFont,ImperialTheme.Text,new(48,y+1,66,32));
            OverlayText(g,snapshot.AmountText(name),rowFont,ImperialTheme.Text,new(116,y+1,94,32),TextFormatFlags.Right,true);
            OverlayText(g,snapshot.RateText(name,"N1"),detailFont,ImperialTheme.Accent,new(218,y+1,80,32),TextFormatFlags.Right,true);
        }
        g.DrawLine(framePen,12,322,Width-12,322);
        OverlayText(g,$"{snapshot.Sources.Sum(s=>s.Kills):N0} kills · {snapshot.Sources.Sum(s=>s.Drops):N0} detected drops",rowFont,ImperialTheme.Text,new(12,329,Width-24,22));
        OverlayText(g,"SOURCE BREAKDOWN",detailFont,ImperialTheme.Muted,new(12,354,Width-24,16));
        for(int i=0;i<Math.Min(4,snapshot.Sources.Count);i++)
        {
            var source=snapshot.Sources[i];int y=373+i*16;
            OverlayText(g,source.Source,detailFont,ImperialTheme.Text,new(12,y,105,16));
            OverlayText(g,$"{source.Kills:N0} kills · {source.Drops:N0} drops",detailFont,ImperialTheme.Muted,new(119,y,Width-131,16),TextFormatFlags.Right);
        }
        g.DrawLine(framePen,12,443,Width-12,443);
        for(int i=0;i<Math.Min(2,snapshot.RecentDrops.Count);i++)
        {
            var drop=snapshot.RecentDrops[i];
            OverlayText(g,$"{drop.Source}: {drop.Name}",detailFont,ImperialTheme.Muted,new(12,450+i*18,Width-24,17));
        }
        if(snapshot.RecentDrops.Count==0)
            OverlayText(g,"No recent detected drops",detailFont,ImperialTheme.Muted,new(12,450,Width-24,17));
        OverlayText(g,$"Zone {snapshot.Zone} · Pending kills {snapshot.PendingKills} · drag header",detailFont,ImperialTheme.Muted,new(10,Height-19,Width-20,16));
    }
}
