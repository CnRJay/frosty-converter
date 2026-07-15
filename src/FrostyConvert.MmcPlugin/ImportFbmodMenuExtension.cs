using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Windows.Media;
using Frosty.Controls;
using Frosty.Core;
using Frosty.Core.Windows;
using FrostyConvert.Core.Mod;
using FrostySdk;
using FrostySdk.IO;
using FrostySdk.Managers;

namespace FrostyConvert.MmcPlugin;

/// <summary>
/// Imports a compiled .fbmod into the LIVE editor AssetManager after the game profile is loaded.
/// Required for CFB/Madden RIFF EBX — offline .fbproject storage cannot open assets in the property grid.
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
        int okEbx = 0, okRes = 0, okChunk = 0, linkedEbx = 0;
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

            errors.Add($"(info) EbxVersion={ProfilesLibrary.EbxVersion}, Oodle={(OodleNet48.IsBound ? OodleNet48.BoundPath : "not bound")}");

            // Chunks before Res so texture pixel data is present when Texture.Read runs.
            var resources = mod.Resources
                .Where(r => r.Type != ModResourceType.Embedded)
                .OrderBy(r => r.Type switch
                {
                    ModResourceType.Chunk => 0,
                    ModResourceType.Res => 1,
                    ModResourceType.Ebx => 2,
                    _ => 3,
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
                        case ModResourceType.Ebx:
                            if (ImportEbx(resource, errors)) { ok++; okEbx++; } else fail++;
                            break;
                        case ModResourceType.Res:
                            if (ImportRes(resource, errors, importedResNames)) { ok++; okRes++; } else fail++;
                            break;
                        case ModResourceType.Chunk:
                            if (ImportChunk(resource, errors)) { ok++; okChunk++; } else fail++;
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

            // Texture-style mods are often Res+Chunk only. Data Explorer lists EBX, so link
            // each modified Res to its same-name TextureAsset so "Show Modified" finds them.
            task.Update("Linking texture EBX…", 99.0);
            linkedEbx = LinkModifiedResToEbx(importedResNames);
        });

        TryRefreshDataExplorer();

        int dirty = 0;
        try { dirty = (int)App.AssetManager.GetDirtyCount(); } catch { /* older MMC */ }

        string summary =
            $"Import finished.\n\n" +
            $"OK: {ok}  (ebx={okEbx}, res={okRes}, chunk={okChunk})\n" +
            $"Failed: {fail}\nSkipped: {skip}\n" +
            $"Linked TextureAsset EBX: {linkedEbx}\n" +
            $"Dirty assets now: {dirty}\n\n" +
            "Data Explorer only lists EBX. Res/chunk-only texture mods appear under " +
            "Show Modified after we link each Res to its TextureAsset.\n\n" +
            "Next: File → Save As… a new .fbproject, then confirm Modified shows assets.";
        if (errors.Count > 0)
            summary += "\n\nDetails:\n" + string.Join("\n", errors.Take(20));
        if (errors.Count > 20)
            summary += $"\n…and {errors.Count - 20} more";

        FrostyMessageBox.Show(summary, "FrostyConvert");
        App.Logger.Log(
            "FrostyConvert import: ok={0} fail={1} ebx={2} res={3} chunk={4} linkedEbx={5} dirty={6} file={7}",
            ok, fail, okEbx, okRes, okChunk, linkedEbx, dirty, Path.GetFileName(path));
    });

    /// <summary>
    /// MMC exposes <c>App.FileSystemManager</c>; stock Frosty uses <c>App.FileSystem</c>.
    /// Resolve at runtime so the plugin compiles against either reference set.
    /// </summary>
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
        if (resource.HasHandler)
        {
            errors.Add($"Ebx '{resource.Name}': custom handler not supported in plugin v1");
            return false;
        }

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
        var entry = App.AssetManager.GetEbxEntry(resource.Name);
        if (entry == null)
        {
            errors.Add($"Ebx '{resource.Name}': not in game (added assets not yet supported)");
            return false;
        }

        object fileSystem = GetFileSystem();
        Exception? lastError = null;

        // Try official factory first (picks Riff / RiffPGA / V2 based on EbxVersion).
        foreach (bool useProjectFactory in new[] { false, true })
        {
            try
            {
                using var ms = new MemoryStream(data);
                EbxReader reader = CreateEbxReader(ms, fileSystem, useProjectFactory);

                if (TryReadAndApply(resource.Name, reader, out string? detail))
                    return true;
                lastError = new Exception(detail ?? $"{reader.GetType().Name} produced no root");
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        // Force RIFF readers (MMC IsValid is unreliable on Riff; Create* may also pick wrong type).
        foreach (string typeName in new[]
                 {
                     "FrostySdk.IO.EbxReaderRiff",
                     "FrostySdk.IO.EbxReaderRiffPGA",
                 })
        {
            try
            {
                Type? t = typeof(EbxReader).Assembly.GetType(typeName);
                if (t == null) continue;

                using var ms = new MemoryStream(data);
                // ctor(Stream, FileSystemManager/FileSystem, bool patched)
                object? inst = Activator.CreateInstance(t, ms, fileSystem, false);
                if (inst is not EbxReader reader) continue;

                if (TryReadAndApply(resource.Name, reader, out string? detail))
                    return true;
                lastError = new Exception(detail ?? $"{typeName} produced no root");
            }
            catch (TargetInvocationException tex)
            {
                lastError = tex.InnerException ?? tex;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        string err = lastError?.Message ?? "unknown";
        errors.Add(
            $"Ebx '{resource.Name}': failed to parse (magic={magic}, len={data.Length}, " +
            $"EbxVersion={ProfilesLibrary.EbxVersion}): {err}");
        return false;
    }

    private static EbxReader CreateEbxReader(MemoryStream ms, object fileSystem, bool projectReader)
    {
        // Call CreateProjectReader/CreateReader via reflection so argument type matches MMC or stock Frosty.
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

    private static bool TryReadAndApply(string name, EbxReader reader, out string? detail)
    {
        detail = null;
        try
        {
            EbxAsset asset = reader.ReadAsset<EbxAsset>();
            object? root = asset.RootObject;
            if (root == null)
            {
                detail = $"{reader.GetType().Name}: RootObject null (IsValid={reader.IsValid})";
                return false;
            }

            App.AssetManager.ModifyEbx(name, asset);
            return true;
        }
        catch (Exception ex)
        {
            detail = $"{reader.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static bool ImportRes(FbmodResource resource, List<string> errors, List<string> importedResNames)
    {
        if (string.IsNullOrWhiteSpace(resource.Name) || resource.Data == null || resource.Data.Length == 0)
            return false;

        // AssetManager stores res keys lowercased; GetResEntry lowercases, but ModifyRes does not.
        string name = resource.Name.ToLowerInvariant();

        if (App.AssetManager.GetResEntry(name) == null)
        {
            errors.Add($"Res '{resource.Name}': not in game");
            return false;
        }
        if (resource.HasHandler)
        {
            errors.Add($"Res '{resource.Name}': handler not supported in plugin v1");
            return false;
        }

        // Mod payloads are CAS-compressed. Texture.Read expects raw RES bytes (e.g. 168-byte header).
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
            importedResNames.Add(name);
            return true;
        }
        catch (Exception ex)
        {
            errors.Add($"Res '{resource.Name}': ModifyRes failed ({ex.Message})");
            return false;
        }
    }

    /// <summary>
    /// Point same-name EBX at each modified Res so Data Explorer "Show Modified" lists TextureAssets.
    /// Many texture .fbmods ship Res+Chunk only (no EBX payload).
    /// </summary>
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

                // If Res already links a modified chunk, also attach it to the EBX graph.
                foreach (var linkedAsset in res.LinkedAssets)
                {
                    if (linkedAsset != null)
                        ebx.LinkAsset(linkedAsset);
                }

                // Force a direct EBX modify so the asset is guaranteed to show under Modified
                // even if LinkedAssets / IsIndirectlyModified is filtered oddly after project load.
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
            // non-fatal — user can toggle Modified checkbox to refresh
        }
    }

    private static bool ImportChunk(FbmodResource resource, List<string> errors)
    {
        if (!Guid.TryParse(resource.Name, out Guid id) || resource.Data == null || resource.Data.Length == 0)
            return false;
        if (resource.HasHandler)
        {
            errors.Add($"Chunk '{id}': handler not supported in plugin v1");
            return false;
        }
        if (App.AssetManager.GetChunkEntry(id) == null)
        {
            errors.Add($"Chunk '{id}': not in game");
            return false;
        }

        // CAS → raw pixel / chunk bytes for ModifyChunk
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
            // Apply range / logical sizes when the mod specifies them (texture mips)
            TryApplyChunkGeometry(id, resource);
            return true;
        }
        catch (Exception ex)
        {
            errors.Add($"Chunk '{id}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Set LogicalOffset/Size and RangeStart/End on the live chunk entry when present in the mod.
    /// </summary>
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

            // MMC: FirstMip of -1 with a non-zero RangeStart is treated as 0.
            int firstMip = resource.FirstMip;
            if (firstMip == -1 && resource.RangeStart != 0)
                firstMip = 0;
            t.GetProperty("FirstMip")?.SetValue(entry, firstMip);

            if (resource.SuperBundlesToAdd is { Count: > 0 })
            {
                var addedSb = t.GetProperty("AddedSuperBundles")?.GetValue(entry);
                if (addedSb is System.Collections.IList list)
                {
                    foreach (int sb in resource.SuperBundlesToAdd)
                        list.Add(sb);
                }
            }
        }
        catch
        {
            // Non-fatal — decompress+ModifyChunk is enough for many textures
        }
    }
}
