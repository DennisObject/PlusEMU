using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Inventory.Trading;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.Items;
using Plus.Core;
using Plus.Core.Settings;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Rooms.Trading;

public sealed class Trade
{
    private readonly Room _instance;

    internal Trade(int id, RoomUser playerOne, RoomUser playerTwo, Room room, ITradeStore store, ISettingsManager settings)
    {
        Id = id;
        CanChange = true;
        _instance = room;
        _store = store;
        _settings = settings;
        Users = new TradeUser[2];
        Users[0] = new(playerOne);
        Users[1] = new(playerTwo);
        playerOne.IsTrading = true;
        playerOne.TradeId = Id;
        playerOne.TradePartner = playerTwo.UserId;
        playerTwo.IsTrading = true;
        playerTwo.TradeId = Id;
        playerTwo.TradePartner = playerOne.UserId;
    }
    private readonly ITradeStore _store;
    private readonly ISettingsManager _settings;

    public int Id { get; set; }
    public TradeUser[] Users { get; set; }
    public bool CanChange { get; set; }

    public bool AllAccepted
    {
        get
        {
            foreach (var user in Users) {
                if (user == null) {
                    continue;
                }

                if (!user.HasAccepted) {
                    return false;
                }
            }

            return true;
        }
    }

    public void SendPacket(IServerPacket packet)
    {
        foreach (var user in Users) {
            if (user?.RoomUser?.GetClient() is not { } client) {
                continue;
            }

            client.Send(packet);
        }
    }

    public void RemoveAccepted()
    {
        foreach (var user in Users) {
            if (user == null) {
                continue;
            }

            user.HasAccepted = false;
        }
    }

    public void EndTrade(int userId)
    {
        foreach (var tradeUser in Users) {
            if (tradeUser == null || tradeUser.RoomUser == null) {
                continue;
            }

            RemoveTrade(tradeUser.RoomUser.UserId);
        }

        SendPacket(new TradingClosedComposer(userId));
        _instance.GetTrading().RemoveTrade(Id);
    }

    public void Finish()
    {
        var firstHabbo = Users[0].RoomUser.GetClient()?.GetHabbo();
        var secondHabbo = Users[1].RoomUser.GetClient()?.GetHabbo();
        if (firstHabbo == null || secondHabbo == null) {
            EndTrade(0);
            return;
        }

        var firstInventory = firstHabbo.Id < secondHabbo.Id ? firstHabbo.InventoryMutationSync : secondHabbo.InventoryMutationSync;
        var secondInventory = firstHabbo.Id < secondHabbo.Id ? secondHabbo.InventoryMutationSync : firstHabbo.InventoryMutationSync;
        lock (firstInventory)
        lock (secondInventory) {
            if (!ProcessItems()) {
                EndTrade(0);
                return;
            }

            foreach (var tradeUser in Users) {
                RemoveTrade(tradeUser.RoomUser.UserId);
            }
            _instance.GetTrading().RemoveTrade(Id);
            foreach (var tradeUser in Users) {
                var client = tradeUser.RoomUser.GetClient();
                if (client != null) {
                    PublishCommitted(() => client.Send(new TradingFinishComposer()));
                }
            }
        }
    }

    public void RemoveTrade(int userId)
    {
        var tradeUser = Users[0];

        if (tradeUser.RoomUser.UserId != userId) {
            tradeUser = Users[1];
        }

        tradeUser.RoomUser.RemoveStatus("trd");
        tradeUser.RoomUser.UpdateNeeded = true;
        tradeUser.RoomUser.IsTrading = false;
        tradeUser.RoomUser.TradeId = 0;
        tradeUser.RoomUser.TradePartner = 0;
    }

