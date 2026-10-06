using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Navigator;

public class RoomInfoUpdatedComposer : IServerPacket
{
    private readonly uint _roomId;

    public uint MessageId => ServerPacketHeader.RoomInfoUpdatedComposer;

    public RoomInfoUpdatedComposer(uint roomId)
    {
        _roomId = roomId;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(_roomId);
    }
}
