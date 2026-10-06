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
        if (_settingsManager.TryGetValue("catalog.enabled") != "1")
        {
            session.SendNotification("The hotel managers have disabled the catalogue");

            return;
        }

        var utcNow = _clock.GetUtcNow();
        var pageId = request.PageId;
        var itemId = request.OfferId;
        var extraData = request.ExtraData;
        var amount = request.Amount;

        if (!_catalogManager.TryGetPage(pageId, out var page))
        {
            return;
        }

        if (!page.CanOpen(session.GetHabbo()))
        {
            return;
        }

        if (page.Layout is "club_buy" or "vip_buy" or "loyalty_vip_buy")
        {
            if (amount != 1)
            {
                session.Send(new PurchaseErrorComposer(PurchaseError.Unavailable));

                return;
            }

            PurchaseClubOffer(session, itemId, utcNow);

            return;
        }

        if (!page.Offers.TryGetValue(itemId, out var item))
        {
            return;
        }

        if (!item.CanPurchase(session.GetHabbo()))
        {
            return;
        }

        if (item.HabbiconId > 0)
        {
            try
            {
                if (amount != 1 || item.Amount != 1 || item.IsLimited)
                {
                    throw new HabbiconRejected(HabbiconActionError.InvalidRequest);
                }

                var change = _habbicons.BuyCatalog(session.GetHabbo(), item.HabbiconId, item.CostCredits, item.CostPixels, item.CostDiamonds);
                HabbiconMessages.Publish(session, change);
                session.Send(new PurchaseOKComposer());
            }
            catch (HabbiconRejected rejected)
            {
                session.Send(new PurchaseErrorComposer((PurchaseError)rejected.Code));
            }
            catch (MySqlConnector.MySqlException exception)
            {
                _logger.LogError(exception, "Failed to purchase habbicon {HabbiconId}", item.HabbiconId);
                session.Send(new PurchaseErrorComposer(PurchaseError.DeliveryFailed));
            }

            return;
        }

        if (amount < 1 || amount > 100 || !item.HaveOffer)
        {
            amount = 1;
        }

        var amountPurchase = item.Amount > 1 ? item.Amount : amount;
        var totalCreditsCost = amount > 1 ? item.CostCredits * amount - (int)Math.Floor((double)amount / 6) * item.CostCredits : item.CostCredits;
        var totalPixelCost = amount > 1 ? item.CostPixels * amount - (int)Math.Floor((double)amount / 6) * item.CostPixels : item.CostPixels;
        var totalDiamondCost = amount > 1 ? item.CostDiamonds * amount - (int)Math.Floor((double)amount / 6) * item.CostDiamonds : item.CostDiamonds;

        if (session.GetHabbo().Credits < totalCreditsCost || session.GetHabbo().Duckets < totalPixelCost || session.GetHabbo().Diamonds < totalDiamondCost)
        {
            return;
        }

        var limitedEditionSells = 0u;
        var limitedEditionStack = 0u;

        switch (item.Definition.InteractionType)
        {
            case InteractionType.None:
                extraData = "";
                break;
            case InteractionType.GuildItem:
            case InteractionType.GuildGate:
                if (FurniExtraData.RejectsClientImage(new[] { extraData }))
                {
                    extraData = "";
                }

                break;
            case InteractionType.Pet:
                if (!PetUtility.TryReadPurchase(extraData, out _, out _, out _))
                {
                    return;
                }

                break;
            case InteractionType.Floor:
            case InteractionType.Wallpaper:
            case InteractionType.Landscape:
                double number = 0;

                try
                {
                    number = string.IsNullOrEmpty(extraData) ? 0 : double.Parse(extraData, CultureInfo.InvariantCulture);
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Invalid catalog floor extra data {ExtraData}", extraData);
                }

                extraData = number.ToString(CultureInfo.InvariantCulture);
                break; // maintain extra data // todo: validate
            case InteractionType.Postit:
                extraData = "FFFF33";
                break;
            case InteractionType.Moodlight:
                extraData = "1,1,1,#000000,255";
                break;
            case InteractionType.Trophy:
                extraData = $"{session.GetHabbo().Username}{Convert.ToChar(9)}{utcNow.Day}-{utcNow.Month}-{utcNow.Year}{Convert.ToChar(9)}{extraData}";
                break;
            case InteractionType.Mannequin:
                extraData = $"m{Convert.ToChar(5)}.ch-210-1321.lg-285-92{Convert.ToChar(5)}Default Mannequin";
                break;
            case InteractionType.BadgeDisplay:
                if (!session.GetHabbo().Inventory.Badges.HasBadge(extraData))
                {
                    session.Send(new BroadcastMessageAlertComposer("Oops, it appears that you do not own this badge."));

                    return;
                }

                extraData = $"{extraData}{Convert.ToChar(9)}{session.GetHabbo().Username}{Convert.ToChar(9)}{utcNow.Day}-{utcNow.Month}-{utcNow.Year}";
                break;
            case InteractionType.Badge:
                {
                    if (session.GetHabbo().Inventory.Badges.HasBadge(item.Definition.ItemName))
                    {
                        session.Send(new PurchaseErrorComposer(PurchaseError.Rejected));

                        return;
                    }

                    break;
                }
            default:
                extraData = "";
                break;
        }

        bool ChargePurchase(Func<System.Data.IDbConnection, System.Data.IDbTransaction, bool>? deliver = null)
        {
            var soldOut = false;

            if (!item.CanPurchase(session.GetHabbo()) || !_clubRewards.Charge(session.GetHabbo(), totalCreditsCost, totalPixelCost, totalDiamondCost, (connection, transaction) =>
            {
                if (!item.CanPurchase(session.GetHabbo()))
                {
                    return false;
                }

                if (item.IsLimited)
                {
                    if (CatalogLimitedStock.Reserve(connection, transaction, item.Id) is not { } serial)
                    {
                        soldOut = true;

                        return false;
                    }

                    limitedEditionSells = (uint)serial;
                    limitedEditionStack = item.LimitedEditionStack;
                }

                return deliver?.Invoke(connection, transaction) ?? true;
            }, ClubRewards.EligibleCatalogPurchase(item.CatalogName)))
            {
                if (soldOut)
                {
                    session.SendNotification("This item has sold out! You have not been charged.");
                    session.Send(new CatalogUpdatedComposer());
                    session.Send(new PurchaseOKComposer());
                }

                return false;
            }

            if (item.IsLimited)
            {
                item.LimitedEditionSells = Math.Max(item.LimitedEditionSells, limitedEditionSells);
            }

            if (totalCreditsCost > 0)
            {
                session.Send(new CreditBalanceComposer(session.GetHabbo().Credits));
            }

            if (totalPixelCost > 0)
            {
                session.Send(new HabboActivityPointNotificationComposer(session.GetHabbo().Duckets, -totalPixelCost));
            }

            if (totalDiamondCost > 0)
            {
                session.Send(new HabboActivityPointNotificationComposer(session.GetHabbo().Diamonds, -totalDiamondCost, 5));
            }

            return true;
        }

        Bot? purchasedBot = null;

        if (item.Definition.ProductType == "r")
        {
            if (!_catalogManager.TryGetBot(item.Definition.Id, out var botPreset))
            {
                session.SendNotification("Oops! There was an error whilst purchasing this bot. It seems that there is no bot data for the bot!");

                return;
            }

            if (!ChargePurchase((connection, transaction) =>
                {
                    purchasedBot = _botPurchases.Create(connection, transaction, botPreset, session.GetHabbo().Id);

                    return true;
                }))
            {
                return;
            }
        }
        else if (item.Definition.ProductType != "p" && !ChargePurchase())
        {
            return;
        }

        switch (item.Definition.ProductType)
        {
            default:
                var generatedGenericItems = new List<Item>();
                Item newItem;

                switch (item.Definition.InteractionType)
                {
                    default:
                        if (amountPurchase > 1)
                        {
                            var items = _itemFactory.CreateMultipleItems(item.Definition, session.GetHabbo(), extraData, amountPurchase);

                            if (items != null)
                            {
                                generatedGenericItems.AddRange(items);
                            }
                        }
                        else
                        {
                            newItem = _itemFactory.CreateSingleItemNullable(item.Definition, session.GetHabbo(), extraData, extraData, 0, limitedEditionSells, limitedEditionStack);

                            if (newItem != null)
                            {
                                generatedGenericItems.Add(newItem);
                            }
                        }

                        break;
                    case InteractionType.GuildGate:
                    case InteractionType.GuildItem:
                    case InteractionType.GuildForum:
                        if (amountPurchase > 1)
                        {
                            var items = _itemFactory.CreateMultipleItems(item.Definition, session.GetHabbo(), extraData, amountPurchase, Convert.ToInt32(extraData));

                            if (items != null)
                            {
                                generatedGenericItems.AddRange(items);
                            }
                        }
                        else
                        {
                            newItem = _itemFactory.CreateSingleItemNullable(item.Definition, session.GetHabbo(), extraData, extraData, Convert.ToInt32(extraData));

                            if (newItem != null)
                            {
                                generatedGenericItems.Add(newItem);
                            }
                        }

                        break;
                    case InteractionType.Arrow:
                    case InteractionType.Teleport:
                        for (var i = 0; i < amountPurchase; i++)
                        {
                            var teleItems = _itemFactory.CreateTeleporterItems(item.Definition, session.GetHabbo());

                            if (teleItems != null)
                            {
                                generatedGenericItems.AddRange(teleItems);
                            }
                        }

                        break;
                    case InteractionType.Moodlight:
                        {
                            if (amountPurchase > 1)
                            {
                                var items = _itemFactory.CreateMultipleItems(item.Definition, session.GetHabbo(), extraData, amountPurchase);

                                if (items != null)
                                {
                                    generatedGenericItems.AddRange(items);

                                    foreach (var I in items)
                                    {
                                        _itemFactory.CreateMoodlightData(I);
                                    }
                                }
                            }
                            else
                            {
                                newItem = _itemFactory.CreateSingleItemNullable(item.Definition, session.GetHabbo(), extraData, extraData);

                                if (newItem != null)
                                {
                                    generatedGenericItems.Add(newItem);
                                    _itemFactory.CreateMoodlightData(newItem);
                                }
                            }
                        }
                        break;
                    case InteractionType.Toner:
                        {
                            if (amountPurchase > 1)
                            {
                                var items = _itemFactory.CreateMultipleItems(item.Definition, session.GetHabbo(), extraData, amountPurchase);

                                if (items != null)
                                {
                                    generatedGenericItems.AddRange(items);

                                    foreach (var I in items)
                                    {
                                        _itemFactory.CreateTonerData(I);
                                    }
                                }
                            }
                            else
                            {
                                newItem = _itemFactory.CreateSingleItemNullable(item.Definition, session.GetHabbo(), extraData, extraData);

                                if (newItem != null)
                                {
                                    generatedGenericItems.Add(newItem);
                                    _itemFactory.CreateTonerData(newItem);
                                }
                            }
                        }
                        break;
                    case InteractionType.Deal:
                        {
                            if (_catalogManager.TryGetDeal(item.Definition.BehaviourData, out var deal))
                            {
                                foreach (var catalogItem in deal.ItemDataList.ToList())
                                {
                                    var items = _itemFactory.CreateMultipleItems(catalogItem.Definition, session.GetHabbo(), "", amountPurchase);

                                    if (items != null)
                                    {
                                        generatedGenericItems.AddRange(items);
                                    }
                                }
                            }

                            break;
                        }
                }

                foreach (var purchasedItem in generatedGenericItems)
                {
                    if (session.GetHabbo().Inventory.Furniture.AddItem(purchasedItem.ToInventoryItem()))
                    {
                        //Session.SendMessage(new FurniListAddComposer(PurchasedItem));
                        session.Send(new FurniListNotificationComposer(purchasedItem.Id, 1));
                    }
                }

                if (generatedGenericItems.Count > 0)
                {
                    _rewardTracks.Progress(session, RewardTrackActions.BuyFromCatalogue);
                }

                break;
            case "e":
                AvatarEffect effect;

                if (session.GetHabbo().Effects.HasEffect(item.Definition.SpriteId))
                {
                    effect = session.GetHabbo().Effects.GetEffectNullable(item.Definition.SpriteId);

                    if (effect != null)
                    {
                        effect.AddToQuantity();
                    }
                }
                else
                {
                    var habbo = session.GetHabbo();
                    effect = _avatarEffects.Create(habbo.Id, item.Definition.SpriteId, 3600);
                    habbo.Effects.TryAdd(effect);
                }

                if (effect != null) // && Session.GetHabbo().Effects().TryAdd(Effect))
                {
                    session.Send(new AvatarEffectAddedComposer(item.Definition.SpriteId, 3600));
                }

                break;
            case "r":
                var bot = purchasedBot ?? throw new InvalidOperationException("The committed bot purchase did not return a bot.");
                session.GetHabbo().Inventory.Bots.AddBot(bot);
                session.Send(new BotInventoryComposer(BotInventorySnapshot.Capture(session.GetHabbo().Inventory.Bots.Bots.Values)));
                session.Send(new FurniListNotificationComposer((uint)bot.Id, 5));
                break;
            case "b":
                {
                    await _badgeManager.GiveBadge(session.GetHabbo(), item.Definition.ItemName);
                    session.Send(new FurniListNotificationComposer(0, 4));
                    break;
                }
            case "p":
                {
                    if (!PetUtility.TryReadPurchase(extraData, out var petName, out var race, out var color))
                    {
                        return;
                    }

                    Plus.HabboHotel.Rooms.AI.Pet? pet = null;

                    if (!ChargePurchase((connection, transaction) => (pet = PetUtility.CreatePet(connection, transaction,
                            utcNow, session.GetHabbo().Username, session.GetHabbo().Id, petName, item.Definition.BehaviourData, race, color)) != null))
                    {
                        session.SendNotification("Oops! There was an error whilst purchasing this pet.");

                        return;
                    }

                    session.GetHabbo().Inventory.Pets.AddPet(pet!);
                    pet.RoomId = 0;
                    pet.PlacedInRoom = false;
                    session.Send(new FurniListNotificationComposer((uint)pet.PetId, 3));
                    session.Send(new PetInventoryComposer(PetAppearanceSnapshots.Inventory(session.GetHabbo().Inventory.Pets.Pets.Values.ToList())));

                    if (_itemManager.Items.TryGetValue(320, out var petFood))
                    {
                        var food = _itemFactory.CreateSingleItemNullable(petFood, session.GetHabbo(), "", "")?.ToInventoryItem();

                        if (food != null)
                        {
                            session.GetHabbo().Inventory.Furniture.AddItem(food);
                            session.Send(new FurniListNotificationComposer(food.Id, 1));
                        }
                    }

                    _achievementManager.ProgressAchievement(session, "ACH_PetLover", 1);
                    break;
                }
        }

        if (!string.IsNullOrEmpty(item.Badge) &&
            _badgeManager.Badges.TryGetValue(item.Badge, out var badge) &&
            (string.IsNullOrEmpty(badge.RequiredRight) || session.GetHabbo().Access.Can(badge.RequiredRight)))
        {
            await _badgeManager.GiveBadge(session.GetHabbo(), badge.Code);
        }

        session.Send(new PurchaseOKComposer(CatalogPurchaseConfirmation.Capture(item, item.Definition)));
        session.Send(new FurniListUpdateComposer());
    }

    private void PurchaseClubOffer(GameClient session, int offerId, DateTimeOffset utcNow)
    {
        var habbo = session.GetHabbo();
        DateTimeOffset? expiry = null;

        if (_catalogManager.TryGetClubOffer(offerId, out var offer))
        {
            expiry = _clubMemberships.Purchase(habbo, offer);
        }

        if (expiry == null)
        {
            session.Send(new PurchaseErrorComposer(PurchaseError.Unavailable));

            return;
        }

        if (offer.Credits > 0)
        {
            session.Send(new CreditBalanceComposer(habbo.Credits));
        }

        if (offer.Points > 0)
        {
            session.Send(offer.PointsType == 5
                ? new HabboActivityPointNotificationComposer(habbo.Diamonds, -offer.Points, 5)
                : new HabboActivityPointNotificationComposer(habbo.Duckets, -offer.Points));
        }

        session.Send(new PurchaseOKComposer());
        var membershipEnd = expiry.Value;
        // The client caches offers; resend them so the next confirmation shows the new end date.
        session.Send(new HabboClubOffersComposer(ClubOfferSnapshotFactory.Capture(_catalogManager.ClubOffers, ClubWindow, membershipEnd, utcNow)));
        session.Send(new ScrSendUserInfoComposer(ClubStatusSnapshot.Capture(habbo.Access, ClubStatusSnapshot.PurchaseResponse)));
    }
}
