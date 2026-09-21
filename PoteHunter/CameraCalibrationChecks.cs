namespace PoteHunter;

/// <summary>Pure checks for signed, settled and repeatable camera calibration samples.</summary>
public static class CameraCalibrationChecks
{
    public static void RunAll()
    {
        var settled=new Movement.CameraSettlement(0,true,true,420,200,8);
        var unsettled=new Movement.CameraSettlement(0,false,true,1400,20,20);

        Movement.AxisSample valid=Movement.EvaluateAxisSample(-.126,.124,-.002,36,-36,settled,settled);
        Expect(valid.Valid&&valid.RadiansPerPixel<-.0034&&valid.RadiansPerPixel>-.0036,"valid signed round trip");

        // Exact scale of the bad live sample: it must not pass merely because
        // its total movement cleared the former .01-radian floor.
        Expect(!Movement.EvaluateAxisSample(-.0129695,.0132529,.0002834,36,-36,settled,settled).Valid,"partial live pitch sample");
        Expect(!Movement.EvaluateAxisSample(-.126,.124,-.002,36,-36,unsettled,settled).Valid,"unsettled outward leg");
        Expect(!Movement.EvaluateAxisSample(-.126,-.124,-.25,36,-36,settled,settled).Valid,"wrong return direction");
        Expect(!Movement.EvaluateAxisSample(-.126,.04,-.086,36,-36,settled,settled).Valid,"asymmetric signed legs");
        Expect(!Movement.EvaluateAxisSample(-.126,.124,.07,36,-36,settled,settled).Valid,"unrestored cycle");

        var rejectedHorizontal=Movement.EvaluateAxisSample(-.126,.04,-.086,36,-36,settled,settled);
        Expect(Movement.CanRebaseRejectedMeasurement(rejectedHorizontal,settled,settled,-.126,.04),
            "finite settled horizontal rejection can rebase");
        Expect(!Movement.CanRebaseRejectedMeasurement(rejectedHorizontal,unsettled,settled,-.126,.04),
            "moving horizontal rejection cannot rebase");
        Expect(!Movement.CanRebaseRejectedMeasurement(rejectedHorizontal,settled,settled,double.NaN,.04),
            "non-finite horizontal rejection cannot rebase");
        CheckQuietWait().GetAwaiter().GetResult();

        Expect(Movement.CalibrationSamplesConsistent(-.00350,-.00365),"repeatable cycles");
        Expect(!Movement.CalibrationSamplesConsistent(-.00350,-.00150),"inconsistent magnitudes");
        Expect(!Movement.CalibrationSamplesConsistent(-.00350,.00350),"inconsistent signs");
        double body=Movement.EvaluateBodyResponse(0,.12,0,36,-36);
        Expect(Math.Abs(body-(-.0033333333333333335))<1e-12,"body turn sign convention");
        var recordedQuietPartial=new Movement.CameraSettlement(.0016697983213821987,false,false,1421,1187,1);
        Expect(Movement.CanDiscardQuietPartial(recordedQuietPartial,.0016697983213821987),"recorded quiet partial was not rebaseable");
        var movingPartial=recordedQuietPartial with{QuietMs=20};
        Expect(!Movement.CanDiscardQuietPartial(movingPartial,.0016697983213821987),"actively moving partial allowed more input");
        CheckDeliveries().GetAwaiter().GetResult();
    }

