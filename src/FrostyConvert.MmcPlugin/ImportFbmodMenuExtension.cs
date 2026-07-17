using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Windows.Media;
using Frosty.Controls;
using Frosty.Core;
using Frosty.Core.Handlers;
using Frosty.Core.Legacy;
using Frosty.Core.Windows;
using FrostyConvert.Core.Convert;
using FrostyConvert.Core.Mod;
using FrostySdk;
using FrostySdk.IO;
using FrostySdk.Managers;
using HandlerExtraData = Frosty.Core.Mod.HandlerExtraData;
using IModCustomActionHandler = Frosty.Core.Mod.IModCustomActionHandler;
using RuntimeResources = Frosty.Core.Mod.RuntimeResources;

namespace FrostyConvert.MmcPlugin;

/// <summary>
/// Imports a compiled .fbmod into the LIVE editor AssetManager after the game profile is loaded.
/// Mirrors MMC <c>FrostyModReader</c> + editor apply paths (bundles, added assets, legacy handlers,
/// BRT-free CFB/Madden assets) so File → Save As produces a native project.
/// </summary>
public sealed class ImportFbmodMenuExtension : MenuExtension
{
    public override string TopLevelMenuName => "Tools";
    public override string SubLevelMenuName => null!;
    public override string MenuItemName => "Import Frosty Mod (.fbmod)…";
    public override ImageSource Icon => null!;

