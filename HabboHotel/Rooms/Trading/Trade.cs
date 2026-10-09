using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Inventory.Trading;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.Items;
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
        foreach (var tradeUser in Users) {
            if (tradeUser == null) {
                continue;
            }

            RemoveTrade(tradeUser.RoomUser.UserId);
        }

        ProcessItems();
        SendPacket(new TradingFinishComposer());
        _instance.GetTrading().RemoveTrade(Id);
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

    public void ProcessItems()
    {
        var userOne = Users[0].OfferedItems.Values.ToList();
        var userTwo = Users[1].OfferedItems.Values.ToList();
        var roomUserOne = Users[0].RoomUser;
        var roomUserTwo = Users[1].RoomUser;
        var logUserOne = "";
        var logUserTwo = "";

        if (roomUserOne == null || roomUserOne.GetClient() == null || roomUserOne.GetClient().GetHabbo() == null || roomUserOne.GetClient().GetHabbo().Inventory == null) {
            return;
        }

        if (roomUserTwo == null || roomUserTwo.GetClient() == null || roomUserTwo.GetClient().GetHabbo() == null || roomUserTwo.GetClient().GetHabbo().Inventory == null) {
            return;
        }

        foreach (var item in userOne) {
            var I = roomUserOne.GetClient().GetHabbo().Inventory.Furniture.GetItem(item.Id);

            if (I == null) {
                SendPacket(new BroadcastMessageAlertComposer("Error! Trading Failed!"));

                return;
            }
        }

        foreach (var item in userTwo) {
            var I = roomUserTwo.GetClient().GetHabbo().Inventory.Furniture.GetItem(item.Id);

            if (I == null) {
                SendPacket(new BroadcastMessageAlertComposer("Error! Trading Failed!"));

                return;
            }
        }

        foreach (var item in userOne) {
            logUserOne += $"{item.Id};";
            roomUserOne.GetClient().GetHabbo().Inventory.Furniture.RemoveItem(item.Id);
            roomUserOne.GetClient().Send(new FurniListRemoveComposer(item.Id));
            ReceiveTradedItem(roomUserTwo.GetClient(), item,
                item.Definition.InteractionType == InteractionType.Exchange && _settings.TryGetValue("trading.auto_exchange_redeemables") == "1", _store);
        }

        foreach (var item in userTwo) {
            logUserTwo += $"{item.Id};";
            roomUserTwo.GetClient().GetHabbo().Inventory.Furniture.RemoveItem(item.Id);
            roomUserTwo.GetClient().Send(new FurniListRemoveComposer(item.Id));
            ReceiveTradedItem(roomUserOne.GetClient(), item,
                item.Definition.InteractionType == InteractionType.Exchange && _settings.TryGetValue("trading.auto_exchange_redeemables") == "1", _store);
        }

        _store.Log(roomUserOne.UserId, roomUserTwo.UserId, logUserOne, logUserTwo);
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
