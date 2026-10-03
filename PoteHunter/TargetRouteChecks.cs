using System.Text.Json;

namespace PoteHunter;

internal static class TargetRouteChecks
{
    public static void Run()
    {
        string directory=Path.Combine(Path.GetTempPath(),"PoteHunter-target-route-checks-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,"routes.json");
        void Check(bool passed,string message){if(!passed)throw new Exception("Target routes: "+message);}
        SavedNavigationRoute Route(int slot)=>new(7,new(slot*10,0),slot+.25,[new(slot*10,0),new(slot*10,2),new(slot*10,4)],DateTime.UnixEpoch,"Fixture",12,35,3,true,true);
        try
        {
            var legacy=Enumerable.Range(0,3).Select(slot=>(SavedNavigationRoute?)Route(slot)).ToArray();
            File.WriteAllText(path,JsonSerializer.Serialize(new SavedNavigationRouteSet(legacy)));
            var nav=new Navigation();nav.SelectTargetSelection("Mimic",path);
            Check(nav.SavedRoutes.All(route=>route==null) && nav.UnassignedRouteCount==3,"legacy routes guessed a target");
            Check(RecoveryTravel.StartupSlot(nav.SavedRoutes,new(0,4),7,"Fixture",12,0)<0 &&
                !nav.TryGetRecoveryRoute(7,new(0,4),new(0,0),out _),"unassigned route used by startup or recovery");
            Check(nav.AssignUnassignedRoutes(path) && nav.UnassignedRouteCount==0,"explicit legacy assignment failed");
            Check(Directory.GetFiles(directory,"*.bak").Length==1,"original migration file not backed up");
            Check(nav.GetSavedRoute(2) is {Character:"Fixture",HuntRadius:35,RepairAfterDeath:true,Heading:2.25} && nav.GetSavedRoute(2)!.Points.SequenceEqual(legacy[2]!.Points),"migration changed route/facing/profile");
            nav.SelectTargetSelection("Tribal",path);
            Check(nav.SavedRoutes.All(route=>route==null) && RecoveryTravel.StartupSlot(nav.SavedRoutes,new(0,4),7,"Fixture",12,0)<0,"target switch borrowed another set");
            Check(nav.SaveCurrentSpot(7,new(30,0),2,0,path),"new target save failed");
            nav.SelectTargetSelection("  mIMic  ",path);
            Check(nav.SavedRoutes.Count(route=>route!=null)==3 && nav.GetSavedRoute(0)!.Anchor==new Vec(0,0),"case/outer whitespace changed target identity");
            nav.ClearSavedRoute(2,path);Check(nav.GetSavedRoute(2)==null && nav.GetSavedRoute(0)!=null,"clear selected changed wrong slot");
            nav.ClearSavedRoute(path);nav.SelectTargetSelection("Tribal",path);
            Check(nav.GetSavedRoute(0)?.Anchor==new Vec(30,0),"clear target deleted another set");
            nav.SelectTargetSelection("",path);Check(nav.SavedRoutes.All(route=>route==null),"All targets shared a named target set");
            Check(nav.SaveCurrentSpot(7,new(40,0),0,1,path),"All targets save failed");
            var other=new Navigation();other.SelectTargetSelection("Towers",path);
            nav.SelectTargetSelection("Mimic",path);Check(nav.SaveCurrentSpot(7,new(50,0),0,0,path),"new named route save failed");
            Check(other.SaveCurrentSpot(7,new(60,0),0,0,path),"second writer save failed");
            nav.SelectTargetSelection("Mimic",path);Check(nav.GetSavedRoute(0)?.Anchor==new Vec(50,0),"save from an older loaded library lost another set");
            nav.Observe("record",new(0,0),12);nav.BeginRecording(new(0,0));nav.Observe("record",new(2,0),12);
            nav.SelectTargetSelection("Pulkhan",path);
            Check(!nav.Recording && nav.RecordingCancelled && nav.RecordingTrail.Count==0 && nav.Trail.Count==0,"old-target recording/trail survived switch");
            nav.BeginRecording(new(5,0));Check(nav.Recording && !nav.RecordingCancelled,"new target recording could not restart");nav.EndRecording();
            var library=JsonSerializer.Deserialize<SavedNavigationRouteLibrary>(File.ReadAllText(path))!;
            library=library with {UnassignedRoutes=legacy};File.WriteAllText(path,JsonSerializer.Serialize(library));
            nav.SelectTargetSelection("Tribal",path);
            Check(!nav.CanAssignUnassignedRoutes && !nav.AssignUnassignedRoutes(path) && nav.UnassignedRouteCount==3,"assignment overwrote existing target routes");
            nav.ClearSavedRoute(path);Check(nav.UnassignedRouteCount==3,"clear target destroyed unassigned routes");
            File.WriteAllText(path,JsonSerializer.Serialize(Route(0)));nav.SelectTargetSelection("Mimic",path);
            Check(nav.UnassignedRouteCount==1 && nav.AssignUnassignedRoutes(path),"single-route legacy migration failed");
            const string unsupported="{\"Version\":999,\"TargetRoutes\":{},\"UnassignedRoutes\":[]}";
            File.WriteAllText(path,unsupported);nav.LoadSavedRoutes(path);
            Check(!nav.SaveCurrentSpot(7,new(0,0),0,0,path),"unsupported route file overwritten");nav.ClearSavedRoute(path);
            Check(File.ReadAllText(path)==unsupported,"unsupported library lost after clear");
            File.WriteAllText(path,"{broken");nav.LoadSavedRoutes(path);
            Check(!nav.SaveCurrentSpot(7,new(0,0),0,0,path) && File.ReadAllText(path)=="{broken","unreadable file overwritten");
            File.WriteAllText(path,"{}");nav.LoadSavedRoutes(path);
            Check(!nav.SaveCurrentSpot(7,new(0,0),0,0,path) && File.ReadAllText(path)=="{}","unknown legacy shape overwritten");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"target-route-checks.json"),JsonSerializer.Serialize(new
                {Passed=true,Checks=new[]{"target startup/recovery isolation","three-slot migration and backup","character/map/height/facing retention","case and outer-space normalization","All targets separate set","scoped clear preserves other sets and unassigned routes","stale loaded library merges newest saves","target change cancels recording/trail","nonempty assignment rejected","old single route migration","unsupported/unreadable file preservation"}}));
        }
        finally
        {
            foreach(string file in Directory.GetFiles(directory))File.Delete(file);
            Directory.Delete(directory);
        }
    }
}