    public override RelayCommand MenuItemClicked => new RelayCommand(_ =>
    {
        if (App.AssetManager == null)
        {
            FrostyMessageBox.Show("Load a game profile first, then import the mod.", "FrostyConvert");
            return;
        }

        using var ofd = new OpenFileDialog
        {
            Title = "Import compiled Frosty mod",
            Filter = "Frosty Mod (*.fbmod)|*.fbmod|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (ofd.ShowDialog() != DialogResult.OK)
            return;

        string path = ofd.FileName;
        int ok = 0, fail = 0, skip = 0;
        int okEbx = 0, okRes = 0, okChunk = 0, okBundle = 0, okLegacy = 0, linkedEbx = 0;
        var errors = new List<string>();
        var importedResNames = new List<string>();

        FrostyTaskWindow.Show("Importing fbmod", Path.GetFileName(path), task =>
        {
            OodleNet48.TryBindSearch(
                AppDomain.CurrentDomain.BaseDirectory,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins"),
                Path.GetDirectoryName(path));

            FbmodFile mod = FbmodReader.Read(path, loadResourceData: true);
            if (mod.Format != FbmodFormatKind.Binary)
            {
                errors.Add("Legacy .fbmod format is not supported yet.");
                return;
            }

            errors.Add(
                $"(info) profile={mod.ProfileName} v={mod.Version} EbxVersion={ProfilesLibrary.EbxVersion}, " +
                $"Oodle={(OodleNet48.IsBound ? OodleNet48.BoundPath : "not bound")}");

            // Bundles first (so AddToBundle can resolve), then chunks (pixels), res, ebx.
            // Handlers last among chunks so collector tables see modified data chunks.
            var resources = mod.Resources
                .Where(r => r.Type != ModResourceType.Embedded)
                .OrderBy(r => r.Type switch
                {
                    ModResourceType.Bundle => 0,
                    ModResourceType.Chunk when !r.HasHandler => 1,
                    ModResourceType.Res => 2,
                    ModResourceType.Ebx => 3,
                    ModResourceType.Chunk when r.HasHandler => 4,
                    ModResourceType.FsFile => 5,
                    _ => 6,
                })
                .ToList();

            int idx = 0;
            foreach (var resource in resources)
            {
                task.Update(resource.Name ?? "", (idx++ / (double)Math.Max(1, resources.Count)) * 100.0);
                try
                {
                    switch (resource.Type)
                    {
                        case ModResourceType.Bundle:
                            if (ImportBundle(resource, errors)) { ok++; okBundle++; } else fail++;
                            break;
                        case ModResourceType.Ebx:
                            if (ImportEbx(resource, errors)) { ok++; okEbx++; } else fail++;
                            break;
                        case ModResourceType.Res:
                            if (ImportRes(resource, errors, importedResNames)) { ok++; okRes++; } else fail++;
                            break;
                        case ModResourceType.Chunk:
                            if (resource.HasHandler)
                            {
                                if (ImportHandlerChunk(resource, errors)) { ok++; okLegacy++; } else fail++;
                            }
                            else if (ImportChunk(resource, errors)) { ok++; okChunk++; } else fail++;
                            break;
                        case ModResourceType.FsFile:
                            if (ImportFsFile(resource, errors)) { ok++; } else fail++;
                            break;
                        default:
                            skip++;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    fail++;
                    errors.Add($"{resource.Type} '{resource.Name}': {ex.Message}");
                }
            }

            task.Update("Linking texture EBX…", 99.0);
            linkedEbx = LinkModifiedResToEbx(importedResNames);
        });

        TryRefreshDataExplorer();

        int dirty = 0;
        try { dirty = (int)App.AssetManager.GetDirtyCount(); } catch { /* older MMC */ }

        FbmodImportReadiness? readiness = null;
        try
        {
            var modSnap = FbmodReader.Read(path, loadResourceData: false);
            readiness = FbmodImportReadiness.Create(modSnap, ok, fail, okEbx, okRes, okChunk, okBundle, okLegacy);
        }
        catch { /* non-fatal */ }

        string summary =
            $"Import finished.\n\n" +
            $"OK: {ok}  (ebx={okEbx}, res={okRes}, chunk={okChunk}, bundle={okBundle}, handler={okLegacy})\n" +
            $"Failed: {fail}\nSkipped: {skip}\n" +
            $"Linked TextureAsset EBX: {linkedEbx}\n" +
            $"Dirty assets now: {dirty}\n\n" +
            "Data Explorer only lists EBX. Res/chunk-only texture mods appear under " +
            "Show Modified after we link each Res to its TextureAsset.\n\n" +
            "REQUIRED: File → Save As… a NEW .fbproject, then export a new .fbmod to test in Mod Manager.";
        if (readiness != null)
            summary += "\n\n" + readiness.ToText();
        if (errors.Count > 0)
            summary += "\n\nDetails:\n" + string.Join("\n", errors.Take(24));
        if (errors.Count > 24)
            summary += $"\n…and {errors.Count - 24} more";

        FrostyMessageBox.Show(summary, "FrostyConvert");
        App.Logger.Log(
            "FrostyConvert import: ok={0} fail={1} ebx={2} res={3} chunk={4} bundle={5} handler={6} linkedEbx={7} dirty={8} file={9}",
            ok, fail, okEbx, okRes, okChunk, okBundle, okLegacy, linkedEbx, dirty, Path.GetFileName(path));
    });

    // -------------------------------------------------------------------------
    // Bundle
    // -------------------------------------------------------------------------

    private static bool ImportBundle(FbmodResource resource, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(resource.Name))
            return false;

        try
        {
            int sbIndex = ResolveSuperBundleIndex(resource.SuperBundleHash);
            if (sbIndex < 0)
            {
                // Fall back to first superbundle so the bundle still exists in the project.
                sbIndex = 0;
                errors.Add($"(warn) Bundle '{resource.Name}': superBundle hash 0x{resource.SuperBundleHash:X8} not found; using index 0");
            }

            // BundleType.None = 0
            App.AssetManager.AddBundle(resource.Name, (BundleType)0, sbIndex);
            return true;
        }
        catch (Exception ex)
        {
            errors.Add($"Bundle '{resource.Name}': {ex.Message}");
            return false;
        }
    }

    private static int ResolveSuperBundleIndex(int superBundleNameHash)
    {
        try
        {
            foreach (SuperBundleEntry sb in App.AssetManager.EnumerateSuperBundles())
            {
                if (sb?.Name == null) continue;
                int h = Fnv1HashString(sb.Name.ToLowerInvariant());
                if (h == superBundleNameHash)
                    return App.AssetManager.GetSuperBundleId(sb);
            }
        }
        catch { /* ignore */ }
        return -1;
    }

    /// <summary>Matches Frosty.Hash.Fnv1.HashString (used by mod bundle hashes).</summary>
    private static int Fnv1HashString(string text)
    {
        uint hash = 2166136261u;
        foreach (char c in text)
        {
            hash *= 16777619u;
            hash ^= c;
        }
        return unchecked((int)hash);
    }

    // -------------------------------------------------------------------------
    // EBX
    // -------------------------------------------------------------------------

    private static object GetFileSystem()
    {
        Type appType = typeof(App);
        foreach (string name in new[] { "FileSystemManager", "FileSystem" })
        {
            PropertyInfo? prop = appType.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (prop != null)
            {
                object? value = prop.GetValue(null);
                if (value != null)
                    return value;
            }

            FieldInfo? field = appType.GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (field != null)
            {
                object? value = field.GetValue(null);
                if (value != null)
                    return value;
            }
        }

        throw new InvalidOperationException("App.FileSystemManager / App.FileSystem not found on this editor build.");
    }

    private static bool ImportEbx(FbmodResource resource, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(resource.Name))
            return false;
        if (resource.Data == null || resource.Data.Length == 0)
        {
            errors.Add($"Ebx '{resource.Name}': no payload");
            return false;
        }

        string name = resource.Name.ToLowerInvariant();
        if (resource.HasHandler)
            return ApplyModHandler(App.AssetManager.GetEbxEntry(name), resource, errors, isEbx: true);

        byte[] data;
        try
        {
            data = CasBlockDecompressorNet48.Decompress(resource.Data);
        }
        catch (Exception ex)
        {
            errors.Add($"Ebx '{resource.Name}': decompress failed ({ex.Message})");
            return false;
        }

        if (data.Length < 4)
        {
            errors.Add($"Ebx '{resource.Name}': decompressed payload too small ({data.Length})");
            return false;
        }

        string magic = System.Text.Encoding.ASCII.GetString(data, 0, 4);
        if (!TryParseEbxAsset(data, out EbxAsset? asset, out string? parseErr) || asset == null)
        {
            errors.Add(
                $"Ebx '{resource.Name}': failed to parse (magic={magic}, len={data.Length}, " +
                $"EbxVersion={ProfilesLibrary.EbxVersion}): {parseErr ?? "unknown"}");
            return false;
        }

        var entry = App.AssetManager.GetEbxEntry(name);
        int[] bundles = ResolveBundleIds(resource.AddedBundleHashes).ToArray();

        try
        {
            if (entry == null)
            {
                // Matches AssetManager.AddEbx(name, asset, bundles) — full live type graph from reader.
                entry = App.AssetManager.AddEbx(name, asset, bundles);
                if (entry == null)
                {
                    errors.Add($"Ebx '{resource.Name}': AddEbx returned null");
                    return false;
                }
            }
            else
            {
                App.AssetManager.ModifyEbx(name, asset);
                entry = App.AssetManager.GetEbxEntry(name);
            }

            ApplyEntryMeta(entry, resource);
            return true;
        }
        catch (Exception ex)
        {
            errors.Add($"Ebx '{resource.Name}': apply failed ({ex.Message})");
            return false;
        }
    }

