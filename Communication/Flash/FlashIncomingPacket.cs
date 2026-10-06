using System.Buffers.Binary;
using Microsoft.IO;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Flash;

public class FlashIncomingPacket : IIncomingPacket
{
    public FlashIncomingPacket() : this(PlusMemoryStream.GetStream()) { }
    public FlashIncomingPacket(RecyclableMemoryStream stream) => Stream = stream;

    public RecyclableMemoryStream Stream { get; }
    public Memory<byte> Buffer
    {
        get => Stream.GetBuffer().AsMemory((int)Stream.Position, (int)(Stream.Length - Stream.Position));
        set
        {
            Stream.SetLength(0);
            Stream.Write(value.Span);
            Stream.Position = 0;
        }
    }
    public uint MessageId { get; set; }

    public byte ReadByte()
    {
        var span = Buffer.Span;
        var result = span[0];
        Stream.Position += sizeof(byte);

        return result;
    }

    public short ReadShort()
    {
        var result = BinaryPrimitives.ReadInt16BigEndian(Buffer.Span);
        Stream.Position += sizeof(short);

        return result;
    }
    public ushort ReadUShort()
    {
        var result = BinaryPrimitives.ReadUInt16BigEndian(Buffer.Span);
        Stream.Position += sizeof(ushort);

        return result;
    }

    public int ReadInt()
    {
        var result = BinaryPrimitives.ReadInt32BigEndian(Buffer.Span);
        Stream.Position += sizeof(int);

        return result;
    }
    public uint ReadUInt()
    {
        var result = BinaryPrimitives.ReadUInt32BigEndian(Buffer.Span);
        Stream.Position += sizeof(uint);

        return result;
    }

    public bool ReadBool() => ReadByte() == 1;

    public string ReadString()
    {
        var length = ReadUShort();
        var value = System.Text.Encoding.UTF8.GetString(Buffer.Span.Slice(0, length));
        Stream.Position += length;

        return value;
    }

    public bool HasDataRemaining() => !Buffer.IsEmpty;
    public byte[] ReadFixedValue()
    {
        var length = ReadUShort();
        var span = Buffer.Slice(0, length);
        Stream.Position += length;

        return span.ToArray();
    }

    public void ReadBytes(Span<byte> destination)
    {
        if (Stream.Read(destination) != destination.Length) {
            throw new EndOfStreamException();
        }
    }
}
