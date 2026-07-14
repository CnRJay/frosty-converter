namespace FrostyConvert.Core.Mod;

public static class FbmodConstants
{
    /// <summary>Binary .fbmod magic: <c>FROSTY\x01\x00</c> as little-endian ulong.</summary>
    public const ulong BinaryMagic = 0x01005954534F5246;

    /// <summary>.fbproject magic: <c>FROSTY\x00\x00</c> as little-endian ulong.</summary>
    public const ulong ProjectMagic = 0x00005954534F5246;

    /// <summary>
    /// Highest binary mod format version we document from open Frosty 1.0.6.3 is 5.
    /// Madden/College Football forks ship mods at least up to version 7 with the same
    /// core resource table layout (validated against real fixtures).
    /// </summary>
    public const uint MaxBinaryVersion = 7;

    /// <summary>Highest project format version known from Frosty 1.0.6.3.</summary>
    public const uint ProjectFormatVersion = 14;

    /// <summary>Legacy collector chunk handler hash.</summary>
    public const int LegacyHandlerHash = unchecked((int)0xBD9BFB65);
}
