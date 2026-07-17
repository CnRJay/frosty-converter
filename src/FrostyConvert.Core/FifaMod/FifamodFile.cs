namespace FrostyConvert.Core.FifaMod;

public sealed class FifamodDetails
{
    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
    public string Version { get; init; } = "";
    public string Description { get; init; } = "";
    public byte MainCategory { get; init; }
    public byte SubCategory { get; init; }
    public string CustomCategory { get; init; } = "";
    public string SecondCustomCategory { get; init; } = "";
    public string OutOfDateModWebsiteLink { get; init; } = "";
    public string DiscordLink { get; init; } = "";
    public string PatreonLink { get; init; } = "";
    public string TwitterLink { get; init; } = "";
    public string YouTubeLink { get; init; } = "";
    public string InstagramLink { get; init; } = "";
    public string FacebookLink { get; init; } = "";
    public string CustomLink { get; init; } = "";
    public byte[] Icon { get; init; } = Array.Empty<byte>();
    public int ScreenshotCount { get; init; }
    public IReadOnlyList<byte[]> Screenshots { get; init; } = Array.Empty<byte[]>();
}

public sealed class FifamodResource
{
    public required string Name { get; init; }
    public FifamodResourceKind Kind { get; init; }

    public FifamodEbxFlags EbxFlags { get; init; }
    public FifamodResFlags ResFlags { get; init; }
    public FifamodChunkFlags ChunkFlags { get; init; }

    public Guid ChunkId { get; init; }

    /// <summary>For added EBX entries written into .fifaproject (e.g. promoted TextureAsset).</summary>
    public Guid EbxGuid { get; init; }

    /// <summary>EBX type name for added assets (e.g. "TextureAsset").</summary>
    public string? EbxTypeName { get; init; }

    public byte[] Sha1 { get; init; } = Array.Empty<byte>();

    /// <summary>Absolute file offset of compressed payload.</summary>
    public long FileOffset { get; init; }

    public int CompressedSize { get; init; }
    public int UncompressedSize { get; init; }

    public uint ResType { get; init; }
    public ulong ResRid { get; init; }
    public byte[] ResMeta { get; init; } = Array.Empty<byte>();

    // Chunk-only fields (from .fifamod index)
    public int LogicalOffset { get; init; }
    public int LogicalSize { get; init; }
    public ulong H32 { get; init; }
    public uint SuperBundleHash { get; init; }
    public ulong LegacyFileNameHash { get; init; }
    public string? LegacyFileName { get; init; }

    public ulong[] AddedBundleHashes { get; init; } = Array.Empty<ulong>();
    public bool BundleAssignmentOnly { get; init; }

    /// <summary>
    /// Per-EBX Bundle Ref Table registration (shoes/kits/etc.). Written into .fifaproject so FET
    /// re-export rebuilds BRTs; omitting this is a common cause of in-game load failures.
    /// </summary>
    public FifamodBrtAddition? BrtAddition { get; init; }

    public byte[]? CompressedData { get; set; }
    public byte[]? Data { get; set; }
    public bool Sha1MatchesCompressed { get; set; }
    public string? DecompressError { get; set; }

    public bool IsAdded =>
        Kind switch
        {
            FifamodResourceKind.Ebx => EbxFlags.HasFlag(FifamodEbxFlags.IsAdded),
            FifamodResourceKind.Res => ResFlags.HasFlag(FifamodResFlags.IsAdded),
            // .fifamod never sets IsAdded (ModWriter); new legacy files only set IsLegacyAdded.
            // Project load requires IsAdded so FET creates the chunk instead of looking it up.
            FifamodResourceKind.Chunk =>
                ChunkFlags.HasFlag(FifamodChunkFlags.IsAdded)
                || ChunkFlags.HasFlag(FifamodChunkFlags.IsLegacyAdded),
            _ => false,
        };
}

public enum FifamodResourceKind
{
    Ebx,
    Res,
    Chunk,
}

