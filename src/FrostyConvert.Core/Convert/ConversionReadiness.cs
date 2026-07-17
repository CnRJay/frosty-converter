using System.Text;
using FrostyConvert.Core.FifaMod;
using FrostyConvert.Core.Mod;

namespace FrostyConvert.Core.Convert;

/// <summary>
/// Post-convert / post-import readiness checklist for users and CI.
/// Scores whether a recovered project is likely safe to Save/export after opening in the live editor.
/// </summary>
public sealed class ConversionReadiness
{
    public string Tool { get; init; } = "";
    public string InputPath { get; init; } = "";
    public bool Success { get; set; }
    public int Score { get; set; } // 0–100
    public List<string> Blocking { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> NextSteps { get; } = new();
    public Dictionary<string, int> Counts { get; } = new();

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Readiness: {(Success ? "OK" : "ISSUES")}  score={Score}/100  ({Tool})");
        if (Counts.Count > 0)
        {
            sb.Append("  counts: ");
            sb.AppendLine(string.Join(", ", Counts.Select(kv => $"{kv.Key}={kv.Value}")));
        }
        foreach (var b in Blocking)
            sb.AppendLine($"  BLOCK: {b}");
        foreach (var w in Warnings)
            sb.AppendLine($"  warn:  {w}");
        if (NextSteps.Count > 0)
        {
            sb.AppendLine("  Next steps:");
            for (int i = 0; i < NextSteps.Count; i++)
                sb.AppendLine($"    {i + 1}. {NextSteps[i]}");
        }
        return sb.ToString().TrimEnd();
    }

    public static ConversionReadiness ForFifamod(FifamodFile mod, int writtenEbx, int writtenRes, int writtenChunks, int decompressErrors)
    {
        var r = new ConversionReadiness
        {
            Tool = "fifamod→fifaproject",
            InputPath = mod.Path,
        };

        int brtEbx = mod.Resources.Count(x => x.BrtAddition is { BrtNameHash: not 0 });
        r.Counts["ebx"] = writtenEbx;
        r.Counts["res"] = writtenRes;
        r.Counts["chunks"] = writtenChunks;
        r.Counts["legacyChunks"] = mod.Resources.Count(x =>
            x.Kind == FifamodResourceKind.Chunk && x.ChunkFlags.HasFlag(FifamodChunkFlags.IsLegacy));
        r.Counts["collectors"] = mod.Collectors.Count;
        r.Counts["brtEbx"] = brtEbx;
        r.Counts["locale"] = mod.LocaleIniFiles.Count;
        r.Counts["initfs"] = mod.InitFsFiles.Count;
        r.Counts["decompressErrors"] = decompressErrors;

        if (writtenEbx + writtenRes + writtenChunks == 0)
            r.Blocking.Add("No writable assets (ebx/res/chunks). Project is empty.");

        if (decompressErrors > 0)
            r.Warnings.Add($"{decompressErrors} resource(s) failed decompression — open with game + Oodle and re-save.");

        if (mod.Collectors.Count > 0)
            r.Warnings.Add("Legacy collectors present — open project in FET with game loaded, then File → Save before export.");

        if (brtEbx > 0)
            r.Warnings.Add($"{brtEbx} EBX have BRT registrations — re-export from FET after Save so BRT tables rebuild.");

        // Heuristic: high decompress failure rate may indicate password-locked / encrypted payloads
        int withPayload = mod.Resources.Count(x => x.CompressedSize > 0);
        if (mod.SuspectedPasswordLock)
        {
            r.Blocking.Add(
                "Suspected password-locked .fifamod (FMT Pro). Ask the author for an unlocked export; " +
                "FrostyConvert cannot unlock password-protected mods.");
        }

        if (withPayload > 0 && decompressErrors * 2 >= withPayload && writtenEbx + writtenRes + writtenChunks == 0)
        {
            r.Blocking.Add(
                "Most payloads failed decompression. This may be a password-locked .fifamod (FMT Pro lock) " +
                "or a corrupt file. FrostyConvert cannot unlock password-protected mods without the password/format key.");
        }

        r.NextSteps.Add("Launch FIFA Editor Tool and load the matching game (e.g. FC26).");
        r.NextSteps.Add("File → Open Project → select the recovered .fifaproject.");
        r.NextSteps.Add("File → Save (required — rebuilds collectors, links, and live types).");
        r.NextSteps.Add("Export a NEW .fifamod from FET and test that export in FIFA Mod Manager.");
        r.NextSteps.Add("Do not launch the original locked/compiled mod mixed with the recovered project without re-export.");

        r.Score = ComputeScore(r, writtenEbx + writtenRes + writtenChunks, decompressErrors, withPayload);
        r.Success = r.Blocking.Count == 0 && r.Score >= 50;
        return r;
    }

