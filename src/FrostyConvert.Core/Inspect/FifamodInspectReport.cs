using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrostyConvert.Core.FifaMod;
using SysConvert = System.Convert;

namespace FrostyConvert.Core.Inspect;

public sealed class FifamodInspectReport
{
    public required string FilePath { get; init; }
    public string Format { get; init; } = "Fifamod";
    public byte ModVersion { get; init; }
    public string GameName { get; init; } = "";
    public uint GameVersion { get; init; }
    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
    public string Version { get; init; } = "";
    public string Description { get; init; } = "";
    public long DataBaseOffset { get; init; }
    public int EbxCount { get; init; }
    public int ResCount { get; init; }
    public int ChunkCount { get; init; }
    public int ResourceCount { get; init; }
    public int Sha1MatchCount { get; set; }
    public int DecompressedCount { get; set; }
    public int RiffEbxCount { get; set; }
    public int DecompressErrorCount { get; set; }
    public List<FifamodInspectResourceRow> Resources { get; init; } = new();
    public List<string> Notes { get; init; } = new();

    public static FifamodInspectReport FromMod(FifamodFile mod)
    {
        var report = new FifamodInspectReport
        {
            FilePath = mod.Path,
            ModVersion = mod.ModVersion,
            GameName = mod.GameName,
            GameVersion = mod.GameVersion,
            Title = mod.Details.Title,
            Author = mod.Details.Author,
            Version = mod.Details.Version,
            Description = mod.Details.Description,
            DataBaseOffset = mod.DataBaseOffset,
            EbxCount = mod.EbxCount,
            ResCount = mod.ResCount,
            ChunkCount = mod.ChunkCount,
            ResourceCount = mod.Resources.Count,
            Notes = mod.Notes.ToList(),
        };

        int index = 0;
        foreach (var r in mod.Resources)
        {
            bool isRiff = r.Data is { Length: >= 4 }
                          && r.Data[0] == (byte)'R'
                          && r.Data[1] == (byte)'I'
                          && r.Data[2] == (byte)'F'
                          && r.Data[3] == (byte)'F';

            if (r.Sha1MatchesCompressed)
                report.Sha1MatchCount++;
            if (r.Data is { Length: > 0 } && r.DecompressError is null)
                report.DecompressedCount++;
            if (isRiff)
                report.RiffEbxCount++;
            if (r.DecompressError is not null)
                report.DecompressErrorCount++;

            report.Resources.Add(new FifamodInspectResourceRow
            {
                Index = index++,
                Kind = r.Kind.ToString(),
                Name = r.Name,
                DataOffset = r.FileOffset,
                CompressedSize = r.CompressedSize,
                UncompressedSize = r.UncompressedSize,
                DataBytes = r.Data?.Length ?? 0,
                Sha1Hex = r.Sha1 is { Length: 20 }
                    ? SysConvert.ToHexString(r.Sha1).ToLowerInvariant()
                    : null,
                Sha1Matches = r.Sha1MatchesCompressed,
                IsRiffEbx = isRiff,
                DecompressError = r.DecompressError,
            });
        }

        if (report.DecompressErrorCount > 0)
            report.Notes.Add($"{report.DecompressErrorCount} resource(s) failed to decompress (need Oodle for CAS type 0x19).");

        report.Notes.Add(
            "FIFA Editor Tool has no plugin API. Convert to .fifaproject and open it in FET with FC26 loaded: " +
            "fbmod2project mod.fifamod -o recovered.fifaproject");

        return report;
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"File:       {FilePath}");
        sb.AppendLine($"Format:     {Format} (FETM)");
        sb.AppendLine($"ModVer:     {ModVersion}");
        sb.AppendLine($"Game:       {GameName}  head/patch={GameVersion}");
        sb.AppendLine($"Title:      {Title}");
        sb.AppendLine($"Author:     {Author}");
        sb.AppendLine($"Version:    {Version}");
        sb.AppendLine($"DataBase:   0x{DataBaseOffset:X}");
        sb.AppendLine($"Resources:  {ResourceCount} (ebx={EbxCount} res={ResCount} chunks={ChunkCount})");
        sb.AppendLine($"SHA-1 ok:   {Sha1MatchCount}/{ResourceCount}");
        sb.AppendLine($"Decomp:     {DecompressedCount}  RIFF-EBX={RiffEbxCount}  errors={DecompressErrorCount}");

        int show = Math.Min(Resources.Count, 40);
        sb.AppendLine();
        sb.AppendLine($"Resources (first {show}):");
        for (int i = 0; i < show; i++)
        {
            var r = Resources[i];
            string flags = r.IsRiffEbx ? "RIFF" : (r.DecompressError is not null ? "ERR" : r.Kind);
            string sha = r.Sha1Matches ? "sha+" : "sha-";
            sb.AppendLine(
                $"  [{r.Index,4}] {flags,-5} {sha} csz={r.CompressedSize,6} usz={r.UncompressedSize,6}  {r.Name}");
            if (r.DecompressError is not null)
                sb.AppendLine($"         ! {r.DecompressError}");
        }

        if (Resources.Count > show)
            sb.AppendLine($"  ... {Resources.Count - show} more");

        if (Notes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Notes:");
            foreach (var n in Notes)
                sb.AppendLine($"  - {n}");
        }

        return sb.ToString();
    }

    public string ToJson() =>
        JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
}

public sealed class FifamodInspectResourceRow
{
    public int Index { get; init; }
    public string Kind { get; init; } = "";
    public string Name { get; init; } = "";
    public long DataOffset { get; init; }
    public int CompressedSize { get; init; }
    public int UncompressedSize { get; init; }
    public int DataBytes { get; init; }
    public string? Sha1Hex { get; init; }
    public bool Sha1Matches { get; init; }
    public bool IsRiffEbx { get; init; }
    public string? DecompressError { get; init; }
}
