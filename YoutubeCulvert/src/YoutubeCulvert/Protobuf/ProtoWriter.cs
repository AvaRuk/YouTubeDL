using System.Buffers;
using System.Text;

namespace YoutubeCulvert.Protobuf;

/// <summary>
/// Minimal protobuf writer. InnerTube expects a handful of small protobuf
/// messages encoded as base64 (search filters, comment tokens, ...), so a full
/// protobuf runtime would be an unnecessary dependency.
/// </summary>
internal sealed class ProtoWriter
{
    private readonly ArrayBufferWriter<byte> _buffer = new(64);

    public ProtoWriter WriteString(int fieldNumber, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return this;

        var bytes = Encoding.UTF8.GetBytes(value);
        WriteTag(fieldNumber, 2);
        WriteVarint((ulong)bytes.Length);
        _buffer.Write(bytes);
        return this;
    }

    public ProtoWriter WriteMessage(int fieldNumber, ProtoWriter? message)
    {
        if (message is null)
            return this;

        var bytes = message.ToArray();
        if (bytes.Length == 0)
            return this;

        WriteTag(fieldNumber, 2);
        WriteVarint((ulong)bytes.Length);
        _buffer.Write(bytes);
        return this;
    }

    /// <summary>Writes an int32 field; proto3 omits zero values.</summary>
    public ProtoWriter WriteInt32(int fieldNumber, int value)
    {
        if (value == 0)
            return this;

        WriteTag(fieldNumber, 0);
        WriteVarint((ulong)(uint)value);
        return this;
    }

    /// <summary>Writes an int32 field even when it is zero.</summary>
    public ProtoWriter WriteInt32Always(int fieldNumber, int value)
    {
        WriteTag(fieldNumber, 0);
        WriteVarint((ulong)(uint)value);
        return this;
    }

    public ProtoWriter WriteBool(int fieldNumber, bool value)
    {
        if (!value)
            return this;

        WriteTag(fieldNumber, 0);
        WriteVarint(1);
        return this;
    }

    public byte[] ToArray() => _buffer.WrittenSpan.ToArray();

    public string ToBase64() => Convert.ToBase64String(ToArray());

    public string ToBase64Url() => ToBase64().TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private void WriteTag(int fieldNumber, int wireType) =>
        WriteVarint((ulong)((uint)(fieldNumber << 3) | (uint)wireType));

    private void WriteVarint(ulong value)
    {
        Span<byte> buffer = stackalloc byte[10];
        var index = 0;

        while (value >= 0x80)
        {
            buffer[index++] = (byte)(value | 0x80);
            value >>= 7;
        }

        buffer[index++] = (byte)value;
        _buffer.Write(buffer[..index]);
    }
}
