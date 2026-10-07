namespace PoteHunter;

internal static class NearbyLootPickupChecks
{
    static void Require(bool condition,string message){if(!condition)throw new Exception("Anchor loot collection: "+message);}
    static GroundItem Drop(uint id,double x,double y=0)=>new(id,id,0,"Existing ground loot",new(x,y),0);

    public static void Run()
    {
        Vec anchor=new(0,0);HashSet<(uint,uint)> baseline=[];
        AnchorLootPlan Plan(Vec position,GroundItem[] drops,bool protect=true)=>
            NearbyLootPickup.PlanAnchorCollection(position,anchor,drops,baseline,protect);
        var recorded=Drop(1,2.81393);
        Require(Plan(anchor,[recorded]).Action==AnchorLootAction.Pickup,
            "recorded 2.81393-unit gold pile still requested movement before E");
        Require(Plan(anchor,[Drop(2,3)]).Action==AnchorLootAction.Pickup &&
            Plan(anchor,[Drop(3,3.00001)]).Action==AnchorLootAction.Approach &&
            Plan(anchor,[Drop(4,1.8,2.4)]).Action==AnchorLootAction.Pickup &&
            Plan(anchor,[Drop(5,1.80001,2.4)]).Action==AnchorLootAction.Approach,
            "inclusive three-unit pickup reach changed or widened");
        var distant=Drop(6,6);
        Require(Plan(anchor,[distant,recorded]).Action==AnchorLootAction.Pickup &&
            Plan(anchor,[distant,recorded]).Nearby.SequenceEqual([recorded]),
            "distant eligible loot displaced a reachable neighborhood");
        Require(Plan(anchor,[Drop(7,10)]).Action==AnchorLootAction.Approach &&
            Plan(anchor,[Drop(8,10.00001)]).Action==AnchorLootAction.Finished &&
            Plan(new(10.00001,0),[Drop(9,10)]).Action==AnchorLootAction.BoundaryBlocked &&
            Plan(new(9,0),[Drop(10,10),Drop(11,11)]).Action==AnchorLootAction.BoundaryBlocked,
            "collection left the ten-unit anchor or admitted an outside E neighbor");
        // Outside drops must not prevent guarded travel to a currently distant
        // inside drop. They forbid E only when an eligible neighborhood exists.
        Require(Plan(new(9,0),[Drop(12,0),Drop(13,11)]).Action==AnchorLootAction.Approach,
            "a currently unsafe E neighborhood prevented otherwise eligible approach planning");
        var existing=Drop(14,2);baseline.Add((existing.KeyA,existing.KeyB));
        Require(Plan(anchor,[recorded,existing]).Action==AnchorLootAction.Protected &&
            Plan(anchor,[distant,existing]).Action==AnchorLootAction.Protected &&
            Plan(anchor,[existing]).Action==AnchorLootAction.Finished &&
            Plan(anchor,[recorded,existing],false).Action==AnchorLootAction.Pickup &&
            Plan(anchor,[recorded,existing with{Position=new(3.00001,0)}]).Action==AnchorLootAction.Pickup,
            "pre-existing ownership exclusion changed or nearby protected loot was admitted");
        Require(Plan(anchor,[]).Action==AnchorLootAction.Finished &&
            Plan(anchor,[Drop(15,double.NaN),Drop(16,double.PositiveInfinity)]).Action==AnchorLootAction.Finished &&
            Plan(new(double.NaN,0),[recorded]).Action==AnchorLootAction.BoundaryBlocked,
            "missing/malformed observations admitted pickup or movement");

        // Crossing into reach during an approach must brake at the admitted
        // neighborhood, without continuing toward the former 2.5-unit target.
        Require(Plan(anchor,[Drop(17,3.01)]).Action==AnchorLootAction.Approach &&
            Plan(new(.02,0),[Drop(17,3.01)]).Action==AnchorLootAction.Pickup &&
            Plan(new(.02,0),[]).Action==AnchorLootAction.Finished,
            "fresh reach arrival or disappeared target retained the approach");
        Require(Plan(anchor,[distant]).Action==AnchorLootAction.Approach &&
            Plan(anchor,[distant,recorded]).Action==AnchorLootAction.Pickup,
            "a newly arrived reachable drop failed to supersede distant approach");
        Require(NearbyLootPickup.RemovedAfterPickup([recorded],[recorded,distant])==0 &&
            NearbyLootPickup.RemovedAfterPickup([recorded],[distant])==1 &&
            NearbyLootPickup.RemovedAfterPickup([recorded,recorded],[distant])==1,
            "new/duplicate drops manufactured pickup progress or removal was missed");

        int attempts=0,movements=0;
        for(;attempts<NearbyLootPickup.MaximumCollectionAttempts;attempts++)
        {
            var plan=Plan(anchor,[recorded]);
            if(plan.Action==AnchorLootAction.Approach)movements++;
            Require(plan.Action==AnchorLootAction.Pickup &&
                NearbyLootPickup.RemovedAfterPickup(plan.Nearby,[recorded])==0,
                "failed stationary E changed reach admission");
        }
        Require(attempts==3 && movements==0,"no-result pickup attempts widened reach, moved or lost the existing bound");
    }
}
