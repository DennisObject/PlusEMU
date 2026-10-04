using Dapper;

namespace Plus.HabboHotel.Items;

public static class ItemHopperFinder
{
    public static uint GetAHopper(uint curRoom)
    {
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        return connection.QuerySingleOrDefault<uint>(
            "SELECT room_id FROM items_hopper WHERE room_id<>@curRoom ORDER BY room_id ASC LIMIT 1", new { curRoom });
    }

    public static uint GetHopperId(uint nextRoom)
    {
        using var connection = PlusEnvironment.DatabaseManager.Connection();
        return connection.QuerySingleOrDefault<uint>("SELECT hopper_id FROM items_hopper WHERE room_id=@nextRoom LIMIT 1", new { nextRoom });
    }
}