/// <summary>EBX registration into a Bundle Ref Table (from .fifamod index or project EBX flags).</summary>
public sealed class FifamodBrtAddition
{
    public uint BrtNameHash { get; init; }
    public string BundleRefPath { get; init; } = "";
    /// <summary>Null means flag HasParentBundleRef is clear (field absent in stream).</summary>
    public string? ParentBundleRefPath { get; init; }
    public bool BundleRefOnly { get; init; }
}

/// <summary>
/// Trailing collector footer entry on .fifamod (ModWriter). FET regenerates these on export from
/// live legacy state; we preserve them for inspect / diagnostics.
/// </summary>
public sealed class FifamodCollectorEntry
{
    public string CollectorEbxName { get; init; } = "";
    public Guid CollectorChunkId { get; init; }
    public bool IsPatch { get; init; }
    /// <summary>Second uint from ChunkCollectorAssets (super-bundle / collector meta).</summary>
    public uint Meta { get; init; }
}

/// <summary>Trailing BRT name table on .fifamod (hash → asset path).</summary>
public sealed class FifamodBundleRefTableEntry
{
    public uint NameHash { get; init; }
    public string Name { get; init; } = "";
}

/// <summary>Added bundle from .fifamod index (ModWriter.EnumerateAddedBundles).</summary>
public sealed class FifamodAddedBundle
{
    public string Name { get; init; } = "";
    public ulong NameHash { get; init; }
    public uint SuperBundleHash { get; init; }
    /// <summary>Project-only field; mods do not store type — default 0.</summary>
    public byte Type { get; init; }
}

public sealed class FifamodLocaleIniFile
{
    public string Description { get; init; } = "";
    public string Contents { get; init; } = "";
}

public sealed class FifamodInitFsFile
{
    public string Name { get; init; } = "";
    public byte[] Data { get; init; } = Array.Empty<byte>();
}

/// <summary>One key in PlayerLua / PlayerKitLua AllModifications map.</summary>
public sealed class FifamodLuaModEntry
{
    public string Key { get; init; } = "";
    public IReadOnlyList<string> Values { get; init; } = Array.Empty<string>();
}

public sealed class FifamodFile
{
    public required string Path { get; init; }
    public byte ModVersion { get; init; }
    public string GameName { get; init; } = "";
    public uint GameVersion { get; init; }
    public FifamodDetails Details { get; init; } = new();
    public long DataBaseOffset { get; init; }
    public IReadOnlyList<FifamodResource> Resources { get; init; } = Array.Empty<FifamodResource>();
    public IReadOnlyList<FifamodCollectorEntry> Collectors { get; init; } = Array.Empty<FifamodCollectorEntry>();
    public IReadOnlyList<FifamodBundleRefTableEntry> BundleRefTables { get; init; } = Array.Empty<FifamodBundleRefTableEntry>();
    public IReadOnlyList<FifamodAddedBundle> AddedBundles { get; init; } = Array.Empty<FifamodAddedBundle>();
    public IReadOnlyList<FifamodLocaleIniFile> LocaleIniFiles { get; init; } = Array.Empty<FifamodLocaleIniFile>();
    public IReadOnlyList<FifamodInitFsFile> InitFsFiles { get; init; } = Array.Empty<FifamodInitFsFile>();
    public IReadOnlyList<FifamodLuaModEntry> PlayerLuaMods { get; init; } = Array.Empty<FifamodLuaModEntry>();
    public IReadOnlyList<FifamodLuaModEntry> PlayerKitLuaMods { get; init; } = Array.Empty<FifamodLuaModEntry>();
    public int EbxCount { get; init; }
    public int ResCount { get; init; }
    public int ChunkCount { get; init; }
    public List<string> Notes { get; init; } = new();

    /// <summary>
    /// True when header heuristics suggest FMT Pro password-lock or unreadable payload layout.
    /// Not a cryptographic guarantee — only a user-facing signal.
    /// </summary>
    public bool SuspectedPasswordLock { get; init; }
}
