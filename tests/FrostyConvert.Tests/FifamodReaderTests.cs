using FrostyConvert.Core.Compression;
using FrostyConvert.Core.FifaMod;
using FrostyConvert.Core.Inspect;
using FrostyConvert.Core.Project;

namespace FrostyConvert.Tests;

public class FifamodReaderTests
{
    private static string? FindFixture()
    {
        string[] roots =
        {
            Path.Combine(AppContext.BaseDirectory, "fixtures"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures"),
            Path.Combine(Directory.GetCurrentDirectory(), "tests", "fixtures"),
            Path.Combine(Directory.GetCurrentDirectory(), "fixtures"),
        };

        foreach (var root in roots)
        {
            string full = Path.GetFullPath(root);
            if (!Directory.Exists(full))
                continue;
            string[] files = Directory.GetFiles(full, "*.fifamod");
            if (files.Length > 0)
                return files[0];
        }

        return null;
    }

    [Fact]
    public void IsFifamod_DetectsMagic()
    {
        string? path = FindFixture();
        if (path is null)
            return;

        Assert.True(FifamodReader.IsFifamod(path));
    }

    [Fact]
    public void Read_Fc26Sample_ParsesOfficialHeaderAndEbxTable()
    {
        string? path = FindFixture();
        Assert.True(path is not null, "Expected tests/fixtures/*.fifamod sample");

        var meta = FifamodReader.Read(path!, loadResourceData: false, decompress: false);
        Assert.Equal("FC26", meta.GameName);
        Assert.True(meta.ModVersion >= 1);
        Assert.False(string.IsNullOrWhiteSpace(meta.Details.Title));
        Assert.False(string.IsNullOrWhiteSpace(meta.Details.Author));
        Assert.True(meta.EbxCount >= 100, $"ebxCount={meta.EbxCount}");
        Assert.Equal(meta.EbxCount, meta.Resources.Count(r => r.Kind == FifamodResourceKind.Ebx));
        Assert.True(meta.DataBaseOffset > 0);
    }

    [Fact]
    public void Read_Fc26Sample_DecompressesRiffEbx()
    {
        string? path = FindFixture();
        Assert.True(path is not null, "Expected tests/fixtures/*.fifamod sample");

        Assert.True(Oodle.IsBound || Oodle.TryBindFromSearchPaths(new[]
        {
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "third_party", "oodle", "bin"),
        }), "oodle-data-shared.dll must be available for fifamod decompress tests");

        var mod = FifamodReader.Read(path!, loadResourceData: true, decompress: true);
        var ebx = mod.Resources.Where(r => r.Kind == FifamodResourceKind.Ebx).ToList();
        Assert.NotEmpty(ebx);

        int shaOk = ebx.Count(r => r.Sha1MatchesCompressed);
        Assert.Equal(ebx.Count, shaOk);

        int riff = 0;
        int errors = 0;
        foreach (var r in ebx.Take(50))
        {
            if (r.DecompressError is not null)
            {
                errors++;
                continue;
            }

            Assert.NotNull(r.Data);
            Assert.Equal(r.UncompressedSize, r.Data!.Length);
            if (r.Data.Length >= 4 &&
                r.Data[0] == (byte)'R' && r.Data[1] == (byte)'I' &&
                r.Data[2] == (byte)'F' && r.Data[3] == (byte)'F')
            {
                riff++;
            }
        }

        Assert.True(riff >= 40, $"Expected most of first 50 EBX to be RIFF, got riff={riff} errors={errors}");
        Assert.Equal(0, errors);

        var report = FifamodInspectReport.FromMod(mod);
        Assert.Equal(mod.Resources.Count, report.ResourceCount);
        Assert.True(report.RiffEbxCount >= 40);
        Assert.Contains("FETM", report.ToText());
    }

    [Fact]
    public void WriteFifaproject_ProducesFetpMagicAndCasPayloads()
    {
        string? path = FindFixture();
        Assert.True(path is not null, "Expected tests/fixtures/*.fifamod sample");

        Assert.True(Oodle.IsBound || Oodle.TryBindFromSearchPaths(new[]
        {
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "third_party", "oodle", "bin"),
        }));

        var mod = FifamodReader.Read(path!, loadResourceData: true, decompress: true);
        string outPath = Path.Combine(Path.GetTempPath(), $"frostyconvert-test-{Guid.NewGuid():N}.fifaproject");
        try
        {
            FifaprojectWriter.Write(outPath, mod);
            Assert.True(File.Exists(outPath));
            byte[] bytes = File.ReadAllBytes(outPath);
            Assert.True(bytes.Length > 64);
            // FETP magic LE
            Assert.Equal((byte)'F', bytes[0]);
            Assert.Equal((byte)'E', bytes[1]);
            Assert.Equal((byte)'T', bytes[2]);
            Assert.Equal((byte)'P', bytes[3]);
            Assert.Equal(FifaprojectWriter.ProjectVersion, bytes[4]);

            // First EBX CAS payload from mod must appear as stored (guard nibble 7)
            var first = mod.Resources.First(r => r.Kind == FifamodResourceKind.Ebx && r.CompressedData is { Length: > 8 });
            byte[] cas = first.CompressedData!;
            Assert.True(IndexOf(bytes, cas) >= 0, "CAS payload from fifamod should be embedded in project");
            uint word1 = (uint)((cas[4] << 24) | (cas[5] << 16) | (cas[6] << 8) | cas[7]);
            Assert.Equal(7u, (word1 >> 20) & 0xF);
        }
        finally
        {
            try { File.Delete(outPath); } catch { /* ignore */ }
        }
    }

    private static int IndexOf(byte[] hay, byte[] needle)
    {
        for (int i = 0; i <= hay.Length - needle.Length; i++)
        {
            bool ok = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (hay[i + j] != needle[j]) { ok = false; break; }
            }
            if (ok) return i;
        }
        return -1;
    }
}
