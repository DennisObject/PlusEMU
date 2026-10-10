using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Items;

public interface IItemTravelStore
{
    uint FindOtherHopperRoom(uint roomId);
    uint FindHopper(uint roomId);
    uint FindLinkedTeleporter(uint itemId);
    bool SetLinkedTeleporter(uint itemId, uint roomId, uint targetId) => false;
    uint FindItemRoom(uint itemId);
    void RegisterHopper(uint itemId, uint roomId);
    void RemoveHopper(uint itemId, uint roomId);
}

public sealed class ItemTravelStore(IDatabase database) : IItemTravelStore
{
    public uint FindOtherHopperRoom(uint roomId)
    {
        using var connection = database.Connection();

        return connection.QuerySingleOrDefault<uint>(
            "SELECT room_id FROM items_hopper WHERE room_id<>@curRoom ORDER BY room_id ASC LIMIT 1", new { curRoom = roomId });
    }

    public uint FindHopper(uint roomId)
    {
        using var connection = database.Connection();

        return connection.QuerySingleOrDefault<uint>("SELECT hopper_id FROM items_hopper WHERE room_id=@nextRoom LIMIT 1", new { nextRoom = roomId });
    }

    public uint FindLinkedTeleporter(uint itemId)
    {
        using var connection = database.Connection();

        return connection.QuerySingleOrDefault<uint>(
            "SELECT tele_two_id FROM room_items_tele_links WHERE tele_one_id=@teleId LIMIT 1", new { teleId = itemId });
    }

    public bool SetLinkedTeleporter(uint itemId, uint roomId, uint targetId)
    {
        if (itemId == 0 || roomId == 0 || targetId == 0) {
            return false;
        }

        using var connection = database.Connection();

        if (connection.State != System.Data.ConnectionState.Open) {
            connection.Open();
        }

        using var transaction = connection.BeginTransaction();

        if (connection.QuerySingleOrDefault<uint>("SELECT id FROM items WHERE id=@itemId AND room_id=@roomId FOR UPDATE",
            new { itemId, roomId }, transaction) == 0) {
            return false;
        }

        var exists = connection.Query<uint>("SELECT tele_one_id FROM room_items_tele_links WHERE tele_one_id=@itemId FOR UPDATE",
            new { itemId }, transaction).Any();

        if (exists) {
            connection.Execute("UPDATE room_items_tele_links SET tele_two_id=@targetId WHERE tele_one_id=@itemId",
                new { itemId, targetId }, transaction);
        }
        else {
            connection.Execute("INSERT INTO room_items_tele_links (tele_one_id,tele_two_id) VALUES (@itemId,@targetId)",
                new { itemId, targetId }, transaction);
        }

        transaction.Commit();

        return true;
    }

    public uint FindItemRoom(uint itemId)
    {
        using var connection = database.Connection();

        return connection.QuerySingleOrDefault<uint>("SELECT room_id FROM items WHERE id=@teleId LIMIT 1", new { teleId = itemId });
    }

    public void RegisterHopper(uint itemId, uint roomId)
    {
        using var connection = database.Connection();
        connection.Execute("INSERT INTO items_hopper (hopper_id,room_id) VALUES (@id,@roomId)", new { id = itemId, roomId });
    }

    public void RemoveHopper(uint itemId, uint roomId)
    {
        using var connection = database.Connection();
        connection.Execute("DELETE FROM items_hopper WHERE hopper_id=@id AND room_id=@roomId LIMIT 1", new { id = itemId, roomId });
    }
}
