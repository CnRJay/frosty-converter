namespace FrostyConvert.Core.Legacy;

/// <summary>DDS header parser (DX9 + DX10 optional) with uncompressed RGB support.</summary>
public sealed class DdsHeader
{
    public const uint Magic = 0x20534444; // 'DDS '
    public const uint DdpfFourCc = 0x4;
    public const uint DdpfRgb = 0x40;
    public const uint DdpfAlphaPixels = 0x1;

    public int HeaderSize { get; init; }
    public uint Flags { get; init; }
    public int Height { get; init; }
    public int Width { get; init; }
    public int PitchOrLinearSize { get; init; }
    public int Depth { get; init; }
    public int MipMapCount { get; init; }
    public uint PfFlags { get; init; }
    public uint FourCC { get; init; }
    public uint RgbBitCount { get; init; }
    public uint RBitMask { get; init; }
    public uint GBitMask { get; init; }
    public uint BBitMask { get; init; }
    public uint ABitMask { get; init; }
    public bool HasDx10 { get; init; }
    public uint DxgiFormat { get; init; }
    public int DataOffset { get; init; }

    public bool IsBlockCompressed
    {
        get
        {
            if (HasDx10)
                return DxgiFormat is >= 70 and <= 99;
            if (FourCC == 0)
                return false;
            // DDPF_FOURCC, or a known compressed FourCC even if flags are incomplete
            if ((PfFlags & DdpfFourCc) != 0)
                return true;
            return FourCC is 0x31545844 or 0x32545844 or 0x33545844 or 0x34545844 or 0x35545844
                or 0x31495441 or 0x32495441 or 0x30315844;
        }
    }

    public bool IsUncompressedRgb =>
        !IsBlockCompressed && (FourCC == 0 || (PfFlags & DdpfRgb) != 0);

    public int BytesPerPixel
    {
        get
        {
            if (IsBlockCompressed)
                return 0;
            if (RgbBitCount is 8 or 16 or 24 or 32)
                return (int)(RgbBitCount / 8);
            if (PitchOrLinearSize > 0 && Width > 0)
            {
                int bpp = PitchOrLinearSize / Width;
                if (bpp is 1 or 2 or 3 or 4)
                    return bpp;
            }
            return 4;
        }
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out DdsHeader header)
    {
        header = null!;
        if (data.Length < 128)
            return false;
        if (BitConverter.ToUInt32(data) != Magic)
            return false;

        int headerSize = BitConverter.ToInt32(data.Slice(4));
        uint flags = BitConverter.ToUInt32(data.Slice(8));
        int height = BitConverter.ToInt32(data.Slice(12));
        int width = BitConverter.ToInt32(data.Slice(16));
        int pitch = BitConverter.ToInt32(data.Slice(20));
        int depth = BitConverter.ToInt32(data.Slice(24));
        int mips = BitConverter.ToInt32(data.Slice(28));
        // pixel format @ 76
        uint pfFlags = BitConverter.ToUInt32(data.Slice(80));
        uint fourCC = BitConverter.ToUInt32(data.Slice(84));
        uint rgbBits = BitConverter.ToUInt32(data.Slice(88));
        uint rMask = BitConverter.ToUInt32(data.Slice(92));
        uint gMask = BitConverter.ToUInt32(data.Slice(96));
        uint bMask = BitConverter.ToUInt32(data.Slice(100));
        uint aMask = BitConverter.ToUInt32(data.Slice(104));

        bool dx10 = fourCC == 0x30315844; // 'DX10'
        int dataOffset = 128;
        uint dxgi = 0;
        if (dx10)
        {
            if (data.Length < 148)
                return false;
            dxgi = BitConverter.ToUInt32(data.Slice(128));
            dataOffset = 148;
        }

        if (width <= 0 || height <= 0)
            return false;
        if (mips <= 0)
            mips = 1;

        header = new DdsHeader
        {
            HeaderSize = headerSize,
            Flags = flags,
            Height = height,
            Width = width,
            PitchOrLinearSize = pitch,
            Depth = depth > 0 ? depth : 1,
            MipMapCount = mips,
            PfFlags = pfFlags,
            FourCC = fourCC,
            RgbBitCount = rgbBits,
            RBitMask = rMask,
            GBitMask = gMask,
            BBitMask = bMask,
            ABitMask = aMask,
            HasDx10 = dx10,
            DxgiFormat = dxgi,
            DataOffset = dataOffset,
        };
        return true;
    }

    public static DdsHeader Parse(ReadOnlySpan<byte> data)
    {
        if (!TryParse(data, out var h))
            throw new InvalidDataException("Not a valid DDS texture.");
        return h;
    }
}
