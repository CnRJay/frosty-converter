using FrostyConvert.Core.FifaMod;
using FrostyConvert.Core.IO;

namespace FrostyConvert.Core.Project;

/// <summary>
/// Writes a FIFA Editor Tool v2 <c>.fifaproject</c> from a parsed <see cref="FifamodFile"/>.
/// Layout matches <c>Fifa_Tool.EditorProject.Save</c> (magic FETP).
/// </summary>
/// <remarks>
/// FIFA Editor Tool has no Frosty-style plugin API. The recovery path is:
/// convert <c>.fifamod</c> → <c>.fifaproject</c>, then open the project in FET with the game loaded
/// (same end state as MMC Tools → Import fbmod → Save project).
/// </remarks>
public static class FifaprojectWriter
{
    public const uint MagicLe = 0x50544546; // 'FETP'
    public const byte ProjectVersion = 2;

    /// <summary>
    /// FET <c>CompressionLevel</c> enum (Sdk): Invalid=0, Fastest=1, …, None=8.
    /// Stored payloads use CAS codec headers; level is metadata for re-save.
    /// </summary>
    public const byte CompressionLevelFastest = 1;

    public static void Write(string path, FifamodFile mod)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var fs = File.Create(path);
        Write(fs, mod);
    }

    public static void Write(Stream stream, FifamodFile mod)
    {
        using var w = new EndianBinaryWriter(stream, leaveOpen: true);

        // --- Header (EditorProject.WriteHeader) ---
        w.WriteUInt32(MagicLe);
        w.WriteByte(ProjectVersion);
        w.WriteByte(0); // tool major (FrostyConvert recovery)
        w.WriteByte(1);
        w.WriteByte(0);
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

        w.Write7BitEncodedInt(0); // screenshots
        w.Write7BitEncodedInt(0); // locale ini
        w.Write7BitEncodedInt(0); // initfs
        w.Write7BitEncodedInt(0); // player lua
        w.Write7BitEncodedInt(0); // player kit lua

        // --- Added bundles ---
        WriteUInt24(w, 0);

        // --- Chunks (skip for now; gameplay mods are EBX-only) ---
        WriteUInt24(w, 0);

        // --- Res ---
        // FET AssetManager.GetEbx/GetRes always runs Decompression.Decompress on ModifiedEntry.Data,
        // which requires a CAS codec header (guard bits == 7). Store the mod's compressed CAS blob.
        var resList = mod.Resources
            .Where(r => r.Kind == FifamodResourceKind.Res && ProjectPayload(r).Length > 0)
            .ToList();
        long posRes = stream.Position;
        WriteUInt24(w, 0);
        int resWritten = 0;
        foreach (var r in resList)
        {
            byte[] payload = ProjectPayload(r);
            int originalSize = r.UncompressedSize > 0 ? r.UncompressedSize : payload.Length;
            w.WriteLengthPrefixedString(r.Name);
            var flags = FifamodResFlags.IsDirectlyModified;
            if (r.ResMeta is { Length: > 0 })
                flags |= FifamodResFlags.HasMeta;
            w.WriteByte((byte)flags);
            w.WriteBytes(PadSha1(r.Sha1 is { Length: 20 } ? r.Sha1 : System.Security.Cryptography.SHA1.HashData(payload)));
            w.Write7BitEncodedInt(originalSize);
            if (flags.HasFlag(FifamodResFlags.HasMeta))
            {
                var meta = new byte[16];
                Buffer.BlockCopy(r.ResMeta, 0, meta, 0, Math.Min(16, r.ResMeta.Length));
                w.WriteBytes(meta);
            }
            WriteUInt24(w, mod.GameVersion);
            w.WriteBytes(new byte[20]); // AssetSha1AtImport
            w.WriteByte(CompressionLevelFastest);
            w.Write7BitEncodedInt(payload.Length);
            w.WriteBytes(payload);
            resWritten++;
        }
        long afterRes = stream.Position;
        stream.Position = posRes;
        WriteUInt24(w, (uint)resWritten);
        stream.Position = afterRes;

        // --- EBX ---
        var ebxList = mod.Resources
            .Where(r => r.Kind == FifamodResourceKind.Ebx && ProjectPayload(r).Length > 0)
            .ToList();
        long posEbx = stream.Position;
        WriteUInt24(w, 0);
        int ebxWritten = 0;
        foreach (var e in ebxList)
        {
            byte[] payload = ProjectPayload(e);
            int originalSize = e.UncompressedSize > 0 ? e.UncompressedSize : payload.Length;
            w.WriteLengthPrefixedString(e.Name);
            w.WriteByte((byte)FifamodEbxFlags.IsDirectlyModified);
            WriteUInt24(w, mod.GameVersion);
            w.WriteBytes(new byte[20]); // AssetSha1AtImport — editor resolves live base

            // CAS-compressed blob from .fifamod (must pass Decompression.ReadCodecHeader)
            byte[] sha = e.Sha1 is { Length: 20 }
                ? e.Sha1
                : System.Security.Cryptography.SHA1.HashData(payload);
            w.WriteBytes(PadSha1(sha));
            w.Write7BitEncodedInt(originalSize);
            w.WriteByte(CompressionLevelFastest);
            w.Write7BitEncodedInt(payload.Length);
            w.WriteBytes(payload);
            ebxWritten++;
        }
        long afterEbx = stream.Position;
        stream.Position = posEbx;
        WriteUInt24(w, (uint)ebxWritten);
        stream.Position = afterEbx;
    }

    /// <summary>
    /// Prefer CAS-compressed bytes. FET always CAS-decompresses ModifiedEntry.Data when opening assets.
    /// Uncompressed RIFF fails with "Invalid guard bits in codec header: expected 7, got 0x0".
    /// </summary>
    private static byte[] ProjectPayload(FifamodResource r)
    {
        if (r.CompressedData is { Length: > 0 })
            return r.CompressedData;
        // Last resort: wrap raw data is not valid CAS — return empty so we skip rather than poison the project
        if (r.Data is { Length: >= 8 } && LooksLikeCasHeader(r.Data))
            return r.Data;
        return Array.Empty<byte>();
    }

    private static bool LooksLikeCasHeader(byte[] data)
    {
        // u32be size, u32be with guard nibble 7 at bits 20-23
        if (data.Length < 8)
            return false;
        uint word1 = (uint)((data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7]);
        uint guard = (word1 >> 20) & 0xF;
        return guard == 7;
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
