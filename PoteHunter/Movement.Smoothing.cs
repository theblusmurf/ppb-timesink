namespace PoteHunter;

// Bound changes by elapsed time and observed heading, not by how often the
// caller happens to poll. This controller does not emit any input.
internal sealed class SmoothSteering
{
    long lastAt, sentAt;
    double velocity, sentHeading;
    bool started, awaitingHeading;
    public void Reset() { started=awaitingHeading=false; velocity=0; }
    public int Next(double error,double heading,double sensitivity,bool walking,long now)
    {
        int limit=Math.Abs(Movement.CalculateTurn(error,sensitivity,walking));
        if(limit==0) { Reset(); return 0; }
        if(!double.IsFinite(heading))throw new InvalidOperationException("Player heading is unavailable.");
        if(started && now-lastAt<25)return 0;
        // Give the client up to 80 ms to reflect a sent turn before adding
        // another one. Existing turn-unresponsive checks still bound retries.
        if(awaitingHeading && now-sentAt<80 && Math.Abs(Wrap(heading-sentHeading))<.001)return 0;
        double dt=started ? Math.Clamp((now-lastAt)/1000.0,.025,.05) : .03;
        if(!started || now-lastAt>250)velocity=0;
        error=Wrap(error);
        if(Math.Sign(velocity)!=Math.Sign(error))velocity=0;
        const double acceleration=24;
        double maximum=walking?3.5:5.0;
        double desired=Math.Sign(error)*Math.Min(maximum,Math.Sqrt(2*acceleration*Math.Max(0,Math.Abs(error)-.035)));
        velocity=Math.Clamp(desired,velocity-acceleration*dt,velocity+acceleration*dt);
        double correction=Math.Sign(error)*Math.Min(Math.Abs(error)*.70,Math.Abs(velocity)*dt);
        int pixels=Math.Clamp((int)Math.Round(correction/sensitivity),-limit,limit);
        if(pixels==0)pixels=Math.Sign(error/sensitivity);
        lastAt=sentAt=now;sentHeading=heading;started=awaitingHeading=true;
        return pixels;
    }
    static double Wrap(double angle)=>Math.Atan2(Math.Sin(angle),Math.Cos(angle));
}

internal sealed class ArrivalMotion
{
    internal const int FrameMilliseconds=16;
    readonly Queue<double> speedSamples=new();
    Vec previous;
    long previousAt;
    bool observed;
    public double Speed { get; private set; }=.012; // conservative until measured, units/ms
    public void Observe(Vec position,long now,bool advancing)
    {
        if(observed && advancing && now>previousAt && now-previousAt<=250)
        {
            double distance=(position-previous).Length;
            if(distance>.01 && distance<5)
            {
                double measured=distance/(now-previousAt);
                if(measured is >=.001 and <=.08)ObserveSpeed(measured);
            }
        }
        previous=position;previousAt=now;observed=true;
    }
    public double BrakingDistance(double tolerance)=>Math.Clamp(Speed*160+tolerance,1.25,4);
    public int PulseMilliseconds(double distance,double tolerance)=>
        Math.Clamp((int)Math.Floor(Math.Max(0,distance-tolerance*.5)/Speed*.5),FrameMilliseconds,60);
    void ObserveSpeed(double measured)
    {
        speedSamples.Enqueue(measured);
        if(speedSamples.Count>5)speedSamples.Dequeue();
        var ordered=speedSamples.Order().ToArray();
        double median=ordered[ordered.Length/2];
        // Limit gain changes; one release/settling jump cannot double speed.
        Speed=Math.Clamp(Speed*.75+Math.Clamp(median,Speed*.75,Speed*1.25)*.25,.001,.08);
    }
    public void ObservePulse(double moved,double heldMilliseconds)
    {
        // A sub-frame correction is quantized, not a velocity measurement.
        // Use actual held time for longer pulses, never the requested sleep.
        if(moved>.01 && moved<3 && heldMilliseconds>=FrameMilliseconds*2)
        {
            double measured=moved/heldMilliseconds;
            if(measured is >=.001 and <=.08)ObserveSpeed(measured);
        }
    }
}

internal static class AnchorArrival
{
    public static bool Settled(Vec before,Vec after,Vec anchor,double tolerance)=>
        before.Finite && after.Finite && anchor.Finite &&
        (after-anchor).Length<=tolerance && (after-before).Length<=.025;

    // Shared by loot and short melee-assist returns. All live operations are
    // injected so the whole stop/settle/face/recheck sequence is testable offline.
    public static async Task<bool> ReturnAsync(Func<Vec> position,Vec anchor,double tolerance,
        Func<CancellationToken,Task> approach,Action stop,Func<CancellationToken,Task> face,
        Func<int,CancellationToken,Task> delay,Func<long> clock,CancellationToken token)
    {
        if(!anchor.Finite || !double.IsFinite(tolerance) || tolerance<=0)throw new ArgumentOutOfRangeException(nameof(anchor));
        long deadline=clock()+15000;
        try
        {
            while(clock()<deadline)
            {
                token.ThrowIfCancellationRequested();
                Vec before=position();
                if(!before.Finite)throw new InvalidOperationException("Anchor return position is unavailable.");
                if((before-anchor).Length>tolerance)
                {
                    await approach(token);await delay(20,token);continue;
                }
                stop();await delay(120,token);
                Vec settled=position();
                if(!Settled(before,settled,anchor,tolerance))continue;
                await face(token);
                stop();before=position();await delay(120,token);
                if(clock()<=deadline && Settled(before,position(),anchor,tolerance))return true;
            }
            return false;
        }
        finally { stop(); }
    }
}

public sealed partial class Movement
{
    readonly SmoothSteering smoothSteering=new();
    readonly ArrivalMotion arrivalMotion=new();

    async Task ApproachPrecisely(World world,Vec goal,double tolerance,CancellationToken token)
    {
        if(StopApproach()) { await Input.Delay(120,token); return; }
        Vec position=world.PlayerPosition(),delta=goal-position;
        if(delta.Length<=tolerance)return;
        if(!await Face(world,delta,token,.035))return;
        // Re-read after aiming; never pulse against stale geometry.
        position=world.PlayerPosition();delta=goal-position;
        if(delta.Length<=tolerance)return;
        Vec forward=FromClientHeading(world.PlayerHeading());
        if(Math.Abs(Angle(forward,delta))>.08)return;
        int duration=arrivalMotion.PulseMilliseconds(delta.Length,tolerance);
        double step=Math.Min(delta.Length,Math.Max(.05,arrivalMotion.Speed*duration*2));
        if(CanAdvance?.Invoke(position,position+forward*step)==false)return;
        long heldAt=Environment.TickCount64;
        double heldMilliseconds=0;
        try
        {
            Input.Hold(Keys.W,true,token);advancing=true;
            heldAt=Environment.TickCount64;
            await Input.Delay(duration,token);
        }
        finally { Input.Hold(Keys.W,false,default);advancing=false;heldMilliseconds=Environment.TickCount64-heldAt; }
        Vec released=world.PlayerPosition();
        await Input.Delay(120,token);
        Vec after=world.PlayerPosition();
        arrivalMotion.ObservePulse((released-position).Length,heldMilliseconds);
        TraceLog.Record("anchor approach correction",new {Before=position,After=after,Goal=goal,
            Remaining=(goal-after).Length,PulseMilliseconds=duration,HeldMilliseconds=heldMilliseconds,
            MovedWhileHeld=(released-position).Length,SettlingDisplacement=(after-released).Length,EstimatedUnitsPerMs=arrivalMotion.Speed});
    }
}
