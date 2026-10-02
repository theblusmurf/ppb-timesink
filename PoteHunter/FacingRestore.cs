namespace PoteHunter;

internal static class FacingRestore
{
    public static async Task<int> RunAsync(Func<CancellationToken,Task<bool>> face,Action reset,Action stop,
        Func<int,CancellationToken,Task> delay,Func<long> clock,Func<TurnUnresponsiveException> failure,
        CancellationToken token,int attempts=1,Action<int>? retry=null)
    {
        if(attempts is <1 or >3)throw new ArgumentOutOfRangeException(nameof(attempts));
        int observations=0;
        for(int attempt=1;attempt<=attempts;attempt++)
        {
            reset();long deadline=clock()+2000;
            try
            {
                while(clock()<deadline)
                {
                    token.ThrowIfCancellationRequested();observations++;
                    if(await face(token))return observations;
                    await delay(20,token);
                }
                throw failure();
            }
            catch(TurnUnresponsiveException) when(attempt<attempts)
            {
                stop();reset();retry?.Invoke(attempt);
                await delay(120,token);
            }
        }
        throw failure();
    }
}

internal sealed class AnchorReturnException(string message) : Exception(message);
