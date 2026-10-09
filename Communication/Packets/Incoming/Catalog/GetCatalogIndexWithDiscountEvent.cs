using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

public sealed class GetCatalogIndexWithDiscountEvent(ICatalogBrowsingService catalog) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        catalog.ShowIndex(session, packet.ReadString());

        return Task.CompletedTask;
    }
}
