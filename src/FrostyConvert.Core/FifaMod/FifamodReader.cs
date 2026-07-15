using System.Security.Cryptography;
using System.Text;
using FrostyConvert.Core.Compression;
using FrostyConvert.Core.IO;
using SysConvert = System.Convert;

namespace FrostyConvert.Core.FifaMod;

public sealed class FifamodReaderException : Exception
{
    public FifamodReaderException(string message) : base(message) { }
    public FifamodReaderException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Parser for FIFA Editor Tool / FIFA Mod Manager <c>.fifamod</c> files.
/// Matches official <c>Modding.ModReader.ReadNewFormat</c> (FETM magic).
/// </summary>
public static class FifamodReader
{
    public static bool IsFifamod(string path)
    {
        if (!File.Exists(path))
            return false;
        using var fs = File.OpenRead(path);
        return IsFifamod(fs);
    }

    public static bool IsFifamod(Stream stream)
    {
        long pos = stream.Position;
        try
        {
            Span<byte> mag = stackalloc byte[4];
            if (stream.Read(mag) != 4)
                return false;
            return mag.SequenceEqual(FifamodConstants.MagicBytes);
        }
        finally
        {
            stream.Position = pos;
        }
    }

    public static FifamodFile Read(string path, bool loadResourceData = true, bool decompress = true)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Mod file not found.", path);

        using var fs = File.OpenRead(path);
        return Read(fs, path, loadResourceData, decompress);
    }

