namespace PoteHunter;

public static class GroupHealerPolicy
{
    public static GroupDecision Follow(PartySnapshot party,string tankName,Entity self,IReadOnlyList<Entity> entities,
        IReadOnlyDictionary<uint,Health> health,double distance,double limit)
    {
        if(!double.IsFinite(distance)||distance<2||!double.IsFinite(limit)||limit<distance)
            throw new InvalidOperationException("Group healer distances are invalid.");
        if(!party.Available)return new(GroupAction.Wait,"Healbot waiting: party unavailable");
        var members=party.Members.Where(m=>m.Name.Equals(tankName,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(members.Length!=1||members[0].Id==self.Id)return new(GroupAction.Wait,"Healbot waiting: select another current party member as tank");
        var matches=entities.Where(e=>e.Id==members[0].Id&&CombatCourtesy.IsOtherPlayer(e,self.Id)).ToArray();
        if(matches.Length!=1)return new(GroupAction.Wait,"Healbot waiting: tank is not uniquely loaded");
        var tank=matches[0];var hp=health.GetValueOrDefault(tank.Id);
        if(!hp.Known||hp.Dead||!self.Position.Finite||!tank.Position.Finite)
            return new(GroupAction.Wait,"Healbot waiting: tank health or position unavailable",tank);
        double separation=(tank.Position-self.Position).Length;
        if(separation>limit)return new(GroupAction.Wait,"Healbot waiting: tank is beyond the follow limit",tank);
        if(separation<=distance+1)return new(GroupAction.Wait,"Healbot in position; watching party health",tank);
        return new(GroupAction.Follow,"Healbot following "+members[0].Name,tank,null,
            tank.Position+(self.Position-tank.Position)/separation*distance);
    }

    public static bool RecipientValid(HealTarget target,Entity self,PartySnapshot party,IReadOnlyList<Entity> entities,
        IReadOnlyDictionary<uint,Health> health,double range,Keys? expectedKey)
    {
        var live=target.IsSelf(self)?self:entities.FirstOrDefault(e=>e.Id==target.Entity.Id);
        return live!=null && live.Id==target.Entity.Id && live.Address==target.Entity.Address && live.Generation==target.Entity.Generation &&
            live.Position.Finite && self.Position.Finite && (live.Position-self.Position).Length<=range &&
            health.GetValueOrDefault(live.Id) is {Known:true,Dead:false} &&
            party.Available && party.Members.Count(m=>m.Id==live.Id)==1 &&
            HealerPolicy.PartyTargetKey(party,live.Id)==expectedKey;
    }

    public static bool HealingSkill(HotbarSlot slot,bool self)=>slot.Kind==SlotKind.Skill &&
        slot.SkillUse is SkillUseKind.Instance or SkillUseKind.Cast &&
        (slot.SkillTarget is SkillTargetKind.Friend or SkillTargetKind.Party ||
         slot.SkillTarget==SkillTargetKind.FriendExceptSelf && !self ||
         slot.SkillTarget==SkillTargetKind.None && self);
}

public sealed partial class HunterForm
{
    sealed class HealerFollowInterruptedException : Exception;
    sealed class HealerRecipientChangedException : Exception;
    bool healerFollowing,healerCasting;
    Keys? healerRecipientKey;

    async Task<bool> FollowHealbotTank(Options options,CancellationToken token)
    {
        if(!options.GroupMode || movement==null)return false;
        RefreshGuardScene();
        var decision=groupDecision;
        if(decision.Action!=GroupAction.Follow || decision.Tank==null || decision.Destination is not Vec goal)
        {
            movement.StopApproach();message=decision.Status;return false;
        }
        activeHealTarget=null;healerFollowing=true;message=decision.Status;
        activeHuntAnchor=decision.Tank.Position;
        navigation.BeginGoal($"healbot-follow:{decision.Tank.Id}:{decision.Tank.Generation}");
        try
        {
            await NavigateTo(movement,goal,decision.Tank.Position,options,token,boundaryRadius:(double)options.GroupFollowLimit);
            return true;
        }
        catch(HealerFollowInterruptedException){movement.StopApproach();message=groupDecision.Status;return true;}
        catch(RouteUnavailableException ex){movement.StopApproach();message="Healbot follow blocked: "+ex.Message;return true;}
        catch(MovementBlockedException ex){movement.StopApproach();message="Healbot follow blocked: "+ex.Message;return true;}
        finally{healerFollowing=false;}
    }

    void GroupHealerPreflight(Options options)
    {
        RefreshGuardScene();
        var self=world.LocalPlayer();
        if(groupDecision.Action!=GroupAction.Follow)movement?.StopApproach();
        if(healerFollowing)
        {
            if(groupDecision.Action!=GroupAction.Follow || groupDecision.Tank==null)
                throw new HealerFollowInterruptedException();
            activeHuntAnchor=groupDecision.Tank.Position;
            var forward=Movement.FromClientHeading(world.PlayerHeading());
            if(movement?.CanAdvance?.Invoke(self.Position,self.Position+forward*.5)!=true)
                movement?.StopApproach();
        }
        if(healerCasting && activeHealTarget is { } recipient &&
            !GroupHealerPolicy.RecipientValid(recipient,self,currentParty,entities,world.HealthSnapshot(),(double)options.PartyHealRange,healerRecipientKey))
            throw new HealerRecipientChangedException();
    }

    async Task<bool> TryHealbotRecovery(Options options,CancellationToken token)
    {
        var hp=world.TargetHealth(world.LocalPlayer().Id);
        if(!options.AutoHeal || !ShouldHeal(hp,options.HealBelowPercent,Environment.TickCount64,nextHealAt))return false;
        var slot=RecoveryItems.Choose(CheckedHotbar(),recoveryCursor);
        if(slot==null)return false;
        movement?.StopApproach();Input.Release();recoveryCursor++;
        await Input.Key((Keys)slot.Key[0],70,token);
        nextHealAt=Environment.TickCount64+(long)options.HealDelaySeconds*1000;
        if(ManaRecovery.Recognized(slot))manaRecovery.RecordUse(DateTimeOffset.UtcNow);
        message="Healbot recovery: "+slot.Name;
        await Input.Delay(100,token);return true;
    }
}
