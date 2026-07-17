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
                throw new FifamodReaderException(
                    "Legacy (pre-FETM) fifamod is not supported. Open the original project in FIFA Editor Tool " +
                    "and re-export a modern FETM .fifamod, or ask the author for an unlocked export.");
            throw new FifamodReaderException(
                $"Not a .fifamod (expected FETM magic 0x{FifamodConstants.MagicLe:X8}, got 0x{magic:X8}).");
        }

        // Official layout: byte modVersion, length-prefixed game name, u24 gameVersion
        byte modVersion = reader.ReadByte();
        // FMT Pro password-lock has been observed to use non-zero high bits / unusual version markers.
        // If the following string read fails or is empty while file is large, treat as locked/corrupt.
        string gameName;
        try
        {
            gameName = reader.ReadLengthPrefixedString();
        }
        catch (Exception ex)
        {
            throw new FifamodReaderException(
                "Failed to read .fifamod header after magic. The file may be password-locked (FMT Pro), " +
                "truncated, or corrupt. FrostyConvert cannot unlock password-protected mods without the author's password.",
                ex);
        }
        if (string.IsNullOrEmpty(gameName) && stream.Length > 256)
        {
            notes.Add(
                "Empty game name with large file — possible password-locked or non-standard .fifamod. " +
                "If convert fails, ask the author for an unlocked export.");
        }
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
        if (screenshotCount < 0 || screenshotCount > 64)
            throw new FifamodReaderException($"Unreasonable screenshot count: {screenshotCount}");
        var screenshots = new List<byte[]>(screenshotCount);
        for (int i = 0; i < screenshotCount; i++)
        {
            int n = reader.Read7BitEncodedInt();
            screenshots.Add(n > 0 ? reader.ReadBytes(n) : Array.Empty<byte>());
        }

        int localeCount = reader.Read7BitEncodedInt();
        if (localeCount < 0 || localeCount > 10_000)
            throw new FifamodReaderException($"Unreasonable locale.ini count: {localeCount}");
        var localeIni = new List<FifamodLocaleIniFile>(localeCount);
        for (int i = 0; i < localeCount; i++)
        {
            localeIni.Add(new FifamodLocaleIniFile
            {
                Description = reader.ReadLengthPrefixedString(),
                Contents = reader.ReadLengthPrefixedString(),
            });
        }

        int initFsCount = reader.Read7BitEncodedInt();
        if (initFsCount < 0 || initFsCount > 100_000)
            throw new FifamodReaderException($"Unreasonable initfs count: {initFsCount}");
        var initFs = new List<FifamodInitFsFile>(initFsCount);
        for (int i = 0; i < initFsCount; i++)
        {
            string name = reader.ReadLengthPrefixedString();
            int n = reader.Read7BitEncodedInt();
            byte[] data = n > 0 ? reader.ReadBytes(n) : Array.Empty<byte>();
            initFs.Add(new FifamodInitFsFile { Name = name, Data = data });
        }

        // Player lua + kit lua: free-form maps (FET new-format load uses version 100 → default branch)
        var playerLua = ReadLuaModMap(reader);
        var playerKitLua = ReadLuaModMap(reader);

        uint dataBaseOffset = reader.ReadUInt32();

        uint addedBundleCount = ReadUInt24(reader);
        if (addedBundleCount > 100_000)
            throw new FifamodReaderException($"Unreasonable added bundle count: {addedBundleCount}");
        var addedBundles = new List<FifamodAddedBundle>((int)addedBundleCount);
        for (uint i = 0; i < addedBundleCount; i++)
        {
            addedBundles.Add(new FifamodAddedBundle
            {
                Name = reader.ReadLengthPrefixedString(),
                NameHash = reader.ReadUInt64(),
                SuperBundleHash = reader.ReadUInt32(),
            });
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

            FifamodBrtAddition? brt = null;
            if (flags.HasFlag(FifamodEbxFlags.AddToBundleRefTable))
            {
                uint brtHash = reader.ReadUInt32();
                string brtPath = reader.ReadLengthPrefixedString();
                string? parentPath = null;
                if (flags.HasFlag(FifamodEbxFlags.HasParentBundleRef))
                    parentPath = reader.ReadLengthPrefixedString();
                brt = new FifamodBrtAddition
                {
                    BrtNameHash = brtHash,
                    BundleRefPath = brtPath,
                    ParentBundleRefPath = parentPath,
                    BundleRefOnly = flags.HasFlag(FifamodEbxFlags.BundleRefOnly),
                };
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
                BrtAddition = brt,
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

        // Trailing collectors + BRT name table (ModWriter after chunk index, before data section)
        int collectorCount = reader.Read7BitEncodedInt();
        if (collectorCount < 0 || collectorCount > 1_000_000)
            throw new FifamodReaderException($"Unreasonable collector count: {collectorCount}");
        var collectors = new List<FifamodCollectorEntry>(collectorCount);
        for (int i = 0; i < collectorCount; i++)
        {
            collectors.Add(new FifamodCollectorEntry
            {
                CollectorEbxName = reader.ReadLengthPrefixedString(),
                CollectorChunkId = reader.ReadGuid(),
                IsPatch = reader.ReadByte() != 0,
                Meta = reader.ReadUInt32(),
            });
        }

        int brtCount = reader.Read7BitEncodedInt();
        if (brtCount < 0 || brtCount > 1_000_000)
            throw new FifamodReaderException($"Unreasonable BRT table count: {brtCount}");
        var bundleRefTables = new List<FifamodBundleRefTableEntry>(brtCount);
        for (int i = 0; i < brtCount; i++)
        {
            bundleRefTables.Add(new FifamodBundleRefTableEntry
            {
                NameHash = reader.ReadUInt32(),
                Name = reader.ReadLengthPrefixedString(),
            });
        }

        int decompressErrors = 0;
        int withPayload = 0;
        if (loadResourceData)
        {
            foreach (var res in resources)
            {
                if (res.CompressedSize <= 0)
                    continue;
                withPayload++;
                if (res.FileOffset < 0 || res.FileOffset + res.CompressedSize > stream.Length)
                {
                    res.DecompressError = "Compressed range out of file bounds.";
                    decompressErrors++;
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
                    decompressErrors++;
                    if (compressed.Length == res.UncompressedSize)
                        res.Data = compressed;
                }
            }
        }

        bool suspectLock = notes.Any(n =>
            n.Contains("password-locked", StringComparison.OrdinalIgnoreCase));
        if (withPayload > 4 && decompressErrors * 2 >= withPayload)
        {
            suspectLock = true;
            notes.Add(
                "High decompress failure rate — possible password-locked (FMT Pro) or corrupt payloads.");
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
                Screenshots = screenshots,
            },
            DataBaseOffset = dataBaseOffset,
            Resources = resources,
            Collectors = collectors,
            BundleRefTables = bundleRefTables,
            AddedBundles = addedBundles,
            LocaleIniFiles = localeIni,
            InitFsFiles = initFs,
            PlayerLuaMods = playerLua,
            PlayerKitLuaMods = playerKitLua,
            EbxCount = (int)ebxCount,
            ResCount = (int)resCount,
            ChunkCount = (int)chunkCount,
            Notes = notes,
            SuspectedPasswordLock = suspectLock,
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

    /// <summary>
    /// Free-form lua mod map: count + (key, valueCount, values…)* — matches
    /// <c>ModWriter.WritePlayerLuaMods</c> / new-format <c>LoadPlayerLuaModifications(..., 100)</c>.
    /// </summary>
    private static List<FifamodLuaModEntry> ReadLuaModMap(EndianBinaryReader reader)
    {
        int keys = reader.Read7BitEncodedInt();
        if (keys < 0 || keys > 100_000)
            throw new FifamodReaderException($"Unreasonable lua mod key count: {keys}");
        var list = new List<FifamodLuaModEntry>(keys);
        for (int i = 0; i < keys; i++)
        {
            string key = reader.ReadLengthPrefixedString();
            int n = reader.Read7BitEncodedInt();
            if (n < 0 || n > 1_000_000)
                throw new FifamodReaderException($"Unreasonable lua mod value count for '{key}': {n}");
            var values = new string[n];
            for (int j = 0; j < n; j++)
                values[j] = reader.ReadLengthPrefixedString();
            list.Add(new FifamodLuaModEntry { Key = key, Values = values });
        }
        return list;
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
