using System.Security.Cryptography;
using System.Text.Json;

namespace PoteHunter;

internal static class ScrollRouteProfileChecks
{
    static void Require(bool value,string reason){if(!value)throw new Exception("Scroll profiles: "+reason);}
    static void Reject(Action action,string reason)
    {
        bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}
        Require(rejected,reason);
    }
    internal static void Run(TeleporterProfile fixture)
    {
        var calibration=fixture with{Scroll=new(0,"7",901,"Teleport Scroll")};
        string directory=Path.Combine(Path.GetTempPath(),"scroll-profile-library-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);string file=Path.Combine(directory,"library.json"),legacy=Path.Combine(directory,"legacy.json");
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        try
        {
            Require(ScrollRouteProfiles.Available(file,legacy).Length==0&&!File.Exists(file),"empty read created a library or invented profiles");
            calibration.Save(legacy);string legacyHash=Hash(legacy);
            var imported=ScrollRouteProfiles.Available(file,legacy).Single();
            Require(!File.Exists(file)&&ScrollRouteProfiles.AssignedId(calibration.TargetSelection,0,file,legacy)==imported.Id,
                "old captures required recalibration or were rewritten during discovery");
            ScrollRouteProfiles.Assign(imported.Id,"Mimic",1,file,legacy);
            ScrollRouteProfiles.Assign(imported.Id,"Pulkhan",2,file,legacy);
            var stored=JsonSerializer.Deserialize<ScrollRouteProfiles.Registry>(File.ReadAllText(file))!;
            Require(stored.Profiles.Length==1&&stored.Assignments.Length==2&&Hash(legacy)==legacyHash,
                "sharing duplicated capture data or modified legacy calibration");
            var first=ScrollRouteProfiles.Resolve(calibration.ClientHash,new(calibration.Width,calibration.Height)," mimic ",1,file)!;
            var second=ScrollRouteProfiles.Resolve(calibration.ClientHash,new(calibration.Width,calibration.Height),"Pulkhan",2,file)!;
            Require(first.TargetSelection==" mimic "&&first.DestinationSlot==1&&second.DestinationSlot==2&&
                JsonSerializer.Serialize(first.Select)==JsonSerializer.Serialize(second.Select)&&
                JsonSerializer.Serialize(first.Confirm)==JsonSerializer.Serialize(second.Confirm)&&first.Scroll==second.Scroll,
                "shared capture did not resolve with the caller's exact route scope");
            Require(ScrollRouteProfiles.Resolve(calibration.ClientHash,new(calibration.Width,calibration.Height),"Other",1,file)==null,
                "assignment leaked into another target scope");
            var afterUpdate=ScrollRouteProfiles.Resolve("different-client",new(calibration.Width,calibration.Height),"Mimic",1,file);
            Require(afterUpdate?.ClientHash==calibration.ClientHash&&afterUpdate.Scroll==calibration.Scroll&&
                afterUpdate.TargetSelection=="Mimic"&&afterUpdate.DestinationSlot==1&&Hash(legacy)==legacyHash,
                "client update lost the saved scroll capture or changed its route assignment");
            Require(ScrollRouteProfiles.SelectableFor(imported,calibration.CharacterId,calibration.Character,
                    new(calibration.Width,calibration.Height),calibration.Landing.Zone)&&
                !ScrollRouteProfiles.SelectableFor(imported,calibration.CharacterId+1,calibration.Character,
                    new(calibration.Width,calibration.Height),calibration.Landing.Zone)&&
                !ScrollRouteProfiles.SelectableFor(imported,calibration.CharacterId,calibration.Character,
                    new(800,600),calibration.Landing.Zone)&&
                !ScrollRouteProfiles.SelectableFor(imported,calibration.CharacterId,calibration.Character,
                    new(calibration.Width,calibration.Height),calibration.Landing.Zone+1),
                "saved-profile selector crossed character, window or map boundaries");
            Reject(()=>ScrollRouteProfiles.Resolve(calibration.ClientHash,new(800,600),"Mimic",1,file),"stale window accepted shared click coordinates");
            ScrollRouteProfiles.Rename(imported.Id,"Shared destination",file,legacy);
            Require(ScrollRouteProfiles.AssignedId("Mimic",1,file,legacy)==imported.Id&&
                ScrollRouteProfiles.AssignedId("Pulkhan",2,file,legacy)==imported.Id&&
                ScrollRouteProfiles.Available(file,legacy).Single(p=>p.Id==imported.Id).Name=="Shared destination",
                "rename changed identity, duplicated the old import or lost route assignments");
            var newProfile=ScrollRouteProfiles.Create(calibration with{Destination="Second destination"},"Second destination",file);
            ScrollRouteProfiles.Assign(newProfile.Id,"Mimic",1,file,legacy);
            Require(ScrollRouteProfiles.Resolve(calibration.ClientHash,new(960,640),"Mimic",1,file)?.Destination=="Second destination"&&
                ScrollRouteProfiles.Resolve(calibration.ClientHash,new(960,640),"Pulkhan",2,file)?.Destination==calibration.Destination,
                "changing one route reassigned another route sharing the original profile");
            var copy=ScrollRouteProfiles.Create(calibration,"Second destination",file);
            Require(copy.Name!="Second destination","new calibration name collision discarded completed captures");
            string before=Hash(file);
            Reject(()=>ScrollRouteProfiles.Rename(newProfile.Id,"Shared destination",file,legacy),"duplicate compatible profile name accepted");
            Reject(()=>ScrollRouteProfiles.Rename(imported.Id,"bad\nname",file,legacy),"control characters entered a profile name");
            Reject(()=>ScrollRouteProfiles.Assign("missing","Mimic",0,file,legacy),"unknown profile assignment accepted");
            Reject(()=>ScrollRouteProfiles.Assign(imported.Id,"Mimic",3,file,legacy),"invalid saved route slot accepted");
            Require(Hash(file)==before,"rejected profile edit modified the library");
            // Re-read on each mutation: another recent profile must survive a
            // subsequent rename/assignment from an older UI selection.
            var latest=ScrollRouteProfiles.Create(calibration,"Recent profile",file);
            ScrollRouteProfiles.Rename(imported.Id,"Renamed shared destination",file,legacy);
            Require(ScrollRouteProfiles.Available(file,legacy).Any(p=>p.Id==latest.Id),"stale selection overwrote a recent profile");
            byte[] valid=File.ReadAllBytes(file);
            var current=JsonSerializer.Deserialize<ScrollRouteProfiles.Registry>(valid)!;
            foreach(var invalid in new[]{current with{Version=2},current with{Profiles=[..current.Profiles,current.Profiles[0]]},
                current with{Assignments=[..current.Assignments,current.Assignments[0]]},
                current with{Assignments=[new("Mimic",0,"missing")]},
                current with{Profiles=[current.Profiles[0] with{Calibration=calibration with{Scroll=null}}]},
                current with{Assignments=Enumerable.Range(0,ScrollRouteProfiles.MaximumAssignments+1).Select(i=>new ScrollRouteAssignment("Target "+i,0,imported.Id)).ToArray()}})
            {
                File.WriteAllBytes(file,JsonSerializer.SerializeToUtf8Bytes(invalid));string invalidHash=Hash(file);
                Reject(()=>ScrollRouteProfiles.Available(file,legacy),"corrupt/duplicate/oversized-format library read accepted");
                Reject(()=>ScrollRouteProfiles.Create(calibration,"Do not overwrite",file),"corrupt library overwritten");
                Require(Hash(file)==invalidHash,"invalid library bytes were not preserved");
            }
            File.WriteAllBytes(file,valid);
            var full=current with{Profiles=Enumerable.Range(0,ScrollRouteProfiles.MaximumProfiles).Select(i=>
                new NamedScrollRoute("profile-"+i,"Profile "+i,calibration)).ToArray(),Assignments=[]};
            File.WriteAllBytes(file,JsonSerializer.SerializeToUtf8Bytes(full));before=Hash(file);
            Reject(()=>ScrollRouteProfiles.Create(calibration,"One too many",file),"profile count limit accepted another capture");
            Require(Hash(file)==before,"profile count failure replaced the library");
            using(var oversized=new FileStream(file,FileMode.Create,FileAccess.Write)){oversized.SetLength(ScrollRouteProfiles.MaximumBytes+1L);}
            Reject(()=>ScrollRouteProfiles.Available(file,legacy),"oversized library was deserialized");
            File.Delete(file);Directory.CreateDirectory(file);bool failed=false;
            try{ScrollRouteProfiles.Create(calibration,"Write failure",file);}catch(IOException){failed=true;}catch(UnauthorizedAccessException){failed=true;}
            Require(failed&&Directory.Exists(file)&&!Directory.EnumerateFiles(directory,"*.tmp").Any(),"failed atomic replacement left partial state");
            Directory.Delete(file);
            Require(Hash(legacy)==legacyHash,"library operations modified the original captures");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"scroll-route-profile-checks.json"),JsonSerializer.Serialize(new
            {Passed=true,HardwareInputEmitted=false,ScrollConsumed=false,Checks=new[]{"legacy discovery without writes or recalibration",
                "one stored capture shared by two target/slot routes","exact route-scope resolution","saved scroll profile reused across client updates with character/window/map boundaries",
                "rename preserves captures and references","one assignment change leaves other routes unchanged","unique names preserve new captures",
                "rejected edits preserve bytes","recent profiles survive stale selections","corrupt/duplicate/missing-reference/count/size limits",
                "failed atomic replacement leaves no partial file","legacy calibration bytes unchanged"}},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally
        {
            if(Directory.Exists(file))Directory.Delete(file);
            foreach(var path in Directory.EnumerateFiles(directory))File.Delete(path);
            Directory.Delete(directory);
        }
    }
}
