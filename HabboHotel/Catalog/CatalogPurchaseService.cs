using Plus.HabboHotel.Users.Inventory.Bots;
using Microsoft.Extensions.Logging;
using Plus.Communication.Packets;
using Plus.HabboHotel.Rooms.AI;
using System.Globalization;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Inventory.AvatarEffect;
using Plus.Communication.Packets.Outgoing.Inventory.Bots;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Inventory.Pets;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Notifications;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.Core;
using Plus.Core.Settings;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.Catalog.Utilities;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Users.Effects;
using Plus.HabboHotel.Habbicons;
using Plus.Communication.Packets.Outgoing.Habbicons;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.HabboHotel.Subscriptions;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Catalog;

public sealed record CatalogPurchaseRequest(int PageId, int OfferId, string ExtraData, int Amount);

[Singleton]
public interface ICatalogPurchaseService
{
    Task Purchase(GameClient session, CatalogPurchaseRequest request);
}

public sealed class CatalogPurchaseService : ICatalogPurchaseService
{
    private readonly ICatalogManager _catalogManager;
    private readonly IHabbiconService _habbicons;
    private readonly ISettingsManager _settingsManager;
    private readonly IAchievementManager _achievementManager;
    private readonly IItemDataManager _itemManager;
    private readonly IBadgeManager _badgeManager;
    private readonly IItemFactory _itemFactory;
    private readonly ICatalogBotPurchaseStore _botPurchases;
    private readonly IClubMembershipService _clubMemberships;
    private readonly IClubRewards _clubRewards;
    private readonly IRewardTrackManager _rewardTracks;
    private readonly IAvatarEffectStore _avatarEffects;
    private readonly TimeProvider _clock;
    private readonly ILogger<CatalogPurchaseService> _logger;
    // Window id the client's club purchase page requests offers for.
    private const int ClubWindow = 1;

