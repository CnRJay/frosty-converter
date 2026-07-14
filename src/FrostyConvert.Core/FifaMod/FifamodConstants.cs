namespace FrostyConvert.Core.FifaMod;

public static class FifamodConstants
{
    /// <summary>ASCII "FETM" as little-endian u32 (official <c>ModReader</c> magic).</summary>
    public const uint MagicLe = 0x4D544546;

    public static readonly byte[] MagicBytes = "FETM"u8.ToArray();

    /// <summary>Legacy frosty-style mod magic used by <c>OldModReader</c>.</summary>
    public const ulong OldMagic = 5498700893333637446UL;
}

[Flags]
public enum FifamodEbxFlags : byte
{
    IsAdded = 1,
    IsDirectlyModified = 2,
    AddToBundleRefTable = 4,
    HasLinkedAssets = 8,
    HasAddedBundles = 0x10,
    BundleAssignmentOnly = 0x20,
    HasParentBundleRef = 0x40,
    BundleRefOnly = 0x80,
}

[Flags]
public enum FifamodResFlags : byte
{
    IsAdded = 1,
    IsDirectlyModified = 2,
    HasMeta = 4,
    HasLinkedAssets = 8,
    HasAddedBundles = 0x10,
    BundleAssignmentOnly = 0x20,
}

[Flags]
public enum FifamodChunkFlags : ushort
{
    IsAdded = 1,
    IsLegacy = 2,
    AddToSuperBundle = 4,
    HasLogicalOffset = 8,
    HasLogicalSize = 0x10,
    HasH32 = 0x20,
    IsLegacyAdded = 0x40,
    HasAddedBundles = 0x80,
    BundleAssignmentOnly = 0x100,
}
