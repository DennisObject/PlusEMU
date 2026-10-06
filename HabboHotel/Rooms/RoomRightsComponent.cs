using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Rooms;

public sealed class RoomRightsComponent(IDatabase database) : IRoomComponent
{
    public int Order => 200;
    private Room _room = null!;
    public void Initiate(Room room) => _room = room;
    public void Initiated()
    {
        _room.UsersWithRights = [];

        if (_room.Group != null) {
            return;
        }

        using var connection = database.Connection();
        _room.UsersWithRights.AddRange(connection.Query<int>(
            "SELECT user_id FROM room_rights WHERE room_id = @roomId", new { roomId = _room.Id }));
    }
}
