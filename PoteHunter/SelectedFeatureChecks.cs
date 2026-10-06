using System.Text.Json;
using System.Text.Json.Nodes;
namespace PoteHunter;

internal static class SelectedFeatureChecks
{
 internal static void Run()
 {
  string root=Path.Combine(Path.GetTempPath(),"PlayPoteBot-features-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try
  {
   var store=new SettingsProfileStore(Path.Combine(root,"profiles"));
   var original=new Options{Player="Local character",UseAttackPotions=true,SmartSkillTargeting=false,AutoRepairLowDurability=true,
    RepairDurabilityPercent=36,RouteCorridorRadius=18,LootPickupRadius=7,HuntRadius=55,ItemGradeHotkey="F7",ItemGradeScalePercent=175,SentinelRadarX=-125};
   store.Save("../Tank",original);var local=new Options{Player="Current character",ItemGradeHotkey="F10",ItemGradeScalePercent=125,SentinelRadarX=-260};
   var restored=store.Materialize("../Tank",local);
   if(!restored.UseAttackPotions||restored.SmartSkillTargeting||!restored.AutoRepairLowDurability||restored.RepairDurabilityPercent!=36||
     restored.RouteCorridorRadius!=18||restored.LootPickupRadius!=7||restored.HuntRadius!=55||restored.Player!=local.Player||
     restored.ItemGradeHotkey!=local.ItemGradeHotkey||restored.ItemGradeScalePercent!=local.ItemGradeScalePercent||restored.SentinelRadarX!=local.SentinelRadarX)throw new Exception("Custom settings or local preferences lost during profile switch.");
   string export=Path.Combine(root,"export.json");store.Export("../Tank",export);
   if(File.ReadAllText(export).Contains(original.Player))throw new Exception("Profile export contains local character identity.");
   if(JsonSerializer.Deserialize<SettingsProfileDocument>(File.ReadAllText(export))!.Settings.ItemGradeScalePercent!=100)
    throw new Exception("Profile export contains this PC's item tooltip size preference.");
   var imported=new SettingsProfileStore(Path.Combine(root,"imported"));imported.Import(export);
   if(imported.Materialize("../Tank",local).RepairDurabilityPercent!=36)throw new Exception("Profile export/import lost custom durability settings.");
   if(imported.Materialize("../Tank",local).ItemGradeScalePercent!=local.ItemGradeScalePercent)throw new Exception("Imported profile replaced the local item tooltip size.");
   var legacy=JsonSerializer.Deserialize<Options>("{\"Target\":\"Mimic\"}")!;
   if(legacy.ItemGradeScalePercent!=100 || legacy.Target!="Mimic")throw new Exception("Legacy item tooltip size defaults changed existing settings.");
   foreach(var (saved,expected) in new[]{(-1,ItemGradeDesk.MinimumScalePercent),(135,135),(999,ItemGradeDesk.MaximumScalePercent)})
   {
    var bounded=JsonSerializer.Deserialize<Options>("{\"ItemGradeScalePercent\":"+saved+"}")!;
    var roundTrip=JsonSerializer.Deserialize<Options>(JsonSerializer.Serialize(bounded))!;
    if(bounded.ItemGradeScalePercent!=expected || roundTrip.ItemGradeScalePercent!=expected)throw new Exception("Item tooltip size did not retain its bounded saved value.");
   }
   string settings=Path.Combine(root,"settings.json");File.WriteAllText(settings,"original settings bytes");
   string backup=store.Activate("../Tank",settings,local);
   if(File.ReadAllText(backup)!="original settings bytes")throw new Exception("Profile activation did not preserve the original file.");
   var unsupported=JsonNode.Parse(File.ReadAllText(export))!;unsupported["SchemaVersion"]=99;File.WriteAllText(export,unsupported.ToJsonString());
   bool rejected=false;try{imported.Import(export);}catch(InvalidDataException){rejected=true;}
   if(!rejected||imported.List().Length!=1)throw new Exception("An unsupported profile changed the catalog.");
   store.Delete("../Tank");if(store.List().Length!=0||Directory.GetFiles(Path.Combine(root,"profiles"),"*.deleted-*").Length!=1)throw new Exception("Profile delete backup failed.");
   string mapsPath=Path.Combine(root,"maps");Directory.CreateDirectory(mapsPath);
   using(var image=new Bitmap(64,64))image.Save(Path.Combine(mapsPath,"map.png"),System.Drawing.Imaging.ImageFormat.Png);
   string hash=new('A',64);var wall=new RouteObstacle(new(50,50),5,"Fixture wall",[new(49,47),new(51,47),new(51,53),new(49,53)]);
   void Map(bool confirmed)=>File.WriteAllText(Path.Combine(mapsPath,"12.json"),JsonSerializer.Serialize(new{Zone=12,Image="map.png",MinX=0,MinY=0,MaxX=100,MaxY=100,ClientSha256=hash,VisualAlignmentConfirmed=confirmed,CollisionObstacles=new[]{wall}}));
   Map(true);using var maps=new ZoneMapBackground(mapsPath);
   if(maps.NavigationObstacles(12,hash).Length!=1||maps.NavigationObstacles(12,new string('B',64)).Length!=0||maps.NavigationObstacles(13,hash).Length!=0)throw new Exception("Collision map identity gate failed.");
   var nav=new Navigation();nav.SetImportedObstacles(maps.NavigationObstacles(12,hash));
   Vec from=new(45,50),to=new(55,50);
   if(nav.CanAdvance(from,to,[]))throw new Exception("Imported polygon did not block direct travel.");
   Vec step=nav.Waypoint(from,to,new(50,50),20,[]);
   if(step==to||!nav.CanAdvance(from,step,[]))throw new Exception("Imported polygon detour is unsafe.");
   Map(false);maps.Reload();if(maps.NavigationObstacles(12,hash).Length!=0)throw new Exception("Unconfirmed collision map affected routing.");
   File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"selected-feature-checks.json"),JsonSerializer.Serialize(new{Passed=true,HardwareInputEmitted=false,Checks=new[]{"profiles preserve custom settings","local identity and display preferences remain local","item tooltip size stays local through profile export/import","legacy tooltip size defaults and bounded JSON round trip","profile round trip and schema rejection","activation and deletion backups","collision map zone/hash/alignment gates","polygon detour"}},new JsonSerializerOptions{WriteIndented=true}));
  }
  finally{try{Directory.Delete(root,true);}catch{}}
 }
}
