using Dapper;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Authentication;

namespace Plus.HabboHotel.Users.Inventory.Furniture;

public interface IInventoryClearStore
{
    void DeleteAll(int userId);
}

public sealed class InventoryClearStore(IDatabase database) : IInventoryClearStore
{
    public void DeleteAll(int userId)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        if (connection.ExecuteScalar<int?>("SELECT id FROM users WHERE id=@userId FOR UPDATE", new { userId }, transaction) == null) {
            throw new InvalidOperationException("Inventory owner no longer exists.");
        }

        connection.Execute("DELETE FROM items WHERE room_id=0 AND user_id=@userId", new { userId }, transaction);
        transaction.Commit();
    }
}

public interface IInventoryClearService
{
    bool TryClear(GameClient session, Room room);
}

public sealed class InventoryClearService(IInventoryClearStore store, IAccountSessionGate accounts) : IInventoryClearService
{
    public bool TryClear(GameClient session, Room room)
    {
        var habbo = session.GetHabbo();
        // Legacy placement and trade publication do not share these locks. Refuse an active trade;
        // the account gate and wallet lock serialize account-aware inventory and logout paths.
        using var account = accounts.Enter(habbo.Id);

        lock (habbo.WalletSync) {
            if (habbo.WalletClosed || !ReferenceEquals(habbo.Client, session) || !ReferenceEquals(habbo.CurrentRoom, room)) {
                return false;
            }

            var roomUser = room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);

            if (roomUser == null || roomUser.IsTrading) {
                return false;
            }

            store.DeleteAll(habbo.Id);
            habbo.Inventory.Furniture.ClearItems();
            session.Send(new FurniListUpdateComposer());
            session.SendNotification("Your inventory has been cleared!");

            return true;
        }
    }
}
