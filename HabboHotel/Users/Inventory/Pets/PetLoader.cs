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
            .Select(row => new Pet(row.Id, row.UserId, row.RoomId, row.Name, row.Type, row.Race, row.Color, row.Experience,
                row.Energy, row.Nutrition, row.Respect, row.CreateStamp, row.X, row.Y, row.Z, row.HaveSaddle, row.AnyoneRide,
                row.Hairdye, row.Pethair, row.GnomeClothing)).ToList();
    }

    private sealed record PetRow(int Id, int UserId, uint RoomId, string Name, int X, int Y, double Z, int Type, string Race,
        string Color, int Experience, int Energy, int Nutrition, int Respect, double CreateStamp, int HaveSaddle,
        int AnyoneRide, int Hairdye, int Pethair, string GnomeClothing);
}
