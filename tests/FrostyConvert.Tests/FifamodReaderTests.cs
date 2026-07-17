using FrostyConvert.Core.Convert;
using FrostyConvert.Core.FifaMod;
using FrostyConvert.Core.Project;

namespace FrostyConvert.Tests;

public class FifamodReaderTests
{
    [Fact]
    public void IsFifamod_False_ForEmptyStream()
    {
        using var ms = new MemoryStream(new byte[] { 0, 1, 2, 3 });
        Assert.False(FifamodReader.IsFifamod(ms));
    }

    [Fact]
    public void IsFifamod_True_ForFetmMagic()
    {
        using var ms = new MemoryStream("FETM"u8.ToArray());
        Assert.True(FifamodReader.IsFifamod(ms));
    }

    [Fact]
    public void Read_Throws_WhenNotFetm()
    {
        using var ms = new MemoryStream(new byte[] { 0x58, 0x58, 0x58, 0x58, 0, 0, 0, 0, 0, 0, 0, 0 });
        var ex = Assert.Throws<FifamodReaderException>(() =>
            FifamodReader.Read(ms, "bad.fifamod", loadResourceData: false, decompress: false));
        Assert.Contains("FETM", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FifaprojectWriter_MagicConstant()
    {
        Assert.Equal(0x50544546u, FifaprojectWriter.MagicLe);
        Assert.Equal(2, FifaprojectWriter.ProjectVersion);
    }

    [Fact]
    public void ConversionReadiness_EmptyProject_IsBlocking()
    {
        var mod = new FifamodFile
        {
            Path = "empty.fifamod",
            GameName = "FC26",
            Resources = Array.Empty<FifamodResource>(),
        };
        var r = ConversionReadiness.ForFifamod(mod, 0, 0, 0, 0);
        Assert.False(r.Success);
        Assert.NotEmpty(r.Blocking);
        Assert.Contains(r.NextSteps, s => s.Contains("Save", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Read_ParsesPerEbxBrtAndTrailingCollectors()
    {
        using var ms = BuildMinimalFetmWithBrtAndCollectors();
        var mod = FifamodReader.Read(ms, "brt.fifamod", loadResourceData: false, decompress: false);

        Assert.Equal(1, mod.EbxCount);
        var ebx = Assert.Single(mod.Resources);
        Assert.Equal(FifamodResourceKind.Ebx, ebx.Kind);
        Assert.NotNull(ebx.BrtAddition);
        Assert.Equal(0x89ABCDEFu, ebx.BrtAddition!.BrtNameHash);
        Assert.Equal("win32/test/bundle_ref", ebx.BrtAddition.BundleRefPath);
        Assert.Equal("win32/parent/ref", ebx.BrtAddition.ParentBundleRefPath);
        Assert.True(ebx.BrtAddition.BundleRefOnly);

        Assert.Single(mod.Collectors);
        Assert.Equal("legacy/collectors/test", mod.Collectors[0].CollectorEbxName);
        Assert.Equal(new Guid("00112233-4455-6677-8899-aabbccddeeff"), mod.Collectors[0].CollectorChunkId);
        Assert.True(mod.Collectors[0].IsPatch);
        Assert.Equal(0x11223344u, mod.Collectors[0].Meta);

        Assert.Single(mod.BundleRefTables);
        Assert.Equal(0x89ABCDEFu, mod.BundleRefTables[0].NameHash);
        Assert.Equal("win32/brt_table", mod.BundleRefTables[0].Name);
    }

    [Fact]
    public void ReadAndWrite_PreservesHeaderLocaleInitFsLuaAndBundles()
    {
        using var ms = BuildFetmWithHeaderExtras();
        var mod = FifamodReader.Read(ms, "hdr.fifamod", loadResourceData: false, decompress: false);

        Assert.Single(mod.Details.Screenshots);
        Assert.Equal(new byte[] { 0x89, 0x50 }, mod.Details.Screenshots[0]);
        Assert.Single(mod.LocaleIniFiles);
        Assert.Equal("en", mod.LocaleIniFiles[0].Description);
        Assert.Contains("Hello", mod.LocaleIniFiles[0].Contents);
        Assert.Single(mod.InitFsFiles);
        Assert.Equal("foo/bar", mod.InitFsFiles[0].Name);
        Assert.Equal(new byte[] { 1, 2, 3 }, mod.InitFsFiles[0].Data);
        Assert.Single(mod.PlayerLuaMods);
        Assert.Equal("Faces", mod.PlayerLuaMods[0].Key);
        Assert.Equal(new[] { "a", "b" }, mod.PlayerLuaMods[0].Values);
        Assert.Empty(mod.PlayerKitLuaMods);
        Assert.Single(mod.AddedBundles);
        Assert.Equal("win32/custombundle", mod.AddedBundles[0].Name);
        Assert.Equal(0xDEADBEEFu, mod.AddedBundles[0].SuperBundleHash);

        // Round-trip into project must parse cleanly
        using var proj = new MemoryStream();
        // Need at least one resource with payload for a non-empty project body
        var withPayload = new FifamodFile
        {
            Path = mod.Path,
            ModVersion = mod.ModVersion,
            GameName = mod.GameName,
            GameVersion = mod.GameVersion,
            Details = mod.Details,
            LocaleIniFiles = mod.LocaleIniFiles,
            InitFsFiles = mod.InitFsFiles,
            PlayerLuaMods = mod.PlayerLuaMods,
            PlayerKitLuaMods = mod.PlayerKitLuaMods,
            AddedBundles = mod.AddedBundles,
            Resources = new[]
            {
                new FifamodResource
                {
                    Name = "content/x",
                    Kind = FifamodResourceKind.Ebx,
                    CompressedData = new byte[] { 9, 9, 9 },
                    UncompressedSize = 3,
                    Sha1 = new byte[20],
                },
            },
        };
        FifaprojectWriter.Write(proj, withPayload);
        proj.Position = 0;
        var summary = FifaprojectReader.ReadSummary(proj);
        Assert.Equal(1, summary.ScreenshotCount);
        Assert.Equal(1, summary.LocaleIniCount);
        Assert.Equal(1, summary.InitFsCount);
        Assert.Equal(1, summary.PlayerLuaKeyCount);
        Assert.Equal(0, summary.PlayerKitLuaKeyCount);
        Assert.Equal(1, summary.AddedBundleCount);
        Assert.Equal(1, summary.EbxCount);
        Assert.Empty(summary.Warnings);
    }

    [Fact]
    public void FifaprojectWriter_WritesBrtFields_ReadableBySummary()
    {
        byte[] payload = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        var mod = new FifamodFile
        {
            Path = "synthetic.fifamod",
            GameName = "FC26",
            GameVersion = 12345,
            Details = new FifamodDetails
            {
                Title = "BRT Test",
                Author = "A",
                Version = "1.0",
                Description = "d",
            },
            Resources = new[]
            {
                new FifamodResource
                {
                    Name = "content/character/shoe/test",
                    Kind = FifamodResourceKind.Ebx,
                    UncompressedSize = payload.Length,
                    CompressedData = payload,
                    Sha1 = System.Security.Cryptography.SHA1.HashData(payload),
                    BrtAddition = new FifamodBrtAddition
                    {
                        BrtNameHash = 0x7F3A2B1C,
                        BundleRefPath = "win32/characters/shoes/test",
                        ParentBundleRefPath = "win32/characters/shoes",
                        BundleRefOnly = false,
                    },
                },
            },
        };

        using var ms = new MemoryStream();
        FifaprojectWriter.Write(ms, mod);
        ms.Position = 0;
        var summary = FifaprojectReader.ReadSummary(ms);

        Assert.Equal(1, summary.EbxCount);
        Assert.Equal(1, summary.EbxWithBrtCount);
        Assert.Contains(summary.SampleBrtPaths, p => p.Contains("win32/characters/shoes/test", StringComparison.Ordinal));
        Assert.Empty(summary.Warnings);
    }

    private static MemoryStream BuildMinimalFetmWithBrtAndCollectors()
    {
        var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);
        WriteFetmPreamble(w, "FC26", addedBundles: false, headerExtras: false);

        U24(w, 1); // ebx count
        Lp(w, "content/test/asset");
        // AddToBundleRefTable|HasParentBundleRef|BundleRefOnly|IsDirectlyModified = 4|0x40|0x80|2
        w.Write((byte)(0x02 | 0x04 | 0x40 | 0x80));
        w.Write(new byte[20]); // sha1
        Write7(w, 0); // rel offset
        Write7(w, 0); // length
        Write7(w, 0); // originalSize
        w.Write(0x89ABCDEFu); // brt hash
        Lp(w, "win32/test/bundle_ref");
        Lp(w, "win32/parent/ref");

        U24(w, 0); // res
        U24(w, 0); // chunks

        Write7(w, 1); // collectors
        Lp(w, "legacy/collectors/test");
        w.Write(new Guid("00112233-4455-6677-8899-aabbccddeeff").ToByteArray());
        w.Write((byte)1); // patch
        w.Write(0x11223344u);

        Write7(w, 1); // brt tables
        w.Write(0x89ABCDEFu);
        Lp(w, "win32/brt_table");

        ms.Position = 0;
        return ms;
    }

    private static MemoryStream BuildFetmWithHeaderExtras()
    {
        var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true);
        WriteFetmPreamble(w, "FC26", addedBundles: true, headerExtras: true);
        U24(w, 0); // ebx
        U24(w, 0); // res
        U24(w, 0); // chunks
        Write7(w, 0); // collectors
        Write7(w, 0); // brt tables
        ms.Position = 0;
        return ms;
    }

    private static void WriteFetmPreamble(BinaryWriter w, string game, bool addedBundles, bool headerExtras)
    {
        w.Write(0x4D544546u); // FETM
        w.Write((byte)1);
        Lp(w, game);
        U24(w, 1);
        Lp(w, "T"); Lp(w, "A");
        w.Write((byte)0); w.Write((byte)0);
        Lp(w, ""); Lp(w, "");
        Lp(w, "1"); Lp(w, "d");
        for (int i = 0; i < 8; i++)
            Lp(w, "");
        Write7(w, 0); // icon

        if (headerExtras)
        {
            Write7(w, 1); // 1 screenshot
            Write7(w, 2);
            w.Write(new byte[] { 0x89, 0x50 });

            Write7(w, 1); // locale
            Lp(w, "en");
            Lp(w, "Hello=World");

            Write7(w, 1); // initfs
            Lp(w, "foo/bar");
            Write7(w, 3);
            w.Write(new byte[] { 1, 2, 3 });

            Write7(w, 1); // player lua keys
            Lp(w, "Faces");
            Write7(w, 2);
            Lp(w, "a");
            Lp(w, "b");

            Write7(w, 0); // kit lua
        }
        else
        {
            Write7(w, 0); // screenshots
            Write7(w, 0); // locale
            Write7(w, 0); // initfs
            Write7(w, 0); // player lua
            Write7(w, 0); // kit lua
        }

        w.Write(0u); // dataBaseOffset
        if (addedBundles)
        {
            U24(w, 1);
            Lp(w, "win32/custombundle");
            w.Write(0xABCDEF0123456789UL);
            w.Write(0xDEADBEEFu);
        }
        else
        {
            U24(w, 0);
        }
    }

    private static void Lp(BinaryWriter w, string s)
    {
        byte[] b = System.Text.Encoding.UTF8.GetBytes(s);
        Write7(w, b.Length);
        w.Write(b);
    }

    private static void Write7(BinaryWriter bw, int value)
    {
        uint v = (uint)value;
        while (v >= 0x80)
        {
            bw.Write((byte)(v | 0x80));
            v >>= 7;
        }
        bw.Write((byte)v);
    }

    private static void U24(BinaryWriter w, uint v)
    {
        w.Write((byte)(v & 0xFF));
        w.Write((byte)((v >> 8) & 0xFF));
        w.Write((byte)((v >> 16) & 0xFF));
    }
}
