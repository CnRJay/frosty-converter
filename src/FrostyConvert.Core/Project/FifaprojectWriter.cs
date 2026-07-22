using FrostyConvert.Core.FifaMod;
using FrostyConvert.Core.IO;

namespace FrostyConvert.Core.Project;

/// <summary>
/// Writes a FIFA Editor Tool v2 <c>.fifaproject</c> from a parsed <see cref="FifamodFile"/>.
/// Layout matches <c>Fifa_Tool.EditorProject.Save</c> (magic FETP).
/// </summary>
public static class FifaprojectWriter
{
    public const uint MagicLe = 0x50544546; // 'FETP'
    public const byte ProjectVersion = 2;

    /// <summary>
    /// FET <c>CompressionLevel</c> enum (Sdk): Invalid=0, Fastest=1, …, None=8.
    /// </summary>
    public const byte CompressionLevelFastest = 1;

    // Project-side chunk flags (same bits as mod ChunkFlags for fields we write).
    private const ushort ProjChunkIsAdded = 1;
    private const ushort ProjChunkIsLegacy = 2;
    private const ushort ProjChunkAddToSuperBundle = 4;
    private const ushort ProjChunkHasLogicalOffset = 8;
    private const ushort ProjChunkHasLogicalSize = 0x10;
    private const ushort ProjChunkHasH32 = 0x20;
    private const ushort ProjChunkIsLegacyAdded = 0x40;
    private const ushort ProjChunkHasAddedBundles = 0x80;
    private const ushort ProjChunkBundleAssignmentOnly = 0x100;