    public CatalogPurchaseService(ICatalogManager catalogManager,
        ISettingsManager settingsManager,
        IAchievementManager achievementManager,
        IItemDataManager itemManager,
        IBadgeManager badgeManager,
        IItemFactory itemFactory,
        ICatalogBotPurchaseStore botPurchases,
        IHabbiconService habbicons,
        IClubMembershipService clubMemberships, IClubRewards clubRewards, IRewardTrackManager rewardTracks, IAvatarEffectStore avatarEffects,
        TimeProvider clock, ILogger<CatalogPurchaseService> logger)
    {
        _catalogManager = catalogManager;
        _habbicons = habbicons;
        _settingsManager = settingsManager;
        _achievementManager = achievementManager;
        _itemManager = itemManager;
        _badgeManager = badgeManager;
        _itemFactory = itemFactory;
        _botPurchases = botPurchases;
        _clubMemberships = clubMemberships;
        _clubRewards = clubRewards;
        _rewardTracks = rewardTracks;
        _avatarEffects = avatarEffects;
        _clock = clock;
        _logger = logger;
    }
    public async Task Purchase(GameClient session, CatalogPurchaseRequest request)
    {
        if (_settingsManager.TryGetValue("catalog.enabled") != "1") {
            session.SendNotification("The hotel managers have disabled the catalogue");

            return;
        }

        var utcNow = _clock.GetUtcNow();
        var pageId = request.PageId;
        var offerId = request.OfferId;
        var extraData = request.ExtraData;
        var amount = request.Amount;

        if (!_catalogManager.TryGetPage(pageId, out var page)) {
            return;
        }

        if (!page.CanOpen(session.GetHabbo())) {
            return;
        }

        if (page.Layout is "club_buy" or "vip_buy" or "loyalty_vip_buy") {
            if (amount != 1) {
                session.Send(new PurchaseErrorComposer(PurchaseError.Unavailable));

                return;
            }

            PurchaseClubOffer(session, offerId, utcNow);

            return;
        }

        if (!page.Offers.TryGetValue(offerId, out var offer) || !offer.Enabled || !offer.CanPurchase(session.GetHabbo())) {
            return;
        }

        var product = offer.Product;

        if (product.Type == CatalogProductType.Habbicon) {
            try {
                if (amount != 1 || offer.IsLimited) {
                    throw new HabbiconRejected(HabbiconActionError.InvalidRequest);
                }

                var change = _habbicons.BuyCatalog(session.GetHabbo(), product.HabbiconId, offer.CostCredits, offer.CostPoints, offer.PointsType);
                HabbiconMessages.Publish(session, change);
                session.Send(new PurchaseOKComposer());
            }
            catch (HabbiconRejected rejected) {
                session.Send(new PurchaseErrorComposer((PurchaseError)rejected.Code));
            }
            catch (MySqlConnector.MySqlException exception) {
                _logger.LogError(exception, "Failed to purchase habbicon {HabbiconId}", product.HabbiconId);
                session.Send(new PurchaseErrorComposer(PurchaseError.DeliveryFailed));
            }

            return;
        }

        // Bought products are delivered into the loaded inventory; without it nothing is charged.
        if (session.GetHabbo().Inventory is not { } inventory) {
            return;
        }

        if (amount < 1 || amount > 100 || !ItemUtility.CanSelectAmount(offer)) {
            amount = 1;
        }

        var amountPurchase = product.Amount > 1 ? product.Amount : amount;
        var totalCreditsCost = amount > 1 ? offer.CostCredits * amount - (int)Math.Floor((double)amount / 6) * offer.CostCredits : offer.CostCredits;
        var totalPointsCost = amount > 1 ? offer.CostPoints * amount - (int)Math.Floor((double)amount / 6) * offer.CostPoints : offer.CostPoints;

        if (session.GetHabbo().Credits < totalCreditsCost || session.GetHabbo().Currencies[offer.PointsType] < totalPointsCost) {
            return;
        }

        var limitedEditionSells = 0u;
        var limitedEditionStack = 0u;

        switch (product.Type) {
            case CatalogProductType.Furni when !offer.IsBundle:
                if (ReadFurniExtraData(session, product.Definition!, extraData, utcNow) is not { } furniExtraData) {
                    return;
                }

                extraData = furniExtraData;
                break;
            case CatalogProductType.Pet:
                if (!PetUtility.TryReadPurchase(extraData, out _, out _, out _)) {
                    return;
                }

                break;
            case CatalogProductType.Badge:
                if (inventory.Badges.HasBadge(product.BadgeCode)) {
                    session.Send(new PurchaseErrorComposer(PurchaseError.Rejected));

                    return;
                }

                extraData = "";
                break;
            default:
                extraData = "";
                break;
        }

        bool ChargePurchase(Func<System.Data.IDbConnection, System.Data.IDbTransaction, bool>? deliver = null)
        {
            var soldOut = false;

            if (!offer.CanPurchase(session.GetHabbo()) || !_clubRewards.Charge(session.GetHabbo(), totalCreditsCost, totalPointsCost, offer.PointsType, (connection, transaction) =>
            {
                if (!offer.CanPurchase(session.GetHabbo())) {
                    return false;
                }

                if (offer.IsLimited) {
                    if (CatalogLimitedStock.Reserve(connection, transaction, offer.Id) is not { } serial) {
                        soldOut = true;

                        return false;
                    }

                    limitedEditionSells = (uint)serial;
                    limitedEditionStack = offer.LimitedStack;
                }

                return deliver?.Invoke(connection, transaction) ?? true;
            }, ClubRewards.EligibleCatalogPurchase(offer.LocalizationKey))) {
                if (soldOut) {
                    offer.LimitedSells = offer.LimitedStack;
                    session.SendNotification("This item has sold out! You have not been charged.");
                    session.Send(new CatalogUpdatedComposer());
                    session.Send(new PurchaseOKComposer());
                }

                return false;
            }

            if (offer.IsLimited) {
                offer.LimitedSells = Math.Max(offer.LimitedSells, limitedEditionSells);
            }

            if (totalCreditsCost > 0) {
                session.Send(new CreditBalanceComposer(session.GetHabbo().Credits));
            }

            if (totalPointsCost > 0) {
                session.Send(new HabboActivityPointNotificationComposer(session.GetHabbo().Currencies[offer.PointsType], -totalPointsCost, offer.PointsType));
            }

            return true;
        }

        switch (product.Type) {
            case CatalogProductType.Furni: {
                    if (!ChargePurchase()) {
                        return;
                    }

                    var generatedGenericItems = offer.IsBundle
                        ? offer.Products.Where(bundled => bundled.Type == CatalogProductType.Furni)
                            .SelectMany(bundled => CreateFurni(session, bundled.Definition!, "", bundled.Amount, 0, 0)).ToList()
                        : CreateFurni(session, product.Definition!, extraData, amountPurchase, limitedEditionSells, limitedEditionStack);

                    foreach (var purchasedItem in generatedGenericItems) {
                        if (inventory.Furniture.AddItem(purchasedItem.ToInventoryItem())) {
                            session.Send(new FurniListNotificationComposer(purchasedItem.Id, 1));
                        }
                    }

                    if (generatedGenericItems.Count > 0) {
                        _rewardTracks.Progress(session, RewardTrackActions.BuyFromCatalogue);
                    }

                    break;
                }
            case CatalogProductType.Effect: {
                    var habbo = session.GetHabbo();

                    // Effects are loaded at login; without them the bought effect could not be delivered, so nothing is charged.
                    if (habbo.Effects is not { } effects || !ChargePurchase()) {
                        return;
                    }

                    var effect = effects.GetEffectNullable(product.EffectId);

                    if (effect != null) {
                        effect.AddToQuantity();
                    }
                    else {
                        effect = _avatarEffects.Create(habbo.Id, product.EffectId, 3600);
                        effects.TryAdd(effect);
                    }

                    session.Send(new AvatarEffectAddedComposer(product.EffectId, 3600));
                    break;
                }
            case CatalogProductType.Bot: {
                    if (!_catalogManager.TryGetBot((uint)product.BotPresetId, out var botPreset)) {
                        session.SendNotification("Oops! There was an error whilst purchasing this bot. It seems that there is no bot data for the bot!");

                        return;
                    }

                    Bot? bot = null;

                    if (!ChargePurchase((connection, transaction) =>
                        {
                            bot = _botPurchases.Create(connection, transaction, botPreset, session.GetHabbo().Id);

                            return true;
                        })) {
                        return;
                    }

                    inventory.Bots.AddBot(bot!);
                    session.Send(new BotInventoryComposer(BotInventorySnapshot.Capture(inventory.Bots.Bots.Values)));
                    session.Send(new FurniListNotificationComposer((uint)bot!.Id, 5));
                    break;
                }
            case CatalogProductType.Badge: {
                    if (!ChargePurchase()) {
                        return;
                    }

                    await _badgeManager.GiveBadge(session.GetHabbo(), product.BadgeCode);
                    session.Send(new FurniListNotificationComposer(0, 4));
                    break;
                }
            case CatalogProductType.Pet: {
                    if (!PetUtility.TryReadPurchase(extraData, out var petName, out var race, out var color)) {
                        return;
                    }

                    Plus.HabboHotel.Rooms.AI.Pet? pet = null;

                    if (!ChargePurchase((connection, transaction) => (pet = PetUtility.CreatePet(connection, transaction,
                            utcNow, session.GetHabbo().Username, session.GetHabbo().Id, petName, product.PetType, race, color)) != null)) {
                        session.SendNotification("Oops! There was an error whilst purchasing this pet.");

                        return;
                    }

                    inventory.Pets.AddPet(pet!);
                    pet!.RoomId = 0;
                    pet.PlacedInRoom = false;
                    session.Send(new FurniListNotificationComposer((uint)pet.PetId, 3));
                    session.Send(new PetInventoryComposer(PetAppearanceSnapshots.Inventory(inventory.Pets.Pets.Values.ToList())));

                    if (_itemManager.Items.TryGetValue(320, out var petFood)) {
                        var food = _itemFactory.CreateSingleItemNullable(petFood, session.GetHabbo(), "", "")?.ToInventoryItem();

                        if (food != null) {
                            inventory.Furniture.AddItem(food);
                            session.Send(new FurniListNotificationComposer(food.Id, 1));
                        }
                    }

                    _achievementManager.ProgressAchievement(session, "ACH_PetLover", 1);
                    break;
                }
        }

        foreach (var badgeCode in offer.AttachedBadges) {
            if (_badgeManager.Badges.TryGetValue(badgeCode, out var badge) &&
                (string.IsNullOrEmpty(badge.RequiredRight) || session.GetHabbo().Access.Can(badge.RequiredRight))) {
                await _badgeManager.GiveBadge(session.GetHabbo(), badge.Code);
            }
        }

        session.Send(new PurchaseOKComposer(CatalogPurchaseConfirmation.Capture(offer)));
        session.Send(new FurniListUpdateComposer());
    }

