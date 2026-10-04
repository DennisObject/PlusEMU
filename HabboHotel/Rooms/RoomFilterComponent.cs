using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Rooms;

public sealed class RoomFilterComponent(IDatabase database) : IRoomComponent
{
    private Room _room = null!;
    public void Initiate(Room room) => _room = room;
    public void Initiated()
    {
        using var connection = database.Connection();
        _room.WordFilterList = connection.Query<string>(
            "SELECT word FROM room_filter WHERE room_id = @roomId", new { roomId = _room.Id }).ToList();
    }
}
