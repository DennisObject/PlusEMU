using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms;

public interface IRoomDeletionService
{
    /// <summary>Returns placed furniture to its owners, unloads the room and deletes it with its rights, visits and favourites.</summary>
    void Delete(Room room);
}

public sealed class RoomDeletionService : IRoomDeletionService
{
    private readonly IGameClientManager _clientManager;
    private readonly IRoomManager _roomManager;
    private readonly IDatabase _database;

    public RoomDeletionService(IGameClientManager clientManager, IRoomManager roomManager, IDatabase database)
    {
        _clientManager = clientManager;
        _roomManager = roomManager;
        _database = database;
    }

    public void Delete(Room room)
    {
        var roomId = room.Id;
        var itemsToRemove = new List<Item>();
        foreach (var item in room.GetRoomItemHandler().GetWallAndFloor.ToList())
        {
            if (item == null)
                continue;
            if (item.Definition.InteractionType == InteractionType.Moodlight)
            {
                using var dbClient = _database.GetQueryReactor();
                dbClient.SetQuery("DELETE FROM `room_items_moodlight` WHERE `item_id` = @itemId LIMIT 1");
                dbClient.AddParameter("itemId", item.Id);
                dbClient.RunQuery();
            }
            itemsToRemove.Add(item);
        }
        foreach (var item in itemsToRemove)
        {
            var targetClient = _clientManager.GetClientByUserId(item.UserId);
            if (targetClient != null && targetClient.GetHabbo() != null) //Again, do we have an active client?
            {
                room.GetRoomItemHandler().RemoveFurniture(targetClient, item.Id);
                targetClient.GetHabbo().Inventory.Furniture.AddItem(item.ToInventoryItem());
                targetClient.Send(new FurniListUpdateComposer());
            }
            else //No, query time.
            {
                room.GetRoomItemHandler().RemoveFurniture(null, item.Id);
                using var dbClient = _database.GetQueryReactor();
                dbClient.SetQuery("UPDATE `items` SET `room_id` = '0' WHERE `id` = @itemId LIMIT 1");
                dbClient.AddParameter("itemId", item.Id);
                dbClient.RunQuery();
            }
        }
        _roomManager.UnloadRoom(roomId);
        using (var dbClient = _database.GetQueryReactor())
        {
            dbClient.SetQuery("DELETE FROM `user_roomvisits` WHERE `room_id` = @roomId; DELETE FROM `rooms` WHERE `id` = @roomId LIMIT 1; " +
                              "DELETE FROM `user_favorites` WHERE `room_id` = @roomId; DELETE FROM `items` WHERE `room_id` = @roomId; " +
                              "DELETE FROM `room_rights` WHERE `room_id` = @roomId; UPDATE `users` SET `home_room` = '0' WHERE `home_room` = @roomId");
            dbClient.AddParameter("roomId", roomId);
            dbClient.RunQuery();
        }
        _roomManager.UnloadRoom(roomId);
    }
}
