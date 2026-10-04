using Plus.Communication.Packets.Outgoing.Marketplace;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Marketplace;

internal class GetMarketplaceCanMakeOfferEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var errorCode = session.GetHabbo().TradingLockExpiry > 0 ? MarketplaceOfferEligibility.TradingLocked : MarketplaceOfferEligibility.Allowed;
        session.Send(new MarketplaceCanMakeOfferResultComposer(errorCode));
        return Task.CompletedTask;
    }
}