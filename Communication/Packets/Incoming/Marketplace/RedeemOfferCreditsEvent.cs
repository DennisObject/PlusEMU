using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Marketplace;

internal class RedeemOfferCreditsEvent(IMarketplaceRedemptionService redemption) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        redemption.Redeem(session);

        return Task.CompletedTask;
    }
}
