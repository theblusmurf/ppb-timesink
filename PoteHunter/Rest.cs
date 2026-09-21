namespace PoteHunter;

public enum RestPosture { Unknown, Standing, SittingDown, Resting, StandingUp }
public sealed record RestReading(RestPosture Posture, byte Flag=0, uint Animation=0)
{
    public static RestReading Decode(byte flag,uint animation) => new(
        flag>1 ? RestPosture.Unknown : animation==11 ? RestPosture.SittingDown : animation==12 ? RestPosture.StandingUp :
        flag==1 && animation==10 ? RestPosture.Resting : flag==0 && animation!=10 ? RestPosture.Standing : RestPosture.Unknown,flag,animation);
}
public enum RestCommand { Wait, Toggle, Complete }

// One request emits at most one C edge. An ignored input never becomes a blind repeated toggle.
public sealed class RestToggle(bool wantRest,long started)
{
    bool sent;
    public RestCommand Next(RestReading reading,long now)
    {
        if(reading.Posture==RestPosture.Unknown) throw new InvalidOperationException("Rest state could not be verified; stopped before toggling C.");
        if(now-started>=4500) throw new InvalidOperationException(wantRest ? "The game did not confirm resting after C; stopped." : "The game did not confirm standing after C; stopped.");
        if(reading.Posture==(wantRest?RestPosture.Resting:RestPosture.Standing)) return RestCommand.Complete;
        if(reading.Posture is RestPosture.SittingDown or RestPosture.StandingUp || sent) return RestCommand.Wait;
        sent=true;return RestCommand.Toggle;
    }

    public static bool SafeToRest(Vec position,IEnumerable<Entity> entities,IReadOnlyDictionary<uint,Health> health,
        IReadOnlyList<AvoidZone> zones,double nearbyRadius,long now,long lastDamageAt) =>
        now-lastDamageAt>=3000 && Avoidance.BlockedPoint(position,zones,RetreatPlanner.Clearance)==null &&
        !entities.Any(e=>e.Monster && !health.GetValueOrDefault(e.Id).Dead && (e.Position-position).Length<=Math.Max(12,nearbyRadius+3));

    public static void SelfTest()
    {
        var standing=RestReading.Decode(0,0);var sitting=RestReading.Decode(1,11);
        var rest=RestReading.Decode(1,10);var rising=RestReading.Decode(0,12);
        var enter=new RestToggle(true,0);
        if(enter.Next(standing,0)!=RestCommand.Toggle || enter.Next(standing,100)!=RestCommand.Wait ||
            enter.Next(sitting,500)!=RestCommand.Wait || enter.Next(rest,1200)!=RestCommand.Complete) throw new Exception("Rest entry did not follow the observed live transition or repeated C.");
        var exit=new RestToggle(false,2000);
        if(exit.Next(rest,2000)!=RestCommand.Toggle || exit.Next(rising,2500)!=RestCommand.Wait || exit.Next(standing,3600)!=RestCommand.Complete) throw new Exception("Rest exit resumed before standing completed.");
        if(new RestToggle(false,0).Next(standing,0)!=RestCommand.Complete || new RestToggle(true,0).Next(rest,0)!=RestCommand.Complete) throw new Exception("Already-correct posture toggled C.");
        bool timedOut=false;try{enter.Next(standing,4500);}catch(InvalidOperationException){timedOut=true;}
        if(!timedOut)throw new Exception("Ignored C had no bounded timeout.");
        bool unknown=false;try{new RestToggle(true,0).Next(RestReading.Decode(2,0),0);}catch(InvalidOperationException){unknown=true;}
        if(!unknown)throw new Exception("Unknown rest flag allowed C.");
        var monster=new Entity(1,0x80000001,"Lv. 1 Villager",new(5,0),0);
        var dead=new Dictionary<uint,Health>{{monster.Id,new(0,100)}};
        if(SafeToRest(new(),[monster],new Dictionary<uint,Health>(),[],3,10000,0) ||
            !SafeToRest(new(),[monster],dead,[],3,10000,0) ||
            SafeToRest(new(),[],dead,[],3,10000,8000) ||
            SafeToRest(new(),[],dead,[new(new(5,0),5,"Knight")],3,10000,0)) throw new Exception("Rest safety ignored living/unknown enemies, recent damage, or named threat.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"rest-checks.json"),System.Text.Json.JsonSerializer.Serialize(new {Passed=true,Checks=new[]{"observed 0-11-10-12-0 posture sequence","one C per requested transition","already standing/resting does not toggle","animation completion before movement","ignored input timeout","invalid flag rejected","nearby live/unknown HP enemy prevents rest","dead enemy ignored","recent damage and avoid buffer prevent rest"}},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    }

    public static void CheckRecorded(string path)
    {
        var transitions=new List<(DateTime Time,RestReading Reading)>();
        foreach(var line in File.ReadLines(path))
        {
            using var doc=System.Text.Json.JsonDocument.Parse(line);
            var bytes=Convert.FromHexString(doc.RootElement.GetProperty("Creature").GetString()!);
            var reading=RestReading.Decode(bytes[0x24c],BitConverter.ToUInt32(bytes,0x1b8));
            if(transitions.Count==0 || transitions[^1].Reading.Posture!=reading.Posture)
                transitions.Add((doc.RootElement.GetProperty("TimeUtc").GetDateTime(),reading));
        }
        RestPosture[] expected=[RestPosture.Standing,RestPosture.SittingDown,RestPosture.Resting,RestPosture.StandingUp,RestPosture.Standing];
        if(!transitions.Select(t=>t.Reading.Posture).Take(5).SequenceEqual(expected)) throw new Exception("Recorded manual C test did not match the expected posture sequence.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"rest-recorded-checks.json"),System.Text.Json.JsonSerializer.Serialize(new {Passed=true,Source=Path.GetFullPath(path),Method="Read-only replay of user-confirmed C rest/stand; no hardware input",Transitions=transitions.Select(t=>new {t.Time,Posture=t.Reading.Posture.ToString(),t.Reading.Flag,t.Reading.Animation})},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    }
}
