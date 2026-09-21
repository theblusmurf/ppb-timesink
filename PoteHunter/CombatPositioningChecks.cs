using System.Text.Json;

namespace PoteHunter;

public static class CombatPositioningChecks
{
    public static void Run()
    {
        static Entity Mob(uint id,double x,double y)=>new(100+id,0x80000000+id,"Lv. 1 Test",new(x,y),0,Generation:1);
        static void Check(bool passed,string why){if(!passed)throw new Exception("Combat positioning: "+why);}
        Vec player=new(0,0);var target=Mob(1,1,0);
        Entity[] surround=[target,Mob(2,-1,.8),Mob(3,-1,-.8)];
        var plan=CombatPositioning.Choose(player,target,surround,2,6,(_,_)=>true);
        Check(plan!=null && plan.Before==1 && plan.After>=2,"surrounded character gets a majority-facing destination");
        Check((plan!.Destination-player).Length<=2.5 && (plan.Destination-target.Position).Length<=2,"short move retains melee reach");
        Check(CombatPositioning.FrontCount(plan.Destination,target.Position,surround,6)==plan.After,"score matches destination");
        Check(CombatPositioning.Choose(plan.Destination,target,surround,2,6,(_,_)=>true)==null,"majority already in front does not circle");
        Entity[] front=[target,Mob(2,2,.3),Mob(3,2,-.3)];
        Check(CombatPositioning.Choose(player,target,front,2,6,(_,_)=>true)==null,"front cluster holds position");
        Check(CombatPositioning.Choose(player,target,[target],2,6,(_,_)=>true)==null,"single target unchanged");
        Entity[] sideStepTargets=[target,Mob(2,.5,.95),Mob(3,.5,.9)];
        var side=CombatPositioning.ChooseSideStep(player,target,sideStepTargets,6,SideStepCadence.Distance,_=>true);
        Check(side!=null && side.After>side.Before && side.After>=2,"side-step improves the attack cone");
        Check(CombatPositioning.ChooseSideStep(player,target,sideStepTargets,6,.5,_=>false)==null,"blocked side-step rejected");
        Check(CombatPositioning.CorrectionKey(new(),new(0,1),0)==Keys.S &&
            CombatPositioning.CorrectionKey(new(),new(1,0),0)==Keys.A &&
            CombatPositioning.CorrectionKey(new(),new(-1,0),0)==Keys.D,"S/A/D correction mapping");
        Check(CombatPositioning.Choose(player,target,[],2,6,(_,_)=>true)==null,"empty scene unchanged");
        Check(CombatPositioning.Choose(player,target,surround,2,6,(_,_)=>false)==null,"blocked routes yield no move");
        var bounded=CombatPositioning.Choose(player,target,surround,2,6,(_,to)=>to.Y>=0 && to.Length<=1.5);
        Check(bounded==null || bounded.Destination.Y>=0 && bounded.Destination.Length<=1.5,"boundary and avoidance callback honored");
        var tiny=CombatPositioning.Choose(player,target,surround,.5,6,(_,_)=>true);
        Check(tiny==null || (tiny.Destination-target.Position).Length<=.5,"small configured reach is not expanded");
        Check(CombatPositioning.Choose(new(double.NaN,0),target,surround,2,6,(_,_)=>true)==null,"bad position rejected");
        foreach(double bad in new[]{0d,-1d,double.NaN,double.PositiveInfinity})
        {
            Check(CombatPositioning.Choose(player,target,surround,bad,6,(_,_)=>true)==null,"invalid reach rejected");
            Check(CombatPositioning.Choose(player,target,surround,2,bad,(_,_)=>true)==null,"invalid radius rejected");
        }
        Check(CombatPositioning.Choose(player,target,surround.Skip(1).ToArray(),2,6,(_,_)=>true)==null,"missing locked identity rejected");
        var health=surround.ToDictionary(e=>e.Id,_=>new Health(50,100));
        var eligible=CombatPositioning.Eligible(surround,health,player,6,_=>true,(_,_)=>true);
        Check(eligible.Length==3,"living engaged candidates retained");
        Check(CombatPositioning.Eligible(surround,health,player,6,_=>false,(_,_)=>true).Length==0,"unengaged enemies excluded");
        Check(CombatPositioning.Eligible(surround,health,player,6,_=>true,(_,_)=>false).Length==0,"protected enemies excluded");
        health[surround[1].Id]=new(0,100);health.Remove(surround[2].Id);
        Check(CombatPositioning.Eligible(surround,health,player,6,_=>true,(_,_)=>true).Length==1,"dead and unknown HP excluded");
        Check(CombatPositioning.Eligible([target,target with {Generation=2}],health,player,6,_=>true,(_,_)=>true).Length==0,"ambiguous identity excluded");
        Check(CombatPositioning.Eligible([target,target],health,player,6,_=>true,(_,_)=>true).Length==1,"duplicate observations counted once");
        var npc=target with {Id=0x40000001};var person=target with {Id=7,Model="PC_MAN.GCMDS"};
        health[npc.Id]=health[person.Id]=new(100,100);
        Check(CombatPositioning.Eligible([npc,person,target with {Position=new(20,0)}],health,player,6,_=>true,(_,_)=>true).Length==0,"NPCs players and distant mobs excluded");
        var clock=new PositioningCadence();
        Check(clock.TryCheck(0) && !clock.TryCheck(749) && clock.TryCheck(750),"check cadence bounded");
        clock.Finished(800);
        Check(!clock.TryCheck(4799) && clock.TryCheck(4800),"attacks get four seconds between moves");
        clock.Reset();Check(clock.TryCheck(0),"new hunt resets cadence");
        var sideClock=new SideStepCadence();
        Check(sideClock.TryCheck(0) && !sideClock.TryCheck(699) && sideClock.TryCheck(700),"side-step cadence bounded");
        sideClock.Finish(700,true);Check(!sideClock.TryCheck(2499) && sideClock.TryCheck(2500),"side-step recovery pause bounded");
        var original=new Options{ContinuousCombatPositioning=false};
        Check(JsonSerializer.Deserialize<Options>(JsonSerializer.Serialize(original))?.ContinuousCombatPositioning==false,"disabled setting survives save");
        Check(JsonSerializer.Deserialize<Options>("{}")!.ContinuousCombatPositioning,"older settings enable new feature");
        Check(!JsonSerializer.Deserialize<Options>("{\"ContinuousPositioning\":false}")!.ContinuousCombatPositioning,"supplied version setting migrates");
        // Sweep rotated and translated scenes to avoid a quadrant-specific policy.
        for(int i=0;i<64;i++)
        {
            double angle=i*Math.PI/32;Vec origin=new(100,-80);
            var rotated=surround.Select(e=>e with {Position=origin+Movement.Rotate(e.Position,angle)}).ToArray();
            var candidate=CombatPositioning.Choose(origin,rotated[0],rotated,2,6,(_,_)=>true);
            Check(candidate!=null && candidate.After>rotated.Length/2 && (candidate.Destination-rotated[0].Position).Length<=2,"rotated cluster majority and reach");
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"combat-positioning-checks.json"),JsonSerializer.Serialize(new
        {
            Passed=true,HardwareInputEmitted=false,LiveGameTested=false,
            Checks=new[]{"surrounded cluster", "majority stops movement", "120 degree scoring", "melee reach", "maximum 2.5 unit move", "blocked paths", "hunt boundary callback", "single and empty scenes", "invalid inputs", "health and identity filters", "protected and unengaged targets excluded", "750ms checks and 4s rest", "settings roundtrip", "64 rotated and translated scenes"}
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}