    // The extra data a bought piece of furniture starts with; null when the purchase must be refused.
    private string? ReadFurniExtraData(GameClient session, ItemDefinition definition, string extraData, DateTimeOffset utcNow)
    {
        switch (definition.InteractionType) {
            case InteractionType.GuildItem:
            case InteractionType.GuildGate:
                return FurniExtraData.RejectsClientImage(new[] { extraData }) ? "" : extraData;
            case InteractionType.Floor:
            case InteractionType.Wallpaper:
            case InteractionType.Landscape:
                double number = 0;

                try {
                    number = string.IsNullOrEmpty(extraData) ? 0 : double.Parse(extraData, CultureInfo.InvariantCulture);
                }
                catch (Exception e) {
                    _logger.LogWarning(e, "Invalid catalog floor extra data {ExtraData}", extraData);
                }

                return number.ToString(CultureInfo.InvariantCulture); // maintain extra data // todo: validate
            case InteractionType.Postit:
                return "FFFF33";
            case InteractionType.Moodlight:
                return "1,1,1,#000000,255";
            case InteractionType.Trophy:
                return $"{session.GetHabbo().Username}{Convert.ToChar(9)}{utcNow.Day}-{utcNow.Month}-{utcNow.Year}{Convert.ToChar(9)}{extraData}";
            case InteractionType.Mannequin:
                return $"m{Convert.ToChar(5)}.ch-210-1321.lg-285-92{Convert.ToChar(5)}Default Mannequin";
            case InteractionType.BadgeDisplay:
                if (session.GetHabbo().Inventory?.Badges.HasBadge(extraData) != true) {
                    session.Send(new BroadcastMessageAlertComposer("Oops, it appears that you do not own this badge."));

                    return null;
                }

                return $"{extraData}{Convert.ToChar(9)}{session.GetHabbo().Username}{Convert.ToChar(9)}{utcNow.Day}-{utcNow.Month}-{utcNow.Year}";
            default:
                return "";
        }
    }

