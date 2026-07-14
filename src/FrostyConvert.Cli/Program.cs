using System.Text;
using FrostyConvert.Core.Convert;
using FrostyConvert.Core.FifaMod;
using FrostyConvert.Core.Inspect;
using FrostyConvert.Core.Mod;
using FrostyConvert.Core.Project;
// FifaprojectWriter lives in Project namespace

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
        string? outputPath = GetOption(args, "-o") ?? GetOption(args, "--output");
        string? oodlePath = GetOption(args, "--oodle");
        bool json = HasFlag(args, "--json");

        string? inputPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--report" or "--extract" or "-o" or "--output" or "-g" or "--key"
                or "--plugins" or "--force-profile" or "--oodle")
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
                FifaprojectWriter.Write(outputPath, fifa);

                int ebx = fifa.Resources.Count(r => r.Kind == FifamodResourceKind.Ebx && r.Data is { Length: > 0 });
                int err = fifa.Resources.Count(r => r.DecompressError is not null);
                Console.WriteLine($"Wrote FIFA Editor project: {outputPath}");
                Console.WriteLine($"  game={fifa.GameName} title={fifa.Details.Title}");
                Console.WriteLine($"  ebx payloads={ebx}/{fifa.EbxCount}  res={fifa.ResCount}  chunks={fifa.ChunkCount}  decompress_errors={err}");
                Console.WriteLine("  Next: open FIFA Editor Tool → load FC26 → File → Open Project → this .fifaproject");
                Console.WriteLine("  Then File → Save to re-serialize with live types (same idea as MMC import).");

                if (reportPath is not null)
                {
                    var rep = FifamodInspectReport.FromMod(fifa);
                    string? reportDir = Path.GetDirectoryName(Path.GetFullPath(reportPath));
                    if (!string.IsNullOrEmpty(reportDir))
                        Directory.CreateDirectory(reportDir);
                    File.WriteAllText(reportPath, rep.ToJson(), Encoding.UTF8);
                }

                if (err > 0 && ebx == 0)
                    return ExitFailure;
                if (err > 0)
                    return ExitPartial;
                return ExitOk;
            }

            var conv = ModToProjectConverter.ConvertToFile(inputPath, outputPath, oodlePath);
            Console.WriteLine(json ? conv.ToJson() : conv.ToText());

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
  fbmod2project <mod.fifamod> -o recovered.fifaproject [--oodle dll]
  fbmod2project <proj.fbproject> --inspect-project

Options:
  --inspect           Parse .fbmod / .fifamod and print a resource inventory (no game install)
  --inspect-project   Validate a recovered .fbproject (ebx flags / payload magic)
  -o, --output PATH   Write recovered project (.fbproject from .fbmod, .fifaproject from .fifamod)
  --oodle <path>      Override Oodle DLL (oodle-data-shared.dll or oo2core_*.dll)
  --json              Emit reports as JSON
  --report <path>     Write JSON report to a file
  --extract <dir>     Write resource payloads to a directory (inspect)
  -h, --help          Show help

Notes:
  .fbmod → offline .fbproject, or use the MMC live-import plugin (preferred for CFB/Madden).

  .fifamod → .fifaproject for FIFA Editor Tool. FET has no Plugins folder (closed
  single-file app), so live import cannot mirror the MMC plugin DLL. Instead:
    1) convert to .fifaproject
    2) open FET, load FC26
    3) File → Open Project → recovered.fifaproject
    4) edit assets, Save / export mod

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