    public static ConversionReadiness ForFbmodImport(FbmodFile mod, int ok, int fail, int okEbx, int okRes, int okChunk, int okBundle, int okHandler)
    {
        var r = new ConversionReadiness
        {
            Tool = "fbmod→MMC live import",
            InputPath = mod.Path,
        };

        r.Counts["ok"] = ok;
        r.Counts["fail"] = fail;
        r.Counts["ebx"] = okEbx;
        r.Counts["res"] = okRes;
        r.Counts["chunk"] = okChunk;
        r.Counts["bundle"] = okBundle;
        r.Counts["handler"] = okHandler;
        r.Counts["resources"] = mod.Resources.Count;
        r.Counts["handlersInMod"] = mod.Resources.Count(x => x.HasHandler);

        if (mod.Format != FbmodFormatKind.Binary)
        {
            r.Blocking.Add("Legacy (DbObject) .fbmod — re-export as binary from MMC 1.1.0.1+ or convert source project.");
        }

        if (ok == 0)
            r.Blocking.Add("No assets imported successfully.");

        if (fail > 0)
            r.Warnings.Add($"{fail} resource(s) failed import — check Details for handlers/missing TOC entries.");

        int handlersInMod = mod.Resources.Count(x => x.HasHandler);
        if (handlersInMod > okHandler && okHandler == 0 && handlersInMod > 0)
            r.Warnings.Add("Mod contains custom handlers but none applied — ensure MMC plugins that register those handlers are installed.");

        if (mod.Version >= 8)
            r.Warnings.Add("Encrypted v8 mod — requires MMC 1.1.0.1+ Mod Manager to launch exported mods.");

        r.NextSteps.Add("In MMC Editor: File → Save As… a NEW .fbproject (do not overwrite blindly).");
        r.NextSteps.Add("Confirm Data Explorer → Show Modified lists expected assets.");
        r.NextSteps.Add("Export a new .fbmod from MMC and test in MMC Mod Manager (matching game TU).");

        int totalAttempt = ok + fail;
        r.Score = ComputeScore(r, ok, fail, totalAttempt);
        r.Success = r.Blocking.Count == 0 && ok > 0;
        return r;
    }

    public static ConversionReadiness ForOfflineFbmod(FbmodFile mod, ConversionReport report)
    {
        var r = new ConversionReadiness
        {
            Tool = "fbmod→fbproject (offline)",
            InputPath = mod.Path,
            Success = report.Success,
        };
        r.Counts["ebx"] = report.EbxCount;
        r.Counts["res"] = report.ResCount;
        r.Counts["chunks"] = report.ChunkCount;
        r.Counts["handlers"] = report.HandlerCount;
        r.Counts["errors"] = report.Errors.Count;

        if (mod.Format != FbmodFormatKind.Binary)
            r.Blocking.Add("Legacy .fbmod not supported offline. Use MMC plugin live import after binary re-export.");

        foreach (var e in report.Errors.Take(8))
            r.Blocking.Add(e);

        r.Warnings.Add(
            "Offline .fbproject is weak for CFB/Madden RIFF property grid. Prefer MMC plugin: Tools → Import Frosty Mod.");

        r.NextSteps.Add("Prefer: MMC Editor plugin → Import .fbmod → Save As .fbproject.");
        r.NextSteps.Add("If using offline project: open only as inventory; re-import live for editing.");

        r.Score = report.Success ? 40 : 10; // cap offline score — always recommend plugin
        if (report.Success && report.EbxCount + report.ResCount + report.ChunkCount > 0)
            r.Score = 55;
        r.Success = report.Success && r.Blocking.Count == 0;
        return r;
    }

    private static int ComputeScore(ConversionReadiness r, int good, int bad, int total)
    {
        int score = 100;
        score -= r.Blocking.Count * 35;
        score -= Math.Min(40, r.Warnings.Count * 8);
        if (total > 0 && bad > 0)
            score -= (int)(40.0 * bad / total);
        if (good == 0)
            score = Math.Min(score, 20);
        return Math.Clamp(score, 0, 100);
    }
}
