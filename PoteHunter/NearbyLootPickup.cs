namespace PoteHunter;

public readonly record struct NearbyLootDecision(int NearbyCount)
{
    public bool HoldLoot => NearbyCount > 0;
}

public static class NearbyLootPickup
{
    public const double AnchorRadius=4;
    public const double PickupReach=3;
    public const double AnchorArrivalTolerance=.15;
    public static bool InsideAnchor(Vec point,Vec anchor) => point.Finite && anchor.Finite &&
        (point-anchor).Length<=AnchorRadius+1e-9;
    public static bool ArrivedAtAnchor(Vec position,Vec anchor) => position.Finite && anchor.Finite &&
        (position-anchor).Length<=AnchorArrivalTolerance;
    // E collects a neighborhood, not an individual selected item. Do not send
    // it beside drops outside the requested circle, even if one inside is near.
    public static bool MayPickupAt(Vec position,Vec anchor,IEnumerable<GroundItem> drops) =>
        InsideAnchor(position,anchor) && !drops.Any(d=>d.Position.Finite &&
            (d.Position-position).Length<=PickupReach && !InsideAnchor(d.Position,anchor));
    public static NearbyLootDecision EvaluateAnchor(Vec position,Vec anchor,IEnumerable<GroundItem> drops)
    {
        var snapshot=drops.ToArray();
        return MayPickupAt(position,anchor,snapshot) ? Evaluate(position,PickupReach,
            snapshot.Where(d=>InsideAnchor(d.Position,anchor))) : new(0);
    }

    public static NearbyLootDecision Evaluate(Vec position,double radius,IEnumerable<GroundItem> drops)
    {
        if(!position.Finite)throw new ArgumentOutOfRangeException(nameof(position));
        if(!double.IsFinite(radius) || radius<=0)throw new ArgumentOutOfRangeException(nameof(radius));
        ArgumentNullException.ThrowIfNull(drops);
        int count=0;
        foreach(var drop in drops)
        {
            if(drop is null || !drop.Position.Finite)continue;
            // Normalize first so finite but very large coordinates or radii
            // cannot overflow a squared-distance comparison into a false match.
            double x=Math.Abs(drop.Position.X-position.X),y=Math.Abs(drop.Position.Y-position.Y);
            if(x>radius || y>radius)continue;
            x/=radius;y/=radius;
            if(x*x+y*y<=1)count++;
        }
        return new(count);
    }

    public static bool HasLoot(Vec position,double radius,IEnumerable<GroundItem> drops)=>
        Evaluate(position,radius,drops).HoldLoot;

    public static void SelfTest()
    {
        static GroundItem Drop(uint id,double x,double y=0)=>new(id,id,0,"Existing ground loot",new Vec(x,y),0);
        var origin=new Vec(0,0);
        var anchor=new Vec(100,100);
        if(!InsideAnchor(new Vec(104,100),anchor) || InsideAnchor(new Vec(104.01,100),anchor) ||
            !InsideAnchor(new Vec(102.4,103.2),anchor) || InsideAnchor(new Vec(103,103),anchor) ||
            InsideAnchor(new Vec(double.NaN,100),anchor))
            throw new Exception("Anchor loot circle is not an inclusive four-unit map radius.");
        if(EvaluateAnchor(new Vec(101.5,100),anchor,[Drop(20,104,100)]).NearbyCount!=1 ||
            EvaluateAnchor(new Vec(103,100),anchor,[Drop(21,105,100)]).HoldLoot ||
            EvaluateAnchor(new Vec(105,100),anchor,[Drop(22,104,100)]).HoldLoot ||
            EvaluateAnchor(new Vec(103,100),anchor,[Drop(23,104,100),Drop(24,105,100)]).HoldLoot ||
            EvaluateAnchor(anchor,anchor,[Drop(25,104,100)]).HoldLoot)
            throw new Exception("Anchor pickup followed the player radius, picked beyond home, or held E before reach.");
        if(!MayPickupAt(anchor,anchor,[Drop(26,105,100)]) ||
            EvaluateAnchor(anchor,anchor,[]).HoldLoot || !ArrivedAtAnchor(new Vec(100.1,100),anchor) ||
            ArrivedAtAnchor(new Vec(100.2,100),anchor) || ArrivedAtAnchor(new Vec(double.NaN,100),anchor))
            throw new Exception("Anchor pickup exclusion or exact-return tolerance failed.");
        var exact=Drop(1,5);var outside=Drop(2,5.01);var diagonal=Drop(3,3,4);
        if(Evaluate(origin,5,[exact,outside,diagonal]).NearbyCount!=2 ||
            !HasLoot(origin,5,[exact]) || HasLoot(origin,5,[outside]))
            throw new Exception("Nearby loot did not use the inclusive configured radius.");
        if(HasLoot(origin,4.99,[exact]) || !HasLoot(origin,5.01,[outside]))
            throw new Exception("Nearby loot ignored a changed Nearby Enemy Radius.");
        if(HasLoot(new Vec(-.01,0),5,[exact]) || !HasLoot(new Vec(.01,0),5,[outside]) ||
            !HasLoot(new Vec(100,100),5,[Drop(4,103,104)]))
            throw new Exception("Nearby loot used a fixed hunt anchor instead of the current player position.");
        var first=Drop(5,1);var second=Drop(6,2);
        if(Evaluate(origin,5,[first,second,outside]).NearbyCount!=2 ||
            !HasLoot(origin,5,[second,outside]) || HasLoot(origin,5,[outside]) || HasLoot(origin,5,[]))
            throw new Exception("Nearby loot stopped before the final nearby item disappeared or retained stale loot.");
        if(!HasLoot(origin,5,[first with {KeyA=0,KeyB=0,TypeId=-1,Name="",Description="Unrecognized item"}]))
            throw new Exception("Nearby loot filtered existing or unrecognized ground items by identity.");
        if(HasLoot(origin,5,[Drop(7,double.NaN),Drop(8,double.PositiveInfinity),Drop(9,0,double.NegativeInfinity)]) ||
            Evaluate(origin,5,[first,Drop(10,double.NaN)]).NearbyCount!=1)
            throw new Exception("Malformed ground-item coordinates affected valid nearby-loot detection.");
        if(HasLoot(new Vec(-double.MaxValue,0),double.MaxValue,[Drop(11,double.MaxValue)]) ||
            !HasLoot(origin,double.MaxValue,[Drop(12,double.MaxValue)]))
            throw new Exception("Nearby loot mishandled finite coordinates whose distance arithmetic can overflow.");
        foreach(var radius in new[]{0d,-1d,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
        {
            bool rejected=false;
            try{Evaluate(origin,radius,[first]);}catch(ArgumentOutOfRangeException){rejected=true;}
            if(!rejected)throw new Exception("Nearby loot accepted an invalid radius.");
        }
        foreach(var position in new[]{new Vec(double.NaN,0),new Vec(0,double.PositiveInfinity),new Vec(double.NegativeInfinity,0)})
        {
            bool rejected=false;
            try{Evaluate(position,5,[first]);}catch(ArgumentOutOfRangeException){rejected=true;}
            if(!rejected)throw new Exception("Nearby loot accepted an invalid player position.");
        }
        bool nullRejected=false;
        try{Evaluate(origin,5,null!);}catch(ArgumentNullException){nullRejected=true;}
        if(!nullRejected)throw new Exception("Nearby loot accepted a missing ground-item observation.");
    }
}
