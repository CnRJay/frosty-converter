using System.Text.RegularExpressions;
using FrostyConvert.Core.Legacy;

namespace FrostyConvert.Core.FifaMod;

/// <summary>
/// Offline heuristics so recovered <c>.fifaproject</c> files register assets that
/// <c>ModWriter</c> never marks with <c>IsAdded</c>, plus best-effort linked-asset graphs.
/// </summary>
public static class FifamodProjectAddedRecovery
{
    /// <summary>FC26 MeshSet RES type (same as mesh <c>*_mesh</c> payloads).</summary>
    public const uint MeshSetResType = 0x49B156D4;

    /// <summary>FET <c>Sdk.Managers.AssetType</c> values used in project linked-asset tables.</summary>
    public const byte LinkedAssetTypeEbx = 0;
    public const byte LinkedAssetTypeRes = 1;
    public const byte LinkedAssetTypeChunk = 2;

    /// <summary>
    /// Matches <c>.../var_N/...</c> or <c>.../var_N_starhead_brt</c> for N ≥ 1.
    /// </summary>
    private static readonly Regex AddedHeadVariationPath = new(
        @"/var_([1-9]\d*)(/|_)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Created / custom faces: pure-numeric folder under <c>player_XXXXX</c>.
    /// </summary>
    private static readonly Regex CreatedPlayerNumericFolder = new(
        @"/player/player_\d+/(\d+)(/|_)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Created teams: pure-numeric kit folder under <c>kit_XXXXX</c>.
    /// </summary>
    private static readonly Regex CreatedKitNumericFolder = new(
        @"/kit/kit_\d+/(\d+)(/|_)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Named EA-style player face at <c>var_0</c> (ORIGID replacements).
    /// </summary>
    private static readonly Regex NamedPlayerVar0Path = new(
        @"/player/player_\d+/[^/]*[A-Za-z][^/]*/var_0(/|_)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Strand-hair add-ons (often new even on named ORIGID faces).
    /// </summary>
    private static readonly Regex StrandHairAssetPath = new(
        @"strandbind_|strandhair|(^|/)strand_|_strand_starhead",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Pack-only content that is almost never a stock FC26 TOC hit.
    /// Includes worlds (created-team flags/scarves); exclusive-chunk policy avoids
    /// force-adding TOC-shared chunk GUIDs that also appear on non-force paths.
    /// </summary>
    private static readonly Regex PackOnlyCreatedPath = new(
        @"(^|/)kitnumber(/|_)"
        + @"|(^|/)jerseyfonts/"
        + @"|(^|/)warpcloth/.*/runtimedata/"
        + @"|(^|/)content/worlds/"
        + @"|(^|/)worlds/"
        + @"|(^|/)character/balls?/"
        + @"|(^|/)character/wardrobe/"
        + @"|(^|/)character/shoe/"
        + @"|(^|/)character/body/common/bodyupper_",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool IsAddedHeadVariationPath(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        return AddedHeadVariationPath.IsMatch(name.Replace('\\', '/'));
    }

    public static bool IsCreatedPlayerAssetPath(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        return CreatedPlayerNumericFolder.IsMatch(name.Replace('\\', '/'));
    }

    public static bool IsCreatedKitAssetPath(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        return CreatedKitNumericFolder.IsMatch(name.Replace('\\', '/'));
    }

    public static bool IsNamedPlayerVar0ModificationPath(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        string n = name.Replace('\\', '/');
        if (AddedHeadVariationPath.IsMatch(n))
            return false;
        if (CreatedPlayerNumericFolder.IsMatch(n))
            return false;
        return NamedPlayerVar0Path.IsMatch(n);
    }

    public static bool IsStrandHairAssetPath(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        return StrandHairAssetPath.IsMatch(name.Replace('\\', '/'));
    }

    public static bool IsPackOnlyCreatedAssetPath(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        return PackOnlyCreatedPath.IsMatch(name.Replace('\\', '/'));
    }

    /// <summary>
    /// Face/hair texture maps that are often absent from the live TOC even on named
    /// ORIGID paths (e.g. Retro <c>face_*_normal</c>). Res force-add overwrites when
    /// present; EBX force-add creates when missing.
    /// </summary>
    private static readonly Regex PlayerTextureMapPath = new(
        @"/(face|hair|haircap|mouthbag)_[^/]+_(color|normal|specmask|coeff|ambient|roughness|opacity|cavity)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool IsPlayerTextureMapPath(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        return PlayerTextureMapPath.IsMatch(name.Replace('\\', '/'));
    }

    /// <summary>
    /// Combined offline force-<c>IsAdded</c> path heuristic.
    /// </summary>
    public static bool IsForceAddedAssetPath(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        string n = name.Replace('\\', '/');

        // Named ORIGID var_0: meshes/blueprints stay TOC mods (need live mesh materials).
        // Texture maps + strand-hair are frequently true adds even on named paths
        // (log: face_*_normal "doesn't exist" → rainbow head preview).
        if (IsNamedPlayerVar0ModificationPath(n))
            return IsStrandHairAssetPath(n) || IsPlayerTextureMapPath(n);

        return AddedHeadVariationPath.IsMatch(n)
               || CreatedPlayerNumericFolder.IsMatch(n)
               || CreatedKitNumericFolder.IsMatch(n)
               || PackOnlyCreatedPath.IsMatch(n)
               || IsStrandHairAssetPath(n)
               || IsPlayerTextureMapPath(n);
    }

    /// <summary>
    /// Chunk GUIDs that must be project-<c>IsAdded</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Offline we deliberately return an <b>empty</b> set. FET's project loader, when
    /// <c>Header.GameVersion != fileSystem.Head</c> (we write header version 0), will:
    /// </para>
    /// <list type="bullet">
    /// <item>Apply <c>ModifiedEntry</c> to existing TOC chunks (non-added path)</item>
    /// <item><c>AddChunk()</c> missing GUIDs instead of "doesn't exist, skipping"</item>
    /// </list>
    /// <para>
    /// Force-adding chunks that already exist orphans the payload ("already exists") and
    /// breaks texture/mesh data — the root of rainbow mesh previews after recover.
    /// </para>
    /// </remarks>
    public static HashSet<Guid> CollectForceAddedChunkIds(IReadOnlyList<FifamodResource> resources)
    {
        _ = resources;
        return new HashSet<Guid>();
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
    /// Best-effort linked-asset graph for one resource (project <c>HasLinkedAssets</c>).
    /// Texture/mesh EBX → same-name Res → force-added payload chunks;
    /// head/hair meshes → co-located face/hair texture EBX (helps material reimport);
    /// MeshVariationDatabase → sibling face/mesh EBX under the same head folder.
    /// </summary>
    public static List<ProjectLinkedAssetRef> BuildLinkedAssets(
        FifamodResource resource,
        IReadOnlyList<FifamodResource> all,
        IReadOnlySet<Guid> knownChunkIds,
        IReadOnlyDictionary<string, FifamodResource> resByName,
        IReadOnlySet<Guid>? forceAddedChunks = null)
    {
        var links = new List<ProjectLinkedAssetRef>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRes(string? name)
        {
            if (string.IsNullOrEmpty(name) || !resByName.ContainsKey(name))
                return;
            string key = "res:" + name;
            if (!seen.Add(key))
                return;
            links.Add(new ProjectLinkedAssetRef { Kind = LinkedAssetTypeRes, Name = name });
        }

        void AddChunk(Guid id)
        {
            if (id == Guid.Empty || !knownChunkIds.Contains(id))
                return;
            // Only link chunks that will load: force-added or always present as TOC mods.
            // Linking non-added missing chunks floods the log with "linked CHUNK doesn't exist".
            if (forceAddedChunks is not null && !forceAddedChunks.Contains(id))
                return;
            string key = "chunk:" + id.ToString("N");
            if (!seen.Add(key))
                return;
            links.Add(new ProjectLinkedAssetRef { Kind = LinkedAssetTypeChunk, ChunkId = id });
        }

        void AddEbx(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return;
            if (!all.Any(r => r.Kind == FifamodResourceKind.Ebx
                              && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
                return;
            string key = "ebx:" + name;
            if (!seen.Add(key))
                return;
            links.Add(new ProjectLinkedAssetRef { Kind = LinkedAssetTypeEbx, Name = name });
        }

        if (resource.Kind == FifamodResourceKind.Ebx && !string.IsNullOrEmpty(resource.Name))
        {
            string n = resource.Name.Replace('\\', '/');

            // Same-name Res (Texture / MeshSet)
            if (resByName.TryGetValue(resource.Name, out var sameRes))
            {
                AddRes(sameRes.Name);
                foreach (Guid g in CollectChunkGuids(sameRes, knownChunkIds))
                    AddChunk(g);
            }

            // Head/hair/haircap mesh → co-located face/hair texture EBX (mesh viewer material lookup)
            if (n.EndsWith("_mesh", StringComparison.OrdinalIgnoreCase)
                && (n.Contains("/head_", StringComparison.OrdinalIgnoreCase)
                    || n.Contains("/hair_", StringComparison.OrdinalIgnoreCase)
                    || n.Contains("/haircap_", StringComparison.OrdinalIgnoreCase)
                    || n.Contains("/mouthbag_", StringComparison.OrdinalIgnoreCase)))
            {
                string? folder = GetParentPath(n);
                if (folder is not null)
                {
                    foreach (var e in all.Where(r => r.Kind == FifamodResourceKind.Ebx
                                                     && r.Name.Replace('\\', '/')
                                                         .StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase)))
                    {
                        string en = e.Name.Replace('\\', '/');
                        if (en.EndsWith("_color", StringComparison.OrdinalIgnoreCase)
                            || en.EndsWith("_normal", StringComparison.OrdinalIgnoreCase)
                            || en.EndsWith("_specmask", StringComparison.OrdinalIgnoreCase)
                            || en.EndsWith("_coeff", StringComparison.OrdinalIgnoreCase))
                        {
                            AddEbx(e.Name);
                            if (resByName.TryGetValue(e.Name, out var tr))
                            {
                                AddRes(tr.Name);
                                foreach (Guid g in CollectChunkGuids(tr, knownChunkIds))
                                    AddChunk(g);
                            }
                        }
                    }
                }
            }

            // MeshVariationDatabase → sibling face/mesh assets under the same head folder
            if (resource.Name.Contains("meshvariationdb", StringComparison.OrdinalIgnoreCase))
            {
                int star = n.IndexOf("_starhead_brt", StringComparison.OrdinalIgnoreCase);
                if (star > 0)
                {
                    string before = n[..star]; // …/var_0
                    foreach (var e in all.Where(r => r.Kind == FifamodResourceKind.Ebx
                                                     && r.Name.Replace('\\', '/')
                                                         .StartsWith(before + "/", StringComparison.OrdinalIgnoreCase)
                                                     && !string.Equals(r.Name, resource.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        AddEbx(e.Name);
                        if (resByName.TryGetValue(e.Name, out var er))
                        {
                            AddRes(er.Name);
                            foreach (Guid g in CollectChunkGuids(er, knownChunkIds))
                                AddChunk(g);
                        }
                    }
                }
            }
        }
        else if (resource.Kind == FifamodResourceKind.Res && resource.Data is { Length: >= 16 })
        {
            foreach (Guid g in CollectChunkGuids(resource, knownChunkIds))
                AddChunk(g);
        }

        return links;
    }

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

        if (Regex.IsMatch(n, @"/(head|hair|haircap|face|mouthbag|hair_accessory)_\d+_\d+_\d+$"))
            return "ObjectBlueprint";

        return "ObjectBlueprint";
    }

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

    private static string? GetParentPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        int i = path.LastIndexOf('/');
        return i > 0 ? path[..i] : null;
    }

    private static List<Guid> CollectChunkGuids(FifamodResource r, IReadOnlySet<Guid> knownChunks)
    {
        var set = new HashSet<Guid>();
        if (r.Data is { Length: >= 16 })
            CollectChunkGuidsFromRes(r, knownChunks, set);
        return set.ToList();
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
        IReadOnlySet<Guid> knownChunks,
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

        for (int i = 0; i + 16 <= d.Length; i++)
        {
            var g = new Guid(d.AsSpan(i, 16));
            if (g != Guid.Empty && knownChunks.Contains(g))
                into.Add(g);
        }
    }
}

/// <summary>One linked-asset row for FET project load (<c>SaveLinkedAssets</c>).</summary>
public sealed class ProjectLinkedAssetRef
{
    /// <summary><see cref="FifamodProjectAddedRecovery.LinkedAssetTypeEbx"/> / Res / Chunk.</summary>
    public byte Kind { get; init; }

    public string? Name { get; init; }
    public Guid ChunkId { get; init; }
}
