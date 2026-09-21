namespace PoteHunter;

public enum HealingRestAction { Wait, Sit, Stand, Complete, Interrupted }
public sealed record HealingRestDecision(HealingRestAction Action,string Reason);

public sealed class HealingRest
{
    public const long QuietMilliseconds=3000;
    Health lastHealth;
    long lastObservationAt;
    public long LastDamageAt { get; private set; }
    public bool Interrupted { get; private set; }
    public bool RestStarted { get; private set; }

    public HealingRest(Health initial,long now)
    {
        ValidateHealth(initial);
        lastHealth=initial;lastObservationAt=now;LastDamageAt=now;
    }

    public static bool MissingHealingItem(HotbarSnapshot bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        // Cooldown and lock describe availability, not missing supplies.
        return !bar.Slots.Any(RecoveryItems.Recognized);
    }

    public static bool ShouldTrigger(Health health,decimal threshold)
    {
        ValidateHealth(health);
        if(threshold<0 || threshold>100)throw new ArgumentOutOfRangeException(nameof(threshold));
        return health.Current<health.Maximum && health.Current*100m<=health.Maximum*threshold;
    }

    public static bool MayStart(Health health,HotbarSnapshot bar,bool hasEngaged,bool pendingReturn,
        bool encounterActive,int pendingLoot,bool attackInProgress,bool lootGuardActive,decimal threshold)
    {
        ValidateHealth(health);
        return ShouldTrigger(health,threshold) && MissingHealingItem(bar) && !hasEngaged && !pendingReturn &&
            !encounterActive && pendingLoot==0 && !attackInProgress && !lootGuardActive;
    }

    // Sit/Stand request a verified posture transition, not a C key edge. The
    // caller uses its existing RestToggle/EnsurePosture for one C per transition.
    public HealingRestDecision Evaluate(Health health,RestReading reading,bool hasEngaged,bool incomingDamage,long now)
    {
        ValidateHealth(health);ArgumentNullException.ThrowIfNull(reading);
        if(reading.Posture==RestPosture.Unknown)throw new InvalidOperationException("Rest recovery cannot verify the character's posture.");
        if(now<lastObservationAt)throw new InvalidOperationException("Rest recovery observations arrived out of order.");
        bool damaged=incomingDamage || health.Current<lastHealth.Current;
        if(damaged)LastDamageAt=now;
        if(damaged || hasEngaged)Interrupted=true;
        lastHealth=health;lastObservationAt=now;
        if(Interrupted)
            return reading.Posture==RestPosture.Standing ?
                new(HealingRestAction.Interrupted,"Rest interrupted by combat or incoming damage; standing confirmed.") :
                new(HealingRestAction.Stand,"Combat or damage interrupted rest; stand before continuing.");
        if(health.Current>=health.Maximum)
            return reading.Posture==RestPosture.Standing ?
                new(HealingRestAction.Complete,"Full health and standing confirmed.") :
                new(HealingRestAction.Stand,"Full health reached; stand before engaging again.");
        if(reading.Posture==RestPosture.Resting)
        {
            RestStarted=true;
            return new(HealingRestAction.Wait,"Resting until health is full.");
        }
        if(reading.Posture is RestPosture.SittingDown or RestPosture.StandingUp)
            return new(HealingRestAction.Wait,"Waiting for the posture animation to finish.");
        if(now-LastDamageAt<QuietMilliseconds)
            return new(HealingRestAction.Wait,"Checking for incoming damage before sitting.");
        RestStarted=true;
        return new(HealingRestAction.Sit,"No healing item is slotted; sit and recover to full health.");
    }

    static void ValidateHealth(Health health)
    {
        if(!health.Known)throw new InvalidOperationException("Player HP is unavailable; rest recovery cannot be checked.");
        if(health.Dead)throw new InvalidOperationException("The character died; rest recovery cannot continue.");
    }

