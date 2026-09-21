namespace PoteHunter;

public sealed record LiveBuffDecision(string Key,int SkillId,string Skill,int EffectIndex,SkillUseKind Use,bool Eligible,bool ShouldCast,string Status,ActiveEffect Effect);
public sealed class LiveBuffUpkeep
{
    string? scope;
    sealed class Attempt{public long PendingUntil,RetryAt;public bool ChantUnconfirmed;}
    readonly Dictionary<(string,int,int),Attempt> attempts=new();
    public static bool CanMaintain(HotbarSlot slot,ActiveEffectSnapshot effects)=>slot.Kind==SlotKind.Skill &&
        slot.SkillTarget==SkillTargetKind.Party && slot.SkillUse is SkillUseKind.Instance or SkillUseKind.Cast or SkillUseKind.Chant && effects.Match(slot.Name)!=null;
    public IReadOnlyList<LiveBuffDecision> Evaluate(string identity,HotbarSnapshot bar,ActiveEffectSnapshot effects,bool enabled,long now)
    {
        if(scope!=identity){scope=identity;attempts.Clear();}
        foreach(var key in attempts.Keys.Where(key=>!bar.Slots.Any(s=>s.Key==key.Item1 && s.Id==key.Item2)).ToArray())attempts.Remove(key);
        if(!effects.Available)return [];
        var results=new List<LiveBuffDecision>();
        foreach(var slot in bar.Slots.Where(s=>s.Kind==SlotKind.Skill))
        {
            var effect=effects.Match(slot.Name);if(effect==null)continue;
            var key=(slot.Key,slot.Id,effect.Index);
            if(!attempts.TryGetValue(key,out var attempt))attempts[key]=attempt=new();
            if(effect.Active){attempt.PendingUntil=0;attempt.RetryAt=0;attempt.ChantUnconfirmed=false;}
            bool eligible=CanMaintain(slot,effects),cast=false;
            string status;
            if(effect.Active)status=slot.SkillUse==SkillUseKind.Chant?"On — leave enabled":$"Active — {effect.RemainingSeconds}s remaining";
            else if(!eligible)status="Inactive — tracked; not an automatic party buff";
            else if(!enabled)status="Inactive — upkeep disabled";
            else if(now<attempt.PendingUntil)status="Waiting for active-effect confirmation";
            else if(attempt.ChantUnconfirmed)status="Chant activation not confirmed — check it; restart upkeep to retry";
            else if(now<attempt.RetryAt)status=$"Not confirmed — retry in {Math.Ceiling((attempt.RetryAt-now)/1000.0)}s";
            else if(!slot.Ready)status=$"Inactive — skill {(slot.Locked?"locked":"cooling down")} ({Math.Max(slot.RemainingCooldown,Math.Max(0,slot.LockRemaining))/1000.0:F1}s)";
            else{status=slot.SkillUse==SkillUseKind.Chant?"Off — ready to turn on":"Inactive — ready to cast";cast=true;}
            results.Add(new(slot.Key,slot.Id,slot.Name,effect.Index,slot.SkillUse,eligible,cast,status,effect));
        }
        return results;
    }
    public void RecordAttempt(LiveBuffDecision decision,long now,bool sent=true)
    {
        attempts[(decision.Key,decision.SkillId,decision.EffectIndex)]=new(){PendingUntil=sent?now+2000:0,RetryAt=now+10000,ChantUnconfirmed=sent && decision.Use==SkillUseKind.Chant};
    }
    public void Restart(){scope=null;attempts.Clear();}
    public static void SelfTest()
    {
        static void Require(bool b,string reason){if(!b)throw new Exception(reason);}
        var effect=new ActiveEffect(9,"Encourage","",0,0);
        var effects=new ActiveEffectSnapshot(true,"",[effect,new(8,"Regeneration","",0,0),new(6,"Maintenance Chant","",58,65000)]);
        var slot=new HotbarSlot("4",SlotKind.Skill,33796,"Encourage Lv.1",60000,0,false,0,SkillUse:SkillUseKind.Instance,SkillTarget:SkillTargetKind.Party);
        var regen=slot with{Key="9",Id=35847,Name="Regeneration Lv.1"};var chant=slot with{Key="0",Id=35586,Name="Maintenance Chant Lv.1",SkillUse=SkillUseKind.Chant};
        var bar=new HotbarSnapshot(0,[slot,regen,chant]);var policy=new LiveBuffUpkeep();
        var due=policy.Evaluate("self:page0",bar,effects,true,1000);
        Require(due.Count==3 && due[0].ShouldCast && due[1].ShouldCast && !due[2].ShouldCast,"All party buffs detected; active chant must stay on");
        policy.RecordAttempt(due[0],1000);
        Require(!policy.Evaluate("self:page0",bar,effects,true,4000)[0].ShouldCast,"Failed cast must back off");
        Require(policy.Evaluate("self:page0",bar,effects,true,11000)[0].ShouldCast,"Failed cast must eventually retry");
        var active=effects with{Effects=[effect with{Magnitude=53,RawSeconds=1}]};
        Require(!policy.Evaluate("self:page0",bar,active,true,12000)[0].ShouldCast,"Present effect at zero display seconds must not be recast");
        var cooling=bar with{Slots=[slot with{RemainingCooldown=9000}]};
        Require(!policy.Evaluate("self:page0",cooling,effects,true,13000)[0].ShouldCast,"Missing effect cannot bypass cooldown");
        Require(policy.Evaluate("self:page0",bar,new(false,"unknown",[]),true,13000).Count==0,"Unavailable effect data cannot imply expiry");
        Require(policy.Evaluate("other:page0",bar,effects,true,2000)[0].ShouldCast,"New character must not inherit attempts");
        Require(policy.Evaluate("other:page0",bar with{Slots=[slot with{Key="7"}]},effects,true,2000)[0].Key=="7","Detection follows moved slots");
        Require(!CanMaintain(slot with{SkillTarget=SkillTargetKind.Enemy},effects) && !CanMaintain(slot with{SkillUse=SkillUseKind.Passive},effects),"Enemy/passive skills cannot be maintained as party buffs");
        var off=effects with{Effects=[new(6,"Maintenance Chant","",0,0)]};var cp=new LiveBuffUpkeep();var cd=cp.Evaluate("self",bar,off,true,0)[0];cp.RecordAttempt(cd,0);
        Require(!cp.Evaluate("self",bar,off,true,60000)[0].ShouldCast,"Unconfirmed chant must not repeatedly toggle");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"live-buff-checks.json"),System.Text.Json.JsonSerializer.Serialize(new{Passed=true,Checks=new[]{"all matched party buffs","moved slots","failed activation retry","active effect beats zero displayed time","cooldown remains required","unknown is not absent","identity reset","chant stays on","enemy/passive excluded"}}));
    }
}
