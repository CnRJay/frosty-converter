namespace FrostyConvert.Core.Project;

public sealed class ProjectDocument
{
    public uint Version { get; set; } = FbprojectConstants.FormatVersion;
    public string ProfileName { get; set; } = "";
    public DateTime CreationDate { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedDate { get; set; } = DateTime.UtcNow;
    public uint GameVersion { get; set; }

    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Category { get; set; } = "";
    public string ModVersion { get; set; } = "";
    public string Description { get; set; } = "";
    public byte[]? Icon { get; set; }
    public byte[]?[] Screenshots { get; set; } = new byte[]?[4];

    public List<ProjectBundle> AddedBundles { get; } = new();
    public List<ProjectEbxAdded> AddedEbx { get; } = new();
    public List<ProjectResAdded> AddedRes { get; } = new();
    public List<ProjectChunkAdded> AddedChunks { get; } = new();

    public List<ProjectEbxModified> ModifiedEbx { get; } = new();
    public List<ProjectResModified> ModifiedRes { get; } = new();
    public List<ProjectChunkModified> ModifiedChunks { get; } = new();
    public List<ProjectLegacyEntry> LegacyEntries { get; } = new();
}

public sealed class ProjectBundle
{
    public required string Name { get; init; }
    public string SuperBundleName { get; init; } = "<unknown>";
    public int Type { get; init; } // BundleType enum as int
}

public sealed class ProjectEbxAdded
{
    public required string Name { get; init; }
    public Guid Guid { get; init; }
}

public sealed class ProjectResAdded
{
    public required string Name { get; init; }
    public ulong ResRid { get; init; }
    public uint ResType { get; init; }
    public byte[] ResMeta { get; init; } = new byte[16];
}

public sealed class ProjectChunkAdded
{
    public Guid Id { get; init; }
    public int H32 { get; init; }
}

public sealed class ProjectLinkedAsset
{
    public required string AssetType { get; init; } // ebx | res | chunk
    public string? Name { get; init; }
    public Guid? ChunkId { get; init; }
}

public sealed class ProjectEbxModified
{
    public required string Name { get; init; }
    public List<ProjectLinkedAsset> LinkedAssets { get; init; } = new();
    public List<string> AddedBundleNames { get; init; } = new();
    public bool HasModifiedData { get; init; }
    public bool IsTransientModified { get; init; }
    public string UserData { get; init; } = "";
    /// <summary>True when payload is ModifiedResource (custom handler).</summary>
    public bool IsCustomHandler { get; init; }
    public byte[]? Data { get; init; }
}

public sealed class ProjectResModified
{
    public required string Name { get; init; }
    public List<ProjectLinkedAsset> LinkedAssets { get; init; } = new();
    public List<string> AddedBundleNames { get; init; } = new();
    public bool HasModifiedData { get; init; }
    public byte[]? Sha1 { get; init; }
    public long OriginalSize { get; init; }
    public byte[]? ResMeta { get; init; }
    public string UserData { get; init; } = "";
    public byte[]? Data { get; init; }
}

public sealed class ProjectChunkModified
{
    public Guid Id { get; init; }
    public List<string> AddedBundleNames { get; init; } = new();
    public int FirstMip { get; init; } = -1;
    public int H32 { get; init; }
    public bool HasModifiedData { get; init; }
    public byte[]? Sha1 { get; init; }
    public uint LogicalOffset { get; init; }
    public uint LogicalSize { get; init; }
    public uint RangeStart { get; init; }
    public uint RangeEnd { get; init; }
    public bool AddToChunkBundle { get; init; }
    public string UserData { get; init; } = "";
    public byte[]? Data { get; init; }
}

public sealed class ProjectLegacyEntry
{
    public required string Name { get; init; }
    public List<ProjectLinkedAsset> LinkedAssets { get; init; } = new();
    public Guid ChunkId { get; init; }
    public long Offset { get; init; }
    public long CompressedOffset { get; init; }
    public long CompressedSize { get; init; }
    public long Size { get; init; }
}

public static class FbprojectConstants
{
    public const ulong Magic = 0x00005954534F5246;
    public const uint FormatVersion = 14;
}
