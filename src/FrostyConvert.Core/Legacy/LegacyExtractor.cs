using FrostyConvert.Core.Compression;
using FrostyConvert.Core.FifaMod;

namespace FrostyConvert.Core.Legacy;

public sealed class LegacyExtractResult
{
    public int Written { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public List<string> ErrorMessages { get; } = new();
}

/// <summary>Writes named legacy chunk payloads from a .fifamod to disk.</summary>
public static class LegacyExtractor
{
    public static LegacyExtractResult Extract(FifamodFile mod, string outputDirectory, string? pathFilter = null)
    {
        Directory.CreateDirectory(outputDirectory);
        var result = new LegacyExtractResult();

        foreach (var r in mod.Resources.Where(x => x.Kind == FifamodResourceKind.Chunk))
        {
            if (string.IsNullOrEmpty(r.LegacyFileName))
            {
                result.Skipped++;
                continue;
            }

            string rel = r.LegacyFileName.Replace('\\', '/').TrimStart('/');
            if (pathFilter is { Length: > 0 } &&
                rel.IndexOf(pathFilter, StringComparison.OrdinalIgnoreCase) < 0)
            {
                result.Skipped++;
                continue;
            }

            try
            {
                byte[]? data = r.Data;
                if (data is null || data.Length == 0)
                {
                    if (r.CompressedData is { Length: > 0 })
                        data = CasBlockDecompressor.Decompress(r.CompressedData);
                }

                if (data is null || data.Length == 0)
                {
                    result.Skipped++;
                    continue;
                }

                string dest = Path.Combine(outputDirectory, rel.Replace('/', Path.DirectorySeparatorChar));
                string? dir = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllBytes(dest, data);
                result.Written++;
            }
            catch (Exception ex)
            {
                result.Errors++;
                if (result.ErrorMessages.Count < 20)
                    result.ErrorMessages.Add($"{r.LegacyFileName}: {ex.Message}");
            }
        }

        return result;
    }
}
