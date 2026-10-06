using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

// TODO @80O: Implement Recycler
public class RecyclerPrizesComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.RecyclerPrizesComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(0); // Count of items
}
