using System.Text.Json;
namespace PoteHunter;

public sealed partial class HunterForm
{
 void CheckSelectedFeatureUi(TabControl tabs)
 {
  if(!offlinePreviewMode||working||connected)throw new Exception("Feature checks require offline mode.");
  Options baseline=CurrentOptions();var page=tabs.SelectedTab;var previousSize=Size;
  try
  {
   var settings=JsonSerializer.Deserialize<Options>(JsonSerializer.Serialize(baseline))!;
   settings.Target="Mimic";settings.HuntRadius=46;settings.RouteCorridorRadius=16;settings.LootPickupRadius=9;
   settings.TurnSpeedDegreesPerSecond=90;
   settings.ItemGradeFocusStat="MAX HP";
   settings.Ranged=true;settings.ArcherClass=true;settings.MeleeRange=27;settings.RangedPullCount=9;settings.RangedPullTimeoutSeconds=38;
   settings.UseAttackPotions=true;settings.UseDefensePotions=true;settings.AutoRepairLowDurability=true;settings.RepairDurabilityPercent=41;
   settings.RevivalDelaySeconds=27;settings.SmartSkillTargeting=false;settings.AutoDetectSkills=false;settings.SkillKeys="12";
   ApplyProfileSettings(settings);var result=CurrentOptions();
   if(result.TurnSpeedDegreesPerSecond!=90 || turnSpeedLimit.Value!=90)throw new Exception("Profile turn-speed limit was not restored.");
   if(result.ItemGradeFocusStat!="MAX HP" || (itemGradeFocus.SelectedItem as ItemGradeDesk.FocusChoice)?.Key!="MAX HP")throw new Exception("Upgrade Desk focus stat was not restored by the settings profile.");
   if(result.Target!="Mimic"||result.HuntRadius!=46||result.RouteCorridorRadius!=16||result.LootPickupRadius!=9||
     !result.ArcherClass||!result.Ranged||result.MeleeRange!=27||result.RangedPullCount!=9||result.RangedPullTimeoutSeconds!=38||
     !result.UseAttackPotions||!result.UseDefensePotions||!result.AutoRepairLowDurability||result.RepairDurabilityPercent!=41||
     result.RevivalDelaySeconds!=27||result.SmartSkillTargeting||result.SkillKeys!="12")throw new Exception("Settings profile controls did not restore custom behavior: "+JsonSerializer.Serialize(new{result.Target,result.HuntRadius,result.RouteCorridorRadius,result.LootPickupRadius,result.ArcherClass,result.Ranged,result.MeleeRange,result.RangedPullCount,result.RangedPullTimeoutSeconds,result.UseAttackPotions,result.UseDefensePotions,result.AutoRepairLowDurability,result.RepairDurabilityPercent,result.RevivalDelaySeconds,result.SmartSkillTargeting,result.SkillKeys}));
   if(itemGradeHotkeyRegistered)throw new Exception("Offline preview registered a global key.");
   ApplyProfileSettings(baseline);
   var routing=navigation3DHint.Parent!.Controls.OfType<CollapsibleSection>().Single(s=>s.Name=="navigationRoutingOptions");
   bool wasExpanded=routing.Expanded;tabs.SelectedTab=navigationPage;routing.Expanded=true;PerformLayout();Application.DoEvents();
   if(!importMapToolWorld.Visible||!routing.Content.Contains(importMapToolWorld.Parent!))throw new Exception("Collision map import is unreachable from routing options.");
   ((ScrollableControl)routing.Parent!).ScrollControlIntoView(routing);PerformLayout();Application.DoEvents();
   using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(Path.Combine(AppContext.BaseDirectory,"selected-collision-map.png"));}
   routing.Expanded=wasExpanded;
   foreach(var (name,image) in new[]{("preferencesPage","selected-settings.png"),("profilesPage","selected-profiles.png")})
   {
    tabs.SelectedTab=tabs.TabPages.Cast<TabPage>().Single(t=>t.Name==name);PerformLayout();Application.DoEvents();
    using var bitmap=new Bitmap(Width,Height);DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(Path.Combine(AppContext.BaseDirectory,image));
   }
   var updates=Controls.Find("settingsUpdates",true).OfType<Button>().Single();
   if(!tabs.TabPages.Cast<TabPage>().Single(t=>t.Name=="preferencesPage").Contains(updates.Parent!))throw new Exception("Updates is not inside Settings.");
   var gradePage=tabs.TabPages.Cast<TabPage>().Single(t=>t.Text=="Item grades");tabs.SelectedTab=gradePage;PerformLayout();Application.DoEvents();
   using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(Path.Combine(AppContext.BaseDirectory,"selected-item-grades.png"));}
   foreach(var size in new[]{previousSize,MinimumSize})
   {
    Size=size;PerformLayout();Application.DoEvents();
    foreach(Control control in new Control[]{itemGradeEnabled,itemGradeAutoShow,itemGradeTarget,itemGradeFocus,itemGradeHotkey,itemGradeLastResult})
    {
     if(!control.Visible||control.Height<20)throw new Exception("Item grade control collapsed: "+control.GetType().Name);
     var bounds=gradePage.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
     if(bounds.Left<0||bounds.Right>gradePage.ClientSize.Width)throw new Exception("Item grade control overflows the page at "+size);
    }
   }
   using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(Path.Combine(AppContext.BaseDirectory,"selected-item-grades-minimum.png"));}
   File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"selected-feature-ui-checks.json"),JsonSerializer.Serialize(new{Passed=true,HardwareInputEmitted=false,Checks=new[]{"profile restores custom UI controls","offline preview has no global grade hotkey","Updates in Settings","profiles and grades reachable"}}));
  }
  finally{Size=previousSize;ApplyProfileSettings(baseline);tabs.SelectedTab=page;}
 }
}
