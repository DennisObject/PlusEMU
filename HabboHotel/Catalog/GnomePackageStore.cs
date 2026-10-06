using Microsoft.Extensions.Logging;
using Plus.Database;
using Plus.HabboHotel.Catalog.Utilities;
using Plus.HabboHotel.Rooms.AI;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Catalog;

public sealed record GnomePackageRequest(uint ItemId, uint BaseItem, int OwnerId, string OwnerName, uint RoomId,
    int X, int Y, double Z, string Name, string Clothing, DateTimeOffset CreatedAt);

[Singleton]
public interface IGnomePackageStore
{
    Pet? Open(GnomePackageRequest request);
}

public sealed class GnomePackageStore(IDatabase database, ILogger<GnomePackageStore> logger) : IGnomePackageStore
{
    public Pet? Open(GnomePackageRequest request)
    {
        try {
            using var connection = database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var pet = PetUtility.CreatePet(connection, transaction, request.CreatedAt, request.OwnerName,
                request.OwnerId, request.Name, 26, "30", "ffffff",
                new(request.ItemId, request.BaseItem, request.RoomId, request.X, request.Y, request.Z), request.Clothing);

            if (pet == null) {
                return null;
            }

            transaction.Commit();

            return pet;
        }
        catch (Exception exception) {
            logger.LogError(exception, "Could not open gnome package {ItemId} for user {OwnerId} in room {RoomId}",
                request.ItemId, request.OwnerId, request.RoomId);

            return null;
        }
    }
}
