namespace PoteHunter;

// A positive HP sample confirms revival, but only arrival completes recovery.
internal sealed class DeathRecoveryState
{
    public bool Pending { get; private set; }
    public long ObservedAt { get; private set; }
    public long Episode { get; private set; }
    public bool PostRevivalPrepared { get; private set; }
    public bool RepairCompleted { get; private set; }
    bool dead;

    public bool Observe(Health health,long now)
    {
        if(!health.Known)return false;
        if(!health.Dead){dead=false;return false;}
        Pending=true;
        if(dead)return false;
        dead=true;ObservedAt=now;Episode++;
        PostRevivalPrepared=false;RepairCompleted=false;
        return true;
    }

    // Arrival can fail after revival and repair succeeded. Keep those completed
    // phases with the pending return rather than repeating inventory actions.
    // An asynchronous completion from an older death must not complete a newer one.
    public bool MarkPostRevivalPrepared(long episode)
    {
        if(!Pending || dead || Episode!=episode)return false;
        PostRevivalPrepared=true;return true;
    }
    public bool MarkRepairCompleted(long episode)
    {
        if(!Pending || dead || !PostRevivalPrepared || Episode!=episode)return false;
        RepairCompleted=true;return true;
    }

    public long ReadyAt(int delaySeconds,bool visual=false)=>ObservedAt+
        Math.Max(visual?VisualRevival.DeathWaitMilliseconds:500,Math.Clamp(delaySeconds,0,600)*1000L);
    public static void InterruptIfDead(Health health,bool enabled,Action<Health> observeDeath)
    {
        if(!enabled || !health.Dead)return;
        observeDeath(health);
        throw new DeathRecoveryRequiredException();
    }
    public void Reset()
    {
        Pending=false;dead=false;ObservedAt=0;Episode++;
        PostRevivalPrepared=false;RepairCompleted=false;
    }
}

internal sealed class DeathRecoveryRequiredException : Exception;

internal static class RecoveryRouting
{
    public static string? SavedReturnProblem(SavedNavigationRoute? route,int zone,string character,double height,Vec anchor)
    {
        if(route==null || !RecoveryTravel.Recorded(route))return "Record a return route with Home / End in Navigation, or turn off Auto revive + return.";
        if(!Compatible(route,zone,character,height) || (route.Anchor-anchor).Length>2.5)
            return "The recorded route does not match this character, map, or anchor. Select the saved spot before starting.";
        return null;
    }
    // The client may recreate the local body on revival. Retain account/body
    // identity checks while allowing its allocation and generation to change.
    public static bool SameCharacter(Entity before,Entity after)=>
        before.Id==after.Id && before.Name==after.Name && before.Model==after.Model;

    // Height describes the saved farming endpoint, not the elevation of every
    // recorded path segment. Travel can cross hills before reaching that floor.
    public static bool CompatibleIdentity(SavedNavigationRoute route,int zone,string character)=>
        route.Zone==zone && route.Anchor.Finite && double.IsFinite(route.Heading) &&
        (string.IsNullOrWhiteSpace(route.Character) || route.Character.Equals(character,StringComparison.OrdinalIgnoreCase));

    public static bool Compatible(SavedNavigationRoute route,int zone,string character,double huntHeight)=>
        CompatibleIdentity(route,zone,character) && double.IsFinite(route.Height) && double.IsFinite(huntHeight) &&
        (route.Height<=0 || huntHeight<=0 || Math.Abs(route.Height-huntHeight)<2);

    public static bool Occupied(Vec anchor,double height,double radius,IEnumerable<Entity> entities,uint selfId)=>
        CombatCourtesy.PlayerNear(anchor,entities.Where(e=>height<=0 || e.Height<=0 || Math.Abs(e.Height-height)<2),
            selfId,Math.Max(3,radius))!=null;

}