    private static bool TryParseEbxAsset(byte[] data, out EbxAsset? asset, out string? error)
    {
        asset = null;
        error = null;
        object fileSystem = GetFileSystem();
        Exception? last = null;

        foreach (bool useProjectFactory in new[] { false, true })
        {
            try
            {
                using var ms = new MemoryStream(data);
                EbxReader reader = CreateEbxReader(ms, fileSystem, useProjectFactory);
                EbxAsset a = reader.ReadAsset<EbxAsset>();
                if (a.RootObject != null)
                {
                    asset = a;
                    return true;
                }
                last = new Exception($"{reader.GetType().Name}: RootObject null");
            }
            catch (Exception ex) { last = ex; }
        }

        foreach (string typeName in new[] { "FrostySdk.IO.EbxReaderRiff", "FrostySdk.IO.EbxReaderRiffPGA" })
        {
            try
            {
                Type? t = typeof(EbxReader).Assembly.GetType(typeName);
                if (t == null) continue;
                using var ms = new MemoryStream(data);
                if (Activator.CreateInstance(t, ms, fileSystem, false) is not EbxReader reader)
                    continue;
                EbxAsset a = reader.ReadAsset<EbxAsset>();
                if (a.RootObject != null)
                {
                    asset = a;
                    return true;
                }
                last = new Exception($"{typeName}: RootObject null");
            }
            catch (TargetInvocationException tex) { last = tex.InnerException ?? tex; }
            catch (Exception ex) { last = ex; }
        }

        error = last?.Message;
        return false;
    }

    private static EbxReader CreateEbxReader(MemoryStream ms, object fileSystem, bool projectReader)
    {
        string methodName = projectReader ? "CreateProjectReader" : "CreateReader";
        MethodInfo[] methods = typeof(EbxReader).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == methodName)
            .ToArray();

        foreach (MethodInfo method in methods)
        {
            ParameterInfo[] ps = method.GetParameters();
            try
            {
                object? result = null;
                if (projectReader && ps.Length >= 4)
                    result = method.Invoke(null, new object[] { ms, fileSystem, false, true });
                else if (!projectReader && ps.Length >= 3)
                    result = method.Invoke(null, new object[] { ms, fileSystem, false });
                else if (ps.Length >= 2)
                    result = method.Invoke(null, new object[] { ms, fileSystem });

                if (result is EbxReader reader)
                    return reader;
            }
            catch (TargetInvocationException)
            {
                // try next overload
            }
        }

