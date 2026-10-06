using Plus.Communication.Packets.Outgoing.Marketplace;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.Communication.Packets.Incoming.Marketplace;

internal class GetMarketplaceCanMakeOfferEvent(ITradingLockService tradingLocks) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var errorCode = tradingLocks.IsLocked(session.GetHabbo()) ? MarketplaceOfferEligibility.TradingLocked : MarketplaceOfferEligibility.Allowed;
        session.Send(new MarketplaceCanMakeOfferResultComposer(errorCode));

        return Task.CompletedTask;
    }
}