internal sealed class RecoveryPath
{
    internal const double SteeringLookaheadUnits=3.5;
    internal const double SteeringCorridorUnits=.35;
    const double CurveHorizonUnits=4;
    const double CurveBrakingDegrees=35;
    const int MaximumLookaheadSegments=4;
    readonly Vec[] points;
    int index;
    Vec? previousPosition;
    public int Index=>index;
    public RecoveryPath(IEnumerable<Vec> waypoints,Vec anchor)
    {
        var source=waypoints.Append(anchor).ToArray();
        if(source.Any(p=>!p.Finite))throw new RouteUnavailableException("Recovery route contains an invalid position.");
        // Plans already include their destination. Keep the actual anchor only
        // once, so it receives final-arrival braking before we pass it.
        points=source.Where((point,at)=>at==source.Length-1 || point!=source[at+1]).ToArray();
    }
    // A clear, short lookahead avoids turning sideways just to touch the
    // projected route entry. Sharp corners and the final anchor stay exact.
    public bool Final=>index==points.Length-1;
    // Capture the actual corner, not both that corner and its preceding sample.
    // Approach already brakes early from the measured speed when a required
    // checkpoint becomes current. A mandatory first connector also needs that
    // braking; it has no incoming segment to prove an overshoot safely.
    public double ArrivalTolerance=>Final?.5:index<points.Length &&
        (index==0 || index>0 && !Straight(index-1) ||
         index==points.Length-2 && (points[index+1]-points[index]).Length<=2.5) ? .6 : 0;
    // Several small bends shorten steering lookahead without turning every
    // sample into a stop/aim/pulse/settle cycle. Required corners remain exact.
    public double UpcomingCurvatureDegrees=>index<points.Length?Curvature(index):0;
    bool CumulativeCurve(int at)=>Curvature(at)>=CurveBrakingDegrees;
    double Curvature(int at)
    {
        double turn=Bend(at),distance=0;
        for(int next=at+1;next<points.Length-1 && next<=at+MaximumLookaheadSegments;next++)
        {
            distance+=(points[next]-points[next-1]).Length;
            if(distance>CurveHorizonUnits)break;
            turn+=Bend(next);
        }
        return turn;
    }
    double Bend(int at)
    {
        if(at<=0 || at>=points.Length-1)return 0;
        Vec incoming=points[at]-points[at-1],outgoing=points[at+1]-points[at];
        if(incoming.Length*outgoing.Length<=.0001)return 0;
        return Math.Abs(Movement.Angle(incoming,outgoing))*180/Math.PI;
    }
    bool Straight(int at)
    {
        if(at+2>=points.Length)return true;
        Vec outgoing=points[at+1]-points[at],following=points[at+2]-points[at+1];
        double lengths=outgoing.Length*following.Length;
        if(lengths<=.0001)return true;
        return (outgoing.X*following.X+outgoing.Y*following.Y)/lengths>=.94;
    }
    public Vec? Next(Vec current,Func<Vec,Vec,bool>? clear=null)
    {
        if(!current.Finite)throw new RouteUnavailableException("Character position is unavailable during recovery.");
        Vec? before=previousPosition;previousPosition=current;
        // Intermediate waypoints are sampled closely together. Never advance
        // merely because one frame of movement was sent to the client.
        while(index<points.Length && (points[index]-current).Length<=(index==points.Length-1?.5:.6))index++;
        // Advance only locally, with an independently checked segment. Never
        // search globally for a closer point on a loop or skip a sharp bend.
        if(clear!=null)
        {
            // A delayed client position can cross a straight sample by more
            // than its proximity window. Require an observed forward crossing
            // inside the local route corridor, rather than turning back toward
            // a checkpoint already passed. New paths have no crossing history.
            if(before is Vec observed && PassedCheckpoint(observed,current,clear))index++;
            while(index<points.Length-1 && (points[index]-current).Length<=1.25 &&
                (points[index+1]-current).Length<=4 &&
                (index==0 || Straight(index-1)) && Straight(index) && !CumulativeCurve(index) && clear(current,points[index+1]))index++;
        }
        return index<points.Length?points[index]:null;
    }

