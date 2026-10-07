using Dapper;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms;

public readonly record struct RoomItemPickup(uint ItemId, uint RoomId, int OwnerId, int RecipientId, InteractionType Type, bool MusicPlayer);

[Singleton]
public interface IRoomItemPickupStore
{
    bool PickUp(RoomItemPickup request);
}

public sealed class RoomItemPickupStore(IDatabase database) : IRoomItemPickupStore
{
    public bool PickUp(RoomItemPickup request)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        if (connection.Execute("""
                UPDATE items SET room_id = 0, user_id = @RecipientId, extra_data = IF(@MusicPlayer, '0', extra_data)
                WHERE id = @ItemId AND room_id = @RoomId AND user_id = @OwnerId
                LIMIT 1
                """, request, transaction) != 1) {
            return false;
        }

        if (request.Type == InteractionType.Moodlight) {
            connection.Execute("DELETE FROM room_items_moodlight WHERE item_id = @ItemId LIMIT 1", request, transaction);
        }
        else if (request.Type == InteractionType.Toner) {
            connection.Execute("DELETE FROM room_items_toner WHERE id = @ItemId LIMIT 1", request, transaction);
        }

        if (request.MusicPlayer) {
            connection.Execute("UPDATE room_music_players SET started_at=NULL,version=version+1 WHERE item_id=@ItemId", request, transaction);
        }

        transaction.Commit();

        return true;
    }
}
