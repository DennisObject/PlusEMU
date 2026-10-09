using System.Data;
using Dapper;
using Plus.Database;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms;

public readonly record struct RoomItemSave(uint Id, int X, int Y, double Z, int Rotation, string? ExtraData, string? WallPosition, bool SaveWallPosition);

[Scoped]
public interface IRoomItemStore
{
    void AssignOwner(uint itemId, int userId);
    void ClearRoom(uint itemId);
    void SaveWallPosition(uint itemId, string wallPosition);
    void MoveWall(uint itemId, uint roomId, string wallPosition) => throw new NotSupportedException("Atomic wall movement is unavailable.");
    void SaveMoved(IReadOnlyList<RoomItemSave> items);
    void PlaceFloor(uint itemId, uint roomId, int x, int y, double z, int rotation);
    void PlaceWall(uint itemId, uint roomId, int x, int y, double z, int rotation, string wallPosition);
}

public sealed class RoomItemStore(IDatabase database) : IRoomItemStore
{
    public void AssignOwner(uint itemId, int userId) => Execute("UPDATE items SET user_id = @userId WHERE id = @itemId LIMIT 1", new { userId, itemId });
    public void ClearRoom(uint itemId) => Execute("UPDATE items SET room_id = 0 WHERE id = @itemId LIMIT 1", new { itemId });
    public void SaveWallPosition(uint itemId, string wallPosition) => Execute("UPDATE items SET wall_pos = @wallPosition WHERE id = @itemId LIMIT 1", new { wallPosition, itemId });
    public void MoveWall(uint itemId, uint roomId, string wallPosition)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var currentRoom = connection.QuerySingleOrDefault<uint?>(
            "SELECT room_id FROM items WHERE id=@itemId FOR UPDATE", new { itemId }, transaction);

        if (currentRoom != roomId) {
            throw new InvalidOperationException("Wall item is no longer in this room.");
        }

        connection.Execute("UPDATE items SET wall_pos=@wallPosition WHERE id=@itemId AND room_id=@roomId",
            new { itemId, roomId, wallPosition }, transaction);
        transaction.Commit();
    }

    public void PlaceFloor(uint itemId, uint roomId, int x, int y, double z, int rotation) =>
        Execute("UPDATE items SET room_id = @roomId, x = @x, y = @y, z = @z, rot = @rotation WHERE id = @itemId LIMIT 1", new { roomId, x, y, z, rotation, itemId });
    public void PlaceWall(uint itemId, uint roomId, int x, int y, double z, int rotation, string wallPosition) =>
        Execute("UPDATE items SET room_id = @roomId, x = @x, y = @y, z = @z, rot = @rotation, wall_pos = @wallPosition WHERE id = @itemId LIMIT 1",
            new { roomId, x, y, z, rotation, wallPosition, itemId });

    public void SaveMoved(IReadOnlyList<RoomItemSave> items)
    {
        using var connection = database.Connection();

        foreach (var item in items) {
            if (!string.IsNullOrEmpty(item.ExtraData)) {
                connection.Execute("UPDATE items SET extra_data = @extraData WHERE id = @id LIMIT 1", new { extraData = item.ExtraData, item.Id });
            }

            if (item.SaveWallPosition) {
                connection.Execute("UPDATE items SET wall_pos = @wallPosition WHERE id = @id LIMIT 1", new { item.WallPosition, item.Id });
            }

            connection.Execute("UPDATE items SET x = @x, y = @y, z = @z, rot = @rotation WHERE id = @id LIMIT 1",
                new { item.X, item.Y, item.Z, item.Rotation, item.Id });
        }
    }

    private void Execute(string sql, object parameters)
    {
        using var connection = database.Connection();
        connection.Execute(sql, parameters);
    }
}
