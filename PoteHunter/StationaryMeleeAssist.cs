namespace PoteHunter;

internal enum StationaryAssistAction { Move, InRange, Yield }

// A small approach is complete only when attack reach is verified, not merely
// because its requested goal is inside an unrelated navigation tolerance.
internal sealed class StationaryMeleeAssist
{
    internal const double ArrivalTolerance=.025;
    internal const long MaximumMilliseconds=5000,NoProgressMilliseconds=1500;
    internal const int MaximumPlans=3;
    internal bool Active {get;private set;}
    // Target disappearance or replacement must not discard the saved return.
    internal bool ReturnRequired {get;private set;}
    internal Vec Goal {get;private set;}
    internal int Plans {get;private set;}
    Vec anchor;long began,progressAt;double bestDistance;

    internal static bool TryGoal(Vec position,Vec target,Vec anchor,double range,out Vec goal)
    {
        goal=default;
        if(!position.Finite || !target.Finite || !anchor.Finite || range<=0 || !double.IsFinite(range))return false;
        Vec delta=target-position,offset=position-anchor;
        double distance=delta.Length,radius=Targeting.StationaryAssistMaximumStep;
        if(offset.Length>radius || !Targeting.TryStationaryAssistStep(distance,range,out var step))return false;
        Vec direction=delta/distance;
        double projection=offset.X*direction.X+offset.Y*direction.Y;
        // Stay infinitesimally inside the strict navigation destination bound;
        // rotated radial goals can otherwise round to 1.5000000000000002.
        double clippedRadius=radius-1e-9;
        double discriminant=projection*projection+clippedRadius*clippedRadius-offset.Length*offset.Length;
        double available=Math.Sqrt(Math.Max(0,discriminant))-projection;
        step=Math.Min(step,Math.Max(0,available));
        if(step<=ArrivalTolerance)return false;
        goal=position+direction*step;
        return goal.Finite && (goal-anchor).Length<=radius;
    }

    internal bool TryBegin(Vec position,Vec target,Vec savedAnchor,double range,long now)
    {
        if(!TryGoal(position,target,savedAnchor,range,out var goal))return false;
        anchor=savedAnchor;Goal=goal;Plans=1;began=progressAt=now;
        bestDistance=(target-position).Length;Active=ReturnRequired=true;return true;
    }

    internal StationaryAssistAction Observe(Vec position,Vec target,double range,long now)
    {
        if(!Active || !position.Finite || !target.Finite || !double.IsFinite(range) || range<=0 ||
           (position-anchor).Length>Targeting.StationaryAssistMaximumStep+1e-6)
        {Active=false;return StationaryAssistAction.Yield;}
        double distance=(target-position).Length;
        if(distance<=range){Active=false;return StationaryAssistAction.InRange;}
        if(distance<bestDistance-.01){bestDistance=distance;progressAt=now;}
        if(now-began>=MaximumMilliseconds || now-progressAt>=NoProgressMilliseconds)
        {Active=false;return StationaryAssistAction.Yield;}
        if((Goal-position).Length<=ArrivalTolerance)
        {
            if(Plans>=MaximumPlans || !TryGoal(position,target,anchor,range,out var goal))
            {Active=false;return StationaryAssistAction.Yield;}
            Goal=goal;Plans++;
        }
        return StationaryAssistAction.Move;
    }

    internal void Cancel()=>Active=false;

    internal static void SelfTest()
    {
        static void Check(bool value,string reason){if(!value)throw new Exception("Stationary assist: "+reason);}
        var origin=new Vec();var target=new Vec(2.5876789831,0);var assist=new StationaryMeleeAssist();
        Check(assist.TryBegin(origin,target,origin,2.5,0),"recorded short assist was rejected");
        Check(Math.Abs(assist.Goal.Length-.1376789831)<1e-8 && assist.Goal.Length>ArrivalTolerance &&
              assist.Observe(origin,target,2.5,100)==StationaryAssistAction.Move,"short assist falsely completed without moving");
        Check(assist.Observe(new Vec(.11,0),target,2.5,200)==StationaryAssistAction.InRange && !assist.Active,
              "actual attack range did not complete movement");
        assist.Cancel();
        Check(assist.ReturnRequired,"in-range completion lost its saved return");
        var disappeared=new StationaryMeleeAssist();
        Check(disappeared.TryBegin(origin,target,origin,2.5,0),"disappeared-target setup");
        disappeared.Cancel();
        Check(!disappeared.Active && disappeared.ReturnRequired,"target invalidation discarded the assisted return");
        var quantized=new StationaryMeleeAssist();
        Check(quantized.TryBegin(origin,target,origin,2.5,0) &&
            quantized.Observe(new Vec(.26,0),target,2.5,200)==StationaryAssistAction.InRange && quantized.ReturnRequired,
            "minimum-frame overshoot missed actual attack range or lost return");
        Check(quantized.TryBegin(origin,target,origin,2.5,0) &&
            quantized.Observe(new Vec(1.5001,0),new Vec(4,0),2.5,200)==StationaryAssistAction.Yield && quantized.ReturnRequired,
            "physical leash departure did not yield with return preserved");
        Check(!TryGoal(origin,new Vec(4.01,0),origin,2.5,out _) &&
              !TryGoal(new Vec(double.NaN,0),target,origin,2.5,out _) &&
              !TryGoal(new Vec(1.49,0),new Vec(4.1,0),origin,2.5,out _) &&
              TryGoal(new Vec(1.4,0),new Vec(4,0),origin,2.5,out var edge) && edge.Length<=1.5+1e-9,
              "anchor leash or invalid geometry was bypassed");
        foreach(var saved in new[]{origin,new Vec(1000,-5000)})
        for(int i=0;i<628;i++)
        {
            double angle=i*.01;var direction=new Vec(Math.Cos(angle),Math.Sin(angle));
            Check(TryGoal(saved+direction*1.4,saved+direction*4,saved,2.5,out var rotated) &&
                (rotated-saved).Length<=Targeting.StationaryAssistMaximumStep,
                "rotated leash goal exceeded the strict navigation bound");
        }
        Check(assist.TryBegin(origin,target,origin,2.5,0) &&
              assist.Observe(origin,target,2.5,NoProgressMilliseconds)==StationaryAssistAction.Yield && !assist.Active,
              "blocked movement waited indefinitely");
        Check(assist.TryBegin(origin,target,origin,2.5,0) &&
              assist.Observe(new Vec(.02,0),target,2.5,MaximumMilliseconds)==StationaryAssistAction.Yield,
              "absolute deadline was extended by progress");
        Check(assist.TryBegin(origin,target,origin,2.5,0),"replan setup");
        for(int i=1;i<MaximumPlans;i++)
        {
            var position=assist.Goal;
            Check(assist.Observe(position,position+new Vec(2.6,0),2.5,i*100)==StationaryAssistAction.Move,
                  "small moving-target correction was consumed early");
        }
        var last=assist.Goal;
        Check(assist.Observe(last,last+new Vec(2.6,0),2.5,400)==StationaryAssistAction.Yield,
              "moving target bypassed plan bound");
    }
}
