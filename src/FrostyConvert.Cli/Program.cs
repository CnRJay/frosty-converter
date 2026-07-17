using System.Text;
using FrostyConvert.Core.Convert;
using FrostyConvert.Core.FifaMod;
using FrostyConvert.Core.Inspect;
using FrostyConvert.Core.Legacy;
using FrostyConvert.Core.Mod;
using FrostyConvert.Core.Project;

namespace FrostyConvert.Cli;

internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitValidation = 1;
    private const int ExitPartial = 2;
    private const int ExitFailure = 3;

    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitFailure;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0 || HasFlag(args, "-h") || HasFlag(args, "--help"))
        {
            PrintHelp();
            return args.Length == 0 ? ExitValidation : ExitOk;
        }

        bool inspect = HasFlag(args, "--inspect");
        bool inspectProject = HasFlag(args, "--inspect-project");
        string? reportPath = GetOption(args, "--report");
        string? extractDir = GetOption(args, "--extract");
        string? extractLegacyDir = GetOption(args, "--extract-legacy");
        string? outputPath = GetOption(args, "-o") ?? GetOption(args, "--output");
        string? oodlePath = GetOption(args, "--oodle");
        bool json = HasFlag(args, "--json");
        bool promoteTextures = HasFlag(args, "--promote-legacy-textures");
        string? texturePrefix = GetOption(args, "--texture-prefix") ?? "content/ui/legacy";
        // Default empty = promote every detectable DDS (full Data Explorer recovery)
        string? textureFilter = GetOption(args, "--texture-filter") ?? "";
        string? maxPromoteStr = GetOption(args, "--texture-max");
        int textureMax = 0;
        if (!string.IsNullOrEmpty(maxPromoteStr) && int.TryParse(maxPromoteStr, out int mp))
            textureMax = mp;

        string? inputPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--report" or "--extract" or "--extract-legacy" or "-o" or "--output"
                or "-g" or "--key" or "--plugins" or "--force-profile" or "--oodle"
                or "--texture-prefix" or "--texture-filter" or "--texture-max" or "--texture-template")
            {
                i++;
                continue;
            }
            if (args[i].StartsWith('-'))
                continue;
            inputPath = args[i];
            break;
        }

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            Console.Error.WriteLine("error: missing input path");
            PrintHelp();
            return ExitValidation;
        }

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"error: file not found: {inputPath}");
            return ExitValidation;
        }

        Console.OutputEncoding = Encoding.UTF8;

        if (inspectProject)
            return InspectProject(inputPath, json);

        // Convert mode
        if (!inspect && outputPath is not null)
        {
            // .fifamod → .fifaproject (FIFA Editor Tool recovery path)
            if (FifamodReader.IsFifamod(inputPath) ||
                inputPath.EndsWith(".fifamod", StringComparison.OrdinalIgnoreCase) ||
                outputPath.EndsWith(".fifaproject", StringComparison.OrdinalIgnoreCase))
            {
                if (oodlePath is not null)
                    FrostyConvert.Core.Compression.Oodle.TryBind(oodlePath);

                var fifa = FifamodReader.Read(inputPath, loadResourceData: true, decompress: true);

                if (extractLegacyDir is not null)
                {
                    var ex = LegacyExtractor.Extract(fifa, extractLegacyDir, pathFilter: null);
                    Console.WriteLine(
                        $"Extracted legacy files: written={ex.Written} skipped={ex.Skipped} errors={ex.Errors} → {extractLegacyDir}");
                    foreach (var msg in ex.ErrorMessages)
                        Console.WriteLine($"  extract error: {msg}");
                }

                TexturePromoteResult? promote = null;
                IReadOnlyList<FifamodResource>? extra = null;
                if (promoteTextures)
                {
                    promote = TextureAssetPromoter.Promote(fifa, new TexturePromoteOptions
                    {
                        NamePrefix = texturePrefix,
                        PathFilter = textureFilter ?? "",
                        MaxCount = textureMax,
                    });
                    extra = promote.Resources;
                    Console.WriteLine(
                        $"Promote → Data Explorer TextureAssets: promoted={promote.PromotedCount} " +
                        $"(ddsCandidates={promote.CandidateCount}, wrappedRes={promote.WrappedExistingRes}, " +
                        $"filter='{textureFilter}', skipFilter={promote.SkippedFilter}, " +
                        $"skipNotDds={promote.SkippedNotDds}, errors={promote.SkippedErrors})");
                    if (promote.LegacyExtHistogram.Count > 0)
                    {
                        Console.WriteLine("  Legacy file types in mod:");
                        foreach (var kv in promote.LegacyExtHistogram.OrderByDescending(k => k.Value))
                            Console.WriteLine($"    {kv.Key}: {kv.Value}");
                    }
                    foreach (var note in promote.NonTextureLegacyNotes)
                        Console.WriteLine($"  note: {note}");
                    foreach (var n in promote.SampleNames.Take(8))
                        Console.WriteLine($"  + {n}");
                    foreach (var e in promote.Errors)
                        Console.WriteLine($"  promote: {e}");
                    if (promote.PromotedCount == 0 && promote.Errors.Count > 0)
                    {
                        Console.Error.WriteLine("error: texture promotion produced no assets.");
                        return ExitFailure;
                    }
                }

                var writable = FifaprojectWriter.CountWritable(fifa, extra);
                FifaprojectWriter.Write(outputPath, fifa, extra);

                int err = fifa.Resources.Count(r => r.DecompressError is not null);
                long outLen = new FileInfo(outputPath).Length;
                Console.WriteLine($"Wrote FIFA Editor project: {outputPath} ({outLen:N0} bytes)");
                Console.WriteLine($"  game={fifa.GameName} title={fifa.Details.Title}");
                int brtEbx = fifa.Resources.Count(r => r.BrtAddition is { BrtNameHash: not 0 });
                Console.WriteLine(
                    $"  written: ebx={writable.Ebx}  res={writable.Res}  " +
                    $"chunks={writable.Chunks}  decompress_errors={err}");
                Console.WriteLine(
                    $"  header: screenshots={fifa.Details.Screenshots.Count} locale={fifa.LocaleIniFiles.Count} " +
                    $"initfs={fifa.InitFsFiles.Count} playerLua={fifa.PlayerLuaMods.Count} " +
                    $"kitLua={fifa.PlayerKitLuaMods.Count} addedBundles={fifa.AddedBundles.Count}");
                Console.WriteLine(
                    $"  brt: ebxWithBrt={brtEbx}  collectors={fifa.Collectors.Count}  " +
                    $"brtTables={fifa.BundleRefTables.Count}");
                if (promote is not null)
                    Console.WriteLine($"  (includes {promote.PromotedCount} promoted TextureAssets for Data Explorer)");

                // Offline self-check against EditorProject.Load layout
                try
                {
                    var check = FifaprojectReader.ReadSummary(outputPath);
                    Console.WriteLine(
                        $"  verify: chunks={check.ChunkCount} legacy={check.LegacyChunkCount} " +
                        $"(added paths={check.LegacyAddedCount}) res={check.ResCount} ebx={check.EbxCount} " +
                        $"ebxWithBrt={check.EbxWithBrtCount} bundles={check.AddedBundleCount}");
                    Console.WriteLine(
                        $"  verify header: shots={check.ScreenshotCount} locale={check.LocaleIniCount} " +
                        $"initfs={check.InitFsCount} lua={check.PlayerLuaKeyCount}/{check.PlayerKitLuaKeyCount}");
                    foreach (var w in check.Warnings)
                        Console.WriteLine($"  verify warning: {w}");

                    if (promote is not null && promote.PromotedCount > 0)
                    {
                        Console.WriteLine();
                        Console.WriteLine("  *** Promoted TextureAssets → use DATA EXPLORER (Show Only Modified) ***");
                        Console.WriteLine($"  Look under: {texturePrefix}/…");
                        Console.WriteLine("  Open a TextureAsset to preview/edit in the Texture editor.");
                        Console.WriteLine("  Original legacy files remain in Legacy Explorer for mod export fidelity.");
                    }
                    else if (check.LegacyChunkCount > 0)
                    {
                        Console.WriteLine();
                        Console.WriteLine("  *** This mod is mostly LEGACY files (UI / .big / fonts), not EBX. ***");
                        Console.WriteLine("  Tip: re-run with --promote-legacy-textures to put crest/UI .dds into Data Explorer.");
                        Console.WriteLine("  Or use Legacy Explorer + Show Only Modified for data/ui/… paths.");
                        Console.WriteLine("  Sample legacy paths:");
                        foreach (var p in fifa.Resources
                                     .Where(x => x.Kind == FifamodResourceKind.Chunk && !string.IsNullOrEmpty(x.LegacyFileName))
                                     .Select(x => x.LegacyFileName!)
                                     .Distinct(StringComparer.OrdinalIgnoreCase)
                                     .Take(10))
                            Console.WriteLine($"    - {p}");
                    }
                    if (check.EbxCount > 0)
                    {
                        Console.WriteLine("  Sample EBX (Data Explorer):");
                        foreach (var n in check.SampleEbxNames.Take(6))
                            Console.WriteLine($"    - {n}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  verify failed (project may not load in FET): {ex.Message}");
                    return ExitFailure;
                }

                Console.WriteLine();
                var readiness = ConversionReadiness.ForFifamod(fifa, writable.Ebx, writable.Res, writable.Chunks, err);
                Console.WriteLine(readiness.ToText());
                Console.WriteLine();

                if (reportPath is not null)
                {
                    var rep = FifamodInspectReport.FromMod(fifa);
                    string? reportDir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
                    if (!string.IsNullOrEmpty(reportDir))
                        Directory.CreateDirectory(reportDir);
                    File.WriteAllText(reportPath, rep.ToJson(), Encoding.UTF8);
                }

                if (!readiness.Success)
                    return ExitPartial;

                if (writable.Ebx + writable.Res + writable.Chunks == 0)
                {
                    Console.Error.WriteLine("error: no assets were written into the project (empty conversion).");
                    return ExitFailure;
                }
                if (err > 0)
                    return ExitPartial;
                return ExitOk;
            }

            var conv = ModToProjectConverter.ConvertToFile(inputPath, outputPath, oodlePath);
            Console.WriteLine(json ? conv.ToJson() : conv.ToText());

            FbmodFile? fbmodForReady = null;
            try { fbmodForReady = FbmodReader.Read(inputPath, loadResourceData: false); }
            catch { /* ignore */ }
            if (fbmodForReady is not null)
            {
                var ready = ConversionReadiness.ForOfflineFbmod(fbmodForReady, conv);
                Console.WriteLine();
                Console.WriteLine(ready.ToText());
            }

            if (reportPath is not null)
            {
                string? reportDir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
                if (!string.IsNullOrEmpty(reportDir))
                    Directory.CreateDirectory(reportDir);
                File.WriteAllText(reportPath, conv.ToJson(), Encoding.UTF8);
                Console.Error.WriteLine($"report written: {reportPath}");
            }

            // Verify project on success
            if (File.Exists(outputPath) && conv.Errors.Count == 0)
            {
                try
                {
                    var proj = FbprojectReader.ReadSummary(outputPath);
                    Console.Error.WriteLine(
                        $"project check: v{proj.Version} profile={proj.ProfileName} ebx={proj.ModifiedEbxCount} " +
                        $"customHandlers={proj.ModifiedEbx.Count(e => e.IsCustomHandler)}");
                    foreach (var e in proj.ModifiedEbx)
                    {
                        Console.Error.WriteLine(
                            $"  ebx {e.Name} data={e.DataLength} magic={e.DataMagic} custom={e.IsCustomHandler}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"project check failed: {ex.Message}");
                }
            }

            if (conv.Errors.Count > 0)
                return ExitFailure;
            // Informational warnings (oodle path, offline notes) are not partial failures
            bool hardWarnings = conv.Warnings.Any(w =>
                w.Contains("skipped", StringComparison.OrdinalIgnoreCase)
                || w.Contains("Partial project", StringComparison.OrdinalIgnoreCase)
                || w.Contains("failed", StringComparison.OrdinalIgnoreCase));
            if (hardWarnings)
                return ExitPartial;
            // Offline path always recommend plugin — ExitPartial if only offline inventory
            if (fbmodForReady is { Format: FbmodFormatKind.Binary } &&
                (inputPath.Contains("CollegeFB", StringComparison.OrdinalIgnoreCase) ||
                 inputPath.Contains("Madden", StringComparison.OrdinalIgnoreCase) ||
                 (fbmodForReady.ProfileName?.Contains("College", StringComparison.OrdinalIgnoreCase) ?? false) ||
                 (fbmodForReady.ProfileName?.Contains("Madden", StringComparison.OrdinalIgnoreCase) ?? false)))
            {
                Console.Error.WriteLine(
                    "note: for CFB/Madden editing use MMC plugin import (offline .fbproject is limited for RIFF).");
            }
            return ExitOk;
        }

        if (!inspect)
        {
            Console.Error.WriteLine("Specify --inspect to inventory a mod, --inspect-project for a project, or -o <file.fbproject> to convert.");
            PrintHelp();
            return ExitValidation;
        }

        // Auto-detect .fifamod (FETM) vs .fbmod
        if (FifamodReader.IsFifamod(inputPath) ||
            inputPath.EndsWith(".fifamod", StringComparison.OrdinalIgnoreCase))
        {
            if (oodlePath is not null)
                FrostyConvert.Core.Compression.Oodle.TryBind(oodlePath);

            var fifa = FifamodReader.Read(inputPath, loadResourceData: true, decompress: true);
            var fifaReport = FifamodInspectReport.FromMod(fifa);

            if (extractDir is not null)
                ExtractFifamodResources(fifa, extractDir);

            Console.WriteLine(json ? fifaReport.ToJson() : fifaReport.ToText());

            if (reportPath is not null)
            {
                string outDir = Path.GetDirectoryName(Path.GetFullPath(reportPath))!;
                if (!string.IsNullOrEmpty(outDir))
                    Directory.CreateDirectory(outDir);
                File.WriteAllText(reportPath, fifaReport.ToJson(), Encoding.UTF8);
                Console.Error.WriteLine($"report written: {reportPath}");
            }

            if (fifaReport.DecompressErrorCount > 0 && fifaReport.DecompressedCount == 0)
                return ExitFailure;
            if (fifaReport.DecompressErrorCount > 0)
                return ExitPartial;
            return ExitOk;
        }

        var mod = FbmodReader.Read(inputPath, loadResourceData: true);
        var report = InspectReport.FromMod(mod);

        if (extractDir is not null)
            ExtractResources(mod, extractDir);

        Console.WriteLine(json ? report.ToJson() : report.ToText());

        if (reportPath is not null)
        {
            string outDir = Path.GetDirectoryName(Path.GetFullPath(reportPath))!;
            if (!string.IsNullOrEmpty(outDir))
                Directory.CreateDirectory(outDir);
            File.WriteAllText(reportPath, report.ToJson(), Encoding.UTF8);
            Console.Error.WriteLine($"report written: {reportPath}");
        }

        return ExitOk;
    }

    private static int InspectProject(string path, bool json)
    {
        var s = FbprojectReader.ReadSummary(path);
        if (json)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(s, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
            }));
        }
        else
        {
            Console.WriteLine($"File:       {path}");
            Console.WriteLine($"Version:    {s.Version}");
            Console.WriteLine($"Profile:    {s.ProfileName}");
            Console.WriteLine($"GameVer:    {s.GameVersion}");
            Console.WriteLine($"Title:      {s.Title}");
            Console.WriteLine($"Author:     {s.Author}");
            Console.WriteLine($"Added:      bundles={s.AddedBundleCount} ebx={s.AddedEbxCount} res={s.AddedResCount} chunks={s.AddedChunkCount}");
            Console.WriteLine($"Modified:   ebx={s.ModifiedEbxCount}");
            foreach (var e in s.ModifiedEbx)
            {
                string flag = e.IsCustomHandler ? " CUSTOM-HANDLER" : "";
                Console.WriteLine($"  [{(e.HasModifiedData ? "mod" : "meta")}] {e.Name}  bytes={e.DataLength} magic={e.DataMagic}{flag}");
            }
        }

        int custom = s.ModifiedEbx.Count(e => e.IsCustomHandler);
        if (custom > 0)
        {
            Console.Error.WriteLine(
                $"warning: {custom} ebx marked custom-handler — Editor may fail with 'Parameter name: type' if data is not ModifiedResource.");
            return ExitPartial;
        }

        return ExitOk;
    }

    private static void ExtractResources(FbmodFile mod, string dir)
    {
        Directory.CreateDirectory(dir);
        int i = 0;
        foreach (var r in mod.Resources)
        {
            if (r.Data is null || r.Data.Length == 0)
            {
                i++;
                continue;
            }

            string safeName = string.Join("_", (r.Name ?? "unnamed").Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "unnamed";
            string file = Path.Combine(dir, $"{i:D4}_{r.Type}_{safeName}.bin");
            File.WriteAllBytes(file, r.Data);
            i++;
        }
        Console.Error.WriteLine($"extracted payloads to: {dir}");
    }

    private static void ExtractFifamodResources(FifamodFile mod, string dir)
    {
        Directory.CreateDirectory(dir);
        int i = 0;
        foreach (var r in mod.Resources)
        {
            byte[]? payload = r.Data ?? r.CompressedData;
            if (payload is null || payload.Length == 0)
            {
                i++;
                continue;
            }

            string safeName = string.Join("_", r.Name.Split(Path.GetInvalidFileNameChars().Concat(new[] { '/' , '\\' }).ToArray()));
            if (string.IsNullOrWhiteSpace(safeName))
                safeName = "unnamed";
            if (safeName.Length > 120)
                safeName = safeName[^120..];
            string suffix = r.Data is not null ? "ebx" : "cas";
            string file = Path.Combine(dir, $"{i:D4}_{suffix}_{safeName}.bin");
            File.WriteAllBytes(file, payload);
            i++;
        }
        Console.Error.WriteLine($"extracted fifamod payloads to: {dir}");
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
"""
FrostyConvert — recover editable projects from compiled Frosty / FIFA mods

Usage:
  fbmod2project <mod.fbmod> --inspect [--json] [--report out.json] [--extract dir]
  fbmod2project <mod.fifamod> --inspect [--oodle dll] [--json] [--report out.json] [--extract dir]
  fbmod2project <mod.fbmod> -o recovered.fbproject [--oodle dll] [--json] [--report out.json]
  fbmod2project <mod.fifamod> -o recovered.fifaproject [--oodle dll] [FIFA options]
  fbmod2project <proj.fbproject> --inspect-project

Options:
  --inspect           Parse .fbmod / .fifamod and print a resource inventory (no game install)
  --inspect-project   Validate a recovered .fbproject (ebx flags / payload magic)
  -o, --output PATH   Write recovered project (.fbproject from .fbmod, .fifaproject from .fifamod)
  --oodle <path>      Override Oodle DLL (oodle-data-shared.dll or oo2core_*.dll)
  --json              Emit reports as JSON
  --report <path>     Write JSON report to a file
  --extract <dir>     Write resource payloads to a directory (inspect)
  --extract-legacy <dir>
                      Write named legacy files (data/ui/…) from a .fifamod (with -o convert)
  --promote-legacy-textures
                      Promote every detectable legacy .dds to TextureAsset EBX for Data
                      Explorer. Also wraps existing content/ Texture RES. Keeps all
                      original legacy files (.big Apt UI, fonts, xml, …) for Legacy Explorer.
  --texture-prefix P  Name prefix for promoted assets (default: content/ui/legacy)
  --texture-filter S  Only promote legacy paths containing S (default: empty = all .dds)
  --texture-max N     Cap number of promoted textures (0 = no limit)
  -h, --help          Show help

Notes:
  .fbmod → offline .fbproject, or use the MMC live-import plugin (preferred for CFB/Madden).

  .fifamod → .fifaproject for FIFA Editor Tool:
    1) convert (add --promote-legacy-textures for crest/UI DDS → Data Explorer)
    2) open FET, load FC26
    3) File → Open Project → recovered.fifaproject
    4) Data Explorer (promoted TextureAssets) and/or Legacy Explorer (original files)
    5) edit, Save / export mod

  Oodle is bundled as oodle-data-shared.dll (UE/OodleUE build).
""");
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static string? GetOption(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}
