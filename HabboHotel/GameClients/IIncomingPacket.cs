using Microsoft.IO;

namespace Plus.HabboHotel.GameClients;

public interface IIncomingPacket
{
    uint MessageId
    {
        get; set;
    }
    RecyclableMemoryStream Stream
    {
        get;
    }
    Memory<byte> Buffer
    {
        get;
    }
    byte ReadByte();
    short ReadShort();
    int ReadInt();
    uint ReadUInt();
    bool ReadBool();
    string ReadString();
    bool HasDataRemaining();
    byte[] ReadFixedValue();
    void ReadBytes(Span<byte> destination);
}
