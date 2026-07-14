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
        var errors = new List<string>();

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

            var resources = mod.Resources.Where(r => r.Type != ModResourceType.Embedded).ToList();
            int idx = 0;
            foreach (var resource in resources)
            {
                task.Update(resource.Name ?? "", (idx++ / (double)Math.Max(1, resources.Count)) * 100.0);
                try
                {
                    switch (resource.Type)
                    {
                        case ModResourceType.Ebx:
                            if (ImportEbx(resource, errors)) ok++; else fail++;
                            break;
                        case ModResourceType.Res:
                            if (ImportRes(resource, errors)) ok++; else fail++;
                            break;
                        case ModResourceType.Chunk:
                            if (ImportChunk(resource, errors)) ok++; else fail++;
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
        });

        string summary =
            $"Import finished.\n\nOK: {ok}\nFailed: {fail}\nSkipped: {skip}\n\n" +
            "Next: File → Save As… a new .fbproject so MMC re-serializes assets correctly.";
        if (errors.Count > 0)
            summary += "\n\nDetails:\n" + string.Join("\n", errors.Take(20));
        if (errors.Count > 20)
            summary += $"\n…and {errors.Count - 20} more";

        FrostyMessageBox.Show(summary, "FrostyConvert");
        App.Logger.Log("FrostyConvert import: ok={0} fail={1} file={2}", ok, fail, Path.GetFileName(path));
    });

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

        Exception? lastError = null;

        // Try official factory first (picks Riff / RiffPGA / V2 based on EbxVersion).
        foreach (bool useProjectFactory in new[] { false, true })
        {
            try
            {
                using var ms = new MemoryStream(data);
                EbxReader reader = useProjectFactory
                    ? EbxReader.CreateProjectReader(ms, App.FileSystemManager, false, true)
                    : EbxReader.CreateReader(ms, App.FileSystemManager, false);

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
                // ctor(Stream, FileSystemManager, bool patched)
                object? inst = Activator.CreateInstance(t, ms, App.FileSystemManager, false);
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

    private static bool ImportRes(FbmodResource resource, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(resource.Name) || resource.Data == null || resource.Data.Length == 0)
            return false;
        if (App.AssetManager.GetResEntry(resource.Name) == null)
        {
            errors.Add($"Res '{resource.Name}': not in game");
            return false;
        }
        if (resource.HasHandler)
        {
            errors.Add($"Res '{resource.Name}': handler not supported in plugin v1");
            return false;
        }
        App.AssetManager.ModifyRes(resource.Name, resource.Data, resource.ResMeta);
        return true;
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
        try
        {
            App.AssetManager.ModifyChunk(id, resource.Data, null);
            return true;
        }
        catch (Exception ex)
        {
            errors.Add($"Chunk '{id}': {ex.Message}");
            return false;
        }
    }
}
