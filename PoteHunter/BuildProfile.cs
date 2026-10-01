namespace PoteHunter;

public sealed record BuildProfile(string Sha256, uint ImageSize, uint CreatureVtable, uint Scene, uint LocalActor, uint CreatureManager, uint UidDataManager, uint MonsterDefinitions, uint ItemDefinitions, uint SkillDefinitions)
{
    public SceneLayout Layout { get; init; } = SceneLayout.Legacy;
    public static readonly BuildProfile Original = new(
        "13497aa1b4db51815336072d6c632e6c220a37035c1012a07e8d0d19aa4e39b5", 0x14c52000,
        0x42dfd4, 0x49b8e4, 0x49d070, 0x14a4ba5c, 0x14a4ba60, 0x49a9dc, 0x14a58b70, 0x49a978);
    public static readonly BuildProfile September10Update = new(
        "2a68fb8b552ef78fb7d63a8a489d9146a17b92ffabe97aa781b5bb58505c68a8", 0x14c4f000,
        0x42cfa4, 0x499754, 0x49aee0, 0x14a498cc, 0x14a498d0, 0x49884c, 0x14a569e0, 0x4987e8);
    public static BuildProfile Resolve(string hash)
    {
        if(TryResolve(hash,out var profile)) return profile;
        throw new InvalidOperationException("This client build has not been mapped yet. Automation is disabled until its memory layout is verified.");
    }
    public static bool TryResolve(string hash,out BuildProfile profile)
    {
        if(hash.Equals(Original.Sha256,StringComparison.OrdinalIgnoreCase)) {profile=Original;return true;}
        if(hash.Equals(September10Update.Sha256,StringComparison.OrdinalIgnoreCase)) {profile=September10Update;return true;}
        profile=null!; return false;
    }
}

// Chosen only after the complete matching code catalog passes; never infer
// shifted fields from a file version or from a plausible live pointer.
public sealed record SceneLayout(uint HotbarPage, uint HotbarSlots, uint SelectedSlot,
    uint GroundItems, uint GroundCount, int SlotKind, int CooldownTotal,
    int CooldownRemaining, int Locked, int LockRemaining, uint Effects = 0x5e8)
{
    public static readonly SceneLayout Legacy = new(0x3168, 0x13a8, 0x13a4,
        0x3134, 0x3138, 0x1c, 0x24, 0x28, 0x19c, 0x1a0);
    public static readonly SceneLayout September30 = new(0x3190, 0x13d0, 0x13cc,
        0x315c, 0x3160, 0x20, 0x28, 0x2c, 0x1a0, 0x1a4, 0x610);
}
