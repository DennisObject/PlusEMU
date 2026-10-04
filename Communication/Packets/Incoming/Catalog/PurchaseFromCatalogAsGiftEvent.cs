using System.Globalization;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.Core.Settings;
using Plus.Database;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Catalog.Utilities;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Quests;
using Plus.Utilities;
using Dapper;
using Plus.HabboHotel.Subscriptions;

namespace Plus.Communication.Packets.Incoming.Catalog;

public class PurchaseFromCatalogAsGiftEvent : IPacketEvent
{
    private readonly ICatalogManager _catalogManager;
    private readonly ISettingsManager _settingsManager;
    private readonly IItemDataManager _itemManager;
    private readonly IDatabase _database;
    private readonly IAchievementManager _achievementManager;
    private readonly IGameClientManager _gameClientManager;
    private readonly IQuestManager _questManager;
    private readonly IClubMembershipService _clubMemberships;
    private readonly IClubRewards _clubRewards;

    public PurchaseFromCatalogAsGiftEvent(ICatalogManager catalogManager,
        ISettingsManager settingsManager,
        IItemDataManager itemManager,
        IDatabase database,
        IAchievementManager achievementManager,
        IGameClientManager gameClientManager,
        IQuestManager questManager,
        IClubMembershipService clubMemberships, IClubRewards clubRewards)
    {
        _catalogManager = catalogManager;
        _settingsManager = settingsManager;
        _itemManager = itemManager;
        _database = database;
        _achievementManager = achievementManager;
        _gameClientManager = gameClientManager;
        _questManager = questManager;
        _clubMemberships = clubMemberships;
        _clubRewards = clubRewards;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var pageId = packet.ReadInt();
        var itemId = packet.ReadInt();
        var data = packet.ReadString();
        var giftUser = StringCharFilter.Escape(packet.ReadString());
        var giftMessage = StringCharFilter.Escape(packet.ReadString().Replace(Convert.ToChar(5), ' '));
        var spriteId = packet.ReadInt();
        var boxId = packet.ReadInt();
        var ribbonId = packet.ReadInt();
        packet.ReadBool();
        if (_settingsManager.TryGetValue("room.item.gifts.enabled") != "1")
        {
            session.SendNotification("The hotel managers have disabled gifting");
            return Task.CompletedTask;
        }
        if (!_catalogManager.TryGetPage(pageId, out var page))
            return Task.CompletedTask;
        if (!page.CanOpen(session.GetHabbo()))
            return Task.CompletedTask;
        if (page.Layout is "club_buy" or "vip_buy" or "loyalty_vip_buy")
        {
            var receiver = _gameClientManager.GetClientByUsername(giftUser)?.GetHabbo();
            if (receiver == null || !receiver.AllowGifts || !_catalogManager.TryGetClubOffer(itemId, out var offer) || !offer.Giftable || _clubMemberships.Purchase(session.GetHabbo(), offer, receiver.Id) == null)
            { session.Send(new PurchaseErrorComposer(0)); return Task.CompletedTask; }
            session.Send(new CreditBalanceComposer(session.GetHabbo().Credits));
            session.Send(new HabboActivityPointNotificationComposer(session.GetHabbo().Duckets, -offer.Points, 0));
            session.Send(new HabboActivityPointNotificationComposer(session.GetHabbo().Diamonds, -offer.Points, 5));
            session.Send(new PurchaseOkComposer());
            return Task.CompletedTask;
        }
        if (!page.Offers.TryGetValue(itemId, out var item))
            return Task.CompletedTask;
        if (!item.CanPurchase(session.GetHabbo())) return Task.CompletedTask;
        if (!ItemUtility.CanGiftItem(item))
            return Task.CompletedTask;
        if (!_itemManager.Gifts.TryGetValue(spriteId, out var presentId) || !_itemManager.Items.TryGetValue(presentId, out var presentData) || presentData.InteractionType != InteractionType.Gift)
            return Task.CompletedTask;
        if (session.GetHabbo().Credits < item.CostCredits)
        {
            session.Send(new PresentDeliverErrorComposer(true, false));
            return Task.CompletedTask;
        }
        if (session.GetHabbo().Duckets < item.CostPixels)
        {
            session.Send(new PresentDeliverErrorComposer(false, true));
            return Task.CompletedTask;
        }
        var habbo = _gameClientManager.GetClientByUsername(giftUser)?.GetHabbo();
        if (habbo == null)
        {
            session.Send(new GiftWrappingErrorComposer());
            return Task.CompletedTask;
        }
        if (!habbo.AllowGifts)
        {
            session.SendNotification("Oops, this user doesn't allow gifts to be sent to them!");
            return Task.CompletedTask;
        }
        if ((DateTime.Now - session.GetHabbo().LastGiftPurchaseTime).TotalSeconds <= 15.0)
        {
            session.SendNotification("You're purchasing gifts too fast! Please wait 15 seconds!");
            session.GetHabbo().GiftPurchasingWarnings += 1;
            if (session.GetHabbo().GiftPurchasingWarnings >= 25)
                session.GetHabbo().SessionGiftBlocked = true;
            return Task.CompletedTask;
        }
        if (session.GetHabbo().SessionGiftBlocked)
            return Task.CompletedTask;
        var extra_data = GiftWrap.PresentData(giftUser, giftMessage, session.GetHabbo().Id, item.Definition.Id, spriteId, boxId, ribbonId);
        string itemExtraData = null;
        switch (item.Definition.InteractionType)
        {
                case InteractionType.None:
                    itemExtraData = "";
                    break;
                case InteractionType.Pet:
                    if (!GiftWrap.PetDataAccepted(data))
                        return Task.CompletedTask;
                    _achievementManager.ProgressAchievement(session, "ACH_PetLover", 1);
                    break;
                case InteractionType.Floor:
                case InteractionType.Wallpaper:
                case InteractionType.Landscape:
                    double number = 0;
                    try
                    {
                        number = string.IsNullOrEmpty(data) ? 0 : double.Parse(data, PlusEnvironment.CultureInfo);
                    }
                    catch
                    {
                        //ignored
                    }
                    itemExtraData = number.ToString(CultureInfo.CurrentCulture).Replace(',', '.');
                    break; // maintain extra data // todo: validate
                case InteractionType.Postit:
                    itemExtraData = "FFFF33";
                    break;
                case InteractionType.Moodlight:
                    itemExtraData = "1,1,1,#000000,255";
                    break;
                case InteractionType.Trophy:
                    itemExtraData = $"{session.GetHabbo().Username}{Convert.ToChar(9)}{DateTime.Now.Day}-{DateTime.Now.Month}-{DateTime.Now.Year}{Convert.ToChar(9)}{data}";
                    break;
                case InteractionType.Mannequin:
                    itemExtraData = $"m{Convert.ToChar(5)}.ch-210-1321.lg-285-92{Convert.ToChar(5)}Default Mannequin";
                    break;
                case InteractionType.BadgeDisplay:
                    if (!session.GetHabbo().Inventory.Badges.HasBadge(data))
                    {
                        session.Send(new BroadcastMessageAlertComposer("Oops, it appears that you do not own this badge."));
                        return Task.CompletedTask;
                    }
                    itemExtraData = $"{data}{Convert.ToChar(9)}{session.GetHabbo().Username}{Convert.ToChar(9)}{DateTime.Now.Day}-{DateTime.Now.Month}-{DateTime.Now.Year}";
                    break;
                default:
                    itemExtraData = item.Definition.InteractionType is InteractionType.CameraPicture or InteractionType.Background
                        || FurniExtraData.RejectsClientImage(new[] { data })
                        ? ""
                        : data;
                    break;
            }

        Plus.HabboHotel.Users.Inventory.Furniture.InventoryItem? giveItem = null;
        if (!_clubRewards.Charge(session.GetHabbo(), item.CostCredits, item.CostPixels, item.CostDiamonds, (connection, transaction) =>
        {
            if (!item.CanPurchase(session.GetHabbo())) return false;
            var newItemId = connection.ExecuteScalar<uint>("INSERT INTO items (base_item, user_id, extra_data) VALUES (@baseId, @habboId, @extra_data); SELECT LAST_INSERT_ID()",
                new { baseId = presentData.Id, habboId = habbo.Id, extra_data }, transaction);
            connection.Execute("INSERT INTO user_presents (item_id, base_id, extra_data) VALUES (@itemId, @baseId, @extra_data)",
                new { itemId = newItemId, baseId = item.Definition.Id, extra_data = itemExtraData ?? "" }, transaction);
            giveItem = new Plus.HabboHotel.Users.Inventory.Furniture.InventoryItem { Id = newItemId, OwnerId = (uint)habbo.Id,
                Definition = presentData, ExtraData = FurniExtraData.Load(presentData, extra_data, keepLegacy: true) };
            return true;
        })) { session.Send(new PurchaseErrorComposer(0)); return Task.CompletedTask; }
        if (giveItem != null)
        {
            var receiver = _gameClientManager.GetClientByUserId(habbo.Id);
            if (receiver != null)
            {
                receiver.GetHabbo().Inventory.Furniture.AddItem(giveItem);
                receiver.Send(new FurniListNotificationComposer(giveItem.Id, 1));
                receiver.Send(new PurchaseOkComposer());
                receiver.Send(new FurniListAddComposer(giveItem));
                receiver.Send(new FurniListUpdateComposer());
            }

            if (habbo.Id != session.GetHabbo().Id)
            {
                _achievementManager.ProgressAchievement(session, "ACH_GiftGiver", 1);
                if (receiver != null)
                    _achievementManager.ProgressAchievement(receiver, "ACH_GiftReceiver", 1);
                _questManager.ProgressUserQuest(session, QuestType.GiftOthers);
            }
        }
        session.Send(new PurchaseOkComposer(item, presentData));
        if (item.CostCredits > 0) session.Send(new CreditBalanceComposer(session.GetHabbo().Credits));
        if (item.CostPixels > 0) session.Send(new HabboActivityPointNotificationComposer(session.GetHabbo().Duckets, -item.CostPixels));
        if (item.CostDiamonds > 0) session.Send(new HabboActivityPointNotificationComposer(session.GetHabbo().Diamonds, -item.CostDiamonds, 5));
        session.GetHabbo().LastGiftPurchaseTime = DateTime.Now;
        return Task.CompletedTask;
    }
}