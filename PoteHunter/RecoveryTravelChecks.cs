using System.Text.Json;

namespace PoteHunter;

internal static class RecoveryTravelChecks
{
    static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    static SavedNavigationRoute Route(params Vec[] points)=>new(5,points[0],1,points,DateTime.UnixEpoch,"Test",10,5);
    static void Invalid(Action action)
    {
        bool failed=false;try{action();}catch(RouteUnavailableException){failed=true;}
        Require(failed,"Invalid/disconnected recovery route was accepted.");
    }
    public static async Task Run()
    {
        var primary=Route(new(0,12),new(0,6),new(0,0));
        var alternative=Route(new(12,0),new(6,0),new(0,0));
        var second=Route(new(-12,0),new(-6,0),new(0,0));
        SavedNavigationRoute?[] routes=[primary,alternative,second];
        Require(RecoveryTravel.StartupSlot(routes,new(10,9),5,"Test",10,0)==0,"Ten-unit segment corridor was not accepted at its boundary.");
        Require(RecoveryTravel.StartupSlot([primary],new(10.01,9),5,"Test",10,0)==-1,"Startup joined outside the ten-unit corridor.");
        Require(RecoveryTravel.StartupSlot(routes,new(0,0),5,"Test",10,2)==2,"Selected compatible route was not preferred.");
        Require(RecoveryTravel.StartupSlot([primary],new(0,6),6,"Test",10,0)==-1 &&
            RecoveryTravel.StartupSlot([primary],new(0,6),5,"Other",10,0)==-1 &&
            RecoveryTravel.StartupSlot([primary],primary.Anchor,5,"Test",20,0)==-1,"Startup accepted a different map, character, or endpoint floor.");
        var sloped=primary with{Height=170};
        Require(RecoveryTravel.StartupSlot([sloped],sloped.RevivalOrigin,5,"Test",96,0)==0 &&
            RecoveryTravel.StartupSlot([sloped],new(0,6),5,"Test",96,0)==0,
            "A recorded uphill path was rejected because its endpoint differs from the current elevation.");
        Require(RecoveryTravel.StartupSlot([sloped],new(2.5,12),5,"Test",96,0)==-1 &&
            RecoveryTravel.StartupSlot([sloped],new(2.5001,12),5,"Test",96,0)==0 &&
            RecoveryTravel.StartupSlot([sloped],sloped.Anchor,5,"Test",168,0)==-1 &&
            RecoveryTravel.StartupSlot([sloped],sloped.Anchor,5,"Test",168.001,0)==0,
            "Endpoint position or two-unit floor boundaries changed during route joining.");
        Require(RecoveryTravel.StartupSlot([sloped],new(10.01,6),5,"Test",96,0)==-1 &&
            RecoveryTravel.StartupSlot([sloped],sloped.RevivalOrigin,6,"Test",96,0)==-1 &&
            RecoveryTravel.StartupSlot([sloped],sloped.RevivalOrigin,5,"Other",96,0)==-1 &&
            RecoveryTravel.StartupSlot([sloped],sloped.RevivalOrigin,5,"Test",double.NaN,0)==-1,
            "Uphill joining bypassed the corridor, map, character, or valid current-height guards.");
        Require(RecoveryRouting.CompatibleIdentity(sloped,5,"Test") &&
            !RecoveryRouting.Compatible(sloped,5,"Test",96) &&
            RecoveryRouting.SavedReturnProblem(sloped,5,"Test",170,sloped.Anchor)==null,
            "Path identity eligibility weakened the saved endpoint floor validation.");
        Require(RecoveryRouting.Compatible(sloped,5,"Test",170)&&
            !RecoveryRouting.Compatible(sloped,5,"Test",96)&&!RecoveryRouting.Compatible(sloped,5,"Test",double.NaN),
            "Live arrival at the endpoint accepted a different or unreadable floor.");
        var segmentJoin=RecoveryTravel.Plan([primary],primary,new(10,9),false);
        Require(segmentJoin.Points.Contains(new Vec(0,9)) && segmentJoin.Points.Last()==primary.Anchor &&
            !segmentJoin.Points.Contains(primary.RevivalOrigin),"Startup failed to join the path segment and travel toward its anchor.");
        var switchPlan=RecoveryTravel.Plan(routes,alternative,primary.Anchor,false);
        Require(switchPlan.Points.SequenceEqual(new Vec[]{new(0,12),new(0,6),new(0,0),new(6,0),new(12,0)}) && switchPlan.Destination==alternative.Anchor,
            "Fallback cut across farm anchors instead of following both recorded paths through their shared origin.");
        var retreat=RecoveryTravel.Plan(routes,primary,alternative.Anchor,true);
        Require(retreat.Points.SequenceEqual(new Vec[]{new(12,0),new(6,0),new(0,0)})&&retreat.Destination==primary.RevivalOrigin,
            "All occupied fallback failed to reverse the current route to its origin.");
        var resumed=RecoveryTravel.Plan(routes,primary,new(0,6),false);
        Require(resumed.Points.SequenceEqual(new Vec[]{new(0,6),new(0,12)}),"Recovery from the middle of a route restarted or skipped its recorded path.");
        var activation=new Vec(.9,12);var exactReturn=new RecoveryPath(resumed.Points,activation);
        Require(exactReturn.Next(new(0,6))==primary.Anchor && exactReturn.Next(primary.Anchor)==activation && exactReturn.Next(activation)==null,
            "A hunt started near the saved destination failed to return to the exact activation anchor.");
        var connector=RecoveryTravel.Plan(routes,primary,new(3,6),false);
        Require(connector.Points[0]==new Vec(1.5,6)&&connector.Points[1]==new Vec(0,6),"Joining a nearby saved route did not subdivide the connector.");
        Invalid(()=>RecoveryTravel.Plan(routes,primary,new(100,100),false));
        Invalid(()=>RecoveryTravel.Plan(routes,primary with{Points=[primary.Anchor]},primary.Anchor,false));
        Invalid(()=>RecoveryTravel.Plan(routes,primary with{Points=[primary.Anchor,new(0,0)]},primary.Anchor,false));
        Invalid(()=>RecoveryTravel.Plan([primary with{Character="Other"}],alternative,primary.Anchor,false));
        Require(RecoveryTravel.SharedOrigin(sloped,alternative with{Height=96}) &&
            !RecoveryTravel.SharedOrigin(primary,alternative with{Zone=6}) &&
            !RecoveryTravel.SharedOrigin(primary,alternative with{Character="Other"}) &&
            !RecoveryTravel.SharedOrigin(primary,Route(new(20,20),new(20,14),new(20,8))),
            "Fallback confused different endpoint heights with origin compatibility or accepted another map, character, or origin.");
        var uphillFallback=RecoveryTravel.Plan([sloped,alternative with{Height=96}],sloped,alternative.Anchor,false);
        Require(uphillFallback.Points.SequenceEqual(new Vec[]{new(12,0),new(6,0),new(0,0),new(0,6),new(0,12)}),
            "Different-height anchors sharing a recorded origin could not follow their saved paths.");

        var cycle=new RecoveryFallbackCycle(1);
        Require(cycle.Candidates(3).SequenceEqual(new[]{1,2,0}),"Fallback omitted the primary route or changed circular order.");
        cycle.Reject(1);cycle.Reject(2);Require(cycle.Candidates(3).SequenceEqual(new[]{0}),"Occupied routes were retried before the cycle finished.");
        cycle.Reject(0);cycle.BeginWait();cycle.Wait(0);cycle.Wait(1000);cycle.Pause();cycle.Wait(100000);
        Require(cycle.Remaining==599000,"A pause/second death consumed the waiting interval.");
        cycle.Wait(200000);Require(cycle.Remaining==599000,"An unobserved pause consumed the waiting interval.");
        for(long now=201000;now<=799000;now+=1000)cycle.Wait(now);
        Require(cycle.Remaining==0&&cycle.Waiting,"Ten active minutes did not finish the occupied-spot cooldown.");
        cycle.Restart();Require(cycle.Candidates(3).SequenceEqual(new[]{1,2,0})&&!cycle.Waiting,"Cooldown failed to retry the saved spots.");
        cycle.Arrived(0);Require(cycle.StartingSlot==0&&!cycle.Rejected(0),"Arrival did not start a fresh fallback cycle.");

        Require(LeashReturn.ShouldReturn(false,true,false,false,11,10,20) &&
            !LeashReturn.ShouldReturn(false,true,true,false,11,10,20) &&
            LeashReturn.ShouldReturn(false,true,true,false,21,10,20) &&
            !LeashReturn.ShouldReturn(true,true,false,false,11,10,20) &&
            !LeashReturn.ShouldReturn(false,false,false,false,11,10,20),"Leash recovery overrode group mode or Gamekeeper completion boundaries.");
        int steps=0,waits=0,faces=0;long clock=0;
        await LeashReturn.Run(()=>steps==3,()=>{steps++;clock+=100;return Task.CompletedTask;},()=>{Require(steps==3,"Facing started before arrival.");faces++;return Task.CompletedTask;},
            ()=>waits==10,()=>{Require(steps==3,"Leash waited outside the anchor.");waits++;clock+=100;return Task.CompletedTask;},()=>clock,default);
        Require(steps==3&&faces==1&&waits==10,"Leash return resumed combat before reaching its anchor and waiting for the enemy.");
        using var cancellation=new CancellationTokenSource();bool stopped=false;
        try
        {
            await LeashReturn.Run(()=>true,()=>Task.CompletedTask,()=>Task.CompletedTask,()=>false,
                ()=>{cancellation.Cancel();return Task.CompletedTask;},()=>0,cancellation.Token);
        }
        catch(OperationCanceledException){stopped=true;}
        Require(stopped,"Stop was ignored while waiting at the anchor.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"recovery-travel-checks.json"),JsonSerializer.Serialize(new
        {
            Passed=true,HardwareInputEmitted=false,
            Checks=new[]{"legacy uphill route joins use identity and XY corridor instead of endpoint height","endpoint position and floor boundaries retained","different-height anchors share recorded origins without borrowing another character or map","legacy path-segment elevation remains unknown","fallback follows current route to shared origin then destination route","reverse path when all spots occupied","mid-route recovery","bounded route connectors","invalid/far/disconnected routes rejected","circular fallback includes primary","ten-minute active wait","death/pause preserves cooldown","leash returns before waiting","Gamekeeper/group boundaries retained","stop during wait"}
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
