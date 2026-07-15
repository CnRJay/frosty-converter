using System.Buffers.Binary;

namespace FrostyConvert.Core.Legacy;

/// <summary>
/// Builds FC26 Texture RES blobs (168 bytes, ResType 0x6BDE20BA).
/// Pixel format values from <c>Sdk.Ebx.RenderFormat</c> (FC26SDK).
/// </summary>
public static class TextureResBuilder
{
    public const uint TextureResType = 0x6BDE20BA;
    public const int FixedSize = 168;
    public const int ChunkIdOffset = 0x28;
    public const int MipSizesOffset = 0x38;
    public const int MaxMipsInHeader = 12;
    public const int NameOffset = 0x68;

    // Sdk.Ebx.RenderFormat (FC26) — 0-based enum order
    public const int FormatR8G8B8A8Unorm = 18;
    public const int FormatR8G8B8A8Srgb = 20;
    public const int FormatB8G8R8A8Unorm = 23;
    public const int FormatB8G8R8A8Srgb = 24;
    public const int FormatBc1Unorm = 54;
    public const int FormatBc1Srgb = 55;
    public const int FormatBc3Unorm = 60;
    public const int FormatBc3Srgb = 61;
    public const int FormatBc7Unorm = 66;
    public const int FormatBc7Srgb = 67;

