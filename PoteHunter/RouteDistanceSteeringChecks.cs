using System.Text.Json;

namespace PoteHunter;

// These fixtures describe synthetic routes only. No game reader, window,
// hardware input, live settings or saved user route is used by these checks.
internal static class RouteDistanceSteeringChecks
{
    static readonly Func<Vec,Vec,bool> Clear=(_,_)=>true;
    static readonly Func<Vec,Vec,bool> Blocked=(_,_)=>false;

    public static async Task Run()
    {
        SamplingDensity();
        SpeedHorizon();
        PhysicalAndPointBounds();
        AdmissionAndGeometry();
        RequiredArrival();
        await RetryOwnership();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"route-distance-steering-checks.json"),
            JsonSerializer.Serialize(new{
                Passed=true,HardwareInputEmitted=false,SyntheticRoutesOnly=true,
                Checks=new[]{
                    "integrated clear-route progression with dense, sparse and irregular samples shares a physical steering horizon",
                    "subdivided gentle bends agree with the same sparse geometry",
                    "opposing mild bends contribute curvature instead of cancelling one another",
                    "rotation and translation preserve the accepted aim",
                    "positive measured speed scales the bounded lookahead; invalid or unknown speed preserves fallback",
                    "geometry outside the four-unit curvature horizon does not alter steering",
                    "extreme sample density cannot exceed the local point or collision-query bounds",
                    "blocked direct chords and individually blocked route segments reject lookahead",
                    "unverified and off-corridor connectors remain at their checkpoint",
                    "accepted mild-curve chords retain the recorded corridor",
                    "initial connector, sharp corner, U-turn, nearby loop and final anchor retain precision",
                    "steering does not advance route progress or renew a consumed waypoint turn retry",
                    "invalid or completed routes cannot supply a live steering aim"
                }
            },new JsonSerializerOptions{WriteIndented=true}));
    }

    static RecoveryPath MakePath(Vec[] points)=>new(points[..^1],points[^1]);
    static Vec Aim(Vec[] route,Vec current,double speed=0)
    {
        var path=MakePath(route);path.Next(current,Clear);
        return path.SteeringGoal(current,Clear,speed);
    }
    static Vec[] Densify(Vec[] points,int subdivisions)
    {
        var result=new List<Vec>{points[0]};
        for(int at=1;at<points.Length;at++)
            for(int step=1;step<=subdivisions;step++)
                result.Add(points[at-1]+(points[at]-points[at-1])*(step/(double)subdivisions));
        return result.ToArray();
    }
    static void Near(Vec actual,Vec expected,string reason,double tolerance=1e-8)
    {
        if(!actual.Finite || (actual-expected).Length>tolerance)
            throw new Exception($"{reason}: expected {expected}, actual {actual}.");
    }
    static void Require(bool passed,string reason)
    {
        if(!passed)throw new Exception(reason);
    }

    static void SamplingDensity()
    {
        Vec[] straight=[new(0,0),new(2,0),new(4,0),new(6,0),new(8,0),new(10,0)];
        Vec[] dense=Densify(straight,8);
        Vec[] irregular=[new(0,0),new(.17,0),new(.45,0),new(.9,0),new(1.4,0),new(2.1,0),
            new(2.3,0),new(2.5,0),new(3.7,0),new(4.4,0),new(6,0),new(8,0),new(10,0)];
        foreach(double speed in new[]{0.0,.010,.014})
        {
            Vec sparseAim=Aim(straight,default,speed),denseAim=Aim(dense,default,speed);
            Near(denseAim,sparseAim,"Recorded point density changed the straight route aim");
            Near(Aim(irregular,default,speed),sparseAim,"Irregular sample density changed the straight route aim");
            Near(sparseAim,new(Math.Clamp(speed>0?speed*250:3.5,1.75,3.5),0),
                "Straight steering horizon did not use physical route distance");
        }
        foreach(double offset in new[]{-.3,.3})
            Near(Aim(dense,new(0,offset)),Aim(straight,new(0,offset)),
                "An admitted incoming offset changed the aim with sample density");

        var gentle=new List<Vec>{default};
        for(int at=0;at<12;at++)
            gentle.Add(gentle[^1]+Movement.Rotate(new(.75,0),at*6*Math.PI/180));
        Vec[] curve=gentle.ToArray(),denseCurve=Densify(curve,4);
        Vec original=Aim(curve,default),subdivided=Aim(denseCurve,default);
        Near(subdivided,original,"Subdividing a gentle polyline changed its physical steering goal");
        Require(original.X>1.75 && original.Y>0,"A gentle bend still aimed only at its next stored sample");
        foreach(double radians in new[]{.73,-1.2,Math.PI})
        {
            Vec Transform(Vec value)=>Movement.Rotate(value,radians)+new Vec(20,-7);
            Near(Aim(curve.Select(Transform).ToArray(),Transform(default)),Transform(original),
                "Route steering changed under a rigid transform");
            Near(Aim(denseCurve.Select(Transform).ToArray(),Transform(default)),Transform(original),
                "Dense route steering changed under a rigid transform");
        }

        var alternating=new List<Vec>{default};
        foreach(double degrees in new[]{0.0,8,0,-8,0,0,0,0,0,0,0,0})
            alternating.Add(alternating[^1]+Movement.Rotate(new(.75,0),degrees*Math.PI/180));
        Vec sBend=Aim(alternating.ToArray(),default);
        Near(Aim(Densify(alternating.ToArray(),4),default),sBend,
            "Dense opposing bends lost the same physical curvature horizon");
        Require(sBend.Length<3.3,"Opposing mild bends cancelled their curvature and retained full straight lookahead");
    }

    static void SpeedHorizon()
    {
        Vec[] route=Densify([new(0,0),new(2,0),new(4,0),new(6,0),new(8,0),new(10,0)],8);
        Vec normal=Aim(route,default);
        foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity,-1.0,0.0})
            Near(Aim(route,default,invalid),normal,"Unknown or invalid speed changed the conservative fallback aim");
        Near(Aim(route,default,.000001),new(1.75,0),"Very slow movement bypassed the minimum lookahead");
        Near(Aim(route,default,1000),new(3.5,0),"A large speed sample enlarged the maximum lookahead");
        Require(Aim(route,default,.010).X>Aim(route,default,.004).X &&
            Aim(route,default,.014).X>Aim(route,default,.010).X,
            "Measured speed did not adjust straight-route lookahead monotonically");
    }

    static void PhysicalAndPointBounds()
    {
        // Keep the next-segment sharp-corner guard beyond the 3.5-unit aim as
        // well. Otherwise that independent safety gate, rather than curvature
        // horizon length, correctly shortens steering before the farther bend.
        Vec[] noFarBend=[new(0,0),new(1,0),new(2,0),new(3,0),new(4,0),new(5,0),new(6,0),new(7,0),new(10,0)];
        Vec[] farBend=[new(0,0),new(1,0),new(2,0),new(3,0),new(4,0),new(5,0),new(6,0),new(6,2),new(6,8)];
        Near(Aim(farBend,default),Aim(noFarBend,default),
            "A bend outside the physical curvature horizon shortened current steering");

        // More than 32 very close samples deliberately exercise the work cap.
        // The cap may conservatively shorten this aim; it must never skip to a
        // remote sample or make an unbounded series of collision queries.
        Vec[] extremelyDense=Enumerable.Range(0,1001).Select(at=>new Vec(at*.005,0)).ToArray();
        var path=MakePath(extremelyDense);Vec checkpoint=path.Next(default,Blocked)!.Value;
        int checkpointIndex=path.Index,calls=0;
        Vec aim=path.SteeringGoal(default,(_,_)=>{calls++;return true;});
        Require(aim.X>=checkpoint.X && aim.X<=checkpoint.X+.2 && calls<=256 && path.Index==checkpointIndex,
            "Extremely dense steering exceeded its bounded local work window or advanced progress");
    }

    static void AdmissionAndGeometry()
    {
        Vec[] straight=[new(0,0),new(2,0),new(4,0),new(6,0),new(10,0)];
        var path=MakePath(straight);Vec current=default,checkpoint=path.Next(current,Blocked)!.Value;
        Near(path.SteeringGoal(current),checkpoint,"Unverified geometry permitted lookahead");
        Near(path.SteeringGoal(current,Blocked),checkpoint,"A blocked checkpoint permitted lookahead");
        Near(path.SteeringGoal(current,(from,to)=>from!=current || to==checkpoint),checkpoint,
            "A blocked direct steering chord was accepted");
        Near(path.SteeringGoal(current,(from,to)=>from!=checkpoint),checkpoint,
            "An individually blocked route segment was skipped");
        Near(path.SteeringGoal(new(0,.351),Clear),checkpoint,
            "An off-corridor connector borrowed lookahead from a recorded segment");

        // Stay outside both independent checkpoint-admission windows, so these
        // cases isolate directional passage rather than legitimate arrival or
        // the existing <=1.25-unit clear-path lookahead admission.
        var backward=MakePath(straight);backward.Next(default,Blocked);backward.Next(new(1.39,0),Blocked);
        Require(backward.Next(new(-.5,0),Clear)==checkpoint && backward.Index==1,
            "A backwards observed step counted as forward route progress");
        var sideways=MakePath(straight);sideways.Next(default,Blocked);sideways.Next(new(1.39,.351),Blocked);
        Require(sideways.Next(new(3.3,.351),Clear)==checkpoint && sideways.Index==1,
            "A crossing outside the incoming corridor advanced route progress");

        // A bounded predecessor search must belong to the current travel leg.
        // The nearby outbound leg is geometrically close but its mandatory
        // corners were already visited; it cannot become a new forward aim.
        var hairpin=new RecoveryPath([new(0,0),new(4,0),new(4,1),new(0,1),new(-4,1)],new(-8,1));
        hairpin.Next(default,Blocked);hairpin.Next(new(4,0),Blocked);hairpin.Next(new(4,1),Blocked);
        int returningIndex=hairpin.Index;
        Vec returnAim=hairpin.SteeringGoal(new(3,.7),Clear);
        Require(returningIndex==3 && returnAim.X<3 && Math.Abs(returnAim.Y-1)<1e-8 && hairpin.Index==returningIndex,
            "Predecessor projection selected a nearby visited outbound leg");
        Near(hairpin.SteeringGoal(new(3,.1),Clear),new(0,1),
            "An off-current-leg position was admitted through a nearby visited branch");

        // A mild curve can remain continuous, but the chord must retain every
        // visited bend inside the existing 0.35-unit recorded-route corridor.
        Vec[] curve=[new(0,0),new(1,0),new(2,.10),new(3,.25),new(4,.45),new(8,.45)];
        var curved=MakePath(curve);curved.Next(default,Blocked);
        Vec accepted=curved.SteeringGoal(default,Clear);
        Require(accepted.X>1 && accepted.Finite,"A clear gentle bend did not produce forward continuous steering");
        foreach(Vec vertex in curve.Where(p=>p.X<=accepted.X))
        {
            Vec chord=accepted;double projection=(vertex.X*chord.X+vertex.Y*chord.Y)/(chord.Length*chord.Length);
            Require(projection>=0 && projection<=1.0001 &&
                (vertex-chord*Math.Clamp(projection,0,1)).Length<=RecoveryPath.SteeringCorridorUnits+1e-8,
                "A near-wall gentle curve accepted a chord outside its recorded corridor");
        }
        Vec curveCheckpoint=curve[1];
        Near(curved.SteeringGoal(default,(from,to)=>from!=default || to==curveCheckpoint),curveCheckpoint,
            "A blocked near-wall chord cut across the recorded curve");
        bool invalidRejected=false;
        try{curved.SteeringGoal(new(double.NaN,0),Clear);}
        catch(RouteUnavailableException){invalidRejected=true;}
        Require(invalidRejected,"Invalid character position produced a route aim");
    }

    static void RequiredArrival()
    {
        var entry=new RecoveryPath([new(3,0),new(7,0)],new(11,0));
        Vec connector=entry.Next(default,Blocked)!.Value;
        Require(entry.Index==0 && entry.ArrivalTolerance==.6,"Initial connector lost precise arrival");
        Near(entry.SteeringGoal(default,Clear,.014),connector,"Lookahead bypassed an unvisited entry connector");
        Require(entry.Next(new(4.5,0),Clear)==connector,"Unobserved entry passage advanced route progress");

        foreach(Vec[] route in new[]{
            new Vec[]{new(0,0),new(2,0),new(2,2),new(2,6)},
            new Vec[]{new(0,0),new(2,0),new(0,0),new(-4,0)},
            new Vec[]{new(0,0),new(2,0),new(2,2),new(0,2),new(0,.1)}})
        {
            var corner=MakePath(route);Vec checkpoint=corner.Next(default,Blocked)!.Value;
            Require(corner.ArrivalTolerance==.6,"A corner or U-turn lost exact checkpoint capture");
            Near(corner.SteeringGoal(default,Clear,.014),checkpoint,
                "A sharp corner, U-turn or nearby loop endpoint was shortcut");
            Require(corner.Next(new(2.7,0),Clear)==checkpoint,
                "An overshot required corner was accepted as a straight passage");
        }

        var final=new RecoveryPath([new(0,0),new(4,0),new(4,0)],new(4,0));
        Vec anchor=final.Next(default,Blocked)!.Value;
        Require(final.Final && final.ArrivalTolerance==.5,"A duplicate final anchor lost its strict tolerance");
        Near(final.SteeringGoal(default,Clear,.014),anchor,"Final anchor was replaced by a lookahead goal");
        Require(final.Next(new(3.49,0),Clear)==anchor && final.Next(new(3.51,0),Clear)==null,
            "Steering changed actual final-arrival acceptance");
        bool completeRejected=false;
        try{final.SteeringGoal(new(4,0),Clear);}
        catch(RouteUnavailableException){completeRejected=true;}
        Require(completeRejected,"A completed path supplied a live steering aim");

        var finalAdjustment=new RecoveryPath([new(0,0),new(4,0)],new(5,0));
        finalAdjustment.Next(default,Blocked);
        Require(!finalAdjustment.Final && finalAdjustment.ArrivalTolerance==.6,
            "A short final connector lost braking before the precise anchor adjustment");
    }

    static async Task RetryOwnership()
    {
        var path=new RecoveryPath([new(0,0),new(2,0),new(4,0),new(6,0)],new(10,0));
        Vec checkpoint=path.Next(default,Blocked)!.Value;int index=path.Index;
        var retry=new SavedRouteTurnRecovery();long now=1000;int releases=0,resets=0;
        retry.ObserveWaypoint(path,now);now+=100;
        var first=await retry.RetryAsync(path,()=>releases++,()=>resets++,
            (milliseconds,_)=>{now+=milliseconds;return Task.CompletedTask;},
            ()=> (new Vec(0,0),0.0),()=>now,CancellationToken.None);
        Require(first.Ready,"Synthetic waypoint did not receive its first bounded retry");
        for(int at=0;at<100;at++)
        {
            path.SteeringGoal(new(.1*at/100,0),Clear,at%2==0?.004:.014);
            retry.ObserveWaypoint(path,++now);
            Require(path.Index==index && path.ArrivalTolerance==0,
                "Steering advanced arrival or waypoint ownership without observed travel");
        }
        var second=await retry.RetryAsync(path,()=>releases++,()=>resets++,
            (_,_)=>Task.CompletedTask,()=> (new Vec(0,0),0.0),()=>now,CancellationToken.None);
        Require(!second.Ready && second.Reason=="AlreadyRetried" && resets==1 && releases==2 &&
            path.Next(new(.1,0),Blocked)==checkpoint && path.Index==index,
            "Changing lookahead or measured speed rearmed a consumed same-waypoint turn retry");
    }
}
