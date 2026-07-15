using System;
using System.IO;
using System.IO.Compression;
using K4os.Compression.LZ4;
using ZstdSharp;

namespace FrostyConvert.MmcPlugin;

public sealed class CasDecompressException : Exception
{
    public CasDecompressException(string message) : base(message) { }
    public CasDecompressException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>CAS block decompress for net48 plugin (Oodle via OodleNet48).</summary>
public static class CasBlockDecompressorNet48
{
    public static byte[] Decompress(byte[] data)
    {
        if (data == null || data.Length < 8 || !LooksLikeCas(data))
            return data ?? Array.Empty<byte>();

        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        using var br = new BinaryReader(input);

        while (input.Position + 8 <= input.Length)
        {
            byte[]? block = ReadBlock(br, input);
            if (block == null) break;
            output.Write(block, 0, block.Length);
        }

        return output.Length == 0 ? data : output.ToArray();
    }

    private static bool LooksLikeCas(byte[] data)
    {
        int decomp = (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
        // Per-block uncompressed size (texture mip blocks can be large)
        if (decomp <= 0 || decomp > 0x4000000) return false;
        ushort raw = (ushort)(data[4] | (data[5] << 8));
        int type = raw & 0x7F;
        // 0x19 = Oodle variant used by recent Frostbite / FIFA titles
        return type is 0x00 or 0x02 or 0x09 or 0x0F or 0x11 or 0x15 or 0x19;
    }

    private static byte[]? ReadBlock(BinaryReader br, Stream stream)
    {
        if (stream.Position + 8 > stream.Length) return null;
        int decompSize = ReadBe32(br);
        ushort typeLe = br.ReadUInt16();
        ushort sizeLow = ReadBe16(br);
        int flags = (typeLe & 0xFF00) >> 8;
        int bufferSize = sizeLow;
        if ((flags & 0x0F) != 0)
            bufferSize = ((flags & 0x0F) << 16) + bufferSize;
        if ((decompSize & unchecked((int)0xFF000000)) != 0)
            decompSize &= 0x00FFFFFF;
        int compressionType = typeLe & 0x7F;
        if (bufferSize < 0 || stream.Position + bufferSize > stream.Length)
            throw new CasDecompressException("CAS block truncated");
        byte[] compressed = br.ReadBytes(bufferSize);
        return compressionType switch
        {
            0x00 => compressed.Length == decompSize
                ? compressed
                : PadOrTrim(compressed, decompSize),
            0x02 => Zlib(compressed, decompSize),
            0x09 => Lz4(compressed, decompSize),
            0x0F => Zstd(compressed, decompSize),
            0x11 or 0x15 or 0x19 => OodleNet48.Decompress(compressed, decompSize),
            _ => throw new CasDecompressException($"Unknown CAS type 0x{compressionType:X2}"),
        };
    }

    private static byte[] PadOrTrim(byte[] data, int size)
    {
        if (data.Length == size) return data;
        var o = new byte[size];
        Buffer.BlockCopy(data, 0, o, 0, Math.Min(data.Length, size));
        return o;
    }

    private static byte[] Zlib(byte[] c, int size)
    {
        using var input = new MemoryStream(c);
        // DeflateStream needs raw deflate; zlib has 2-byte header — strip if present
        if (c.Length > 2 && c[0] == 0x78)
            input.Position = 2;
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        var output = new byte[size];
        int read = 0;
        while (read < size)
        {
            int n = deflate.Read(output, read, size - read);
            if (n == 0) break;
            read += n;
        }
        return output;
    }

    private static byte[] Zstd(byte[] c, int size)
    {
        using var d = new Decompressor();
        var output = new byte[size];
        int written = d.Unwrap(c, 0, c.Length, output, 0, output.Length);
        if (written <= 0) throw new CasDecompressException("Zstd failed");
        if (written == size) return output;
        var t = new byte[written];
        Buffer.BlockCopy(output, 0, t, 0, written);
        return t;
    }

    private static byte[] Lz4(byte[] c, int size)
    {
        var output = new byte[size];
        int written = LZ4Codec.Decode(c, 0, c.Length, output, 0, output.Length);
        if (written < 0) throw new CasDecompressException("LZ4 failed");
        if (written == size) return output;
        var t = new byte[written];
        Buffer.BlockCopy(output, 0, t, 0, written);
        return t;
    }

    private static int ReadBe32(BinaryReader br) =>
        (br.ReadByte() << 24) | (br.ReadByte() << 16) | (br.ReadByte() << 8) | br.ReadByte();

    private static ushort ReadBe16(BinaryReader br) =>
        (ushort)((br.ReadByte() << 8) | br.ReadByte());
}