        throw new MissingMethodException($"EbxReader.{methodName} not found for this editor build.");
    }

    // -------------------------------------------------------------------------
    // Res
    // -------------------------------------------------------------------------

    private static bool ImportRes(FbmodResource resource, List<string> errors, List<string> importedResNames)
    {
        if (string.IsNullOrWhiteSpace(resource.Name) || resource.Data == null || resource.Data.Length == 0)
            return false;

        string name = resource.Name.ToLowerInvariant();
        var entry = App.AssetManager.GetResEntry(name);

        // Res handlers are keyed by ResourceType (ProcessModResources), not handler hash.
        if (resource.HasHandler || entry?.ResType is uint rt && App.PluginManager?.GetCustomHandler((ResourceType)rt) != null)
        {
            if (entry == null && resource.IsAdded)
            {
                try
                {
                    var added = new ResAssetEntry
                    {
                        Name = name,
                        ResType = resource.ResType,
                        ResRid = resource.ResRid,
                        IsAdded = true,
                    };
                    if (resource.ResMeta is { Length: > 0 })
                        added.ResMeta = resource.ResMeta;
                    App.AssetManager.AddRes(added);
                    entry = App.AssetManager.GetResEntry(name);
                }
                catch (Exception ex)
                {
                    errors.Add($"Res '{resource.Name}': AddRes failed ({ex.Message})");
                    return false;
                }
            }

            if (ApplyModHandler(entry, resource, errors, isEbx: false, resType: resource.ResType != 0 ? resource.ResType : entry?.ResType))
            {
                importedResNames.Add(name);
                return true;
            }
            // fall through to raw modify if handler failed
        }

        if (entry == null)
        {
            // Force-add when missing (matches executor forcing IsAdded for orphan assets)
            try
            {
                var added = new ResAssetEntry
                {
                    Name = name,
                    ResType = resource.ResType,
                    ResRid = resource.ResRid != 0 ? resource.ResRid : 1,
                    IsAdded = true,
                };
                if (resource.ResMeta is { Length: > 0 })
                    added.ResMeta = resource.ResMeta;
                App.AssetManager.AddRes(added);
                entry = App.AssetManager.GetResEntry(name);
            }
            catch (Exception ex)
            {
                errors.Add($"Res '{resource.Name}': not in game and AddRes failed ({ex.Message})");
                return false;
            }
        }

        byte[] data;
        try
        {
            data = CasBlockDecompressorNet48.Decompress(resource.Data);
        }
        catch (Exception ex)
        {
            errors.Add($"Res '{resource.Name}': decompress failed ({ex.Message})");
            return false;
        }

        if (data.Length == 0)
        {
            errors.Add($"Res '{resource.Name}': empty after decompress");
            return false;
        }

        try
        {
            App.AssetManager.ModifyRes(name, data, resource.ResMeta);
            ApplyEntryMeta(App.AssetManager.GetResEntry(name), resource);
            importedResNames.Add(name);
            return true;
        }
        catch (Exception ex)
        {
            errors.Add($"Res '{resource.Name}': ModifyRes failed ({ex.Message})");
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Chunk
    // -------------------------------------------------------------------------

    private static bool ImportChunk(FbmodResource resource, List<string> errors)
    {
        if (!Guid.TryParse(resource.Name, out Guid id) || resource.Data == null || resource.Data.Length == 0)
            return false;

        if (App.AssetManager.GetChunkEntry(id) == null)
        {
            // Force-add missing chunks (ProcessModResources sets IsAdded when TOC miss)
            byte[] addData;
            try
            {
                addData = CasBlockDecompressorNet48.Decompress(resource.Data);
            }
            catch (Exception ex)
            {
                errors.Add($"Chunk '{id}': decompress failed for AddChunk ({ex.Message})");
                return false;
            }

            try
            {
                int[] bundles = ResolveBundleIds(resource.AddedBundleHashes).ToArray();
                App.AssetManager.AddChunk(addData, id, null, bundles);
            }
            catch (Exception ex)
            {
                errors.Add($"Chunk '{id}': AddChunk failed ({ex.Message})");
                return false;
            }
        }

        byte[] data;
        try
        {
            data = CasBlockDecompressorNet48.Decompress(resource.Data);
        }
        catch (Exception ex)
        {
            errors.Add($"Chunk '{id}': decompress failed ({ex.Message})");
            return false;
        }

        try
        {
            App.AssetManager.ModifyChunk(id, data, null);
            TryApplyChunkGeometry(id, resource);
            ApplyEntryMeta(App.AssetManager.GetChunkEntry(id), resource);
            return true;
        }
        catch (Exception ex)
        {
            errors.Add($"Chunk '{id}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Chunk custom handlers — full Load + Modify like <c>FrostyModExecutor</c>, then write
    /// compressed result into <c>ModifiedEntry.Data</c> for editor Save.
    /// </summary>
    private static bool ImportHandlerChunk(FbmodResource resource, List<string> errors)
    {
        if (!Guid.TryParse(resource.Name, out Guid id) || resource.Data == null || resource.Data.Length == 0)
            return false;

        var entry = App.AssetManager.GetChunkEntry(id);
        if (entry == null)
        {
            // Ensure collector/handler chunk exists
            try
            {
                App.AssetManager.AddChunk(new byte[1], id);
                entry = App.AssetManager.GetChunkEntry(id);
            }
            catch (Exception ex)
            {
                errors.Add($"Chunk '{id}': cannot create handler chunk ({ex.Message})");
                return false;
            }
        }

        bool isLegacy = resource.HandlerHash == FbmodConstants.LegacyHandlerHash
                        || unchecked((uint)resource.HandlerHash) == LegacyCustomActionHandler.Hash;

        // Legacy: also update live LegacyFileEntry collectors (editor project path)
        if (isLegacy)
            ApplyLegacyCollectorEntries(resource, id, errors);

        return ApplyModHandler(entry, resource, errors, isEbx: false);
    }

    private static void ApplyLegacyCollectorEntries(FbmodResource resource, Guid collectorChunkId, List<string> errors)
    {
        try
        {
            var handler = new LegacyCustomActionHandler();
            object? loaded = handler.Load(null, resource.Data!);
            if (loaded is not System.Collections.IEnumerable list)
                return;

            int applied = 0;
            foreach (object item in list)
            {
                Type t = item.GetType();
                int hash = (int)(t.GetProperty("Hash")?.GetValue(item) ?? 0);
                Guid chunkId = (Guid)(t.GetProperty("ChunkId")?.GetValue(item) ?? Guid.Empty);
                long offset = Convert.ToInt64(t.GetProperty("Offset")?.GetValue(item) ?? 0L);
                long compressedOffset = Convert.ToInt64(t.GetProperty("CompressedOffset")?.GetValue(item) ?? 0L);
                long compressedSize = Convert.ToInt64(t.GetProperty("CompressedSize")?.GetValue(item) ?? 0L);
                long size = Convert.ToInt64(t.GetProperty("Size")?.GetValue(item) ?? 0L);

                LegacyFileEntry? legacy = FindLegacyByHash(hash);
                if (legacy == null)
                    continue;

                foreach (LegacyFileEntry.ChunkCollectorInstance ci in legacy.CollectorInstances)
                {
                    ci.ModifiedEntry = new LegacyFileEntry.ChunkCollectorInstance
                    {
                        ChunkId = chunkId,
                        Offset = offset,
                        CompressedOffset = compressedOffset,
                        CompressedSize = compressedSize,
                        Size = size,
                    };
                }

                try
                {
                    var ch = App.AssetManager.GetChunkEntry(chunkId);
                    if (ch != null)
                        ch.IsDirty = true;
                }
                catch { /* ignore */ }

                applied++;
            }

            if (applied > 0)
                errors.Add($"(info) Legacy collector '{collectorChunkId}': updated {applied} legacy file(s)");
            else
                errors.Add($"(warn) Legacy collector '{collectorChunkId}': no legacy NameHash matches");
        }
        catch (Exception ex)
        {
            errors.Add($"Legacy collector parse '{collectorChunkId}': {ex.Message}");
        }
    }

    /// <summary>
    /// Full <c>IModCustomActionHandler.Load</c> + <c>Modify</c> pipeline used by MMC mod apply,
    /// then store compressed output on <c>ModifiedEntry.Data</c> for editor Save As.
    /// </summary>
    private static bool ApplyModHandler(
        AssetEntry? entry,
        FbmodResource resource,
        List<string> errors,
        bool isEbx,
        uint? resType = null)
    {
        try
        {
            // Ensure a live entry exists (ProcessModResources creates one via FillAssetEntry).
            if (entry == null)
            {
                if (isEbx)
                {
                    var e = new EbxAssetEntry { Name = resource.Name.ToLowerInvariant(), IsAdded = true };
                    App.AssetManager.AddEbx(e);
                    entry = App.AssetManager.GetEbxEntry(e.Name);
                }
                else if (resource.Type == ModResourceType.Res)
                {
                    var e = new ResAssetEntry
                    {
                        Name = resource.Name.ToLowerInvariant(),
                        ResType = resource.ResType,
                        ResRid = resource.ResRid != 0 ? resource.ResRid : 1,
                        IsAdded = true,
                    };
                    if (resource.ResMeta is { Length: > 0 })
                        e.ResMeta = resource.ResMeta;
                    App.AssetManager.AddRes(e);
                    entry = App.AssetManager.GetResEntry(e.Name);
                }
                else if (resource.Type == ModResourceType.Chunk && Guid.TryParse(resource.Name, out Guid cid))
                {
                    App.AssetManager.AddChunk(new byte[1], cid);
                    entry = App.AssetManager.GetChunkEntry(cid);
                }
            }

            if (entry == null)
            {
                errors.Add($"{resource.Type} '{resource.Name}': no asset entry for handler");
                return false;
            }

            IModCustomActionHandler? handler = null;
            bool isLegacy = resource.HandlerHash == FbmodConstants.LegacyHandlerHash
                            || unchecked((uint)resource.HandlerHash) == LegacyCustomActionHandler.Hash;

            if (isLegacy)
                handler = new LegacyCustomActionHandler();
            else if (resType is uint rt && rt != 0)
                handler = App.PluginManager?.GetCustomHandler((ResourceType)rt);
            else if (resource.Type == ModResourceType.Res && entry is ResAssetEntry re)
                handler = App.PluginManager?.GetCustomHandler((ResourceType)re.ResType);
            else if (resource.HandlerHash != 0)
                handler = App.PluginManager?.GetCustomHandler((uint)resource.HandlerHash);

            if (handler == null)
            {
                errors.Add($"{resource.Type} '{resource.Name}': no IModCustomActionHandler (0x{resource.HandlerHash:X8})");
                return false;
            }

            object? existingData = null;
            if (entry.ExtraData is HandlerExtraData existingExtra)
                existingData = existingExtra.Data;

            // Handler payloads are typically raw merge blobs (not CAS); use Data as-is.
            object data = handler.Load(existingData, resource.Data!);

            var extra = new HandlerExtraData
            {
                Handler = handler,
                Data = data,
            };
            entry.ExtraData = extra;

            var runtime = new RuntimeResources();
            handler.Modify(entry, App.AssetManager, runtime, data, out byte[] outData);

            if (outData is { Length: > 0 })
            {
                if (entry.ModifiedEntry == null)
                    entry.ModifiedEntry = new ModifiedAssetEntry();
                entry.ModifiedEntry.Data = outData;
                try
                {
                    MethodInfo? gen = typeof(Utils).GetMethod("GenerateSha1", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(byte[]) }, null);
                    if (gen != null)
                    {
                        object? sha = gen.Invoke(null, new object[] { outData });
                        if (sha != null)
                            entry.ModifiedEntry.Sha1 = (Sha1)sha;
                    }
                }
                catch { /* leave default sha1 */ }
                entry.ModifiedEntry.OriginalSize = outData.Length;
                entry.IsDirty = true;
            }

            // Apply any runtime resources the handler emitted (additional ebx/res/chunk)
            foreach (var rr in runtime.Resources)
            {
                try
                {
                    byte[] rd = runtime.GetResourceData(rr);
                    ApplyRuntimeResource(rr, rd, errors);
                }
                catch (Exception ex)
                {
                    errors.Add($"(warn) runtime resource from handler: {ex.Message}");
                }
            }

            ApplyEntryMeta(entry, resource);
            return true;
        }
        catch (Exception ex)
        {
            errors.Add($"{resource.Type} handler '{resource.Name}': {ex.Message}");
            return false;
        }
    }

    private static void ApplyRuntimeResource(Frosty.Core.Mod.BaseModResource rr, byte[] data, List<string> errors)
    {
        // Runtime resources from handlers are already processed forms; best-effort modify by name/type.
        try
        {
            string typeName = rr.Type.ToString();
            if (typeName.Equals("Ebx", StringComparison.OrdinalIgnoreCase))
            {
                if (App.AssetManager.GetEbxEntry(rr.Name) != null && data.Length > 0)
                {
                    byte[] raw = CasBlockDecompressorNet48.Decompress(data);
                    if (TryParseEbxAsset(raw, out EbxAsset? asset, out _) && asset != null)
                        App.AssetManager.ModifyEbx(rr.Name.ToLowerInvariant(), asset);
                }
            }
            else if (typeName.Equals("Res", StringComparison.OrdinalIgnoreCase))
            {
                if (App.AssetManager.GetResEntry(rr.Name) != null && data.Length > 0)
                {
                    byte[] raw = CasBlockDecompressorNet48.Decompress(data);
                    App.AssetManager.ModifyRes(rr.Name.ToLowerInvariant(), raw, null);
                }
            }
            else if (typeName.Equals("Chunk", StringComparison.OrdinalIgnoreCase))
            {
                if (Guid.TryParse(rr.Name, out Guid id) && App.AssetManager.GetChunkEntry(id) != null && data.Length > 0)
                {
                    byte[] raw = CasBlockDecompressorNet48.Decompress(data);
                    App.AssetManager.ModifyChunk(id, raw, null);
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"(warn) ApplyRuntimeResource {rr.Type} '{rr.Name}': {ex.Message}");
        }
    }

    private static bool ImportFsFile(FbmodResource resource, List<string> errors)
    {
        if (resource.Data == null || resource.Data.Length == 0)
        {
            errors.Add($"FsFile '{resource.Name}': no payload");
            return false;
        }

        // ProcessModResources: DbReader.ReadDbObject → later FileSystemManager.WriteInitFs at launch.
        // Editor: push into FileSystemManager if WriteInitFs / similar is available.
        try
        {
            object fs = GetFileSystem();
            Type fsType = fs.GetType();

            // Prefer WriteInitFs(string source, string dest, IDictionary) style — build dict with one entry.
            // For live editor session, try in-memory override methods first.
            MethodInfo? writeInit = fsType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name.IndexOf("WriteInitFs", StringComparison.OrdinalIgnoreCase) >= 0
                                     || m.Name.IndexOf("WriteFs", StringComparison.OrdinalIgnoreCase) >= 0);

            // Parse DbObject via FrostySdk.IO.DbReader if present
            Type? dbReaderType = typeof(EbxReader).Assembly.GetType("FrostySdk.IO.DbReader")
                                 ?? Type.GetType("FrostySdk.IO.DbReader, FrostySdk");
            object? dbObj = null;
            if (dbReaderType != null)
            {
                using var ms = new MemoryStream(resource.Data);
                object? reader = Activator.CreateInstance(dbReaderType, ms, null);
                dbObj = dbReaderType.GetMethod("ReadDbObject")?.Invoke(reader, null);
            }

            if (writeInit != null && dbObj != null)
            {
                // Build ConcurrentDictionary<string, DbObject>
                var dictType = typeof(System.Collections.Concurrent.ConcurrentDictionary<,>)
                    .MakeGenericType(typeof(string), dbObj.GetType());
                object dict = Activator.CreateInstance(dictType)!;
                dictType.GetMethod("TryAdd")?.Invoke(dict, new[] { resource.Name, dbObj });

                // Call with best-effort args (base, out, dict) using empty paths for in-place if arity matches
                ParameterInfo[] ps = writeInit.GetParameters();
                if (ps.Length >= 3)
                {
                    string basePath = (string?)fsType.GetProperty("BasePath")?.GetValue(fs) ?? "";
                    writeInit.Invoke(fs, new object[] { basePath, basePath, dict });
                    errors.Add($"(info) FsFile '{resource.Name}': WriteInitFs invoked");
                    return true;
                }
            }

            // Fallback: stash as custom asset if a manager exists
            try
            {
                App.AssetManager.ModifyCustomAsset("fs", resource.Name, resource.Data);
                errors.Add($"(info) FsFile '{resource.Name}': stored via ModifyCustomAsset(fs)");
                return true;
            }
            catch
            {
                // last resort: mark success if we at least parsed DbObject
                if (dbObj != null)
                {
                    errors.Add($"(warn) FsFile '{resource.Name}': parsed DbObject but no live WriteInitFs target (launch-time only in stock MMC)");
                    return true; // data is valid; editor project may not persist initfs until export path
                }
            }

            errors.Add($"FsFile '{resource.Name}': could not apply to live FileSystem");
            return false;
        }
        catch (Exception ex)
        {
            errors.Add($"FsFile '{resource.Name}': {ex.Message}");
            return false;
        }
    }

    private static LegacyFileEntry? FindLegacyByHash(int nameHash)
    {
        try
        {
            foreach (AssetEntry ae in App.AssetManager.EnumerateCustomAssets("legacy"))
            {
                if (ae is LegacyFileEntry lfe && lfe.NameHash == nameHash)
                    return lfe;
            }
        }
        catch { /* ignore */ }
        return null;
    }

    private static void TryApplyChunkGeometry(Guid id, FbmodResource resource)
    {
        try
        {
            var entry = App.AssetManager.GetChunkEntry(id);
            if (entry == null) return;

            Type t = entry.GetType();
            if (resource.LogicalSize != 0)
            {
                t.GetProperty("LogicalOffset")?.SetValue(entry, resource.LogicalOffset);
                t.GetProperty("LogicalSize")?.SetValue(entry, resource.LogicalSize);
            }
            if (resource.RangeEnd != 0 || resource.RangeStart != 0)
            {
                t.GetProperty("RangeStart")?.SetValue(entry, resource.RangeStart);
                t.GetProperty("RangeEnd")?.SetValue(entry, resource.RangeEnd);
            }

            t.GetProperty("H32")?.SetValue(entry, resource.H32);
            t.GetProperty("H64")?.SetValue(entry, resource.H64);

            int firstMip = resource.FirstMip;
            if (firstMip == -1 && resource.RangeStart != 0)
                firstMip = 0;
            t.GetProperty("FirstMip")?.SetValue(entry, firstMip);

            // ModifiedEntry geometry (what Save uses)
            if (entry.ModifiedEntry != null)
            {
                var me = entry.ModifiedEntry;
                Type mt = me.GetType();
                if (resource.LogicalSize != 0)
                {
                    mt.GetProperty("LogicalOffset")?.SetValue(me, resource.LogicalOffset);
                    mt.GetProperty("LogicalSize")?.SetValue(me, resource.LogicalSize);
                }
                if (resource.RangeEnd != 0 || resource.RangeStart != 0)
                {
                    mt.GetProperty("RangeStart")?.SetValue(me, resource.RangeStart);
                    mt.GetProperty("RangeEnd")?.SetValue(me, resource.RangeEnd);
                }
                mt.GetProperty("FirstMip")?.SetValue(me, firstMip);
            }

            if (resource.SuperBundlesToAdd is { Count: > 0 })
            {
                var addedSb = t.GetProperty("AddedSuperBundles")?.GetValue(entry);
                if (addedSb is System.Collections.IList list)
                {
                    foreach (int sb in resource.SuperBundlesToAdd)
                    {
                        if (!list.Contains(sb))
                            list.Add(sb);
                    }
                }
            }

            if (resource.IsTocChunk)
                t.GetProperty("IsTocChunk")?.SetValue(entry, true);
        }
        catch
        {
            // Non-fatal
        }
    }

    // -------------------------------------------------------------------------
    // Shared meta: AddedBundles (FNV hashes), UserData, IsInline
    // -------------------------------------------------------------------------

    private static void ApplyEntryMeta(AssetEntry? entry, FbmodResource resource)
    {
        if (entry == null) return;

        try
        {
            if (resource.ShouldInline)
                entry.IsInline = true;

            if (!string.IsNullOrEmpty(resource.UserData) && entry.ModifiedEntry != null)
            {
                try
                {
                    entry.ModifiedEntry.GetType().GetProperty("UserData")
                        ?.SetValue(entry.ModifiedEntry, resource.UserData);
                }
                catch { /* ignore */ }
            }

            if (resource.AddedBundleHashes is { Count: > 0 })
            {
                foreach (int hash in resource.AddedBundleHashes)
                {
                    int bid = ResolveBundleIdFromHash(hash);
                    if (bid >= 0)
                        entry.AddToBundle(bid);
                }
            }
        }
        catch
        {
            // non-fatal
        }
    }

    /// <summary>
    /// Bundle hashes in .fbmod match <c>FrostyModExecutor.HashBundle</c>:
    /// FNV1 of lowercased name, or raw int if name is 8-char hex.
    /// </summary>
    private static int ResolveBundleIdFromHash(int hash)
    {
        try
        {
            foreach (BundleEntry be in App.AssetManager.EnumerateBundles())
            {
                if (be?.Name == null) continue;
                int h = Fnv1HashString(be.Name.ToLowerInvariant());
                if (be.Name.Length == 8
                    && int.TryParse(be.Name, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex)
                    && hex == hash)
                {
                    return App.AssetManager.GetBundleId(be);
                }
                if (h == hash)
                    return App.AssetManager.GetBundleId(be);
            }
        }
        catch { /* ignore */ }
        return -1;
    }

    private static List<int> ResolveBundleIds(IReadOnlyList<int> hashes)
    {
        var ids = new List<int>();
        if (hashes == null) return ids;
        foreach (int h in hashes)
        {
            int id = ResolveBundleIdFromHash(h);
            if (id >= 0 && !ids.Contains(id))
                ids.Add(id);
        }
        return ids;
    }

    // -------------------------------------------------------------------------
    // Texture linking / UI
    // -------------------------------------------------------------------------

    private static int LinkModifiedResToEbx(List<string> resNames)
    {
        int linked = 0;
        foreach (string name in resNames)
        {
            try
            {
                var res = App.AssetManager.GetResEntry(name);
                var ebx = App.AssetManager.GetEbxEntry(name);
                if (res == null || ebx == null)
                    continue;

                ebx.LinkAsset(res);

                foreach (var linkedAsset in res.LinkedAssets)
                {
                    if (linkedAsset != null)
                        ebx.LinkAsset(linkedAsset);
                }

                try
                {
                    var asset = App.AssetManager.GetEbx(ebx);
                    if (asset?.RootObject != null)
                        App.AssetManager.ModifyEbx(name, asset);
                }
                catch
                {
                    // Link alone is still enough for IsIndirectlyModified in most builds
                }

                linked++;
            }
            catch
            {
                // non-fatal per asset
            }
        }
        return linked;
    }

    private static void TryRefreshDataExplorer()
    {
        try
        {
            var window = App.EditorWindow;
            if (window == null) return;
            window.DataExplorer?.RefreshAll();
            window.VisibleExplorer?.RefreshAll();
        }
        catch
        {
            // non-fatal
        }
    }
}
