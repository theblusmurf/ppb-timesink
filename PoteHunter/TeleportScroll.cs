namespace PoteHunter;

// Bind a consumable to its actual item identity and active hotbar page. A
// configured key alone never authorizes using a replacement item or skill.
internal sealed record TeleportScrollSlot(int PageBase,string Key,int ItemId,string Name)
{
    internal static bool IsScroll(HotbarSlot slot)=>slot.Kind==SlotKind.Item && slot.Id>0 &&
        slot.Name.Trim().Equals("Teleport Scroll",StringComparison.OrdinalIgnoreCase);
    internal void Validate()
    {
        if(PageBase is not (0 or 10) || Key is not {Length:1} || !"1234567890".Contains(Key[0]) || ItemId<=0 ||
            !string.Equals(Name?.Trim(),"Teleport Scroll",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Teleport Scroll setup has an invalid item, key or hotbar page. Configure again.");
    }
    internal void RequireReady(HotbarSnapshot bar)
    {
        Validate();var slots=bar.Slots.Where(s=>s.Key==Key).ToArray();
        if(bar.PageBase!=PageBase || slots.Length!=1 || !IsScroll(slots[0]) || slots[0].Id!=ItemId ||
            slots[0].Name!=Name || !slots[0].Ready)
            throw new InvalidOperationException("The captured Teleport Scroll is missing, replaced, locked, cooling down or on another hotbar page. No scroll was used.");
    }
    internal static TeleportScrollSlot Capture(HotbarSnapshot bar)
    {
        var slots=bar.Slots.Where(IsScroll).ToArray();
        if(slots.Length!=1)throw new InvalidOperationException("Place one Teleport Scroll in a single slot on the active hotbar page before configuring scroll travel.");
        var slot=slots[0];var captured=new TeleportScrollSlot(bar.PageBase,slot.Key,slot.Id,slot.Name);
        captured.RequireReady(bar);return captured;
    }
}
