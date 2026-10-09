using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.IO;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Flash;

public class FlashOutgoingPacket : IOutgoingPacket
{
    private readonly RecyclableMemoryStream _stream;
    public int MessageId { get; set; }

    public FlashOutgoingPacket(RecyclableMemoryStream stream)
    {
        _stream = stream;
        _stream.Advance(6);
    }

    public ReadOnlyMemory<byte> Buffer => _stream.GetMemory();
    public void WriteByte(byte value) => _stream.WriteByte(value);

    public void WriteShort(short value)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(sizeof(short));
        var span = buffer.AsSpan();

        if (BitConverter.IsLittleEndian) {
            value = BinaryPrimitives.ReverseEndianness(value);
        }

        MemoryMarshal.Write(span, in value);
        _stream.Write(span.Slice(0, sizeof(short)));
        ArrayPool<byte>.Shared.Return(buffer);
    }

    public void WriteInt(int value)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(sizeof(int));
        var span = buffer.AsSpan();

        if (BitConverter.IsLittleEndian) {
            value = BinaryPrimitives.ReverseEndianness(value);
        }

        MemoryMarshal.Write(span, in value);
        _stream.Write(span.Slice(0, sizeof(int)));
        ArrayPool<byte>.Shared.Return(buffer);
    }
    public void WriteUInt(uint value)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(sizeof(uint));
        var span = buffer.AsSpan();

        if (BitConverter.IsLittleEndian) {
            value = BinaryPrimitives.ReverseEndianness(value);
        }

        MemoryMarshal.Write(span, in value);
        _stream.Write(span.Slice(0, sizeof(uint)));
        ArrayPool<byte>.Shared.Return(buffer);
    }

    public void WriteInteger(int value) => WriteInt(value);
    public void WriteUInteger(uint value) => WriteUInt(value);

    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);
    public void WriteBoolean(bool value) => WriteBool(value);

    public void WriteString(string value)
    {
        var buffer = !string.IsNullOrEmpty(value) ? Encoding.UTF8.GetBytes(value) : Array.Empty<byte>();

        if (buffer.Length <= ushort.MaxValue) {
            WriteShort((short)buffer.Length);

            if (buffer.Length > 0) {
                _stream.Write(buffer, 0, buffer.Length);
            }
        }
    }

    public void WriteDouble(double value)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(sizeof(double));
        var span = buffer.AsSpan();

        if (BitConverter.IsLittleEndian) {
            var dSpan = new Span<byte>(BitConverter.GetBytes(value));
            dSpan.Reverse();
            value = BitConverter.ToDouble(dSpan);
        }

        MemoryMarshal.Write(span, in value);
        _stream.Write(span.Slice(0, sizeof(double)));
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
