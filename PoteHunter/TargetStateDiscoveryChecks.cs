namespace PoteHunter;

public static class TargetStateDiscoveryChecks
{
    public static void RunAll()
    {
        const string hash="abc123";
        const string valid="""
        {"Passed":true,"HardwareInputEmitted":false,"ClientHash":"abc123","Fields":[
          {"ModuleRva":"0x483F44","Transitions":2,"TargetIds":[{"Id":"0x80000001"},{"Id":"0x80000002"}]},
          {"ModuleRva":"0x483F4C","Transitions":3,"TargetIds":[{"Id":"0x80000001"},{"Id":"0x80000002"}]}
        ]}
        """;
        var layout=TargetStateDiscovery.ParseLayout(valid,hash);
        Expect(layout.ModuleRvas.SequenceEqual(new uint[]{0x483f44,0x483f4c}),"two independently changing module fields accepted");
        Expect(new TargetStateSnapshot(true,"",[0x80000002,0x80000002]).Matches(0x80000002),"matching target fields accepted");
        Expect(!new TargetStateSnapshot(true,"",[0x80000001,0x80000002]).Matches(0x80000002),"mixed target fields rejected");
        Expect(!new TargetStateSnapshot(false,"",[0x80000002]).Matches(0x80000002),"unavailable state rejected");
        ExpectThrows(()=>TargetStateDiscovery.ParseLayout(valid,"different"),"different client hash rejected");
        ExpectThrows(()=>TargetStateDiscovery.ParseLayout(valid.Replace("\"Transitions\":2","\"Transitions\":1"),hash),"unproven field rejected");
    }

    static void Expect(bool value,string name){if(!value)throw new InvalidOperationException("Target state discovery check failed: "+name);}
    static void ExpectThrows(Action action,string name)
    {
        try {action();}catch(InvalidOperationException){return;}
        throw new InvalidOperationException("Target state discovery check failed: "+name);
    }
}
