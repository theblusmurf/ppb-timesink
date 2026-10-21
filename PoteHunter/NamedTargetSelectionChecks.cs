using System.Text.Json;

namespace PoteHunter;

internal static class NamedTargetSelectionChecks
{
    internal static void Run()
    {
        static void Require(bool condition,string message)
        {if(!condition)throw new Exception(message);}
        var selected=new Entity(100,0x80001755,"Pulkhan",new(1,0),0,Model:"mon_pulkhan02.GCMDS");
        var other=new Entity(200,0x80001754,"Tribal",new(.5,0),0,Model:"MON_SsangNom.GCMDS");
        var living=new Health(100,100);
        Require(selected.Monster,"Named-target fixture was not a verified monster.");
        Require(Targeting.Eligible(selected,living,Threat.Unknown,"Pulkhan",[]),
            "A selected named monster was excluded when no color restriction was selected.");
        Require(!Targeting.Eligible(other,living,Threat.Unknown,"Pulkhan",[]),"Name-only selection admitted an unrelated monster.");
        Require(!Targeting.Eligible(selected,living,Threat.Unknown,"",[]) &&
            !Targeting.Eligible(selected,living,Threat.Unknown,"  ",[]),"An empty selection admitted every monster.");
        Require(!Targeting.Eligible(selected,living,Threat.Red,"Pulkhan",["Green"]) &&
            !Targeting.Eligible(selected,living,Threat.Unknown,"Pulkhan",["Green"]) &&
            Targeting.Eligible(selected,living,Threat.Green,"Pulkhan",["Green"]),"Explicit color restrictions were bypassed.");
        Require(!Targeting.Eligible(selected,default,Threat.Unknown,"Pulkhan",[]) &&
            !Targeting.Eligible(selected,new(0,100),Threat.Unknown,"Pulkhan",[]),"Unknown or dead HP was approved.");
        Require(!Targeting.Eligible(selected with{Id=1,Model=""},living,Threat.Unknown,"Pulkhan",[]),"A player with a matching name was approved.");
        var hp=new Dictionary<uint,Health>{{selected.Id,living},{other.Id,living}};
        Entity? Choose(Entity target)=>Targeting.Choose([target,other],hp,new(),new(),20,_=>Threat.Unknown,"Pulkhan",[]);
        Require(Choose(selected)?.Id==selected.Id && Choose(selected with{Position=new(21,0)})==null,
            "Named selection failed or expanded the hunt boundary.");
        TargetSearchReport Explain(Entity target,Func<Entity,Health,string?> guard)=>TargetSearch.Explain([target,other],hp,
            new(),new(),20,60,_=>Threat.Unknown,"Pulkhan",[],false,guard);
        var available=Explain(selected,(_,_)=>null);
        Require(available.ApprovedAlive==1 && available.Candidates.Single().Reason=="Available",
            "Search diagnostics disagree with named target selection.");
        Require(Explain(selected,(_,_)=>"Player nearby").Protected==1 &&
            Explain(selected with{Position=new(21,0)},(_,_)=>null).OutsideArea==1,
            "Named-target diagnostics lost ownership or boundary exclusions.");
        hp.Remove(selected.Id);
        Require(Explain(selected,(_,_)=>null).Unreadable==1,"Named-target diagnostics lost unreadable HP.");
        var empty=TargetSearch.Explain([selected,other],hp,new(),new(),20,60,_=>Threat.Unknown,"",[],false,(_,_)=>null);
        Require(empty.Candidates.Length==0 && empty.Message.Contains("enter a target name"),"Empty selection did not explain how to select targets.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"named-target-selection-checks.json"),
            JsonSerializer.Serialize(new{Passed=true,NameOnlyWithoutColors=true,ExplicitColorsRetained=true,
                BlankSelectionRejected=true,HealthIdentityBoundaryAndProtectionRetained=true,HardwareInputEmitted=false}));
    }
}
