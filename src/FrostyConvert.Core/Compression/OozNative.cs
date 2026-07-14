using OozSharp;

namespace FrostyConvert.Core.Compression;

/// <summary>
/// Built-in Oodle data decompress via <see cref="OozSharp"/> (open-source Kraken/Mermaid/etc.
/// reimplementation of <c>powzix/ooz</c>). Secondary to native <see cref="Oodle"/>.
/// </summary>
public static class OozNative
{
    public static bool IsAvailable => true;

    public static byte[] Decompress(byte[] compressed, int decompressedSize)
    {
        if (compressed is null || compressed.Length == 0)
            throw new CasDecompressException("Empty Oodle compressed buffer.");
        if (decompressedSize <= 0)
            throw new CasDecompressException($"Invalid Oodle decompressed size: {decompressedSize}.");

        try
        {
            var kraken = new Kraken();
            ReadOnlyMemory<byte> result = kraken.Decompress(compressed, decompressedSize);
            if (result.Length <= 0)
                throw new CasDecompressException("OozSharp returned empty output.");
            if (result.Length == decompressedSize)
                return result.ToArray();
            if (result.Length < decompressedSize)
                return result.ToArray();
            return result.Span[..decompressedSize].ToArray();
        }
        catch (Exception ex) when (ex is not CasDecompressException)
        {
            throw new CasDecompressException($"OozSharp Kraken decompress failed: {ex.Message}", ex);
        }
    }
}
