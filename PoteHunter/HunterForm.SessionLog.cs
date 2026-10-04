using System.Diagnostics;

namespace PoteHunter;

public sealed partial class HunterForm
{
    internal static string HuntingLogRoot=>Path.Combine(AppContext.BaseDirectory,"hunting-logs");

    void ObserveLoggedHealth(Entity self,Health hp,int zone)
    {
        var log=HuntingSessionLog.Current;
        if(log==null)return;
        log.UpdateLoot(lootTracker.Snapshot());
        log.Observe(world.Pid,self,zone,hp);
    }

    void ObserveLoggedPlayerHealth(Health hp)
    {
        if(HuntingSessionLog.Current==null)return;
        try { ObserveLoggedHealth(world.LocalPlayer(),hp,world.ActiveZone()); }
        catch(InvalidOperationException) { /* A transient missing body is not a new death or revival. */ }
    }

    void BeginLoggedHunt(Options options)
    {
        var log=HuntingSessionLog.Current;
        if(log==null || runCharacter==null)return;
        log.UpdateLoot(lootTracker.Snapshot());
        string mode=options.HealerMode?(options.GroupMode?"Group healer":"Healer"):(options.GroupMode?"Group hunt":"Solo hunt");
        Health hp=default;
        try {hp=world.TargetHealth(runCharacter.Id);}
        catch(InvalidOperationException) { /* Logging must not interrupt a hunt on a transient health read. */ }
        log.BeginHunt(mode,options.Target,world.Pid,runCharacter,runZone??0,hp);
    }

    void FinishLoggedHunt(string reason)
    {
        var log=HuntingSessionLog.Current;
        if(log==null)return;
        log.UpdateLoot(lootTracker.Snapshot());
        log.EndHunt(reason);
    }

    void InitializeSessionLogIndex(TabPage page)
    {
        var text=page.Controls.OfType<Label>().Single();
        text.Text+="\n\nSESSION LOGS\nLogging starts when PlayPoteBot opens. Connected HP observations record deaths and confirmed revivals even when hunting is stopped. Each hunt has its own ID; loot snapshots use the tracker totals, wallet-based gold and earnings timer. Loot and timer resets are recorded. Death cause stays Unknown because the client has no verified killer reading. Logs stay on this computer in hunting-logs; open events.csv in Excel. Clicks are recorded as attempts, not successful revivals.";
        page.Controls.Remove(text);
        var body=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top,FlowDirection=FlowDirection.TopDown,WrapContents=false};
        var open=new Button{Name="openHuntingLogs",Text="Open session logs",AutoSize=true,Margin=new Padding(0,0,0,14)};
        open.Click+=(_,_)=>
        {
            try {Directory.CreateDirectory(HuntingLogRoot);Process.Start(new ProcessStartInfo(HuntingLogRoot){UseShellExecute=true});}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {message="Cannot open session logs: "+ex.Message;}
        };
        body.Controls.AddRange([open,text]);page.Controls.Add(body);
    }
}
