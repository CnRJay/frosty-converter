namespace FrostyConvert.Core.Mod;

public sealed class FbmodDetails
{
    public required string Title { get; init; }
    public required string Author { get; init; }
    public required string Category { get; init; }
    public required string Version { get; init; }
    public required string Description { get; init; }
    public string Link { get; init; } = "";
}
