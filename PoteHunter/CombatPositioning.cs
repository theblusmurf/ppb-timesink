namespace PoteHunter;

public sealed record CombatPositionPlan(Vec Destination,int Before,int After,int Total);
public sealed record CombatSideStepPlan(bool Left,Vec Destination,int Before,int After);

// Positions for a 120-degree forward arc while retaining the existing target's
// exact aim. This is a positioning heuristic, not a claim about skill hitboxes.
public static class CombatPositioning
{
    public const double ConeHalfAngle=Math.PI/3;
    public const int MaxConeTargets=10;

    public static Entity[] ConeRoster(IEnumerable<Entity> enemies,Vec position,Entity target)
    {
        var ordered=enemies.Where(e=>e.Position.Finite).OrderBy(e=>(e.Position-position).Length).ThenBy(e=>e.Id).ToList();
        if(ordered.Count<=MaxConeTargets)return ordered.ToArray();
        var selected=ordered.Take(MaxConeTargets).ToList();
        if(selected.Any(e=>e.Id==target.Id && e.Generation==target.Generation && e.Address==target.Address))return selected.ToArray();
        selected[^1]=target;
        return selected.ToArray();
    }
    public const double MaximumStep=2.5;
    public const long CheckMilliseconds=750, BurstMilliseconds=1200, RestMilliseconds=4000;

    public static Entity[] Eligible(IEnumerable<Entity> scene,IReadOnlyDictionary<uint,Health> health,
        Vec player,double radius,Func<Entity,bool> engaged,Func<Entity,Health,bool> permitted)
    {
        if(!player.Finite || !double.IsFinite(radius) || radius<=0)return [];
        return scene.GroupBy(e=>e.Id)
            .Where(g=>g.Select(e=>(e.Generation,e.Address)).Distinct().Count()==1)
            .Select(g=>g.First()).Where(e=>e.Monster && !e.PriorityLootObject && e.Position.Finite &&
                (e.Position-player).Length<=radius && health.GetValueOrDefault(e.Id) is var hp && hp.Known && !hp.Dead &&
                engaged(e) && permitted(e,hp)).OrderBy(e=>e.Id).ToArray();
    }

    public static int FrontCount(Vec position,Vec target,IReadOnlyList<Entity> enemies,double radius)
    {
        return ConeCount(position,target-position,enemies,radius,ConeHalfAngle);
    }

    public static int ConeCount(Vec position,Vec direction,IReadOnlyList<Entity> enemies,double radius,double halfAngle=ConeHalfAngle)
    {
        if(!position.Finite || !direction.Finite || direction.Length<.05 || !double.IsFinite(radius) || radius<=0 ||
            !double.IsFinite(halfAngle) || halfAngle<=0)return 0;
        return enemies.Count(e=>
        {
            Vec offset=e.Position-position;double length=offset.Length;
            return length>=.05 && length<=radius && Math.Abs(Movement.Angle(direction,offset))<=halfAngle;
        });
    }

    public static CombatPositionPlan? Choose(Vec position,Entity target,IReadOnlyList<Entity> enemies,
        double meleeRange,double radius,Func<Vec,Vec,bool> safe)
    {
        if(!position.Finite || !target.Position.Finite || !double.IsFinite(meleeRange) || meleeRange<=0 ||
            !double.IsFinite(radius) || radius<=0 || enemies.Count<2 || enemies.Any(e=>!e.Position.Finite) ||
            !enemies.Any(e=>e.Id==target.Id && e.Generation==target.Generation && e.Address==target.Address))return null;
        int before=FrontCount(position,target.Position,enemies,radius);
        CombatPositionPlan? best=null;double bestTravel=double.MaxValue;
        void Consider(Vec goal)
        {
            double travel=(goal-position).Length,reach=(goal-target.Position).Length;
            if(!goal.Finite || travel<.35 || travel>MaximumStep || reach<Math.Min(.6,meleeRange*.6) || reach>meleeRange || !safe(position,goal))return;
            int count=FrontCount(goal,target.Position,enemies,radius);
            // Keep improving until the largest safe cone is reached. Requiring
            // a majority left scattered members outside the attack arc when a
            // pack was wider than the configured nearby radius.
            if(count<=before)return;
            if(best==null || count>best.After || count==best.After && travel<bestTravel)
            {best=new(goal,before,count,enemies.Count);bestTravel=travel;}
        }
        // Short local moves plus a ring around the selected target work even
        // when the configured melee reach is smaller than the local sampling.
        for(int i=0;i<48;i++)
        {
            Vec direction=new(Math.Cos(i*Math.PI/24),Math.Sin(i*Math.PI/24));
            foreach(double step in new[]{.45,.75,1.2,1.8,MaximumStep})Consider(position+direction*step);
            Consider(target.Position+direction*(meleeRange*.85));
        }
        return best;
    }

    public static CombatSideStepPlan? ChooseSideStep(Vec position,Entity target,IReadOnlyList<Entity> enemies,
        double radius,double step,Func<Vec,bool> safe)
    {
        if(!position.Finite || !target.Position.Finite || !double.IsFinite(radius) || radius<=0 ||
            !double.IsFinite(step) || step<=0 || step>1 || enemies.Count<2 || enemies.Any(e=>!e.Position.Finite))return null;
        Vec forward=target.Position-position;double length=forward.Length;
        if(!forward.Finite || length<.1)return null;
        int before=FrontCount(position,target.Position,enemies,radius);
        CombatSideStepPlan? best=null;
        for(int sign=-1;sign<=1;sign+=2)
        {
            Vec lateral=Movement.Rotate(forward/length,sign*Math.PI/2);Vec destination=position+lateral*step;
            if(!destination.Finite || !safe(destination))continue;
            int after=FrontCount(destination,target.Position,enemies,radius);
            if(after<=before || best!=null && after<=best.After)continue;
            best=new(sign<0,destination,before,after);
        }
        return best;
    }

    public static Keys CorrectionKey(Vec position,Vec destination,double heading)
    {
        Vec delta=destination-position,forward=Movement.FromClientHeading(heading);
        Vec right=new(forward.Y,-forward.X);
        double forwardAmount=delta.X*forward.X+delta.Y*forward.Y;
        double rightAmount=delta.X*right.X+delta.Y*right.Y;
        if(forwardAmount<-.15)return Keys.S;
        return rightAmount<0 ? Keys.A : Keys.D;
    }
}

public sealed class SideStepCadence
{
    public const double Distance=.55;
    long due;
    public bool TryCheck(long now){if(now<due)return false;due=now+700;return true;}
    public void Finish(long now,bool moved)=>due=now+(moved?1800:700);
    public void Reset()=>due=0;
}

public sealed class PositioningCadence
{
    long nextCheck;
    public bool TryCheck(long now)
    {
        if(now<nextCheck)return false;
        nextCheck=now+CombatPositioning.CheckMilliseconds;return true;
    }
    public void Finished(long now)=>nextCheck=now+CombatPositioning.RestMilliseconds;
    public void Reset()=>nextCheck=0;
}

