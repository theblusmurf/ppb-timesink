namespace PoteHunter;

public static class AimCalibration
{
    static double Wrap(double angle)=>Math.Atan2(Math.Sin(angle),Math.Cos(angle));
    public static bool Measure(double start,double turned,double restored,out double change,out double error)
    {
        change=Wrap(turned-start);error=Wrap(restored-start);
        double back=Wrap(restored-turned);
        return double.IsFinite(change) && double.IsFinite(error) && Math.Abs(change)>=.015 && Math.Abs(change)<=1.2 &&
            change*back<0 && Math.Abs(back)>=.01 && Math.Abs(error)<=Math.Max(.16,Math.Abs(change)*.75);
    }
    public static async Task<double> WaitForHeading(Func<double> read,Func<int,CancellationToken,Task> delay,Func<long> clock,
        double before,CancellationToken token)
    {
        long started=clock(),stableSince=started;double previous=before,current=before;bool changed=false;
        while(clock()-started<700)
        {
            await delay(40,token);current=read();long now=clock();
            if(!double.IsFinite(current))throw new InvalidOperationException("Character facing angle is unavailable.");
            if(Math.Abs(Wrap(current-before))>=.01)changed=true;
            if(Math.Abs(Wrap(current-previous))>.004)stableSince=now;
            previous=current;
            if(changed && now-started>=250 && now-stableSince>=120)return current;
        }
        return current;
    }
    public static async Task SelfTest()
    {
        if(!Measure(4.7,4.9,4.83,out _,out _) || Measure(4.7,4.7,4.5,out _,out _) ||
            Measure(4.7,4.9,4.9,out _,out _) || Measure(0,1.5,0,out _,out _) ||
            Measure(0,.2,.4,out _,out _) || Measure(double.NaN,.2,0,out _,out _))
            throw new Exception("Relaxed calibration accepted a missing/wrong turn or rejected tolerable drift");
        long now=0;
        Task Delay(int ms,CancellationToken token){token.ThrowIfCancellationRequested();now+=ms;return Task.CompletedTask;}
        var value=await WaitForHeading(()=>now<280?0:.2,Delay,()=>now,0,default);
        if(value!=.2 || now<400 || now>700)throw new Exception("Delayed calibration heading was sampled too early");
        now=0;value=await WaitForHeading(()=>0,Delay,()=>now,0,default);
        if(value!=0 || now<700)throw new Exception("No-response calibration did not wait to its bound");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"aim-calibration-checks.json"),System.Text.Json.JsonSerializer.Serialize(new {
            Passed=true,Checks=new[]{"delayed heading settles","bounded no-response wait","moderate return drift accepted","missing/reversed/invalid responses rejected"}},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
    }
}
