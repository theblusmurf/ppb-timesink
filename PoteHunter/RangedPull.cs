namespace PoteHunter;

public enum RangedPullPhase { Idle, Tagging, Gathering, Clearing }

public readonly record struct MeleeClusterFocus(Entity Target,int Density);

public sealed class RangedPull
{
    readonly HashSet<(uint Id,uint Generation,long Address)> attempted=[];
    long phaseStartedAt;
    long lastUpdateAt;
    Vec? roamWaypoint;

    public RangedPullPhase Phase { get; private set; }
    public int AttemptedCount => attempted.Count;
    public Vec? RoamWaypoint => roamWaypoint;
    // Cooldown activation proves that Firing was submitted even when the shot
    // was blocked or missed. It records a pull attempt; nearby observations,
    // rather than this signal, decide when the pack is ready to clear.
    public static bool ShotConfirmed(Health before,Health after,bool skillActivated) => skillActivated ||
        before.Known && after.Known && before.Maximum==after.Maximum && after.Current<before.Current;

    // Pack growth stops at the user's existing recovery threshold regardless of
    // whether automatic healing is enabled or a healing item is currently ready.
    public static bool ShouldStopTagging(Health playerHealth,decimal healBelowPercent,bool recoveryPending) =>
        recoveryPending || HealingRest.ShouldTrigger(playerHealth,healBelowPercent);

