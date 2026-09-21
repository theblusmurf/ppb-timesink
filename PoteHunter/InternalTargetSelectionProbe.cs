using System.Text.Json;
using PoteMemoryProbe;

namespace PoteHunter;

internal static class InternalTargetSelectionProbe
{
    internal static readonly long[] CrosshairCandidates=[0x165FAE90,0x165FAF90,0x165FAFE4];
    internal static int Run()
    {
        WindowsClientRead.Enabled=true;
        try
        {
            using var world=new World();world.Connect();
            var beforeNeighborhood=world.TargetStateNeighborhood();
            var target=world.Poll().Where(entity=>entity.Monster&&entity.Targetable&&entity.Position.Finite)
                .Where(entity=>world.TargetHealth(entity.Id) is {Known:true,Dead:false})
                .OrderBy(entity=>(entity.Position-world.PlayerPosition()).Length).ThenBy(entity=>entity.Id).FirstOrDefault();
            if(target==null)throw new InvalidOperationException("No living loaded monster is available for the internal target-selection probe.");
            var result=world.TrySelectTargetInternally(target);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"internal-target-selection.json"),JsonSerializer.Serialize(new
            {
                TimeUtc=DateTime.UtcNow,HardwareInputEmitted=false,world.ClientHash,world.Pid,
                Target=new {target.Id,target.DisplayName,target.Generation,target.Address,target.Position},beforeNeighborhood,
                result,afterNeighborhood=world.TargetStateNeighborhood()
            },new JsonSerializerOptions {WriteIndented=true}));
            return result.Applied?0:1;
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"internal-target-selection.json"),JsonSerializer.Serialize(new {TimeUtc=DateTime.UtcNow,HardwareInputEmitted=false,Passed=false,Error=ex.ToString()},new JsonSerializerOptions {WriteIndented=true}));
            return 1;
        }
    }

    internal static int RunCandidateTest()
    {
        WindowsClientRead.Enabled=true;
        try
        {
            using var world=new World();world.Connect();
            var target=world.Poll().Where(entity=>entity.Monster&&entity.Targetable&&entity.Position.Finite)
                .Where(entity=>world.TargetHealth(entity.Id) is {Known:true,Dead:false})
                .OrderBy(entity=>(entity.Position-world.PlayerPosition()).Length).ThenBy(entity=>entity.Id).FirstOrDefault();
            if(target==null)throw new InvalidOperationException("No living loaded monster is available for the candidate-field test.");
            var before=CrosshairCandidates.ToDictionary(address=>$"0x{address:X8}",world.ReadTargetStateWord);
            var result=world.TrySelectTargetWithCandidates(target,CrosshairCandidates);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"internal-target-candidate-selection.json"),JsonSerializer.Serialize(new
            {
                TimeUtc=DateTime.UtcNow,HardwareInputEmitted=false,world.ClientHash,world.Pid,
                Target=new {target.Id,target.DisplayName,target.Generation,target.Address,target.Position},before,result
            },new JsonSerializerOptions {WriteIndented=true}));
            return result.Applied?0:1;
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"internal-target-candidate-selection.json"),JsonSerializer.Serialize(new {TimeUtc=DateTime.UtcNow,HardwareInputEmitted=false,Passed=false,Error=ex.ToString()},new JsonSerializerOptions {WriteIndented=true}));
            return 1;
        }
    }
}
