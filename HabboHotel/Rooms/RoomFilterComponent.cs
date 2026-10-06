using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Rooms;

internal interface IRoomFilterStore
{
    void Add(uint roomId, string word);
    void Remove(uint roomId, string word);
}

public sealed class RoomFilterComponent(IDatabase database) : IRoomComponent, IRoomFilterStore
{
    public int Order => 210;
    private Room _room = null!;
    public void Initiate(Room room)
    {
        _room = room;
        room.SetFilter(new(room, this));
    }
    public void Initiated()
    {
        using var connection = database.Connection();
        _room.WordFilterList = connection.Query<string>(
            "SELECT word FROM room_filter WHERE room_id = @roomId", new
            {
                roomId = _room.Id
            }).ToList();
    }

    void IRoomFilterStore.Add(uint roomId, string word)
    {
        using var connection = database.Connection();
        connection.Execute("INSERT INTO room_filter (room_id, word) VALUES (@roomId, @word)", new
        {
            roomId,
            word
        });
    }

    void IRoomFilterStore.Remove(uint roomId, string word)
    {
        using var connection = database.Connection();
        connection.Execute("DELETE FROM room_filter WHERE room_id = @roomId AND word = @word", new
        {
            roomId,
            word
        });
    }
}
