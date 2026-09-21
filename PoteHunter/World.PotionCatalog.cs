namespace PoteHunter;

public sealed partial class World
{
    public IReadOnlyList<ItemDetails> PotionCatalog()
    {
        long table = moduleBase + profile.ItemDefinitions;
        uint count = Pointer(table), records = Pointer(table + 4);
        if (count is 0 or >50000 || records < 0x10000 || (ulong)records + count * 0x26cUL > uint.MaxValue)
            throw new InvalidOperationException("Item catalog is unavailable.");
        var result = new List<ItemDetails>();
        for (uint i = 0; i < count; i++)
        {
            var bytes = PoteMemoryProbe.Native.Read(handle!, (nint)(records + i * 0x26cL), 2);
            var item = DescribeItem(BitConverter.ToUInt16(bytes));
            if (item.Category.Contains("Potion", StringComparison.OrdinalIgnoreCase) ||
                item.Category.Equals("Food", StringComparison.OrdinalIgnoreCase)) result.Add(item);
        }
        return result;
    }
}
