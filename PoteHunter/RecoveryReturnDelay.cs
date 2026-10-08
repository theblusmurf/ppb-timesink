namespace PoteHunter;

// This phase belongs to the death episode, before any saved-route movement.
// Completed waits survive route retries; another death requires a new wait.
internal static class RecoveryReturnDelay
{
    internal const int MaximumSeconds=600;
    internal const int SliceMilliseconds=100;

    internal static async Task Run(DeathRecoveryState state,long episode,bool enabled,int seconds,
        Func<long> now,Action validate,Action release,Func<int,CancellationToken,Task> delay,
        Action<long> status,CancellationToken token)
    {
        void Check()
        {
            token.ThrowIfCancellationRequested();
            if(!state.Pending || state.Episode!=episode || !state.PostRevivalPrepared || !state.RepairCompleted)
                throw new DeathRecoveryRequiredException();
            validate();
            token.ThrowIfCancellationRequested();
            // Validation may observe a new death. Never complete its episode
            // using the older asynchronous wait.
            if(state.Episode!=episode || !state.Pending || !state.RepairCompleted)
                throw new DeathRecoveryRequiredException();
        }
        try
        {
            release();
            Check();
            if(state.ReturnDelayCompleted)return;
            long last=now();
            if(!state.BeginReturnDelay(episode,enabled?Math.Clamp(seconds,0,MaximumSeconds)*1000:0,last))
                throw new DeathRecoveryRequiredException();
            long deadline=state.ReturnDelayReadyAt!.Value;
            while(true)
            {
                Check();long current=now();
                if(current<last)throw new InvalidOperationException("Recovery return delay clock moved backwards; stopped.");
                last=current;long remaining=deadline-current;
                if(remaining<=0)break;
                status(remaining);
                await delay((int)Math.Min(SliceMilliseconds,remaining),token);
            }
            Check();
            if(!state.MarkReturnDelayCompleted(episode))throw new DeathRecoveryRequiredException();
        }
        finally {release();}
    }
}
