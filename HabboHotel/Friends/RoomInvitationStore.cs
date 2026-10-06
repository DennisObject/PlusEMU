using Dapper;
using Plus.Database;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Friends;

[Singleton]
public interface IRoomInvitationStore
{
    Task Log(int userId, string message, DateTimeOffset invitedAt);
}

public sealed class RoomInvitationStore(IDatabase database) : IRoomInvitationStore
{
    public async Task Log(int userId, string message, DateTimeOffset invitedAt)
    {
        using var connection = database.Connection();
        await connection.ExecuteAsync(
            "INSERT INTO chatlogs_console_invitations (user_id, message, timestamp) VALUES (@userId, @message, @invitedAt)",
            new { userId, message, invitedAt });
    }
}
