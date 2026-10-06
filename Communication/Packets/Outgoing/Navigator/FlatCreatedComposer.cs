using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Navigator;

public class FlatCreatedComposer : IServerPacket
{
    private readonly uint _roomId;
    private readonly string _roomName;

    public uint MessageId => ServerPacketHeader.FlatCreatedComposer;

    public FlatCreatedComposer(uint roomId, string roomName)
    {
        _roomId = roomId;
        _roomName = roomName;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(_roomId);
        packet.WriteString(_roomName);
    }
}
