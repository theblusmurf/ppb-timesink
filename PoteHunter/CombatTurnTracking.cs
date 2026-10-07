namespace PoteHunter;

// Cooperative work inside the existing activity. No timer, background input
// writer, key/button transition, or second input owner is introduced.
internal static class CombatTurnTracking
{
    internal const double SkillFacingTolerance=.035;

    internal static bool SkillFacingRequired(bool bodyMode,bool prioritySelfHeal)=>bodyMode&&!prioritySelfHeal;

    internal static bool FacingReady(double heading,Vec delta,out double errorRadians)
    {
        errorRadians=double.NaN;
        if(!double.IsFinite(heading)||!delta.Finite||!double.IsFinite(delta.Length)||delta.Length<.01)return false;
        errorRadians=Movement.Angle(Movement.FromClientHeading(heading),delta);
        // One fresh observation inside the ordinary two-degree tolerance is
        // sufficient. Requiring consecutive stable angles would starve skills
        // on a moving target or harmless angular jitter; each activation and
        // retry must instead refresh the target direction and heading again.
        return double.IsFinite(errorRadians)&&Math.Abs(errorRadians)<=SkillFacingTolerance;
    }

    internal static bool Allowed(bool running,bool bodyMode,bool matchingTarget,bool navigation,
        bool recovery,bool repair,bool resting,bool tagging)=>
        running&&bodyMode&&matchingTarget&&!navigation&&!recovery&&!repair&&!resting&&!tagging;

    // A deferred request to recover after the owned fight is not a posture
    // transition. Keep aiming that fight while upright; an active recovery,
    // sitting/standing animation, or unreadable supported posture stays exclusive.
    internal static bool PostureAllowsTracking(bool recoveryPending,bool recoveryActive,
        bool postureSupported,RestPosture posture)=>
        !recoveryActive && (postureSupported ? posture==RestPosture.Standing : !recoveryPending);

    internal static async Task WaitAsync(int milliseconds,Func<long> clock,
        Func<bool> correct,Func<int,CancellationToken,Task> guardedDelay,CancellationToken token)
    {
        if(milliseconds<0||milliseconds>1000)throw new ArgumentOutOfRangeException(nameof(milliseconds));
        long deadline=clock()+milliseconds;
        while(clock()<deadline)
        {
            token.ThrowIfCancellationRequested();
            if(!correct())return;
            int remaining=(int)Math.Clamp(deadline-clock(),0,int.MaxValue);
            if(remaining>0)await guardedDelay(Math.Min(SmoothSteering.FrameMilliseconds,remaining),token);
        }
        token.ThrowIfCancellationRequested();
    }
}

public sealed partial class Movement
{
    bool fineFacing;
    long lastCombatTurnTrace;

    internal bool CombatSkillFacingReady(World world,Vec refreshedTargetPosition,out double errorRadians)=>
        CombatSkillFacingReady(world.LocalPlayer,refreshedTargetPosition,out errorRadians);

    internal bool CombatSkillFacingReady(Func<Entity> readPlayer,Vec refreshedTargetPosition,out double errorRadians)
    {
        // Do not reuse Forward or a successful earlier Face result: the key
        // selection delay and retargeting can make either observation stale.
        // LocalPlayer supplies position and heading from the same fresh read.
        var player=readPlayer();
        if(double.IsFinite(player.Heading))Forward=FromClientHeading(player.Heading);
        return CombatTurnTracking.FacingReady(player.Heading,refreshedTargetPosition-player.Position,out errorRadians);
    }

    internal void TrackFacing(World world,Vec delta,CancellationToken token,string owner="combat")
    {
        double heading=world.PlayerHeading();
        Forward=FromClientHeading(heading);
        double angle=delta.Length<.01?0:Angle(Forward,delta);
        // Start outside two degrees, settle inside one. This avoids chatter
        // while following a target through the old ten-degree attack dead band.
        if(Math.Abs(angle)>.035)fineFacing=true;
        else if(Math.Abs(angle)<=.018){fineFacing=false;smoothSteering.Reset();turnResponse.Reset();}
        if(!fineFacing)return;
        long now=Environment.TickCount64;
        int pixels=smoothSteering.Next(angle,heading,RadiansPerPixel,false,now,turnRateBudget,TurnSpeedDegreesPerSecond,.018);
        Vec position=world.PlayerPosition();
        if(pixels!=0)Input.Turn(pixels,token); // Existing focus/identity/health/preflight guards run here.
        ObserveTurning(position,heading,pixels,now,owner,angle,.018,token);
        if(pixels!=0&&now-lastCombatTurnTrace>=100)
        {
            lastCombatTurnTrace=now;
            TraceLog.Record("combat turn feedback",new{ErrorDegrees=angle*180/Math.PI,Heading=heading,TurnPixels=pixels,
                RadiansPerPixel,TurnSpeedDegreesPerSecond});
        }
    }
}

public sealed partial class HunterForm
{
    async Task CombatFacingWait(Movement drive,Entity expected,Options o,int milliseconds,CancellationToken token)
    {
        bool bodyMode=!o.Ranged || RangedPullEnabled(o)&&rangedPull.Phase==RangedPullPhase.Clearing;
        if(!bodyMode){await Input.Delay(milliseconds,token);return;}
        await CombatTurnTracking.WaitAsync(milliseconds,()=>Environment.TickCount64,()=>
        {
            bool returnDefense=stationaryReturnDefenseGuard is {} guard &&
                StationaryReturnDefense.CanDefend(guard());
            bool postureReady=CombatTurnTracking.PostureAllowsTracking(healingRestPending,healingRest!=null,
                world.RestSupported,world.RestState().Posture);
            if(!CombatTurnTracking.Allowed(working,bodyMode,
                lockedTarget!=null&&TargetIdentity(lockedTarget)==TargetIdentity(expected),navigationInputOwned&&!returnDefense,
                deathRecoveryActive||(deathReturnInProgress&&!returnDefense),repairInProgress,!postureReady,rangedTagging))return false;
            var live=world.Find(expected.Id);
            if(live==null||TargetIdentity(live)!=TargetIdentity(expected)||!live.Position.Finite)return false;
            var health=world.TargetHealth(live.Id);
            Vec position=world.PlayerPosition();
            if(!health.Known||health.Dead||TargetGuardReason(live,health,position,o)!=null)return false;
            if(activeHuntAnchor is Vec anchor && !o.GroupMode &&
                (live.Position-anchor).Length>activeMovementBoundary)return false;
            drive.TrackFacing(world,live.Position-position,token,$"combat:{expected.Id}:{expected.Generation}:{expected.Address}");
            return true;
        },Input.Delay,token);
        // Even an invalid/replaced/dead target gets the normal safety/preflight
        // check before the owning combat loop resumes its decisions.
        await Input.Delay(0,token);
    }
}
