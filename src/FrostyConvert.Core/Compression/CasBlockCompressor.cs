namespace FrostyConvert.Core.Compression;

/// <summary>
/// Writes Frostbite CAS block streams accepted by FIFA Editor Tool's decompress path.
/// Uses compression type <c>0x00</c> (store) with flag byte <c>0x70</c> so the
/// codec guard nibble is 7 (required by FET). Multi-block for large payloads.
/// </summary>
public static class CasBlockCompressor
{
    /// <summary>Max payload per block (u16 size field; flag low-nibble extension unused).</summary>
    public const int MaxBlockPayload = 0xFFFF;

    /// <summary>
    /// Encode <paramref name="uncompressed"/> as one or more CAS store blocks (type 0x00).
    /// Prefer this over zlib — FET may not ship a zlib native and throws "Dll was not found".
    /// </summary>
    public static byte[] Compress(byte[] uncompressed)
    {
        if (uncompressed is null || uncompressed.Length == 0)
            return Array.Empty<byte>();

        if (uncompressed.Length <= MaxBlockPayload)
            return BuildStoreBlock(uncompressed, 0, uncompressed.Length);

        using var ms = new MemoryStream(uncompressed.Length + (uncompressed.Length / MaxBlockPayload + 1) * 8);
        int offset = 0;
        while (offset < uncompressed.Length)
        {
            int n = Math.Min(MaxBlockPayload, uncompressed.Length - offset);
            byte[] block = BuildStoreBlock(uncompressed, offset, n);
            ms.Write(block, 0, block.Length);
            offset += n;
        }
        return ms.ToArray();
    }

    /// <summary>Same as <see cref="Compress"/> (store blocks).</summary>
    public static byte[] StoreUncompressed(byte[] data) => Compress(data);

    private static byte[] BuildStoreBlock(byte[] data, int offset, int length)
    {
        // Stream layout matching FIFA .fifamod samples:
        //   u32 BE decompressed size
        //   u8  compression type (0x00 = none)
        //   u8  flags (0x70 → guard nibble 7 when header is read as BE u32 at +4)
        //   u16 BE compressed size (== length for store)
        //   payload
        var result = new byte[8 + length];
        result[0] = (byte)((length >> 24) & 0xFF);
        result[1] = (byte)((length >> 16) & 0xFF);
        result[2] = (byte)((length >> 8) & 0xFF);
        result[3] = (byte)(length & 0xFF);
        result[4] = 0x00; // type none
        result[5] = 0x70; // flags / guard
        result[6] = (byte)((length >> 8) & 0xFF);
        result[7] = (byte)(length & 0xFF);
        Buffer.BlockCopy(data, offset, result, 8, length);
        return result;
    }
}
