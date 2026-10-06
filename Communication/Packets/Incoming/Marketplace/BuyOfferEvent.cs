using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Marketplace;

internal class BuyOfferEvent(IMarketplacePurchaseService purchases) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        purchases.Buy(session, packet.ReadInt());
        return Task.CompletedTask;
    }
}
