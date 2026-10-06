using Dapper;
using Plus.Database;
using Plus.Utilities;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms;

[Singleton]
public interface IRoomPromotionLoader
{
    RoomPromotion? Load(uint roomId);
}

public sealed class RoomPromotionLoader(IDatabase database) : IRoomPromotionLoader
{
    public RoomPromotion? Load(uint roomId) => Load(database, roomId);

    internal static RoomPromotion? Load(IDatabase database, uint roomId)
    {
        using var connection = database.Connection();
        var row = connection.QuerySingleOrDefault<PromotionRow>("""
            SELECT title, description, timestamp_start AS StartsAt, timestamp_expire AS ExpiresAt, category_id AS CategoryId
            FROM room_promotions WHERE room_id = @roomId AND timestamp_expire > UNIX_TIMESTAMP() LIMIT 1
            """, new { roomId });
        return row == null ? null : new(row.Title, row.Description, row.StartsAt, row.ExpiresAt, row.CategoryId);
    }

    private sealed record PromotionRow(string Title, string Description, double StartsAt, double ExpiresAt, int CategoryId);
}

public sealed class RoomPromotionComponent(IRoomPromotionLoader loader) : IRoomComponent
{
    private Room _room = null!;
    public int Order => 120;
    public void Initiate(Room room) => _room = room;
    public void Initiated() => _room.Promotion = loader.Load(_room.Id);
}
