using Dapper;
using Plus.Database;
using Plus.HabboHotel.Rooms.Instance;

namespace Plus.HabboHotel.Rooms;

internal interface ITradeStore
{
    void DeleteItem(uint itemId);
    void TransferItem(uint itemId, int userId);
    void Log(int firstUserId, int secondUserId, string firstItems, string secondItems);
}

public sealed class RoomTradingComponent(IDatabase database) : IRoomComponent, ITradeStore
{
    public int Order => 10;
    public void Initiate(Room room)
    {
        room.SetTrading(new TradingComponent(room, this));
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
        connection.Execute("""
            INSERT INTO logs_client_trade (`1id`, `2id`, `1items`, `2items`, `timestamp`)
            VALUES (@firstUserId, @secondUserId, @firstItems, @secondItems, UNIX_TIMESTAMP())
            """, new { firstUserId, secondUserId, firstItems, secondItems });
    }
}