    public bool ProcessItems()
    {
        var userOne = Users[0].OfferedItems.Values.ToList();
        var userTwo = Users[1].OfferedItems.Values.ToList();
        var roomUserOne = Users[0].RoomUser;
        var roomUserTwo = Users[1].RoomUser;
        if (roomUserOne == null || roomUserOne.GetClient() == null || roomUserOne.GetClient().GetHabbo() == null || roomUserOne.GetClient().GetHabbo().Inventory == null) {
            return false;
        }

        if (roomUserTwo == null || roomUserTwo.GetClient() == null || roomUserTwo.GetClient().GetHabbo() == null || roomUserTwo.GetClient().GetHabbo().Inventory == null) {
            return false;
        }

        var clientOne = roomUserOne.GetClient();
        var clientTwo = roomUserTwo.GetClient();
        var habboOne = clientOne.GetHabbo();
        var habboTwo = clientTwo.GetHabbo();
        if (!OwnsAll(habboOne, userOne) || !OwnsAll(habboTwo, userTwo) || HasAny(habboTwo, userOne) || HasAny(habboOne, userTwo)) {
            SendPacket(new BroadcastMessageAlertComposer("Error! Trading Failed!"));
            return false;
        }

        var autoRedeem = _settings.TryGetValue("trading.auto_exchange_redeemables") == "1";
        var firstWallet = habboOne.Id < habboTwo.Id ? habboOne.WalletSync : habboTwo.WalletSync;
        var secondWallet = habboOne.Id < habboTwo.Id ? habboTwo.WalletSync : habboOne.WalletSync;
        var applied = new List<(Plus.HabboHotel.Users.Habbo Sender, Plus.HabboHotel.Users.Habbo Recipient, InventoryItem Item, bool Redeemed)>();
        lock (firstWallet)
        lock (secondWallet) {
            var oneCreditDelta = RedeemedCredits(habboOne, userTwo, autoRedeem);
            var twoCreditDelta = RedeemedCredits(habboTwo, userOne, autoRedeem);
            var transfers = userOne.Select(item => Transfer(habboOne, habboTwo, item, autoRedeem))
                .Concat(userTwo.Select(item => Transfer(habboTwo, habboOne, item, autoRedeem))).ToArray();
            try {
                if (!_store.Commit(transfers, habboOne.Id, habboTwo.Id, habboOne.Credits + oneCreditDelta, habboTwo.Credits + twoCreditDelta,
                    string.Concat(userOne.Select(item => $"{item.Id};")), string.Concat(userTwo.Select(item => $"{item.Id};")),
                    () => ApplyItems(habboOne, habboTwo, userOne, autoRedeem, applied) && ApplyItems(habboTwo, habboOne, userTwo, autoRedeem, applied))) {
                    RestoreItems(applied);
                    SendPacket(new BroadcastMessageAlertComposer("Error! Trading Failed!"));
                    return false;
                }
            }
            catch {
                RestoreItems(applied);
                SendPacket(new BroadcastMessageAlertComposer("Error! Trading Failed!"));
                return false;
            }

            PublishTransfers(clientOne, clientTwo, userOne, autoRedeem);
            PublishTransfers(clientTwo, clientOne, userTwo, autoRedeem);
        }

        return true;
    }

    private static bool OwnsAll(Plus.HabboHotel.Users.Habbo owner, IEnumerable<InventoryItem> items) =>
        items.All(item => ReferenceEquals(owner.Inventory.Furniture.GetItem(item.Id), item));

    private static bool HasAny(Plus.HabboHotel.Users.Habbo recipient, IEnumerable<InventoryItem> items) =>
        items.Any(item => recipient.Inventory.Furniture.HasItem(item.Id));

    private static bool ShouldRedeem(Plus.HabboHotel.Users.Habbo recipient, InventoryItem item, bool autoRedeem) =>
        autoRedeem && item.Definition.InteractionType == InteractionType.Exchange && !recipient.WalletClosed;

    private static int RedeemedCredits(Plus.HabboHotel.Users.Habbo recipient, IEnumerable<InventoryItem> items, bool autoRedeem) =>
        items.Where(item => ShouldRedeem(recipient, item, autoRedeem)).Sum(item => item.Definition.BehaviourData);

    private static TradeTransfer Transfer(Plus.HabboHotel.Users.Habbo sender, Plus.HabboHotel.Users.Habbo recipient, InventoryItem item, bool autoRedeem) =>
        new(item.Id, sender.Id, recipient.Id, ShouldRedeem(recipient, item, autoRedeem));