    bool PassedCheckpoint(Vec before,Vec current,Func<Vec,Vec,bool> clear)
    {
        if(index<=0 || index>=points.Length-1 || ArrivalTolerance>0)return false;
        Vec checkpoint=points[index],incoming=checkpoint-points[index-1],outgoing=points[index+1]-checkpoint;
        if(incoming.Length<=.0001 || outgoing.Length<=.0001)return false;
        Vec direction=incoming/incoming.Length;
        double beforeAlong=Dot(before-checkpoint,direction),afterAlong=Dot(current-checkpoint,direction);
        // Bounded local observations cannot identify a remote connector, a
        // return leg on a loop, a sideways crossing, or a backwards step as
        // forward route progress. The next checkpoint must still lie ahead.
        if(beforeAlong>0 || beforeAlong<-SteeringLookaheadUnits || afterAlong<=0 || afterAlong>2.5 ||
            (points[index+1]-current).Length>4 || Dot(points[index+1]-current,direction)<=0 ||
            (before-checkpoint-direction*beforeAlong).Length>SteeringCorridorUnits ||
            (current-checkpoint-direction*afterAlong).Length>SteeringCorridorUnits)return false;
        Vec onward=outgoing/outgoing.Length;
        double onwardAlong=Dot(current-checkpoint,onward);
        if(onwardAlong<0 || (current-checkpoint-onward*onwardAlong).Length>SteeringCorridorUnits)return false;
        return clear(before,current) && clear(checkpoint,current) && clear(current,points[index+1]);
    }

    static double Dot(Vec a,Vec b)=>a.X*b.X+a.Y*b.Y;

    // Steering is separate from checkpoint progression. Call Next first and
    // keep its checkpoint for arrival, progress and blocked-route checks. This
    // aim point is only for turning during continuous approach; precise arrival
    // must still use the checkpoint and ArrivalTolerance.
    public Vec SteeringGoal(Vec current,Func<Vec,Vec,bool>? clear=null)
    {
        if(!current.Finite || index>=points.Length)
            throw new RouteUnavailableException("A live route checkpoint is required for steering.");
        Vec checkpoint=points[index];
        double distance=(checkpoint-current).Length;
        double lookahead=Math.Clamp(SteeringLookaheadUnits/(1+UpcomingCurvatureDegrees/90),1.75,SteeringLookaheadUnits);
        if(clear==null || index==0 || ArrivalTolerance>0 || distance>=lookahead ||
            !clear(current,checkpoint))return checkpoint;
        // The chord corridor below bounds the lookahead after the checkpoint.
        // Independently require the current position inside the original
        // incoming segment: shortening the chord must not turn an off-route
        // connector into permission to skip its required entry point.
        Vec incoming=checkpoint-points[index-1];double square=Dot(incoming,incoming);
        if(square<=.0001)return checkpoint;
        double progress=Math.Clamp(Dot(current-points[index-1],incoming)/square,0,1);
        if((current-(points[index-1]+incoming*progress)).Length>SteeringCorridorUnits)return checkpoint;
        double remaining=lookahead-distance;
        Vec accepted=checkpoint,previous=checkpoint;
        var corridor=new List<Vec>{current,checkpoint};
        for(int next=index+1;next<points.Length-1 && next<=index+MaximumLookaheadSegments && remaining>.01;next++)
        {
            // Do not look through a required corner, a U-turn, or the final
            // anchor. The controller must capture those checkpoints exactly.
            if(!Straight(next-1) || !Straight(next))break;
            Vec delta=points[next]-previous;double length=delta.Length;
            if(length<=.0001){previous=points[next];continue;}
            double step=Math.Min(remaining,length);
            Vec candidate=previous+delta*(step/length);
            if(!clear(previous,candidate) || !InsideSteeringCorridor(current,candidate,corridor) ||
                !clear(current,candidate))break;
            accepted=candidate;remaining-=step;
            if(step<length)break;
            previous=points[next];corridor.Add(previous);
        }
        return accepted;
    }
    static bool InsideSteeringCorridor(Vec from,Vec to,IEnumerable<Vec> vertices)
    {
        Vec segment=to-from;double square=segment.X*segment.X+segment.Y*segment.Y;
        if(square<=.0001)return false;
        double previousProjection=0;
        foreach(Vec vertex in vertices)
        {
            Vec delta=vertex-from;
            double projection=(delta.X*segment.X+delta.Y*segment.Y)/square;
            // Monotonic projection prevents a locally nearby loop/backtrack
            // from being treated as a straight shortcut. Checking every vertex
            // bounds the complete local polyline against the candidate chord.
            if(projection<previousProjection-.0001 || projection>1.0001 ||
                (vertex-(from+segment*Math.Clamp(projection,0,1))).Length>SteeringCorridorUnits)return false;
            previousProjection=projection;
        }
        return true;
    }
}
