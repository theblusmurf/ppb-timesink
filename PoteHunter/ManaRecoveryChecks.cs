namespace PoteHunter;

internal static class ManaRecoveryChecks
{
    public static void Run()
    {
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        var pure = new HotbarSlot("1", SlotKind.Item, 1, "Mana potion", 0, 0, false, 0, "Potion", "Restores 500 mana points.", 0, 500);
        var combined = new HotbarSlot("2", SlotKind.Item, 2, "Soup", 0, 0, false, 0, "Food", "Restores 300 health and 100 mana points.", 300, 100);
        var amountless = new HotbarSlot("3", SlotKind.Item, 3, "Mana food", 0, 0, false, 0, "Food", "Restores mana points.");
        var combinedAmountless = new HotbarSlot("6", SlotKind.Item, 6, "Combined potion", 0, 0, false, 0, "Potion", "Restores health and mana points.");
        var hpOnly = new HotbarSlot("4", SlotKind.Item, 4, "Bread", 0, 0, false, 0, "Food", "Restores 400 health points.", 400);
        var nonFood = pure with { Category = "NonFood" };
        var potionRecipe = pure with { Category = "Potion recipe" };
        var skill = pure with { Key = "5", Kind = SlotKind.Skill };
        var cooling = combined with { RemainingCooldown = 1 };
        var locked = amountless with { Locked = true };
        var bar = new HotbarSnapshot(0, [pure, combined, amountless, hpOnly, skill]);
        Require(ManaRecovery.Recognized(pure) && ManaRecovery.Recognized(combined) && ManaRecovery.Recognized(amountless) && ManaRecovery.Recognized(combinedAmountless), "Mana items were not recognized.");
        Require(!ManaRecovery.Recognized(hpOnly) && !ManaRecovery.Recognized(skill) && !ManaRecovery.Recognized(nonFood) && !ManaRecovery.Recognized(potionRecipe), "HP-only item, skill, or partial category was accepted for mana recovery.");
        Require(ManaRecovery.ChooseReady(bar)?.Key == "2", "The smallest known ready mana restoration should be selected first.");
        Require(ManaRecovery.ChooseReady(new(0, [cooling, locked, hpOnly])) is null, "Cooldown, lock, or HP-only guards failed.");
        var policy = new ManaRecovery(new(35, TimeSpan.FromSeconds(2)));
        var now = DateTimeOffset.UtcNow;
        Require(!policy.Evaluate(default, bar, now).Needed, "Unknown mana triggered recovery.");
        Require(policy.Evaluate(new(true, 0, 100), bar, now).Ready, "Zero mana did not trigger recovery.");
        Require(!policy.Evaluate(new(true, 36, 100), bar, now).Needed, "Mana above threshold triggered recovery.");
        var due = policy.Evaluate(new(true, 35, 100), bar, now);
        Require(due.Ready && due.Slot?.Key == "2", "Threshold recovery did not select the combined item.");
        policy.RecordUse(now);
        Require(policy.Evaluate(new(true, 20, 100), bar, now.AddSeconds(1)).Slot is null, "Policy cooldown was ignored.");

        var record = new byte[0x75];
        BitConverter.TryWriteBytes(record.AsSpan(0, 4), 5377u);
        BitConverter.TryWriteBytes(record.AsSpan(0x48, 4), 844);
        BitConverter.TryWriteBytes(record.AsSpan(0x50, 2), (ushort)850);
        record[0x52] = 0xFF; record[0x53] = 0xFF; record[0x74] = 1;
        var decoded = World.DecodeManaRecord(record, 5377, 0x48, 0x50);
        Require(decoded.Known && decoded.Current == 844 && decoded.Maximum == 850, "Maximum mana was not read as the proven 16-bit field.");
    }
}
