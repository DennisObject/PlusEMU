using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Avatar;

public class CarryObjectComposer : IServerPacket
{
    private readonly int _virtualId;
    private readonly int _itemId;

    public uint MessageId => ServerPacketHeader.CarryObjectComposer;

    public CarryObjectComposer(int virtualId, int itemId)
    {
        _virtualId = virtualId;
        _itemId = itemId;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_virtualId);
        packet.WriteInteger(_itemId);
    }
}
