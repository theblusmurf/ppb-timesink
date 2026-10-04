using System.Text.Json;

namespace PoteHunter;

internal static class AdjustableRadiusChecks
{
    static void Require(bool condition,string message)
    {
        if(!condition)throw new Exception(message);
    }
    static void RejectPlan(Action action)
    {
        bool rejected=false;
        try{action();}catch(RouteUnavailableException){rejected=true;}
        Require(rejected,"Route travel accepted a position outside the chosen join corridor.");
    }

    internal static void Run()
    {
        var defaults=JsonSerializer.Deserialize<Options>("{}")!;
        Require(defaults.RouteCorridorRadius==10 && defaults.LootPickupRadius==10 && defaults.HuntRadius==35,
            "Older settings lost their radius defaults.");
        var older=JsonSerializer.Deserialize<Options>("{\"HuntRadius\":37.5,\"NearbyEnemyRadius\":7}")!;
        Require(older.HuntRadius==37.5m && older.NearbyEnemyRadius==7 && older.RouteCorridorRadius==10 && older.LootPickupRadius==10,
            "Adding route and loot radii changed an existing farming or nearby-enemy setting.");
        var restored=JsonSerializer.Deserialize<Options>(JsonSerializer.Serialize(new Options
        {
            HuntRadius=37.5m,RouteCorridorRadius=4.5m,LootPickupRadius=26.5m
        }))!;
        Require(restored.HuntRadius==37.5m && restored.RouteCorridorRadius==4.5m && restored.LootPickupRadius==26.5m,
            "Independent fractional radii did not survive settings serialization.");
        var bounded=JsonSerializer.Deserialize<Options>("{\"RouteCorridorRadius\":0,\"LootPickupRadius\":100,\"HuntRadius\":37.5}")!;
        Require(bounded.RouteCorridorRadius==.5m && bounded.LootPickupRadius==30 && bounded.HuntRadius==37.5m,
            "Malformed saved radii escaped the controls' bounds or changed the farming radius.");

        // Standing midway along a segment distinguishes a true corridor from
        // distance to recorded waypoints or to the final farming anchor.
        var route=new SavedNavigationRoute(5,new(0,12),1,[new(0,12),new(0,6),new(0,0)],DateTime.UnixEpoch,"Test",10,35);
        int Join(Vec position,double radius)=>RecoveryTravel.StartupSlot([route],position,5,"Test",10,0,radius);
        foreach(double radius in new[]{.5,4.5,10,26.5,30})
        {
            Require(Join(new(radius,9),radius)==0 && Join(new(radius+.001,9),radius)==-1,
                "The route corridor did not include its exact segment boundary or excluded nearby outside positions.");
            var travel=RecoveryTravel.Plan([route],route,new(radius,9),false,radius);
            Require(travel.Points.Contains(new Vec(0,9)) && travel.Points.Last()==route.Anchor && travel.Destination==route.Anchor,
                "A configured startup corridor could not join its nearest segment and reach the saved anchor.");
            RejectPlan(()=>RecoveryTravel.Plan([route],route,new(radius+.001,9),false,radius));
        }
        Require(Join(new(8,9),4.5)==-1 && Join(new(8,9),10)==0 && Join(new(25,9),10)==-1 && Join(new(25,9),30)==0,
            "Shrinking or expanding the corridor did not change eligible route starts.");
        RejectPlan(()=>RecoveryTravel.Plan([route],route,new(25,9),false));
        Require(RecoveryTravel.StartupSlot([route],new(0,9),6,"Test",10,0,30)==-1 &&
            RecoveryTravel.StartupSlot([route],new(0,9),5,"Other",10,0,30)==-1 &&
            RecoveryTravel.StartupSlot([route],route.Anchor,5,"Test",20,0,30)==-1,
            "A wider corridor admitted a different zone, character or endpoint floor.");
        foreach(double invalid in new[]{0,-1,double.NaN,double.PositiveInfinity})
            Require(Join(new(0,9),invalid)==-1,"An invalid corridor admitted startup travel.");

        static GroundItem Drop(uint id,double x)=>new(id,id,0,"Ground loot",new(x,100),0);
        var anchor=new Vec(100,100);
        foreach(double radius in new[]{.5,4.5,10,26.5,30})
        {
            var atBoundary=Drop(1,100+radius);
            var beyondBoundary=Drop(2,100+radius+.001);
            var position=new Vec(100+Math.Max(0,radius-2),100);
            Require(NearbyLootPickup.InsideAnchor(atBoundary.Position,anchor,radius) &&
                !NearbyLootPickup.InsideAnchor(beyondBoundary.Position,anchor,radius) &&
                NearbyLootPickup.EvaluateAnchor(position,anchor,[atBoundary],radius).HoldLoot &&
                !NearbyLootPickup.EvaluateAnchor(position,anchor,[atBoundary,beyondBoundary],radius).HoldLoot,
                "Configured loot boundaries admitted an outside drop to the shared E pickup area.");
            Require(!NearbyLootPickup.MayPickupAt(new(100+radius+.001,100),anchor,[atBoundary],radius),
                "Ground pickup was held while the player was outside the configured circle.");
        }
        Require(!NearbyLootPickup.InsideAnchor(Drop(3,108).Position,anchor,4.5) &&
            NearbyLootPickup.InsideAnchor(Drop(3,108).Position,anchor,10) &&
            !NearbyLootPickup.InsideAnchor(Drop(4,125).Position,anchor,10) &&
            NearbyLootPickup.InsideAnchor(Drop(4,125).Position,anchor,30),
            "Loot eligibility did not respond to a smaller or larger pickup radius.");
        Require(!NearbyLootPickup.EvaluateAnchor(anchor,anchor,[Drop(5,104)],30).HoldLoot &&
            NearbyLootPickup.PickupReach==3 && NearbyLootPickup.ArrivedAtAnchor(new(100.1,100),anchor) &&
            !NearbyLootPickup.ArrivedAtAnchor(new(100.2,100),anchor),
            "A larger loot circle changed physical pickup reach or exact anchor-return tolerance.");

        var target=new Entity(1,0x813d1753,"Mimic",new(10.5,0),0,Model:"MON_mimic.GCMDS");
        var health=new Dictionary<uint,Health>{{target.Id,new(100,100)}};
        Entity? Pick(double radius)=>Targeting.Choose([target],health,new(),new(),radius,_=>Threat.Yellow,"Mimic",["Yellow"],prioritizeGamekeeper:false);
        Require(Pick(10)==null && Pick(10.5)==target && Pick(35)==target,
            "The adjustable anchor area is not the actual farming target-selection boundary.");
        var largeAreas=new Options{HuntRadius=150,RouteCorridorRadius=30,LootPickupRadius=30,MeleeRange=1.5m};
        Require(Targeting.ShouldHoldStationaryAnchor(target,largeAreas,new(1,0),new(),new(100,100)) &&
            !Targeting.ShouldHoldStationaryAnchor(target,largeAreas,new(1.6,0),new(),new(100,100)) &&
            !Targeting.TryStationaryAssistStep(4.01,2.5,out _),
            "A wider area changed stationary farming into a longer melee excursion.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"adjustable-radius-checks.json"),JsonSerializer.Serialize(new
        {
            Passed=true,HardwareInputEmitted=false,
            Checks=new[]{"old settings defaults and farming value preserved","independent fractional settings serialization","saved bounds","inclusive segment corridor at .5 through 30","smaller/larger startup radius","wide startup connector beyond legacy20","death fallback retains20","map/character/floor guards","loot boundary and shared-E outside exclusion","smaller/larger loot eligibility","pickup reach and exact anchor arrival retained","actual farming target boundary","stationary melee excursion retained"}
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
