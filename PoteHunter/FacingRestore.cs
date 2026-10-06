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
        Action<FacingRestoreFailure>? failureObserved=null,Func<TurnUnresponsiveException>? deadlineFailure=null,
        Func<double>? errorRadians=null)
    {
        if(attempts is <1 or >3)throw new ArgumentOutOfRangeException(nameof(attempts));
        if(timeoutMilliseconds is <2000 or >8000)throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        int observations=0;long runStartedAt=clock(),hardDeadline=runStartedAt+20000;
        double ReadError()
        {
            double measured=errorRadians!();
            if(!double.IsFinite(measured))throw new InvalidOperationException("Saved facing error is unavailable.");
            return Math.Abs(Math.Atan2(Math.Sin(measured),Math.Cos(measured)));
        }
        for(int attempt=1;attempt<=attempts;attempt++)
        {
            reset();long startedAt=clock(),deadline=Math.Min(hardDeadline,startedAt+timeoutMilliseconds);
            bool deadlineExpired=false;
            double bestError=double.PositiveInfinity;int progressExtensions=0;
            try
            {
                token.ThrowIfCancellationRequested();
                if(errorRadians!=null)bestError=ReadError();
                while(clock()<deadline && clock()<hardDeadline)
                {
                    token.ThrowIfCancellationRequested();observations++;
                    if(await face(token))
                    {
                        if(clock()<=hardDeadline)return observations;
                        break;
                    }
                    if(clock()>=hardDeadline)break;
                    if(errorRadians!=null)
                    {
                        double actualError=ReadError();
                        // Only fresh measured improvement of the best error
                        // grants time. Elapsed time, sent pixels, noise and
                        // oscillation back toward an older error do not.
                        if(bestError-actualError>=.01)
                        {
                            bestError=actualError;progressExtensions++;
                            deadline=Math.Min(hardDeadline,Math.Max(deadline,clock()+timeoutMilliseconds));
                        }
                    }
                    await delay(20,token);
                }
                deadlineExpired=true;
                throw deadlineFailure?.Invoke() ?? failure();
            }
            catch(TurnUnresponsiveException)
            {
                failureObserved?.Invoke(new(deadlineExpired?"Deadline":"NoProgress",attempt,
                    Math.Max(0,clock()-startedAt),timeoutMilliseconds,
                    errorRadians!=null?bestError:null,progressExtensions,Math.Max(0,clock()-runStartedAt)));
                if(attempt==attempts || clock()>=hardDeadline)throw;
                stop();reset();retry?.Invoke(attempt);
                await delay(120,token);
            }
        }
        throw failure();
    }
}

internal readonly record struct FacingRestoreFailure(string Reason,int Attempt,long ElapsedMilliseconds,int TimeoutMilliseconds,
    double? BestErrorRadians=null,int ProgressExtensions=0,long TotalElapsedMilliseconds=0);

internal sealed class AnchorReturnException(string message) : Exception(message);
