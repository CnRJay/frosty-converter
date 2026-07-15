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

    public byte[]? CompressedData { get; set; }
    public byte[]? Data { get; set; }
    public bool Sha1MatchesCompressed { get; set; }
    public string? DecompressError { get; set; }

    public bool IsAdded =>
        Kind switch
        {
            FifamodResourceKind.Ebx => EbxFlags.HasFlag(FifamodEbxFlags.IsAdded),
            FifamodResourceKind.Res => ResFlags.HasFlag(FifamodResFlags.IsAdded),
            FifamodResourceKind.Chunk => ChunkFlags.HasFlag(FifamodChunkFlags.IsAdded),
            _ => false,
        };
}

public enum FifamodResourceKind
{
    Ebx,
    Res,
    Chunk,
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
    public int EbxCount { get; init; }
    public int ResCount { get; init; }
    public int ChunkCount { get; init; }
    public List<string> Notes { get; init; } = new();
}
