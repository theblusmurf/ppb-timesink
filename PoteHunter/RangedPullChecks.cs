using System.Text.Json;

namespace PoteHunter;

public static class RangedPullChecks
{
    public static void Run()
    {
        static Entity Mob(uint id,double x,uint generation=1,long? address=null,string name="Lv. 1 Monster")=>
            new(address??id+100,id,name,new(x,0),0,Generation:generation);
        static Dictionary<uint,Health> Healthy(params Entity[] entities)=>entities.ToDictionary(entity=>entity.Id,_=>new Health(100,100));
        static void Require(bool condition,string message) {if(!condition)throw new Exception(message);}

        Vec origin=new(0,0);
        var failedShot=new RangedPull();failedShot.Begin(0);
        var wolf=Mob(0x804d001b,5);
        for(int retry=0;retry<3;retry++)
            Require(!failedShot.ConfirmShot(wolf,new(1154,1154),new(1154,1154),false,retry) && failedShot.AttemptedCount==0,
                "Recorded Firing failures with unchanged 1154 HP incorrectly counted as a pulled monster.");
        Require(failedShot.ChooseNext([wolf],Healthy(wolf),origin,origin,10,10)==wolf,
            "A rejected shot without cooldown activation was not eligible for retry.");
        Require(!RangedPull.ShotConfirmed(new(1154,1154),default,false) &&
            failedShot.ConfirmShot(wolf,new(1154,1154),new(1100,1154),false,4) && failedShot.AttemptedCount==1,
            "Unknown HP was treated as damage, or a confirmed hit was lost.");
        var blocked=Mob(0x804d001c,5);
        Require(failedShot.ConfirmShot(blocked,new(1100,1154),new(1100,1154),true,5) && failedShot.AttemptedCount==2,
            "A blocked or missed Firing activation was not retained as a distinct pull attempt.");
        Require(RangedPull.ShotConfirmed(new(1154,1154),new(1154,1154),true),
            "A cooldown activation was not accepted as evidence of a pull attempt.");
        failedShot.Update([wolf,blocked],Healthy(wolf,blocked),origin,2,6,5,10,10,false,true);
        Require(failedShot.Phase==RangedPullPhase.Tagging,
            "A two-member confirmed pull switched to melee before reaching its five-ID contract.");
        Require(!RangedPull.ShotConfirmed(new(1154,1154),new(1100,1155),false),
            "A health observation with a changed maximum was treated as confirmed damage.");
        Require(RangedPull.WithinNearby3D(wolf with {Position=new Vec(1,0),Height=1},origin,0,2) &&
            !RangedPull.WithinNearby3D(wolf with {Position=new Vec(1,0),Height=3},origin,0,2),
            "Nearby filtering ignored vertical separation between floors.");
        Require(!RangedPull.ShouldStopTagging(new(51,100),50,false) &&
            RangedPull.ShouldStopTagging(new(50,100),50,false) &&
            RangedPull.ShouldStopTagging(new(99,100),50,true) &&
            !RangedPull.ShouldStopTagging(new(100,100),100,false),
            "Pack tagging did not use the configured inclusive recovery threshold or pending recovery state.");
        var phaseGate=new RangedPull();phaseGate.Begin(0);
        Require(!phaseGate.ConfirmShot(wolf,new(1154,1154),new(1154,1154),false,1),"A failed firing unexpectedly confirmed.");
        phaseGate.Update([wolf],Healthy(wolf),origin,10,2,1,10,10,false,false);
        Require(phaseGate.Phase==RangedPullPhase.Tagging && phaseGate.AttemptedCount==0,
            "A failed firing advanced the pack phase before any confirmation.");
        Require(phaseGate.ConfirmShot(wolf,new(1154,1154),new(1100,1154),false,3),"Confirmed damage did not advance the bounded pull.");
        phaseGate.Update([wolf],Healthy(wolf),origin,10,4,1,10,10,false,false);
        Require(phaseGate.Phase==RangedPullPhase.Clearing && phaseGate.AttemptedCount==1,
            "A confirmed one-ID pull did not switch directly to melee mode.");
        var first=Mob(0x80000001,3);var second=Mob(0x80000002,5);var third=Mob(0x80000003,7);
        Entity[] pack=[first,second,third];var hp=Healthy(pack);
        var pull=new RangedPull();pull.Begin(0);
        Require(pull.Active && pull.Phase==RangedPullPhase.Tagging && pull.AttemptedCount==0,"A ranged pull did not begin in an empty tagging phase.");
        Require(pull.ChooseNext(pack,hp,origin,origin,20,20)==first && pull.AttemptedCount==0,
            "Choosing a tag counted an attack that was never submitted.");
        pull.MarkAttempt(first,10);pull.MarkAttempt(second,20);pull.MarkAttempt(third,30);pull.MarkAttempt(first,40);
        Require(pull.AttemptedCount==3 && pack.All(pull.Contains),"Three distinct submitted tags were not retained exactly once.");
        pull.Update(pack,hp,origin,10,50,3,10,10,false,false);
        Require(pull.Phase==RangedPullPhase.Clearing && pull.AttemptedCount==3,
            "The confirmed three-ID pull did not switch directly to melee mode.");

        pull.Reset();pull.Begin(100);pull.MarkAttempt(first,110);
        pull.Update([first],hp,origin,2,10_099,3,10,10,false,false);
        Require(pull.Phase==RangedPullPhase.Tagging,"Tagging timed out before its inclusive deadline.");
        pull.Update([first],hp,origin,2,10_100,3,10,10,false,false);
        Require(pull.Phase==RangedPullPhase.Tagging,"A sub-five population stayed gathering after the tag timeout.");
        pull.Update([first],hp,origin,2,15_000,3,10,10,false,false);
        Require(pull.Phase==RangedPullPhase.Tagging,"A sub-five distant population did not remain eligible for replacement pulling.");
        pull.BeginGathering(15_050);
        pull.Update([],hp,origin,2,15_100,3,10,10,false,false);
        Require(pull.Phase==RangedPullPhase.Tagging && pull.AttemptedCount==0,
            "A missing tagged population did not reopen replacement pulling.");
        pull.Update([first with {Position=new(2,0)}],hp,origin,2,15_200,3,10,10,false,false);
        Require(pull.Phase==RangedPullPhase.Tagging,"One nearby monster incorrectly started clearing or paused replacement pulling.");

        pull.Reset();pull.Begin(0);pull.MarkAttempt(first,1);pull.BeginGathering(2);
        pull.Update([first],hp,origin,1,10_001,3,5,10,false,false);
        Require(pull.Phase==RangedPullPhase.Tagging && pull.AttemptedCount==1,
            "A sub-five observed population did not immediately reopen tagging.");
        pull.Update([first],hp,origin,1,10_002,3,5,10,false,false);
        Require(pull.Phase==RangedPullPhase.Tagging && pull.AttemptedCount==1,
            "Elapsed time controlled gathering or the sub-five population did not reopen tagging.");

        pull.Reset();pull.Begin(0);pull.MarkAttempt(first,1);pull.Update([first],hp,origin,10,2,3,10,10,true,false);
        Require(pull.Phase==RangedPullPhase.Gathering,"Health pressure started clearing before five nearby monsters.");
        pull.Reset();pull.Begin(0);pull.MarkAttempt(first,1);pull.BeginGathering(2);
        pull.Update([first],hp,origin,1,3,3,10,10,true,true);
        Require(pull.Phase==RangedPullPhase.Gathering,"Health pressure started clearing from Gathering before five nearby monsters.");

        pull.Reset();pull.Begin(0);pull.MarkAttempt(first,1);pull.MarkAttempt(second,2);pull.MarkAttempt(third,3);pull.BeginGathering(4);
        hp[first.Id]=new(0,100);hp[second.Id]=new(100,100);hp[third.Id]=new(100,100);
        pull.Update([first,second with {Position=new(.5,0)},third],hp,origin,1,5,3,10,10,false,false);
        Require(pull.Phase==RangedPullPhase.Tagging && pull.AttemptedCount==2 && pull.Contains(second) && pull.Contains(third),
            "A dead tag was retained or the living survivor did not reopen tagging for a replacement.");

        var oldLife=Mob(0x80000009,2,1,900);var newLife=oldLife with {Generation=2,Address=901};var reuseHp=Healthy(oldLife);
        pull.Reset();pull.Begin(0);pull.MarkAttempt(oldLife,1);
        Require(pull.Contains(oldLife) && !pull.Contains(newLife),"A reused entity identity inherited tag history.");
        Require(pull.ChooseNext([newLife],reuseHp,origin,origin,10,10)==newLife,"An unambiguous reused identity was blocked by an old tag.");
        Require(pull.ChooseNext([oldLife,newLife],reuseHp,origin,origin,10,10)==null,"An ambiguous reused ID was selected with unsafe health ownership.");

        var damaged=Mob(0x80000010,1);var priority=Mob(0x8000175F,1,address:1000) with {Model="NPC_AG_Container.gcmds"};
        var keeper=Mob(0x80001752,1,address:1001,name:"Gamekeeper") with {Model="MON_SnowGun2.GCMDS"};
        var outsideHunt=Mob(0x80000011,11);var outsideAttack=Mob(0x80000012,9);
        var nearest=Mob(0x80000013,4);var tiedLower=Mob(0x80000014,5);var tiedHigher=Mob(0x80000015,5);
        var candidates=new[]{damaged,priority,keeper,outsideHunt,outsideAttack,tiedHigher,tiedLower,nearest};var candidateHp=Healthy(candidates);
        candidateHp[damaged.Id]=new(99,100);
        pull.Reset();pull.Begin(0);
        Require(pull.ChooseNext(candidates,candidateHp,origin,origin,8,6)==nearest,
            "Selection accepted a damaged, protected, priority, or out-of-bounds candidate instead of the nearest permitted full-health monster.");
        Require(pull.ChooseNext([tiedHigher,tiedLower],candidateHp,origin,origin,8,6)==tiedLower,
            "Equal-distance selection was not stable by entity ID.");

        // Live regression: after one confirmed tag creates an engaged encounter,
        // Tagging must still be able to select a second member that needs a bounded
        // approach. The hunt anchor remains the hard acquisition boundary.
        var approachedSecond=Mob(0x80000016,12);var beyondHunt=Mob(0x80000017,21);
        var movingPackHp=Healthy(nearest,approachedSecond,beyondHunt);
        pull.MarkAttempt(nearest,1);
        Require(pull.Phase==RangedPullPhase.Tagging &&
            pull.ChooseNext([nearest,approachedSecond,beyondHunt],movingPackHp,origin,origin,20,40)==approachedSecond,
            "An engaged first tag blocked bounded movement to the next full-health pack member, or acquisition escaped the hunt radius.");
        pull.BeginGathering(2);
        Require(pull.ChooseNext([approachedSecond],movingPackHp,origin,origin,20,40)==null,
            "Gathering was allowed to acquire and chase another pack member.");
        pull.Reset();pull.Begin(0);

        Entity[] nearbyFive=Enumerable.Range(0,5).Select(i=>Mob((uint)(0x80000100+i),.4+i*.3)).ToArray();
        var nearbyHp=Healthy(nearbyFive);
        pull.Reset();pull.Begin(0);
        for(int i=0;i<nearbyFive.Length;i++)pull.MarkAttempt(nearbyFive[i],i+1);
        pull.Update(nearbyFive,nearbyHp,origin,2,6,5,10,10,false,false);
        Require(pull.Phase==RangedPullPhase.Clearing && pull.AttemptedCount==5,
            "Five confirmed IDs did not start melee mode immediately.");
        Require(pull.ApplyNearbyPolicy(nearbyFive,nearbyHp,origin,2,5,7)==5 && pull.Phase==RangedPullPhase.Clearing,
            "Nearby count changed an active tagged-ID melee phase.");
        Require(pull.ApplyNearbyPolicy(nearbyFive[..4],nearbyHp,origin,2,5,8)==4 && pull.Phase==RangedPullPhase.Clearing,
            "One to four nearby monsters ended an active tagged-ID melee phase.");
        var closeLone=Mob(0x80000300,.3) with {Position=new Vec(0,.3)};
        var clusterCenter=Mob(0x80000301,1) with {Position=new Vec(1,0)};
        var clusterLeft=Mob(0x80000302,1.1) with {Position=new Vec(1,.5)};
        var clusterRight=Mob(0x80000303,1.1) with {Position=new Vec(1,-.5)};
        var focus=RangedPull.ChooseMeleeCluster([closeLone,clusterLeft,clusterRight,clusterCenter],origin);
        Require(focus is {Target.Id:0x80000301,Density:3},"Melee clearing did not face the densest monster direction over a closer lone monster.");
        pull.Update([],new Dictionary<uint,Health>(),origin,2,9,5,10,10,false,false);
        Require(pull.ApplyNearbyPolicy([],new Dictionary<uint,Health>(),origin,2,5,10)==0 &&
            pull.Phase==RangedPullPhase.Tagging && pull.AttemptedCount==0,
            "Melee mode did not resume pulling after every tagged ID was gone.");
        var unknown=Mob(0x80000200,.5);var dead=Mob(0x80000201,.7);var distant=Mob(0x80000202,2.1);
        var excludedHp=Healthy(unknown,dead,distant);excludedHp[unknown.Id]=default;excludedHp[dead.Id]=new(0,100);
        Require(pull.ApplyNearbyPolicy([..nearbyFive[..4],unknown,dead,distant],excludedHp.Concat(nearbyHp).ToDictionary(kv=>kv.Key,kv=>kv.Value),origin,2,5,11)==4 &&
            pull.Phase==RangedPullPhase.Tagging,"Nearby count incorrectly changed a fresh pulling phase.");
        var clockIndependent=new RangedPull();clockIndependent.Begin(0);
        Entity[] distantFive=nearbyFive.Select((entity,index)=>entity with {Position=new Vec(20+index,0)}).ToArray();
        for(int i=0;i<distantFive.Length;i++)clockIndependent.MarkAttempt(distantFive[i],i+1);
        clockIndependent.BeginGathering(10);
        clockIndependent.ApplyNearbyPolicy(distantFive,Healthy(distantFive),origin,2,5,11);
        clockIndependent.Update(distantFive,Healthy(distantFive),origin,2,600_000,10,1,1,false,true);
        Require(clockIndependent.Phase==RangedPullPhase.Tagging && clockIndependent.AttemptedCount==5,
            "A partial tagged set did not resume pulling to complete its configured ID contract.");
        pull.Reset();
        Require(!pull.Active && pull.Phase==RangedPullPhase.Idle && pull.AttemptedCount==0 && !pull.Contains(nearest),
            "Reset retained phase or identity state from the completed pack.");

        var forward=Movement.CalculateTravelKeys(origin,new(0,-5),0);
        var right=Movement.CalculateTravelKeys(origin,new(-5,0),0);
        var diagonal=Movement.CalculateTravelKeys(origin,new(-5,-5),0);
        var behind=Movement.CalculateTravelKeys(origin,new(0,5),0);
        Require(forward.Forward&&!forward.Back&&!forward.Left&&!forward.Right &&
            !right.Right&&right.Forward&&!right.Back&&!right.Left && diagonal.Forward&&!diagonal.Left&&!diagonal.Right && behind.Forward&&!behind.Back,
            "Ranged travel keys did not stay mouse-steered and forward-only toward the waypoint.");
        pull.Begin(1);
        Vec? roam=pull.ChooseRoamWaypoint(origin,origin,origin,10,8,(from,to)=>to.X<=0);
        Require(roam is Vec selected && selected.Length<=4.0001 && selected.X<=0 && pull.RoamWaypoint==roam,
            "Roaming escaped its half-range/half-hunt orbit or accepted a blocked candidate.");
        Require(pull.ChooseRoamWaypoint(new Vec(.1,0),origin,origin,10,8,(from,to)=>true)==roam,
            "A live roaming waypoint was replanned before it was reached.");
        Require(RangedPull.RoamShootingRadius(24)==23 && RangedPull.RoamShootingRadius(1)==.5,
            "The shooting-range margin was not one unit or did not clamp for a small range.");
        Vec oldRoam=roam!.Value;
        Vec? rangeSafe=pull.ChooseRoamWaypoint(new Vec(.1,0),origin,new Vec(-20,0),10,30,(from,to)=>true);
        Require(rangeSafe==null || (rangeSafe.Value-new Vec(-20,0)).Length<=RangedPull.RoamShootingRadius(10)+.0001,
            "A retained or replacement roaming waypoint escaped the active target's shooting range.");
        Require(rangeSafe!=oldRoam,"A retained waypoint survived after the active target's safe shooting disk moved away.");
        pull.BeginGathering(2);
        Require(pull.RoamWaypoint==null,"The roaming waypoint survived beyond Tagging.");

        string[] checks=["target HP decrease records pull attempt","cooldown activation records blocked pull attempt","attempts do not prove nearby arrival","three dimensional nearby filter",
            "configured HP emergency stops pack growth","three distinct submitted tags","selection does not mark attempts","pull-count gate","tag timeout",
            "partial timed pull remains gathering","single nearby does not clear","gathering has no clock outcome","pressure clears early","dead tags reopen replacement pulling",
            "identity reuse isolation","ambiguous ID rejection","full-health protected bounded selection","stable nearest selection",
            "engaged pack movement remains bounded to tagging and hunt radius","explicit reset",
            "five within two units begin clearing","one to four keep clearing","densest melee sector wins over closest lone target","zero nearby resumes pulling","nearby health and distance exclusions","five distant tags ignore clocks",
            "heading-relative travel keys","bounded retained roaming waypoint","shooting range margin and retained waypoint invalidation","roaming stops after tagging"];
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"ranged-pull-checks.json"),
            JsonSerializer.Serialize(new {Passed=true,Checks=checks},new JsonSerializerOptions {WriteIndented=true}));
    }
}
