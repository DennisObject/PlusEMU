using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Items;

public interface IItemTravelStore
{
    uint FindOtherHopperRoom(uint roomId);
    uint FindHopper(uint roomId);
    uint FindLinkedTeleporter(uint itemId);
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
