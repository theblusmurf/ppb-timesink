using System.Text.Json;
namespace PoteHunter;

public static class TightGatheringChecks
{
    public static void Run()
    {
        static Entity Mob(uint n,double x,double y)=>new(100+n,0x80000000+n,"Lv. 1 Gather",new(x,y),0,Generation:1);
        static void Check(bool ok,string why){if(!ok)throw new Exception("Tight gathering: "+why);}
        Vec player=new(0,0);var target=Mob(1,.4,0);
        Entity[] gathered=[target,Mob(2,.3,.2),Mob(3,.3,-.2)];
        Check(TightGathering.CloseCount(player,gathered)==3 && TightGathering.Ready(player,target,gathered),"close forward majority ready");
        Entity[] edge=[target,Mob(2,.5,0),Mob(3,.5001,0)];
        Check(TightGathering.CloseCount(player,edge)==2,"0.5 included and 0.5001 excluded");
        Entity[] pairwise=[target,Mob(2,.4,.3),Mob(3,.4,-.3)];
        Check((pairwise[1].Position-pairwise[2].Position).Length>.5 && TightGathering.Ready(player,target,pairwise),"radius is from player, not pairwise spacing");
        Entity[] opposite=[target,Mob(2,-.4,0),Mob(3,-.3,.1)];
        Check(TightGathering.CloseCount(player,opposite)==3 && !TightGathering.Ready(player,target,opposite),"close bodies behind character not a forward majority");
        Entity[] tied=[target,Mob(2,-.4,0)];
        Check(!TightGathering.Ready(player,target,tied),"half is not a majority");
        Check(!TightGathering.Ready(player,target,[target]),"single monster uses normal combat");
        Entity[] cluster=[Mob(1,1,0),Mob(2,1.1,.1),Mob(3,1.1,-.1)];
        var plan=TightGathering.Choose(player,cluster[0],cluster,2,(_,_)=>true);
        Check(plan!=null && plan.Front==3 && plan.Close==3,"finds gathering point for a compact cluster");
        Check((plan!.Destination-player).Length<=2.5 && (plan.Destination-cluster[0].Position).Length<=2,"step and attack reach bounded");
        Check(TightGathering.Choose(player,cluster[0],cluster,2,(_,_)=>false)==null,"blocked points rejected");
        var bounded=TightGathering.Choose(player,cluster[0],cluster,2,(_,p)=>p.X<=.25 && p.Y>=0);
        Check(bounded==null || bounded.Destination.X<=.25 && bounded.Destination.Y>=0,"safe-path boundary respected");
        var near=TightGathering.Choose(player,cluster[0],cluster,.5,(_,_)=>true);
        Check(near!=null && (near.Destination-cluster[0].Position).Length<=.5,"tight configured melee reach preserved");
        foreach(double invalid in new[]{0d,-1d,double.NaN,double.PositiveInfinity})
            Check(TightGathering.Choose(player,cluster[0],cluster,invalid,(_,_)=>true)==null,"invalid reach rejected");
        Check(TightGathering.Choose(new(double.NaN,0),target,gathered,2,(_,_)=>true)==null,"invalid player rejected");
        Check(TightGathering.Choose(player,target,[target],2,(_,_)=>true)==null,"single planner disabled");
        Check(TightGathering.Choose(player,target with{Id=0x80000042,Address=666},cluster,2,(_,_)=>true)==null,"different target identity rejected");

        var hp=cluster.ToDictionary(e=>e.Id,_=>new Health(50,100));
        var far=cluster[2] with {Position=new(20,0)};
        var observed=TightGathering.ObserveRoster(cluster,[cluster[0],cluster[1],far],hp,(_,_)=>true);
        Check(observed?.Length==3 && !TightGathering.Ready(player,cluster[0],observed),"leaving nearby radius does not shrink denominator");
        Check(TightGathering.ObserveRoster(cluster,cluster.Take(2),hp,(_,_)=>true)==null,"missing member aborts attempt");
        Check(TightGathering.ObserveRoster(cluster,[cluster[0],cluster[1],cluster[2] with {Generation=2}],hp,(_,_)=>true)==null,"reused member aborts attempt");
        Check(TightGathering.ObserveRoster(cluster,cluster,hp,(e,_)=>e.Id!=cluster[2].Id)==null,"new protection aborts attempt");
        hp.Remove(cluster[2].Id);
        Check(TightGathering.ObserveRoster(cluster,cluster,hp,(_,_)=>true)==null,"unknown health aborts attempt");
        hp[cluster[2].Id]=new(0,100);
        Check(TightGathering.ObserveRoster(cluster,cluster,hp,(_,_)=>true)?.Length==2,"confirmed dead original member removed");
        Check(TightGathering.ObserveRoster(cluster,[cluster[0],cluster[1],cluster[2] with {Generation=2}],hp,(_,_)=>true)==null,"dead replacement cannot erase original roster member");
        var newcomer=Mob(4,.2,0);hp[newcomer.Id]=new(50,100);
        Check(TightGathering.ObserveRoster(cluster,cluster.Append(newcomer),hp,(_,_)=>true)?.Length==2,"newcomer does not join active gathering attempt");

        var session=new TightGatherSession(0,new(1,0));
        Check(session.Next(0,player,cluster[0],cluster)==GatherAction.Move,"move phase begins");
        Check(session.Next(1799,player,cluster[0],cluster)==GatherAction.Move,"move phase bounded");
        Check(session.Next(1800,player,cluster[0],cluster)==GatherAction.Wait,"movement timeout stops and waits");
        Check(session.Next(2999,player,cluster[0],cluster)==GatherAction.Wait,"settle phase bounded");
        Check(session.Next(3000,player,cluster[0],cluster)==GatherAction.GiveUp,"cannot gather forever");
        session=new(0,player);
        Check(session.Next(0,player,cluster[0],cluster)==GatherAction.Wait && session.Next(1200,player,cluster[0],cluster)==GatherAction.GiveUp,"stationary waits only 1.2 seconds");
        session=new(0,new(1,0));
        Check(session.Next(0,player,cluster[0],cluster)==GatherAction.Move,"replanning session starts with movement");
        session.SetDestination(new(2,0));
        Check(session.Next(1200,player,cluster[0],cluster)==GatherAction.Move,"replanning resets the settle timer");
        Check(session.Next(1800,player,cluster[0],cluster)==GatherAction.Wait,"replanned destination settles after movement window");
        session=new(0,player);
        Check(session.Next(0,player,target,gathered)==GatherAction.Attack,"ready group releases attack delay immediately");
        Check(new TightGatherSession(100,player).Next(99,player,cluster[0],cluster)==GatherAction.GiveUp,"backward clock aborts");
        var clock=new TightGatherCadence();Check(clock.TryCheck(0) && !clock.TryCheck(749) && clock.TryCheck(750),"check throttle");
        clock.Finish(1000,false);Check(!clock.TryCheck(15999) && clock.TryCheck(16000),"failed gather gets 15 seconds of combat");
        clock.Finish(17000,true);Check(!clock.TryCheck(22999) && clock.TryCheck(23000),"success gets six seconds before retry");
        clock.Reset();Check(clock.TryCheck(0),"new hunt resets gather cadence");
        var options=JsonSerializer.Deserialize<Options>(JsonSerializer.Serialize(new Options{TightGathering=false,ContinuousCombatPositioning=true}))!;
        Check(!options.TightGathering && options.ContinuousCombatPositioning,"gather switch independent of positioning switch");
        Check(JsonSerializer.Deserialize<Options>("{}")!.TightGathering,"old settings default to requested gathering mode");
        for(int i=0;i<32;i++)
        {
            Vec origin=new(100,-50);double angle=i*Math.PI/16;
            var rotated=cluster.Select(e=>e with{Position=origin+Movement.Rotate(e.Position,angle)}).ToArray();
            var choice=TightGathering.Choose(origin,rotated[0],rotated,2,(_,_)=>true);
            Check(choice!=null && choice.Front>rotated.Length/2 && (choice.Destination-origin).Length<=2.5,"rotated cluster gathering geometry");
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"tight-gathering-checks.json"),JsonSerializer.Serialize(new
        {
            Passed=true,HardwareInputEmitted=false,LiveGameTested=false,Radius=.5,
            Checks=new[]{"player-centered inclusive radius", "forward strict majority", "pairwise spacing differs from radius", "safe compact cluster point", "reach and step limits", "fixed roster", "dead versus missing health", "identity reuse", "protected members", "no added newcomers", "move/wait/attack/timeout phases", "failure and success cooldowns", "settings persistence", "32 rotated scenes"}
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}

