using System.Security.Cryptography;
using System.Text;
using FrostyConvert.Core.Compression;
using FrostyConvert.Core.Mod;
using FrostyConvert.Core.Project;
// Oodle is in Compression namespace

namespace FrostyConvert.Core.Convert;

/// <summary>
/// Offline conversion of a parsed binary <c>.fbmod</c> into a v14 <c>.fbproject</c> document.
/// Does not require a game install. Bundle hashes are stored as <c>0x........</c> placeholders
/// (Editor resolves only when matching bundles exist). EBX is CAS-decompressed when possible;
/// handler payloads are stored as custom <c>ModifiedResource</c> blobs.
/// </summary>
public static class ModToProjectConverter
{
    public static (ProjectDocument Project, ConversionReport Report) Convert(FbmodFile mod)
    {
        var report = new ConversionReport
        {
            InputPath = mod.Path,
            ProfileName = mod.ProfileName,
            GameVersion = mod.GameVersion,
        };

        if (mod.Format != FbmodFormatKind.Binary)
        {
            report.Errors.Add(
                "Legacy (DbObject) .fbmod is not supported offline. " +
                "Open the original project in MMC Editor and export a binary .fbmod (v5+), " +
                "or use Tools → Import Frosty Mod on a binary export. " +
                "Pre-binary Frosty archive mods cannot be recovered by this tool.");
            report.Success = false;
            return (new ProjectDocument(), report);
        }

        var project = new ProjectDocument
        {
            Version = FbprojectConstants.FormatVersion,
            ProfileName = mod.ProfileName,
            CreationDate = DateTime.UtcNow,
            ModifiedDate = DateTime.UtcNow,
            GameVersion = unchecked((uint)mod.GameVersion),
            Title = mod.Details?.Title ?? Path.GetFileNameWithoutExtension(mod.Path),
            Author = mod.Details?.Author ?? "Unknown (recovered)",
            Category = mod.Details?.Category ?? "",
            ModVersion = mod.Details?.Version ?? "0.0",
            Description = BuildDescription(mod),
        };

        // Embedded icon / screenshots (first 5 embeds conventionally)
        var embeds = mod.Resources.Where(r => r.Type == ModResourceType.Embedded).ToList();
        if (embeds.Count > 0 && embeds[0].Data is { Length: > 0 })
            project.Icon = embeds[0].Data;
        for (int i = 0; i < 4 && i + 1 < embeds.Count; i++)
        {
            if (embeds[i + 1].Data is { Length: > 0 })
                project.Screenshots[i] = embeds[i + 1].Data;
        }

        foreach (var resource in mod.Resources)
        {
            try
            {
                switch (resource.Type)
                {
                    case ModResourceType.Embedded:
                        break;
                    case ModResourceType.Bundle:
                        ConvertBundle(resource, project, report);
                        break;
                    case ModResourceType.Ebx:
                        ConvertEbx(resource, project, report);
                        break;
                    case ModResourceType.Res:
                        ConvertRes(resource, project, report);
                        break;
                    case ModResourceType.Chunk:
                        ConvertChunk(resource, project, report);
                        break;
                    default:
                        report.Warnings.Add($"Skipped unknown resource type {(int)resource.Type} '{resource.Name}'.");
                        break;
                }
            }
            catch (Exception ex)
            {
                report.Warnings.Add($"Failed resource '{resource.Name}' ({resource.Type}): {ex.Message}");
            }
        }

        report.EbxCount = project.ModifiedEbx.Count;
        report.ResCount = project.ModifiedRes.Count;
        report.ChunkCount = project.ModifiedChunks.Count;
        report.BundleCount = project.AddedBundles.Count;
        report.HandlerCount = project.ModifiedEbx.Count(e => e.IsCustomHandler)
            + project.ModifiedRes.Count(r => r.HasModifiedData && r.Sha1 is not null && r.Sha1.All(b => b == 0));
        report.LegacyCount = project.LegacyEntries.Count;

        bool hasAssets = project.ModifiedEbx.Count + project.ModifiedRes.Count
            + project.ModifiedChunks.Count + project.AddedBundles.Count > 0;
        report.Success = report.Errors.Count == 0 && hasAssets;

        if (!report.Success && report.Errors.Count == 0)
            report.Warnings.Add("No convertible assets found in mod.");

        if (report.Success)
        {
            report.Warnings.Add(
                "Offline recovery: EBX is stored as decompressed game-format bytes. " +
                "The Madden/CFB editor may still need to re-save assets once so they match project EBX layout. " +
                "Open with the matching profile (e.g. CollegeFB27).");
        }

        return (project, report);
    }

