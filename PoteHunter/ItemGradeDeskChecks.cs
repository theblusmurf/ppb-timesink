using System.Text.Json;

namespace PoteHunter;

internal static class ItemGradeDeskChecks
{
    static void Require(bool ok, string reason) { if (!ok) throw new Exception("Upgrade Desk: " + reason); }
    internal static void CheckModel()
    {
        var profile = ItemGradeTable.Find(101)!;
        var plan = ItemGradePlanner.Plan(profile, profile.Name, profile.Id, new Dictionary<string,int> { ["DEF"] = 100, ["EVAS"] = 10, ["MAX HP"] = 2000, ["ODD"] = 12 }, null);
        Require(ItemGradeDesk.Focus(plan, "Auto")?.Stat == "DEF", "Auto must choose a stat with an actionable gem plan.");
        Require(ItemGradeDesk.Focus(plan, "MAX HP")?.Target == ItemGrade.S, "Explicit focus must preserve the planner's automatic S target.");
        Require(ItemGradeDesk.Focus(plan, "MIN")?.Stat == "DEF", "A weapon-only focus must fall back on armor.");
        var stat = plan.Stats.Single(s => s.Stat == "DEF");
        Require(ItemGradeDesk.RegularGem(stat) == stat.Gems!.Single(g => g.Name == stat.GemName), "Regular gem count must be the planner's existing result.");
        Require(ItemGradeDesk.Threshold(plan, stat) == 173 && Math.Abs(ItemGradeDesk.Ratio(plan, stat)!.Value - 100.0/173) < .00001, "Progress must use the selected target's actual threshold.");
        var unknown = plan.Stats.Single(s => s.Stat == "ODD");
        Require(ItemGradeDesk.Ratio(plan,unknown)==null && ItemGradeDesk.RegularGem(unknown)==null && ItemGradeDesk.Gap(unknown)=="—", "Unknown stats invented a grade, gap or gem plan.");
        var reached = ItemGradePlanner.Plan(profile, profile.Name, profile.Id, new Dictionary<string,int>{["DEF"]=500},ItemGrade.AAA).Stats.Single();
        Require(ItemGradeDesk.GemSummary(reached)=="No gems needed" && ItemGradeDesk.Gap(reached)=="Reached", "Reached target must not request gems.");
        var missing = ItemGradePlanner.Plan(null,"Unknown",77,new Dictionary<string,int>{["DEF"]=4},null);
        Require(ItemGradeDesk.Ratio(missing,missing.Stats.Single())==null && ItemGradeDesk.ProjectionText(missing.Stats.Single()).Contains("without grade data"), "Missing item profile invented recommendations.");
        var empty = missing with {Stats=Array.Empty<ItemStatPlan>()}; Require(ItemGradeDesk.Focus(empty,"Auto")==null,"Empty item requires an empty focus state.");
        var legacy = JsonSerializer.Deserialize<Options>("{\"Target\":\"Mimic\",\"TurnSpeedDegreesPerSecond\":150}")!;
        Require(legacy.ItemGradeFocusStat=="Auto" && legacy.Target=="Mimic" && legacy.TurnSpeedDegreesPerSecond==150,"Focus migration changed unrelated options.");
        Require(ItemGradeDesk.NormalizeFocus("bad")=="Auto" && ItemGradeDesk.NormalizeFocus(null)=="Auto","Invalid saved focus must fall back.");
        foreach(int dpi in new[]{96,144,192}) foreach(var area in new[]{new Size(1920,1080),new Size(1366,728),new Size(800,560)})
        {
            var logical = new Size(ItemGradeDesk.Width,ItemGradeDesk.Height(plan)); float scale=ItemGradeDesk.FitScale(logical,area,dpi);
            Require(logical.Width*scale<=area.Width+.01 && logical.Height*scale<=area.Height+.01,"DPI-scaled panel escaped its work area.");
            var screen=new Rectangle(-1366,-200,area.Width,area.Height);
            var size=new Size((int)(logical.Width*scale),(int)(logical.Height*scale));
            foreach(var anchor in new[]{screen.Location,new Point(screen.Right-1,screen.Bottom-1)})
                Require(screen.Contains(new Rectangle(ItemGradeOverlay.PlaceNear(anchor,size,screen,true),size)),"Panel escaped a secondary monitor.");
        }
    }
    internal static int RunNative(string output)
    {
        try
        {
            Directory.CreateDirectory(output); ApplicationConfiguration.Initialize(); CheckModel();
            using var overlay=new ItemGradeOverlay(); overlay.SetClickThrough(true);
            _=overlay.Handle; Require(overlay.HasPassiveWindowStyles && overlay.HasClickThroughStyle && !overlay.Visible,"Automatic overlay lost passive or click-through styles.");
            var sword=ItemGradeTable.Bundled.Values.First(p=>p.Name=="Ancient Sword");
            var armor=ItemGradeTable.Bundled.Values.First(p=>p.Name=="Spiked Armor");
            var weaponPlan=ItemGradePlanner.Plan(sword,sword.Name,sword.Id,new Dictionary<string,int>{["MIN"]=290,["MAX"]=331,["ACC"]=298,["MAX MP"]=1660,["CRI"]=94,["MP REG"]=111},null);
            var armorPlan=ItemGradePlanner.Plan(armor,armor.Name,armor.Id,new Dictionary<string,int>{["DEF"]=135,["EVAS"]=101,["MAX HP"]=1590,["HP REG"]=80,["MR"]=84},null);
            var missing=ItemGradePlanner.Plan(null,"Unknown equipment",77,new Dictionary<string,int>{["DEF"]=4},null);
            foreach(var (name,plan,focus) in new[]{("weapon",weaponPlan,"Auto"),("armor",armorPlan,"MAX HP"),("unknown",missing,"Auto"),
                ("reached",ItemGradePlanner.Plan(armor,armor.Name,armor.Id,new Dictionary<string,int>{["DEF"]=500},ItemGrade.AAA),"Auto")})
            {
                using var bitmap=overlay.RenderPreview(plan,focus); bitmap.Save(Path.Combine(output,"upgrade-desk-"+name+".png"));
                Require(overlay.FocusedStat==ItemGradeDesk.Focus(plan,focus),"Rendered focus does not match saved preference.");
            }
            using(var empty=overlay.RenderPreview(null,status:"Hover a weapon or armor to see its stats."))empty.Save(Path.Combine(output,"upgrade-desk-empty.png"));
            using(var scaled=overlay.RenderPreview(weaponPlan,available:new Size(800,560),dpi:192))
            {Require(scaled.Width<=800&&scaled.Height<=560,"Small-screen rendering clipped the panel.");scaled.Save(Path.Combine(output,"upgrade-desk-small-screen.png"));}
            overlay.SetClickThrough(false); Require(overlay.HasPassiveWindowStyles&&!overlay.HasClickThroughStyle&&!overlay.Visible,"Manual mode removed no-activate styles or showed a test window.");
            File.WriteAllText(Path.Combine(output,"upgrade-desk-checks.json"),JsonSerializer.Serialize(new{Passed=true,HardwareInputEmitted=false,LiveClientConnected=false,
                Checks=new[]{"planner-derived grades/gaps/gems/thresholds","explicit focus and missing-stat fallback","unknown/reached/empty states","legacy settings preserved",
                    "all stats rendered","96/144/192 DPI and secondary monitor bounds","automatic click-through and manual no-activate styles","offscreen native rendering"}},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception ex){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"upgrade-desk-error.txt"),ex.ToString());return 1;}
    }
}
