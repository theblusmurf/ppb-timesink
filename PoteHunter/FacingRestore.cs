namespace PoteHunter;

internal static class FacingRestore
{
    // Even a responsive half-turn takes six seconds at the supported 30°/s
    // cap. Leave two seconds for delayed measured feedback and settling,
    // while retaining a hard bound for every attempt.
    public static int TimeoutMilliseconds(double degreesPerSecond)
    {
        if(!double.IsFinite(degreesPerSecond) || degreesPerSecond is <30 or >360)
            throw new ArgumentOutOfRangeException(nameof(degreesPerSecond));
        return Math.Clamp((int)Math.Ceiling(180000/degreesPerSecond)+2000,2000,8000);
    }

    public static async Task<int> RunAsync(Func<CancellationToken,Task<bool>> face,Action reset,Action stop,
        Func<int,CancellationToken,Task> delay,Func<long> clock,Func<TurnUnresponsiveException> failure,
        CancellationToken token,int attempts=1,Action<int>? retry=null,int timeoutMilliseconds=2000,
        Action<FacingRestoreFailure>? failureObserved=null,Func<TurnUnresponsiveException>? deadlineFailure=null)
    {
        if(attempts is <1 or >3)throw new ArgumentOutOfRangeException(nameof(attempts));
        if(timeoutMilliseconds is <2000 or >8000)throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        int observations=0;
        for(int attempt=1;attempt<=attempts;attempt++)
        {
            reset();long startedAt=clock(),deadline=startedAt+timeoutMilliseconds;
            bool deadlineExpired=false;
            try
            {
                while(clock()<deadline)
                {
                    token.ThrowIfCancellationRequested();observations++;
                    if(await face(token))return observations;
                    await delay(20,token);
                }
                deadlineExpired=true;
                throw deadlineFailure?.Invoke() ?? failure();
            }
            catch(TurnUnresponsiveException)
            {
                failureObserved?.Invoke(new(deadlineExpired?"Deadline":"NoProgress",attempt,
                    Math.Max(0,clock()-startedAt),timeoutMilliseconds));
                if(attempt==attempts)throw;
                stop();reset();retry?.Invoke(attempt);
                await delay(120,token);
            }
        }
        throw failure();
    }
}

internal readonly record struct FacingRestoreFailure(string Reason,int Attempt,long ElapsedMilliseconds,int TimeoutMilliseconds);

internal sealed class AnchorReturnException(string message) : Exception(message);
