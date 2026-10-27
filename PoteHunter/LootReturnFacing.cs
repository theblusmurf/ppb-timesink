namespace PoteHunter;

// A routine fixed-melee loot return owns its physical endpoint, not a required
// idle heading. Ordinary combat will obtain a fresh target-facing observation.
// Never consume an existing mandatory recovery/priority-facing obligation.
internal static class LootReturnFacing
{
    internal static bool Optional(Options options,bool requiredFacingPending)=>
        !requiredFacingPending && !options.GroupMode && !options.HealerMode && !options.Ranged &&
        !options.LeaveAreaWhenEmpty && Targeting.IsStationaryHuntFilter(options.Target);

    internal static async Task CompleteAsync(Options options,bool requiredFacingPending,
        Action stop,Action<CancellationToken> validate,Func<bool> settledContext,
        Func<CancellationToken,Task> restore,Action keptCurrentHeading,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();stop();validate(token);
        if(Optional(options,requiredFacingPending) && settledContext())
        {
            keptCurrentHeading();return;
        }
        await restore(token);
    }
}
