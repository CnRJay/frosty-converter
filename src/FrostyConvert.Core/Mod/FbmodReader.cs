using FrostyConvert.Core.IO;

namespace FrostyConvert.Core.Mod;

public sealed class FbmodReaderException : Exception
{
    public FbmodReaderException(string message) : base(message) { }
    public FbmodReaderException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Standalone parser for Frosty <c>.fbmod</c> files (binary format v1–v5).
/// Does not require Frosty assemblies or game data.
/// </summary>
public static class FbmodReader
{
    /// <summary>
    /// Parse an .fbmod file and optionally load resource payloads into each <see cref="FbmodResource.Data"/>.
    /// </summary>
    public static FbmodFile Read(string path, bool loadResourceData = true)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Mod file not found.", path);

        using var fs = File.OpenRead(path);
        return Read(fs, path, loadResourceData);
    }

    public static FbmodFile Read(Stream stream, string pathForContext, bool loadResourceData = true)
    {
        if (!stream.CanSeek)
            throw new ArgumentException("Stream must be seekable.", nameof(stream));

        using var reader = new EndianBinaryReader(stream, leaveOpen: true);
        long start = stream.Position;
        ulong magic = reader.ReadUInt64();

        if (magic != FbmodConstants.BinaryMagic)
        {
            // Legacy DbObject format starts with a different structure (often a DbObject blob).
            // We detect "not binary" rather than fully parsing legacy here.
            stream.Position = start;
            return new FbmodFile
            {
                Path = pathForContext,
                Format = FbmodFormatKind.Legacy,
                Version = 0,
                ProfileName = "",
                GameVersion = 0,
                Details = null,
                Resources = Array.Empty<FbmodResource>(),
            };
        }

        uint version = reader.ReadUInt32();
        if (version == 0 || version > FbmodConstants.MaxBinaryVersion + 2)
        {
            // Allow slightly newer versions with a soft parse; hard-fail only on absurd values.
            if (version > 100)
                throw new FbmodReaderException($"Unsupported or corrupt mod version: {version}");
        }

        long dataOffset = reader.ReadInt64();
        int dataCount = reader.ReadInt32();

        // Profile: BinaryWriter.Write(string) → 7-bit length + bytes (ASCII profile names).
        string profileName = reader.ReadLengthPrefixedString();
        int gameVersion = reader.ReadInt32();

        var details = ReadDetails(reader, version);

        int resourceCount = reader.ReadInt32();
        if (resourceCount < 0 || resourceCount > 1_000_000)
            throw new FbmodReaderException($"Unreasonable resource count: {resourceCount}");

        var resources = new List<FbmodResource>(resourceCount);
        for (int i = 0; i < resourceCount; i++)
            resources.Add(ReadResource(reader, version, profileName));

        if (loadResourceData && dataCount > 0 && dataOffset > 0 && dataOffset < stream.Length)
        {
            LoadPayloads(reader, resources, dataOffset, dataCount);
        }

        return new FbmodFile
        {
            Path = pathForContext,
            Format = FbmodFormatKind.Binary,
            Version = version,
            ProfileName = profileName,
            GameVersion = gameVersion,
            Details = details,
            Resources = resources,
            DataOffset = dataOffset,
            DataCount = dataCount,
        };
    }

    /// <summary>True if the file starts with binary Frosty mod magic.</summary>
    public static bool IsBinaryFbmod(string path)
    {
        using var fs = File.OpenRead(path);
        if (fs.Length < 8)
            return false;
        using var reader = new EndianBinaryReader(fs);
        return reader.ReadUInt64() == FbmodConstants.BinaryMagic;
    }

    private static FbmodDetails ReadDetails(EndianBinaryReader reader, uint version)
    {
        string title = reader.ReadNullTerminatedString();
        string author = reader.ReadNullTerminatedString();
        string category = reader.ReadNullTerminatedString();
        string ver = reader.ReadNullTerminatedString();
        string description = reader.ReadNullTerminatedString();
        string link = version >= 5 ? reader.ReadNullTerminatedString() : "";

        return new FbmodDetails
        {
            Title = title,
            Author = author,
            Category = category,
            Version = ver,
            Description = description,
            Link = link,
        };
    }

