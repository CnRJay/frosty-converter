using System.Security.Cryptography;
using FrostyConvert.Core.Compression;
using FrostyConvert.Core.FifaMod;

namespace FrostyConvert.Core.Legacy;

public sealed class TexturePromoteOptions
{
    /// <summary>Asset name prefix for legacy-sourced textures, e.g. content/ui/legacy</summary>
    public string NamePrefix { get; init; } = "content/ui/legacy";

    /// <summary>
    /// Only promote legacy paths containing this substring (case-insensitive).
    /// Empty = all detectable DDS (recommended for full recovery).
    /// </summary>
    public string PathFilter { get; init; } = "";

    /// <summary>Max textures to promote (0 = unlimited).</summary>
    public int MaxCount { get; init; }

    /// <summary>Also emit TextureAsset EBX for existing content/ RES textures in the mod.</summary>
    public bool WrapExistingResTextures { get; init; } = true;

    public byte[]? TemplateResData { get; init; }
    public byte[]? TemplateEbxCas { get; init; }
    public int TemplateEbxOriginalSize { get; init; }
}

public sealed class TexturePromoteResult
{
    public List<FifamodResource> Resources { get; } = new();
    public int CandidateCount { get; set; }
    public int PromotedCount { get; set; }
    public int WrappedExistingRes { get; set; }
    public int SkippedNotDds { get; set; }
    public int SkippedFilter { get; set; }
    public int SkippedErrors { get; set; }
    public List<string> Errors { get; } = new();
    public List<string> SampleNames { get; } = new();
    public Dictionary<string, int> LegacyExtHistogram { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> NonTextureLegacyNotes { get; } = new();
}

/// <summary>
/// Promotes legacy DDS (and existing Texture RES) into Data Explorer TextureAsset EBX.
/// </summary>
public static class TextureAssetPromoter
{
    public static TexturePromoteResult Promote(FifamodFile mod, TexturePromoteOptions? options = null)
    {
        options ??= new TexturePromoteOptions();
        var result = new TexturePromoteResult();

        // Histogram of all named legacy files for reporting
        foreach (var leg in mod.Resources.Where(r => r.Kind == FifamodResourceKind.Chunk && !string.IsNullOrEmpty(r.LegacyFileName)))
        {
            string n = leg.LegacyFileName!;
            string ext = Path.GetExtension(n);
            if (string.IsNullOrEmpty(ext))
                ext = "(none)";
            result.LegacyExtHistogram[ext] = result.LegacyExtHistogram.GetValueOrDefault(ext) + 1;
        }

        if (result.LegacyExtHistogram.GetValueOrDefault(".big") > 0)
        {
            result.NonTextureLegacyNotes.Add(
                $"{result.LegacyExtHistogram[".big"]} .big archive(s) = Scaleform/Apt UI packs — " +
                "edit in Legacy Explorer (Big File editor). Not TextureAssets.");
        }
        if (result.LegacyExtHistogram.GetValueOrDefault(".ttf") > 0
            || result.LegacyExtHistogram.GetValueOrDefault(".otf") > 0)
        {
            int fonts = result.LegacyExtHistogram.GetValueOrDefault(".ttf")
                        + result.LegacyExtHistogram.GetValueOrDefault(".otf");
            result.NonTextureLegacyNotes.Add(
                $"{fonts} font file(s) — Legacy Explorer only (no freestanding font EBX type).");
        }
        foreach (var ext in new[] { ".xml", ".txt" })
        {
            if (result.LegacyExtHistogram.GetValueOrDefault(ext) > 0)
                result.NonTextureLegacyNotes.Add(
                    $"{result.LegacyExtHistogram[ext]} {ext} — Legacy Explorer only.");
        }

        byte[]? templateRes = options.TemplateResData;
        if (templateRes is null)
        {
            var tmpl = TextureResBuilder.FindTextureResTemplate(mod.Resources);
            if (tmpl is not null)
            {
                templateRes = tmpl.Data is { Length: > 0 }
                    ? tmpl.Data
                    : tmpl.CompressedData is { Length: > 0 }
                        ? CasBlockDecompressor.Decompress(tmpl.CompressedData)
                        : null;
            }
        }

        if (templateRes is null || templateRes.Length < 64)
        {
            result.Errors.Add(
                "No Texture RES template found in the mod. Need a ResType Texture (0x6BDE20BA) sample.");
            return result;
        }

        byte[] ebxCas = options.TemplateEbxCas is { Length: > 0 }
            ? options.TemplateEbxCas
            : CasBlockCompressor.Compress(System.Text.Encoding.UTF8.GetBytes("TextureAsset"));
        int ebxOriginal = options.TemplateEbxOriginalSize > 0
            ? options.TemplateEbxOriginalSize
            : 12;

        string prefix = options.NamePrefix.Trim().TrimEnd('/').Replace('\\', '/');
        string filter = options.PathFilter ?? "";

        // All chunks that look like DDS (extension or magic)
        var candidates = mod.Resources
            .Where(r => r.Kind == FifamodResourceKind.Chunk)
            .Where(r => LooksLikeDdsCandidate(r))
            .ToList();
        result.CandidateCount = candidates.Count;

        var usedRids = new HashSet<ulong>();
        foreach (var r in mod.Resources.Where(x => x.Kind == FifamodResourceKind.Res && x.ResRid != 0))
            usedRids.Add(r.ResRid);

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var leg in candidates)
        {
            if (options.MaxCount > 0 && result.PromotedCount >= options.MaxCount)
                break;

            string legacyPath = (leg.LegacyFileName ?? leg.Name).Replace('\\', '/');
            if (filter.Length > 0 &&
                legacyPath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                result.SkippedFilter++;
                continue;
            }

            try
            {
                byte[]? ddsBytes = GetDecompressed(leg);
                if (ddsBytes is null || !DdsHeader.TryParse(ddsBytes, out var dds))
                {
                    result.SkippedNotDds++;
                    continue;
                }

                string assetName = MapLegacyPathToAssetName(legacyPath, prefix);
                if (!usedNames.Add(assetName))
                {
                    // disambiguate
                    assetName += "_" + leg.ChunkId.ToString("N")[..8];
                    usedNames.Add(assetName);
                }

                PromoteOneDds(result, templateRes, ebxCas, ebxOriginal, usedRids,
                    assetName, ddsBytes, dds, isAdded: true);
            }
            catch (Exception ex)
            {
                result.SkippedErrors++;
                if (result.Errors.Count < 20)
                    result.Errors.Add($"{legacyPath}: {ex.Message}");
            }
        }

