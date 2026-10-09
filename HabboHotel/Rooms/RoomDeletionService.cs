using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Dapper;
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

        foreach (var item in room.GetRoomItemHandler().GetWallAndFloor.ToList()) {
            if (item == null) {
                continue;
            }

            itemsToRemove.Add(item);
        }

        using (var connection = _database.Connection()) {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            connection.Execute("UPDATE room_music_players player JOIN items item ON item.id=player.item_id SET player.started_at=NULL,player.version=player.version+1,item.extra_data='0' WHERE item.room_id=@roomId", new { roomId }, transaction);
            connection.Execute("UPDATE items SET room_id=0 WHERE room_id=@roomId", new { roomId }, transaction);
            connection.Execute("DELETE FROM user_roomvisits WHERE room_id=@roomId", new { roomId }, transaction);
            connection.Execute("DELETE FROM user_favorites WHERE room_id=@roomId", new { roomId }, transaction);
            connection.Execute("DELETE FROM room_rights WHERE room_id=@roomId", new { roomId }, transaction);
            connection.Execute("UPDATE users_settings SET home_room=0 WHERE home_room=@roomId", new { roomId }, transaction);
            connection.Execute("DELETE FROM rooms WHERE id=@roomId LIMIT 1", new { roomId }, transaction);
            transaction.Commit();
        }

        foreach (var item in itemsToRemove) {
            if (Music.RoomMusicDefinition.IsPlayer(item.Definition)) {
                item.LegacyDataString = "0";
            }

            var targetClient = _clientManager.GetClientByUserId(item.UserId);

            // An owner whose inventory is not loaded is handled like an offline one: the item moves in storage only.
            if (targetClient?.GetHabbo()?.Inventory is { } targetInventory) //Again, do we have an active client?
            {
                room.GetRoomItemHandler().RemoveFurniture(targetClient, item.Id);
                targetInventory.Furniture.AddItem(item.ToInventoryItem());
                targetClient.Send(new FurniListUpdateComposer());
            }
            else //No, query time.
            {
                room.GetRoomItemHandler().RemoveFurniture(null, item.Id);
            }
        }

        _roomManager.UnloadRoom(roomId);
    }
}
