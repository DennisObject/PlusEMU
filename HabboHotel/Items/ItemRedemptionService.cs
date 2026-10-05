using Dapper;
using Plus.Communication.Packets.Outgoing.Inventory.AvatarEffect;
using Plus.Communication.Packets.Outgoing.Inventory.AvatarEffects;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Rooms.Notifications;
using Plus.Core.Settings;
using Plus.Database;
using Plus.HabboHotel.Catalog.Clothing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Clothing.Parts;

namespace Plus.HabboHotel.Items;

public interface IItemRedemptionStore
{
    void DeleteExchange(uint itemId, int ownerId, uint roomId);
    IReadOnlyList<ClothingParts> ConsumeClothing(uint itemId, int ownerId, uint roomId, string name, IReadOnlyCollection<int> partIds);
}

public sealed class ItemRedemptionStore(IDatabase database) : IItemRedemptionStore
{
    public void DeleteExchange(uint itemId, int ownerId, uint roomId)
    {
        using var connection = database.Connection();
        var deleted = connection.Execute(
            "DELETE FROM items WHERE id=@itemId AND user_id=@ownerId AND room_id=@roomId LIMIT 1",
            new { itemId, ownerId, roomId });
        if (deleted != 1)
            throw new InvalidOperationException("Exchange furniture was not deleted.");
    }

    public IReadOnlyList<ClothingParts> ConsumeClothing(uint itemId, int ownerId, uint roomId, string name, IReadOnlyCollection<int> partIds)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var deleted = connection.Execute(
            "DELETE FROM items WHERE id=@itemId AND user_id=@ownerId AND room_id=@roomId LIMIT 1",
            new { itemId, ownerId, roomId }, transaction);
        if (deleted != 1)
            throw new InvalidOperationException("Clothing furniture was not deleted.");
        if (connection.ExecuteScalar<int?>("SELECT id FROM users WHERE id=@ownerId FOR UPDATE", new { ownerId }, transaction) == null)
            throw new InvalidOperationException("Clothing owner no longer exists.");

        var result = new List<ClothingParts>();
        foreach (var partId in partIds.Distinct())
        {
            var existing = connection.QuerySingleOrDefault<ClothingRow>(
                "SELECT id,part_id AS PartId,part AS Part FROM user_clothing WHERE user_id=@ownerId AND part_id=@partId LIMIT 1",
                new { ownerId, partId }, transaction);
            if (existing != null)
            {
                result.Add(new(existing.Id, existing.PartId, existing.Part));
                continue;
            }

            var id = connection.ExecuteScalar<int>(
                "INSERT INTO user_clothing(user_id,part_id,part) VALUES(@ownerId,@partId,@name); SELECT LAST_INSERT_ID()",
                new { ownerId, partId, name }, transaction);
            result.Add(new(id, partId, name));
        }

        transaction.Commit();
        return result;
    }

    private sealed record ClothingRow(int Id, int PartId, string Part);
}

public interface IItemRedemptionService
{
    void RedeemCredits(Room room, GameClient session, uint itemId);
    void RedeemClothing(GameClient session, uint itemId);
}

public sealed class ItemRedemptionService(IItemRedemptionStore store, ISettingsManager settings, IClothingManager clothingManager) : IItemRedemptionService
{
    public void RedeemCredits(Room room, GameClient session, uint itemId)
    {
        var habbo = session.GetHabbo();
        if (habbo.CurrentRoom != room || !room.CheckRights(session, true))
            return;
        if (settings.TryGetValue("room.item.exchangeables.enabled") != "1")
        {
            session.SendNotification("The hotel managers have temporarilly disabled exchanging!");
            return;
        }

        lock (habbo.WalletSync)
        {
            if (habbo.WalletClosed || habbo.CurrentRoom != room || !room.CheckRights(session, true))
                return;
            var item = room.GetRoomItemHandler().GetItem(itemId);
            if (item == null || item.IsTemporary || item.RoomId != room.RoomId || item.OwnerId != habbo.Id || item.Definition?.InteractionType != InteractionType.Exchange)
                return;
            var value = item.Definition.BehaviourData;
            if (value <= 0)
                return;
            int balance;
            try { balance = checked(habbo.Credits + value); }
            catch (OverflowException) { return; }

            store.DeleteExchange(item.Id, habbo.Id, room.RoomId);
            habbo.Credits = balance;
            room.GetRoomItemHandler().RemoveFurniture(null, item.Id);
            habbo.Inventory.Furniture.RemoveItem(item.Id);
            session.Send(new CreditBalanceComposer(balance));
            session.Send(new FurniListUpdateComposer());
            session.Send(new FurniListRemoveComposer(item.Id));
        }
    }

    public void RedeemClothing(GameClient session, uint itemId)
    {
        var habbo = session.GetHabbo();
        var room = habbo.CurrentRoom;
        if (room == null)
            return;
        lock (habbo.WalletSync)
        {
            if (habbo.WalletClosed || habbo.CurrentRoom != room)
                return;
            var item = room.GetRoomItemHandler().GetItem(itemId);
            if (item == null || item.IsTemporary || item.RoomId != room.RoomId || item.OwnerId != habbo.Id)
                return;
            if (item.Definition?.InteractionType != InteractionType.PurchasableClothing)
            {
                session.SendNotification("Oops, this item isn't set as a sellable clothing item!");
                return;
            }
            if (item.Definition.BehaviourData == 0)
            {
                session.SendNotification("Oops, this item doesn't have a linking clothing configuration, please report it!");
                return;
            }
            if (!clothingManager.TryGetClothing(item.Definition.BehaviourData, out var clothing))
            {
                session.SendNotification("Oops, we couldn't find this clothing part!");
                return;
            }

            var committed = store.ConsumeClothing(item.Id, habbo.Id, room.RoomId, clothing.ClothingName, clothing.PartIds);
            habbo.Clothing.PublishCommitted(committed);
            room.GetRoomItemHandler().RemoveFurniture(session, item.Id);
            session.Send(new FigureSetIdsComposer(habbo.Clothing.GetClothingParts.ToArray()));
            session.Send(new RoomNotificationComposer("figureset.redeemed.success"));
            session.SendWhisper("If for some reason cannot see your new clothing, reload the hotel!");
        }
    }
}
