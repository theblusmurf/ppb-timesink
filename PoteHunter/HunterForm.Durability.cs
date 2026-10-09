namespace PoteHunter;

public sealed partial class HunterForm
{
    readonly DurabilityRepairPolicy durabilityRepairPolicy=new();
    DurabilityReading latestDurability=new(false,"",[],DateTime.UtcNow,"Connect to read equipped durability");
    long nextDurabilityRead;

    void RefreshDurabilityStatus()
    {
        if(Environment.TickCount64<nextDurabilityRead)return;
        nextDurabilityRead=Environment.TickCount64+1000;
        latestDurability=world.ReadDurabilityScreening();
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
        var screening=world.ReadDurabilityScreening();latestDurability=screening;
        durabilityRepairPolicy.Observe(screening,options.RepairDurabilityPercent,DateTime.UtcNow);
        if(screening.LowestPercent is not decimal screenedLowest || screenedLowest>options.RepairDurabilityPercent)return false;
        if(DurabilityRepairDeferred(options,pendingReturn))return false;
        // A screen can only suggest an opportunity. Capture a fresh two-pass
        // baseline and renew living/focus/activity guards before claiming it.
        var before=world.ReadDurability();latestDurability=before;
        durabilityRepairPolicy.Observe(before,options.RepairDurabilityPercent,DateTime.UtcNow);
        if(before.LowestPercent is not decimal lowest || lowest>options.RepairDurabilityPercent ||
            DurabilityRepairDeferred(options,pendingReturn))return false;
        if(!durabilityRepairPolicy.TryBegin(before,options.RepairDurabilityPercent,DateTime.UtcNow))return false;
        var self=world.LocalPlayer();
        void ValidateCombatRepair()
        {
            var current=world.LocalPlayer();var health=world.TargetHealth(current.Id);
            if(!health.Known || health.Dead || !LocalCharacter.Same(self,current))
                throw new InvalidOperationException("Durability repair stopped: health or character changed.");
            // Incoming damage and newly engaged mobs are expected during combat.
            // Death, unknown health and changed equipment still stop all UI input.
            var currentEquipment=world.ReadDurability();
            if(!DurabilityRepairPolicy.SameEquipment(before,currentEquipment) || before.Items.Any(item=>
                currentEquipment.Items.FirstOrDefault(current=>current.Slot==item.Slot) is not { } currentItem ||
                currentItem.Maximum!=item.Maximum))
                throw new InvalidOperationException("Durability repair stopped: equipped items or their reading changed.");
        }
        async Task Verify()
        {
            long deadline=Environment.TickCount64+5000;
            do
            {
                await Input.Delay(0,token);ValidateCombatRepair();
                latestDurability=world.ReadDurability();
                if(DurabilityRepairPolicy.VerifyCombatImprovement(before,latestDurability,options.RepairDurabilityPercent,DateTime.UtcNow))
                {
                    durabilityRepairPolicy.Observe(latestDurability,options.RepairDurabilityPercent,DateTime.UtcNow);
                    TraceLog.Record("durability repair verified",new{Before=before.LowestPercent,After=latestDurability.LowestPercent,options.RepairDurabilityPercent});
                    return;
                }
                await Input.Delay(200,token);
            }while(Environment.TickCount64<deadline);
            throw new InvalidOperationException("Repair UI finished, but durability did not recover above the threshold. Stopped without another confirmation.");
        }
        TraceLog.Record("low durability repair started",new{Lowest=lowest,options.RepairDurabilityPercent,Items=before.Items,
            DuringCombat=HasActiveFight()||encounter.Active||lockedTarget!=null,IncomingDamageAllowed=true});
        await RunRepairAsync(token,ValidateCombatRepair,Verify);
        StartNearbyPickup(options);
        message=$"Equipment repaired · lowest durability {latestDurability.LowestPercent:0.#}%";
        return true;
    }

    bool DurabilityRepairDeferred(Options options,bool pendingReturn)
    {
        var self=world.LocalPlayer();var health=world.TargetHealth(self.Id);
        var activity=new DurabilityActivity(Input.Allowed(),health.Known&&!health.Dead,repairInProgress,
            deathRecovery.Pending||deathRecoveryActive||deathReturnInProgress,
            returningFromPriority||lootGuardPosition.HasValue||pendingReturn||retreatRecovery!=null||healerFollowing||navigationInputOwned,
            healerCasting||buffInProgress||rangedTagging||rangedPull.Phase==RangedPullPhase.Tagging);
        return !DurabilityRepairPolicy.CanRepairDuringCombat(activity,out _);
    }
}
