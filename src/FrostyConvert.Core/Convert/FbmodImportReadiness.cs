using System.Text;
using FrostyConvert.Core.Mod;

namespace FrostyConvert.Core.Convert;

/// <summary>Readiness checklist for MMC live import (no FIFA types — safe to link into plugin).</summary>
public sealed class FbmodImportReadiness
{
    public bool Success { get; set; }
    public int Score { get; set; }
    public List<string> Blocking { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> NextSteps { get; } = new();
    public Dictionary<string, int> Counts { get; } = new();

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Readiness: {(Success ? "OK" : "ISSUES")}  score={Score}/100  (fbmod→MMC live import)");
        if (Counts.Count > 0)
            sb.AppendLine("  counts: " + string.Join(", ", Counts.Select(kv => $"{kv.Key}={kv.Value}")));
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

    public static FbmodImportReadiness Create(
        FbmodFile mod, int ok, int fail, int okEbx, int okRes, int okChunk, int okBundle, int okHandler)
    {
        var r = new FbmodImportReadiness();
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
            r.Blocking.Add("Legacy (DbObject) .fbmod — re-export as binary from MMC 1.1.0.1+.");

        if (ok == 0)
            r.Blocking.Add("No assets imported successfully.");

        if (fail > 0)
            r.Warnings.Add($"{fail} resource(s) failed import — check Details.");

        int handlersInMod = mod.Resources.Count(x => x.HasHandler);
        if (handlersInMod > 0 && okHandler == 0)
            r.Warnings.Add("Mod has custom handlers but none applied — install MMC plugins that register those handlers.");

        if (mod.Version >= 8)
            r.Warnings.Add("Encrypted v8 mod — needs MMC 1.1.0.1+ Mod Manager for exported mods.");

        r.NextSteps.Add("File → Save As… a NEW .fbproject.");
        r.NextSteps.Add("Confirm Data Explorer → Show Modified lists expected assets.");
        r.NextSteps.Add("Export a new .fbmod from MMC and test in MMC Mod Manager.");

        int score = 100;
        score -= r.Blocking.Count * 35;
        score -= Math.Min(40, r.Warnings.Count * 8);
        int total = ok + fail;
        if (total > 0 && fail > 0)
            score -= (int)(40.0 * fail / total);
        if (ok == 0)
            score = Math.Min(score, 20);
        r.Score = score < 0 ? 0 : score > 100 ? 100 : score;
        r.Success = r.Blocking.Count == 0 && ok > 0;
        return r;
    }
}
