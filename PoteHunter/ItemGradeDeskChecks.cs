using System.Text.Json;

namespace PoteHunter;

internal static class ItemGradeDeskChecks
{
    static void Require(bool ok, string reason) { if (!ok) throw new Exception("Upgrade Desk: " + reason); }
    internal static void CheckModel()
    {
        var profile = ItemGradeTable.Find(101)!;
        var plan = ItemGradePlanner.Plan(profile, profile.Name, profile.Id, new Dictionary<string,int> { ["DEF"] = 100, ["EVAS"] = 10, ["MAX HP"] = 2000, ["ODD"] = 12 }, null);
        Require(ItemGradeDesk.Focus(plan, "Auto")?.Stat == "MAX HP", "Auto must compare progress to each stat's actual AAA/S target.");
        Require(ItemGradeDesk.Focus(plan, "MAX HP")?.Target == ItemGrade.S, "Explicit focus must preserve the planner's automatic S target.");
        Require(ItemGradeDesk.Focus(plan, "MIN")?.Stat == "MAX HP", "A missing saved focus must use the closest target on this item.");
        Require(ItemGradeDesk.Focus(plan, "DEF")?.Stat == "DEF" && ItemGradeDesk.Focus(plan, "bad")?.Stat == "MAX HP", "Explicit focus must win; invalid focus must use Auto.");
        CheckClosestTarget();
        CheckEveryStat();
        CheckJewelry();
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
        Require(ItemGradeDesk.EmptyFocusMessage(missing).Contains("No supported stat") &&
            ItemGradeDesk.EmptyFocusMessage(empty).Contains("No readable stats"),"Missing data and empty readings need distinct messages.");
        var allReached = ItemGradePlanner.Plan(profile,profile.Name,profile.Id,new Dictionary<string,int>{["DEF"]=500},ItemGrade.AAA);
        Require(ItemGradeDesk.Focus(allReached,"Auto")==null && ItemGradeDesk.EmptyFocusMessage(allReached).Contains("have reached"),
            "All-reached items must show completion without selecting a reached focus.");
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
    static void CheckJewelry()
    {
        foreach(var category in new[]{"Ring","Rings","Necklace","Necklaces","Amulet","Amulets","Earring","Earrings","Bracelet","Bracelets","Jewelry","Jewellery","  rInG  ","Ring/Necklace","Ring | Amulet"})
            Require(ItemGradeEligibility.IsJewelry(category),"Jewelry category not excluded: "+category);
        Require(ItemGradeEligibility.IsJewelry("", "Ring") && ItemGradeEligibility.IsJewelry("", "", "Jewellery"),
            "Verified table metadata must also exclude jewelry.");
        foreach(var category in new[]{"Ringmail","Body Armor","Weapon","Accessory", ""})
            Require(!ItemGradeEligibility.IsJewelry(category),"Unconfirmed category incorrectly excluded: "+category);
        Require(!ItemGradeEligibility.IsJewelry(null),"Missing categories cannot be invented.");
        foreach(int id in new[]{5406,1307})
        {
            var profile=ItemGradeTable.Find(id)!;
            Require(!ItemGradeEligibility.IsJewelry(null,profile.Type,profile.Category),
                "A weapon with a ring-like name was suppressed: "+profile.Name);
        }
        var legacyReading=new HoveredItemReading(true,"Equipment",null,"Mount Ring",new Dictionary<string,int>());
        Require(!legacyReading.SuppressOverlay,"Existing equipment reads changed disposition.");
    }
    static void CheckClosestTarget()
    {
        var profile = new ItemGradeProfile(900001, "Scale comparison", "Armor", "Armor", "", 0, 0,
            new Dictionary<string,ItemStatBreakpoints> {
                ["DEF"] = new("DEF", 1, 10, 40, 80, 100, 110),
                ["MAX HP"] = new("MAX HP", 1, 1000, 4000, 8000, 10000, 20000),
                ["ACC"] = new("ACC", 1, 10, 40, 80, null, null),
                ["MAX MP"] = new("MAX MP", 1, 10, 40, 80, 0, null)
            });
        ItemGradePlan Plan(int defense, int health, ItemGrade target = ItemGrade.AAA) =>
            ItemGradePlanner.Plan(profile, profile.Name, profile.Id,
                new Dictionary<string,int> { ["DEF"] = defense, ["MAX HP"] = health, ["ACC"] = 99, ["ODD"] = 999999 }, target);
        var unequal = Plan(95, 9900);
        Require(ItemGradeDesk.Focus(unequal, "Auto")?.Stat == "MAX HP", "A 99% HP target must beat 95% defense despite the larger raw gap.");
        Require(ItemGradeDesk.Focus(unequal with { Stats = unequal.Stats.Select(s => s with { Gems = null }).ToArray() }, "Auto")?.Stat == "MAX HP", "Missing gem estimates must not change proximity.");
        Require(ItemGradeDesk.Focus(Plan(95, 9500), "Auto")?.Stat == "DEF", "Equal percentages must retain tooltip order.");
        Require(ItemGradeDesk.Focus(Plan(95, 9800, ItemGrade.S), "Auto")?.Stat == "DEF", "Changing the target must recalculate which stat is closest.");
        var reached = Plan(100, 9900);
        Require(ItemGradeDesk.Focus(reached, "Auto")?.Stat == "MAX HP" && ItemGradeDesk.Focus(reached, "DEF")?.Stat == "DEF", "Auto skips reached targets; explicit reached focus remains available.");
        Require(ItemGradeDesk.Focus(Plan(100, 10000), "Auto") == null, "All reached targets must leave Auto without a focus.");
        var zeroThreshold = ItemGradePlanner.Plan(profile, profile.Name, profile.Id, new Dictionary<string,int> { ["MAX MP"] = 1, ["MAX HP"] = 9900 }, ItemGrade.AAA);
        Require(ItemGradeDesk.Focus(zeroThreshold, "Auto")?.Stat == "MAX HP", "A zero threshold must not outrank a valid unfinished target.");
        var unavailable = unequal with { Stats = unequal.Stats.Where(s => s.Stat is "ACC" or "ODD").ToArray() };
        Require(ItemGradeDesk.Focus(unavailable, "Auto") == null && ItemGradeDesk.Ratio(unavailable, unavailable.Stats[0]) == null,
            "Unavailable targets must not become Auto focus.");
    }
    static void CheckEveryStat()
    {
        var thresholds = ItemGradeTable.StatOrder.ToDictionary(stat => stat,
            stat => new ItemStatBreakpoints(stat, 0, 10, 25, 40, 100, 200));
        var profile = new ItemGradeProfile(900002, "All supported fields", "Armor", "Armor", "", 0, 0, thresholds);
        foreach (var winner in ItemGradeTable.StatOrder)
        {
            var values = ItemGradeTable.StatOrder.ToDictionary(stat => stat, stat => stat == winner ? 95 : 50);
            var plan = ItemGradePlanner.Plan(profile, profile.Name, profile.Id, values, ItemGrade.AAA);
            Require(plan.Stats.Count == ItemGradeTable.StatOrder.Length && ItemGradeDesk.Focus(plan, "Auto")?.Stat == winner,
                "Auto omitted supported stat " + winner);
            values[winner] = 100;
            var reached = ItemGradePlanner.Plan(profile, profile.Name, profile.Id, values, ItemGrade.AAA);
            Require(ItemGradeDesk.Focus(reached, "Auto")?.Stat != winner, "Reached stat became Auto focus: " + winner);
            values[winner] = 150;
            var above = ItemGradePlanner.Plan(profile, profile.Name, profile.Id, values, ItemGrade.AAA);
            Require(ItemGradeDesk.Focus(above, "Auto")?.Stat != winner, "Above-target stat became Auto focus: " + winner);
            Require(reached.Stats.Single(s => s.Stat == winner).Needed == 0,
                "Reached focus invented an upgrade gap: " + winner);
        }
    }
    internal static int RunNative(string output)
    {
        try
        {
            Directory.CreateDirectory(output); ApplicationConfiguration.Initialize(); CheckModel();
            using var overlay=new ItemGradeOverlay(); overlay.SetClickThrough(true);
            using(var form=new HunterForm(true))form.CheckItemGradeSuppression();
            _=overlay.Handle; Require(overlay.HasPassiveWindowStyles && overlay.HasClickThroughStyle && !overlay.Visible,"Automatic overlay lost passive or click-through styles.");
            var sword=ItemGradeTable.Bundled.Values.First(p=>p.Name=="Ancient Sword");
            var armor=ItemGradeTable.Bundled.Values.First(p=>p.Name=="Spiked Armor");
            var weaponPlan=ItemGradePlanner.Plan(sword,sword.Name,sword.Id,new Dictionary<string,int>{["MIN"]=290,["MAX"]=331,["ACC"]=298,["MAX MP"]=1660,["CRI"]=94,["MP REG"]=111},null);
            var armorPlan=ItemGradePlanner.Plan(armor,armor.Name,armor.Id,new Dictionary<string,int>{["DEF"]=135,["EVAS"]=101,["MAX HP"]=1590,["HP REG"]=80,["MR"]=84},null);
            var missing=ItemGradePlanner.Plan(null,"Unknown equipment",77,new Dictionary<string,int>{["DEF"]=4},null);
            foreach(var (name,plan,focus) in new[]{("weapon",weaponPlan,"Auto"),("armor",armorPlan,"MAX HP"),("armor-auto",armorPlan,"Auto"),("unknown",missing,"Auto"),
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
                Checks=new[]{"planner-derived grades/gaps/gems/thresholds","all 13 supported stats compete for Auto focus","reached and above-target stats excluded without fallback","jewelry category suppression preserves ring-named weapons","immediate suppression and repeated-hover cache reset","closest target across unlike scales and AAA/S targets","target changes and stable percentage ties","explicit focus and missing-stat fallback","missing gem estimates preserve proximity","unknown/reached/empty states","legacy settings preserved",
                    "all stats rendered","96/144/192 DPI and secondary monitor bounds","automatic click-through and manual no-activate styles","offscreen native rendering"}},new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception ex){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"upgrade-desk-error.txt"),ex.ToString());return 1;}
    }
}
