using System.Text.Json;
using PoteMemoryProbe;

namespace PoteHunter;

internal static class RangeSurvey
{
    internal static int Run()
    {
        WindowsClientRead.Enabled=true;
        try
        {
            using var world=new World();world.Connect();
            long until=Environment.TickCount64+30000;
            var snapshots=new List<object>();
            double monsterMaximum=0,chestMaximum=0;
            var seen=new HashSet<(uint,uint,long)>();
            var positions=new List<Vec>();
            do
            {
                var self=world.LocalPlayer();var entities=world.Poll();var health=world.HealthSnapshot();
                var monsters=entities.Where(e=>e.Monster && e.Position.Finite && health.GetValueOrDefault(e.Id) is {Known:true,Dead:false})
                    .Select(e=>new{e.Id,e.DisplayName,Distance=(e.Position-self.Position).Length,e.Position}).OrderByDescending(e=>e.Distance).ToArray();
                var chests=entities.Where(e=>Targeting.IsChest(e) && e.Position.Finite && !health.GetValueOrDefault(e.Id).Dead)
                    .Select(e=>new{e.Id,e.DisplayName,Distance=(e.Position-self.Position).Length,e.Position}).OrderByDescending(e=>e.Distance).ToArray();
                foreach(var entity in entities)seen.Add((entity.Id,entity.Generation,entity.Address));
                monsterMaximum=Math.Max(monsterMaximum,monsters.FirstOrDefault()?.Distance??0);
                chestMaximum=Math.Max(chestMaximum,chests.FirstOrDefault()?.Distance??0);
                positions.Add(self.Position);
                snapshots.Add(new{TimeUtc=DateTime.UtcNow,PlayerPosition=self.Position,LoadedObjects=entities.Count,Monsters=monsters.Length,Chests=chests.Length,FarthestMonsters=monsters.Take(3),FarthestChests=chests.Take(3)});
                Thread.Sleep(500);
            }while(Environment.TickCount64<until);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"loaded-range-survey.json"),JsonSerializer.Serialize(new
            {
                Passed=true,HardwareInputEmitted=false,Units="bot/game units",world.ClientHash,
                MaximumObservedMonsterDistance=monsterMaximum,MaximumObservedChestDistance=chestMaximum,
                UniqueLoadedObjects=seen.Count,PlayerDisplacement=(positions[^1]-positions[0]).Length,
                Note="Observed active-client data only; this is not a proven server visibility ceiling. No hunt-radius or display-zoom filter was applied.",Snapshots=snapshots
            },new JsonSerializerOptions{WriteIndented=true}));
            return 0;
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"loaded-range-survey.json"),JsonSerializer.Serialize(new{Passed=false,Error=ex.ToString()}));
            return 1;
        }
    }
}
