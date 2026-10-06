namespace PoteHunter;

// Accumulate sub-pixel corrections instead of forcing a pixel on every poll.
// Keep at most 50ms or one calibrated pixel of allowance. A low speed
// with coarse calibration waits for a full pixel instead of stalling forever.
internal sealed class TurnRateBudget
{
    long lastAt;
    bool started;
    double radians;
    internal int Available(long now,double degreesPerSecond,double radiansPerPixel)
    {
        if(!double.IsFinite(degreesPerSecond)||degreesPerSecond is <30 or >360 ||
            !double.IsFinite(radiansPerPixel)||Math.Abs(radiansPerPixel)<.00001)
            throw new InvalidOperationException("Invalid turn-speed limit or calibration.");
        double rate=degreesPerSecond*Math.PI/180;
        double elapsed=started?Math.Clamp((now-lastAt)/1000.0,0,.05):.04;
        radians=Math.Min(Math.Max(rate*.05,Math.Abs(radiansPerPixel)),radians+rate*elapsed);lastAt=now;started=true;
        return (int)Math.Floor((radians+1e-12)/Math.Abs(radiansPerPixel));
    }
    internal void Consume(int pixels,double sensitivity)=>radians=Math.Max(0,radians-Math.Abs(pixels*sensitivity));
}

public sealed partial class Movement
{
    double turnSpeedDegreesPerSecond=180;
    public double TurnSpeedDegreesPerSecond
    {
        get=>turnSpeedDegreesPerSecond;
        set=>turnSpeedDegreesPerSecond=double.IsFinite(value)?Math.Clamp(value,30,360):180;
    }
    readonly TurnRateBudget turnRateBudget=new();
    int LimitCameraTurn(int pixels,double sensitivity,long now)
    {
        int allowance=turnRateBudget.Available(now,TurnSpeedDegreesPerSecond,sensitivity);
        int result=Math.Clamp(pixels,-allowance,allowance);
        turnRateBudget.Consume(result,sensitivity);return result;
    }
}

public sealed partial class HunterForm
{
    readonly NumericUpDown turnSpeedLimit=new(){Name="turnSpeedLimit",Minimum=30,Maximum=360,Increment=15,Value=180,Width=85,
        AccessibleName="Maximum turn speed in degrees per second"};
    void InitializeTurnSpeedLimit(TableLayoutPanel operating)
    {
        try{turnSpeedLimit.Value=Options.Read().TurnSpeedDegreesPerSecond;}catch{turnSpeedLimit.Value=180;}
        CompactAdd(operating,CompactRow("Turn speed cap",turnSpeedLimit,new Label{Text="° / second",AutoSize=true}));
        priorityHint.SetToolTip(turnSpeedLimit,"Maximum calibrated horizontal turn speed: 30–360 degrees per second. Lower values smooth direction changes. Applies to travel, facing and ranged camera yaw; calibration is unchanged. Saved with your hunting profile.");
    }
}
