using System.Text.RegularExpressions;
using FrostyConvert.Core.Legacy;

namespace FrostyConvert.Core.FifaMod;

/// <summary>
/// Offline heuristics so recovered <c>.fifaproject</c> files register assets that
/// <c>ModWriter</c> never marks with <c>IsAdded</c>.
/// <para>
/// FET's project loader looks up non-added EBX/Res/Chunks in the live TOC. Missing entries
/// become orphan modifications (warning: "doesn't exist") and never appear in Data Explorer.
/// Paths that cannot exist in the base TOC are force-marked <c>IsAdded</c>:
/// head variations (<c>var_N</c>, N≥1), created-player folders (numeric face id), and
/// created-team kit folders (numeric team id), plus exclusive chunks those RES reference.
/// </para>
/// </summary>
public static class FifamodProjectAddedRecovery
{
    /// <summary>FC26 MeshSet RES type (same as mesh <c>*_mesh</c> payloads).</summary>
    public const uint MeshSetResType = 0x49B156D4;

    /// <summary>
    /// Matches <c>.../var_N/...</c> or <c>.../var_N_starhead_brt</c> for N ≥ 1.
    /// Named-player <c>var_0</c> is usually a TOC hit / modification.
    /// </summary>
    private static readonly Regex AddedHeadVariationPath = new(
        @"/var_([1-9]\d*)(/|_)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Created / custom faces use a pure-numeric folder:
    /// <c>…/player/player_50000/50247/var_0/…</c> (no firstname_lastname_id segment).
    /// EA base faces use a named segment; those stay non-added so live TOC mods still apply.
    /// </summary>
    private static readonly Regex CreatedPlayerNumericFolder = new(
        @"/player/player_\d+/(\d+)(/|_)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Created teams use a pure-numeric kit folder:
    /// <c>…/kit/kit_150000/150039/home_0_0/jersey_…</c> (no club_name_id segment).
    /// </summary>
    private static readonly Regex CreatedKitNumericFolder = new(
        @"/kit/kit_\d+/(\d+)(/|_)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool IsAddedHeadVariationPath(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        return AddedHeadVariationPath.IsMatch(name.Replace('\\', '/'));
    }

    /// <summary>True for created-player paths with a numeric face-id folder (any <c>var_N</c>).</summary>
    public static bool IsCreatedPlayerAssetPath(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        return CreatedPlayerNumericFolder.IsMatch(name.Replace('\\', '/'));
    }

    /// <summary>True for created-team kit paths with a numeric team-id folder.</summary>
    public static bool IsCreatedKitAssetPath(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        return CreatedKitNumericFolder.IsMatch(name.Replace('\\', '/'));
    }

    /// <summary>
    /// Combined offline force-<c>IsAdded</c> path heuristic (head var_N≥1, created player, created kit).
    /// </summary>
    public static bool IsForceAddedAssetPath(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        string n = name.Replace('\\', '/');
        return AddedHeadVariationPath.IsMatch(n)
               || CreatedPlayerNumericFolder.IsMatch(n)
               || CreatedKitNumericFolder.IsMatch(n);
    }

    /// <summary>
    /// Chunk GUIDs referenced only by force-added RES payloads.
    /// Excludes GUIDs also referenced by other RES in the mod (shared / base replacements).
    /// </summary>
    public static HashSet<Guid> CollectForceAddedChunkIds(IReadOnlyList<FifamodResource> resources)
    {
        var knownChunks = new HashSet<Guid>();
        foreach (var r in resources)
        {
            if (r.Kind == FifamodResourceKind.Chunk && r.ChunkId != Guid.Empty)
                knownChunks.Add(r.ChunkId);
        }

        var fromAdded = new HashSet<Guid>();
        var fromOther = new HashSet<Guid>();

        foreach (var r in resources)
        {
            if (r.Kind != FifamodResourceKind.Res)
                continue;
            if (r.Data is not { Length: >= 16 })
                continue;

            bool force = IsForceAddedAssetPath(r.Name);
            CollectChunkGuidsFromRes(r, knownChunks, force ? fromAdded : fromOther);
        }

        fromAdded.ExceptWith(fromOther);
        return fromAdded;
    }

    /// <summary>Map of res name → res type for same-name EBX type guessing.</summary>
    public static Dictionary<string, uint> BuildResTypeByName(IReadOnlyList<FifamodResource> resources)
    {
        var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in resources)
        {
            if (r.Kind != FifamodResourceKind.Res || string.IsNullOrEmpty(r.Name))
                continue;
            map[r.Name] = r.ResType;
        }
        return map;
    }

    public static bool ShouldForceAdded(FifamodResource r, HashSet<Guid> forceAddedChunks)
    {
        if (r.IsAdded)
            return true;

        return r.Kind switch
        {
            FifamodResourceKind.Ebx or FifamodResourceKind.Res
                => IsForceAddedAssetPath(r.Name),
            FifamodResourceKind.Chunk
                => r.ChunkId != Guid.Empty && forceAddedChunks.Contains(r.ChunkId),
            _ => false,
        };
    }

    /// <summary>
    /// Best-effort type name for project <c>IsAdded</c> EBX rows.
    /// Prefer same-name RES type: Texture / MeshSet. Hair/head roots without RES are
    /// <c>ObjectBlueprint</c> (or cloth variant) — labeling them <c>SkinnedMeshAsset</c>
    /// makes FET open the mesh editor and crash with null RES entry.
    /// </summary>
    public static string GuessEbxTypeName(string? name, uint? matchingResType = null)
    {
        if (string.IsNullOrEmpty(name))
            return "ObjectBlueprint";

        string n = name.Replace('\\', '/').ToLowerInvariant();

        if (matchingResType == TextureResBuilder.TextureResType)
            return "TextureAsset";
        if (matchingResType == MeshSetResType)
            return "SkinnedMeshAsset";

        if (n.Contains("meshvariationdb", StringComparison.Ordinal))
            return "MeshVariationDatabase";
        if (n.EndsWith("_blueprint", StringComparison.Ordinal) || n.Contains("_blueprint/", StringComparison.Ordinal))
            return "ObjectBlueprint";
        if (n.EndsWith("_mesh", StringComparison.Ordinal))
            return "SkinnedMeshAsset";
        if (n.Contains("clothwrapping", StringComparison.Ordinal))
            return "ClothWrappingAsset";
        if (n.EndsWith("_brt", StringComparison.Ordinal) || n.Contains("_starhead_brt", StringComparison.Ordinal))
            return "ObjectBlueprint";
        if (Regex.IsMatch(n, @"_(color|normal|coeff|specmask|ambient|roughness|opacity|cavity)$"))
            return "TextureAsset";

        // hair/head/haircap/mouthbag/hair_accessory roots: ObjectBlueprint that points at *_mesh.
        // Cloth hair may be ClothObjectBlueprint in-game; ObjectBlueprint still opens property grid.
        if (Regex.IsMatch(n, @"/(head|hair|haircap|face|mouthbag|hair_accessory)_\d+_\d+_\d+$"))
            return "ObjectBlueprint";

        return "ObjectBlueprint";
    }

    /// <summary>
    /// Extract partition (file) GUID from RIFF EBX <c>EBXD</c> payload.
    /// Observed FC26 layout: 12 zero pad bytes, then 16-byte GUID (not 16 zeros).
    /// </summary>
    public static Guid? TryExtractRiffEbxGuid(byte[]? data)
    {
        if (data is null || data.Length < 28)
            return null;
        if (data[0] != (byte)'R' || data[1] != (byte)'I' || data[2] != (byte)'F' || data[3] != (byte)'F')
            return null;

        int pos = 12;
        while (pos + 8 <= data.Length)
        {
            bool isEbxd = data[pos] == (byte)'E'
                          && data[pos + 1] == (byte)'B'
                          && data[pos + 2] == (byte)'X'
                          && data[pos + 3] == (byte)'D';
            int size = BitConverter.ToInt32(data, pos + 4);
            if (size < 0 || pos + 8 > data.Length)
                break;

            if (isEbxd)
            {
                int payload = pos + 8;
                int end = Math.Min(data.Length, payload + Math.Max(0, size));
                // Prefer 12-byte zero pad (common), then 16-byte pad, then raw start.
                if (payload + 28 <= end && IsZero(data, payload, 12))
                {
                    var g = new Guid(data.AsSpan(payload + 12, 16));
                    if (g != Guid.Empty)
                        return g;
                }
                if (payload + 32 <= end && IsZero(data, payload, 16))
                {
                    var g = new Guid(data.AsSpan(payload + 16, 16));
                    if (g != Guid.Empty)
                        return g;
                }
                if (payload + 16 <= end)
                {
                    var g = new Guid(data.AsSpan(payload, 16));
                    if (g != Guid.Empty)
                        return g;
                }
                return null;
            }

            pos += 8 + size;
            if ((size & 1) != 0)
                pos++;
        }

        return null;
    }

    private static bool IsZero(byte[] data, int offset, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (data[offset + i] != 0)
                return false;
        }
        return true;
    }

    private static void CollectChunkGuidsFromRes(
        FifamodResource r,
        HashSet<Guid> knownChunks,
        HashSet<Guid> into)
    {
        byte[] d = r.Data!;

        if (r.ResType == TextureResBuilder.TextureResType
            && d.Length >= TextureResBuilder.ChunkIdOffset + 16)
        {
            var g = new Guid(d.AsSpan(TextureResBuilder.ChunkIdOffset, 16));
            if (g != Guid.Empty && knownChunks.Contains(g))
                into.Add(g);
            return;
        }

        // Mesh / other RES: scan for GUIDs that exist as chunks in this mod.
        // 1-byte step catches unaligned LOD chunk IDs in MeshSet blobs.
        for (int i = 0; i + 16 <= d.Length; i++)
        {
            var g = new Guid(d.AsSpan(i, 16));
            if (g != Guid.Empty && knownChunks.Contains(g))
                into.Add(g);
        }
    }
}
