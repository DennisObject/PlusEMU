using Dapper;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms;

public readonly record struct RoomItemPickup(uint ItemId, uint RoomId, int OwnerId, int RecipientId, InteractionType Type, bool MusicPlayer, uint BaseItem = 0);

[Singleton]
public interface IRoomItemPickupStore
{
    bool PickUp(RoomItemPickup request);
}

public sealed class RoomItemPickupStore(IDatabase database) : IRoomItemPickupStore
{
    private sealed class PickupRow
    {
        public uint RoomId { get; set; }
        public int OwnerId { get; set; }
        public uint BaseItem { get; set; }
    }

    internal bool PickUpMany(IReadOnlyList<RoomItemPickup> requests)
    {
        if (requests.Count == 0) {
            return true;
        }

        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        foreach (var request in requests.OrderBy(request => request.ItemId)) {
            var current = connection.QuerySingleOrDefault<PickupRow>(
                "SELECT room_id RoomId,user_id OwnerId,base_item BaseItem FROM items WHERE id=@ItemId FOR UPDATE", request, transaction);

            if (current == null || current.RoomId != request.RoomId || current.OwnerId != request.OwnerId
                || request.BaseItem != 0 && current.BaseItem != request.BaseItem) {
                return false;
            }
        }

        foreach (var request in requests) {
            if (connection.Execute("UPDATE items SET room_id=0,user_id=@RecipientId WHERE id=@ItemId AND room_id=@RoomId AND user_id=@OwnerId AND (@BaseItem=0 OR base_item=@BaseItem)", request, transaction) != 1) {
                return false;
            }
        }

        transaction.Commit();

        return true;
    }

    public bool PickUp(RoomItemPickup request)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        if (connection.Execute("""
                UPDATE items SET room_id = 0, user_id = @RecipientId, extra_data = IF(@MusicPlayer, '0', extra_data)
                WHERE id = @ItemId AND room_id = @RoomId AND user_id = @OwnerId AND (@BaseItem=0 OR base_item=@BaseItem)
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
