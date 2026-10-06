using Dapper;
using Microsoft.Extensions.Logging;
using Plus.Database;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms.AI;

public readonly record struct PetRoomMove(
    int PetId,
    int OwnerId,
    uint PreviousRoomId,
    uint RoomId,
    int X,
    int Y,
    double Z,
    int Experience,
    int Energy,
    int Nutrition,
    int Respect);

[Singleton]
public interface IPetRoomStore
{
    bool TryMove(PetRoomMove move);
}

public sealed class PetRoomStore(IDatabase database, ILogger<PetRoomStore> logger) : IPetRoomStore
{
    public bool TryMove(PetRoomMove move)
    {
        try {
            using var connection = database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            if (connection.Execute("""
                    UPDATE bots
                    SET room_id = @RoomId, x = @X, y = @Y, z = @Z
                    WHERE id = @PetId AND ai_type = 'pet' AND user_id = @OwnerId AND room_id = @PreviousRoomId
                    LIMIT 1
                    """, move, transaction) != 1) {
                return false;
            }

            if (connection.Execute("""
                    UPDATE bots_petdata
                    SET experience = @Experience, energy = @Energy, nutrition = @Nutrition, respect = @Respect
                    WHERE id = @PetId
                    LIMIT 1
                    """, move, transaction) != 1) {
                return false;
            }

            transaction.Commit();

            return true;
        }
        catch (Exception exception) {
            logger.LogError(exception, "Could not move pet {PetId} from room {PreviousRoomId} to room {RoomId}",
                move.PetId, move.PreviousRoomId, move.RoomId);

            return false;
        }
    }
}
