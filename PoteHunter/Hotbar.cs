namespace PoteHunter;

public enum SlotKind { Empty, Skill, Item, Unknown }
public enum SkillUseKind { Unknown=-1,None=0,Passive=1,Instance=2,Cast=3,Chant=4,Enchant=5,Item=6,Set=7,Action=8 }
public enum SkillTargetKind { Unknown=-1,None=0,Melee=1,Friend=2,Enemy=3,FriendDead=4,EnemyDead=5,FriendObject=6,EnemyObject=7,Party=8,FriendExceptSelf=9,EnemyLine=10 }
public readonly record struct SkillManaCost(int Base,int Extra);
public record SkillMetadata(SkillUseKind Use,SkillTargetKind Target,string Description,IReadOnlyList<SkillManaCost>? ManaCosts=null);
public record HotbarSlot(string Key, SlotKind Kind, int Id, string Name, uint TotalCooldown, uint RemainingCooldown, bool Locked, int LockRemaining, string Category = "", string Description = "", int RestoresHealth = 0, int RestoresMana = 0,SkillUseKind SkillUse=SkillUseKind.Unknown,SkillTargetKind SkillTarget=SkillTargetKind.Unknown,int? ManaCost=null)
{
    public bool Ready => (Kind is SlotKind.Skill or SlotKind.Item) && RemainingCooldown == 0 && !Locked;
    public bool HasCooldown => TotalCooldown > 0 || RemainingCooldown > 0;
}
public record HotbarSnapshot(int PageBase, IReadOnlyList<HotbarSlot> Slots)
{
    public string Page => PageBase == 0 ? "Z" : "X";
    public HotbarSlot Slot(char key) => Slots.Single(s => s.Key == key.ToString());
}
public static class SkillRotation
{
    public static long RetryDelayMilliseconds(HotbarSlot slot,bool cooldownStarted,decimal configuredSeconds)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if(configuredSeconds<=0 || configuredSeconds>long.MaxValue/1000m)throw new ArgumentOutOfRangeException(nameof(configuredSeconds));
        // A failed cast of a skill with a live cooldown should be tried again
        // soon. Choose still requires the slot to be ready, so this local retry
        // delay never replaces the game's cooldown or locked-slot check.
        return cooldownStarted ? 200 : slot.HasCooldown ? 1000 : (long)(configuredSeconds*1000);
    }

    public static string DetectKeys(HotbarSnapshot bar)=>AvailableKeys("1234567890",bar);
    public static string AvailableKeys(string configured,HotbarSnapshot bar) => new(configured.Where(key=>
        bar.Slots.Any(slot=>slot.Key==key.ToString() && slot.Kind==SlotKind.Skill && slot.SkillUse is SkillUseKind.Unknown or SkillUseKind.Instance or SkillUseKind.Cast)).ToArray());
    public static int Choose(string keys, int cursor, HotbarSnapshot bar, IReadOnlyDictionary<char,long> retryAt, long now,
        Func<HotbarSlot,bool>? eligible=null)
    {
        for (int offset = 0; offset < keys.Length; offset++)
        {
            int index = (cursor + offset) % keys.Length;
            var slot = bar.Slot(keys[index]);
            if (slot.Kind == SlotKind.Skill && slot.Ready && (eligible==null || eligible(slot)) &&
                (!retryAt.TryGetValue(keys[index], out long next) || now >= next)) return index;
        }
        return -1;
    }

    // Ranged weapon skills (e.g. Firing) only make sense at distance: the
    // hunter casts them while closing and switches to the weapon swing once
    // the target is in reach.
    public static bool IsRangedSkill(string name) => !string.IsNullOrEmpty(name) && name.StartsWith("Firing", StringComparison.OrdinalIgnoreCase);
    public static string FiringKeys(string keys,HotbarSnapshot bar) => new(keys.Where(key=>
        bar.Slots.Any(slot=>slot.Key==key.ToString() && slot.Kind==SlotKind.Skill && IsRangedSkill(slot.Name) &&
            slot.SkillUse is SkillUseKind.Instance or SkillUseKind.Cast)).ToArray());
    public static string DetectFiringKeys(HotbarSnapshot bar)=>FiringKeys("1234567890",bar);
    public static int ChooseRanged(string keys, HotbarSnapshot bar, IReadOnlyDictionary<char,long> retryAt, long now)
    {
        for (int index = 0; index < keys.Length; index++)
        {
            var slot = bar.Slot(keys[index]);
            if (IsRangedSkill(slot.Name) && slot.Kind == SlotKind.Skill && slot.Ready && (!retryAt.TryGetValue(keys[index], out long next) || now >= next)) return index;
        }
        return -1;
    }

    public static void RetrySelfTest()
    {
        static void Require(bool condition,string message)
        {
            if(!condition)throw new Exception("Skill retry: "+message);
        }
        var firing=new HotbarSlot("1",SlotKind.Skill,101,"Firing Lv.1",900,0,false,0,SkillUse:SkillUseKind.Instance);
        var other=firing with {Key="2",Id=102,Name="Other skill",TotalCooldown=1500};
        var ready=new HotbarSnapshot(0,[firing,other]);
        var retryAt=new Dictionary<char,long>{['1']=RetryDelayMilliseconds(firing,false,12)};
        Require(retryAt['1']==1000 && Choose("1",0,ready,retryAt,999)==-1 && Choose("1",0,ready,retryAt,1000)==0,
            "failed Firing with a900ms live cooldown waited12000ms or retried before1000ms.");
        Require(Choose("12",0,ready,retryAt,999)==1,
            "one failed cast blocked another ready skill during its retry delay.");
        var cooling=new HotbarSnapshot(0,[firing with {RemainingCooldown=300},other]);
        var locked=new HotbarSnapshot(0,[firing with {Locked=true,LockRemaining=300},other]);
        Require(Choose("1",0,cooling,retryAt,1000)==-1 && Choose("12",0,cooling,retryAt,1000)==1 &&
            Choose("1",0,locked,retryAt,1000)==-1 && Choose("12",0,locked,retryAt,1000)==1,
            "the short retry bypassed a live cooldown or lock, or prevented another skill from being chosen.");
        Require(Choose("12",1,ready,retryAt,1000)==1 && Choose("12",0,ready,retryAt,1000)==0,
            "short retries broke cursor rotation or eligible-key selection.");
        retryAt['1']=500+RetryDelayMilliseconds(firing,true,12);
        Require(retryAt['1']==700 && Choose("1",0,ready,retryAt,699)==-1 && Choose("1",0,ready,retryAt,700)==0 &&
            Choose("1",0,cooling,retryAt,700)==-1 && Choose("1",0,locked,retryAt,700)==-1,
            "confirmed activation lost its200ms local delay or bypassed live slot readiness.");
        var noCooldown=firing with {TotalCooldown=0,RemainingCooldown=0};
        Require(RetryDelayMilliseconds(noCooldown,false,12)==12000 && RetryDelayMilliseconds(noCooldown,false,1.25m)==1250 &&
            RetryDelayMilliseconds(noCooldown,true,12)==200 && RetryDelayMilliseconds(noCooldown with {RemainingCooldown=50},false,12)==1000,
            "no-cooldown skills lost their configured fallback or observed live cooldowns were ignored.");
        var noCooldownBar=new HotbarSnapshot(0,[noCooldown]);
        retryAt['1']=RetryDelayMilliseconds(noCooldown,false,12);
        Require(Choose("1",0,noCooldownBar,retryAt,11999)==-1 && Choose("1",0,noCooldownBar,retryAt,12000)==0,
            "a no-cooldown skill no longer honors the user's configured retry interval.");
        Require(IsRangedSkill("Firing Lv.1") && IsRangedSkill("firing") && !IsRangedSkill("Meditation Lv.2") && !IsRangedSkill(""),
            "ranged skill name matching failed.");
        Require(ChooseRanged("12",ready,new Dictionary<char,long>(),1000)==0 &&
            ChooseRanged("12",ready,new Dictionary<char,long>{['1']=2000},1000)==-1 &&
            ChooseRanged("12",cooling,new Dictionary<char,long>(),1000)==-1 &&
            ChooseRanged("2",ready,new Dictionary<char,long>(),1000)==-1,
            "ranged selection must pick the ready Firing slot only, honoring retry delays and cooldowns.");
        Require(FiringKeys("12",ready)=="1" && FiringKeys("2",ready)=="" && DetectFiringKeys(ready)=="1" &&
            FiringKeys("",ready)=="" && Choose("",0,ready,new Dictionary<char,long>(),1000,slot=>!IsRangedSkill(slot.Name))==-1 &&
            Choose("12",0,ready,new Dictionary<char,long>(),1000,slot=>!IsRangedSkill(slot.Name))==1 &&
            Choose("1",0,ready,new Dictionary<char,long>(),1000,slot=>!IsRangedSkill(slot.Name))==-1,
            "dedicated pull detection missed slotted Firing, or clearing used Firing/changed other offensive key indices.");
        foreach(var kind in new[]{SlotKind.Empty,SlotKind.Item,SlotKind.Unknown})
            Require(Choose("1",0,new(0,[firing with {Kind=kind}]),new Dictionary<char,long>(),1000)==-1,
                "retry selection accepted a non-skill slot.");
        foreach(decimal invalid in new[]{0m,-1m,decimal.MaxValue})
        {
            bool rejected=false;try{RetryDelayMilliseconds(firing,false,invalid);}catch(ArgumentOutOfRangeException){rejected=true;}
            Require(rejected,"an invalid configured interval was accepted.");
        }
    }
}
public static class RecoveryItems
{
    // Some client item descriptions specify the effect but omit its amount (e.g. Fungus Recovery Potion).
    // Preserve an unknown amount as zero; recognize the explicit effect separately.
    public static bool Recognized(HotbarSlot slot) => slot.Kind == SlotKind.Item &&
        (slot.RestoresHealth > 0 || PotionItems.Recovery(slot.Category,slot.Description).Health);
    public static (int Health, int Mana) Parse(string category, string description)
    {
        var effect=PotionItems.Recovery(category,description);
        return (effect.HealthAmount,effect.ManaAmount);
    }
    public static HotbarSlot? Choose(HotbarSnapshot bar, int cursor)
    {
        var items = bar.Slots.Where(Recognized).ToArray();
        for (int i = 0; i < items.Length; i++)
        {
            var slot = items[(cursor + i) % items.Length];
            if (slot.Ready) return slot;
        }
        return null;
    }
}
public record ItemDetails(int Id, string Name, string Category, string Description, int RestoresHealth, int RestoresMana);
