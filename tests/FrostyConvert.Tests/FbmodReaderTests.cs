using FrostyConvert.Core.Inspect;
using FrostyConvert.Core.Mod;

namespace FrostyConvert.Tests;

public class FbmodReaderTests
{
    [Fact]
    public void Read_MinimalV5_ParsesDetailsAndResources()
    {
        byte[] payload = "hello-ebx-data"u8.ToArray();
        byte[] bytes = FbmodWriterHelper.BuildMinimalV5(
            profileName: "FIFA20",
            gameVersion: 20200301,
            details: new FbmodDetails
            {
                Title = "Test Mod",
                Author = "Author",
                Category = "Gameplay",
                Version = "1.0",
                Description = "A test",
                Link = "https://example.com",
            },
            resources: new[]
            {
                new SyntheticResource
                {
                    Type = ModResourceType.Embedded,
                    Name = "Icon",
                    Data = new byte[] { 1, 2, 3 },
                },
                new SyntheticResource
                {
                    Type = ModResourceType.Ebx,
                    Name = "systems/testasset",
                    Data = payload,
                    Flags = 0x08, // added
                    AddedBundleHashes = new List<int> { unchecked((int)0xABCDEF01) },
                },
                new SyntheticResource
                {
                    Type = ModResourceType.Chunk,
                    Name = "00112233-4455-6677-8899-aabbccddeeff",
                    Data = new byte[] { 9, 8, 7 },
                    RangeStart = 1,
                    RangeEnd = 2,
                    LogicalOffset = 3,
                    LogicalSize = 4,
                    H32 = 5,
                    FirstMip = 0,
                },
                new SyntheticResource
                {
                    Type = ModResourceType.Res,
                    Name = "textures/test",
                    Data = new byte[] { 4, 5 },
                    ResType = 0x6BDE1ABD,
                    ResRid = 0x1234,
                    ResMeta = new byte[16],
                },
                new SyntheticResource
                {
                    Type = ModResourceType.Bundle,
                    Name = "win32/testbundle",
                    SuperBundleHash = 0x11223344,
                },
            });

        using var ms = new MemoryStream(bytes);
        var mod = FbmodReader.Read(ms, "synthetic.fbmod", loadResourceData: true);

        Assert.Equal(FbmodFormatKind.Binary, mod.Format);
        Assert.Equal(5u, mod.Version);
        Assert.Equal("FIFA20", mod.ProfileName);
        Assert.Equal(20200301, mod.GameVersion);
        Assert.NotNull(mod.Details);
        Assert.Equal("Test Mod", mod.Details!.Title);
        Assert.Equal("Author", mod.Details.Author);
        Assert.Equal("https://example.com", mod.Details.Link);
        Assert.Equal(5, mod.Resources.Count);

        var ebx = mod.Resources.Single(r => r.Type == ModResourceType.Ebx);
        Assert.Equal("systems/testasset", ebx.Name);
        Assert.True(ebx.IsAdded);
        Assert.Equal(payload, ebx.Data);
        Assert.Single(ebx.AddedBundleHashes);

        var chunk = mod.Resources.Single(r => r.Type == ModResourceType.Chunk);
        Assert.Equal(1u, chunk.RangeStart);
        Assert.Equal(0, chunk.FirstMip);
        Assert.Equal(new byte[] { 9, 8, 7 }, chunk.Data);

        var res = mod.Resources.Single(r => r.Type == ModResourceType.Res);
        Assert.Equal(0x6BDE1ABDu, res.ResType);
        Assert.Equal(0x1234ul, res.ResRid);

        var bundle = mod.Resources.Single(r => r.Type == ModResourceType.Bundle);
        Assert.Equal("win32/testbundle", bundle.Name);
        Assert.Equal(0x11223344, bundle.SuperBundleHash);
    }

