using Dapper;
using Plus.Database;
using Plus.Core.Settings;
using Plus.HabboHotel.Rooms.Instance;

namespace Plus.HabboHotel.Rooms;

internal interface ITradeStore
{
    void DeleteItem(uint itemId);
    void TransferItem(uint itemId, int userId);
    void Log(int firstUserId, int secondUserId, string firstItems, string secondItems);
    bool Commit(IReadOnlyList<TradeTransfer> transfers, int firstUserId, int secondUserId, int firstCredits, int secondCredits,
        string firstItems, string secondItems, Func<bool> apply);
}

internal sealed record TradeTransfer(uint ItemId, int SenderId, int RecipientId, bool Redeem);

public sealed class RoomTradingComponent(IDatabase database, TimeProvider clock, ISettingsManager settings) : IRoomComponent, ITradeStore
{
    public int Order => 10;
    public void Initiate(Room room)
    {
        room.SetTrading(new TradingComponent(room, this, settings));
    }
    public void Initiated() { }

    void ITradeStore.DeleteItem(uint itemId)
    {
        using var connection = database.Connection();
        connection.Execute("DELETE FROM items WHERE id = @itemId LIMIT 1", new { itemId });
    }

    void ITradeStore.TransferItem(uint itemId, int userId)
    {
        using var connection = database.Connection();
        connection.Execute("UPDATE items SET user_id = @userId WHERE id = @itemId LIMIT 1", new { userId, itemId });
    }

    void ITradeStore.Log(int firstUserId, int secondUserId, string firstItems, string secondItems)
    {
        using var connection = database.Connection();
        var now = clock.GetUtcNow();
        connection.Execute("""
            INSERT INTO logs_client_trade (`1id`, `2id`, `1items`, `2items`, `timestamp`)
            VALUES (@firstUserId, @secondUserId, @firstItems, @secondItems, @createdAt)
            """, new { firstUserId, secondUserId, firstItems, secondItems, createdAt = now.UtcDateTime });
    }

    bool ITradeStore.Commit(IReadOnlyList<TradeTransfer> transfers, int firstUserId, int secondUserId, int firstCredits, int secondCredits,
        string firstItems, string secondItems, Func<bool> apply)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        try {
            foreach (var transfer in transfers) {
                var changed = transfer.Redeem
                    ? connection.Execute("DELETE FROM items WHERE id = @ItemId AND user_id = @SenderId LIMIT 1", transfer, transaction)
                    : connection.Execute("UPDATE items SET user_id = @RecipientId WHERE id = @ItemId AND user_id = @SenderId LIMIT 1", transfer, transaction);
                if (changed != 1) {
                    transaction.Rollback();
                    return false;
                }
            }

            if (connection.Execute("UPDATE users SET credits = @firstCredits WHERE id = @firstUserId LIMIT 1", new { firstCredits, firstUserId }, transaction) != 1 ||
                connection.Execute("UPDATE users SET credits = @secondCredits WHERE id = @secondUserId LIMIT 1", new { secondCredits, secondUserId }, transaction) != 1) {
                transaction.Rollback();
                return false;
            }

            var now = clock.GetUtcNow();
            if (connection.Execute("""
                INSERT INTO logs_client_trade (`1id`, `2id`, `1items`, `2items`, `timestamp`)
                VALUES (@firstUserId, @secondUserId, @firstItems, @secondItems, @createdAt)
                """, new { firstUserId, secondUserId, firstItems, secondItems, createdAt = now.UtcDateTime }, transaction) != 1 || !apply()) {
                transaction.Rollback();
                return false;
            }

            transaction.Commit();
            return true;
        }
        catch {
            transaction.Rollback();
            throw;
        }
    }
}
