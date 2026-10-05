using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Users.Process;

public interface IUserProcessStore
{
    void ResetDailyRespects(int userId, int respects, int petRespects, string day);
}

public sealed class UserProcessStore(IDatabase database) : IUserProcessStore
{
    public void ResetDailyRespects(int userId, int respects, int petRespects, string day)
    {
        using var connection = database.Connection();
        connection.Execute("UPDATE `user_statistics` SET `dailyRespectPoints` = @respects, `dailyPetRespectPoints` = @petRespects, `respectsTimestamp` = @timestamp WHERE `id` = @id",
            new { respects, petRespects, timestamp = day, id = userId });
    }
}
