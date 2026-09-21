using System.Text.RegularExpressions;
namespace PoteHunter;

public sealed record ActiveEffect(int Index,string Name,string Description,ushort Magnitude,ushort RawSeconds)
{
    public bool Active=>Magnitude!=0;
    public int RemainingSeconds=>Math.Max(0,RawSeconds-1);
}
public sealed record ActiveEffectSnapshot(bool Available,string Status,IReadOnlyList<ActiveEffect> Effects)
{
    public ActiveEffect? Match(string skillName)
    {
        var matches=Effects.Where(e=>Normalize(e.Name)==Normalize(skillName)).Take(2).ToArray();
        return Available && matches.Length==1?matches[0]:null;
    }
    public static string Normalize(string name)=>Regex.Replace(name.Trim(),@"\s+Lv\.?\s*\d+\s*$","",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant).Trim().ToUpperInvariant();
    public static ActiveEffectSnapshot Decode(byte[] data,IReadOnlyList<(string Name,string Description)> names)
    {
        if(data.Length!=0x138 || names.Count!=77)return new(false,"Active-effect data is incomplete",[]);
        return new(true,"Live local-character effects",names.Select((n,i)=>new ActiveEffect(i+1,n.Name,n.Description,
            BitConverter.ToUInt16(data,i*2),BitConverter.ToUInt16(data,0x9c+i*2))).ToArray());
    }
    public static void SelfTest()
    {
        var names=Enumerable.Range(1,77).Select(i=>(Name:$"Effect {i}",Description:"")).ToArray();
        names[8]=("Encourage","");names[11]=("Harden Skin","");names[7]=("Regeneration","");
        var data=new byte[0x138];
        BitConverter.GetBytes((ushort)53).CopyTo(data,8*2);BitConverter.GetBytes((ushort)19).CopyTo(data,0x9c+8*2);
        var snap=Decode(data,names);
        if(snap.Match("Encourage Lv.1") is not{Active:true,RemainingSeconds:18} || snap.Match("Regeneration Lv.1") is not{Active:false})throw new Exception("Active buff decoding failed");
        data[0x9c+8*2]=0;data[0x9c+8*2+1]=0;
        if(Decode(data,names).Match("Encourage") is not{Active:true,RemainingSeconds:0})throw new Exception("A zero timer incorrectly removed a still-present effect");
        data[8*2]=0;data[8*2+1]=0;
        if(Decode(data,names).Match("Encourage") is not{Active:false})throw new Exception("Effect removal not detected");
        if(Decode([],names).Available || new ActiveEffectSnapshot(false,"unknown",snap.Effects).Match("Encourage")!=null)throw new Exception("Unknown effect state treated as valid");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"active-effects-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{Passed=true,Checks=new[]{"live unsigned-short layout","level-independent name matching","display timer minus one","zero time does not imply removal","magnitude removal","unavailable is unknown"}}));
    }
}