    private List<Item> CreateFurni(GameClient session, ItemDefinition definition, string extraData, int amount, uint limitedEditionSells, uint limitedEditionStack)
    {
        var items = new List<Item>();

        switch (definition.InteractionType) {
            case InteractionType.GuildGate:
            case InteractionType.GuildItem:
            case InteractionType.GuildForum:
                var groupId = int.TryParse(extraData, NumberStyles.Integer, CultureInfo.InvariantCulture, out var group) ? group : 0;

                if (amount > 1) {
                    items.AddRange(_itemFactory.CreateMultipleItems(definition, session.GetHabbo(), extraData, amount, groupId) ?? []);
                }
                else if (_itemFactory.CreateSingleItemNullable(definition, session.GetHabbo(), extraData, extraData, groupId) is { } guildItem) {
                    items.Add(guildItem);
                }

                break;
            case InteractionType.Arrow:
            case InteractionType.Teleport:
                for (var i = 0; i < amount; i++) {
                    items.AddRange(_itemFactory.CreateTeleporterItems(definition, session.GetHabbo()) ?? []);
                }

                break;
            default:
                if (amount > 1) {
                    items.AddRange(_itemFactory.CreateMultipleItems(definition, session.GetHabbo(), extraData, amount) ?? []);
                }
                else if (_itemFactory.CreateSingleItemNullable(definition, session.GetHabbo(), extraData, extraData, 0, limitedEditionSells, limitedEditionStack) is { } item) {
                    items.Add(item);
                }

                break;
        }

        foreach (var item in items) {
            if (definition.InteractionType == InteractionType.Moodlight) {
                _itemFactory.CreateMoodlightData(item);
            }
            else if (definition.InteractionType == InteractionType.Toner) {
                _itemFactory.CreateTonerData(item);
            }
        }

        return items;
    }

    private void PurchaseClubOffer(GameClient session, int offerId, DateTimeOffset utcNow)
    {
        var habbo = session.GetHabbo();
        DateTimeOffset? expiry = null;

        if (_catalogManager.TryGetClubOffer(offerId, out var offer)) {
            expiry = _clubMemberships.Purchase(habbo, offer);
        }

        // A purchase only happened for a found offer, so either check alone means nothing was bought.
        if (expiry == null || offer == null) {
            session.Send(new PurchaseErrorComposer(PurchaseError.Unavailable));

            return;
        }

        if (offer.Credits > 0) {
            session.Send(new CreditBalanceComposer(habbo.Credits));
        }

        if (offer.Points > 0) {
            session.Send(new HabboActivityPointNotificationComposer(habbo.Currencies[offer.PointsType], -offer.Points, offer.PointsType));
        }

        session.Send(new PurchaseOKComposer());
        var membershipEnd = expiry.Value;
        // The client caches offers; resend them so the next confirmation shows the new end date.
        session.Send(new HabboClubOffersComposer(ClubOfferSnapshotFactory.Capture(_catalogManager.ClubOffers, ClubWindow, membershipEnd, utcNow)));
        session.Send(new ScrSendUserInfoComposer(ClubStatusSnapshot.Capture(habbo.Access, ClubStatusSnapshot.PurchaseResponse)));
    }
}
