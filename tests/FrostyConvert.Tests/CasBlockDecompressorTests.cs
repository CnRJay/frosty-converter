using FrostyConvert.Core.Compression;

namespace FrostyConvert.Tests;

public class CasBlockDecompressorTests
{
    [Fact]
    public void Decompress_UncompressedBlock_ReturnsPayload()
    {
        byte[] raw = "hello cas"u8.ToArray();
        byte[] cas = BuildUncompressedBlock(raw);
        byte[] result = CasBlockDecompressor.Decompress(cas);
        Assert.Equal(raw, result);
    }

    [Fact]
    public void Decompress_NonCasData_ReturnsOriginal()
    {
        byte[] raw = "not compressed stream!!!!"u8.ToArray();
        byte[] result = CasBlockDecompressor.Decompress(raw);
        Assert.Equal(raw, result);
    }

    private static byte[] BuildUncompressedBlock(byte[] raw)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int size = raw.Length;
        w.Write((byte)((size >> 24) & 0xFF));
        w.Write((byte)((size >> 16) & 0xFF));
        w.Write((byte)((size >> 8) & 0xFF));
        w.Write((byte)(size & 0xFF));
        w.Write((byte)0x00);
        w.Write((byte)0x70);
        w.Write((byte)((size >> 8) & 0xFF));
        w.Write((byte)(size & 0xFF));
        w.Write(raw);
        return ms.ToArray();
    }
}
