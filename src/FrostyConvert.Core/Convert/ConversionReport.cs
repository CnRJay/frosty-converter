using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrostyConvert.Core.Convert;

public sealed class ConversionReport
{
    public required string InputPath { get; init; }
    public string? OutputPath { get; set; }
    public string ProfileName { get; set; } = "";
    public int GameVersion { get; set; }
    public bool Success { get; set; }
    public int EbxCount { get; set; }
    public int ResCount { get; set; }
    public int ChunkCount { get; set; }
    public int BundleCount { get; set; }
    public int HandlerCount { get; set; }
    public int LegacyCount { get; set; }
    public List<string> Warnings { get; } = new();
    public List<string> Errors { get; } = new();

    public string ToJson(bool indented = true) =>
        JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = indented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        });

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Success ? "Conversion: OK" : "Conversion: FAILED");
        sb.AppendLine($"Input:    {InputPath}");
        if (OutputPath is not null)
            sb.AppendLine($"Output:   {OutputPath}");
        sb.AppendLine($"Profile:  {ProfileName}");
        sb.AppendLine($"GameVer:  {GameVersion}");
        sb.AppendLine($"Ebx: {EbxCount}  Res: {ResCount}  Chunk: {ChunkCount}  Bundle: {BundleCount}");
        sb.AppendLine($"Handlers: {HandlerCount}  Legacy: {LegacyCount}");
        if (Warnings.Count > 0)
        {
            sb.AppendLine("Warnings:");
            foreach (var w in Warnings)
                sb.AppendLine($"  - {w}");
        }
        if (Errors.Count > 0)
        {
            sb.AppendLine("Errors:");
            foreach (var e in Errors)
                sb.AppendLine($"  - {e}");
        }
        return sb.ToString();
    }
}
