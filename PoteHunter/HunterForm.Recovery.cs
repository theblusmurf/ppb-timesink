namespace PoteHunter;

public sealed partial class HunterForm
{
    string savedReturnPhase="";
    async Task ReturnAndWaitForLeashedTarget(Movement drive,Entity target,Vec anchor,Options options,
        Func<CancellationToken,Task> restoreFacing,CancellationToken token)
    {
        var originalBoundary=activeMovementBoundary;
        bool originalReturning=returningFromPriority;
        drive.StopApproach();Input.Release(preserveNearbyPickup:true);lockedTarget=null;
        returningFromPriority=true;
        navigation.BeginGoal("return after engaged enemy left farming range");
        try
        {
            await LeashReturn.Run(()=>PriorityGamekeeper(options)!=null && !Targeting.IsGamekeeper(target) || (world.PlayerPosition()-anchor).Length<=.5,
                async()=>
                {
                    message="Enemy left the farming area; returning to the saved anchor";
                    await NavigateTo(drive,anchor,anchor,options,token,
                        boundaryRadius:Math.Max(activeCompletionBoundary,(world.PlayerPosition()-anchor).Length+2),watchTurns:true);
                    await Input.Delay(30,token);
                },async()=>
                {
                    drive.StopApproach();
                    if((world.PlayerPosition()-anchor).Length<=.5)await restoreFacing(token);
                },()=>
                {
                    if(PriorityGamekeeper(options)!=null && !Targeting.IsGamekeeper(target))return true;
                    var current=world.Find(target.Id);
                    if(current==null || TargetIdentity(current)!=TargetIdentity(target))return true;
                    var hp=world.TargetHealth(target.Id);
                    if(!hp.Known || hp.Dead)return true;
                    if((current.Position-anchor).Length<=(double)options.HuntRadius)return true;
                    // Other engaged monsters may already be back in melee range.
                    return entities.Any(e=>encounter.IsEngaged(e) && e.Id!=target.Id &&
                        (e.Position-world.PlayerPosition()).Length<=(double)options.MeleeRange &&
                        world.TargetHealth(e.Id) is {Known:true,Dead:false});
                },async()=>
                {
                    message="At saved anchor; waiting for engaged enemies to return";
                    await Input.Delay(100,token);await TryHeal(drive,options,token);
                },()=>Environment.TickCount64,token);
        }
        finally{drive.StopApproach();activeMovementBoundary=originalBoundary;returningFromPriority=originalReturning;}
    }
}
