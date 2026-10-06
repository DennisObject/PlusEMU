using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class CatalogUpdatedComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.CatalogUpdatedComposer;

    public void Compose(IOutgoingPacket packet) => packet.WriteBoolean(false);
}
