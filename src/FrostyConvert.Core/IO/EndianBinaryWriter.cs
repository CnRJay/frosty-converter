using System.Text;

namespace FrostyConvert.Core.IO;

/// <summary>Little-endian writer matching Frosty <c>NativeWriter</c> string conventions.</summary>
public sealed class EndianBinaryWriter : IDisposable
{
    private readonly BinaryWriter _w;
    private readonly bool _leaveOpen;

    public EndianBinaryWriter(Stream stream, bool leaveOpen = false)
    {
        _leaveOpen = leaveOpen;
        _w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen);
    }

    public Stream BaseStream => _w.BaseStream;

    public long Position
    {
        get => BaseStream.Position;
        set => BaseStream.Position = value;
    }

    public long Length => BaseStream.Length;

    public void Write(byte value) => _w.Write(value);
    public void WriteByte(byte value) => _w.Write(value);
    public void Write(byte[] buffer) => _w.Write(buffer);
    public void WriteBytes(byte[] buffer) => _w.Write(buffer);
    public void Write(byte[] buffer, int index, int count) => _w.Write(buffer, index, count);
    public void Write(short value) => _w.Write(value);
    public void Write(ushort value) => _w.Write(value);
    public void WriteUInt16(ushort value) => _w.Write(value);
    public void Write(int value) => _w.Write(value);
    public void Write(uint value) => _w.Write(value);
    public void WriteUInt32(uint value) => _w.Write(value);
    public void Write(long value) => _w.Write(value);
    public void Write(ulong value) => _w.Write(value);
    public void Write(bool value) => _w.Write(value);

    public void Write(Guid guid) => _w.Write(guid.ToByteArray());
    public void WriteGuid(Guid guid) => _w.Write(guid.ToByteArray());

    /// <summary>.NET BinaryWriter.Write(string): 7-bit length + UTF-8 bytes.</summary>
    public void WriteLengthPrefixedString(string? value)
    {
        value ??= "";
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Write7BitEncodedInt(bytes.Length);
        if (bytes.Length > 0)
            _w.Write(bytes);
    }

    public void Write7BitEncodedInt(int value)
    {
        uint v = (uint)value;
        while (v >= 0x80)
        {
            _w.Write((byte)(v | 0x80));
            v >>= 7;
        }
        _w.Write((byte)v);
    }

    public void WriteSha1(byte[]? sha1)
    {
        if (sha1 is null || sha1.Length != 20)
        {
            _w.Write(new byte[20]);
            return;
        }
        _w.Write(sha1);
    }

    public void WriteNullTerminatedString(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            _w.Write(Encoding.UTF8.GetBytes(value));
        _w.Write((byte)0);
    }

    public void WriteInt32BigEndian(int value)
    {
        _w.Write((byte)((value >> 24) & 0xFF));
        _w.Write((byte)((value >> 16) & 0xFF));
        _w.Write((byte)((value >> 8) & 0xFF));
        _w.Write((byte)(value & 0xFF));
    }

    public void WriteUInt16BigEndian(ushort value)
    {
        _w.Write((byte)((value >> 8) & 0xFF));
        _w.Write((byte)(value & 0xFF));
    }

    public void Dispose()
    {
        if (!_leaveOpen)
            _w.Dispose();
    }
}