    public static ConversionReport ConvertToFile(string fbmodPath, string fbprojectPath, string? oodleDllPath = null)
    {
        TryBindOodle(oodleDllPath, Path.GetDirectoryName(Path.GetFullPath(fbmodPath)));

        var mod = FbmodReader.Read(fbmodPath, loadResourceData: true);
        var (project, report) = Convert(mod);
        report.OutputPath = fbprojectPath;

        if (Oodle.IsBound)
            report.Warnings.Insert(0, $"Oodle native library: {Oodle.BoundPath}");
        else
            report.Warnings.Insert(0,
                "Oodle native library not found (expected oodle-data-shared.dll). " +
                "Falling back to OozSharp (limited). Optional: --oodle path\\to\\oo2core_win64.dll.");

        // Refuse to write a project that would crash the Editor (no ebx recovered + decompress errors).
        if (report.Errors.Count > 0 && project.ModifiedEbx.Count == 0)
        {
            report.Success = false;
            report.Errors.Add(
                "No project written: every ebx failed decompression. See errors above.");
            return report;
        }

        if (report.Errors.Count > 0)
        {
            // Partial project: only successfully converted assets (never broken custom-handler stubs).
            report.Success = false;
            report.Warnings.Insert(0,
                "Partial project written — some assets were skipped due to errors. Do not treat this as a full recovery.");
        }

        FbprojectWriter.Write(fbprojectPath, project);
        if (report.Errors.Count == 0)
            report.Success = true;
        return report;
    }

