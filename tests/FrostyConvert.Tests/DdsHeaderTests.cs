using FrostyConvert.Core.Compression;
using FrostyConvert.Core.Legacy;

namespace FrostyConvert.Tests;

public class DdsHeaderTests
{
    [Fact]
    public void Parse_MinimalDds_ReadsDimensions()
    {
        // Minimal 128-byte DDS header: 4x4, 1 mip, DXT1 fourCC
        var dds = new byte[128];
        BitConverter.TryWriteBytes(dds.AsSpan(0), 0x20534444u); // magic
        BitConverter.TryWriteBytes(dds.AsSpan(4), 124); // header size
        BitConverter.TryWriteBytes(dds.AsSpan(8), 0x1007u); // flags caps|height|width|pixelformat
        BitConverter.TryWriteBytes(dds.AsSpan(12), 4); // height
        BitConverter.TryWriteBytes(dds.AsSpan(16), 4); // width
        BitConverter.TryWriteBytes(dds.AsSpan(28), 1); // mips
        BitConverter.TryWriteBytes(dds.AsSpan(76), 32); // pf size
        BitConverter.TryWriteBytes(dds.AsSpan(84), 0x31545844u); // DXT1

        Assert.True(DdsHeader.TryParse(dds, out var h));
        Assert.Equal(4, h.Width);
        Assert.Equal(4, h.Height);
        Assert.Equal(1, h.MipMapCount);
        Assert.Equal(128, h.DataOffset);
    }

    [Fact]
    public void MapLegacyPath_StripsDataUiAndExtension()
    {
        string name = TextureAssetPromoter.MapLegacyPathToAssetName(
            "data/ui/imgAssets/crestFWC/111465.dds",
            "content/ui/legacy");
        Assert.Equal("content/ui/legacy/imgassets/crestfwc/111465", name);
    }

    [Fact]
    public void AllocateResRid_UniqueAndNonZero()
    {
        var used = new HashSet<ulong>();
        ulong a = TextureAssetPromoter.AllocateResRid("content/ui/legacy/a", used);
        ulong b = TextureAssetPromoter.AllocateResRid("content/ui/legacy/b", used);
        Assert.NotEqual(0UL, a);
        Assert.NotEqual(0UL, b);
        Assert.NotEqual(a, b);
        Assert.Equal(2, used.Count);
    }

    [Fact]
    public void CasStore_RoundTrip_Large_WithGuard7()
    {
        var src = new byte[100_000];
        Random.Shared.NextBytes(src);
        byte[] cas = CasBlockCompressor.Compress(src);
        byte[] back = CasBlockDecompressor.Decompress(cas);
        Assert.Equal(src, back);

        uint word1 = (uint)((cas[4] << 24) | (cas[5] << 16) | (cas[6] << 8) | cas[7]);
        Assert.Equal(7u, (word1 >> 20) & 0xF);
        Assert.Equal(0x00, cas[4]); // store type
        Assert.Equal(0x70, cas[5]); // flags
    }

    [Fact]
    public void TextureRes_MipSizes_MatchBc7_2048x1024()
    {
        // Wipe sample: BC7 2048x1024 → mip0 = 2097152
        int[] sizes = TextureResBuilder.ComputeBlockMipSizes(2048, 1024, 12, 16);
        Assert.Equal(2_097_152, sizes[0]);
        Assert.Equal(524_288, sizes[1]);
        Assert.Equal(2_796_240, sizes.Sum());
    }

    [Fact]
    public void TextureRes_UncompressedRgba_MipChain_256()
    {
        // FIFA crest dark: 256x256 BGRA8, 9 mips → 349524 bytes
        int[] sizes = TextureResBuilder.ComputeUncompressedMipSizes(256, 256, 9, 4);
        Assert.Equal(262_144, sizes[0]);
        Assert.Equal(349_524, sizes.Sum());
    }

    [Fact]
    public void TextureRes_Uncompressed_300x3000_MatchesPixelLen()
    {
        int[] sizes = TextureResBuilder.ComputeUncompressedMipSizes(300, 3000, 1, 4);
        Assert.Equal(3_600_000, sizes.Sum());
        Assert.Equal(23, TextureResBuilder.FormatB8G8R8A8Unorm);
    }

    [Fact]
    public void TextureRes_Build_WritesWidthHeightChunkId()
    {
        var template = new byte[168];
        var ddsBytes = new byte[128 + 8]; // header + 1 DXT1 block (4x4)
        BitConverter.TryWriteBytes(ddsBytes.AsSpan(0), 0x20534444u);
        BitConverter.TryWriteBytes(ddsBytes.AsSpan(4), 124);
        BitConverter.TryWriteBytes(ddsBytes.AsSpan(12), 4); // h
        BitConverter.TryWriteBytes(ddsBytes.AsSpan(16), 4); // w
        BitConverter.TryWriteBytes(ddsBytes.AsSpan(28), 1); // mips
        BitConverter.TryWriteBytes(ddsBytes.AsSpan(84), 0x31545844u); // DXT1

        Assert.True(DdsHeader.TryParse(ddsBytes, out var dds));
        Guid id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        byte[] res = TextureResBuilder.BuildFromTemplate(template, id, dds, "content/ui/legacy/test");

        Assert.Equal(4, BitConverter.ToUInt16(res, 0x16));
        Assert.Equal(4, BitConverter.ToUInt16(res, 0x18));
        Assert.Equal(1, BitConverter.ToUInt16(res, 0x1E));
        Assert.Equal(id, new Guid(res.AsSpan(0x28, 16)));
        Assert.Equal(8, BitConverter.ToInt32(res, 0x38)); // one DXT1 4x4 block
        Assert.Equal(8, TextureResBuilder.TotalMipDataSize(res));
    }
}
