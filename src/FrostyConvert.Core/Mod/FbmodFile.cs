namespace FrostyConvert.Core.Mod;

public enum FbmodFormatKind
{
    /// <summary>Binary format (Frosty mod versions 1–5).</summary>
    Binary,

    /// <summary>Legacy DbObject header with external <c>*_NN.archive</c> sidecars.</summary>
    Legacy,
}

public sealed class FbmodFile
{
    public required string Path { get; init; }
    public required FbmodFormatKind Format { get; init; }
    public uint Version { get; init; }
    public string ProfileName { get; init; } = "";
    public int GameVersion { get; init; }
    public FbmodDetails? Details { get; init; }
    public IReadOnlyList<FbmodResource> Resources { get; init; } = Array.Empty<FbmodResource>();
    public long DataOffset { get; init; }
    public int DataCount { get; init; }

    public IEnumerable<FbmodResource> EnumerateNonEmbedded() =>
        Resources.Where(r => r.Type != ModResourceType.Embedded);

    public IEnumerable<FbmodResource> EnumerateByType(ModResourceType type) =>
        Resources.Where(r => r.Type == type);
}
