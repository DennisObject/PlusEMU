using Dapper;
using Plus.Database;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms;

public readonly record struct RoomPetSave(int Id, int OwnerId, uint RoomId, string Name, int Type, string Race, string Color,
    DateTimeOffset? CreatedAt, int X, int Y, double Z, int Experience, int Energy, int Nutrition, int Respect, bool Insert);
public readonly record struct RoomBotSave(int Id, int X, int Y, double Z, string Name, string Look, int Rotation);

[Scoped]
public interface IRoomUserStore
{
    void UpdateUserCount(uint roomId, int count);
    void SavePet(RoomPetSave pet);
    void SaveBot(RoomBotSave bot);
    void RecordExit(uint roomId, int userId, double exitTimestamp, int usersNow);
}

public sealed class RoomUserStore(IDatabase database) : IRoomUserStore
{
    public void UpdateUserCount(uint roomId, int count)
    {
        using var connection = database.Connection();
        connection.Execute("UPDATE rooms SET users_now = @count WHERE id = @roomId LIMIT 1", new { count, roomId });
    }

    public void SavePet(RoomPetSave pet)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (pet.Insert)
        {
            connection.Execute("""
                INSERT INTO bots (id, user_id, room_id, ai_type, name, motto, look, x, y, z, rotation, walk_mode,
                                  automatic_chat, speaking_interval, mix_sentences, chat_bubble)
                VALUES (@Id, @OwnerId, @RoomId, 'pet', @Name, '', '', 0, 0, 0, 0, 'freeroam', FALSE, 0, FALSE, 0)
                """, pet, transaction);
            connection.Execute("""
                INSERT INTO bots_petdata (id, type, race, color, experience, energy, createstamp, nutrition, respect,
                                          have_saddle, anyone_ride, hairdye, pethair, gnome_clothing)
                VALUES (@Id, @Type, @Race, @Color, 0, 100, @CreatedAt, 0, 0, 0, 0, 1, -1, '-1')
                """, new
                {
                    pet.Id, pet.Type, pet.Race, pet.Color,
                    CreatedAt = pet.CreatedAt?.UtcDateTime
                }, transaction);
            transaction.Commit();
            return;
        }
        connection.Execute("UPDATE bots SET room_id = @RoomId, x = @X, y = @Y, z = @Z WHERE id = @Id LIMIT 1", pet, transaction);
        connection.Execute("""
            UPDATE bots_petdata SET experience = @Experience, energy = @Energy, nutrition = @Nutrition, respect = @Respect
            WHERE id = @Id LIMIT 1
            """, pet, transaction);
        transaction.Commit();
    }

    public void SaveBot(RoomBotSave bot)
    {
        using var connection = database.Connection();
        connection.Execute("""
            UPDATE bots SET x = @X, y = @Y, z = @Z, name = @Name, look = @Look, rotation = @Rotation
            WHERE id = @Id LIMIT 1
            """, bot);
    }

    public void RecordExit(uint roomId, int userId, double exitTimestamp, int usersNow)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("""
            UPDATE user_roomvisits SET exit_timestamp = @exitTimestamp
            WHERE room_id = @roomId AND user_id = @userId ORDER BY exit_timestamp DESC LIMIT 1
            """, new { exitTimestamp, roomId, userId }, transaction);
        connection.Execute("UPDATE rooms SET users_now = @usersNow WHERE id = @roomId LIMIT 1", new { usersNow, roomId }, transaction);
        transaction.Commit();
    }
}
