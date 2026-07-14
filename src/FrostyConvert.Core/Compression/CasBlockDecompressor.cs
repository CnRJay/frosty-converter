using System.IO.Compression;
using K4os.Compression.LZ4;
using ZstdSharp;

namespace FrostyConvert.Core.Compression;

public enum CasCompressionKind
{
    None = 0x00,
    ZLib = 0x02,
    LZ4 = 0x09,
    ZStd = 0x0F,
    OodleV6 = 0x11,
    OodleV4 = 0x15,
    /// <summary>Oodle variant used by FIFA Editor Tool / .fifamod (type byte 0x19, often written as BE 0x1970).</summary>
    OodleFifa = 0x19,
}

public sealed class CasDecompressException : Exception
{
    public CasDecompressException(string message) : base(message) { }
    public CasDecompressException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Decompresses Frostbite CAS-style block streams as written by <c>Utils.CompressFile</c>
/// and read by <c>CasReader.ReadBlock</c>.
/// </summary>
public static class CasBlockDecompressor
{
    /// <summary>
    /// Try to decompress a CAS block stream. Returns original data if it does not look compressed.
    /// </summary>
    public static byte[] Decompress(byte[] data)
    {
        if (data is null || data.Length < 8)
            return data ?? Array.Empty<byte>();

        // Heuristic: first 4 bytes BE = first block decompressed size; must be plausible.
        if (!LooksLikeCasStream(data))
            return data;

        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        using var br = new BinaryReader(input);

        while (input.Position + 8 <= input.Length)
        {
            byte[]? block = ReadBlock(br, input);
            if (block is null)
                break;
            output.Write(block, 0, block.Length);
        }

        if (output.Length == 0)
            return data;

        return output.ToArray();
    }

    public static bool LooksLikeCasStream(byte[] data)
    {
        if (data.Length < 8)
            return false;

        int decompSize = ReadInt32BigEndian(data, 0);
        if (decompSize <= 0 || decompSize > 0x400000) // 4MB per block max sanity
            return false;

        // compression type ushort LE (Frosty writes BE, CasReader reads LE — see format notes)
        ushort rawType = (ushort)(data[4] | (data[5] << 8));
        int type = rawType & 0x7F;
        return type is 0x00 or 0x02 or 0x09 or 0x0F or 0x11 or 0x15 or 0x19;
    }

    private static byte[]? ReadBlock(BinaryReader br, Stream stream)
    {
        if (stream.Position + 8 > stream.Length)
            return null;

        int decompressedSize = ReadInt32BigEndian(br);
        ushort compressionTypeLe = br.ReadUInt16(); // LE read of BE-written value
        ushort bufferSizeLow = ReadUInt16BigEndian(br);

        int flags = (compressionTypeLe & 0xFF00) >> 8;
        int bufferSize = bufferSizeLow;
        if ((flags & 0x0F) != 0)
            bufferSize = ((flags & 0x0F) << 16) + bufferSize;

        if ((decompressedSize & unchecked((int)0xFF000000)) != 0)
            decompressedSize &= 0x00FFFFFF;

        int compressionType = compressionTypeLe & 0x7F;

        if (bufferSize < 0 || stream.Position + bufferSize > stream.Length)
            throw new CasDecompressException(
                $"CAS block truncated (need {bufferSize} bytes at offset {stream.Position}).");

        byte[] compressed = br.ReadBytes(bufferSize);

        return compressionType switch
        {
            0x00 => compressed.Length == decompressedSize
                ? compressed
                : PadOrTrim(compressed, decompressedSize),
            0x02 => DecompressZLib(compressed, decompressedSize),
            0x09 => DecompressLz4(compressed, decompressedSize),
            0x0F => DecompressZstd(compressed, decompressedSize),
            0x11 or 0x15 or 0x19 => DecompressOodle(compressed, decompressedSize),
            _ => throw new CasDecompressException($"Unknown CAS compression type 0x{compressionType:X2}."),
        };
    }

