using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

internal sealed class GetClubOffersEvent(ICatalogBrowsingService catalog) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        catalog.ShowOffer(session, packet.ReadInt());
        return Task.CompletedTask;
    }
}
