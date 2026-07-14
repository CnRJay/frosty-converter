using FrostyConvert.Core.Convert;
using FrostyConvert.Core.IO;
using FrostyConvert.Core.Mod;
using FrostyConvert.Core.Project;

namespace FrostyConvert.Tests;

public class ModToProjectConverterTests
{
    [Fact]
    public void Convert_SyntheticMod_WritesReadableProjectHeader()
    {
        byte[] modBytes = FbmodWriterHelper.BuildMinimalV5(
            profileName: "FIFA20",
            gameVersion: 12345,
            details: new FbmodDetails
            {
                Title = "Recover Me",
                Author = "OriginalAuthor",
                Category = "Gameplay",
                Version = "2.1",
                Description = "Abandoned mod",
                Link = "https://example.com/mod",
            },
            resources: new[]
            {
                new SyntheticResource
                {
                    Type = ModResourceType.Embedded,
                    Name = "Icon",
                    Data = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
                },
                new SyntheticResource
                {
                    Type = ModResourceType.Ebx,
                    Name = "systems/test/asset",
                    Data = BuildUncompressedCasPayload("EBXDATA_TEST"u8.ToArray()),
                    Flags = 0x08,
                    AddedBundleHashes = new List<int> { unchecked((int)0xA1B2C3D4) },
                },
                new SyntheticResource
                {
                    Type = ModResourceType.Res,
                    Name = "textures/foo",
                    Data = new byte[] { 1, 2, 3, 4 },
                    ResType = 0x6BDE1ABD,
                    ResRid = 99,
                    ResMeta = new byte[16],
                    Sha1 = Enumerable.Repeat((byte)0xAB, 20).ToArray(),
                },
                new SyntheticResource
                {
                    Type = ModResourceType.Chunk,
                    Name = "11223344-5566-7788-99aa-bbccddeeff00",
                    Data = new byte[] { 9, 9, 9 },
                    H32 = 42,
                    FirstMip = 0,
                    LogicalSize = 3,
                },
                new SyntheticResource
                {
                    Type = ModResourceType.Bundle,
                    Name = "win32/mybundle",
                    SuperBundleHash = 0x55,
                },
            });

        using var ms = new MemoryStream(modBytes);
        var mod = FbmodReader.Read(ms, "synthetic.fbmod");
        var (project, report) = ModToProjectConverter.Convert(mod);

        Assert.True(report.Success, string.Join("; ", report.Errors));
        Assert.Equal("FIFA20", project.ProfileName);
        Assert.Equal("Recover Me", project.Title);
        Assert.Equal("OriginalAuthor", project.Author);
        Assert.Contains("Recovered from compiled", project.Description);
        Assert.Single(project.ModifiedEbx);
        Assert.Equal("systems/test/asset", project.ModifiedEbx[0].Name);
        Assert.Equal(new[] { "0xa1b2c3d4" }, project.ModifiedEbx[0].AddedBundleNames);
        Assert.False(project.ModifiedEbx[0].IsCustomHandler);
        Assert.Equal("EBXDATA_TEST"u8.ToArray(), project.ModifiedEbx[0].Data);
        Assert.Single(project.AddedEbx);
        Assert.Single(project.ModifiedRes);
        Assert.Single(project.ModifiedChunks);
        Assert.Single(project.AddedBundles);
        Assert.NotNull(project.Icon);

        string path = Path.Combine(Path.GetTempPath(), $"proj-{Guid.NewGuid():N}.fbproject");
        try
        {
            FbprojectWriter.Write(path, project);
            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 64);

            using var fs = File.OpenRead(path);
            using var r = new EndianBinaryReader(fs);
            Assert.Equal(FbprojectConstants.Magic, r.ReadUInt64());
            Assert.Equal(FbprojectConstants.FormatVersion, r.ReadUInt32());
            Assert.Equal("FIFA20", r.ReadNullTerminatedString());
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Convert_HandlerEbx_MarksCustomHandler()
    {
        byte[] modBytes = FbmodWriterHelper.BuildMinimalV5(
            "DAI",
            1,
            new FbmodDetails
            {
                Title = "H",
                Author = "A",
                Category = "C",
                Version = "1",
                Description = "D",
                Link = "",
            },
            new[]
            {
                new SyntheticResource
                {
                    Type = ModResourceType.Ebx,
                    Name = "loc/strings",
                    Data = new byte[] { 0x01, 0x02, 0x03 },
                    HandlerHash = 0x12345678,
                    UserData = "merge",
                },
            });

        using var ms = new MemoryStream(modBytes);
        var mod = FbmodReader.Read(ms, "h.fbmod");
        var (project, report) = ModToProjectConverter.Convert(mod);

        Assert.True(report.Success);
        Assert.True(project.ModifiedEbx[0].IsCustomHandler);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, project.ModifiedEbx[0].Data);
        Assert.Equal("merge", project.ModifiedEbx[0].UserData);
    }

