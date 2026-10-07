using System.Text.Json;
namespace PoteHunter;

internal static class SurvivalCombatHandoffChecks
{
    static void Require(bool condition,string reason)
    {if(!condition)throw new Exception("Survival handoff: "+reason);}
    static Entity Target(uint generation=1)=>new(100,0x80001755,"Lv. 1 Pulkhan",new(2.593,0),10,Generation:generation);
    static SurvivalCombatHandoff.Observation Sample(Entity? target=null)=>
        new(default,default,new(6992,16610),target??Target(),new(9188,9188),2.5,true,true,true,7);

    internal static async Task Run()
    {
        var drain=new HotbarSlot("1",SlotKind.Skill,12,"Power Drain Lv.1",1000,0,false,0,
            SkillUse:SkillUseKind.Instance,SkillTarget:SkillTargetKind.Enemy);
        var bash=drain with{Key="2",Id=13,Name="Bash"};var options=new Options();
        var ready=new HotbarSnapshot(0,[drain,bash]);var retry=new Dictionary<char,long>();
        Require(CombatSkillPolicy.ChoosePriorityHeal("12",1,ready,retry,0,new(6992,16610),options,_=>true)==0,
            "recorded below-threshold drain waited behind offense");
        foreach(var bar in new[]{new HotbarSnapshot(0,[drain with{RemainingCooldown=1},bash]),
            new HotbarSnapshot(0,[drain with{Locked=true},bash])})
            Require(CombatSkillPolicy.ChoosePriorityHeal("12",0,bar,retry,0,new(40,100),options,_=>true)==-1,
                "survival selection bypassed a live cooldown or lock");
        retry['1']=1000;
        Require(CombatSkillPolicy.ChoosePriorityHeal("12",0,ready,retry,999,new(40,100),options,_=>true)==-1 &&
            CombatSkillPolicy.ChoosePriorityHeal("12",0,ready,retry,1000,new(40,100),options,_=>true)==0,
            "survival selection bypassed its failed-cast retry");
        Require(CombatSkillPolicy.ChoosePriorityHeal("12",0,ready,retry,1000,new(51,100),options,_=>true)==-1 &&
            CombatSkillPolicy.ChoosePriorityHeal("12",0,ready,retry,1000,new(40,100),options,_=>false)==-1,
            "HP or MP eligibility was bypassed");
        options.HealthConditionKeys="2";
        Require(!CombatSkillPolicy.IsPriorityHeal(bash,options),"HP-conditioned offensive key entered survival priority");
        Require(SurvivalCombatHandoff.RequiresEnemy(drain) && !SurvivalCombatHandoff.RequiresEnemy(
            drain with{Name="Greater Healing",SkillTarget=SkillTargetKind.Friend}),"friendly heal authorized outward chase");
        Require(SkillHealthRule.ActivationBlockedReason(drain,drain with{Id=99},new(40,100),options)=="live skill slot identity changed" &&
            SkillHealthRule.ActivationBlockedReason(drain,drain with{Locked=true},new(40,100),options)=="live skill slot locked" &&
            SkillHealthRule.ActivationBlockedReason(drain,drain with{RemainingCooldown=1},new(40,100),options)=="live skill cooldown" &&
            SkillHealthRule.ActivationBlockedReason(drain,drain,default,options)=="HP condition health unavailable" &&
            SkillHealthRule.ActivationBlockedReason(drain,drain,new(0,100),options)=="HP condition character or recipient dead" &&
            SkillHealthRule.ActivationBlockedReason(drain,drain,new(51,100),options)=="HP above assigned threshold",
            "activation audit collapsed distinct slot, cooldown and health causes");
        var liveSlot=drain;var liveHp=new Health(40,100);bool liveMana=true,liveCombat=true;
        int finalAdmissions=0,withheldAdmissions=0;
        var finalGuard=SkillHealthRule.FreshActivationGuard(drain,options,()=>liveSlot,()=>liveHp,_=>liveMana,
            ()=>liveCombat,(_,_)=>finalAdmissions++,(_,_,_)=>withheldAdmissions++);
        foreach(Action preflightChange in new Action[]
        {
            ()=>liveHp=new(51,100),()=>liveHp=default,()=>liveHp=new(0,100),
            ()=>liveSlot=drain with{Id=99},()=>liveSlot=drain with{RemainingCooldown=1},
            ()=>liveSlot=drain with{Locked=true},()=>liveMana=false,()=>liveCombat=false
        })
        {
            liveSlot=drain;liveHp=new(40,100);liveMana=liveCombat=true;
            bool activated=await SkillHealthRule.ActivateGuardedAsync(finalGuard,admission=>
            {
                // The real input path performs its scene preflight first;
                // the exact shared guard used by HunterForm then reads again.
                preflightChange();return Task.FromResult(admission?.Invoke()??true);
            });
            Require(!activated && finalAdmissions==0,"final input preflight invalidation still admitted a heal");
        }
        Require(withheldAdmissions==7,"precise final slot/health/MP reasons were lost");
        liveSlot=drain;liveHp=new(50,100);liveMana=liveCombat=true;
        Require(await SkillHealthRule.ActivateGuardedAsync(finalGuard,admission=>Task.FromResult(admission?.Invoke()??true)) &&
            finalAdmissions==1,"inclusive assigned HP boundary lost a guarded activation");
        var owned=Target();var fresh=owned with{Id=0x80011755,Address=101,Position=new(1,0)};
        var health=new Dictionary<uint,Health>{{owned.Id,new(9188,9188)},{fresh.Id,new(9188,9188)}};
        Require(SurvivalCombatHandoff.Choose([owned,fresh],[owned],health,default,2.5,_=>true)==owned,
            "nearby full-health spawn stole verified ownership");
        Require(SurvivalCombatHandoff.Choose([owned with{Generation=2}],[owned],health,default,2.5,_=>true)==null &&
            SurvivalCombatHandoff.Choose([owned,owned with{Generation=2}],[owned],health,default,2.5,_=>true)==null,
            "replacement or ambiguous generation borrowed survival ownership");
        Require(SurvivalCombatHandoff.Choose([owned with{Position=new(3.001,0)}],[owned],health,default,2.5,_=>true)==null,
            "survival correction expanded by more than half a map unit");
        int previousGuardCalls=0;
        var bounded=SurvivalCombatHandoff.BoundAdvance((from,to)=>{previousGuardCalls++;return to.X<.45;},default);
        Require(bounded(default,new(.273,0)) && previousGuardCalls==1 &&
            !bounded(new(.273,0),new(.657,0)) && previousGuardCalls==1 &&
            !bounded(new(.273,0),new(.46,0)) && previousGuardCalls==2 &&
            !bounded(new(.501,0),new(.49,0)) && previousGuardCalls==2 &&
            !SurvivalCombatHandoff.BoundAdvance(null,default)(default,new(.1,0)),
            "physical .273+.384 pulse escaped the original-origin cap or replaced its collision predicate");

        // Execute the exact async runner used by HunterForm. The recorded .093
        // gap takes one approved correction and activates only after measured
        // range entry and settling. No input API is used by these delegates.
        long now=0;int moves=0,casts=0,stops=0;bool forward=false;
        var sample=Sample();var handoff=new SurvivalCombatHandoff();
        var outcome=await handoff.RunAsync(()=>sample,(goal,ct)=>
        {
            ct.ThrowIfCancellationRequested();moves++;forward=true;
            Require((goal-sample.Position).Length<=.5 && (goal-sample.Anchor).Length<=1.5,
                "urgent geometry exceeded correction/leash");
            sample=sample with{Position=new(.20,0)};now+=60;return Task.CompletedTask;
        },()=>{stops++;forward=false;},async (guard,ct)=>
        {
            Require(!forward && (sample.Target!.Position-sample.Position).Length<=2.5,"cast before stop or strict reach");
            now+=130;
            await SkillHealthRule.ActivateGuardedAsync(guard,admission=>
            {bool admitted=admission?.Invoke()??true;if(admitted)casts++;return Task.FromResult(admitted);});
        },(ms,ct)=>{ct.ThrowIfCancellationRequested();now+=ms;return Task.CompletedTask;},()=>now,default);
        Require(outcome==SurvivalHandoffOutcome.CastAttempted && moves==1 && casts==1 && !forward && stops>=2 &&
            handoff.CorrectionConsumed,"recorded gap remained starved or correction did not end safely");

        // The same low-health episode cannot rearm another correction after
        // return, a target switch, an unknown read, or a failed cooldown retry.
        sample=Sample(Target(2));now+=1000;
        outcome=await handoff.RunAsync(()=>sample,(_,_)=>{moves++;return Task.CompletedTask;},()=>{},
            (_,_)=>{casts++;return Task.CompletedTask;},(_,_)=>Task.CompletedTask,()=>now,default);
        Require(outcome==SurvivalHandoffOutcome.Unavailable && moves==1 && casts==1,"target switch renewed urgent budget");
        handoff.ObserveHealth(default,50);handoff.ObserveHealth(new(316,16610),50);
        Require(handoff.CorrectionConsumed,"unknown or falling health renewed urgent budget");
        handoff.ObserveHealth(new(51,100),50);
        Require(!handoff.CorrectionConsumed,"verified healthy episode did not release correction budget");

        foreach(var invalidate in new Func<SurvivalCombatHandoff.Observation,SurvivalCombatHandoff.Observation>[]
        {
            value=>value with{ContextVerified=false,BlockedReason="focus changed"},
            value=>value with{Target=value.Target! with{Generation=2}},
            value=>value with{Target=value.Target! with{Address=101}},
            value=>value with{PlayerHealth=new(0,16610)},value=>value with{PlayerHealth=default},
            value=>value with{TargetHealth=new(0,9188)},value=>value with{TargetHealth=default},
            value=>value with{Owned=false},value=>value with{SkillReady=false},
            value=>value with{RecoveryEpisode=8},value=>value with{Anchor=new(1,0)},
            value=>value with{Position=new(.526,0)},
            value=>value with{ContextVerified=false,BlockedReason="rest posture is not verified standing"},
            value=>value with{ContextVerified=false,BlockedReason="repair owns input"},
            value=>value with{ContextVerified=false,BlockedReason="Gamekeeper has priority"}
        })
        {
            sample=Sample();now=0;moves=casts=stops=0;handoff=new();
            outcome=await handoff.RunAsync(()=>sample,(_,_)=>
                {moves++;sample=invalidate(sample with{Position=new(.20,0)});now+=60;return Task.CompletedTask;},
                ()=>stops++,(_,_)=>{casts++;return Task.CompletedTask;},(ms,_)=>{now+=ms;return Task.CompletedTask;},()=>now,default);
            Require(outcome==SurvivalHandoffOutcome.Yielded && moves==1 && casts==0 && stops>=2 && handoff.CorrectionConsumed,
                "fresh focus/identity/health/ownership/posture/recovery/leash gate was bypassed after a move");
        }

        // In-range admission changes during key selection must prevent both
        // the first activation and an injected fallback without blind input.
        foreach(bool fallback in new[]{false,true})
        {
            sample=Sample() with{Position=new(.20,0)};now=0;casts=0;handoff=new();
            outcome=await handoff.RunAsync(()=>sample,(_,_)=>throw new Exception("unneeded travel"),()=>{},
                async (guard,ct)=>
                {
                    now+=130;sample=sample with{Target=sample.Target! with{Position=new(3,0)}};
                    int attempts=fallback?2:1;
                    for(int i=0;i<attempts;i++)await SkillHealthRule.ActivateGuardedAsync(guard,admission=>
                    {bool admitted=admission?.Invoke()??true;if(admitted)casts++;return Task.FromResult(admitted);});
                },(ms,_)=>{now+=ms;return Task.CompletedTask;},()=>now,default);
            Require(outcome==SurvivalHandoffOutcome.CastAttempted && casts==0 && !handoff.CorrectionConsumed,
                "post-key target departure permitted activation/fallback or charged unneeded movement");
        }
        sample=Sample();now=0;casts=0;handoff=new();
        outcome=await handoff.RunAsync(()=>sample,(_,_)=>{now+=25;return Task.CompletedTask;},()=>{},
            (_,_)=>{casts++;return Task.CompletedTask;},(ms,_)=>{now+=ms;return Task.CompletedTask;},()=>now,default);
        Require(outcome==SurvivalHandoffOutcome.NoProgress && now>=1500 && now<=2000 && casts==0,
            "no-displacement corrections waited or activated indefinitely");
        sample=Sample();now=0;handoff=new();
        outcome=await handoff.RunAsync(()=>sample,(_,_)=>{now=2000;return Task.CompletedTask;},()=>{},
            (_,_)=>throw new Exception("cast after deadline"),(ms,_)=>{now+=ms;return Task.CompletedTask;},()=>now,default);
        Require(outcome==SurvivalHandoffOutcome.DurationExpired && handoff.CorrectionConsumed,"absolute deadline renewed after await");
        var cancel=new CancellationTokenSource();sample=Sample();handoff=new();stops=casts=0;now=0;
        bool canceled=false;
        try
        {
            await handoff.RunAsync(()=>sample,(_,_)=>{cancel.Cancel();return Task.CompletedTask;},()=>stops++,
                (_,_)=>{casts++;return Task.CompletedTask;},(_,ct)=>{ct.ThrowIfCancellationRequested();return Task.CompletedTask;},()=>now,cancel.Token);
        }
        catch(OperationCanceledException){canceled=true;}
        Require(canceled && casts==0 && stops>=2 && handoff.CorrectionConsumed,"in-flight cancellation lost cleanup or renewed budget");
        handoff=new();sample=Sample();bool collision=false;stops=casts=0;
        try {await handoff.RunAsync(()=>sample,(_,_)=>throw new RouteUnavailableException("blocked corridor"),()=>stops++,
            (_,_)=>{casts++;return Task.CompletedTask;},(_,_)=>Task.CompletedTask,()=>0,default);}
        catch(RouteUnavailableException){collision=true;}
        Require(collision && stops==1 && casts==0 && handoff.CorrectionConsumed,"collision refusal swallowed or permitted blind cast");
        foreach(string safetyReason in new[]{"Game window lost focus","Stopped by F9","Stopped by Enter"})
        {
            handoff=new();sample=Sample();stops=casts=0;bool safetyPropagated=false;
            try {await handoff.RunAsync(()=>sample,(_,_)=>throw new OperationCanceledException(safetyReason),()=>stops++,
                (_,_)=>{casts++;return Task.CompletedTask;},(_,_)=>Task.CompletedTask,()=>0,default);}
            catch(OperationCanceledException ex){safetyPropagated=ex.Message==safetyReason;}
            Require(safetyPropagated && stops==1 && casts==0 && handoff.CorrectionConsumed,
                "input safety cancellation was mistaken for the bounded time allowance");
        }
        // A blocked asynchronous movement or settlement await must itself
        // receive the fixed real deadline, rather than waiting for its return
        // before consulting a virtual clock. Neither operation emits input.
        foreach(bool blockedDelay in new[]{false,true})
        {
            handoff=new();sample=Sample();stops=casts=0;
            var elapsed=System.Diagnostics.Stopwatch.StartNew();
            outcome=await handoff.RunAsync(()=>sample,async (_,ct)=>
            {
                if(blockedDelay)sample=sample with{Position=new(.20,0)};
                else await Task.Delay(Timeout.Infinite,ct);
            },()=>stops++,(_,_)=>{casts++;return Task.CompletedTask;},
                (_,ct)=>Task.Delay(Timeout.Infinite,ct),()=>elapsed.ElapsedMilliseconds,default);
            Require(outcome==SurvivalHandoffOutcome.DurationExpired && elapsed.ElapsedMilliseconds is >=1800 and <4500 &&
                stops>=1 && casts==0 && handoff.CorrectionConsumed,
                "blocked move/settle await escaped the absolute two-second allowance");
        }
        var trace=new SelfHealBlockTrace();
        Require(trace.ShouldRecord("1","outside reach",0) && !trace.ShouldRecord("1","outside reach",1999) &&
            trace.ShouldRecord("1","outside reach",2000) && trace.ShouldRecord("1","identity changed",2001),
            "blocked-reason logs spammed identical passes or hid a changed cause");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"survival-combat-handoff-checks.json"),JsonSerializer.Serialize(new
        {
            Passed=true,HardwareInputEmitted=false,LiveGameTested=false,
            Checks=new[]{"recorded 2.593 geometry reaches strict 2.5 before guarded cast","physical .273+.384 pulse envelope bounded before W-down",
                "existing collision/route predicate composed and fails closed","no fresh-spawn or generation ownership",
                "whole low-HP episode correction budget","cooldown/retry/HP/MP/shared skill rules","friendly heals cannot justify travel",
                "fresh focus/identity/death/posture/recovery/priority/leash guards","post-key and fallback admission",
                "same final HP/cooldown/MP guard used by HunterForm after input preflight",
                "no-progress and fixed real deadline covers blocked move/settle awaits","cancellation and collision cleanup",
                "focus/F9/Enter safety cancellation propagates","deduplicated blocked reasons"}
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
