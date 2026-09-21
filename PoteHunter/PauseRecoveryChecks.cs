namespace PoteHunter;

public static class PauseRecoveryChecks
{
    public static void Run()
    {
        static void Require(bool condition,string message)
        {
            if(!condition)throw new Exception("Pause recovery: "+message);
        }
        static Entity Mimic(uint life,double distance)=>new(10000+life,0x80000000u | life<<16 | 5971u,"",new(distance,0),0,
            Generation:1,Model:"MON_mimic.GCMDS");
        Vec position=new(0,0);
        Entity[] mimics=[Mimic(1,.97),Mimic(2,1.72),Mimic(3,2.29)];
        var health=mimics.ToDictionary(entity=>entity.Id,_=>new Health(1000,1000));
        var baseline=new Health(5482,7710);var damaged=new Health(5413,7710);
        var pressure=new CombatPressure();var encounter=new Encounter();
        var emptyBar=new HotbarSnapshot(0,[]);
        var standing=new RestReading(RestPosture.Standing);var resting=new RestReading(RestPosture.Resting);
        var rising=new RestReading(RestPosture.StandingUp);
        Require(!pressure.Observe(baseline,0) && !pressure.RecentDamage(0),"recorded low HP was mistaken for a fresh attack.");
        Require(HealingRest.MayStart(baseline,emptyBar,false,false,false,0,false,false,75),"recorded baseline could not start quiet recovery.");
        var recovery=new HealingRest(baseline,0);
        Require(recovery.Evaluate(baseline,resting,false,false,3000).Action==HealingRestAction.Wait,
            "an undisturbed active rest did not keep recovering.");

        // The HP values and nearby-distance range reproduce the recorded pause.
        // Full-health Mimics intentionally have no previous outgoing-hit history.
        Require(pressure.Observe(damaged,3100),"the recorded 5482-to-5413 HP drop was not detected.");
        var defender=CombatPressure.ChooseDefense(mimics,health,position,5,(_,_)=>true) ??
            throw new Exception("Pause recovery: nearby fresh Mimics produced no defensive candidate.");
        Require(defender.Id==mimics[0].Id,"nearby fresh Mimics did not produce the nearest defensive candidate.");
        encounter.MarkDefensive(defender,health[defender.Id]);
        Require(encounter.HasEngaged && !encounter.MayHaveReceivedOurDamage(defender),
            "defensive enrollment failed or fabricated outgoing damage ownership.");
        Require(recovery.Evaluate(damaged,resting,encounter.HasEngaged,false,3100).Action==HealingRestAction.Stand &&
            recovery.Evaluate(damaged,rising,encounter.HasEngaged,false,3200).Action==HealingRestAction.Stand &&
            recovery.Evaluate(damaged,standing,encounter.HasEngaged,false,3300).Action==HealingRestAction.Interrupted,
            "incoming damage did not stand before leaving the recovery state.");
        int freshSelections=0;
        Entity? Fresh(){freshSelections++;return mimics[2];}
        Require(GamekeeperPriority.ChooseFirst(encounter,null,Fresh)?.Id==defender.Id && freshSelections==0,
            "interrupted recovery chose a fresh target before its defensive fight.");

        var previous=new Entity(90000,0x80000042,"Lv. 1 Gorgon",new(3,0),0,Generation:1);
        health[previous.Id]=new(500,1000);
        encounter.MarkAttack(previous,health[previous.Id]);
        encounter.MarkDefensive(defender,health[defender.Id]);
        Require(encounter.EngagedCount==2 && encounter.IsEngaged(previous) && encounter.IsEngaged(defender),
            "adding a defensive fight discarded a previous unfinished engagement.");
        var keeper=new Entity(91000,0x80001752,"Gamekeeper",new(4,0),0,Generation:1,Model:"MON_SnowGun2.GCMDS");
        health[keeper.Id]=new(1000,1000);
        var priority=GamekeeperPriority.Choose([keeper],health,position,position,20,60,true);
        Require(GamekeeperPriority.ChooseFirst(encounter,priority,Fresh)?.Id==keeper.Id && freshSelections==0 && encounter.EngagedCount==2,
            "defense blocked Gamekeeper or erased the fights to resume afterward.");
        Require(!HealingRest.MayStart(damaged,emptyBar,encounter.HasEngaged,false,encounter.Active,0,false,false,75),
            "low health restarted resting during unfinished engagements.");

        health[defender.Id]=new(0,1000);
        encounter.Observe([previous,..mimics],health,position,5,(_,_)=>false,(_,_)=>true,clearNearby:false);
        Require(!encounter.IsEngaged(defender) && encounter.EngagedCount==1 &&
            GamekeeperPriority.ChooseFirst(encounter,null,Fresh)?.Id==previous.Id && freshSelections==0,
            "one confirmed death lost the other engagement or admitted fresh hunting.");
        health[previous.Id]=new(0,1000);
        encounter.Observe([previous,..mimics],health,position,5,(_,_)=>false,(_,_)=>true,clearNearby:false);
        Require(!encounter.HasEngaged,"confirmed defensive and previous deaths failed to clear the combat queue.");
        encounter.Reset();
        Require(pressure.RecentDamage(6099) && !pressure.RecentDamage(6100),"the three-second damage quiet period was not exact.");
        Require(HealingRest.MayStart(damaged,emptyBar,false,false,encounter.Active,0,false,false,75),
            "recovery could not resume after defensive combat finished.");
        var resumed=new HealingRest(damaged,6100);
        Require(resumed.Evaluate(damaged,standing,false,false,6100).Action==HealingRestAction.Wait &&
            resumed.Evaluate(damaged,standing,false,false,9099).Action==HealingRestAction.Wait &&
            resumed.Evaluate(damaged,standing,false,false,9100).Action==HealingRestAction.Sit,
            "new recovery skipped its quiet observation before sitting.");
        Require(resumed.Evaluate(new(6000,7710),resting,false,false,9200).Action==HealingRestAction.Wait &&
            resumed.Evaluate(new(7709,7710),resting,false,false,9300).Action==HealingRestAction.Wait &&
            resumed.Evaluate(new(7710,7710),resting,false,false,9400).Action==HealingRestAction.Stand &&
            resumed.Evaluate(new(7710,7710),standing,false,false,9500).Action==HealingRestAction.Complete,
            "resumed recovery finished at the healing threshold instead of full health and confirmed standing.");

        var unknown=Mimic(4,.1);var dead=Mimic(5,.2);var outside=Mimic(6,5.01);
        var prop=new Entity(92000,0x8000175F,"Lv. 1 Box",new(.1,0),0,Generation:1,Model:"NPC_AG_Container.gcmds");
        var npc=new Entity(93000,0x40000001,"Guard",new(.1,0),0,Model:"NPC_M13.GCMDS");
        var player=new Entity(94000,7,"Other player",new(.1,0),0,Model:"PC_MAN.GCMDS");
        var replacement=mimics[1] with {Address=mimics[1].Address+1,Generation=2};
        health[dead.Id]=new(0,1000);health[outside.Id]=health[prop.Id]=health[npc.Id]=health[player.Id]=new(1000,1000);
        Entity[] excluded=[unknown,dead,outside,prop,npc,player,mimics[1],replacement];
        Require(CombatPressure.ChooseDefense(excluded,health,position,5,(_,_)=>true)==null,
            "defense selected an unknown, dead, distant, non-monster, prop, or ambiguous identity.");
        Require(CombatPressure.ChooseDefense([mimics[2]],health,position,5,(_,_)=>false)==null,
            "defense ignored an explicit protection refusal.");
        var courtesy=new CombatCourtesy();
        Require(CombatPressure.ChooseDefense([mimics[2],player],health,position,5,
            (entity,hp)=>courtesy.Blocked(entity,hp,[mimics[2],player],123,true,5)==null)==null,
            "defense ignored a nearby-player protection.");
        var noCandidateRest=new HealingRest(damaged,10000);
        var nextDamage=new Health(5350,7710);
        Require(pressure.Observe(nextDamage,10100) &&
            noCandidateRest.Evaluate(nextDamage,resting,false,false,10100).Action==HealingRestAction.Stand &&
            noCandidateRest.Evaluate(nextDamage,standing,false,false,10200).Action==HealingRestAction.Interrupted,
            "damage without a permitted candidate failed to leave sitting for caller-directed reacquisition.");
    }
}