    public static FifamodFile Read(Stream stream, string pathForContext, bool loadResourceData = true, bool decompress = true)
    {
        if (!stream.CanSeek)
            throw new ArgumentException("Stream must be seekable.", nameof(stream));

        using var reader = new EndianBinaryReader(stream, leaveOpen: true);
        var notes = new List<string>();

        uint magic = reader.ReadUInt32();
        if (magic != FifamodConstants.MagicLe)
        {
            stream.Position = 0;
            ulong old = reader.ReadUInt64();
            if (old == FifamodConstants.OldMagic)
                throw new FifamodReaderException("Legacy (pre-FETM) fifamod format is not supported yet.");
            throw new FifamodReaderException(
                $"Not a .fifamod (expected FETM magic 0x{FifamodConstants.MagicLe:X8}, got 0x{magic:X8}).");
        }

        // Official layout: byte modVersion, length-prefixed game name, u24 gameVersion
        byte modVersion = reader.ReadByte();
        string gameName = reader.ReadLengthPrefixedString();
        uint gameVersion = ReadUInt24(reader);

        string title = reader.ReadLengthPrefixedString();
        string author = reader.ReadLengthPrefixedString();
        byte mainCategory = reader.ReadByte();
        byte subCategory = reader.ReadByte();
        string customCategory = reader.ReadLengthPrefixedString();
        string secondCustomCategory = reader.ReadLengthPrefixedString();
        string version = reader.ReadLengthPrefixedString();
        string description = reader.ReadLengthPrefixedString();
        string outOfDateLink = reader.ReadLengthPrefixedString();
        string discord = reader.ReadLengthPrefixedString();
        string patreon = reader.ReadLengthPrefixedString();
        string twitter = reader.ReadLengthPrefixedString();
        string youtube = reader.ReadLengthPrefixedString();
        string instagram = reader.ReadLengthPrefixedString();
        string facebook = reader.ReadLengthPrefixedString();
        string customLink = reader.ReadLengthPrefixedString();

        int iconLen = reader.Read7BitEncodedInt();
        byte[] icon = iconLen > 0 ? reader.ReadBytes(iconLen) : Array.Empty<byte>();

        int screenshotCount = reader.Read7BitEncodedInt();
        for (int i = 0; i < screenshotCount; i++)
        {
            int n = reader.Read7BitEncodedInt();
            if (n > 0)
                stream.Position += n; // skip screenshot payloads
        }

        int localeCount = reader.Read7BitEncodedInt();
        for (int i = 0; i < localeCount; i++)
        {
            _ = reader.ReadLengthPrefixedString();
            _ = reader.ReadLengthPrefixedString();
        }

        int initFsCount = reader.Read7BitEncodedInt();
        for (int i = 0; i < initFsCount; i++)
        {
            _ = reader.ReadLengthPrefixedString();
            int n = reader.Read7BitEncodedInt();
            if (n > 0)
                stream.Position += n;
        }

        // Player lua + kit lua (count of keys, each with list of strings)
        SkipSimpleLua(reader);
        SkipSimpleLua(reader);

        uint dataBaseOffset = reader.ReadUInt32();

        uint addedBundleCount = ReadUInt24(reader);
        for (uint i = 0; i < addedBundleCount; i++)
        {
            _ = reader.ReadLengthPrefixedString();
            _ = reader.ReadUInt64(); // name hash
            _ = reader.ReadUInt32(); // super bundle hash
        }

        var resources = new List<FifamodResource>();

        uint ebxCount = ReadUInt24(reader);
        for (uint i = 0; i < ebxCount; i++)
        {
            string name = reader.ReadLengthPrefixedString();
            var flags = (FifamodEbxFlags)reader.ReadByte();
            byte[] sha = reader.ReadSha1();
            long relOff = Read7BitEncodedLong(reader);
            int length = reader.Read7BitEncodedInt();
            int originalSize = reader.Read7BitEncodedInt();

            if (flags.HasFlag(FifamodEbxFlags.AddToBundleRefTable))
            {
                _ = reader.ReadUInt32(); // brt name hash
                _ = reader.ReadLengthPrefixedString(); // bundle ref path
                if (flags.HasFlag(FifamodEbxFlags.HasParentBundleRef))
                    _ = reader.ReadLengthPrefixedString();
            }

            ulong[] bundles = Array.Empty<ulong>();
            if (flags.HasFlag(FifamodEbxFlags.HasAddedBundles))
            {
                int n = reader.Read7BitEncodedInt();
                bundles = new ulong[n];
                for (int b = 0; b < n; b++)
                    bundles[b] = reader.ReadUInt64();
            }

            resources.Add(new FifamodResource
            {
                Name = name,
                Kind = FifamodResourceKind.Ebx,
                EbxFlags = flags,
                Sha1 = sha,
                FileOffset = dataBaseOffset + relOff,
                CompressedSize = length,
                UncompressedSize = originalSize,
                AddedBundleHashes = bundles,
                BundleAssignmentOnly = flags.HasFlag(FifamodEbxFlags.BundleAssignmentOnly),
            });
        }

        uint resCount = ReadUInt24(reader);
        for (uint i = 0; i < resCount; i++)
        {
            string name = reader.ReadLengthPrefixedString();
            var flags = (FifamodResFlags)reader.ReadByte();
            byte[] sha = reader.ReadSha1();
            long relOff = Read7BitEncodedLong(reader);
            int length = reader.Read7BitEncodedInt();
            int originalSize = reader.Read7BitEncodedInt();
            int bundleN = reader.Read7BitEncodedInt();
            var bundles = new ulong[bundleN];
            for (int b = 0; b < bundleN; b++)
                bundles[b] = reader.ReadUInt64();
            uint resType = reader.ReadUInt32();
            ulong resRid = reader.ReadUInt64();
            byte[] resMeta = reader.ReadBytes(16);

            resources.Add(new FifamodResource
            {
                Name = name,
                Kind = FifamodResourceKind.Res,
                ResFlags = flags,
                Sha1 = sha,
                FileOffset = dataBaseOffset + relOff,
                CompressedSize = length,
                UncompressedSize = originalSize,
                ResType = resType,
                ResRid = resRid,
                ResMeta = resMeta,
                AddedBundleHashes = bundles,
                BundleAssignmentOnly = flags.HasFlag(FifamodResFlags.BundleAssignmentOnly),
            });
        }

        bool h32IsU64 = gameName.StartsWith("FC26", StringComparison.OrdinalIgnoreCase)
                        || gameName.StartsWith("FC 26", StringComparison.OrdinalIgnoreCase)
                        || gameName.Contains("26", StringComparison.Ordinal);

        uint chunkCount = ReadUInt24(reader);
        for (uint i = 0; i < chunkCount; i++)
        {
            Guid id = reader.ReadGuid();
            byte[] sha = reader.ReadSha1();
            var flags = (FifamodChunkFlags)reader.ReadUInt16();
            long relOff = Read7BitEncodedLong(reader);
            int length = reader.Read7BitEncodedInt();
            int logicalOffset = 0;
            int logicalSize = 0;
            ulong h32 = 0;
            ulong legacyHash = 0;
            string? legacyName = null;
            uint superBundleHash = 0;

            if (flags.HasFlag(FifamodChunkFlags.HasLogicalOffset))
                logicalOffset = reader.Read7BitEncodedInt();
            if (flags.HasFlag(FifamodChunkFlags.HasLogicalSize))
                logicalSize = reader.Read7BitEncodedInt();
            if (flags.HasFlag(FifamodChunkFlags.HasH32))
            {
                // FC26+ uses u64; older titles used u32
                h32 = h32IsU64 ? reader.ReadUInt64() : reader.ReadUInt32();
            }
            if (flags.HasFlag(FifamodChunkFlags.IsLegacy))
            {
                legacyHash = reader.ReadUInt64();
                legacyName = reader.ReadLengthPrefixedString();
            }
            ulong[] bundles = Array.Empty<ulong>();
            if (flags.HasFlag(FifamodChunkFlags.HasAddedBundles))
            {
                int n = reader.Read7BitEncodedInt();
                bundles = new ulong[n];
                for (int b = 0; b < n; b++)
                    bundles[b] = reader.ReadUInt64();
            }
            if (flags.HasFlag(FifamodChunkFlags.AddToSuperBundle))
                superBundleHash = reader.ReadUInt32();

            resources.Add(new FifamodResource
            {
                Name = id.ToString(),
                Kind = FifamodResourceKind.Chunk,
                ChunkId = id,
                ChunkFlags = flags,
                Sha1 = sha,
                FileOffset = dataBaseOffset + relOff,
                CompressedSize = length,
                UncompressedSize = 0,
                LogicalOffset = logicalOffset,
                LogicalSize = logicalSize,
                H32 = h32,
                SuperBundleHash = superBundleHash,
                LegacyFileNameHash = legacyHash,
                LegacyFileName = legacyName,
                AddedBundleHashes = bundles,
                BundleAssignmentOnly = flags.HasFlag(FifamodChunkFlags.BundleAssignmentOnly),
            });
        }

        // collectors + bundle ref tables (skip payloads, keep parse position valid)
        int collectorCount = reader.Read7BitEncodedInt();
        for (int i = 0; i < collectorCount; i++)
        {
            _ = reader.ReadLengthPrefixedString();
            _ = reader.ReadGuid();
            _ = reader.ReadByte(); // bool
            _ = reader.ReadUInt32();
        }

        int brtCount = reader.Read7BitEncodedInt();
        for (int i = 0; i < brtCount; i++)
        {
            _ = reader.ReadUInt32();
            _ = reader.ReadLengthPrefixedString();
        }

        if (loadResourceData)
        {
            foreach (var res in resources)
            {
                if (res.CompressedSize <= 0)
                    continue;
                if (res.FileOffset < 0 || res.FileOffset + res.CompressedSize > stream.Length)
                {
                    res.DecompressError = "Compressed range out of file bounds.";
                    continue;
                }

                stream.Position = res.FileOffset;
                byte[] compressed = reader.ReadBytes(res.CompressedSize);
                res.CompressedData = compressed;
                res.Sha1MatchesCompressed = SHA1.HashData(compressed).AsSpan().SequenceEqual(res.Sha1);

                if (!decompress)
                    continue;

                try
                {
                    if (res.Kind == FifamodResourceKind.Ebx || res.Kind == FifamodResourceKind.Res)
                        res.Data = DecompressPayload(compressed, res.UncompressedSize);
                    else
                        res.Data = CasBlockDecompressor.LooksLikeCasStream(compressed)
                            ? CasBlockDecompressor.Decompress(compressed)
                            : compressed;
                }
                catch (Exception ex)
                {
                    res.DecompressError = ex.Message;
                    if (compressed.Length == res.UncompressedSize)
                        res.Data = compressed;
                }
            }
        }

        return new FifamodFile
        {
            Path = pathForContext,
            ModVersion = modVersion,
            GameName = gameName,
            GameVersion = gameVersion,
            Details = new FifamodDetails
            {
                Title = title,
                Author = author,
                Version = version,
                Description = description,
                MainCategory = mainCategory,
                SubCategory = subCategory,
                CustomCategory = customCategory,
                SecondCustomCategory = secondCustomCategory,
                OutOfDateModWebsiteLink = outOfDateLink,
                DiscordLink = discord,
                PatreonLink = patreon,
                TwitterLink = twitter,
                YouTubeLink = youtube,
                InstagramLink = instagram,
                FacebookLink = facebook,
                CustomLink = customLink,
                Icon = icon,
                ScreenshotCount = screenshotCount,
            },
            DataBaseOffset = dataBaseOffset,
            Resources = resources,
            EbxCount = (int)ebxCount,
            ResCount = (int)resCount,
            ChunkCount = (int)chunkCount,
            Notes = notes,
        };
    }

