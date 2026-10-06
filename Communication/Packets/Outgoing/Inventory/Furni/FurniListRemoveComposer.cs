using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Inventory.Furni;

public class FurniListRemoveComposer : IServerPacket
{
    private readonly uint _id;
    public uint MessageId => ServerPacketHeader.FurniListRemoveComposer;

    public FurniListRemoveComposer(uint id)
    {
        _id = id;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(_id);
    }
}
