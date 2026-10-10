using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

public class GetMarketplaceConfigurationEvent(IMarketplaceFeePolicy fee) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        session.Send(new MarketplaceConfigurationComposer(fee));

        return Task.CompletedTask;
    }
}
