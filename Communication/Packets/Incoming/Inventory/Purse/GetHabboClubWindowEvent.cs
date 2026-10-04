using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Subscriptions;

namespace Plus.Communication.Packets.Incoming.Inventory.Purse;

// The client's GetClubOffers request: answers with the purchasable Habbo Club offers.
internal class GetHabboClubWindowEvent : IPacketEvent
{
    private readonly ICatalogManager _catalogManager;
    private readonly TimeProvider _clock;

    public GetHabboClubWindowEvent(ICatalogManager catalogManager, TimeProvider clock)
    {
        _catalogManager = catalogManager;
        _clock = clock;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var windowId = packet.ReadInt();
        var now = _clock.GetUtcNow().UtcDateTime;
        var expiry = DateTimeOffset.FromUnixTimeSeconds(session.GetHabbo().Access.Membership.ExpiresAt).UtcDateTime;
        var membershipEnd = expiry > now ? expiry : now;
        session.Send(new HabboClubOffersComposer(_catalogManager.ClubOffers, windowId, membershipEnd, now));
        return Task.CompletedTask;
    }
}
