using System.Text.Json;

namespace PoteHunter;

internal static class StationaryReturnDefenseChecks
{
    static void Require(bool condition,string message){if(!condition)throw new Exception("Stationary return defense: "+message);}
    static Entity Mob(uint id=0x80001755)=>new(100,id,"Lv. 1 Pulkhan",new(2,0),10,Generation:1);
    static StationaryReturnDefense.Observation Sample()=>new(new(0,0),new(0,0),new(100,100),Mob(),new(100,100),2.5,true,true);

    public static async Task Run()
    {
        var sample=Sample();
        Require(StationaryReturnDefense.CanDefend(sample),"approved nearby living stationary enemy was refused");
        Require(StationaryReturnDefense.CanDefend(sample with{Position=new(1.5,0)}) &&
            !StationaryReturnDefense.CanDefend(sample with{Position=new(1.50001,0)}),"anchor scope boundary changed");
        Require(StationaryReturnDefense.CanDefend(sample with{Target=Mob() with{Position=new(2.5,0)}}) &&
            !StationaryReturnDefense.CanDefend(sample with{Target=Mob() with{Position=new(2.50001,0)}}),"defense broadened the swing range");
        Require(StationaryReturnDefense.CanDefend(sample with{Target=Mob(0x80001752) with{Name="Gamekeeper",Model="MON_SnowGun2.GCMDS"}}),
            "approved in-range Gamekeeper was excluded");
        var invalid=new[]{sample with{PlayerHealth=default},sample with{PlayerHealth=new(0,100)},
            sample with{TargetHealth=default},sample with{TargetHealth=new(0,100)},sample with{ContextVerified=false},
            sample with{TargetApproved=false},sample with{Position=new(double.NaN,0)},sample with{Anchor=new(double.NaN,0)},
            sample with{Target=null},sample with{Target=Mob() with{Position=new(double.NaN,0)}},
            sample with{Target=Mob(7) with{Model="PC_MAN.GCMDS"}},sample with{Target=Mob(0x8000175F)},
            sample with{Target=Mob(0x80000001)},sample with{SwingRange=double.NaN},sample with{SwingRange=0},
            sample with{Position=new(2,0)}};
        foreach(var bad in invalid)Require(!StationaryReturnDefense.CanDefend(bad),"invalid context/health/protected or distant target was admitted");

        int attacks=0,stops=0,releases=0;long now=0;
        Task Delay(int milliseconds,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Require(milliseconds is >0 and <=50,"poll interval exceeded the bound");
            now+=milliseconds;return Task.CompletedTask;
        }
        Task Attack(StationaryReturnDefense.Observation current,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Require(StationaryReturnDefense.CanDefend(current),"attack lacked immediate eligibility");
            attacks++;return Task.CompletedTask;
        }
        var outcome=await StationaryReturnDefense.RunAsync(()=>sample,()=>stops++,Attack,()=>releases++,Delay,()=>now,default);
        Require(outcome==StationaryReturnDefenseOutcome.BudgetExpired && now==1000 && attacks==20 && releases==1 && stops>=2,
            "defense refreshed its budget, polled slowly, or retained input after the chunk");

        // These snapshots reproduce a near-anchor return with living enemies.
        // Damage must preserve defense, while each invalidation must yield
        // before another attack operation and leave external return state alone.
        foreach(var changed in new[]{sample with{PlayerHealth=new(90,100)},sample with{TargetHealth=new(90,100)}})
        {
            attacks=stops=releases=0;now=0;
            outcome=await StationaryReturnDefense.RunAsync(()=>attacks==0?sample:changed,()=>stops++,Attack,()=>releases++,Delay,()=>now,default,100);
            Require(outcome==StationaryReturnDefenseOutcome.BudgetExpired && attacks==2 && releases==1,
                "normal player/target damage cancelled stationary defense");
        }
        var recovery=new DeathRecoveryState();recovery.Observe(new(0,100),0);recovery.Observe(new(100,100),2000);
        long episode=recovery.Episode;Require(recovery.MarkPostRevivalPrepared(episode) && recovery.MarkRepairCompleted(episode),"test recovery preparation failed");
        foreach(var changed in new[]{sample with{TargetHealth=new(0,100)},sample with{TargetHealth=default},
            sample with{PlayerHealth=new(0,100)},sample with{PlayerHealth=default},sample with{ContextVerified=false},
            sample with{TargetApproved=false},sample with{Position=new(1.51,0)},sample with{Anchor=new(.1,0)},
            sample with{Target=Mob() with{Position=new(2.51,0)}},sample with{Target=null},
            sample with{Target=Mob() with{Id=0x80011755}},sample with{Target=Mob() with{Address=101}},
            sample with{Target=Mob() with{Generation=2}}})
        {
            attacks=stops=releases=0;now=0;
            outcome=await StationaryReturnDefense.RunAsync(()=>attacks==0?sample:changed,()=>stops++,Attack,()=>releases++,Delay,()=>now,default);
            Require(outcome==StationaryReturnDefenseOutcome.Yielded && attacks==1 && now==50 && releases==1 && stops>=2,
                "death/unknown health/scope/protection/identity change did not yield immediately");
            Require(recovery.Pending && recovery.Episode==episode && recovery.PostRevivalPrepared && recovery.RepairCompleted,
                "defense cleared or repeated completed death recovery phases");
        }
        attacks=stops=releases=0;now=0;
        outcome=await StationaryReturnDefense.RunAsync(()=>sample with{Position=new(10,0)},()=>stops++,Attack,()=>releases++,Delay,()=>now,default);
        Require(outcome==StationaryReturnDefenseOutcome.Unavailable && attacks==0 && stops==0 && releases==0,
            "a transit point away from the anchor was defended or disturbed");
        int observations=0;attacks=stops=releases=0;now=0;
        outcome=await StationaryReturnDefense.RunAsync(()=>++observations==1?sample:sample with{TargetApproved=false},()=>stops++,Attack,()=>releases++,Delay,()=>now,default);
        Require(outcome==StationaryReturnDefenseOutcome.Yielded && attacks==0 && releases==1,
            "first target was not revalidated after movement stopped");

        foreach(bool loseFocus in new[]{false,true})
        {
            using var cancelled=new CancellationTokenSource();attacks=stops=releases=0;now=0;bool interrupted=false;
            Task Interrupt(int milliseconds,CancellationToken token)
            {
                if(loseFocus)throw new OperationCanceledException("Focus lost");
                cancelled.Cancel();return Task.CompletedTask;
            }
            try {await StationaryReturnDefense.RunAsync(()=>sample,()=>stops++,Attack,()=>releases++,Interrupt,()=>now,cancelled.Token);}
            catch(OperationCanceledException){interrupted=true;}
            Require(interrupted && attacks==1 && releases==1 && stops>=2,"focus loss/stop was swallowed or attack retained");
        }
        attacks=stops=releases=0;now=0;bool failed=false;
        try {await StationaryReturnDefense.RunAsync(()=>sample,()=>stops++,(_,_)=>throw new InvalidOperationException("Target read changed"),()=>releases++,Delay,()=>now,default);}
        catch(InvalidOperationException){failed=true;}
        Require(failed && releases==1 && stops>=2,"attack failure retained input or was swallowed");

        // The pending pressure flag is repeatedly renewed by the preflight.
        // Being at the anchor must hand off each pass to target selection,
        // instead of clearing that flag and unconditionally continuing again.
        var pressure=new CombatPressure();pressure.Observe(new(100,100),0);
        int selections=0;
        for(int pass=1;pass<=20;pass++)
        {
            pressure.Observe(new(100-pass,100),pass*100);
            if(pressure.RecentDamage(pass*100) && StationaryReturnDefense.ShouldHandOffPressure(false,true,new(),new(),new(100-pass,100)))selections++;
        }
        Require(selections==20 && pressure.LastDamageAt==2000,"near-anchor pressure still starved target selection or forgot incoming damage");
        Require(StationaryReturnDefense.ShouldHandOffPressure(false,true,new(1.5,0),new(),new(10,100)) &&
            !StationaryReturnDefense.ShouldHandOffPressure(false,true,new(1.51,0),new(),new(10,100)) &&
            !StationaryReturnDefense.ShouldHandOffPressure(true,true,new(),new(),new(10,100)) &&
            !StationaryReturnDefense.ShouldHandOffPressure(false,false,new(),new(),new(10,100)) &&
            !StationaryReturnDefense.ShouldHandOffPressure(false,true,new(),new(),default) &&
            !StationaryReturnDefense.ShouldHandOffPressure(false,true,new(),new(),new(0,100)),
            "pressure handoff bypassed group/assignment/range/living-health boundaries");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"stationary-return-defense-checks.json"),JsonSerializer.Serialize(new
        {
            Passed=true,HardwareInputEmitted=false,
            Checks=new[]{"near-anchor and swing-range exact boundaries","known living local/target health and fresh approval required",
                "stationary family and in-range Gamekeeper only","1000ms chunk with50ms polls","normal incoming damage retains defense",
                "target identity/death/unknown health/scope/protection and saved-anchor changes yield","initial pre-attack revalidation",
                "focus loss/stop and callback failure release input","return episode and completed repair preserved",
                "transit points excluded","repeated anchor pressure hands off without forgetting damage"}
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
