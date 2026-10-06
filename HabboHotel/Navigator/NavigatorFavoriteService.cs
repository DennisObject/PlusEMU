using Dapper;
using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Navigator;

public interface INavigatorFavoriteService
{
    void Add(GameClient session, uint roomId);
    void Remove(GameClient session, uint roomId);
}

public sealed class NavigatorFavoriteService(IDatabase database, IRoomManager roomManager) : INavigatorFavoriteService
{
    private const int MaximumFavorites = 30;

    public void Add(GameClient session, uint roomId)
    {
        using var connection = database.Connection();

        if (!RoomExists(connection, roomId)) {
            return;
        }

        var habbo = session.GetHabbo();

        lock (habbo.FavoriteRooms.SyncRoot) {
            if (habbo.FavoriteRooms.Count >= MaximumFavorites || habbo.FavoriteRooms.Contains(roomId)) {
                return;
            }

            connection.Execute("INSERT INTO user_favorites (user_id, room_id) VALUES (@userId, @roomId)",
                new { userId = habbo.Id, roomId });
            habbo.FavoriteRooms.Add(roomId);
            session.Send(new UpdateFavouriteRoomComposer(roomId, true));
        }
    }

    public void Remove(GameClient session, uint roomId)
    {
        var habbo = session.GetHabbo();

        lock (habbo.FavoriteRooms.SyncRoot) {
            using var connection = database.Connection();
            connection.Execute("DELETE FROM user_favorites WHERE user_id=@userId AND room_id=@roomId LIMIT 1",
                new { userId = habbo.Id, roomId });
            habbo.FavoriteRooms.Remove(roomId);
            session.Send(new UpdateFavouriteRoomComposer(roomId, false));
        }
    }

    private bool RoomExists(System.Data.IDbConnection connection, uint roomId)
    {
        if (roomManager.TryGetRoom(roomId, out _)) {
            return true;
        }

        var modelName = connection.QuerySingleOrDefault<string?>(
            "SELECT rooms.model_name FROM rooms INNER JOIN users ON users.id=rooms.owner WHERE rooms.id=@roomId LIMIT 1",
            new { roomId });

        return modelName != null && roomManager.TryGetModel(modelName, out _);
    }
}
