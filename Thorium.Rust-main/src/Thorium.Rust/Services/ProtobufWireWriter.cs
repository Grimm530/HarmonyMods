using System;
using System.IO;
using System.Text;

namespace Thorium.Rust.Services;

internal enum ProtobufWireType
{
    Varint = 0,
    Fixed64 = 1,
    LengthDelimited = 2,
    Fixed32 = 5,
}

/// <summary>
/// Minimal Protocol Buffers wire-format writer (no external serializers).
/// Implements: varint, fixed32, length-delimited, embedded messages.
/// </summary>
internal sealed class ProtobufWireWriter
{
    private readonly Stream _stream;

    public ProtobufWireWriter(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    public static byte[] BuildMessage(Action<ProtobufWireWriter> write)
    {
        using var ms = new MemoryStream();
        var w = new ProtobufWireWriter(ms);
        write(w);
        return ms.ToArray();
    }

    public void WriteTag(int fieldNumber, ProtobufWireType wireType)
    {
        if (fieldNumber <= 0) throw new ArgumentOutOfRangeException(nameof(fieldNumber));
        var tag = (uint)((fieldNumber << 3) | (int)wireType);
        WriteVarint(tag);
    }

    public void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            _stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        _stream.WriteByte((byte)value);
    }

    public void WriteVarint(uint value) => WriteVarint((ulong)value);

    public void WriteInt64(long value) => WriteVarint((ulong)value);

    public void WriteUInt64(ulong value) => WriteVarint(value);

    public void WriteInt32(int value) => WriteVarint((uint)value);

    public void WriteUInt32(uint value) => WriteVarint(value);

    public void WriteBool(bool value) => WriteVarint(value ? 1u : 0u);

    public void WriteFixed32(float value)
    {
        var b = BitConverter.GetBytes(value);
        // protobuf fixed32 is little-endian
        _stream.Write(b, 0, 4);
    }

    public void WriteString(string? value)
    {
        value ??= string.Empty;
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteVarint((uint)bytes.Length);
        if (bytes.Length > 0)
            _stream.Write(bytes, 0, bytes.Length);
    }

    public void WriteBytes(byte[]? value)
    {
        if (value == null || value.Length == 0)
        {
            WriteVarint(0u);
            return;
        }
        WriteVarint((uint)value.Length);
        _stream.Write(value, 0, value.Length);
    }

    public void WriteEmbeddedMessage(int fieldNumber, Action<ProtobufWireWriter> write)
    {
        WriteTag(fieldNumber, ProtobufWireType.LengthDelimited);
        var payload = BuildMessage(write);
        WriteVarint((uint)payload.Length);
        if (payload.Length > 0)
            _stream.Write(payload, 0, payload.Length);
    }
}
