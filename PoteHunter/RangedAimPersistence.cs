using System.Text.Json;

namespace PoteHunter;

public sealed record SavedRangedAimCalibration(
    DateTime TimeUtc,
    string ClientHash,
    string Character,
    RangedAimCalibration Calibration);

public static class RangedAimPersistence
{
    public static bool TryRestore(string path,World world,out Movement? movement,out string status)
    {
        movement=null;
        if(!File.Exists(path)){status="No saved 3D aim sensitivity is available yet.";return false;}
        try
        {
            var saved=JsonSerializer.Deserialize<SavedRangedAimCalibration>(File.ReadAllText(path));
            if(saved==null){status="Saved 3D aim sensitivity could not be read.";return false;}
            if(!string.Equals(saved.ClientHash,world.ClientHash,StringComparison.OrdinalIgnoreCase))
            {status="Saved 3D aim sensitivity belongs to a different client build.";return false;}
            string character=world.LocalPlayer().Name;
            if(!string.Equals(saved.Character,character,StringComparison.Ordinal))
            {status="Saved 3D aim sensitivity belongs to a different character.";return false;}
            movement=Movement.RestoreRangedCalibration(world,saved.Calibration);
            status="Restored verified 3D aim sensitivity from "+saved.TimeUtc.ToLocalTime().ToString("g")+".";
            return true;
        }
        catch(Exception ex){status="Saved 3D aim sensitivity was rejected: "+ex.Message;return false;}
    }

    public static void Save(string path,World world,Movement movement)
    {
        var saved=new SavedRangedAimCalibration(DateTime.UtcNow,world.ClientHash,world.LocalPlayer().Name,movement.ExportRangedAimCalibration());
        File.WriteAllText(path,JsonSerializer.Serialize(saved,new JsonSerializerOptions{WriteIndented=true}));
    }
}
