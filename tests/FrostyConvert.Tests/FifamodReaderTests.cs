using FrostyConvert.Core.Convert;
using FrostyConvert.Core.FifaMod;
using FrostyConvert.Core.Legacy;
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
    public void ProjectAddedRecovery_DetectsVar1PlusPaths()
    {
        Assert.True(FifamodProjectAddedRecovery.IsAddedHeadVariationPath(
            "content/character/player/player_1/1/var_1/head_1_0_1_color"));
        Assert.True(FifamodProjectAddedRecovery.IsAddedHeadVariationPath(
            "content/character/player/player_1/1/var_1_starhead_brt"));
        Assert.True(FifamodProjectAddedRecovery.IsAddedHeadVariationPath(
            "content/character/player/player_1/1/var_2/face_1_0_2_normal"));
        Assert.False(FifamodProjectAddedRecovery.IsAddedHeadVariationPath(
            "content/character/player/player_1/1/var_0/head_1_0_0_color"));
        Assert.False(FifamodProjectAddedRecovery.IsAddedHeadVariationPath(
            "content/character/player/player_1/1/var_0_starhead_brt"));
        Assert.False(FifamodProjectAddedRecovery.IsAddedHeadVariationPath(null));
    }

    [Fact]
    public void ProjectAddedRecovery_DetectsCreatedPlayerAndKitPaths()
    {
        // Created faces: pure-numeric folder under player_XXXXX (any var, including var_0)
        Assert.True(FifamodProjectAddedRecovery.IsCreatedPlayerAssetPath(
            "content/character/player/player_50000/50247/var_0/hair_50247_0_0_coeff"));
        Assert.True(FifamodProjectAddedRecovery.IsCreatedPlayerAssetPath(
            "content/character/player/player_50000/50247/var_0_starhead_brt"));
        Assert.True(FifamodProjectAddedRecovery.IsForceAddedAssetPath(
            "content/character/player/player_50000/50247/var_0/face_50247_0_0_color"));

        // Named EA-style player segment: not a "created" folder
        Assert.False(FifamodProjectAddedRecovery.IsCreatedPlayerAssetPath(
            "content/character/player/player_185000/luiz_gustavo_dias_185221/var_0/head_185221_0_0_mesh"));
        Assert.False(FifamodProjectAddedRecovery.IsForceAddedAssetPath(
            "content/character/player/player_185000/luiz_gustavo_dias_185221/var_0/head_185221_0_0_mesh"));

        // Created kits: pure-numeric team folder
        Assert.True(FifamodProjectAddedRecovery.IsCreatedKitAssetPath(
            "content/character/kit/kit_150000/150039/home_0_0/jersey_150039_0_0_color"));
        Assert.True(FifamodProjectAddedRecovery.IsForceAddedAssetPath(
            "content/character/kit/kit_150000/150039/home_0_0_launch_kit_brt"));
        Assert.False(FifamodProjectAddedRecovery.IsCreatedKitAssetPath(
            "content/character/kit/kit_131500/milano_fc_131681/home_0_0/hotspots_131681_0_0"));

        Assert.False(FifamodProjectAddedRecovery.IsCreatedPlayerAssetPath(null));
        Assert.False(FifamodProjectAddedRecovery.IsCreatedKitAssetPath(null));
        Assert.False(FifamodProjectAddedRecovery.IsForceAddedAssetPath(null));
    }

    [Fact]
    public void ProjectAddedRecovery_GuessesTypes_FromResAndPath()
    {
        Assert.Equal("TextureAsset",
            FifamodProjectAddedRecovery.GuessEbxTypeName(
                "content/x/var_1/face_1_0_1_color", TextureResBuilder.TextureResType));
        Assert.Equal("SkinnedMeshAsset",
            FifamodProjectAddedRecovery.GuessEbxTypeName(
                "content/x/var_1/hair_1_0_1_mesh", FifamodProjectAddedRecovery.MeshSetResType));
        // No RES: hair/head roots are ObjectBlueprint (mesh editor needs *_mesh RES)
        Assert.Equal("ObjectBlueprint",
            FifamodProjectAddedRecovery.GuessEbxTypeName("content/x/var_1/hair_1_0_1"));
        Assert.Equal("ObjectBlueprint",
            FifamodProjectAddedRecovery.GuessEbxTypeName("content/x/var_1/head_1_0_1"));
        Assert.Equal("MeshVariationDatabase",
            FifamodProjectAddedRecovery.GuessEbxTypeName(
                "content/x/var_1_starhead_brt/meshvariationdb_win32"));
    }

    [Fact]
    public void ProjectAddedRecovery_ExtractsEbxGuid_After12BytePad()
    {
        var expected = new Guid("e5a96b52-af39-4f29-9283-5df9f62a4bdc");
        byte[] ebx = BuildMinimalRiffEbx(expected);
        Guid? got = FifamodProjectAddedRecovery.TryExtractRiffEbxGuid(ebx);
        Assert.Equal(expected, got);
    }

    [Fact]
    public void FifaprojectWriter_ForcesIsAdded_ForHeadVariationVar1()
    {
        // Minimal RIFF EBX with EBXD + 16 zero pad + partition guid
        var partitionGuid = new Guid("11223344-5566-7788-99aa-bbccddeeff00");
        byte[] ebxData = BuildMinimalRiffEbx(partitionGuid);
        byte[] ebxPayload = ebxData; // store uncompressed as project payload is fine for synthetic

        var chunkId = new Guid("aabbccdd-eeff-0011-2233-445566778899");
        byte[] texRes = new byte[TextureResBuilder.FixedSize];
        chunkId.ToByteArray().CopyTo(texRes.AsSpan(TextureResBuilder.ChunkIdOffset));

        byte[] chunkBytes = new byte[] { 1, 2, 3, 4, 5 };

        var mod = new FifamodFile
        {
            Path = "face-var1.fifamod",
            GameName = "FC26",
            GameVersion = 2927799,
            Details = new FifamodDetails
            {
                Title = "Var1 Face",
                Author = "T",
                Version = "1.0",
                Description = "d",
            },
            Resources = new[]
            {
                new FifamodResource
                {
                    Name = chunkId.ToString(),
                    Kind = FifamodResourceKind.Chunk,
                    ChunkId = chunkId,
                    // No IsAdded in mod flags — recovery must force it
                    CompressedData = chunkBytes,
                    Data = chunkBytes,
                    UncompressedSize = chunkBytes.Length,
                    Sha1 = System.Security.Cryptography.SHA1.HashData(chunkBytes),
                },
                new FifamodResource
                {
                    Name = "content/character/player/player_1000/test_player_1001/var_1/face_1001_0_1_color",
                    Kind = FifamodResourceKind.Res,
                    ResType = TextureResBuilder.TextureResType,
                    ResRid = 99,
                    ResMeta = new byte[16],
                    CompressedData = texRes,
                    Data = texRes,
                    UncompressedSize = texRes.Length,
                    Sha1 = System.Security.Cryptography.SHA1.HashData(texRes),
                },
                new FifamodResource
                {
                    Name = "content/character/player/player_1000/test_player_1001/var_1/face_1001_0_1_color",
                    Kind = FifamodResourceKind.Ebx,
                    // ModWriter never sets IsAdded
                    CompressedData = ebxPayload,
                    Data = ebxData,
                    UncompressedSize = ebxData.Length,
                    Sha1 = System.Security.Cryptography.SHA1.HashData(ebxPayload),
                },
                // Named-player var_0 control: must stay non-added (TOC modification)
                new FifamodResource
                {
                    Name = "content/character/player/player_1000/test_player_1001/var_0/face_1001_0_0_color",
                    Kind = FifamodResourceKind.Ebx,
                    CompressedData = ebxPayload,
                    Data = ebxData,
                    UncompressedSize = ebxData.Length,
                    Sha1 = System.Security.Cryptography.SHA1.HashData(ebxPayload),
                },
            },
        };

        using var ms = new MemoryStream();
        FifaprojectWriter.Write(ms, mod);
        ms.Position = 0;
        var summary = FifaprojectReader.ReadSummary(ms);

        Assert.Equal(2, summary.EbxCount);
        Assert.Equal(1, summary.AddedEbxCount); // only var_1
        Assert.Equal(1, summary.ResCount);
        Assert.Equal(1, summary.AddedResCount);
        Assert.Equal(1, summary.ChunkCount);
        Assert.Equal(1, summary.AddedChunkCount);
        Assert.Empty(summary.Warnings);
    }

    [Fact]
    public void FifaprojectWriter_ForcesIsAdded_ForCreatedPlayerAndKit()
    {
        var partitionGuid = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        byte[] ebxData = BuildMinimalRiffEbx(partitionGuid);
        byte[] ebxPayload = ebxData;

        var chunkId = new Guid("11111111-2222-3333-4444-555555555555");
        byte[] texRes = new byte[TextureResBuilder.FixedSize];
        chunkId.ToByteArray().CopyTo(texRes.AsSpan(TextureResBuilder.ChunkIdOffset));
        byte[] chunkBytes = new byte[] { 9, 8, 7, 6 };

        var mod = new FifamodFile
        {
            Path = "created-face-kit.fifamod",
            GameName = "FC26",
            GameVersion = 2927799,
            Details = new FifamodDetails
            {
                Title = "Created Face+Kit",
                Author = "T",
                Version = "1.0",
                Description = "d",
            },
            Resources = new[]
            {
                new FifamodResource
                {
                    Name = chunkId.ToString(),
                    Kind = FifamodResourceKind.Chunk,
                    ChunkId = chunkId,
                    CompressedData = chunkBytes,
                    Data = chunkBytes,
                    UncompressedSize = chunkBytes.Length,
                    Sha1 = System.Security.Cryptography.SHA1.HashData(chunkBytes),
                },
                new FifamodResource
                {
                    Name = "content/character/player/player_50000/50247/var_0/face_50247_0_0_color",
                    Kind = FifamodResourceKind.Res,
                    ResType = TextureResBuilder.TextureResType,
                    ResRid = 50,
                    ResMeta = new byte[16],
                    CompressedData = texRes,
                    Data = texRes,
                    UncompressedSize = texRes.Length,
                    Sha1 = System.Security.Cryptography.SHA1.HashData(texRes),
                },
                new FifamodResource
                {
                    Name = "content/character/player/player_50000/50247/var_0/face_50247_0_0_color",
                    Kind = FifamodResourceKind.Ebx,
                    CompressedData = ebxPayload,
                    Data = ebxData,
                    UncompressedSize = ebxData.Length,
                    Sha1 = System.Security.Cryptography.SHA1.HashData(ebxPayload),
                },
                new FifamodResource
                {
                    Name = "content/character/kit/kit_150000/150039/home_0_0/jersey_150039_0_0_color",
                    Kind = FifamodResourceKind.Ebx,
                    CompressedData = ebxPayload,
                    Data = ebxData,
                    UncompressedSize = ebxData.Length,
                    Sha1 = System.Security.Cryptography.SHA1.HashData(ebxPayload),
                },
                // Named player var_0 must remain a plain modification
                new FifamodResource
                {
                    Name = "content/character/player/player_185000/luiz_gustavo_dias_185221/var_0/hair_185221_0_0_color",
                    Kind = FifamodResourceKind.Ebx,
                    CompressedData = ebxPayload,
                    Data = ebxData,
                    UncompressedSize = ebxData.Length,
                    Sha1 = System.Security.Cryptography.SHA1.HashData(ebxPayload),
                },
            },
        };

        using var ms = new MemoryStream();
        FifaprojectWriter.Write(ms, mod);
        ms.Position = 0;
        var summary = FifaprojectReader.ReadSummary(ms);

        Assert.Equal(3, summary.EbxCount);
        Assert.Equal(2, summary.AddedEbxCount); // created face + created kit
        Assert.Equal(1, summary.ResCount);
        Assert.Equal(1, summary.AddedResCount);
        Assert.Equal(1, summary.ChunkCount);
        Assert.Equal(1, summary.AddedChunkCount);
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

    /// <summary>Minimal RIFF/EBX with EBXD chunk: 12 zero pad + partition GUID (FC26 layout).</summary>
    private static byte[] BuildMinimalRiffEbx(Guid partitionGuid)
    {
        var ebxD = new byte[28];
        // 12 zero pad then GUID
        partitionGuid.ToByteArray().CopyTo(ebxD.AsSpan(12));
        int riffPayload = 4 + 8 + ebxD.Length;
        var buf = new byte[8 + riffPayload];
        buf[0] = (byte)'R';
        buf[1] = (byte)'I';
        buf[2] = (byte)'F';
        buf[3] = (byte)'F';
        BitConverter.TryWriteBytes(buf.AsSpan(4), riffPayload);
        buf[8] = (byte)'E';
        buf[9] = (byte)'B';
        buf[10] = (byte)'X';
        buf[11] = 0;
        buf[12] = (byte)'E';
        buf[13] = (byte)'B';
        buf[14] = (byte)'X';
        buf[15] = (byte)'D';
        BitConverter.TryWriteBytes(buf.AsSpan(16), ebxD.Length);
        Buffer.BlockCopy(ebxD, 0, buf, 20, ebxD.Length);
        return buf;
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
