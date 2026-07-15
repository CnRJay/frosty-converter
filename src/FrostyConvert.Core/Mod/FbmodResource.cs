namespace FrostyConvert.Core.Mod;

public sealed class FbmodResource
{
    public required ModResourceType Type { get; init; }
    public int ResourceIndex { get; init; } = -1;
    public string Name { get; init; } = "";
    public byte[]? Sha1 { get; init; }
    public long Size { get; init; }
    public byte Flags { get; init; }
    public int HandlerHash { get; init; }
    public string UserData { get; init; } = "";
    public IReadOnlyList<int> AddedBundleHashes { get; init; } = Array.Empty<int>();

    // Res-specific
    public uint ResType { get; init; }
    public ulong ResRid { get; init; }
    public byte[]? ResMeta { get; init; }

    // Chunk-specific
    public uint RangeStart { get; init; }
    public uint RangeEnd { get; init; }
    public uint LogicalOffset { get; init; }
    public uint LogicalSize { get; init; }
    public int H32 { get; init; }
    /// <summary>MMC CollegeFB27/Madden27 v7 extension (after H32, only when no handler).</summary>
    public long H64 { get; init; }
    public int FirstMip { get; init; }
    /// <summary>Superbundle FNV hashes added by the chunk (MMC mod version &gt; 5).</summary>
    public IReadOnlyList<int> SuperBundlesToAdd { get; init; } = Array.Empty<int>();

    // Bundle-specific
    public int SuperBundleHash { get; init; }

    public bool IsModified => ResourceIndex != -1 && Type is not ModResourceType.Embedded and not ModResourceType.Bundle;
    public bool IsAdded => (Flags & 0x08) != 0;
    public bool ShouldInline => (Flags & 0x01) != 0;
    public bool IsTocChunk => (Flags & 0x02) != 0;
    public bool HasHandler => HandlerHash != 0;

    /// <summary>Payload bytes from the mod data section, if present.</summary>
    public byte[]? Data { get; set; }
}
