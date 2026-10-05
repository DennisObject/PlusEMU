using Dapper;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Rooms.Furni;
using Plus.Database;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Items;

public sealed record GiftContent(uint BaseId, string ExtraData);

public interface IGiftStore
{
    GiftContent? Find(uint itemId);
    void Open(uint itemId, int ownerId, uint roomId, GiftContent content);
    void DeleteInvalid(uint itemId, int ownerId, uint roomId);
}

public sealed class GiftStore(IDatabase database) : IGiftStore
{
    public GiftContent? Find(uint itemId)
    {
        using var connection = database.Connection();
        return connection.QuerySingleOrDefault<GiftContent>("SELECT base_id AS BaseId,extra_data AS ExtraData FROM user_presents WHERE item_id=@itemId LIMIT 1", new { itemId });
    }

    public void Open(uint itemId, int ownerId, uint roomId, GiftContent content)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (connection.Execute("UPDATE items SET base_item=@BaseId,extra_data=@ExtraData,room_id=0 WHERE id=@itemId AND user_id=@ownerId AND room_id=@roomId LIMIT 1", new { itemId, ownerId, roomId, content.BaseId, content.ExtraData }, transaction) != 1 ||
            connection.Execute("DELETE FROM user_presents WHERE item_id=@itemId LIMIT 1", new { itemId }, transaction) != 1)
            throw new InvalidOperationException("Gift was not opened.");
        transaction.Commit();
    }

    public void DeleteInvalid(uint itemId, int ownerId, uint roomId)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (connection.Execute("DELETE FROM items WHERE id=@itemId AND user_id=@ownerId AND room_id=@roomId LIMIT 1", new { itemId, ownerId, roomId }, transaction) != 1)
            throw new InvalidOperationException("Invalid gift was not deleted.");
        connection.Execute("DELETE FROM user_presents WHERE item_id=@itemId", new { itemId }, transaction);
        transaction.Commit();
    }
}

public interface IGiftOpeningService { Task OpenAsync(GameClient session, uint itemId); }

public sealed class GiftOpeningService(IGiftStore store, IItemDataManager items, ICacheManager cache) : IGiftOpeningService
{
    public Task OpenAsync(GameClient session, uint itemId)
    {
        var habbo = session.GetHabbo();
        lock (habbo.WalletSync)
        {
            var room = habbo.CurrentRoom;
            if (habbo.WalletClosed || room == null) return Task.CompletedTask;
            var gift = room.GetRoomItemHandler().GetItem(itemId);
            if (gift == null || gift.IsTemporary || gift.RoomId != room.RoomId || gift.OwnerId != habbo.Id || gift.Definition?.InteractionType != InteractionType.Gift)
                return Task.CompletedTask;
            var content = store.Find(gift.Id);
            var fields = gift.LegacyDataString.Split((char)5);
            var purchaserValid = fields.Length > 2 && int.TryParse(fields[2], out var purchaserId) && cache.GenerateUser(purchaserId) != null;
            if (content == null || !purchaserValid || !items.Items.TryGetValue(content?.BaseId ?? 0, out var definition))
            {
                store.DeleteInvalid(gift.Id, habbo.Id, room.RoomId);
                room.GetRoomItemHandler().RemoveFurniture(null, gift.Id);
                habbo.Inventory.Furniture.RemoveItem(gift.Id);
                session.Send(new FurniListRemoveComposer(gift.Id));
                session.SendNotification(content != null && purchaserValid ? "Oops, it appears that the item within the gift is no longer in the hotel!" : "Oops! Appears there was a bug with this gift.\nWe'll just get rid of it for you.");
                return Task.CompletedTask;
            }

            var x = gift.GetX;
            var y = gift.GetY;
            var rotation = gift.Rotation;
            store.Open(gift.Id, habbo.Id, room.RoomId, content);
            room.GetRoomItemHandler().RemoveFurniture(session, gift.Id);
            gift.BaseItem = (int)content.BaseId;
            gift.Definition = definition;
            gift.LegacyDataString = content.ExtraData ?? string.Empty;
            var inRoom = definition.Type == ItemType.Floor && room.GetRoomItemHandler().SetFloorItem(session, gift, x, y, rotation, true, false, true);
            if (!inRoom)
                habbo.Inventory.Furniture.AddItem(gift.ToInventoryItem());
            var wire = new OpenGiftWireData(definition.Type.ToString(), definition.SpriteId, definition.ItemName, gift.Id, inRoom, gift.LegacyDataString);
            session.Send(new OpenGiftComposer(wire));
            session.Send(new FurniListUpdateComposer());
            return Task.CompletedTask;
        }
    }
}
