using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Inventory.Furni;

public class FurniListNotificationComposer : IServerPacket
{
    private readonly uint _id;
    private readonly int _type;
    public uint MessageId => ServerPacketHeader.FurniListNotificationComposer;

    public FurniListNotificationComposer(uint id, int type)
    {
        _id = id;
        _type = type;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(1);
        packet.WriteInteger(_type);
        packet.WriteInteger(1);
        packet.WriteUInteger(_id);
    }
}
