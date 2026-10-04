using System.Runtime.InteropServices;
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
        var result = MemoryMarshal.Read<byte>(span);
        Stream.Position += sizeof(byte);
        return result;
    }

    public short ReadShort()
    {
        var span = Buffer.Span.Slice(0, sizeof(short));
        span.Reverse();
        var result = MemoryMarshal.Read<short>(span);
        Stream.Position += sizeof(short);
        return result;
    }
    public ushort ReadUShort()
    {
        var span = Buffer.Span.Slice(0, sizeof(ushort));
        span.Reverse();
        var result = MemoryMarshal.Read<ushort>(span);
        Stream.Position += sizeof(ushort);
        return result;
    }

    public int ReadInt()
    {
        var span = Buffer.Span.Slice(0, sizeof(int));
        span.Reverse();
        var result = MemoryMarshal.Read<int>(span);
        Stream.Position += sizeof(int);
        return result;
    }
    public uint ReadUInt()
    {
        var span = Buffer.Span.Slice(0, 4);
        span.Reverse();
        var result = MemoryMarshal.Read<uint>(span);
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
        if (Stream.Read(destination) != destination.Length) throw new EndOfStreamException();
    }
}