    private static byte[] DecompressZLib(byte[] compressed, int decompressedSize)
    {
        // Frostbite uses zlib wrapper; ZLibStream handles zlib headers.
        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        var output = new byte[decompressedSize];
        int read = 0;
        while (read < decompressedSize)
        {
            int n = zlib.Read(output, read, decompressedSize - read);
            if (n == 0)
                break;
            read += n;
        }
        if (read != decompressedSize)
            throw new CasDecompressException($"ZLib decompressed {read} bytes, expected {decompressedSize}.");
        return output;
    }

    private static byte[] DecompressZstd(byte[] compressed, int decompressedSize)
    {
        try
        {
            using var decompressor = new Decompressor();
            var output = new byte[decompressedSize];
            int written = decompressor.Unwrap(
                compressed, 0, compressed.Length,
                output, 0, output.Length);
            if (written <= 0)
                throw new CasDecompressException("Zstd returned empty output.");
            if (written != decompressedSize)
            {
                var trimmed = new byte[written];
                Buffer.BlockCopy(output, 0, trimmed, 0, written);
                return trimmed;
            }
            return output;
        }
        catch (Exception ex) when (ex is not CasDecompressException)
        {
            throw new CasDecompressException("Zstd decompression failed.", ex);
        }
    }

    private static byte[] DecompressOodle(byte[] compressed, int decompressedSize)
    {
        // Primary: real Oodle via oodle-data-shared.dll (UE Oodle distribution) or oo2core.
        // Secondary: OozSharp pure-managed Kraken reimplementation (incomplete for some streams).
        Exception? primaryEx = null;
        if (Oodle.IsBound)
        {
            try
            {
                return Oodle.Decompress(compressed, decompressedSize);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                primaryEx = ex;
            }
        }

        if (OozNative.IsAvailable)
        {
            try
            {
                return OozNative.Decompress(compressed, decompressedSize);
            }
            catch (Exception oozEx) when (oozEx is not OutOfMemoryException)
            {
                string primary = primaryEx is null
                    ? "Oodle native DLL not loaded"
                    : $"Oodle native failed ({primaryEx.Message})";
                throw new CasDecompressException(
                    $"Oodle decompress failed: {primary}; OozSharp fallback failed ({oozEx.Message}). " +
                    "Ensure oodle-data-shared.dll is next to the tool/plugin, or pass --oodle path\\to\\oo2core_win64.dll.",
                    oozEx);
            }
        }

        if (primaryEx != null)
            throw new CasDecompressException(
                $"Oodle decompress failed ({primaryEx.Message}). Ensure oodle-data-shared.dll is next to the tool/plugin.",
                primaryEx);

        throw new CasDecompressException(
            "Oodle native DLL not loaded. Ensure oodle-data-shared.dll is next to the tool/plugin.");
    }

    private static byte[] DecompressLz4(byte[] compressed, int decompressedSize)
    {
        var output = new byte[decompressedSize];
        int written = LZ4Codec.Decode(compressed, 0, compressed.Length, output, 0, output.Length);
        if (written < 0)
            throw new CasDecompressException("LZ4 decompression failed.");
        if (written != decompressedSize)
        {
            var trimmed = new byte[written];
            Buffer.BlockCopy(output, 0, trimmed, 0, written);
            return trimmed;
        }
        return output;
    }

    private static byte[] PadOrTrim(byte[] data, int size)
    {
        if (data.Length == size)
            return data;
        var result = new byte[size];
        Buffer.BlockCopy(data, 0, result, 0, Math.Min(data.Length, size));
        return result;
    }

    private static int ReadInt32BigEndian(BinaryReader br)
    {
        byte b0 = br.ReadByte();
        byte b1 = br.ReadByte();
        byte b2 = br.ReadByte();
        byte b3 = br.ReadByte();
        return (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
    }

    private static int ReadInt32BigEndian(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static ushort ReadUInt16BigEndian(BinaryReader br)
    {
        byte b0 = br.ReadByte();
        byte b1 = br.ReadByte();
        return (ushort)((b0 << 8) | b1);
    }
}
