using System.Text.Json;
namespace PoteHunter;

internal static class ArrivedCombatHandoffChecks
{
    static void Require(bool condition,string reason)
    {if(!condition)throw new Exception("Arrived combat handoff: "+reason);}
    static Entity Mob(uint id,long address,Vec position,uint generation=1)=>
        new(address,id,"Lv. 1 Pulkhan",position,10,Generation:generation);
    static StationaryReturnDefense.Observation Sample(Entity target,Health health)=>
        new(new(.287,0),default,new(75,100),target,health,2.5,true,true);

    internal static async Task Run()
    {
        await CheckFreshLootHandoff();
        var low=Mob(0x80001755,100,new(2,0));var high=Mob(0x80011755,200,new(2.5,0));
        var fresh=Mob(0x80021755,300,new(1,0));
        var health=new Dictionary<uint,Health>{{low.Id,new(40,100)},{high.Id,new(80,100)},{fresh.Id,new(100,100)}};
        Require(ArrivedCombatHandoff.Choose([low,high,fresh],[low,high],health,default,2.5)==high,
            "fresh spawn displaced the highest-HP owned target");
        Require(ArrivedCombatHandoff.Choose([low,high],[low,high],health,default,2.5,e=>e!=high)==low,
            "protected owned target displaced an approved member");
        var replaced=high with{Generation=2,Address=201};
        Require(ArrivedCombatHandoff.Choose([replaced],[high],health,default,2.5)==null,
            "same-ID replacement borrowed the old engagement");
        Require(ArrivedCombatHandoff.Choose([high,replaced],[high],health,default,2.5,e=>e==high)==null,
            "approval filtering hid an ambiguous identity");
        // An incomplete duplicate can be excluded by Encounter's finite-scene
        // filter while its ordinary candidate list still contains the old
        // high-HP identity. Arrival selection must use the complete scene and
        // retain the other owned member instead.
        var filteredShadow=replaced with{Position=new(double.NaN,0)};
        var queue=new Encounter();queue.MarkAttack(low,health[low.Id]);queue.MarkAttack(high,health[high.Id]);
        queue.Observe([low,high,filteredShadow],health,default,6,(_,_)=>true,(_,_)=>true);
        Require(Targeting.ChooseStationaryEngaged(queue.EngagedCandidates,health,default,2.5)==high,
            "filtered-shadow fixture did not retain the ordinary higher-HP candidate");
        Require(ArrivedCombatHandoff.Choose([low,high,filteredShadow],queue.EngagedCandidates,health,default,2.5)==low,
            "complete-scene ambiguity displaced the safe lower-HP owned member");
        Require(ArrivedCombatHandoff.Choose([high with{Position=new(2.50001,0)}],[high],health,default,2.5)==null,
            "just-outside target deferred facing without attack reach");
        health[high.Id]=default;
        Require(ArrivedCombatHandoff.Choose([high],[high],health,default,2.5)==null,"unknown HP borrowed arrival");
        health[high.Id]=new(0,100);
        Require(ArrivedCombatHandoff.Choose([high,fresh],[high],health,default,2.5)==null,
            "owned death during an awaited heal/mana observation admitted a fresh spawn");
        // Encounter remains active through the last death until the facing
        // transition and ordinary quiet/cleanup path have completed.
        Require(!HealingRest.MayStart(new(75,100),new HotbarSnapshot(0,[]),false,false,true,0,false,false,75),
            "last owned death admitted rest before encounter/facing cleanup");
        health[high.Id]=new(80,100);

        var sample=Sample(high,new(80,100));var handoff=new ArrivedCombatHandoff();
        foreach(var invalid in new[]{sample with{PlayerHealth=default},sample with{PlayerHealth=new(0,100)},
            sample with{TargetHealth=default},sample with{TargetHealth=new(0,100)},sample with{ContextVerified=false},
            sample with{TargetApproved=false},sample with{Position=new(.50001,0)},sample with{Target=high with{Position=new(3,0)}}})
            Require(!handoff.TryDefer(invalid,true,RestPosture.Standing,1,7,0)&&!handoff.Pending,
                "invalid health/arrival/protection/context deferred saved facing");
        foreach(var posture in new[]{RestPosture.Unknown,RestPosture.SittingDown,RestPosture.Resting,RestPosture.StandingUp})
            Require(!handoff.TryDefer(sample,true,posture,1,7,0),"unknown or non-standing posture borrowed arrival");
        Require(!handoff.TryDefer(sample,false,RestPosture.Standing,1,7,0),"unsettled point borrowed arrival");
        Require(!handoff.TryDefer(sample,true,RestPosture.Standing,double.NaN,7,0),"missing saved heading deferred facing");

        // The released-attack gap came from a final small correction followed
        // by saved-facing restoration while an already owned pack survived.
        // Travel releases attack, settles, then defers the turn to that pack.
        Vec position=new(.549,0);long now=0;bool forward=false,attackHeld=true;int savedTurns=0,moves=0;
        Task Delay(int ms,CancellationToken ct){ct.ThrowIfCancellationRequested();now+=ms;return Task.CompletedTask;}
        bool arrived=await AnchorArrival.ReturnAsync(()=>position,default,.5,
            ct=>{ct.ThrowIfCancellationRequested();attackHeld=false;forward=true;moves++;position=new(.287,0);return Task.CompletedTask;},
            ()=>forward=false,async ct=>
            {
                Require(!forward&&!attackHeld,"attack was carried through final movement");
                Vec stopped=position;await Delay(120,ct);
                bool settled=AnchorArrival.Settled(stopped,position,default,.5);
                Require(handoff.TryDefer(sample with{Position=position},settled,RestPosture.Standing,1,7,now),
                    "settled known living owned pack did not receive arrival");
                await FacingRestore.RunAsync(_=>Task.FromResult(true),()=>{},()=>forward=false,Delay,()=>now,
                    ()=>new TurnUnresponsiveException(position,new(0,-1)),ct);
            },Delay,()=>now,default);
        Require(arrived&&moves==1&&now<1000&&handoff.Pending&&savedTurns==0&&!forward,
            "settled handoff waited for saved-facing turn or carried movement");
        Require(handoff.Matches(default,1,7),"arrival lost its exact anchor/heading/recovery context");
        // After ownership clears, a new spawn is excluded and actual saved
        // facing must complete before the ordinary fresh-selection path.
        health[high.Id]=new(0,100);
        Require(ArrivedCombatHandoff.Choose([high,fresh],[high],health,position,2.5)==null&&handoff.Pending,
            "fresh candidate erased pending facing after last owned death");
        await FacingRestore.RunAsync(_=>{savedTurns++;return Task.FromResult(true);},()=>{},()=>{},Delay,()=>now,
            ()=>new TurnUnresponsiveException(position,new(0,-1)),default);
        handoff.Reset();
        Require(savedTurns==1&&!handoff.Pending,"successful saved facing did not release the pending transition");

        // A progressing single target and repeated same-anchor assist/loot
        // returns get one absolute allowance; neither target/HP changes nor a
        // completed repair epoch may restart it.
        handoff=new();now=1000;
        Require(handoff.TryDefer(sample,true,RestPosture.Standing,1,7,now),"initial deferral unavailable");
        for(int step=1;step<=119;step++)
        {
            now=1000+step*1000;
            var next=sample with{Target=step%2==0?high:low,TargetHealth=new(100-step%70,100)};
            Require(!handoff.Expired(now)&&handoff.TryDefer(next,true,RestPosture.Standing,1,7,now),
                "same owned fight/assist return lost its remaining allowance");
        }
        handoff.CompleteRecovery(default,1,7,8);
        Require(handoff.Matches(default,1,8)&&!handoff.Matches(default,1,7),"confirmed repair-return completion lost pending facing");
        now=121000;
        Require(handoff.Expired(now)&&!handoff.TryDefer(sample,true,RestPosture.Standing,1,8,now),
            "continuous progress/spawns/repair completion renewed the hard deadline");
        // Expiry terminates an ordinary target loop before another attack. A
        // priority Gamekeeper keeps its existing fight and saved return owner.
        int ordinaryAttacks=0;while(!handoff.Expired(now)){ordinaryAttacks++;now+=50;}
        Require(ordinaryAttacks==0,"long single-target fight attacked after the absolute allowance");
        var keeper=Mob(0x80001752,400,new(1,0)) with{Name="Gamekeeper",Model="MON_SnowGun2.GCMDS"};
        Require(Targeting.IsGamekeeper(keeper),"priority fixture invalid");
        bool breakOrdinary=handoff.Pending&&!Targeting.IsGamekeeper(keeper)&&handoff.Expired(now);
        Require(!breakOrdinary&&handoff.Pending,"expiry starved Gamekeeper or forgot saved facing");
        bool rollback=false;try{handoff.Expired(now-1);}catch(InvalidOperationException){rollback=true;}
        Require(rollback,"clock rollback admitted another pending combat pass");
        Require(!handoff.Matches(new(1,0),1,8)&&!handoff.Matches(default,2,8)&&!handoff.Matches(default,1,9),
            "changed destination/facing/death episode borrowed old intent");
        handoff.Reset();
        Require(handoff.TryDefer(sample with{Anchor=new(.1,0)},true,RestPosture.Standing,2,9,0),
            "a new verified recovery destination retained stale state");

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"arrived-combat-handoff-checks.json"),JsonSerializer.Serialize(new
        {
            Passed=true,HardwareInputEmitted=false,
            Checks=new[]{"selected in-range combat precedes optional anchor loot","post-loot settled exact fresh reservation precedes saved turn",
                "reservation revalidates identity/HP/range/protection and cannot chain spawns or renew lease",
                "settled final correction hands owned pack back before saved turn","movement releases attack before handoff",
                "fresh highest-HP living owned target only","approval cannot hide all-scene identity ambiguity",
                "incomplete duplicate leaves ordinary queue but arrival selects another owned identity",
                "last owned death during heal/mana excludes fresh spawn and rest","unknown/dead/protected/out-of-range/unsettled/posture/context rejected",
                "saved facing still pending after physical arrival","actual facing precedes fresh selection after owned pack clears",
                "single-target and repeated target/assist/loot returns retain one120-second allowance",
                "completed repair epoch transfers without renewing allowance","Gamekeeper does not enter expired ordinary-target loop",
                "clock rollback and changed anchor/heading/death episode excluded"}
        },new JsonSerializerOptions{WriteIndented=true}));
    }

    static async Task CheckFreshLootHandoff()
    {
        var selected=Mob(0x80001755,100,new(2,0));
        var other=Mob(0x80011755,200,new(1,0)) with{Name="Lv. 1 Tribal"};
        var hp=new Dictionary<uint,Health>{{selected.Id,new(100,100)},{other.Id,new(100,100)}};
        bool Approved(Entity e)=>Targeting.Eligible(e,hp.GetValueOrDefault(e.Id),Threat.Unknown,"Pulkhan",[],false);
        Entity? Pick(IEnumerable<Entity> scene,Vec position=default,double radius=8)=>
            ArrivedCombatHandoff.ChooseFresh(scene,hp,position,default,2.5,radius,Approved);
        // The nearer unrelated mob must not win this early housekeeping gate.
        Require(Pick([other,selected])==selected,"selected nearby combat lost to housekeeping or an unrelated mob");
        var farther=Mob(0x80021755,300,new(2.4,0));hp[farther.Id]=new(200,200);
        Require(Pick([farther,selected])==selected,"fresh selection borrowed highest-HP owned-pack ordering");
        Require(Pick([selected],new(.50001,0))==null&&Pick([selected],radius:1.99)==null,
            "early combat expanded arrival tolerance or hunt boundary");
        foreach(var unavailable in new[]{default(Health),new Health(0,100)})
        {hp[selected.Id]=unavailable;Require(Pick([selected])==null,"unknown/dead HP entered early combat");}
        hp[selected.Id]=new(100,100);
        Require(Pick([selected with{Position=new(2.50001,0)}])==null,"early combat began an out-of-range chase");
        var replacement=selected with{Generation=2,Address=101};
        Require(Pick([selected,replacement with{Position=new(double.NaN,0)}])==null,
            "approval filtering hid an ambiguous fresh identity");
        Require(ArrivedCombatHandoff.ChooseFresh([selected],hp,default,default,2.5,8,_=>false)==null,
            "protected target entered early combat");

        var handoff=new ArrivedCombatHandoff();long now=0;int savedTurns=0;bool attackHeld=false,walking=true;
        Task Delay(int ms,CancellationToken ct){ct.ThrowIfCancellationRequested();now+=ms;return Task.CompletedTask;}
        var sample=new StationaryReturnDefense.Observation(default,default,new(75,100),selected,hp[selected.Id],2.5,true,true);
        Require(!handoff.TryDeferFresh(sample with{ContextVerified=false},true,RestPosture.Standing,1,7,now)&&!handoff.Pending,
            "fresh reservation bypassed return context");
        bool arrived=await AnchorArrival.ReturnAsync(()=>default,default,.5,_=>throw new Exception("unexpected movement"),
            ()=>walking=false,async ct=>
            {
                await Delay(120,ct);
                Require(!walking&&!attackHeld,"fresh combat retained travel input");
                Require(handoff.TryDeferFresh(sample,true,RestPosture.Standing,1,7,now),"settled fresh target did not defer optional facing");
            },Delay,()=>now,default);
        Entity? Pending(IEnumerable<Entity> scene,IEnumerable<Entity>? owned=null,Func<Entity,bool>? guard=null)=>
            handoff.ChoosePending(scene,owned??[],hp,default,2.5,guard??(_=>true),Approved);
        Require(arrived&&savedTurns==0&&Pending([selected,other])==selected,
            "post-loot arrival waited for a saved turn before eligible combat");
        var keeper=Mob(0x80001752,400,new(2,0)) with{Model="MON_SnowGun2.GCMDS"};
        Require(Targeting.IsGamekeeper(keeper)&&handoff.IsFreshReservation(selected)&&!handoff.IsFreshReservation(keeper)&&
            !handoff.IsFreshReservation(farther)&&!handoff.IsFreshReservation(replacement),
            "first-attack reservation guard captured a priority target, other owned target, or replacement");
        Require(Pending([selected,farther],[farther])==farther,"fresh reservation displaced existing owned combat");
        Require(Pending([replacement])==null&&Pending([selected,replacement])==null,
            "same-ID replacement inherited the reservation");
        Require(Pending([selected],guard:_=>false)==null,"reserved target bypassed fresh protection");
        Require(Pending([selected with{Position=new(3,0)}])==null,"reserved target left swing range");
        hp[selected.Id]=default;Require(Pending([selected])==null,"awaited observation reused old readable HP");
        hp[selected.Id]=new(0,100);
        Require(Pending([selected,other])==null,"last reserved death chained a different spawn");
        Require(!handoff.TryDeferFresh(sample with{Target=other,TargetHealth=hp[other.Id]},true,RestPosture.Standing,1,7,now+100),
            "fresh target renewed an existing lease");
        hp[selected.Id]=new(100,100);
        Require(handoff.Expired(now+ArrivedCombatHandoff.MaximumDeferralMilliseconds),"fresh handoff renewed the absolute facing budget");
        // The caller now gives actual saved facing a turn before another fresh
        // reservation, just as for an exhausted owned pack.
        await FacingRestore.RunAsync(_=>{savedTurns++;return Task.FromResult(true);},()=>{},()=>{},Delay,()=>now,
            ()=>new TurnUnresponsiveException(default,new(0,-1)),default);
        handoff.Reset();
        Require(savedTurns==1&&!handoff.Pending&&Pending([selected])==null,"completed facing retained a stale reservation");
    }
}
