namespace PoteHunter;

// Cooperative work inside the existing activity. No timer, background input
// writer, key/button transition, or second input owner is introduced.
internal static class CombatTurnTracking
{
    internal static bool Allowed(bool running,bool bodyMode,bool matchingTarget,bool navigation,
        bool recovery,bool repair,bool resting,bool tagging)=>
        running&&bodyMode&&matchingTarget&&!navigation&&!recovery&&!repair&&!resting&&!tagging;

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
    internal void TrackFacing(World world,Vec delta,CancellationToken token)
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
        if(turnResponse.Observe(position,heading,pixels,now,true))throw new TurnUnresponsiveException(position,Forward);
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
            if(!CombatTurnTracking.Allowed(working,bodyMode,
                lockedTarget!=null&&TargetIdentity(lockedTarget)==TargetIdentity(expected),navigationInputOwned,
                deathRecoveryActive||deathReturnInProgress,repairInProgress,healingRestPending,rangedTagging))return false;
            var live=world.Find(expected.Id);
            if(live==null||TargetIdentity(live)!=TargetIdentity(expected)||!live.Position.Finite)return false;
            var health=world.TargetHealth(live.Id);
            Vec position=world.PlayerPosition();
            if(!health.Known||health.Dead||TargetGuardReason(live,health,position,o)!=null)return false;
            if(activeHuntAnchor is Vec anchor && !o.GroupMode &&
                (live.Position-anchor).Length>activeMovementBoundary)return false;
            drive.TrackFacing(world,live.Position-position,token);
            return true;
        },Input.Delay,token);
        // Even an invalid/replaced/dead target gets the normal safety/preflight
        // check before the owning combat loop resumes its decisions.
        await Input.Delay(0,token);
    }
}