    private static void TryBindOodle(string? oodleDllPath, string? modDir)
    {
        if (!string.IsNullOrWhiteSpace(oodleDllPath))
        {
            if (Directory.Exists(oodleDllPath))
                Oodle.TryBindFromSearchPaths(new[] { oodleDllPath });
            else
                Oodle.TryBind(oodleDllPath);
            if (Oodle.IsBound)
                return;
        }

        Oodle.TryBindFromSearchPaths(new[]
        {
            modDir,
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
            // Common EA install roots (best-effort; user can override with --oodle)
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "EA Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EA Games"),
            @"C:\Program Files\EA Games",
            @"C:\Program Files (x86)\Origin Games",
            @"C:\Program Files\Epic Games",
        });
    }

    private static string BuildDescription(FbmodFile mod)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(mod.Details?.Description))
            sb.AppendLine(mod.Details!.Description.Trim());
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("Recovered from compiled .fbmod by FrostyConvert.");
        if (!string.IsNullOrWhiteSpace(mod.Details?.Link))
            sb.AppendLine("Original link: " + mod.Details!.Link);
        sb.AppendLine($"Source gameVersion: {mod.GameVersion}");
        sb.AppendLine($"Source mod format version: {mod.Version}");
        return sb.ToString().Trim();
    }

    private static void ConvertBundle(FbmodResource resource, ProjectDocument project, ConversionReport report)
    {
        project.AddedBundles.Add(new ProjectBundle
        {
            Name = resource.Name,
            SuperBundleName = resource.SuperBundleHash != 0
                ? $"0x{resource.SuperBundleHash:x8}"
                : "<unknown>",
            Type = 0,
        });
        report.BundleCount++;
    }

    private static void ConvertEbx(FbmodResource resource, ProjectDocument project, ConversionReport report)
    {
        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            report.Warnings.Add("Skipped ebx with empty name.");
            return;
        }

        var bundleNames = BundleNames(resource);
        bool hasData = resource.Data is { Length: > 0 };
        bool isHandler = resource.HasHandler;

        if (resource.IsAdded)
        {
            Guid guid = TryExtractEbxGuid(resource.Data) ?? GuidFromName(resource.Name);
            project.AddedEbx.Add(new ProjectEbxAdded { Name = resource.Name, Guid = guid });
        }

        byte[]? payload = null;
        // ONLY true when the mod itself used a custom handler.
        // Never invent this flag — Frosty loads those blobs via ModifiedResource.Read +
        // Activator.CreateInstance(Type.GetType(...)). Bad data yields:
        //   "Value cannot be null. Parameter name: type"
        bool customHandler = isHandler;

        if (hasData)
        {
            if (isHandler)
            {
                // Handler blobs are ModifiedResource.Save() — not CAS-compressed.
                payload = resource.Data;
                if (resource.HandlerHash == FbmodConstants.LegacyHandlerHash)
                {
                    report.Warnings.Add($"Ebx '{resource.Name}' has legacy handler hash; stored as custom handler data.");
                }
            }
            else
            {
                try
                {
                    payload = CasBlockDecompressor.Decompress(resource.Data!);
                }
                catch (CasDecompressException ex)
                {
                    // Do NOT write compressed/raw bytes as project ebx or as custom-handler data.
                    // Either path crashes or corrupts the Editor load.
                    report.Errors.Add(
                        $"Ebx '{resource.Name}': cannot decompress ({ex.Message}). " +
                        "Pass --oodle path\\to\\oo2core_win64.dll (from the CFB/Madden game or toolsuite folder).");
                    return;
                }
            }
        }

        if (payload is not { Length: > 0 } && !isHandler)
        {
            report.Warnings.Add($"Ebx '{resource.Name}': no payload; skipped.");
            return;
        }

        project.ModifiedEbx.Add(new ProjectEbxModified
        {
            Name = resource.Name,
            AddedBundleNames = bundleNames,
            HasModifiedData = payload is { Length: > 0 },
            IsTransientModified = false,
            UserData = resource.UserData ?? "",
            IsCustomHandler = customHandler,
            Data = payload,
        });
    }

    private static void ConvertRes(FbmodResource resource, ProjectDocument project, ConversionReport report)
    {
        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            report.Warnings.Add("Skipped res with empty name.");
            return;
        }

        if (resource.IsAdded)
        {
            project.AddedRes.Add(new ProjectResAdded
            {
                Name = resource.Name,
                ResRid = resource.ResRid,
                ResType = resource.ResType,
                ResMeta = resource.ResMeta is { Length: > 0 } ? resource.ResMeta : new byte[16],
            });
        }

        bool isHandler = resource.HasHandler;
        byte[]? sha1 = resource.Sha1;
        byte[]? data = resource.Data;

        // Handler res: sha1 zero signals ModifiedResource in project format
        if (isHandler)
        {
            sha1 = new byte[20];
            report.HandlerCount++;
        }

        project.ModifiedRes.Add(new ProjectResModified
        {
            Name = resource.Name,
            AddedBundleNames = BundleNames(resource),
            HasModifiedData = data is { Length: > 0 } || isHandler,
            Sha1 = sha1,
            OriginalSize = resource.Size,
            ResMeta = resource.ResMeta,
            UserData = resource.UserData ?? "",
            Data = data,
        });
    }

    private static void ConvertChunk(FbmodResource resource, ProjectDocument project, ConversionReport report)
    {
        if (!Guid.TryParse(resource.Name, out Guid id))
        {
            report.Warnings.Add($"Skipped chunk with invalid Guid name '{resource.Name}'.");
            return;
        }

        // Legacy collector handler
        if (resource.HandlerHash == FbmodConstants.LegacyHandlerHash && resource.Data is { Length: > 0 })
        {
            ParseLegacyCollector(resource, id, project, report);
            return;
        }

        if (resource.IsAdded)
        {
            project.AddedChunks.Add(new ProjectChunkAdded
            {
                Id = id,
                H32 = resource.H32,
            });
        }

        bool addToChunkBundle = resource.IsTocChunk;
        project.ModifiedChunks.Add(new ProjectChunkModified
        {
            Id = id,
            AddedBundleNames = BundleNames(resource),
            FirstMip = resource.FirstMip,
            H32 = resource.H32,
            HasModifiedData = resource.Data is { Length: > 0 },
            Sha1 = resource.Sha1,
            LogicalOffset = resource.LogicalOffset,
            LogicalSize = resource.LogicalSize != 0 ? resource.LogicalSize : (uint)Math.Min(resource.Size, uint.MaxValue),
            RangeStart = resource.RangeStart,
            RangeEnd = resource.RangeEnd,
            AddToChunkBundle = addToChunkBundle,
            UserData = resource.UserData ?? "",
            Data = resource.Data,
        });
    }

    /// <summary>
    /// Legacy collector payload: repeated records of
    /// hash(i32) + chunkId(Guid) + offset + compressedOffset + compressedSize + size (all i64).
    /// </summary>
    private static void ParseLegacyCollector(
        FbmodResource resource,
        Guid collectorChunkId,
        ProjectDocument project,
        ConversionReport report)
    {
        byte[] data = resource.Data!;
        const int recordSize = 4 + 16 + 8 * 4;
        if (data.Length % recordSize != 0)
        {
            report.Warnings.Add(
                $"Legacy collector chunk {collectorChunkId}: payload size {data.Length} not aligned to {recordSize}; storing as modified chunk instead.");
            project.ModifiedChunks.Add(new ProjectChunkModified
            {
                Id = collectorChunkId,
                HasModifiedData = true,
                Sha1 = resource.Sha1,
                Data = data,
                H32 = resource.H32,
                FirstMip = resource.FirstMip,
                UserData = resource.UserData ?? "",
                AddToChunkBundle = true,
            });
            return;
        }

        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        int count = data.Length / recordSize;
        for (int i = 0; i < count; i++)
        {
            int hash = br.ReadInt32();
            Guid chunkId = new Guid(br.ReadBytes(16));
            long offset = br.ReadInt64();
            long compressedOffset = br.ReadInt64();
            long compressedSize = br.ReadInt64();
            long size = br.ReadInt64();

            string name = !string.IsNullOrEmpty(resource.UserData)
                ? $"{resource.UserData}|0x{hash:x8}"
                : $"legacy_0x{hash:x8}";

            project.LegacyEntries.Add(new ProjectLegacyEntry
            {
                Name = name,
                ChunkId = chunkId,
                Offset = offset,
                CompressedOffset = compressedOffset,
                CompressedSize = compressedSize,
                Size = size,
                LinkedAssets =
                {
                    new ProjectLinkedAsset { AssetType = "chunk", ChunkId = chunkId },
                },
            });
        }

        // Also keep collector chunk modification if it was a real data chunk
        if (resource.IsModified && resource.ResourceIndex >= 0)
        {
            // Collector resource data is the index table, not chunk bytes — don't add as chunk data.
        }

        report.LegacyCount += count;
    }

    private static List<string> BundleNames(FbmodResource resource) =>
        resource.AddedBundleHashes.Select(h => $"0x{h:x8}").ToList();

    private static Guid GuidFromName(string name)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(name.ToLowerInvariant()));
        return new Guid(hash);
    }

    /// <summary>Best-effort: many ebx headers start with partition Guid at offset 0 or after magic.</summary>
    private static Guid? TryExtractEbxGuid(byte[]? data)
    {
        if (data is null || data.Length < 16)
            return null;
        try
        {
            // Decompress first if needed
            byte[] raw = CasBlockDecompressor.LooksLikeCasStream(data)
                ? CasBlockDecompressor.Decompress(data)
                : data;
            if (raw.Length < 16)
                return null;
            // Frostbite EBX often has file GUID at 0x0 in some versions — use first 16 bytes if non-zero
            var g = new Guid(raw.AsSpan(0, 16).ToArray());
            if (g == Guid.Empty)
                return null;
            return g;
        }
        catch
        {
            return null;
        }
    }
}
