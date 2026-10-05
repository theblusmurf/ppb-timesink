namespace PoteHunter;

public sealed partial class HunterForm
{
    readonly DurabilityRepairPolicy durabilityRepairPolicy=new();
    DurabilityReading latestDurability=new(false,"",[],DateTime.UtcNow,"Connect to read equipped durability");
    long nextDurabilityRead;
    readonly CombatPressure durabilityPressure=new();
    string? durabilityHealthContext;
    long durabilityHealthBaselineAt;

    bool DurabilityHealthQuiet(Entity self,Health health)
    {
        long now=Environment.TickCount64;
        string context=world.NavigationContext(self);
        if(context!=durabilityHealthContext || !health.Known || health.Dead)
        {
            durabilityPressure.Reset();durabilityHealthContext=context;durabilityHealthBaselineAt=now;
        }
        if(!health.Known || health.Dead)return false;
        durabilityPressure.Observe(health,now);
        return now-durabilityHealthBaselineAt>=CombatPressure.QuietMilliseconds && !durabilityPressure.RecentDamage(now);
    }

    void RefreshDurabilityStatus()
    {
        if(Environment.TickCount64<nextDurabilityRead)return;
        nextDurabilityRead=Environment.TickCount64+1000;
        latestDurability=world.ReadDurability();
        var self=world.LocalPlayer();_=DurabilityHealthQuiet(self,world.TargetHealth(self.Id));
        durabilityStatus.Text=latestDurability.LowestPercent is decimal lowest
            ? $"Lowest equipped durability: {lowest:0.#}% · repair at {durabilityThreshold.Value:0.#}% or lower."
            : "Durability unavailable: "+latestDurability.Status;
        durabilityRepairPolicy.Observe(latestDurability,activeGuardOptions?.RepairDurabilityPercent??durabilityThreshold.Value,DateTime.UtcNow);
    }

    // Only the owning hunt/support loop invokes this awaited sequence. UI
    // telemetry never starts repair or sends input.
    async Task<bool> TryDurabilityRepair(Options options,CancellationToken token,bool pendingReturn=false)
    {
        if(!options.AutoRepairLowDurability)return false;
        var before=world.ReadDurability();latestDurability=before;
        durabilityRepairPolicy.Observe(before,options.RepairDurabilityPercent,DateTime.UtcNow);
        if(before.LowestPercent is not decimal lowest || lowest>options.RepairDurabilityPercent)return false;
        if(DurabilityRepairDeferred(options,pendingReturn))return false;
        if(!durabilityRepairPolicy.TryBegin(before,options.RepairDurabilityPercent,DateTime.UtcNow))return false;
        var self=world.LocalPlayer();int hp=world.TargetHealth(self.Id).Current;
        void ValidateQuiet()
        {
            var current=world.LocalPlayer();var health=world.TargetHealth(current.Id);
            if(!health.Known || health.Dead || !LocalCharacter.Same(self,current))
                throw new InvalidOperationException("Durability repair stopped: health or character changed.");
            if(health.Current<hp)
                throw new InvalidOperationException("Durability repair stopped: incoming damage while the inventory may be open.");
            hp=health.Current;
            var actors=world.Poll();var healthById=world.HealthSnapshot();
            if(actors.Any(actor=>actor.Monster && actor.Targetable && !healthById.GetValueOrDefault(actor.Id).Dead &&
                (actor.Position-current.Position).Length<=Math.Max(6,(double)options.NearbyEnemyRadius)))
                throw new InvalidOperationException("Durability repair stopped: a nearby threat appeared while the inventory may be open.");
            if(options.GroupMode)
            {
                entities=actors;currentParty=world.Party();RefreshGroupDecision();
                if(groupDecision.Action!=GroupAction.Wait)
                    throw new InvalidOperationException("Durability repair stopped: group movement or combat is now required.");
            }
            if(options.HealerMode && HealerPolicy.Select(world.Party(),current,actors,healthById,(double)options.PartyHealRange,options.PartyHealBelowPercent)!=null)
                throw new InvalidOperationException("Durability repair stopped: a party member now needs healing.");
            var currentEquipment=world.ReadDurability();
            if(!DurabilityRepairPolicy.SameEquipment(before,currentEquipment) || before.Items.Any(item=>
                currentEquipment.Items.FirstOrDefault(current=>current.Slot==item.Slot) is not { } currentItem ||
                currentItem.Maximum!=item.Maximum || currentItem.Current<item.Current))
                throw new InvalidOperationException("Durability repair stopped: equipped items or their reading changed.");
        }
        async Task Verify()
        {
            long deadline=Environment.TickCount64+5000;
            do
            {
                await Input.Delay(0,token);ValidateQuiet();
                latestDurability=world.ReadDurability();
                if(DurabilityRepairPolicy.VerifyImprovement(before,latestDurability,options.RepairDurabilityPercent,DateTime.UtcNow))
                {
                    durabilityRepairPolicy.Observe(latestDurability,options.RepairDurabilityPercent,DateTime.UtcNow);
                    TraceLog.Record("durability repair verified",new{Before=before.LowestPercent,After=latestDurability.LowestPercent,options.RepairDurabilityPercent});
                    return;
                }
                await Input.Delay(200,token);
            }while(Environment.TickCount64<deadline);
            throw new InvalidOperationException("Repair UI finished, but durability did not recover above the threshold. Stopped without another confirmation.");
        }
        TraceLog.Record("low durability repair started",new{Lowest=lowest,options.RepairDurabilityPercent,Items=before.Items});
        await RunRepairAsync(token,ValidateQuiet,Verify);
        StartNearbyPickup(options);
        message=$"Equipment repaired · lowest durability {latestDurability.LowestPercent:0.#}%";
        return true;
    }

    bool DurabilityRepairDeferred(Options options,bool pendingReturn)
    {
        if(!Input.Allowed() || repairInProgress || deathRecovery.Pending || deathRecoveryActive || deathReturnInProgress ||
            returningFromPriority || lootGuardPosition.HasValue || pendingReturn ||
            completionReturnPending || retreatRecovery!=null || healingRestPending ||
            healerFollowing || healerCasting || HasActiveFight() || encounter.Active || encounter.HasUnresolvedNearby ||
            lockedTarget!=null || rangedPull.Active || combatPressure.RecentDamage(Environment.TickCount64))return true;
        var self=world.LocalPlayer();var health=world.HealthSnapshot();
        if(!DurabilityHealthQuiet(self,health.GetValueOrDefault(self.Id)))return true;
        if(entities.Any(actor=>actor.Monster && actor.Targetable && !health.GetValueOrDefault(actor.Id).Dead &&
            (actor.Position-self.Position).Length<=Math.Max(6,(double)options.NearbyEnemyRadius)))return true;
        if(options.GroupMode)
        {
            RefreshGroupDecision();
            if(groupDecision.Action!=GroupAction.Wait)return true;
        }
        if(options.HealerMode && HealerPolicy.Select(currentParty,self,entities,health,(double)options.PartyHealRange,options.PartyHealBelowPercent)!=null)return true;
        return false;
    }
}
