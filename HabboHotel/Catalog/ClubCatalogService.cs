using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Notifications;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Subscriptions;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Catalog;

public sealed record ClubGiftReceivedSnapshot(
    string CatalogName,
    string ProductType,
    int SpriteId,
    int Amount);

[Singleton]
public interface IClubCatalogService
{
    Task ShowStatus(GameClient session, string type);
    Task ShowKickback(GameClient session);
    Task ShowGifts(GameClient session);
    Task ClaimGift(GameClient session, string productCode);
    Task PurchaseMembership(GameClient session, int offerId);
}

public sealed class ClubCatalogService(
    IClubRewards rewards,
    ICatalogSnapshotService snapshots,
    ICatalogManager catalog,
    IClubMembershipService memberships) : IClubCatalogService
{
    public Task ShowStatus(GameClient session, string type)
    {
        if (type == "habbo_club")
            session.Send(new ScrSendUserInfoComposer(ClubStatusSnapshot.Capture(session.GetHabbo().Access)));
        return Task.CompletedTask;
    }

    public Task ShowKickback(GameClient session)
    {
        var info = rewards.Kickback(session.GetHabbo());
        session.Send(new KickbackInfoComposer(info));
        return Task.CompletedTask;
    }

    public Task ShowGifts(GameClient session)
    {
        var gifts = rewards.Gifts(session.GetHabbo());
        session.Send(new ClubGiftsComposer(snapshots.CaptureClubGifts(gifts)));
        return Task.CompletedTask;
    }

    public Task ClaimGift(GameClient session, string productCode)
    {
        var habbo = session.GetHabbo();
        var claim = rewards.Claim(habbo, productCode);
        if (claim == null)
        {
            session.Send(new PurchaseErrorComposer(PurchaseError.Unavailable));
            return Task.CompletedTask;
        }

        var definition = claim.Gift.Item.Definition;
        var received = new ClubGiftReceivedSnapshot(
            claim.Gift.Item.CatalogName,
            definition.ProductType,
            definition.SpriteId,
            claim.Gift.Item.Amount);
        var itemIds = claim.Items.Select(item => item.Id).ToArray();
        var gifts = rewards.Gifts(habbo);
        var giftList = snapshots.CaptureClubGifts(gifts);

        session.Send(new ClubGiftReceivedComposer(received));
        foreach (var itemId in itemIds)
            session.Send(new FurniListNotificationComposer(itemId, 1));
        session.Send(new FurniListUpdateComposer());
        session.Send(new ClubGiftsComposer(giftList));
        session.Send(new PickMonthlyClubGiftComposer(gifts.Available));
        return Task.CompletedTask;
    }

    public Task PurchaseMembership(GameClient session, int offerId)
    {
        var habbo = session.GetHabbo();
        if (!catalog.Pages.Any(page => page.CanOpen(habbo) &&
                page.Layout is "club_buy" or "vip_buy" or "loyalty_vip_buy") ||
            !catalog.TryGetClubOffer(offerId, out var offer) ||
            memberships.Purchase(habbo, offer) == null)
        {
            session.Send(new PurchaseErrorComposer(PurchaseError.Unavailable));
            return Task.CompletedTask;
        }

        session.Send(new CreditBalanceComposer(habbo.Credits));
        if (offer.Points > 0)
        {
            var balance = offer.PointsType == 5 ? habbo.Diamonds : habbo.Duckets;
            session.Send(new HabboActivityPointNotificationComposer(balance, -offer.Points, offer.PointsType));
        }
        session.Send(new PurchaseOKComposer());
        session.Send(new ScrSendUserInfoComposer(ClubStatusSnapshot.Capture(habbo.Access, ClubStatusSnapshot.PurchaseResponse)));
        return Task.CompletedTask;
    }
}
