using System.Text;

namespace FrostyConvert.Core.IO;

/// <summary>
/// Little-endian binary reader matching Frosty <c>NativeReader</c> string conventions.
/// </summary>
public sealed class EndianBinaryReader : IDisposable
{
    private readonly BinaryReader _reader;
    private readonly bool _leaveOpen;

    public EndianBinaryReader(Stream stream, bool leaveOpen = false)
    {
        _leaveOpen = leaveOpen;
        _reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen);
    }

    public Stream BaseStream => _reader.BaseStream;
    public long Position
    {
        get => BaseStream.Position;
        set => BaseStream.Position = value;
    }

    public long Length => BaseStream.Length;

    public byte ReadByte() => _reader.ReadByte();
    public byte[] ReadBytes(int count) => _reader.ReadBytes(count);
    public short ReadInt16() => _reader.ReadInt16();
    public ushort ReadUInt16() => _reader.ReadUInt16();
    public int ReadInt32() => _reader.ReadInt32();
    public uint ReadUInt32() => _reader.ReadUInt32();
    public long ReadInt64() => _reader.ReadInt64();
    public ulong ReadUInt64() => _reader.ReadUInt64();
    public float ReadSingle() => _reader.ReadSingle();
    public double ReadDouble() => _reader.ReadDouble();

    public Guid ReadGuid()
    {
        return new Guid(ReadBytes(16));
    }

    /// <summary>20-byte SHA1 digest (Frosty <c>Sha1</c>).</summary>
    public byte[] ReadSha1() => ReadBytes(20);

    public string ReadNullTerminatedString()
    {
        var sb = new StringBuilder();
        while (true)
        {
            byte b = ReadByte();
            if (b == 0)
                return sb.ToString();
            sb.Append((char)b);
        }
    }

    /// <summary>
    /// Frosty profile name encoding: 7-bit length prefix via <see cref="BinaryWriter.Write(string)"/>
    /// which for names &lt; 128 chars is a single length byte, then that many bytes.
    /// </summary>
    public string ReadLengthPrefixedString()
    {
        int len = ReadByte();
        if (len == 0)
            return string.Empty;

        // Support multi-byte 7-bit length (rare for profile names, but BinaryWriter may emit it).
        if ((len & 0x80) != 0)
        {
            int value = len & 0x7F;
            int shift = 7;
            byte b;
            do
            {
                b = ReadByte();
                value |= (b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);
            len = value;
        }

        var bytes = ReadBytes(len);
        return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
    }

    public string ReadSizedString(int length)
    {
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            char c = (char)ReadByte();
            if (c != 0)
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Pascal string: single length byte + UTF-8 bytes (FIFA/FETM headers).</summary>
    public string ReadPascalString()
    {
        int len = ReadByte();
        if (len == 0)
            return string.Empty;
        return Encoding.UTF8.GetString(ReadBytes(len));
    }

    /// <summary>.NET 7-bit encoded integer (BinaryReader.Read7BitEncodedInt).</summary>
    public int Read7BitEncodedInt()
    {
        int result = 0;
        int shift = 0;
        byte b;
        do
        {
            if (shift >= 35)
                throw new FormatException("7-bit encoded integer is too large.");
            b = ReadByte();
            result |= (b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);
        return result;
    }

    public void Dispose()
    {
        if (!_leaveOpen)
            _reader.Dispose();
    }
}