    [Fact]
    public void Read_HandlerResource_PreservesHandlerHash()
    {
        byte[] bytes = FbmodWriterHelper.BuildMinimalV5(
            "Madden20",
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
                    Name = "localization/test",
                    Data = new byte[] { 0xCA, 0xFE },
                    HandlerHash = FbmodConstants.LegacyHandlerHash,
                    UserData = "legacy;Collector (foo)",
                },
            });

        using var ms = new MemoryStream(bytes);
        var mod = FbmodReader.Read(ms, "h.fbmod");
        var r = Assert.Single(mod.Resources);
        Assert.True(r.HasHandler);
        Assert.Equal(FbmodConstants.LegacyHandlerHash, r.HandlerHash);
        Assert.Equal("legacy;Collector (foo)", r.UserData);
    }

    [Fact]
    public void Read_NonMagic_ReturnsLegacyKind()
    {
        byte[] junk = "not a frosty mod"u8.ToArray();
        using var ms = new MemoryStream(junk);
        var mod = FbmodReader.Read(ms, "old.fbmod", loadResourceData: false);
        Assert.Equal(FbmodFormatKind.Legacy, mod.Format);
        Assert.Empty(mod.Resources);
    }

    [Fact]
    public void InspectReport_FromMod_SummarizesCounts()
    {
        byte[] bytes = FbmodWriterHelper.BuildMinimalV5(
            "DAI",
            42,
            new FbmodDetails
            {
                Title = "T",
                Author = "A",
                Category = "C",
                Version = "1",
                Description = "D",
                Link = "",
            },
            new[]
            {
                new SyntheticResource { Type = ModResourceType.Embedded, Name = "Icon", Data = new byte[] { 1 } },
                new SyntheticResource { Type = ModResourceType.Ebx, Name = "a", Data = new byte[] { 2 } },
                new SyntheticResource { Type = ModResourceType.Ebx, Name = "b", Data = new byte[] { 3 } },
            });

        using var ms = new MemoryStream(bytes);
        var mod = FbmodReader.Read(ms, "x.fbmod");
        var report = InspectReport.FromMod(mod);

        Assert.Equal(3, report.ResourceCount);
        Assert.Equal(2, report.ResourceCountsByType["Ebx"]);
        Assert.Equal(1, report.ResourceCountsByType["Embedded"]);
        Assert.Contains("T", report.ToText());
        Assert.Contains("\"ProfileName\"", report.ToJson());
    }

    [Fact]
    public void Read_CollegeFbV7Chunk_ParsesH64AndSuperBundles()
    {
        // Hand-build a minimal v7 CollegeFB27 mod with one chunk matching MMC ChunkResource.Read.
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(FbmodConstants.BinaryMagic);
            w.Write(7u);
            long dataOffsetPos = ms.Position;
            w.Write(0L);
            w.Write(0);
            w.Write("CollegeFB27"); // BinaryWriter length-prefixed string
            w.Write(4298863);

            void Nt(string s)
            {
                w.Write(System.Text.Encoding.UTF8.GetBytes(s));
                w.Write((byte)0);
            }
            Nt("T"); Nt("A"); Nt("C"); Nt("1"); Nt("D"); Nt(""); // details + link

            w.Write(1); // resource count
            w.Write((byte)ModResourceType.Chunk);
            w.Write(0); // resourceIndex
            Nt("00112233-4455-6677-8899-aabbccddeeff");
            w.Write(new byte[20]); // sha1
            w.Write(3L); // size
            w.Write((byte)0); // flags
            w.Write(0); // handlerHash
            Nt(""); // userData
            w.Write(0); // added bundles
            w.Write(1u); // rangeStart
            w.Write(2u); // rangeEnd
            w.Write(3u); // logicalOffset
            w.Write(4u); // logicalSize
            w.Write(0x11111111); // h32
            w.Write(0x2222222233333333L); // h64 (v7 CFB, no handler)
            w.Write(5); // firstMip
            w.Write(2); // superBundles count
            w.Write(unchecked((int)0xAABBCCDD));
            w.Write(unchecked((int)0x11223344));

            long dataOffset = ms.Position;
            w.Write(0L); // payload offset
            w.Write(3L); // payload size
            w.Write(new byte[] { 9, 8, 7 });

            long end = ms.Position;
            ms.Position = dataOffsetPos;
            w.Write(dataOffset);
            w.Write(1);
            ms.Position = end;
        }

        ms.Position = 0;
        var mod = FbmodReader.Read(ms, "v7.fbmod", loadResourceData: true);
        Assert.Equal(7u, mod.Version);
        Assert.Equal("CollegeFB27", mod.ProfileName);
        var chunk = Assert.Single(mod.Resources);
        Assert.Equal(ModResourceType.Chunk, chunk.Type);
        Assert.Equal(1u, chunk.RangeStart);
        Assert.Equal(0x11111111, chunk.H32);
        Assert.Equal(0x2222222233333333L, chunk.H64);
        Assert.Equal(5, chunk.FirstMip);
        Assert.Equal(2, chunk.SuperBundlesToAdd.Count);
        Assert.Equal(unchecked((int)0xAABBCCDD), chunk.SuperBundlesToAdd[0]);
        Assert.Equal(new byte[] { 9, 8, 7 }, chunk.Data);
    }

    [Fact]
    public void IsBinaryFbmod_OnSynthetic_ReturnsTrue()
    {
        byte[] bytes = FbmodWriterHelper.BuildMinimalV5(
            "P",
            1,
            new FbmodDetails
            {
                Title = "T",
                Author = "A",
                Category = "C",
                Version = "1",
                Description = "D",
                Link = "",
            },
            Array.Empty<SyntheticResource>());

        string path = Path.Combine(Path.GetTempPath(), $"fbmod-test-{Guid.NewGuid():N}.fbmod");
        try
        {
            File.WriteAllBytes(path, bytes);
            Assert.True(FbmodReader.IsBinaryFbmod(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Cryptor_RoundTrip_RestoresPlaintext()
    {
        byte[] plain = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
        byte[] enc = FbmodCryptor.Encrypt(plain);
        Assert.True(FbmodCryptor.IsEncrypted(enc));
        Assert.StartsWith("FMENC001", System.Text.Encoding.ASCII.GetString(enc, 0, 8));
        Assert.Equal(plain, FbmodCryptor.Decrypt(enc));
        Assert.Equal(plain, FbmodCryptor.MaybeDecrypt(enc));
        Assert.Equal(plain, FbmodCryptor.MaybeDecrypt(plain));
    }

    [Fact]
    public void Cryptor_TamperedMac_Throws()
    {
        byte[] enc = FbmodCryptor.Encrypt(new byte[] { 1, 2, 3, 4 });
        enc[30] ^= 0xFF; // flip a MAC byte
        Assert.Throws<FbmodReaderException>(() => FbmodCryptor.Decrypt(enc));
    }

    [Fact]
    public void Read_V8EncryptedPayload_DecryptsOnLoad()
    {
        byte[] plain = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02 };
        byte[] encrypted = FbmodCryptor.Encrypt(plain);

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(FbmodConstants.BinaryMagic);
            w.Write(8u);
            long dataOffsetPos = ms.Position;
            w.Write(0L);
            w.Write(0);
            w.Write("CollegeFB27");
            w.Write(4298863);

            void Nt(string s)
            {
                w.Write(System.Text.Encoding.UTF8.GetBytes(s));
                w.Write((byte)0);
            }
            Nt("Enc"); Nt("A"); Nt("C"); Nt("1"); Nt("D"); Nt("");

            w.Write(1);
            w.Write((byte)ModResourceType.Ebx);
            w.Write(0);
            Nt("systems/encrypted");
            w.Write(new byte[20]);
            w.Write((long)plain.Length); // logical size is plaintext
            w.Write((byte)0);
            w.Write(0);
            Nt("");
            w.Write(0); // added bundles

            long dataOffset = ms.Position;
            w.Write(0L);
            w.Write((long)encrypted.Length);
            w.Write(encrypted);

            long end = ms.Position;
            ms.Position = dataOffsetPos;
            w.Write(dataOffset);
            w.Write(1);
            ms.Position = end;
        }

        ms.Position = 0;
        var mod = FbmodReader.Read(ms, "v8.fbmod", loadResourceData: true);
        Assert.Equal(8u, mod.Version);
        Assert.Equal(FbmodConstants.MaxBinaryVersion, 8u);
        var ebx = Assert.Single(mod.Resources);
        Assert.Equal(plain, ebx.Data);
        Assert.False(FbmodCryptor.IsEncrypted(ebx.Data!));
    }
}
