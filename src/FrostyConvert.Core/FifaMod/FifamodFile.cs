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
    public byte[] Sha1 { get; init; } = Array.Empty<byte>();

    /// <summary>Absolute file offset of compressed payload.</summary>
    public long FileOffset { get; init; }

    public int CompressedSize { get; init; }
    public int UncompressedSize { get; init; }

    public uint ResType { get; init; }
    public ulong ResRid { get; init; }
    public byte[] ResMeta { get; init; } = Array.Empty<byte>();

    public ulong[] AddedBundleHashes { get; init; } = Array.Empty<ulong>();
    public bool BundleAssignmentOnly { get; init; }

    public byte[]? CompressedData { get; set; }
    public byte[]? Data { get; set; }
    public bool Sha1MatchesCompressed { get; set; }
    public string? DecompressError { get; set; }
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
