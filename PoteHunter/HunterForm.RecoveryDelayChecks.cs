using System.Text.Json;

namespace PoteHunter;

public sealed partial class HunterForm
{
    void CheckRecoveryReturnDelayUi(TabControl tabs)
    {
        if(!offlinePreviewMode||working||busy||connected||clientRecoveryRunning)
            throw new InvalidOperationException("Recovery return delay UI checks require a disconnected, stopped offline fixture.");
        Options baseline=CurrentOptions();var previousPage=tabs.SelectedTab;var previousSize=Size;bool settingsEnabled=settings.Enabled;
        try
        {
            var configured=JsonSerializer.Deserialize<Options>(JsonSerializer.Serialize(baseline))!;
            configured.HealerMode=false;configured.GroupMode=false;configured.AutoReviveAfterDeath=true;
            configured.AutoRepairAfterDeath=true;configured.RevivalDelaySeconds=27;
            configured.DelayReturnAfterRevival=true;configured.ReturnDelaySeconds=87;
            ApplyProfileSettings(configured);
            var restored=CurrentOptions();
            if(!delayRouteReturn.Checked||returnDelaySeconds.Value!=87||!delayRouteReturn.Enabled||!returnDelaySeconds.Enabled||
                !restored.DelayReturnAfterRevival||restored.ReturnDelaySeconds!=87||restored.RevivalDelaySeconds!=27||!restored.AutoRepairAfterDeath)
                throw new InvalidOperationException("Profile restore lost the route-return delay or confused it with revival/repair settings.");

            void WaitForAutosave()
            {
                var elapsed=System.Diagnostics.Stopwatch.StartNew();
                while(compactSaveTimer.Enabled&&elapsed.ElapsedMilliseconds<2000){Application.DoEvents();Thread.Sleep(10);}
                if(compactSaveTimer.Enabled||compactSaved.Text!="Saved")throw new InvalidOperationException("Return-delay controls did not finish autosaving.");
            }
            delayRouteReturn.Checked=false;WaitForAutosave();
            if(returnDelaySeconds.Enabled||returnDelaySeconds.Value!=87||Options.Read().DelayReturnAfterRevival||Options.Read().ReturnDelaySeconds!=87)
                throw new InvalidOperationException("Disabled route-return delay discarded its duration or did not autosave.");
            delayRouteReturn.Checked=true;returnDelaySeconds.Value=23;WaitForAutosave();
            if(!Options.Read().DelayReturnAfterRevival||Options.Read().ReturnDelaySeconds!=23||Options.Read().RevivalDelaySeconds!=27)
                throw new InvalidOperationException("Return-delay seconds did not autosave independently from revival delay.");
            autoRevive.Checked=false;RefreshRecoveryReturnDelaySettings();
            if(delayRouteReturn.Enabled||returnDelaySeconds.Enabled||!delayRouteReturn.Checked||returnDelaySeconds.Value!=23)
                throw new InvalidOperationException("Disabled death recovery left return-delay controls usable or lost their choices.");
            autoRevive.Checked=true;RefreshRecoveryReturnDelaySettings();

            compactSaveTimer.Stop();string saved=File.ReadAllText(Options.PathName);
            foreach(string state in new[]{"hunt","operation","client recovery"})
            {
                working=state=="hunt";busy=state=="operation";clientRecoveryRunning=state=="client recovery";
                RefreshRecoveryReturnDelaySettings();QueueRecoveryReturnDelaySave();
                if(delayRouteReturn.Enabled||returnDelaySeconds.Enabled||compactSaveTimer.Enabled||File.ReadAllText(Options.PathName)!=saved)
                    throw new InvalidOperationException("Return-delay controls bypassed the "+state+" lock or queued an active-operation save.");
            }
            working=busy=clientRecoveryRunning=false;settings.Enabled=false;RefreshRecoveryReturnDelaySettings();
            if(delayRouteReturn.Enabled||returnDelaySeconds.Enabled)throw new InvalidOperationException("Return-delay controls lost the inherited settings lock.");
            settings.Enabled=true;RefreshRecoveryReturnDelaySettings();
            if(!delayRouteReturn.Enabled||!returnDelaySeconds.Enabled)throw new InvalidOperationException("Return-delay controls did not unlock after the operation.");
            healerMode.Checked=true;RefreshRecoveryReturnDelaySettings();
            if(delayRouteReturn.Enabled||returnDelaySeconds.Enabled)throw new InvalidOperationException("Healer mode enabled a saved-route death-return delay.");
            healerMode.Checked=false;

            foreach(int seconds in new[]{0,600,45})
            {
                configured.ReturnDelaySeconds=seconds;ApplyProfileSettings(configured);
                if(returnDelaySeconds.Value!=seconds||CurrentOptions().ReturnDelaySeconds!=seconds||returnDelaySeconds.Minimum!=0||returnDelaySeconds.Maximum!=600)
                    throw new InvalidOperationException("Return-delay UI/profile boundaries did not restore the saved duration.");
            }

            Control ancestor=delayRouteReturn;
            while(ancestor is not TabPage&&ancestor.Parent!=null)ancestor=ancestor.Parent;
            if(ancestor is not TabPage recoveryPage)throw new InvalidOperationException("Return-delay controls are outside the Setup page.");
            tabs.SelectedTab=recoveryPage;
            var row=Controls.Find("recoveryReturnDelayRow",true).OfType<TableLayoutPanel>().Single();
            foreach(var size in new[]{previousSize,MinimumSize})
            {
                Size=size;PerformLayout();Application.DoEvents();recoveryPage.ScrollControlIntoView(row);PerformLayout();Application.DoEvents();
                foreach(Control control in new Control[]{delayRouteReturn,returnDelaySeconds})
                {
                    if(!control.Visible||control.Height<20)throw new InvalidOperationException("Return-delay control collapsed in Setup.");
                    var bounds=recoveryPage.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                    if(bounds.Left<0||bounds.Right>recoveryPage.ClientSize.Width)
                        throw new InvalidOperationException("Return-delay control overflows Setup at "+size+".");
                }
                using var preview=new Bitmap(Width,Height);DrawToBitmap(preview,new(Point.Empty,Size));
                preview.Save(Path.Combine(AppContext.BaseDirectory,size==MinimumSize?"ui-recovery-return-delay-minimum.png":"ui-recovery-return-delay.png"));
            }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"recovery-return-delay-ui-checks.json"),JsonSerializer.Serialize(new
            {
                Passed=true,HardwareInputEmitted=false,
                Checks=new[]{"native Death recovery controls restored by named profile bindings","return toggle and seconds autosave independently from revival delay",
                    "disabled return toggle retains duration","revival-off and healer-mode usability","hunt/operation/client-recovery and inherited settings locks",
                    "zero and six-hundred second profile boundaries","controls remain visible and fit normal/minimum widths"}
            },new JsonSerializerOptions{WriteIndented=true}));
        }
        finally
        {
            working=busy=clientRecoveryRunning=false;settings.Enabled=settingsEnabled;Size=previousSize;
            ApplyProfileSettings(baseline);tabs.SelectedTab=previousPage;
        }
    }
}