        // Existing content/ Texture RES (e.g. wipe colors) — wrap with TextureAsset EBX
        if (options.WrapExistingResTextures)
        {
            foreach (var res in mod.Resources.Where(r =>
                         r.Kind == FifamodResourceKind.Res
                         && (r.ResType == TextureResBuilder.TextureResType || r.UncompressedSize is >= 100 and <= 256)))
            {
                if (usedNames.Contains(res.Name))
                    continue;
                try
                {
                    // EBX only — RES+chunk already in project from original mod write
                    result.Resources.Add(new FifamodResource
                    {
                        Name = res.Name,
                        Kind = FifamodResourceKind.Ebx,
                        EbxFlags = FifamodEbxFlags.IsDirectlyModified, // existing asset, not added
                        EbxTypeName = "TextureAsset",
                        EbxGuid = Guid.NewGuid(),
                        CompressedData = ebxCas,
                        CompressedSize = ebxCas.Length,
                        UncompressedSize = ebxOriginal,
                        Sha1 = SHA1.HashData(ebxCas),
                    });
                    usedNames.Add(res.Name);
                    result.WrappedExistingRes++;
                    result.PromotedCount++;
                    if (result.SampleNames.Count < 12)
                        result.SampleNames.Add(res.Name + " (existing res)");
                }
                catch (Exception ex)
                {
                    result.SkippedErrors++;
                    if (result.Errors.Count < 20)
                        result.Errors.Add($"wrap {res.Name}: {ex.Message}");
                }
            }
        }

        return result;
    }

