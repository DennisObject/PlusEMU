using System.Globalization;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Notifications;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.Core.Settings;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Catalog.Utilities;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.Utilities;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Catalog;

public sealed record CatalogGiftPurchaseRequest(
    int PageId,
    int OfferId,
    string ExtraData,
    string RecipientName,
    string Message,
    int SpriteId,
    int BoxId,
    int RibbonId,
    bool ShowUsername);

[Singleton]
public interface ICatalogGiftPurchaseService
{
    Task Purchase(GameClient session, CatalogGiftPurchaseRequest request);
}

public sealed class CatalogGiftPurchaseService : ICatalogGiftPurchaseService
{
    private readonly ICatalogManager _catalogManager;
    private readonly ISettingsManager _settingsManager;
    private readonly IItemDataManager _itemManager;
    private readonly IAchievementManager _achievementManager;
    private readonly IGameClientManager _gameClientManager;
    private readonly IQuestManager _questManager;
    private readonly IClubMembershipService _clubMemberships;
    private readonly IClubRewards _clubRewards;
    private readonly ICatalogGiftStore _giftStore;
    private readonly TimeProvider _clock;

    public CatalogGiftPurchaseService(ICatalogManager catalogManager,
        ISettingsManager settingsManager,
        IItemDataManager itemManager,
        IAchievementManager achievementManager,
        IGameClientManager gameClientManager,
        IQuestManager questManager,
        IClubMembershipService clubMemberships,
        IClubRewards clubRewards,
        ICatalogGiftStore giftStore,
        TimeProvider clock)
    {
        _catalogManager = catalogManager;
        _settingsManager = settingsManager;
        _itemManager = itemManager;
        _achievementManager = achievementManager;
        _gameClientManager = gameClientManager;
        _questManager = questManager;
        _clubMemberships = clubMemberships;
        _clubRewards = clubRewards;
        _giftStore = giftStore;
        _clock = clock;
    }