    [Fact]
    public void ConvertToFile_LegacyMagic_FailsGracefully()
    {
        string modPath = Path.Combine(Path.GetTempPath(), $"leg-{Guid.NewGuid():N}.fbmod");
        string outPath = Path.Combine(Path.GetTempPath(), $"out-{Guid.NewGuid():N}.fbproject");
        try
        {
            File.WriteAllBytes(modPath, "not-binary"u8.ToArray());
            var report = ModToProjectConverter.ConvertToFile(modPath, outPath);
            Assert.False(report.Success);
            Assert.NotEmpty(report.Errors);
            Assert.False(File.Exists(outPath));
        }
        finally
        {
            if (File.Exists(modPath)) File.Delete(modPath);
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }

    [Fact]
    public void Convert_OodleWithoutDll_DoesNotMarkCustomHandler()
    {
        // Minimal CAS block with Oodle type 0x15 (LE-read of BE 0x1570).
        byte[] fakeOodlePayload = BuildOodleCasStub(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        byte[] modBytes = FbmodWriterHelper.BuildMinimalV5(
            "CollegeFB27",
            1,
            new FbmodDetails
            {
                Title = "OodleMod",
                Author = "A",
                Category = "C",
                Version = "1",
                Description = "D",
                Link = "",
            },
            new[]
            {
                new SyntheticResource
                {
                    Type = ModResourceType.Ebx,
                    Name = "football/test",
                    Data = fakeOodlePayload,
                },
            });

        using var ms = new MemoryStream(modBytes);
        var mod = FbmodReader.Read(ms, "oodle.fbmod");
        var (project, report) = ModToProjectConverter.Convert(mod);

        // With built-in OozSharp, a fake/stub Oodle stream should still fail decompression,
        // and must NOT invent a custom-handler project entry.
        Assert.Empty(project.ModifiedEbx);
        Assert.NotEmpty(report.Errors);
        Assert.DoesNotContain(report.Errors, e => e.Contains("Parameter name"));
    }

    [Fact]
    public void Convert_RealCnrSample_IfPresent_DecompressesWithOoz()
    {
        string modPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "cnr-gameplaymod.fbmod"));
        // Also try workspace-relative path when running from repo
        if (!File.Exists(modPath))
            modPath = Path.GetFullPath("tests/fixtures/cnr-gameplaymod.fbmod");
        if (!File.Exists(modPath))
            return; // fixture not present in this environment

        var mod = FbmodReader.Read(modPath, loadResourceData: true);
        var (project, report) = ModToProjectConverter.Convert(mod);

        Assert.True(project.ModifiedEbx.Count > 0,
            "Expected OozSharp to decompress sample ebx. Errors: " + string.Join("; ", report.Errors));
        Assert.All(project.ModifiedEbx, e =>
        {
            Assert.False(e.IsCustomHandler);
            Assert.NotNull(e.Data);
            Assert.True(e.Data!.Length > 0);
        });
    }

    /// <summary>CAS header claiming Oodle compression (type 0x15).</summary>
    private static byte[] BuildOodleCasStub(byte[] fakeCompressed)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int decomp = 64;
        w.Write((byte)((decomp >> 24) & 0xFF));
        w.Write((byte)((decomp >> 16) & 0xFF));
        w.Write((byte)((decomp >> 8) & 0xFF));
        w.Write((byte)(decomp & 0xFF));
        // BE 0x1570 → LE read 0x7015 → type & 0x7F = 0x15 (Oodle v4)
        w.Write((byte)0x15);
        w.Write((byte)0x70);
        int csize = fakeCompressed.Length;
        w.Write((byte)((csize >> 8) & 0xFF));
        w.Write((byte)(csize & 0xFF));
        w.Write(fakeCompressed);
        return ms.ToArray();
    }

    /// <summary>Single uncompressed CAS block (type 0x00).</summary>
    private static byte[] BuildUncompressedCasPayload(byte[] raw)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        // decompressed size BE
        int size = raw.Length;
        w.Write((byte)((size >> 24) & 0xFF));
        w.Write((byte)((size >> 16) & 0xFF));
        w.Write((byte)((size >> 8) & 0xFF));
        w.Write((byte)(size & 0xFF));
        // compress code written BE as 0x0070, read LE → 0x7000, type & 0x7F = 0
        w.Write((byte)0x00);
        w.Write((byte)0x70);
        // compressed size BE
        w.Write((byte)((size >> 8) & 0xFF));
        w.Write((byte)(size & 0xFF));
        w.Write(raw);
        return ms.ToArray();
    }
}
