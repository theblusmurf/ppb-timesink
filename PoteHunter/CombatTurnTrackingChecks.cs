using System.Text.Json;
namespace PoteHunter;

internal static class CombatTurnTrackingChecks
{
    static void Require(bool condition,string reason)
    {if(!condition)throw new Exception("Combat turn tracking: "+reason);}

    internal static async Task Run()
    {
        CheckSkillFacing();
        Require(CombatTurnTracking.Allowed(true,true,true,false,false,false,false,false),"ordinary melee tracking blocked");
        for(int blocked=0;blocked<8;blocked++)
        {
            bool[] gates=[true,true,true,false,false,false,false,false];gates[blocked]=!gates[blocked];
            Require(!CombatTurnTracking.Allowed(gates[0],gates[1],gates[2],gates[3],gates[4],gates[5],gates[6],gates[7]),
                "stop, camera mode, replaced target or exclusive activity admitted tracking");
        }
        foreach(int duration in new[]{0,45,50,75,100})
        {
            long clock=0;var times=new List<long>();var delays=new List<int>();
            await CombatTurnTracking.WaitAsync(duration,()=>clock,()=>{times.Add(clock);return true;},
                (ms,ct)=>{ct.ThrowIfCancellationRequested();delays.Add(ms);clock+=ms;return Task.CompletedTask;},default);
            Require(clock==duration && delays.All(ms=>ms is >0 and <=16),"combat wait deadline or correction spacing changed");
            Require(times.SequenceEqual(Enumerable.Range(0,(duration+15)/16).Select(i=>(long)i*16)),"corrections skipped ordinary waits");
        }
        long time=0;int calls=0;
        Task Delay(int ms,CancellationToken ct){ct.ThrowIfCancellationRequested();time+=ms;return Task.CompletedTask;}
        await CombatTurnTracking.WaitAsync(100,()=>time,()=>++calls<2,Delay,default);
        Require(calls==2&&time==16,"invalid target failed to stop further corrections");
        time=0;calls=0;
        await CombatTurnTracking.WaitAsync(45,()=>time,()=>{calls++;time+=70;return true;},Delay,default);
        Require(calls==1&&time==70,"slow reads triggered catch-up corrections after the deadline");
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();bool stopped=false;
            try{await CombatTurnTracking.WaitAsync(50,()=>0,()=>throw new Exception("cancelled turn emitted"),Delay,cancel.Token);}
            catch(OperationCanceledException){stopped=true;}
            Require(stopped,"pre-cancelled tracking continued");
        }
        using(var cancel=new CancellationTokenSource())
        {
            time=0;calls=0;bool stopped=false;
            try{await CombatTurnTracking.WaitAsync(75,()=>time,()=>{calls++;return true;},
                (ms,ct)=>{time+=ms;cancel.Cancel();return Task.CompletedTask;},cancel.Token);}
            catch(OperationCanceledException){stopped=true;}
            Require(stopped&&calls==1,"cancel during wait allowed another correction");
        }
        foreach(double sensitivity in new[]{-.004,.004,-.0007,.0007})
        foreach(int latency in new[]{0,32,80,160})
        foreach(double goal in new[]{-.08,.08,-2.09,2.09})
        {
            var steering=new SmoothSteering();var rate=new TurnRateBudget();var pending=new PriorityQueue<double,long>();
            double observed=0;int elapsed;
            for(elapsed=0;elapsed<9000;elapsed+=16)
            {
                while(pending.TryPeek(out double part,out long at)&&at<=elapsed){pending.Dequeue();observed+=part;}
                double error=goal-observed;
                Require(Math.Sign(error)==Math.Sign(goal)||Math.Abs(error)<=.018,"fine tracking overshot delayed feedback");
                if(Math.Abs(error)<=.018&&pending.Count==0)break;
                int pixels=steering.Next(error,-observed,sensitivity,false,elapsed,rate,150,.018);
                // Delayed heading reads can spread sends beyond two frames.
                // They admit at most50ms from the measured rate budget; the
                // remaining-angle reservation still forbids stacked overshoot.
                Require(Math.Abs(pixels*sensitivity)<=150*Math.PI/180*.050+Math.Abs(sensitivity),"correction exceeded the bounded50ms measured allowance");
                if(pixels!=0)
                    for(int part=0;part<4;part++)pending.Enqueue(pixels*sensitivity/4,elapsed+latency+part*16);
            }
            Require(elapsed<9000,"fine turning failed to settle with delayed partial feedback");
        }
        var fixedTurn=Simulate(16,0);var delayed=Simulate(16,32);
        Require(fixedTurn.Elapsed<=1000&&fixedTurn.LargestDegrees<3,"responsive turn lost smooth, fast correction");
        Require(delayed.Elapsed<=1100&&delayed.LargestDegrees<5,"two-frame feedback caused slow or large corrections");
        var fastPoll=Simulate(1,0);Require(fastPoll.Elapsed>=fixedTurn.Elapsed-32,"rapid polling bypassed the frame rate");
        var paused=new SmoothSteering();var pausedRate=new TurnRateBudget();double angle=0;
        for(int at=0;at<400;at+=16)angle+=paused.Next(2.5-angle,-angle,.004,false,at,pausedRate,150)*.004;
        int resumed=paused.Next(2.5-angle,-angle,.004,false,650,pausedRate,150);
        Require(Math.Abs(resumed*.004)<=150*Math.PI/180*.020+.004,"long pause released a catch-up burst");
        var coarse=new SmoothSteering();var coarseRate=new TurnRateBudget();int first=coarse.Next(.08,0,.05,false,0,coarseRate,30);
        Require(first==0,"fractional correction was forcibly rounded to a full pixel");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"combat-turn-tracking-checks.json"),JsonSerializer.Serialize(new
        {
            Passed=true,HardwareInputEmitted=false,Fixed120DegreeTurnAt150=fixedTurn,Delayed32msFeedbackAt150=delayed,
            Checks=new[]{"16ms cooperative combat waits","original wait deadlines","slow reads without catch-up","cancellation before/during waits",
                "target and exclusive-activity guards","fine one-degree settling with partial delayed feedback and both sensitivity signs",
                "sub-pixel accumulation","speed cap and fast-poll bound","long pause burst bound",
                "fresh offensive body-facing confirmation","observed 26.76-degree retarget error rejected","finite usable direction required",
                "refreshed moving target and player direction","two-degree inclusive tolerance without jitter starvation",
                "priority self-heal and ranged-facing exemptions"}
        },new JsonSerializerOptions{WriteIndented=true}));
    }

    static void CheckSkillFacing()
    {
        Require(CombatTurnTracking.SkillFacingRequired(true,false),"offensive melee facing exemption");
        Require(!CombatTurnTracking.SkillFacingRequired(true,true),"priority self-heal waited for offensive facing");
        Require(!CombatTurnTracking.SkillFacingRequired(false,false)&&!CombatTurnTracking.SkillFacingRequired(false,true),
            "ranged optical facing replaced by body-facing gate");

        static Vec Direction(double angle)=>Movement.Rotate(new Vec(0,-1),angle);
        bool Ready(double heading,Vec delta)=>CombatTurnTracking.FacingReady(heading,delta,out _);
        Require(Ready(0,Direction(0)),"aligned melee direction rejected");
        foreach(double sign in new[]{-1d,1d})
        {
            Require(Ready(0,Direction(sign*(CombatTurnTracking.SkillFacingTolerance-1e-9))),"inside two-degree boundary rejected");
            Require(!Ready(0,Direction(sign*(CombatTurnTracking.SkillFacingTolerance+1e-9))),"outside two-degree boundary admitted");
            Require(!Ready(0,Direction(sign*26.76*Math.PI/180)),"observed retarget angle admitted a skill");
        }
        Require(Ready(0,new(Math.Sin(.035),-Math.Cos(.035))),"inclusive two-degree boundary rejected");
        foreach(double jitter in new[]{.017,-.018,.022,-.032,.030,0d})
            Require(Ready(0,Direction(jitter)),"harmless angular jitter required consecutive settling reads");
        Require(!Ready(0,Direction(.036)),"fresh jitter beyond tolerance kept old readiness");
        Require(Ready(Math.PI-1e-4,Movement.FromClientHeading(-Math.PI+1e-4)),"wrapped client heading rejected");
        foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
        {
            Require(!Ready(invalid,new(0,-1)),"nonfinite heading admitted a skill");
            Require(!Ready(0,new(invalid,-1))&&!Ready(0,new(0,invalid)),"nonfinite target direction admitted a skill");
        }
        Require(!Ready(0,new(0,0))&&!Ready(0,new(0,-.009))&&!Ready(0,new(double.MaxValue,double.MaxValue)),
            "missing or overflowing direction admitted a skill");
        Require(Ready(0,new(0,-.01)),"usable minimum direction rejected");

        var drive=new Movement();
        var player=new Entity(1,1,"Synthetic",new(10,10),0,0);
        int samples=0;
        Entity ReadPlayer(){samples++;return player;}
        Vec oldTarget=player.Position+new Vec(0,-3);
        Vec retargeted=player.Position+Direction(26.76*Math.PI/180)*3;
        Require(drive.CombatSkillFacingReady(ReadPlayer,oldTarget,out _),"initial aligned sample unavailable");
        Require(!drive.CombatSkillFacingReady(ReadPlayer,retargeted,out double retargetError)&&
            Math.Abs(retargetError*180/Math.PI-26.76)<1e-9,"old target facing reused after retarget");
        player=player with{Heading=-26.76*Math.PI/180};
        Require(drive.CombatSkillFacingReady(ReadPlayer,retargeted,out double alignedError)&&Math.Abs(alignedError)<1e-9,
            "fresh aligned heading ignored while cached facing was mismatched");
        player=player with{Heading=0};
        Require(!drive.CombatSkillFacingReady(ReadPlayer,retargeted,out _),"stale aligned heading admitted another activation");
        Vec refreshedTarget=player.Position+new Vec(3,0);
        Require(!drive.CombatSkillFacingReady(ReadPlayer,refreshedTarget,out double movedError)&&Math.Abs(movedError-Math.PI/2)<1e-9,
            "moving target direction reused its old readiness");
        Require(drive.CombatSkillFacingReady(ReadPlayer,oldTarget,out _),"fresh original direction failed to restore readiness");
        player=player with{Position=player.Position+new Vec(2,0)};
        Require(!drive.CombatSkillFacingReady(ReadPlayer,oldTarget,out _),"stale player position admitted a skill");
        Require(samples==7,"facing gate failed to take exactly one fresh player sample per activation");
        player=player with{Heading=double.NaN};
        Require(!drive.CombatSkillFacingReady(ReadPlayer,oldTarget,out double invalidError)&&double.IsNaN(invalidError),
            "invalid fresh player observation retained earlier readiness");
    }

    internal readonly record struct TurnResult(int Elapsed,double LargestDegrees);
    static TurnResult Simulate(int cadence,int latency)
    {
        var steering=new SmoothSteering();var budget=new TurnRateBudget();var pending=new Queue<(long At,double Angle)>();
        double observed=0,maximum=0,sensitivity=-.00244444211324056,goal=120*Math.PI/180;
        for(int at=0;at<6000;at+=cadence)
        {
            while(pending.Count>0&&pending.Peek().At<=at)observed+=pending.Dequeue().Angle;
            if(Math.Abs(goal-observed)<=.035&&pending.Count==0)return new(at,maximum*180/Math.PI);
            int pixels=steering.Next(goal-observed,-observed,sensitivity,false,at,budget,150);
            if(pixels!=0){double turn=pixels*sensitivity;maximum=Math.Max(maximum,Math.Abs(turn));pending.Enqueue((at+latency,turn));}
        }
        throw new Exception("Combat turn tracking: fixed goal never settled");
    }
}
