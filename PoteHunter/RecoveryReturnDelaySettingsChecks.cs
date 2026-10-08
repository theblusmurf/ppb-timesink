using System.Text.Json;
using System.Text.Json.Nodes;

namespace PoteHunter;

internal static class RecoveryReturnDelaySettingsChecks
{
    static void Require(bool condition,string message)
    {if(!condition)throw new InvalidOperationException("Recovery return delay settings: "+message);}

    internal static void Run()
    {
        var legacy=JsonSerializer.Deserialize<Options>("{\"Target\":\"Mimic\",\"AutoRepairAfterDeath\":true,\"RevivalDelaySeconds\":27}")!;
        Require(!legacy.DelayReturnAfterRevival&&legacy.ReturnDelaySeconds==10&&legacy.Target=="Mimic"&&legacy.AutoRepairAfterDeath&&legacy.RevivalDelaySeconds==27,
            "older settings changed their recovery behavior or lost existing choices");
        using(var stream=typeof(Options).Assembly.GetManifestResourceStream("PoteHunter.DefaultSettings.json"))
        {
            Require(stream!=null,"embedded defaults are unavailable");
            var defaults=JsonSerializer.Deserialize<Options>(stream!)!;
            Require(!defaults.DelayReturnAfterRevival&&defaults.ReturnDelaySeconds==10,"fresh installs enabled a return wait or used a different duration");
        }
        foreach(var (saved,expected) in new[]{(int.MinValue,0),(0,0),(10,10),(87,87),(600,600),(int.MaxValue,600)})
        {
            var bounded=JsonSerializer.Deserialize<Options>("{\"DelayReturnAfterRevival\":true,\"ReturnDelaySeconds\":"+saved+"}")!;
            var restored=JsonSerializer.Deserialize<Options>(JsonSerializer.Serialize(bounded))!;
            Require(bounded.DelayReturnAfterRevival&&bounded.ReturnDelaySeconds==expected&&restored.DelayReturnAfterRevival&&restored.ReturnDelaySeconds==expected,
                "JSON duration bounds or enabled-choice round trip failed");
        }
        Require(!SettingsProfileStore.LocalFields.Contains(nameof(Options.DelayReturnAfterRevival))&&!SettingsProfileStore.LocalFields.Contains(nameof(Options.ReturnDelaySeconds)),
            "named profiles excluded return-delay behavior");

        string directory=Path.Combine(Path.GetTempPath(),"PlayPoteBot-return-delay-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store=new SettingsProfileStore(Path.Combine(directory,"profiles"));
            var configured=new Options{DelayReturnAfterRevival=true,ReturnDelaySeconds=87,RevivalDelaySeconds=27,AutoRepairAfterDeath=true};
            var local=new Options{Player="Local fixture",DelayReturnAfterRevival=false,ReturnDelaySeconds=5};
            store.Save("Delayed return",configured);
            var restored=store.Materialize("Delayed return",local);
            Require(restored.DelayReturnAfterRevival&&restored.ReturnDelaySeconds==87&&restored.RevivalDelaySeconds==27&&restored.AutoRepairAfterDeath&&restored.Player==local.Player,
                "named profile restored local delay choices instead of the saved recovery behavior");
            string export=Path.Combine(directory,"export.json");store.Export("Delayed return",export);
            var imported=new SettingsProfileStore(Path.Combine(directory,"imported"));imported.Import(export);
            restored=imported.Materialize("Delayed return",local);
            Require(restored.DelayReturnAfterRevival&&restored.ReturnDelaySeconds==87,"profile export/import lost the return delay");
            configured.DelayReturnAfterRevival=false;configured.ReturnDelaySeconds=600;store.Save("Immediate return",configured);
            restored=store.Read("Immediate return").Settings;
            Require(!restored.DelayReturnAfterRevival&&restored.ReturnDelaySeconds==600,"disabled toggle discarded its saved duration");

            var oldProfile=JsonNode.Parse(File.ReadAllText(export))!.AsObject();oldProfile["Name"]="Legacy recovery";
            var oldSettings=oldProfile["Settings"]!.AsObject();oldSettings.Remove(nameof(Options.DelayReturnAfterRevival));oldSettings.Remove(nameof(Options.ReturnDelaySeconds));
            File.WriteAllText(export,oldProfile.ToJsonString());imported.Import(export);
            restored=imported.Materialize("Legacy recovery",local);
            Require(!restored.DelayReturnAfterRevival&&restored.ReturnDelaySeconds==10&&restored.RevivalDelaySeconds==27&&restored.AutoRepairAfterDeath,
                "existing schema-2 profiles did not migrate to immediate return with a retained default duration");

            string settings=Path.Combine(directory,"settings.json");File.WriteAllText(settings,"original fixture settings");
            string backup=store.Activate("Delayed return",settings,local);
            restored=JsonSerializer.Deserialize<Options>(File.ReadAllText(settings))!;
            Require(File.ReadAllText(backup)=="original fixture settings"&&restored.DelayReturnAfterRevival&&restored.ReturnDelaySeconds==87,
                "profile activation lost the saved delay or original settings backup");
        }
        finally
        {
            foreach(string file in Directory.GetFiles(directory,"*",SearchOption.AllDirectories))File.Delete(file);
            foreach(string child in Directory.GetDirectories(directory).OrderByDescending(path=>path.Length))Directory.Delete(child);
            Directory.Delete(directory);
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"recovery-return-delay-settings-checks.json"),JsonSerializer.Serialize(new
        {
            Passed=true,HardwareInputEmitted=false,
            Checks=new[]{"legacy settings retain immediate return and existing revival/repair choices","embedded defaults off with ten-second duration",
                "zero to six-hundred second JSON bounds and round trip","named profiles restore return-delay behavior",
                "profile export/import and activation preserve delay and original backup","disabled toggle retains saved duration","existing schema-2 profile migration"}
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
