using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FrostyConvert.Core.Mod;

namespace FrostyConvert.Core.Inspect;

public sealed class InspectReport
{
    public required string FilePath { get; init; }
    public required string Format { get; init; }
    public uint Version { get; init; }
    public string ProfileName { get; init; } = "";
    public int GameVersion { get; init; }
    public FbmodDetails? Details { get; init; }
    public int ResourceCount { get; init; }
    public int DataEntryCount { get; init; }
    public Dictionary<string, int> ResourceCountsByType { get; init; } = new();
    public int HandlerResourceCount { get; set; }
    public int AddedAssetCount { get; set; }
    public List<InspectResourceRow> Resources { get; init; } = new();
    public List<string> Notes { get; init; } = new();

    public static InspectReport FromMod(FbmodFile mod)
    {
        var report = new InspectReport
        {
            FilePath = mod.Path,
            Format = mod.Format.ToString(),
            Version = mod.Version,
            ProfileName = mod.ProfileName,
            GameVersion = mod.GameVersion,
            Details = mod.Details,
            ResourceCount = mod.Resources.Count,
            DataEntryCount = mod.DataCount,
            HandlerResourceCount = mod.Resources.Count(r => r.HasHandler),
            AddedAssetCount = mod.Resources.Count(r => r.IsAdded),
        };

        if (mod.Format == FbmodFormatKind.Legacy)
        {
            report.Notes.Add(
                "Legacy DbObject .fbmod detected. Full legacy parse (+ *.archive sidecars) is not implemented in inspect yet.");
            return report;
        }

        foreach (var g in mod.Resources.GroupBy(r => r.Type.ToString()))
            report.ResourceCountsByType[g.Key] = g.Count();

        int index = 0;
        foreach (var r in mod.Resources)
        {
            report.Resources.Add(new InspectResourceRow
            {
                Index = index++,
                Type = r.Type.ToString(),
                Name = r.Name,
                ResourceIndex = r.ResourceIndex,
                Size = r.Size,
                DataBytes = r.Data?.Length ?? 0,
                Flags = r.Flags,
                IsAdded = r.IsAdded,
                HandlerHash = r.HandlerHash,
                HandlerHashHex = r.HasHandler ? $"0x{r.HandlerHash:X8}" : "",
                UserData = r.UserData,
                AddedBundleCount = r.AddedBundleHashes.Count,
                AddedBundleHashes = r.AddedBundleHashes.Select(h => $"0x{h:X8}").ToList(),
                ResType = r.Type == ModResourceType.Res ? r.ResType : null,
                ResRid = r.Type == ModResourceType.Res ? r.ResRid : null,
                Sha1Hex = r.Sha1 is { Length: 20 } ? global::System.Convert.ToHexString(r.Sha1).ToLowerInvariant() : null,
            });

            if (r.HasHandler && r.HandlerHash == FbmodConstants.LegacyHandlerHash)
                report.Notes.Add($"Resource '{r.Name}' uses Legacy collector handler (0xBD9BFB65).");
        }

        if (mod.Version > FbmodConstants.MaxBinaryVersion)
        {
            report.Notes.Add(
                $"Mod version {mod.Version} is newer than documented Frosty 1.0.6.3 (v{FbmodConstants.MaxBinaryVersion}). Parse may be incomplete.");
        }

        return report;
    }

    public string ToJson(bool writeIndented = true)
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = writeIndented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"File:          {FilePath}");
        sb.AppendLine($"Format:        {Format}");
        sb.AppendLine($"Version:       {Version}");
        sb.AppendLine($"Profile:       {ProfileName}");
        sb.AppendLine($"GameVersion:   {GameVersion}");
        if (Details is not null)
        {
            sb.AppendLine($"Title:         {Details.Title}");
            sb.AppendLine($"Author:        {Details.Author}");
            sb.AppendLine($"Category:      {Details.Category}");
            sb.AppendLine($"ModVersion:    {Details.Version}");
            if (!string.IsNullOrWhiteSpace(Details.Link))
                sb.AppendLine($"Link:          {Details.Link}");
            if (!string.IsNullOrWhiteSpace(Details.Description))
                sb.AppendLine($"Description:   {Truncate(Details.Description, 120)}");
        }

        sb.AppendLine($"Resources:     {ResourceCount} (data entries: {DataEntryCount})");
        sb.AppendLine($"Handlers:      {HandlerResourceCount}");
        sb.AppendLine($"Added assets:  {AddedAssetCount}");
        if (ResourceCountsByType.Count > 0)
        {
            sb.AppendLine("By type:");
            foreach (var kv in ResourceCountsByType.OrderBy(k => k.Key))
                sb.AppendLine($"  {kv.Key,-12} {kv.Value}");
        }

        if (Notes.Count > 0)
        {
            sb.AppendLine("Notes:");
            foreach (var n in Notes.Distinct())
                sb.AppendLine($"  - {n}");
        }

        sb.AppendLine();
        sb.AppendLine("Resources:");
        foreach (var r in Resources)
        {
            string handler = r.HandlerHash != 0 ? $" handler={r.HandlerHashHex}" : "";
            string added = r.IsAdded ? " [added]" : "";
            sb.AppendLine(
                $"  [{r.Index,3}] {r.Type,-8} {r.Name}  idx={r.ResourceIndex} size={r.Size} data={r.DataBytes}{handler}{added}");
        }

        return sb.ToString();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}

public sealed class InspectResourceRow
{
    public int Index { get; init; }
    public required string Type { get; init; }
    public required string Name { get; init; }
    public int ResourceIndex { get; init; }
    public long Size { get; init; }
    public int DataBytes { get; init; }
    public byte Flags { get; init; }
    public bool IsAdded { get; init; }
    public int HandlerHash { get; init; }
    public string HandlerHashHex { get; init; } = "";
    public string UserData { get; init; } = "";
    public int AddedBundleCount { get; init; }
    public List<string> AddedBundleHashes { get; init; } = new();
    public uint? ResType { get; init; }
    public ulong? ResRid { get; init; }
    public string? Sha1Hex { get; init; }
}
