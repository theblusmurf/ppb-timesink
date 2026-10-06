namespace PoteHunter;

// Bound changes by elapsed time and observed heading, not by how often the
// caller happens to poll. This controller does not emit any input.
internal sealed class SmoothSteering
{
    long lastAt, sentAt, lastProgressAt;
    double velocity, sentHeading, observedHeading, outstandingTurn, fractionalPixels;
    bool started, awaitingHeading;
    internal const int FrameMilliseconds=16;
    public void Reset() { started=awaitingHeading=false; velocity=outstandingTurn=fractionalPixels=0; }
    public int Next(double error,double heading,double sensitivity,bool walking,long now,TurnRateBudget? rateBudget=null,double maxDegreesPerSecond=360,double deadZone=.035)
    {
        int limit=Math.Abs(Movement.CalculateTurn(error,sensitivity,walking));
        error=Wrap(error);
        if(!double.IsFinite(deadZone)||deadZone<=0||deadZone>.035)throw new ArgumentOutOfRangeException(nameof(deadZone));
        // Fine visual tracking is independent of the looser attack permission.
        if(limit==0 && Math.Abs(error)>=deadZone)
            limit=Math.Max(1,(int)Math.Round(Math.Abs(error*.70/sensitivity)));
        if(rateBudget!=null)limit=Math.Min(limit,rateBudget.Available(now,maxDegreesPerSecond,sensitivity));
        if(!double.IsFinite(heading))throw new InvalidOperationException("Player heading is unavailable.");
        if(started)
        {
            // Client heading runs opposite to the calibrated geometric turn.
            // Consume observed partial feedback before checking send cadence.
            double observed=-Wrap(heading-observedHeading);
            if(Math.Sign(observed)==Math.Sign(outstandingTurn) && Math.Abs(observed)>0)
            {
                outstandingTurn=Math.Sign(outstandingTurn)*Math.Max(0,Math.Abs(outstandingTurn)-Math.Abs(observed));
                if(Math.Abs(outstandingTurn)<1e-9)outstandingTurn=0;
                lastProgressAt=now;
            }
            observedHeading=heading;
            // A lost command may retry after a quiet 220 ms, but do not forget
            // a newer turn or an angle that is still arriving in partial updates.
            if(now-Math.Max(lastProgressAt,sentAt)>=220)outstandingTurn=0;
        }
        if(Math.Abs(error)<deadZone) { velocity=fractionalPixels=0;return 0; }
        if(limit==0)return 0;
        if(started && now-lastAt<FrameMilliseconds)return 0;
        if(awaitingHeading && now-sentAt<80 && Math.Abs(Wrap(heading-sentHeading))<.001)return 0;
        // Long combat/reader pauses never turn into a large catch-up packet.
        double elapsed=started?(now-lastAt)/1000.0:.016;
        // Reader/preflight work can put measured feedback 80–120ms apart.
        // Admit at most 50ms of elapsed correction, also bounded by the
        // calibrated rate/pixel and outstanding-angle budgets below. Treating
        // every slow observation as only 20ms made a responsive turn crawl.
        bool restartFrame=!started || now-lastAt>=250;
        double dt=restartFrame?.016:Math.Clamp(elapsed,.016,.050);
        if(restartFrame)velocity=fractionalPixels=0;
        // Reserve unreported corrections against the remaining angle. Small
        // sensitivity can pipeline turns without blindly stacking near the goal.
        if(outstandingTurn!=0 && Math.Sign(outstandingTurn)!=Math.Sign(error))return 0;
        double available=Math.Max(0,Math.Abs(error)*.70-Math.Abs(outstandingTurn));
        double packetAngularLimit=walking?.225:.300;
        int budgetPixels=(int)Math.Min(limit,Math.Floor(Math.Min(available,packetAngularLimit)/Math.Abs(sensitivity)));
        if(budgetPixels<1)return 0;
        if(Math.Sign(velocity)!=Math.Sign(error))velocity=fractionalPixels=0;
        const double acceleration=32;
        double maximum=walking?4.5:6.0;
        maximum=Math.Min(maximum,maxDegreesPerSecond*Math.PI/180);
        double desired=Math.Sign(error)*Math.Min(maximum,Math.Sqrt(2*acceleration*Math.Max(0,Math.Abs(error)-deadZone)));
        velocity=Math.Clamp(desired,velocity-acceleration*dt,velocity+acceleration*dt);
        double correction=Math.Sign(error)*Math.Min(Math.Abs(error)*.70,Math.Abs(velocity)*dt);
        double precisePixels=correction/sensitivity+fractionalPixels;
        int rounded=(int)Math.Round(precisePixels);
        int pixels=Math.Clamp(rounded,-budgetPixels,budgetPixels);
        fractionalPixels=rounded==pixels?precisePixels-rounded:0;
        if(pixels==0)
        {
            if(!started){observedHeading=heading;lastProgressAt=now;started=true;}
            lastAt=now;return 0;
        }
        if(!started) { observedHeading=heading;lastProgressAt=now; }
        outstandingTurn+=pixels*sensitivity;
        rateBudget?.Consume(pixels,sensitivity);
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
        Func<int,CancellationToken,Task> delay,Func<long> clock,CancellationToken token,
        Func<CancellationToken,Task<bool>>? defend=null)
    {
        if(!anchor.Finite || !double.IsFinite(tolerance) || tolerance<=0)throw new ArgumentOutOfRangeException(nameof(anchor));
        long startedAt=clock(),deadline=startedAt+15000,hardDeadline=startedAt+120000;
        try
        {
            while(clock()<deadline && clock()<hardDeadline)
            {
                token.ThrowIfCancellationRequested();
                if(defend!=null)
                {
                    long defenseAt=clock();
                    // The injected stationary defense owns/relinquishes its
                    // attack input. It must not leave attack held when it
                    // returns false and this controller resumes aiming.
                    if(await defend(token))
                    {
                        deadline=Math.Min(hardDeadline,deadline+Math.Max(0,clock()-defenseAt));
                        stop();await delay(20,token);continue;
                    }
                }
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
                if(clock()<=deadline && clock()<=hardDeadline && Settled(before,position(),anchor,tolerance))return true;
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
        double heldMilliseconds=0;
        try
        {
            advancing=true;
            heldMilliseconds=await Input.PulseForward(duration,token);
        }
        finally { Input.Hold(Keys.W,false,default);advancing=false; }
        Vec released=world.PlayerPosition();
        await Input.Delay(120,token);
        Vec after=world.PlayerPosition();
        // The client frequently publishes most/all movement after key-up.
        // Train on the complete settled pulse rather than a partial first read.
        arrivalMotion.ObservePulse((after-position).Length,heldMilliseconds);
        TraceLog.Record("anchor approach correction",new {Before=position,After=after,Goal=goal,
            Remaining=(goal-after).Length,PulseMilliseconds=duration,HeldMilliseconds=heldMilliseconds,
            MovedWhileHeld=(released-position).Length,SettlingDisplacement=(after-released).Length,EstimatedUnitsPerMs=arrivalMotion.Speed});
    }
}
