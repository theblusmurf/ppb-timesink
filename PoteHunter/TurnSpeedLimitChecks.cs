using System.Text.Json;
namespace PoteHunter;

internal static class TurnSpeedLimitChecks
{
    internal static void Run()
    {
        void Require(bool value,string reason){if(!value)throw new Exception("Turn speed limit: "+reason);}
        foreach(double speed in new[]{30d,90d,180d,360d})
        foreach(double sensitivity in new[]{-.05,.05,-.004,.004,-.0007,.0007})
        foreach(int cadence in new[]{1,16,20,80,300})
        {
            var budget=new TurnRateBudget();double total=0,rate=speed*Math.PI/180;
            for(long now=0;now<=2000;now+=cadence)
            {
                int pixels=budget.Available(now,speed,sensitivity);budget.Consume(pixels,sensitivity);total+=Math.Abs(pixels*sensitivity);
                Require(total<=rate*(now/1000d+.04)+1e-9,"polling cadence exceeded cumulative angular speed");
                Require(Math.Abs(pixels*sensitivity)<=Math.Max(rate*.05,Math.Abs(sensitivity))+1e-9,"idle allowance exceeded 50ms or one calibrated pixel");
            }
            Require(total>0,"sub-pixel speed limit permanently stalled turning");
        }
        foreach(double speed in new[]{30d,90d,180d,360d})
        foreach(double sensitivity in new[]{-.004,.004,-.0007,.0007})
        {
            var steering=new SmoothSteering();var budget=new TurnRateBudget();double error=Math.PI,heading=0,total=0;
            long now;
            for(now=0;now<12000&&error>.035;now+=20)
            {
                int pixels=steering.Next(error,heading,sensitivity,false,now,budget,speed);
                double angle=pixels*sensitivity;Require(angle>=0&&angle<=error*.7+1e-9,"cap changed correction direction or overshot");
                total+=angle;error-=angle;heading-=angle;
                Require(total<=speed*Math.PI/180*(now/1000d+.04)+1e-9,"steering bypassed turn budget");
            }
            Require(error<=.035,"limited half-turn did not converge");
        }
        var old=JsonSerializer.Deserialize<Options>("{}")!;Require(old.TurnSpeedDegreesPerSecond==180,"old settings lack the smooth default");
        var saved=JsonSerializer.Deserialize<Options>(JsonSerializer.Serialize(new Options{TurnSpeedDegreesPerSecond=90}))!;
        Require(saved.TurnSpeedDegreesPerSecond==90,"saved turn cap was lost");
        Require(new Options{TurnSpeedDegreesPerSecond=-1}.TurnSpeedDegreesPerSecond==30&&new Options{TurnSpeedDegreesPerSecond=900}.TurnSpeedDegreesPerSecond==360,"invalid settings escaped bounds");
    }
}