    private static bool ApplyItems(Plus.HabboHotel.Users.Habbo sender, Plus.HabboHotel.Users.Habbo recipient, IEnumerable<InventoryItem> items,
        bool autoRedeem, ICollection<(Plus.HabboHotel.Users.Habbo Sender, Plus.HabboHotel.Users.Habbo Recipient, InventoryItem Item, bool Redeemed)> applied)
    {
        foreach (var item in items) {
            if (!sender.Inventory.Furniture.RemoveItem(item.Id)) {
                return false;
            }

            var redeem = ShouldRedeem(recipient, item, autoRedeem);
            if (redeem) {
                recipient.Credits += item.Definition.BehaviourData;
            }
            else if (!recipient.Inventory.Furniture.AddItem(item)) {
                sender.Inventory.Furniture.AddItem(item);
                return false;
            }

            if (!redeem) {
                item.OwnerId = (uint)recipient.Id;
            }

            applied.Add((sender, recipient, item, redeem));
        }

        return true;
    }

    private static void RestoreItems(IList<(Plus.HabboHotel.Users.Habbo Sender, Plus.HabboHotel.Users.Habbo Recipient, InventoryItem Item, bool Redeemed)> applied)
    {
        for (var index = applied.Count - 1; index >= 0; index--) {
            var transfer = applied[index];
            if (transfer.Redeemed) {
                transfer.Recipient.Credits -= transfer.Item.Definition.BehaviourData;
            }
            else {
                transfer.Recipient.Inventory.Furniture.RemoveItem(transfer.Item.Id);
                transfer.Item.OwnerId = (uint)transfer.Sender.Id;
            }

            transfer.Sender.Inventory.Furniture.AddItem(transfer.Item);
        }

        applied.Clear();
    }

    private static void PublishTransfers(GameClient sender, GameClient recipient, IEnumerable<InventoryItem> items, bool autoRedeem)
    {
        var creditsChanged = false;
        foreach (var item in items) {
            PublishCommitted(() => sender.Send(new FurniListRemoveComposer(item.Id)));
            if (ShouldRedeem(recipient.GetHabbo(), item, autoRedeem)) {
                creditsChanged = true;
                continue;
            }

            PublishCommitted(() => recipient.Send(new FurniListAddComposer(InventoryItemSnapshot.Capture(item))));
            PublishCommitted(() => recipient.Send(new FurniListNotificationComposer(item.Id, 1)));
        }

        if (creditsChanged) {
            PublishCommitted(() => recipient.Send(new CreditBalanceComposer(recipient.GetHabbo().Credits)));
        }
    }

    internal static void PublishCommitted(Action publish)
    {
        try {
            publish();
        }
        catch (Exception exception) {
            ExceptionLogger.LogException(exception);
        }
    }

    internal static void ReceiveTradedItem(GameClient recipient, InventoryItem item, bool autoRedeem, ITradeStore store)
    {
        var habbo = recipient.GetHabbo();

        if (item.Definition.InteractionType == InteractionType.Exchange && autoRedeem) {
            lock (habbo.WalletSync) {
                if (!habbo.WalletClosed) {
                    habbo.Credits += item.Definition.BehaviourData;
                    recipient.Send(new CreditBalanceComposer(habbo.Credits));
                    store.DeleteItem(item.Id);

                    return;
                }
            }
        }

        // ProcessItems only trades between loaded inventories, so the received item always has somewhere to go.
        var inventory = habbo.Inventory ?? throw new InvalidOperationException("The trade recipient has no loaded inventory.");

        // A wallet saved for shutdown cannot receive credits; transfer the voucher intact.
        if (inventory.Furniture.AddItem(item)) {
            recipient.Send(new FurniListAddComposer(InventoryItemSnapshot.Capture(item)));
            recipient.Send(new FurniListNotificationComposer(item.Id, 1));
            store.TransferItem(item.Id, habbo.Id);
        }
    }
}
