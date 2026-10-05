using Dapper;
using System.Data;
using Plus.Database;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;

namespace Plus.HabboHotel.Users.Inventory.Pets;

internal sealed class PetLoader(IDatabase database) : IPetLoader
{
    public List<Pet> GetPetsForUser(int userId)
    {
        using var connection = database.Connection();
        return Load(connection, userId).Select(row => new Pet(row.Id, row.UserId, row.RoomId, row.Name, row.Type, row.Race,
                row.Color, row.Experience, row.Energy, row.Nutrition, row.Respect, row.CreatedAt,
                row.X, row.Y, row.Z, row.HaveSaddle, row.AnyoneRide, row.Hairdye, row.Pethair, row.GnomeClothing, row.OwnerName)).ToList();
    }

    internal static IEnumerable<PetRow> Load(IDbConnection connection, int userId) => connection.Query<PetRow>("""
            SELECT bots.id, bots.user_id AS UserId, COALESCE(owner.username, '') AS OwnerName, bots.room_id AS RoomId, bots.name, bots.x, bots.y, bots.z,
                   pet.type, pet.race, pet.color, pet.experience, pet.energy, pet.nutrition, pet.respect,
                   pet.createstamp AS CreatedAt, pet.have_saddle AS HaveSaddle, pet.anyone_ride AS AnyoneRide,
                   pet.hairdye, pet.pethair, pet.gnome_clothing AS GnomeClothing
            FROM bots JOIN bots_petdata pet ON pet.id = bots.id
            LEFT JOIN users owner ON owner.id = bots.user_id
            WHERE bots.user_id = @userId AND bots.room_id = 0 AND bots.ai_type = 'pet'
            """, new { userId });

    internal sealed class PetRow
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string OwnerName { get; set; } = "";
        public uint RoomId { get; set; }
        public string Name { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public double Z { get; set; }
        public int Type { get; set; }
        public string Race { get; set; } = "";
        public string Color { get; set; } = "";
        public int Experience { get; set; }
        public int Energy { get; set; }
        public int Nutrition { get; set; }
        public int Respect { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public int HaveSaddle { get; set; }
        public int AnyoneRide { get; set; }
        public int Hairdye { get; set; }
        public int Pethair { get; set; }
        public string GnomeClothing { get; set; } = "";
    }
}