    public static bool WithinNearby3D(Entity entity,Vec player,double playerHeight,double radius)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if(!entity.Position.Finite||!player.Finite||!double.IsFinite(entity.Height)||!double.IsFinite(playerHeight) ||
            !double.IsFinite(radius)||radius<=0)return false;
        double dx=entity.Position.X-player.X,dy=entity.Position.Y-player.Y,dz=entity.Height-playerHeight;
        return dx*dx+dy*dy+dz*dz<=radius*radius;
    }

    public bool ConfirmShot(Entity entity,Health before,Health after,bool skillActivated,long now)
    {
        if(!ShotConfirmed(before,after,skillActivated))return false;
        MarkAttempt(entity,now);
        return true;
    }
    public bool Active => Phase!=RangedPullPhase.Idle;

    // A basic attack swings through a forward sector. Choose the sector with
    // the most nearby pack members rather than just the closest individual.
    public static MeleeClusterFocus? ChooseMeleeCluster(IEnumerable<Entity> candidates,Vec player)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if(!player.Finite)throw new ArgumentOutOfRangeException(nameof(player));
        var nearby=candidates.Where(entity=>entity!=null && entity.Position.Finite && (entity.Position-player).Length>=.01).ToArray();
        if(nearby.Length==0)return null;
        const double halfSector=Math.PI/4; // 90-degree swing sector
        return nearby.Select(candidate=>
            new MeleeClusterFocus(candidate,nearby.Count(other=>Math.Abs(Movement.Angle(candidate.Position-player,other.Position-player))<=halfSector)))
            .OrderByDescending(focus=>focus.Density)
            .ThenBy(focus=>(focus.Target.Position-player).Length)
            .ThenBy(focus=>focus.Target.Id)
            .First();
    }

    static (uint Id,uint Generation,long Address) Identity(Entity entity)=>(entity.Id,entity.Generation,entity.Address);

    public bool Contains(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return attempted.Contains(Identity(entity));
    }

    public void Reset()
    {
        attempted.Clear();
        Phase=RangedPullPhase.Idle;
        phaseStartedAt=0;
        lastUpdateAt=0;
        roamWaypoint=null;
    }

    public void Begin(long now)
    {
        if(Phase!=RangedPullPhase.Idle || attempted.Count!=0)
            throw new InvalidOperationException("Reset the previous ranged pull before beginning another one.");
        Phase=RangedPullPhase.Tagging;
        phaseStartedAt=lastUpdateAt=now;
    }

    // Call only after the attack input was actually submitted. Merely choosing a
    // candidate must never consume one of the bounded pull attempts. The
    // confirmed identities are the pack contract through its melee phase.
    public void MarkAttempt(Entity entity,long now)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if(Phase!=RangedPullPhase.Tagging)throw new InvalidOperationException("Ranged pull attempts can only be marked while tagging.");
        ObserveTime(now);
        attempted.Add(Identity(entity));
    }

    public void BeginGathering(long now)
    {
        if(Phase!=RangedPullPhase.Tagging)throw new InvalidOperationException("Gathering can only follow tagging.");
        ObserveTime(now);
        Phase=RangedPullPhase.Gathering;
        phaseStartedAt=now;
        roamWaypoint=null;
    }

    public static double RoamShootingRadius(double attackRange)
    {
        if(!double.IsFinite(attackRange)||attackRange<=0)throw new ArgumentOutOfRangeException(nameof(attackRange));
        // Preserve a full unit where practical without eliminating movement for
        // unusually small configured ranges.
        return Math.Max(attackRange*.5,attackRange-1);
    }

    public Vec? ChooseRoamWaypoint(Vec player,Vec anchor,Vec target,double attackRange,double huntRadius,Func<Vec,Vec,bool> canAdvance)
    {
        if(Phase!=RangedPullPhase.Tagging){roamWaypoint=null;return null;}
        if(!player.Finite||!anchor.Finite||!target.Finite)throw new ArgumentOutOfRangeException(nameof(player));
        if(!double.IsFinite(attackRange)||attackRange<=0||!double.IsFinite(huntRadius)||huntRadius<=0)
            throw new ArgumentOutOfRangeException(nameof(attackRange));
        ArgumentNullException.ThrowIfNull(canAdvance);
        double shootingRadius=RoamShootingRadius(attackRange);
        if(roamWaypoint is Vec retained && (retained-player).Length>.65 &&
            Within(retained,anchor,huntRadius) && Within(retained,target,shootingRadius) &&
            canAdvance(player,retained))return retained;
        roamWaypoint=null;
        double radius=Math.Min(attackRange*.5,huntRadius*.5);
        if(radius<1)return null;
        double baseAngle=Math.Atan2(player.Y-anchor.Y,player.X-anchor.X);
        for(int i=1;i<=8;i++)
        {
            double angle=baseAngle+i*Math.PI/4;
            Vec candidate=anchor+new Vec(Math.Cos(angle),Math.Sin(angle))*radius;
            // The shooting-range disk is convex. With the currently validated
            // player position and an endpoint inside this smaller disk, the
            // entire direct segment remains inside the static range boundary.
            if((candidate-player).Length>=1 && Within(candidate,anchor,huntRadius) &&
                Within(candidate,target,shootingRadius) && canAdvance(player,candidate))
                return roamWaypoint=candidate;
        }
        return null;
    }

    public void Update(IReadOnlyList<Entity> entities,IReadOnlyDictionary<uint,Health> health,Vec player,
        double gatherRadius,long now,int pullCount,int tagTimeoutSeconds,int gatherTimeoutSeconds,bool threatened,bool hasEngaged)
    {
        ArgumentNullException.ThrowIfNull(entities);ArgumentNullException.ThrowIfNull(health);
        if(!player.Finite)throw new ArgumentOutOfRangeException(nameof(player));
        if(!double.IsFinite(gatherRadius) || gatherRadius<=0)throw new ArgumentOutOfRangeException(nameof(gatherRadius));
        if(pullCount<=0)throw new ArgumentOutOfRangeException(nameof(pullCount));
        if(tagTimeoutSeconds<=0)throw new ArgumentOutOfRangeException(nameof(tagTimeoutSeconds));
        if(gatherTimeoutSeconds<=0)throw new ArgumentOutOfRangeException(nameof(gatherTimeoutSeconds));
        if(Phase==RangedPullPhase.Idle)return;
        ObserveTime(now);

        if(Phase==RangedPullPhase.Tagging)
        {
            if(threatened)
                BeginGathering(now);
            else if(attempted.Count>=pullCount)
            {
                Phase=RangedPullPhase.Clearing;phaseStartedAt=now;roamWaypoint=null;
            }
        }

        var ambiguous=entities.GroupBy(entity=>entity.Id)
            .Where(group=>group.Select(Identity).Distinct().Skip(1).Any())
            .Select(group=>group.Key).ToHashSet();
        var observedLiving=entities.Where(entity=>!ambiguous.Contains(entity.Id) && attempted.Contains(Identity(entity)) &&
            health.GetValueOrDefault(entity.Id) is {Known:true,Dead:false}).Select(Identity).ToHashSet();
        if(Phase==RangedPullPhase.Clearing)
        {
            if(observedLiving.Count==0)
            {
                attempted.Clear();Phase=RangedPullPhase.Tagging;phaseStartedAt=now;roamWaypoint=null;
            }
            return;
        }

        if(Phase==RangedPullPhase.Gathering && !threatened)
        {
            if(observedLiving.Count<pullCount)
            {
                attempted.IntersectWith(observedLiving);
                Phase=RangedPullPhase.Tagging;phaseStartedAt=now;roamWaypoint=null;
            }
        }
    }

    public int ApplyNearbyPolicy(IEnumerable<Entity> approvedNearby,IReadOnlyDictionary<uint,Health> health,Vec player,
        double radius,int minimumNearby,long now)
    {
        ArgumentNullException.ThrowIfNull(approvedNearby);ArgumentNullException.ThrowIfNull(health);
        if(!player.Finite)throw new ArgumentOutOfRangeException(nameof(player));
        if(!double.IsFinite(radius)||radius<=0)throw new ArgumentOutOfRangeException(nameof(radius));
        if(minimumNearby<=0)throw new ArgumentOutOfRangeException(nameof(minimumNearby));
        if(Phase==RangedPullPhase.Idle)return 0;
        ObserveTime(now);
        var observed=approvedNearby.ToArray();
        var ambiguous=observed.GroupBy(entity=>entity.Id)
            .Where(group=>group.Select(Identity).Distinct().Skip(1).Any())
            .Select(group=>group.Key).ToHashSet();
        int nearby=observed.GroupBy(Identity).Select(group=>group.First()).Count(entity=>
            !ambiguous.Contains(entity.Id) && entity.Monster && entity.Targetable && !entity.PriorityLootObject &&
            !Targeting.IsGamekeeper(entity) && entity.Position.Finite && Within(entity.Position,player,radius) &&
            health.GetValueOrDefault(entity.Id) is {Known:true,Dead:false});
        return nearby;
    }

    public Entity? ChooseNext(IEnumerable<Entity> permitted,IReadOnlyDictionary<uint,Health> health,Vec player,
        Vec anchor,double huntRadius,double attackRange)
    {
        ArgumentNullException.ThrowIfNull(permitted);ArgumentNullException.ThrowIfNull(health);
        if(!player.Finite)throw new ArgumentOutOfRangeException(nameof(player));
        if(!anchor.Finite)throw new ArgumentOutOfRangeException(nameof(anchor));
        if(!double.IsFinite(huntRadius) || huntRadius<=0)throw new ArgumentOutOfRangeException(nameof(huntRadius));
        if(!double.IsFinite(attackRange) || attackRange<=0)throw new ArgumentOutOfRangeException(nameof(attackRange));
        if(Phase!=RangedPullPhase.Tagging)return null;

        var observed=permitted.ToArray();
        var ambiguous=observed.GroupBy(entity=>entity.Id)
            .Where(group=>group.Select(Identity).Distinct().Skip(1).Any())
            .Select(group=>group.Key).ToHashSet();
        return observed.GroupBy(Identity).Select(group=>group.First())
            .Where(entity=>!ambiguous.Contains(entity.Id) && entity.Monster && entity.Targetable &&
                !entity.PriorityLootObject && !Targeting.IsGamekeeper(entity) && entity.Position.Finite &&
                !attempted.Contains(Identity(entity)) && health.GetValueOrDefault(entity.Id) is {Known:true,Dead:false} hp &&
                hp.Current==hp.Maximum && Within(entity.Position,anchor,huntRadius) && Within(entity.Position,player,attackRange))
            .OrderBy(entity=>DistanceSquared(entity.Position,player)).ThenBy(entity=>entity.Id).FirstOrDefault();
    }

    void ObserveTime(long now)
    {
        if(now<lastUpdateAt)throw new InvalidOperationException("Ranged pull observations arrived out of order.");
        lastUpdateAt=now;
    }

    static bool Elapsed(long now,long since,int seconds)=>now-since>=seconds*1000L;
    static bool Within(Vec a,Vec b,double radius)
    {
        double x=Math.Abs(a.X-b.X),y=Math.Abs(a.Y-b.Y);
        if(x>radius || y>radius)return false;
        x/=radius;y/=radius;
        return x*x+y*y<=1;
    }
    static double DistanceSquared(Vec a,Vec b)
    {
        double scale=Math.Max(Math.Abs(a.X-b.X),Math.Abs(a.Y-b.Y));
        if(scale==0)return 0;
        double x=(a.X-b.X)/scale,y=(a.Y-b.Y)/scale;
        return scale*scale*(x*x+y*y);
    }
}