    private static bool LooksLikeDdsCandidate(FifamodResource r)
    {
        if (!string.IsNullOrEmpty(r.LegacyFileName)
            && r.LegacyFileName.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            return true;

        // extensionless / misnamed — sniff magic after decompress when possible
        byte[]? data = r.Data;
        if (data is null && r.CompressedData is { Length: >= 12 })
        {
            // cheap: only accept if we already have decompressed data; full sniff in loop
            return string.IsNullOrEmpty(Path.GetExtension(r.LegacyFileName ?? ""));
        }

        if (data is { Length: >= 4 } && BitConverter.ToUInt32(data, 0) == DdsHeader.Magic)
            return true;

        return false;
    }

    private static byte[]? GetDecompressed(FifamodResource r)
    {
        if (r.Data is { Length: > 0 })
            return r.Data;
        if (r.CompressedData is { Length: > 0 })
            return CasBlockDecompressor.Decompress(r.CompressedData);
        return null;
    }

    private static void PromoteOneDds(
        TexturePromoteResult result,
        byte[] templateRes,
        byte[] ebxCas,
        int ebxOriginal,
        HashSet<ulong> usedRids,
        string assetName,
        byte[] ddsBytes,
        DdsHeader dds,
        bool isAdded)
    {
        Guid chunkId = Guid.NewGuid();
        Guid ebxGuid = Guid.NewGuid();

        int pixelOffset = dds.DataOffset;
        if (pixelOffset < 0 || pixelOffset >= ddsBytes.Length)
            pixelOffset = 128;
        int pixelLen = ddsBytes.Length - pixelOffset;
        if (pixelLen <= 0)
        {
            result.SkippedNotDds++;
            return;
        }

        var pixelData = new byte[pixelLen];
        Buffer.BlockCopy(ddsBytes, pixelOffset, pixelData, 0, pixelLen);

        byte[] resData = TextureResBuilder.BuildFromTemplate(templateRes, chunkId, dds, assetName);

        int expectedBytes = TextureResBuilder.TotalMipDataSize(resData);
        if (expectedBytes > 0 && expectedBytes != pixelData.Length)
        {
            if (expectedBytes < pixelData.Length)
            {
                var trimmed = new byte[expectedBytes];
                Buffer.BlockCopy(pixelData, 0, trimmed, 0, expectedBytes);
                pixelData = trimmed;
            }
            else
            {
                var padded = new byte[expectedBytes];
                Buffer.BlockCopy(pixelData, 0, padded, 0, pixelData.Length);
                pixelData = padded;
            }
        }

        byte[] chunkCas = CasBlockCompressor.Compress(pixelData);
        byte[] resCas = CasBlockCompressor.Compress(resData);

        var chunkFlags = FifamodChunkFlags.HasLogicalSize;
        if (isAdded)
            chunkFlags |= FifamodChunkFlags.IsAdded;

        result.Resources.Add(new FifamodResource
        {
            Name = chunkId.ToString(),
            Kind = FifamodResourceKind.Chunk,
            ChunkId = chunkId,
            ChunkFlags = chunkFlags,
            LogicalSize = pixelData.Length,
            CompressedData = chunkCas,
            Data = pixelData,
            CompressedSize = chunkCas.Length,
            UncompressedSize = pixelData.Length,
            Sha1 = SHA1.HashData(chunkCas),
        });

        ulong resRid = AllocateResRid(assetName, usedRids);
        var resFlags = FifamodResFlags.IsDirectlyModified | FifamodResFlags.HasMeta;
        if (isAdded)
            resFlags |= FifamodResFlags.IsAdded;

        result.Resources.Add(new FifamodResource
        {
            Name = assetName,
            Kind = FifamodResourceKind.Res,
            ResFlags = resFlags,
            ResType = TextureResBuilder.TextureResType,
            ResRid = resRid,
            ResMeta = new byte[] { 0x0C, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            CompressedData = resCas,
            Data = resData,
            CompressedSize = resCas.Length,
            UncompressedSize = resData.Length,
            Sha1 = SHA1.HashData(resCas),
        });

        var ebxFlags = FifamodEbxFlags.IsDirectlyModified;
        if (isAdded)
            ebxFlags |= FifamodEbxFlags.IsAdded;

        result.Resources.Add(new FifamodResource
        {
            Name = assetName,
            Kind = FifamodResourceKind.Ebx,
            EbxFlags = ebxFlags,
            EbxTypeName = "TextureAsset",
            EbxGuid = ebxGuid,
            CompressedData = ebxCas,
            CompressedSize = ebxCas.Length,
            UncompressedSize = ebxOriginal,
            Sha1 = SHA1.HashData(ebxCas),
        });

        result.PromotedCount++;
        if (result.SampleNames.Count < 12)
            result.SampleNames.Add(assetName);
    }

    public static string MapLegacyPathToAssetName(string legacyPath, string prefix)
    {
        string p = legacyPath.Replace('\\', '/').TrimStart('/');
        if (p.StartsWith("data/ui/", StringComparison.OrdinalIgnoreCase))
            p = p["data/ui/".Length..];
        else if (p.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
            p = p["data/".Length..];

        if (p.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            p = p[..^4];

        // numeric-only / hash names
        if (p.All(c => char.IsDigit(c) || c is '_' or '-'))
            p = "misc/" + p;

        return $"{prefix.TrimEnd('/')}/{p}".ToLowerInvariant();
    }

    public static ulong AllocateResRid(string assetName, HashSet<ulong> used)
    {
        ulong rid = Fnv1a64(assetName);
        if (rid == 0)
            rid = 1;
        if (rid < 0x10000)
            rid |= 0xFC260000UL;

        while (!used.Add(rid))
            rid++;

        return rid;
    }

    private static ulong Fnv1a64(string s)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (char c in s.ToLowerInvariant())
        {
            hash ^= c;
            hash *= prime;
        }
        return hash;
    }
}
