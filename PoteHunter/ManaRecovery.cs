using System.Text.RegularExpressions;

namespace PoteHunter;

public sealed record ManaRecoverySettings(double ThresholdPercent = 35, TimeSpan? Cooldown = null)
{
    public TimeSpan EffectiveCooldown => Cooldown ?? TimeSpan.FromSeconds(2);
    public void Validate()
    {
        if (!double.IsFinite(ThresholdPercent) || ThresholdPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(ThresholdPercent));
        if (EffectiveCooldown < TimeSpan.Zero || EffectiveCooldown > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(Cooldown));
    }
}

public readonly record struct ManaRecoveryDecision(bool Needed, HotbarSlot? Slot, string Reason)
{
    public bool Ready => Needed && Slot is not null;
}

public sealed class ManaRecovery
{
    DateTimeOffset? lastUse;
    public ManaRecoverySettings Settings { get; private set; } = new();

    public ManaRecovery(ManaRecoverySettings? settings = null) => Configure(settings ?? new());

    public void Configure(ManaRecoverySettings settings)
    {
        settings.Validate();
        Settings = settings;
    }

    public void Reset() => lastUse = null;

    public static bool Recognized(HotbarSlot slot) => slot.Kind==SlotKind.Item &&
        PotionItems.Consumable(slot.Category) &&
        (slot.RestoresMana>0 || PotionItems.Recovery(slot.Category,slot.Description).Mana);

    public static HotbarSlot? ChooseReady(HotbarSnapshot bar)
    {
        return bar.Slots.Where(Recognized).Where(slot => slot.Ready)
            .OrderBy(slot => slot.RestoresMana == 0 ? int.MaxValue : slot.RestoresMana)
            .ThenBy(slot => slot.Key, StringComparer.Ordinal).FirstOrDefault();
    }

    public ManaRecoveryDecision Evaluate(ManaReading mana, HotbarSnapshot bar, DateTimeOffset now)
    {
        if (!mana.Known) return new(false, null, "Mana is unknown");
        if (mana.Percent > Settings.ThresholdPercent) return new(false, null, "Mana is above the configured threshold");
        if (lastUse is { } used && now - used < Settings.EffectiveCooldown)
            return new(true, null, "Mana recovery cooldown is active");
        var slot = ChooseReady(bar);
        return slot is null
            ? new(true, null, "No ready mana food or potion is slotted")
            : new(true, slot, "Mana is at or below the configured threshold");
    }

    public void RecordUse(DateTimeOffset now) => lastUse = now;
}
