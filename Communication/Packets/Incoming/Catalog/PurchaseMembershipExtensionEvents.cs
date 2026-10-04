using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Subscriptions;

namespace Plus.Communication.Packets.Incoming.Catalog;

internal abstract class PurchaseMembershipExtensionEvent(ICatalogManager catalog, IClubMembershipService memberships) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var id = packet.ReadInt();
        var habbo = session.GetHabbo();
        if (!catalog.Pages.Any(page => page.CanOpen(habbo) && page.Layout is "club_buy" or "vip_buy" or "loyalty_vip_buy") ||
            !catalog.TryGetClubOffer(id, out var offer) || memberships.Purchase(habbo, offer) == null)
        { session.Send(new PurchaseErrorComposer(PurchaseError.Unavailable)); return Task.CompletedTask; }
        session.Send(new CreditBalanceComposer(habbo.Credits));
        if (offer.Points > 0) session.Send(new HabboActivityPointNotificationComposer(offer.PointsType == 5 ? habbo.Diamonds : habbo.Duckets, -offer.Points, offer.PointsType));
        session.Send(new PurchaseOKComposer());
        session.Send(new ScrSendUserInfoComposer(habbo.Access, ScrSendUserInfoComposer.PurchaseResponse));
        return Task.CompletedTask;
    }
}
