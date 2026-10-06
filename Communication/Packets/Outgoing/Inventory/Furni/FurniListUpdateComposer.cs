using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Inventory.Furni;

public class FurniListUpdateComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.FurniListUpdateComposer;

    public void Compose(IOutgoingPacket packet)
    {
        // Empty Body
    }
}
