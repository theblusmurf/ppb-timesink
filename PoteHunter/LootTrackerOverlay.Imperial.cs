namespace PoteHunter;

internal sealed partial class LootTrackerOverlay
{
    void DrawImperialHud(Graphics g,LootTrackerSnapshot snapshot)
    {
        using var gold=new SolidBrush(ImperialTheme.Gold);
        using var surface=new SolidBrush(ImperialTheme.Surface);
        using var divider=new Pen(ImperialTheme.Border);
        g.FillRectangle(surface,1,1,Width-2,HeaderHeight-1);
        g.DrawRectangle(framePen,0,0,Width-1,Height-1);
        g.DrawLine(framePen,0,HeaderHeight,Width,HeaderHeight);
        g.DrawString("LOOT TRACKER",titleFont,gold,new PointF(10,6));
        Text("drag header to move",detailFont,ImperialTheme.Muted,new(240,9,138,17),true);
        Text($"Session {FormatDuration(snapshot.Elapsed)}  ·  Active {FormatDuration(snapshot.RateElapsed)}",detailFont,ImperialTheme.Muted,new(10,37,Width-20,16));
        Text("Gold is net wallet · other totals are detected drops",detailFont,ImperialTheme.Muted,new(10,52,Width-20,16));
        Text("RESOURCE",detailFont,ImperialTheme.Muted,new(10,68,130,16));
        Text("TOTAL",detailFont,ImperialTheme.Muted,new(155,68,92,16),true);
        Text("PER HOUR",detailFont,ImperialTheme.Muted,new(250,68,128,16),true);
        string[] names=["Gold","Silvin","Mithril","Iternium","Fehu","Gems"];
        int y=85;
        foreach(string name in names)
        {
            if(name=="Gold")
            {
                g.FillEllipse(gold,13,y+2,14,14);g.DrawEllipse(framePen,11,y+5,14,14);
            }
            else GameLootIcons.Draw(g,name,new RectangleF(10,y,24,22));
            Text(LootTrackerSnapshot.DisplayName(name),rowFont,ImperialTheme.Text,new(40,y,114,22));
            Text(snapshot.AmountText(name),rowFont,ImperialTheme.Text,new(155,y,92,22),true);
            Text(snapshot.RateText(name,"N1"),rowFont,ImperialTheme.Gold,new(250,y,128,22),true);
            y+=22;
        }
        g.DrawLine(divider,10,y+1,Width-10,y+1);y+=5;
        Text("TRACKED TARGETS",detailFont,ImperialTheme.Muted,new(10,y,Width-20,16));y+=16;
        foreach(var source in snapshot.Sources.Take(4))
        {
            Text($"{source.Source} · {source.Kills:N0} kills · {source.Drops:N0} drops",detailFont,ImperialTheme.Text,new(10,y,Width-20,14));y+=14;
        }
        if(y<=Height-32)
            Text(snapshot.RecentDrops.Count==0?"Recent drops: waiting for a tracked kill":$"Recent: {snapshot.RecentDrops[0].Source} · {snapshot.RecentDrops[0].Name}",
                detailFont,ImperialTheme.Gold,new(10,y,Width-20,16));
        Text($"Zone {snapshot.Zone} · Pending kills {snapshot.PendingKills}",detailFont,ImperialTheme.Muted,new(10,Height-17,Width-20,16));

        void Text(string value,Font font,Color color,Rectangle bounds,bool right=false) =>
            TextRenderer.DrawText(g,value,font,bounds,color,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|
                TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding|(right?TextFormatFlags.Right:TextFormatFlags.Left));
    }
}
