using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Subscriptions;

namespace Plus.Communication.Packets.Incoming.Inventory.Purse;

// The client's GetClubOffers request: answers with the purchasable Habbo Club offers.
internal class GetHabboClubWindowEvent : IPacketEvent
{
    private readonly ICatalogManager _catalogManager;
    private readonly IClubMembershipService _clubMemberships;

    public GetHabboClubWindowEvent(ICatalogManager catalogManager, IClubMembershipService clubMemberships)
    {
        _catalogManager = catalogManager;
        _clubMemberships = clubMemberships;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var windowId = packet.ReadInt();
        var expiry = DateTimeOffset.FromUnixTimeSeconds(_clubMemberships.GetExpiry(session.GetHabbo().Id)).UtcDateTime;
        var membershipEnd = expiry > DateTime.UtcNow ? expiry : DateTime.UtcNow;
        session.Send(new HabboClubOffersComposer(_catalogManager.ClubOffers, windowId, membershipEnd));
        return Task.CompletedTask;
    }
}