    private static FbmodResource ReadResource(EndianBinaryReader reader, uint version, string profileName)
    {
        var type = (ModResourceType)reader.ReadByte();
        int resourceIndex = reader.ReadInt32();

        string name = "";
        // BaseModResource.Read: name always for v>3; for v<=3 only when resourceIndex != -1
        if ((version <= 3 && resourceIndex != -1) || version > 3)
            name = reader.ReadNullTerminatedString();

        byte[]? sha1 = null;
        long size = 0;
        byte flags = 0;
        int handlerHash = 0;
        string userData = "";
        var bundles = new List<int>();

        if (resourceIndex != -1)
        {
            sha1 = reader.ReadSha1();
            size = reader.ReadInt64();
            flags = reader.ReadByte();
            handlerHash = reader.ReadInt32();
            if (version >= 3)
                userData = reader.ReadNullTerminatedString();
        }

        if (version <= 3 && resourceIndex != -1)
        {
            // existing bundles (ignore) + added bundles
            int existing = reader.ReadInt32();
            for (int i = 0; i < existing; i++)
                reader.ReadInt32();

            int added = reader.ReadInt32();
            for (int i = 0; i < added; i++)
                bundles.Add(reader.ReadInt32());
        }
        else if (version > 3)
        {
            int added = reader.ReadInt32();
            for (int i = 0; i < added; i++)
                bundles.Add(reader.ReadInt32());
        }

        uint resType = 0;
        ulong resRid = 0;
        byte[]? resMeta = null;
        uint rangeStart = 0, rangeEnd = 0, logicalOffset = 0, logicalSize = 0;
        int h32 = 0, firstMip = 0;
        long h64 = 0;
        var superBundles = new List<int>();
        int superBundleHash = 0;

        switch (type)
        {
            case ModResourceType.Res:
                resType = reader.ReadUInt32();
                resRid = reader.ReadUInt64();
                int metaLen = reader.ReadInt32();
                if (metaLen < 0 || metaLen > 1024 * 1024)
                    throw new FbmodReaderException($"Invalid res meta length: {metaLen}");
                resMeta = metaLen > 0 ? reader.ReadBytes(metaLen) : Array.Empty<byte>();
                break;

            case ModResourceType.Chunk:
                // Matches MMC Frosty.Core.Mod.ChunkResource.Read (CollegeFB / Madden forks):
                // range×4, h32, [h64 if v>=7 && no handler && CFB27/Madden27], firstMip,
                // [superBundles count+ints if v>5].
                rangeStart = reader.ReadUInt32();
                rangeEnd = reader.ReadUInt32();
                logicalOffset = reader.ReadUInt32();
                logicalSize = reader.ReadUInt32();
                h32 = reader.ReadInt32();
                if (version >= 7 && handlerHash == 0 && UsesMmcV7ChunkH64(profileName))
                    h64 = reader.ReadInt64();
                firstMip = reader.ReadInt32();
                if (version > 5)
                {
                    int sbCount = reader.ReadInt32();
                    if (sbCount < 0 || sbCount > 100_000)
                        throw new FbmodReaderException($"Invalid superBundle count: {sbCount}");
                    for (int i = 0; i < sbCount; i++)
                        superBundles.Add(reader.ReadInt32());
                }
                break;

            case ModResourceType.Bundle:
                // BundleResource.Read calls base.Read then overwrites name and reads superBundle hash.
                // base already read name (for v>3); Bundle then: name again + superBundleName int.
                name = reader.ReadNullTerminatedString();
                superBundleHash = reader.ReadInt32();
                break;
        }

        return new FbmodResource
        {
            Type = type,
            ResourceIndex = resourceIndex,
            Name = name,
            Sha1 = sha1,
            Size = size,
            Flags = flags,
            HandlerHash = handlerHash,
            UserData = userData,
            AddedBundleHashes = bundles,
            ResType = resType,
            ResRid = resRid,
            ResMeta = resMeta,
            RangeStart = rangeStart,
            RangeEnd = rangeEnd,
            LogicalOffset = logicalOffset,
            LogicalSize = logicalSize,
            H32 = h32,
            H64 = h64,
            FirstMip = firstMip,
            SuperBundlesToAdd = superBundles,
            SuperBundleHash = superBundleHash,
        };
    }

    /// <summary>
    /// MMC only emits/consumes the extra chunk <c>h64</c> field for College Football 27 / Madden 27
    /// profiles (see <c>ChunkResource.Read</c> + <c>ProfilesLibrary.IsLoaded</c>).
    /// </summary>
    private static bool UsesMmcV7ChunkH64(string profileName)
    {
        if (string.IsNullOrEmpty(profileName))
            return false;

        // Mod header profile strings used by MMC forks (observed: "CollegeFB27").
        return profileName.Equals("CollegeFB27", StringComparison.OrdinalIgnoreCase)
            || profileName.Equals("Madden27", StringComparison.OrdinalIgnoreCase)
            || profileName.Equals("CollegeFootball27", StringComparison.OrdinalIgnoreCase);
    }

    private static void LoadPayloads(
        EndianBinaryReader reader,
        List<FbmodResource> resources,
        long dataOffset,
        int dataCount)
    {
        long tableStart = dataOffset;
        long payloadBase = dataOffset + (dataCount * 16L);

        foreach (var resource in resources)
        {
            if (resource.ResourceIndex < 0 || resource.ResourceIndex >= dataCount)
                continue;

            reader.Position = tableStart + (resource.ResourceIndex * 16L);
            long offset = reader.ReadInt64();
            long size = reader.ReadInt64();
            if (size < 0 || size > int.MaxValue)
                continue;

            long abs = payloadBase + offset;
            if (abs < 0 || abs + size > reader.Length)
                continue;

            reader.Position = abs;
            resource.Data = reader.ReadBytes((int)size);
        }
    }
}
