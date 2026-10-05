using System.Diagnostics.CodeAnalysis;
using Dapper;

namespace Plus.HabboHotel.Rooms;

public static class RoomFactory
{
    public static List<RoomData> GetRoomsDataByOwnerSortByName(int ownerId)
    {
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        var rows = connection.Query<RoomDataRow>(RoomDataMapping.SelectRoom + "WHERE users.id = @ownerId ORDER BY rooms.caption", new { ownerId });
        var rooms = new List<RoomData>();
        foreach (var row in rows)
        {
            if (PlusEnvironment.Game.RoomManager.TryGetRoom(row.Id, out var loaded))
                rooms.Add(loaded.Data);
            else if (PlusEnvironment.Game.RoomManager.TryGetModel(row.ModelName, out var model))
                rooms.Add(Prepare(RoomDataMapping.CreateData(row, model, false)));
        }
        return rooms;
    }

    public static bool TryGetData(uint roomId, [NotNullWhen(true)] out RoomData? data)
    {
        if (PlusEnvironment.Game.RoomManager.TryGetRoom(roomId, out var loaded))
        {
            data = loaded.Data;
            return true;
        }
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        var row = connection.QuerySingleOrDefault<RoomDataRow>(RoomDataMapping.SelectRoom + "WHERE rooms.id = @roomId LIMIT 1", new { roomId });
        if (row != null && PlusEnvironment.Game.RoomManager.TryGetModel(row.ModelName, out var model))
        {
            data = Prepare(RoomDataMapping.CreateData(row, model, true));
            return true;
        }
        data = null;
        return false;
    }

    private static RoomData Prepare(RoomData data)
    {
        data.Promotion = RoomPromotionLoader.Load(PlusEnvironment.DatabaseManager, data.Id, TimeProvider.System);
        if (data.GroupId > 0 && PlusEnvironment.Game.GroupManager.TryGetGroup(data.GroupId, out var group)) data.Group = group;
        return data;
    }
}
