using System.Text.Json;

namespace PoteHunter;

internal static class SentinelDisplayNumberChecks
{
    internal static void Run()
    {
        var local=new Entity(100,1,"Local",default,0,Model:"PC_MAN.GCMDS");
        var context=new SentinelAlertContext("client",SentinelPlayerIdentity.Of(local),8);
        var first=new Entity(200,2,"",new(1,0),0,Generation:1,Model:"PC_Akhan_A.GCMDS");
        var second=first with{Address=300,Id=3,Name="Second",Position=new(20,0)};
        var labels=new SentinelDisplayNumbers();
        void Require(bool value,string error){if(!value)throw new Exception("Sentinel identification: "+error);}
        void At(long now,params Entity[] players)=>labels.Update(true,true,context,players,now);
        At(0,first,second);int one=labels.Number(first),two=labels.Number(second);
        Require(one>0&&two>0&&one!=two,"different observed identities need distinct labels");
        At(100,second with{Position=new(.1,0)},first with{Position=new(99,0)});
        Require(labels.Number(first)==one&&labels.Number(second)==two,"crossing distance order or leaving display range renumbered players");
        At(200);labels.Update(true,false,context,[],300);At(1000,second,first);
        Require(labels.Number(first)==one&&labels.Number(second)==two,"brief empty or stale readings changed labels");
        var replaced=first with{Address=201,Generation=2};At(1100,replaced,second);
        Require(labels.Number(replaced)>0&&labels.Number(replaced)!=one,"a replacement body inherited another identity's label");
        var modelChanged=replaced with{Model="PC_Akhan_B.GCMDS"};At(1200,modelChanged,second);
        Require(labels.Number(modelChanged)!=labels.Number(replaced),"model replacement inherited a label");
        labels.Reset();At(0,local,first,first with{Generation=2},second with{Model="NPC_MAN.GCMDS"});
        Require(labels.Count==0,"self, ambiguous UID or unknown player body acquired a label");
        At(100,first);At(30101);
        Require(labels.Count==0,"absent identities outlived bounded retention");
        At(30200,first);labels.Update(true,true,context with{Zone=12},[second],30300);
        Require(labels.Number(first)==0&&labels.Number(second)==1,"zone change retained previous labels");
        labels.Update(false,false,context,[],30400);Require(labels.Count==0,"disconnect retained labels");
        var crowd=Enumerable.Range(2,600).Select(id=>first with{Id=(uint)id,Address=id*100,Generation=(uint)id}).ToArray();
        At(40000,crowd);Require(labels.Count==SentinelDisplayNumbers.Capacity,"large crowds exceeded memory bounds");
        var visible=crowd.Where(p=>labels.Number(p)>0).Select(labels.Number).ToArray();
        Require(visible.Length==visible.Distinct().Count(),"bounded registry reused a current label");
        Require(PlayerRecognition.DisplayName(1981,"")=="Player 000007BD"&&PlayerRecognition.DisplayName(1981,"  Named player  ")=="Named player",
            "verified names and explicit UID fallback are inconsistent");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"sentinel-identification-checks.json"),JsonSerializer.Serialize(new
        {Passed=true,HardwareInputEmitted=false,AudioPlayed=false,Checks=new[]{"stable distance/range labels","brief read gaps","full identity replacement",
            "duplicate UID/self/model exclusion","bounded retention/capacity","zone/disconnect reset","verified name or explicit UID fallback"}},new JsonSerializerOptions{WriteIndented=true}));
    }
}
