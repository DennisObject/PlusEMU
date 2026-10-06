using Plus.Database;
using Plus.HabboHotel.Rooms.AI;
using Dapper;

namespace Plus.HabboHotel.Users.Inventory.Pets;

internal class PetLoader : IPetLoader
{
    private readonly IDatabase _database;

    public PetLoader(IDatabase database)
    {
        _database = database;
    }
    public List<Pet> GetPetsForUser(int userId)
    {
        using var connection = _database.Connection();
        return connection.Query<PetRow>("SELECT b.`id`, b.`user_id` AS UserId, b.`room_id` AS RoomId, b.`name`, b.`x`, b.`y`, b.`z`, " +
                "p.`type`, p.`race`, p.`color`, p.`experience`, p.`energy`, p.`nutrition`, p.`respect`, p.`createstamp` AS CreateStamp, " +
                "p.`have_saddle` AS HaveSaddle, p.`anyone_ride` AS AnyoneRide, p.`hairdye`, p.`pethair`, p.`gnome_clothing` AS GnomeClothing " +
                "FROM `bots` b INNER JOIN `bots_petdata` p ON p.`id` = b.`id` WHERE b.`user_id` = @userId AND b.`room_id` = 0 AND b.`ai_type` = 'pet'",
                new { userId })
            .Select(row => new Pet(checked((int)row.Id), checked((int)row.UserId), row.RoomId, row.Name, checked((int)row.Type), row.Race, row.Color, row.Experience,
                row.Energy, row.Nutrition, row.Respect, row.CreateStamp, row.X, row.Y, row.Z, row.HaveSaddle, row.AnyoneRide,
                row.Hairdye, row.Pethair, row.GnomeClothing)).ToList();
    }

    private sealed class PetRow
    {
        public uint Id { get; set; }
        public uint UserId { get; set; }
        public uint RoomId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        public double Z { get; set; }
        public uint Type { get; set; }
        public string Race { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public int Experience { get; set; }
        public int Energy { get; set; }
        public int Nutrition { get; set; }
        public int Respect { get; set; }
        public double CreateStamp { get; set; }
        public int HaveSaddle { get; set; }
        public int AnyoneRide { get; set; }
        public int Hairdye { get; set; }
        public int Pethair { get; set; }
        public string GnomeClothing { get; set; } = string.Empty;
    }
}