    public static byte[] DecompressPayload(byte[] compressed, int expectedUncompressed)
    {
        if (compressed is null || compressed.Length == 0)
            return Array.Empty<byte>();

        if (CasBlockDecompressor.LooksLikeCasStream(compressed))
            return CasBlockDecompressor.Decompress(compressed);

        if (compressed.Length >= 8)
        {
            int uszBe = (compressed[0] << 24) | (compressed[1] << 16) | (compressed[2] << 8) | compressed[3];
            int typeBe = (compressed[4] << 8) | compressed[5];
            int cszBe = (compressed[6] << 8) | compressed[7];
            if (uszBe == expectedUncompressed && cszBe == compressed.Length - 8)
            {
                byte[] payload = new byte[cszBe];
                Buffer.BlockCopy(compressed, 8, payload, 0, cszBe);
                if ((typeBe & 0x7F) == 0x19 || (typeBe >> 8) == 0x19 || typeBe == 0x1970)
                    return Oodle.Decompress(payload, uszBe);
            }
        }

        if (compressed.Length == expectedUncompressed)
            return compressed;

        throw new FifamodReaderException(
            $"Unable to decompress payload (csz={compressed.Length}, usz={expectedUncompressed}, head={SysConvert.ToHexString(compressed.AsSpan(0, Math.Min(8, compressed.Length)))}).");
    }

    private static void SkipSimpleLua(EndianBinaryReader reader)
    {
        int keys = reader.Read7BitEncodedInt();
        for (int i = 0; i < keys; i++)
        {
            _ = reader.ReadLengthPrefixedString();
            int n = reader.Read7BitEncodedInt();
            for (int j = 0; j < n; j++)
                _ = reader.ReadLengthPrefixedString();
        }
    }

    private static uint ReadUInt24(EndianBinaryReader reader)
    {
        byte b0 = reader.ReadByte();
        byte b1 = reader.ReadByte();
        byte b2 = reader.ReadByte();
        return (uint)(b0 | (b1 << 8) | (b2 << 16));
    }

    private static long Read7BitEncodedLong(EndianBinaryReader reader)
    {
        long result = 0;
        int shift = 0;
        byte b;
        do
        {
            if (shift >= 63)
                throw new FormatException("7-bit encoded long is too large.");
            b = reader.ReadByte();
            result |= (long)(b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);
        return result;
    }
}
