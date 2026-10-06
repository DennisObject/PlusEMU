using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Friends;

public interface IHabbiconMessengerStore
{
    // Writes the console audit row and, for an offline recipient, the offline row in one transaction.
    int Record(int senderId, int recipientId, string fallback, DateTime createdAtUtc, bool deliverOffline);
}

public sealed class HabbiconMessengerStore(IDatabase database) : IHabbiconMessengerStore
{
    public int Record(int senderId, int recipientId, string fallback, DateTime createdAtUtc, bool deliverOffline)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("INSERT INTO chatlogs_console (from_id, to_id, message, timestamp) VALUES (@senderId, @recipientId, @fallback, @createdAtUtc)",
            new { senderId, recipientId, fallback, createdAtUtc }, transaction);
        int messageId = connection.QuerySingle<int>("SELECT LAST_INSERT_ID()", transaction: transaction);

        if (deliverOffline) {
            connection.Execute("INSERT INTO messenger_offline_messages (from_id, to_id, message, timestamp) VALUES (@senderId, @recipientId, @fallback, @createdAtUtc)",
                new { senderId, recipientId, fallback, createdAtUtc }, transaction);
        }

        transaction.Commit();

        return messageId;
    }
}