    public Task Purchase(GameClient session, CatalogGiftPurchaseRequest request)
    {
        var utcNow = _clock.GetUtcNow();
        var pageId = request.PageId;
        var itemId = request.OfferId;
        var data = request.ExtraData;
        var giftUser = StringCharFilter.Escape(request.RecipientName);
        var giftMessage = StringCharFilter.Escape(request.Message.Replace(Convert.ToChar(5), ' '));
        var spriteId = request.SpriteId;
        var boxId = request.BoxId;
        var ribbonId = request.RibbonId;

        if (_settingsManager.TryGetValue("room.item.gifts.enabled") != "1") {
            session.SendNotification("The hotel managers have disabled gifting");

            return Task.CompletedTask;
        }

        if (!_catalogManager.TryGetPage(pageId, out var page)) {
            return Task.CompletedTask;
        }

        if (!page.CanOpen(session.GetHabbo())) {
            return Task.CompletedTask;
        }

        if (page.Layout is "club_buy" or "vip_buy" or "loyalty_vip_buy") {
            var receiver = _gameClientManager.GetClientByUsername(giftUser)?.GetHabbo();

            if (receiver == null || receiver.Id == session.GetHabbo().Id || !receiver.AllowGifts ||
                !_catalogManager.TryGetClubOffer(itemId, out var offer) || !offer.Giftable ||
                _clubMemberships.Purchase(session.GetHabbo(), offer, receiver.Id) == null) {
                session.Send(new PurchaseErrorComposer(PurchaseError.Unavailable));

                return Task.CompletedTask;
            }

            session.Send(new CreditBalanceComposer(session.GetHabbo().Credits));

            if (offer.Points > 0) {
                session.Send(new HabboActivityPointNotificationComposer(session.GetHabbo().Currencies[offer.PointsType], -offer.Points, offer.PointsType));
            }

            session.Send(new PurchaseOKComposer());

            return Task.CompletedTask;
        }

        if (!page.Offers.TryGetValue(itemId, out var item) || !item.Enabled) {
            return Task.CompletedTask;
        }

        if (!item.CanPurchase(session.GetHabbo())) {
            return Task.CompletedTask;
        }

        // Only a single piece of furniture can be wrapped, so the offer's definition is known from here on.
        if (!ItemUtility.CanGiftItem(item)) {
            return Task.CompletedTask;
        }

        var definition = item.Definition!;

        if (!_itemManager.Gifts.TryGetValue(spriteId, out var presentId) || !_itemManager.Items.TryGetValue(presentId, out var presentData) || presentData.InteractionType != InteractionType.Gift) {
            return Task.CompletedTask;
        }

        if (session.GetHabbo().Credits < item.CostCredits) {
            session.Send(new PresentDeliverErrorComposer(true, false));

            return Task.CompletedTask;
        }

        if (session.GetHabbo().Currencies[item.PointsType] < item.CostPoints) {
            session.Send(new PresentDeliverErrorComposer(false, true));

            return Task.CompletedTask;
        }

        var habbo = _gameClientManager.GetClientByUsername(giftUser)?.GetHabbo();

        if (habbo == null) {
            session.Send(new GiftWrappingErrorComposer());

            return Task.CompletedTask;
        }

        if (habbo.Id == session.GetHabbo().Id) {
            return Task.CompletedTask;
        }

        if (!habbo.AllowGifts) {
            session.SendNotification("Oops, this user doesn't allow gifts to be sent to them!");

            return Task.CompletedTask;
        }

        var sender = session.GetHabbo();

        lock (sender.GiftPurchaseSync) {
            if (sender.LastGiftPurchasedAt is { } lastPurchase && utcNow - lastPurchase <= TimeSpan.FromSeconds(15)) {
                session.SendNotification("You're purchasing gifts too fast! Please wait 15 seconds!");
                sender.GiftPurchasingWarnings += 1;

                if (sender.GiftPurchasingWarnings >= 25) {
                    sender.SessionGiftBlocked = true;
                }

                return Task.CompletedTask;
            }

            if (sender.SessionGiftBlocked) {
                return Task.CompletedTask;
            }

            var extra_data = GiftWrap.PresentData(giftUser, giftMessage, session.GetHabbo().Id, definition.Id, spriteId, boxId, ribbonId);
            string? itemExtraData = null;
            var progressPetAchievement = false;

            switch (definition.InteractionType) {
                case InteractionType.None:
                    itemExtraData = "";
                    break;
                case InteractionType.Pet:
                    if (!GiftWrap.PetDataAccepted(data)) {
                        return Task.CompletedTask;
                    }

                    progressPetAchievement = true;
                    break;
                case InteractionType.Floor:
                case InteractionType.Wallpaper:
                case InteractionType.Landscape:
                    double number = 0;

                    try {
                        number = string.IsNullOrEmpty(data) ? 0 : double.Parse(data, CultureInfo.InvariantCulture);
                    }
                    catch {
                        //ignored
                    }

                    itemExtraData = number.ToString(CultureInfo.InvariantCulture);
                    break; // maintain extra data // todo: validate
                case InteractionType.Postit:
                    itemExtraData = "FFFF33";
                    break;
                case InteractionType.Moodlight:
                    itemExtraData = "1,1,1,#000000,255";
                    break;
                case InteractionType.Trophy:
                    itemExtraData = $"{sender.Username}{Convert.ToChar(9)}{utcNow.Day}-{utcNow.Month}-{utcNow.Year}{Convert.ToChar(9)}{data}";
                    break;
                case InteractionType.Mannequin:
                    itemExtraData = $"m{Convert.ToChar(5)}.ch-210-1321.lg-285-92{Convert.ToChar(5)}Default Mannequin";
                    break;
                case InteractionType.BadgeDisplay:
                    if (session.GetHabbo().Inventory?.Badges.HasBadge(data) != true) {
                        session.Send(new BroadcastMessageAlertComposer("Oops, it appears that you do not own this badge."));

                        return Task.CompletedTask;
                    }

                    itemExtraData = $"{data}{Convert.ToChar(9)}{sender.Username}{Convert.ToChar(9)}{utcNow.Day}-{utcNow.Month}-{utcNow.Year}";
                    break;
                default:
                    itemExtraData = definition.InteractionType is InteractionType.CameraPicture or InteractionType.Background
                        || FurniExtraData.RejectsClientImage(new[] { data })
                        ? ""
                        : data;
                    break;
            }

            Plus.HabboHotel.Users.Inventory.Furniture.InventoryItem? giveItem = null;

            if (!_clubRewards.Charge(session.GetHabbo(), item.CostCredits, item.CostPoints, item.PointsType, (connection, transaction) =>
            {
                if (!item.CanPurchase(session.GetHabbo())) {
                    return false;
                }

                giveItem = _giftStore.Create(connection, transaction, habbo.Id, presentData, definition,
                    extra_data, itemExtraData ?? "");

                return true;
            }, ClubRewards.EligibleCatalogPurchase(item.LocalizationKey))) {
                session.Send(new PurchaseErrorComposer(PurchaseError.Unavailable));

                return Task.CompletedTask;
            }

            if (giveItem != null) {
                if (progressPetAchievement) {
                    _achievementManager.ProgressAchievement(session, "ACH_PetLover", 1);
                }

                var receiver = _gameClientManager.GetClientByUserId(habbo.Id);

                if (receiver != null) {
                    // The gift is already stored; an inventory that is not loaded picks it up from the database later.
                    receiver.GetHabbo().Inventory?.Furniture.AddItem(giveItem);
                    receiver.Send(new FurniListNotificationComposer(giveItem.Id, 1));
                    receiver.Send(new PurchaseOKComposer());
                    receiver.Send(new FurniListAddComposer(InventoryItemSnapshot.Capture(giveItem)));
                    receiver.Send(new FurniListUpdateComposer());
                }

                if (habbo.Id != session.GetHabbo().Id) {
                    _achievementManager.ProgressAchievement(session, "ACH_GiftGiver", 1);

                    if (receiver != null) {
                        _achievementManager.ProgressAchievement(receiver, "ACH_GiftReceiver", 1);
                    }

                    _questManager.ProgressUserQuest(session, QuestType.GiftOthers);
                }
            }

            session.Send(new PurchaseOKComposer(CatalogPurchaseConfirmation.Capture(item, new CatalogProduct { Type = CatalogProductType.Furni, Definition = presentData })));

            if (item.CostCredits > 0) {
                session.Send(new CreditBalanceComposer(session.GetHabbo().Credits));
            }

            if (item.CostPoints > 0) {
                session.Send(new HabboActivityPointNotificationComposer(session.GetHabbo().Currencies[item.PointsType], -item.CostPoints, item.PointsType));
            }

            sender.LastGiftPurchasedAt = utcNow;

            return Task.CompletedTask;
        }
    }
}
