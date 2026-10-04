namespace Plus.HabboHotel.GameClients;

public interface IOutgoingPacket
{
    int MessageId { get; set; }
    ReadOnlyMemory<byte> Buffer { get; }
    void WriteByte(byte value);
    void WriteShort(short value);
    void WriteInt(int value);
    void WriteInteger(int value);
    void WriteUInt(uint value);
    void WriteUInteger(uint value);
    void WriteBool(bool value);
    void WriteBoolean(bool value);
    void WriteString(string value);
    void WriteDouble(double value);
}