    public static byte[] BuildFromTemplate(
        ReadOnlySpan<byte> templateRes,
        Guid chunkId,
        DdsHeader dds,
        string resourceName)
    {
        if (templateRes.Length < FixedSize)
            throw new ArgumentException($"Texture RES template must be at least {FixedSize} bytes.", nameof(templateRes));

        var buf = new byte[FixedSize];
        templateRes[..FixedSize].CopyTo(buf);

        int width = Math.Clamp(dds.Width, 1, 16384);
        int height = Math.Clamp(dds.Height, 1, 16384);
        int mips = Math.Clamp(dds.MipMapCount, 1, MaxMipsInHeader);
        bool block = dds.IsBlockCompressed;
        int bytesPerBlock = block ? GetBytesPerBlock(dds) : 0;
        int bytesPerPixel = block ? 0 : dds.BytesPerPixel;
        int pixelFormat = MapPixelFormat(dds, block, bytesPerBlock);

        // Fully self-contained 2D texture in the chunk (no streaming).
        // TextureFlags.Streaming = 0x1 causes FET to use mipOffsets / partial reads → E_INVALIDARG.
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x00), 0); // FirstMipOffset
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x04), 0); // SecondMipOffset
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x08), 0); // TextureType_2d
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0x0C), pixelFormat);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x10), 0); // unknown1

        // flags: 0 = non-streaming; SrgbGamma(0x2) optional for sRGB formats
        ushort flags = 0;
        if (pixelFormat is FormatB8G8R8A8Srgb or FormatR8G8B8A8Srgb
            or FormatBc1Srgb or FormatBc3Srgb or FormatBc7Srgb)
            flags = 0x2; // TextureFlags.SrgbGamma
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0x14), flags);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0x16), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0x18), (ushort)height);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0x1A), 1); // depth
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(0x1C), 1); // sliceCount
        buf[0x1E] = (byte)mips;
        buf[0x1F] = 0; // firstMip
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x20), (uint)mips);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0x24), 0);

        chunkId.ToByteArray().CopyTo(buf.AsSpan(ChunkIdOffset));

        int[] mipSizes = block
            ? ComputeBlockMipSizes(width, height, mips, bytesPerBlock)
            : ComputeUncompressedMipSizes(width, height, mips, bytesPerPixel);

        int totalSize = 0;
        for (int i = 0; i < MaxMipsInHeader; i++)
        {
            uint sz = i < mipSizes.Length ? (uint)mipSizes[i] : 0u;
            totalSize += (int)sz;
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(MipSizesOffset + i * 4), sz);
        }

        // Prefer UNORM for BGRA if SRGB causes device create issues on some paths —
        // kept SRGB with SrgbGamma flag above; chunk logical size must match totalSize.
        _ = totalSize;

        for (int i = NameOffset; i < FixedSize; i++)
            buf[i] = 0;
        string shortName = resourceName.Replace('\\', '/');
        int slash = shortName.LastIndexOf('/');
        if (slash >= 0)
            shortName = shortName[(slash + 1)..];
        if (shortName.Length > FixedSize - NameOffset - 1)
            shortName = shortName[..(FixedSize - NameOffset - 1)];
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(shortName);
        Buffer.BlockCopy(nameBytes, 0, buf, NameOffset, nameBytes.Length);

        return buf;
    }

    public static int TotalMipDataSize(ReadOnlySpan<byte> res)
    {
        if (res.Length < MipSizesOffset + MaxMipsInHeader * 4)
            return 0;
        int mips = res[0x1E];
        mips = Math.Clamp(mips, 1, MaxMipsInHeader);
        int total = 0;
        for (int i = 0; i < mips; i++)
            total += (int)BinaryPrimitives.ReadUInt32LittleEndian(res.Slice(MipSizesOffset + i * 4));
        return total;
    }

    public static int[] ComputeMipSizes(int width, int height, int mipCount, int bytesPerBlock) =>
        ComputeBlockMipSizes(width, height, mipCount, bytesPerBlock);

    public static int[] ComputeBlockMipSizes(int width, int height, int mipCount, int bytesPerBlock)
    {
        var sizes = new int[mipCount];
        int w = width;
        int h = height;
        for (int i = 0; i < mipCount; i++)
        {
            int bw = Math.Max(1, (w + 3) / 4);
            int bh = Math.Max(1, (h + 3) / 4);
            sizes[i] = bw * bh * bytesPerBlock;
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
        return sizes;
    }

    public static int[] ComputeUncompressedMipSizes(int width, int height, int mipCount, int bytesPerPixel)
    {
        var sizes = new int[mipCount];
        int w = width;
        int h = height;
        for (int i = 0; i < mipCount; i++)
        {
            sizes[i] = w * h * bytesPerPixel;
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
        return sizes;
    }

    public static int GetBytesPerBlock(DdsHeader dds)
    {
        if (!dds.IsBlockCompressed)
            return 0;

        if (dds.HasDx10)
        {
            return dds.DxgiFormat switch
            {
                71 or 72 => 8,   // BC1
                74 or 75 => 16,  // BC2
                77 or 78 => 16,  // BC3
                80 or 81 => 8,   // BC4
                83 or 84 => 16,  // BC5
                95 or 96 => 16,  // BC6H
                98 or 99 => 16,  // BC7
                _ => 16,
            };
        }

        return dds.FourCC switch
        {
            0x31545844 => 8,  // DXT1
            0x32545844 => 16, // DXT2
            0x33545844 => 16, // DXT3
            0x34545844 => 16, // DXT4
            0x35545844 => 16, // DXT5
            0x31495441 => 8,  // ATI1
            0x32495441 => 16, // ATI2
            _ => 16,
        };
    }

    public static int MapPixelFormat(DdsHeader dds) =>
        MapPixelFormat(dds, dds.IsBlockCompressed, GetBytesPerBlock(dds));

    public static int MapPixelFormat(DdsHeader dds, bool blockCompressed, int bytesPerBlock)
    {
        if (!blockCompressed)
        {
            // BGRA: R=00FF0000 G=0000FF00 B=000000FF (FIFA crest DDS)
            bool bgra = dds.RBitMask == 0x00FF0000 && dds.BBitMask == 0x000000FF;
            bool rgba = dds.RBitMask == 0x000000FF && dds.BBitMask == 0x00FF0000;
            // Use UNORM for promoted assets — more reliable with D3D CreateTexture2D
            // (SRGB + Streaming flags previously caused E_INVALIDARG).
            if (bgra)
                return FormatB8G8R8A8Unorm;
            if (rgba)
                return FormatR8G8B8A8Unorm;
            return FormatB8G8R8A8Unorm;
        }

        if (dds.HasDx10)
        {
            return dds.DxgiFormat switch
            {
                71 => FormatBc1Unorm,
                72 => FormatBc1Srgb,
                77 => FormatBc3Unorm,
                78 => FormatBc3Srgb,
                98 => FormatBc7Unorm,
                99 => FormatBc7Srgb,
                _ => bytesPerBlock <= 8 ? FormatBc1Srgb : FormatBc7Srgb,
            };
        }

        return dds.FourCC switch
        {
            0x31545844 => FormatBc1Srgb, // DXT1
            0x35545844 => FormatBc3Srgb, // DXT5
            _ => bytesPerBlock <= 8 ? FormatBc1Srgb : FormatBc7Srgb,
        };
    }

    public static FifaMod.FifamodResource? FindTextureResTemplate(IEnumerable<FifaMod.FifamodResource> resources)
    {
        return resources
            .Where(r => r.Kind == FifaMod.FifamodResourceKind.Res)
            .Where(r => r.ResType == TextureResType || r.UncompressedSize is >= 100 and <= 256)
            .Where(r => r.Data is { Length: > 0 } || r.CompressedData is { Length: > 0 })
            .OrderByDescending(r => r.Data?.Length == FixedSize)
            .ThenByDescending(r => r.UncompressedSize == FixedSize)
            .FirstOrDefault();
    }
}