    public static void Write(string path, FifamodFile mod, IEnumerable<FifamodResource>? extraResources = null)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var fs = File.Create(path);
        Write(fs, mod, extraResources);
    }

    public static void Write(Stream stream, FifamodFile mod, IEnumerable<FifamodResource>? extraResources = null)
    {
        using var w = new EndianBinaryWriter(stream, leaveOpen: true);

        WriteHeader(w, mod);

        // Added bundles — FET project layout: name, superBundle hash u32, type u8
        WriteUInt24(w, (uint)mod.AddedBundles.Count);
        foreach (var b in mod.AddedBundles)
        {
            w.WriteLengthPrefixedString(b.Name);
            w.WriteUInt32(b.SuperBundleHash);
            w.WriteByte(b.Type);
        }

        bool h32IsU64 = mod.GameName.StartsWith("FC26", StringComparison.OrdinalIgnoreCase)
                        || mod.GameName.StartsWith("FC 26", StringComparison.OrdinalIgnoreCase)
                        || mod.GameName.Contains("26", StringComparison.Ordinal);

        IReadOnlyList<FifamodResource> all = extraResources is null
            ? mod.Resources
            : mod.Resources.Concat(extraResources).ToList();

        // .fifamod never stores IsAdded for EBX/Res (ModWriter clears it). New head variations
        // (var_1+) must be project-IsAdded or FET skips them as "doesn't exist".
        HashSet<Guid> forceAddedChunks = FifamodProjectAddedRecovery.CollectForceAddedChunkIds(all);
        Dictionary<string, uint> resTypeByName = FifamodProjectAddedRecovery.BuildResTypeByName(all);

        // --- Chunks ---
        var chunks = all
            .Where(r => r.Kind == FifamodResourceKind.Chunk && ProjectPayload(r).Length > 0)
            .ToList();
        long posChunks = stream.Position;
        WriteUInt24(w, 0);
        int chunksWritten = 0;
        foreach (var c in chunks)
        {
            byte[] payload = ProjectPayload(c);
            bool forceAdded = FifamodProjectAddedRecovery.ShouldForceAdded(c, forceAddedChunks);
            WriteChunkEntry(w, mod, c, payload, h32IsU64, forceAdded);
            chunksWritten++;
        }
        long afterChunks = stream.Position;
        stream.Position = posChunks;
        WriteUInt24(w, (uint)chunksWritten);
        stream.Position = afterChunks;

        // --- Res ---
        var resList = all
            .Where(r => r.Kind == FifamodResourceKind.Res && ProjectPayload(r).Length > 0)
            .ToList();
        long posRes = stream.Position;
        WriteUInt24(w, 0);
        int resWritten = 0;
        foreach (var r in resList)
        {
            bool forceAdded = FifamodProjectAddedRecovery.ShouldForceAdded(r, forceAddedChunks);
            WriteResEntry(w, mod, r, ProjectPayload(r), forceAdded);
            resWritten++;
        }
        long afterRes = stream.Position;
        stream.Position = posRes;
        WriteUInt24(w, (uint)resWritten);
        stream.Position = afterRes;

        // --- EBX ---
        var ebxList = all
            .Where(r => r.Kind == FifamodResourceKind.Ebx && ProjectPayload(r).Length > 0)
            .ToList();
        long posEbx = stream.Position;
        WriteUInt24(w, 0);
        int ebxWritten = 0;
        foreach (var e in ebxList)
        {
            bool forceAdded = FifamodProjectAddedRecovery.ShouldForceAdded(e, forceAddedChunks);
            WriteEbxEntry(w, mod, e, ProjectPayload(e), forceAdded, resTypeByName);
            ebxWritten++;
        }
        long afterEbx = stream.Position;
        stream.Position = posEbx;
        WriteUInt24(w, (uint)ebxWritten);
        stream.Position = afterEbx;
    }

    /// <summary>Counts written by <see cref="Write"/> for CLI reporting.</summary>
    public static (int Chunks, int Res, int Ebx) CountWritable(
        FifamodFile mod,
        IEnumerable<FifamodResource>? extraResources = null)
    {
        IEnumerable<FifamodResource> all = extraResources is null
            ? mod.Resources
            : mod.Resources.Concat(extraResources);
        int chunks = all.Count(r => r.Kind == FifamodResourceKind.Chunk && ProjectPayload(r).Length > 0);
        int res = all.Count(r => r.Kind == FifamodResourceKind.Res && ProjectPayload(r).Length > 0);
        int ebx = all.Count(r => r.Kind == FifamodResourceKind.Ebx && ProjectPayload(r).Length > 0);
        return (chunks, res, ebx);
    }

    private static void WriteHeader(EndianBinaryWriter w, FifamodFile mod)
    {
        w.WriteUInt32(MagicLe);
        w.WriteByte(ProjectVersion);
        // Tool version stamped as FrostyConvert release (major.minor.build.revision)
        w.WriteByte(1);
        w.WriteByte(0);
        w.WriteByte(10);
        w.WriteByte(0);

        w.WriteLengthPrefixedString(mod.GameName);
        WriteUInt24(w, mod.GameVersion);

        var d = mod.Details;
        w.WriteLengthPrefixedString(d.Title);
        w.WriteLengthPrefixedString(d.Author);
        w.WriteByte(d.MainCategory);
        w.WriteByte(d.SubCategory);
        w.WriteLengthPrefixedString(d.CustomCategory ?? "");
        w.WriteLengthPrefixedString(d.SecondCustomCategory ?? "");
        w.WriteLengthPrefixedString(string.IsNullOrEmpty(d.Version) ? "1.0" : d.Version);
        string desc = d.Description ?? "";
        if (!desc.Contains("FrostyConvert", StringComparison.Ordinal))
            desc += "\n\nRecovered from .fifamod by FrostyConvert for editing in FIFA Editor Tool.";
        w.WriteLengthPrefixedString(desc);
        w.WriteLengthPrefixedString(d.OutOfDateModWebsiteLink ?? "");
        w.WriteLengthPrefixedString(d.DiscordLink ?? "");
        w.WriteLengthPrefixedString(d.PatreonLink ?? "");
        w.WriteLengthPrefixedString(d.TwitterLink ?? "");
        w.WriteLengthPrefixedString(d.YouTubeLink ?? "");
        w.WriteLengthPrefixedString(d.InstagramLink ?? "");
        w.WriteLengthPrefixedString(d.FacebookLink ?? "");
        w.WriteLengthPrefixedString(d.CustomLink ?? "");

        if (d.Icon is { Length: > 0 })
        {
            w.Write7BitEncodedInt(d.Icon.Length);
            w.WriteBytes(d.Icon);
        }
        else
        {
            w.Write7BitEncodedInt(0);
        }

        // Screenshots (same as EditorProject.WriteHeader)
        IReadOnlyList<byte[]> shots = d.Screenshots;
        w.Write7BitEncodedInt(shots.Count);
        foreach (byte[] shot in shots)
        {
            if (shot is { Length: > 0 })
            {
                w.Write7BitEncodedInt(shot.Length);
                w.WriteBytes(shot);
            }
            else
            {
                w.Write7BitEncodedInt(0);
            }
        }

        w.Write7BitEncodedInt(mod.LocaleIniFiles.Count);
        foreach (var locale in mod.LocaleIniFiles)
        {
            w.WriteLengthPrefixedString(locale.Description);
            w.WriteLengthPrefixedString(locale.Contents);
        }

        w.Write7BitEncodedInt(mod.InitFsFiles.Count);
        foreach (var file in mod.InitFsFiles)
        {
            w.WriteLengthPrefixedString(file.Name);
            w.Write7BitEncodedInt(file.Data.Length);
            if (file.Data.Length > 0)
                w.WriteBytes(file.Data);
        }

        // Free-form maps (new-format load uses version 100 → default branch)
        WriteLuaModMap(w, mod.PlayerLuaMods);
        WriteLuaModMap(w, mod.PlayerKitLuaMods);
    }

    private static void WriteLuaModMap(EndianBinaryWriter w, IReadOnlyList<FifamodLuaModEntry> entries)
    {
        w.Write7BitEncodedInt(entries.Count);
        foreach (var e in entries)
        {
            w.WriteLengthPrefixedString(e.Key);
            w.Write7BitEncodedInt(e.Values.Count);
            foreach (string v in e.Values)
                w.WriteLengthPrefixedString(v);
        }
    }

    private static void WriteChunkEntry(
        EndianBinaryWriter w,
        FifamodFile mod,
        FifamodResource c,
        byte[] payload,
        bool h32IsU64,
        bool forceAdded = false)
    {
        w.WriteGuid(c.ChunkId);

        ushort flags = 0;
        bool isAdded = forceAdded || c.IsAdded;
        if (isAdded)
            flags |= ProjChunkIsAdded;
        if (c.LogicalOffset != 0)
            flags |= ProjChunkHasLogicalOffset;
        if (c.LogicalSize != 0)
            flags |= ProjChunkHasLogicalSize;
        if (c.H32 != 0)
            flags |= ProjChunkHasH32;
        if (c.ChunkFlags.HasFlag(FifamodChunkFlags.IsLegacy) || !string.IsNullOrEmpty(c.LegacyFileName))
        {
            flags |= ProjChunkIsLegacy;
            if (c.ChunkFlags.HasFlag(FifamodChunkFlags.IsLegacyAdded))
                flags |= ProjChunkIsLegacyAdded;
        }
        if (c.AddedBundleHashes.Length > 0)
            flags |= ProjChunkHasAddedBundles;
        if (c.BundleAssignmentOnly)
            flags |= ProjChunkBundleAssignmentOnly;
        if (isAdded && c.SuperBundleHash != 0)
            flags |= ProjChunkAddToSuperBundle;

        w.WriteUInt16(flags);
        w.WriteBytes(PadSha1(c.Sha1 is { Length: 20 } ? c.Sha1 : System.Security.Cryptography.SHA1.HashData(payload)));

        if (c.LogicalOffset != 0)
            w.Write7BitEncodedInt(c.LogicalOffset);
        if (c.LogicalSize != 0)
            w.Write7BitEncodedInt(c.LogicalSize);
        if (c.H32 != 0)
        {
            if (h32IsU64)
                w.Write(c.H32);
            else
                w.WriteUInt32((uint)c.H32);
        }

        w.Write7BitEncodedInt(payload.Length);

        if ((flags & ProjChunkIsLegacy) != 0)
        {
            w.Write(c.LegacyFileNameHash);
            if ((flags & ProjChunkIsLegacyAdded) != 0)
                w.WriteLengthPrefixedString(c.LegacyFileName ?? "");
        }

        WriteUInt24(w, mod.GameVersion);
        if (!isAdded)
            w.WriteBytes(new byte[20]); // AssetSha1AtImport unknown offline

        if (c.AddedBundleHashes.Length > 0)
        {
            w.Write7BitEncodedInt(c.AddedBundleHashes.Length);
            foreach (ulong h in c.AddedBundleHashes)
                w.Write(h);
        }

        if (isAdded && c.SuperBundleHash != 0)
            w.WriteUInt32(c.SuperBundleHash);

        w.WriteByte(CompressionLevelFastest);
        w.WriteBytes(payload);
    }

    private static void WriteResEntry(
        EndianBinaryWriter w,
        FifamodFile mod,
        FifamodResource r,
        byte[] payload,
        bool forceAdded = false)
    {
        int originalSize = r.UncompressedSize > 0 ? r.UncompressedSize : payload.Length;
        bool isAdded = forceAdded || r.IsAdded;

        w.WriteLengthPrefixedString(r.Name);
        var flags = FifamodResFlags.IsDirectlyModified;
        if (isAdded)
            flags |= FifamodResFlags.IsAdded;
        if (r.ResMeta is { Length: > 0 } || isAdded)
            flags |= FifamodResFlags.HasMeta;
        if (r.AddedBundleHashes.Length > 0)
            flags |= FifamodResFlags.HasAddedBundles;
        if (r.BundleAssignmentOnly)
            flags |= FifamodResFlags.BundleAssignmentOnly;
        w.WriteByte((byte)flags);

        if (isAdded)
        {
            w.WriteUInt32(r.ResType);
            w.Write(r.ResRid);
        }

        w.WriteBytes(PadSha1(r.Sha1 is { Length: 20 } ? r.Sha1 : System.Security.Cryptography.SHA1.HashData(payload)));
        w.Write7BitEncodedInt(originalSize);

        if (flags.HasFlag(FifamodResFlags.HasMeta))
        {
            var meta = new byte[16];
            if (r.ResMeta is { Length: > 0 })
                Buffer.BlockCopy(r.ResMeta, 0, meta, 0, Math.Min(16, r.ResMeta.Length));
            w.WriteBytes(meta);
        }

        WriteUInt24(w, mod.GameVersion);
        if (!isAdded)
            w.WriteBytes(new byte[20]);

        if (r.AddedBundleHashes.Length > 0)
        {
            w.Write7BitEncodedInt(r.AddedBundleHashes.Length);
            foreach (ulong h in r.AddedBundleHashes)
                w.Write(h);
        }

        w.WriteByte(CompressionLevelFastest);
        w.Write7BitEncodedInt(payload.Length);
        w.WriteBytes(payload);
    }

    private static void WriteEbxEntry(
        EndianBinaryWriter w,
        FifamodFile mod,
        FifamodResource e,
        byte[] payload,
        bool forceAdded = false,
        IReadOnlyDictionary<string, uint>? resTypeByName = null)
    {
        int originalSize = e.UncompressedSize > 0 ? e.UncompressedSize : payload.Length;
        bool isAdded = forceAdded || e.IsAdded;
        FifamodBrtAddition? brt = e.BrtAddition is { BrtNameHash: not 0 } b ? b : null;

        w.WriteLengthPrefixedString(e.Name);
        var flags = FifamodEbxFlags.IsDirectlyModified;
        if (isAdded)
            flags |= FifamodEbxFlags.IsAdded;
        if (brt is not null)
        {
            flags |= FifamodEbxFlags.AddToBundleRefTable;
            if (brt.ParentBundleRefPath is not null)
                flags |= FifamodEbxFlags.HasParentBundleRef;
            if (brt.BundleRefOnly)
                flags |= FifamodEbxFlags.BundleRefOnly;
        }
        if (e.AddedBundleHashes.Length > 0)
            flags |= FifamodEbxFlags.HasAddedBundles;
        if (e.BundleAssignmentOnly)
            flags |= FifamodEbxFlags.BundleAssignmentOnly;
        w.WriteByte((byte)flags);

        if (isAdded)
        {
            uint? resType = null;
            if (resTypeByName is not null
                && !string.IsNullOrEmpty(e.Name)
                && resTypeByName.TryGetValue(e.Name, out uint rt))
                resType = rt;

            string typeName = !string.IsNullOrEmpty(e.EbxTypeName)
                ? e.EbxTypeName!
                : FifamodProjectAddedRecovery.GuessEbxTypeName(e.Name, resType);
            Guid guid = e.EbxGuid != Guid.Empty
                ? e.EbxGuid
                : FifamodProjectAddedRecovery.TryExtractRiffEbxGuid(e.Data)
                  ?? Guid.NewGuid();
            w.WriteLengthPrefixedString(typeName);
            w.WriteGuid(guid);
        }

        // Order matches EditorProject.Save / Load for directly-modified EBX:
        // gameVersion, AssetSha1AtImport, [BRT], [added bundles], sha1, sizes, payload.
        WriteUInt24(w, mod.GameVersion);
        if (!isAdded)
            w.WriteBytes(new byte[20]);

        if (brt is not null)
        {
            w.WriteUInt32(brt.BrtNameHash);
            w.WriteLengthPrefixedString(brt.BundleRefPath ?? "");
            if (brt.ParentBundleRefPath is not null)
                w.WriteLengthPrefixedString(brt.ParentBundleRefPath);
        }

        if (e.AddedBundleHashes.Length > 0)
        {
            w.Write7BitEncodedInt(e.AddedBundleHashes.Length);
            foreach (ulong h in e.AddedBundleHashes)
                w.Write(h);
        }

        byte[] sha = e.Sha1 is { Length: 20 }
            ? e.Sha1
            : System.Security.Cryptography.SHA1.HashData(payload);
        w.WriteBytes(PadSha1(sha));
        w.Write7BitEncodedInt(originalSize);
        w.WriteByte(CompressionLevelFastest);
        w.Write7BitEncodedInt(payload.Length);
        w.WriteBytes(payload);
    }

    /// <summary>
    /// Prefer CAS-compressed bytes from the mod. FET decompresses ModifiedEntry.Data via codec headers.
    /// For chunks, store the raw mod payload even when it is not multi-block CAS (sha already matched).
    /// </summary>
    internal static byte[] ProjectPayload(FifamodResource r)
    {
        if (r.CompressedData is { Length: > 0 })
            return r.CompressedData;
        if (r.Data is { Length: > 0 })
            return r.Data;
        return Array.Empty<byte>();
    }

    private static byte[] PadSha1(byte[] sha)
    {
        if (sha.Length == 20)
            return sha;
        var z = new byte[20];
        Buffer.BlockCopy(sha, 0, z, 0, Math.Min(20, sha.Length));
        return z;
    }

    private static void WriteUInt24(EndianBinaryWriter w, uint value)
    {
        w.WriteByte((byte)(value & 0xFF));
        w.WriteByte((byte)((value >> 8) & 0xFF));
        w.WriteByte((byte)((value >> 16) & 0xFF));
    }
}