    static async Task CheckDeliveries()
    {
        Movement.CameraSettlement none(double angle)=>new(angle,false,false,1400,1400,0);
        Movement.CameraSettlement moved(double angle)=>new(angle,true,true,500,190,7);
        async Task<(Movement.LegDelivery Result,int Sends)> Run(params Movement.CameraSettlement[] observations)
        {
            int sends=0,index=0;double angle=0;
            var result=await Movement.RunDeliveredLeg(36,0,5,()=>angle,_=>sends++,(_,_)=>
            {
                var value=observations[Math.Min(index++,observations.Length-1)];angle=value.Angle;return Task.FromResult(value);
            },(_,_)=>Task.CompletedTask,null,default);
            return(result,sends);
        }
        var lostPositive=await Run(none(0),moved(.12));
        Expect(lostPositive.Sends==2&&lostPositive.Result.Settlement.Settled,"lost positive retried");
        var consumed=await Run(moved(.12));
        Expect(consumed.Sends==1,"consumed response was resent");
        var persistent=await Run(none(0));
        Expect(persistent.Sends==5&&persistent.Result.NoResponse,"persistent loss was not bounded");
        var partial=await Run(new Movement.CameraSettlement(.02,false,true,1400,20,2));
        Expect(partial.Sends==1&&!partial.Result.NoResponse,"partial motion was retried");

        // Real round trip: retain the consumed outward leg while only the erased
        // reverse packet is resent. Sensitivity still divides by one 36px leg.
        var packets=new List<int>();double roundTripAngle=0;
        var outward=await Movement.RunDeliveredLeg(36,0,5,()=>roundTripAngle,p=>packets.Add(p),(_,_)=>
        {
            roundTripAngle=.12;return Task.FromResult(moved(.12));
        },(_,_)=>Task.CompletedTask,null,default);
        int reverseObservation=0;
        var returned=await Movement.RunDeliveredLeg(-36,.12,5,()=>roundTripAngle,p=>packets.Add(p),(_,_)=>
        {
            if(reverseObservation++==0)return Task.FromResult(none(.12));
            roundTripAngle=0;return Task.FromResult(moved(0));
        },(_,_)=>Task.CompletedTask,null,default);
        var roundTripSample=Movement.EvaluateAxisSample(.12,-.12,0,36,-36,outward.Settlement,returned.Settlement);
        Expect(packets.SequenceEqual([36,-36,-36])&&outward.Attempts==1&&returned.Attempts==2&&
            roundTripSample.Valid&&Math.Abs(roundTripSample.RadiansPerPixel-.0033333333333333335)<1e-12,"lost return did not preserve outward denominator");

        int driftSends=0;double driftAngle=0;
        var drifted=await Movement.RunDeliveredLeg(36,0,5,()=>driftAngle,_=>driftSends++,(_,_)=>Task.FromResult(none(0)),(_,_)=>
        {driftAngle=.01;return Task.CompletedTask;},null,default);
        Expect(driftSends==1&&!drifted.Settlement.Settled&&!drifted.NoResponse,"baseline drift caused another send");

        int cancelSends=0;using var cts=new CancellationTokenSource();cts.Cancel();bool cancelled=false;
        try
        {
            await Movement.RunDeliveredLeg(36,0,5,()=>0,_=>cancelSends++,(_,ct)=>Task.FromCanceled<Movement.CameraSettlement>(ct),
                (_,ct)=>Task.FromCanceled(ct),null,cts.Token);
        }
        catch(OperationCanceledException){cancelled=true;}
        Expect(cancelled&&cancelSends==0,"cancellation did not stop delivery");

        int waitingSends=0;using var waitingCts=new CancellationTokenSource();bool waitCancelled=false;
        try
        {
            await Movement.RunDeliveredLeg(36,0,5,()=>0,_=>waitingSends++,async (_,ct)=>
            {
                waitingCts.Cancel();await Task.Delay(1,ct);return none(0);
            },(_,ct)=>Task.Delay(1,ct),null,waitingCts.Token);
        }
        catch(OperationCanceledException){waitCancelled=true;}
        Expect(waitCancelled&&waitingSends==1,"cancellation during observation did not stop after the sent pulse");
    }

    static async Task CheckQuietWait()
    {
        long now=0;int reads=0;double[] moving=[0,.01,.02,.03,.03,.03,.03,.03,.03,.03,.03,.03];
        var quiet=await Movement.WaitForQuiet(()=>moving[Math.Min(reads++,moving.Length-1)],(ms,_)=>{now+=ms;return Task.CompletedTask;},3000,default,
            (current,previous)=>current-previous,()=>now);
        Expect(quiet.Settled&&quiet.QuietMs>=180&&quiet.Angle==.03,"quiet wait resumed at the settled angle");

        now=0;reads=0;double persistent=0;
        var timedOut=await Movement.WaitForQuiet(()=>{persistent+=.01;return persistent;},(ms,_)=>{now+=ms;return Task.CompletedTask;},3000,default,
            (current,previous)=>current-previous,()=>now);
        Expect(!timedOut.Settled&&timedOut.ElapsedMs>=3000,"persistent motion quiet wait was bounded");

        using var cts=new CancellationTokenSource();cts.Cancel();bool cancelled=false;
        try{await Movement.WaitForQuiet(()=>0,(ms,_)=>Task.CompletedTask,3000,cts.Token,clock:()=>0);}
        catch(OperationCanceledException){cancelled=true;}
        Expect(cancelled,"quiet wait respected cancellation");
    }

    static void Expect(bool condition,string name)
    {
        if(!condition)throw new InvalidOperationException("Camera calibration check failed: "+name);
    }
}
