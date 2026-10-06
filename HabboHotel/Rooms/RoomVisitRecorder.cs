using Dapper;
using Plus.Database;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms;

[Singleton]
public interface IRoomVisitRecorder
{
    void RecordEntry(int userId, uint roomId);
}

public sealed class RoomVisitRecorder(IDatabase database, TimeProvider clock) : IRoomVisitRecorder
{
    public void RecordEntry(int userId, uint roomId)
    {
        var enteredAt = clock.GetUtcNow();
        using var connection = database.Connection();
        connection.Execute("""
            INSERT INTO user_roomvisits (user_id, room_id, entry_timestamp, exit_timestamp)
            VALUES (@userId, @roomId, @enteredAt, NULL)
            """, new { userId, roomId, enteredAt = enteredAt.UtcDateTime });
    }
}
