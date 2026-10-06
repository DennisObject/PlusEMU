using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Engine;

public class RoomPropertyComposer : IServerPacket
{
    private readonly string _name;
    private readonly string _value;

    public uint MessageId => ServerPacketHeader.RoomPropertyComposer;

    public RoomPropertyComposer(string name, string value)
    {
        _name = name;
        _value = value;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(_name);
        packet.WriteString(_value);
    }
}
