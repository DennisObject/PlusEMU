using Dapper;
using Plus.Database;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.AI.Speech;

namespace Plus.HabboHotel.Rooms;

public sealed class RoomPetsComponent(IDatabase database) : IRoomComponent
{
    public int Order => 310;
    private Room _room = null!;
    public void Initiate(Room room) => _room = room;
    public void Initiated()
    {
        using var connection = database.Connection();
        foreach (var row in Load(connection, _room.Id))
        {
            var data = LoadData(connection, row.Id);
            if (data == null) continue;
            var pet = new Pet(row.Id, row.UserId, row.RoomId, row.Name, data.Type, data.Race, data.Color,
                data.Experience, data.Energy, data.Nutrition, data.Respect, data.CreatedAt, row.X, row.Y, row.Z,
                data.HaveSaddle, data.AnyoneRide, data.Hairdye, data.Pethair, data.GnomeClothing, row.OwnerName);
            var speeches = new List<RandomSpeech>();
            _room.GetRoomUserManager().DeployBot(new(pet.PetId, _room.Id, "pet", "freeroam", pet.Name, "", pet.Look,
                pet.X, pet.Y, Convert.ToInt32(pet.Z), 0, 0, 0, 0, 0, ref speeches, "", 0, pet.OwnerId, false, 0, false, 0), pet);
        }
    }

    internal static IEnumerable<PetLocation> Load(System.Data.IDbConnection connection, uint roomId) => connection.Query<PetLocation>(
        "SELECT b.id, b.user_id AS UserId, b.room_id AS RoomId, b.name, b.x, b.y, b.z, COALESCE(owner.username, '') AS OwnerName FROM bots b LEFT JOIN users owner ON owner.id = b.user_id WHERE b.room_id = @roomId AND b.ai_type = 'pet'", new { roomId });

    internal static PetData? LoadData(System.Data.IDbConnection connection, int petId) => connection.QuerySingleOrDefault<PetData>("""
        SELECT type, race, color, experience, energy, nutrition, respect, createstamp AS CreatedAt,
               have_saddle AS HaveSaddle, anyone_ride AS AnyoneRide, hairdye, pethair,
               gnome_clothing AS GnomeClothing
        FROM bots_petdata WHERE id = @petId LIMIT 1
        """, new { petId });

    internal sealed record PetLocation(int Id, int UserId, uint RoomId, string Name, int X, int Y, double Z, string OwnerName);
    internal sealed record PetData(int Type, string Race, string Color, int Experience, int Energy, int Nutrition,
        int Respect, DateTimeOffset? CreatedAt, int HaveSaddle, int AnyoneRide, int Hairdye, int Pethair, string GnomeClothing);
}
