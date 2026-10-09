using Dapper;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Music;
using Plus.HabboHotel.Users.Authentication;

namespace Plus.HabboHotel.Users.Inventory.Furniture;

public interface IInventoryClearStore
{
    IReadOnlyList<InventoryItem> DeleteAll(int userId);
    InventoryItem? AvailableDisc(uint discId, uint ownerId);
}

public sealed class InventoryClearStore(IDatabase database, IItemDataManager definitions) : IInventoryClearStore
{
    public InventoryItem? AvailableDisc(uint discId, uint ownerId)
    {
        using var connection = database.Connection();

        return RoomMusicStore.ReadAvailableDisc(connection, definitions, discId, ownerId);
    }

    public IReadOnlyList<InventoryItem> DeleteAll(int userId)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        if (connection.ExecuteScalar<int?>("SELECT id FROM users WHERE id=@userId FOR UPDATE", new { userId }, transaction) == null) {
            throw new InvalidOperationException("Inventory owner no longer exists.");
        }

        // Capture the set before a deleted player's cascading links expose its discs again.
        var ids = connection.Query<uint>("""
            SELECT id FROM items WHERE room_id=0 AND user_id=@userId
                AND NOT EXISTS (SELECT 1 FROM room_music_playlist link WHERE link.disc_id=items.id)
            ORDER BY id FOR UPDATE
            """, new { userId }, transaction).ToArray();

        if (ids.Length == 0) {
            return [];
        }

        var discs = connection.Query<DiscRow>("""
            SELECT item.id,item.user_id AS OwnerId,item.base_item AS BaseItem,item.extra_data AS ExtraData,
                   item.limited_number AS LimitedNumber,item.limited_stack AS LimitedStack
            FROM room_music_playlist link JOIN items item ON item.id=link.disc_id
            WHERE link.player_id IN @ids AND item.room_id=0 FOR UPDATE
            """, new { ids }, transaction).ToArray();
        connection.Execute("DELETE FROM room_music_playlist WHERE player_id IN @ids", new { ids }, transaction);
        connection.Execute("DELETE FROM items WHERE id IN @ids AND room_id=0 AND user_id=@userId", new { ids, userId }, transaction);
        var map = definitions.Items;
        var released = discs.Where(disc => map.TryGetValue(disc.BaseItem, out var definition) && RoomMusicDefinition.IsDisc(definition))
            .Select(disc => RoomMusicDefinition.Inventory(disc.Id, disc.OwnerId, map[disc.BaseItem], disc.ExtraData, disc.LimitedNumber, disc.LimitedStack)).ToArray();
        transaction.Commit();

        return released;
    }
    private sealed class DiscRow
    {
        public uint Id { get; set; }
        public uint OwnerId { get; set; }
        public uint BaseItem { get; set; }
        public string ExtraData { get; set; } = "";
        public uint LimitedNumber { get; set; }
        public uint LimitedStack { get; set; }
    }
}

public interface IInventoryClearService
{
    bool TryClear(GameClient session, Room room);
}

public sealed class InventoryClearService(IInventoryClearStore store, IAccountSessionGate accounts,
    IGameClientManager clients) : IInventoryClearService
{
    public bool TryClear(GameClient session, Room room)
    {
        var habbo = session.GetHabbo();
        IReadOnlyList<InventoryItem> released;

        using (accounts.Enter(habbo.Id)) {
            lock (habbo.WalletSync) {
                // Clearing empties the loaded inventory together with storage, so nothing is deleted without one.
                if (habbo.AccessClosed || !ReferenceEquals(habbo.Client, session) || !ReferenceEquals(habbo.CurrentRoom, room)
                    || habbo.Inventory is not { } inventory) {
                    return false;
                }

                var roomUser = room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);

                if (roomUser == null || roomUser.IsTrading) {
                    return false;
                }

                released = store.DeleteAll(habbo.Id);
                inventory.Furniture.ClearItems();
            }
        }

        // A foreign disc keeps its original owner; resolve the current session after taking that owner's gate.
        foreach (var disc in released) {
            RoomMusicComponent.PublishReturned(disc, accounts, clients, store.AvailableDisc);
        }

        session.Send(new FurniListUpdateComposer());
        session.SendNotification("Your inventory has been cleared!");

        return true;
    }
}