    public static void SelfTest()
    {
        var empty=new HotbarSnapshot(0,[]);var low=new Health(75,100);
        var potion=new HotbarSlot("8",SlotKind.Item,3204,"Fungus Recovery Potion",12000,5000,true,1000,"Potion","Restores health points.");
        var cooling=new HotbarSnapshot(0,[potion]);
        if(!MissingHealingItem(empty) || MissingHealingItem(cooling) || RecoveryItems.Choose(cooling,0)!=null ||
            MayStart(low,cooling,false,false,false,0,false,false,75))
            throw new Exception("A supported healing item on cooldown or locked was treated as missing supplies.");
        if(!MayStart(low,empty,false,false,false,0,false,false,75) || MayStart(new(99,100),empty,false,false,false,0,false,false,75) ||
            MayStart(low,empty,true,false,false,0,false,false,75) || MayStart(low,empty,false,true,false,0,false,false,75) ||
            MayStart(low,empty,false,false,true,0,false,false,75) || MayStart(low,empty,false,false,false,1,false,false,75) ||
            MayStart(low,empty,false,false,false,0,true,false,75) || MayStart(low,empty,false,false,false,0,false,true,75) ||
            MayStart(new(100,100),empty,false,false,false,0,false,false,100))
            throw new Exception("Rest started during an engagement, return or pickup, or failed to pause an unattacked approach.");
        // Deferred low-health recovery is reconsidered after a fight or return.
        // Passive regeneration above the trigger cancels an unstarted rest.
        if(!ShouldTrigger(new(740,1000),75) || !ShouldTrigger(new(750,1000),75) || ShouldTrigger(new(751,1000),75) ||
            ShouldTrigger(new(99,100),75) || !ShouldTrigger(new(80,100),85) || ShouldTrigger(new(80,100),75) ||
            MayStart(new(74,100),empty,true,true,false,0,true,false,75) ||
            MayStart(new(76,100),empty,false,false,false,0,false,false,75))
            throw new Exception("Rest ignored the automatic healing threshold or retained a stale deferred request after HP recovered.");
        var standing=new RestReading(RestPosture.Standing);var sitting=new RestReading(RestPosture.SittingDown);
        var resting=new RestReading(RestPosture.Resting);var rising=new RestReading(RestPosture.StandingUp);
        var rest=new HealingRest(low,0);
        if(rest.Evaluate(low,standing,false,false,0).Action!=HealingRestAction.Wait ||
            rest.Evaluate(low,standing,false,false,2999).Action!=HealingRestAction.Wait ||
            rest.Evaluate(low,standing,false,false,3000).Action!=HealingRestAction.Sit)
            throw new Exception("Rest did not wait for a quiet observation period before requesting C.");
        var enter=new RestToggle(true,3000);
        if(enter.Next(standing,3000)!=RestCommand.Toggle || enter.Next(standing,3100)!=RestCommand.Wait ||
            rest.Evaluate(low,sitting,false,false,3500).Action!=HealingRestAction.Wait || enter.Next(sitting,3500)!=RestCommand.Wait ||
            enter.Next(resting,4000)!=RestCommand.Complete)
            throw new Exception("Rest entry repeated C or ignored the sitting transition.");
        if(rest.Evaluate(low,resting,false,false,4000).Action!=HealingRestAction.Wait ||
            rest.Evaluate(new(76,100),resting,false,false,4500).Action!=HealingRestAction.Wait ||
            rest.Evaluate(new(99,100),resting,false,false,5000).Action!=HealingRestAction.Wait ||
            rest.Evaluate(new(100,100),resting,false,false,6000).Action!=HealingRestAction.Stand ||
            rest.Evaluate(new(100,100),rising,false,false,6100).Action!=HealingRestAction.Stand ||
            rest.Evaluate(new(100,100),standing,false,false,7000).Action!=HealingRestAction.Complete)
            throw new Exception("Rest resumed hunting below full health or before standing was confirmed.");

        var interrupted=new HealingRest(low,0);interrupted.Evaluate(low,standing,false,false,3000);
        if(interrupted.Evaluate(new(74,100),sitting,false,false,3500).Action!=HealingRestAction.Stand || interrupted.LastDamageAt!=3500)
            throw new Exception("Damage during the sitting transition did not request standing.");
        var abortSit=new RestToggle(false,3500);
        if(abortSit.Next(sitting,3500)!=RestCommand.Wait || abortSit.Next(resting,4000)!=RestCommand.Toggle ||
            abortSit.Next(rising,4100)!=RestCommand.Wait || abortSit.Next(standing,5000)!=RestCommand.Complete ||
            interrupted.Evaluate(new(74,100),standing,false,false,5000).Action!=HealingRestAction.Interrupted ||
            interrupted.Evaluate(new(100,100),standing,false,false,9000).Action!=HealingRestAction.Interrupted)
            throw new Exception("Interrupted rest blindly toggled C, sat again, or resumed fresh hunting.");
        var engaged=new HealingRest(low,0);
        if(engaged.Evaluate(low,standing,true,false,3000).Action!=HealingRestAction.Interrupted)
            throw new Exception("Rest requested sitting while engaged.");
        var damageSignal=new HealingRest(low,0);
        if(damageSignal.Evaluate(low,resting,false,true,3000).Action!=HealingRestAction.Stand)
            throw new Exception("Explicit incoming damage was ignored when healing concealed the net HP loss.");
        foreach(var invalid in new[]{new Health(0,0),new Health(0,100)})
        {
            bool rejected=false;try{_=new HealingRest(invalid,0);}catch(InvalidOperationException){rejected=true;}
            if(!rejected)throw new Exception("Rest accepted unknown or dead player health.");
            rejected=false;try{new HealingRest(low,0).Evaluate(invalid,standing,false,false,3000);}catch(InvalidOperationException){rejected=true;}
            if(!rejected)throw new Exception("Rest continued after player health became unavailable or dead.");
        }
        bool unknownRejected=false;
        try{new HealingRest(low,0).Evaluate(low,new RestReading(RestPosture.Unknown),false,false,3000);}catch(InvalidOperationException){unknownRejected=true;}
        if(!unknownRejected)throw new Exception("An unknown posture permitted an unverified C transition.");
    }
}
