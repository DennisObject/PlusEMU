using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Quests;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms;

[Singleton]
public interface IRoomItemPickupService
{
    Task PickUp(GameClient session, uint itemId);
    bool TryPickUp(GameClient session, uint itemId);
    bool TryPickUpReserved(GameClient session, uint itemId, RoomItemTransfer transfer) => false;
}

public sealed class RoomItemPickupService(
    IGameClientManager clients,
    IQuestManager quests,
    IRoomItemPickupStore store) : IRoomItemPickupService
{
    public Task PickUp(GameClient session, uint itemId)
    {
        TryPickUp(session, itemId);

        return Task.CompletedTask;
    }

    public bool TryPickUp(GameClient session, uint itemId)
    {
        var room = session.GetHabbo()?.CurrentRoom;
        var item = room?.GetRoomItemHandler().GetItem(itemId);

        if (session.GetHabbo()?.AccessClosed != false || room == null || item == null || !room.GetRoomItemHandler().TryReserveTransfers([item], out var transfer)) {
            return false;
        }

        return TryPickUpReserved(session, itemId, transfer!);
    }

    public bool TryPickUpReserved(GameClient session, uint itemId, RoomItemTransfer transfer)
    {
        var habbo = session.GetHabbo();
        var room = habbo?.CurrentRoom;
        var item = room?.GetRoomItemHandler().GetItem(itemId);

        if (habbo == null || habbo.AccessClosed || room == null || item == null) {
            transfer.Owner.CancelUnstartedTransfer(transfer);

            return false;
        }

        var handler = room.GetRoomItemHandler();

        if (!handler.TransferIsCurrent(transfer, item)) {
            return false;
        }

        lock (item) {
            if (!ReferenceEquals(habbo.CurrentRoom, room) ||
                !ReferenceEquals(room.GetRoomItemHandler().GetItem(itemId), item) || !handler.TransferIsCurrent(transfer, item) || item.IsTemporary ||
                item.RoomId != room.Id || item.OwnerId is 0 or > int.MaxValue ||
                item.Definition.InteractionType == InteractionType.Postit) {
                handler.CancelUnstartedTransfer(transfer);

                return false;
            }

            var ownerId = (int)item.OwnerId;
            var mayTake = habbo.Access.Can(PermissionKeys.RoomItemTake);

            if (ownerId != habbo.Id && !mayTake && !room.CheckRights(session, false) &&
                !(room.Group != null && room.CheckRights(session, false, true))) {
                handler.CancelUnstartedTransfer(transfer);

                return false;
            }

            var receiver = ownerId == habbo.Id || mayTake ? session : clients.GetClientByUserId(ownerId);
            var receiverHabbo = receiver?.GetHabbo();
            var recipientId = mayTake ? habbo.Id : ownerId;
            var inventoryItem = item.ToInventoryItem();
            inventoryItem.OwnerId = (uint)recipientId;

            if (!handler.BeginTransferSql(transfer, [item])) {
                return false;
            }

            if (!store.PickUp(new(item.Id, room.Id, ownerId, recipientId, item.Definition.InteractionType, Music.RoomMusicDefinition.IsPlayer(item.Definition), transfer.Entries.First(entry => ReferenceEquals(entry.Item, item)).BaseItem))) {
                handler.CancelRolledBackTransfer(transfer, [item]);

                return false;
            }

            if (Music.RoomMusicDefinition.IsPlayer(item.Definition)) {
                item.LegacyDataString = "0";
                inventoryItem.ExtraData = item.ExtraData;
            }

            if (item.Definition.InteractionType is InteractionType.Tent or InteractionType.TentSmall) {
                room.RemoveTent(item.Id);
            }

            if (!handler.RemoveFurniture(receiverHabbo == null ? null! : receiver!, item, transfer)) {
                return false;
            }

            item.RoomId = 0;

            if (receiverHabbo?.Inventory is { } recipientInventory) {
                // The pickup is already stored; an inventory that is not loaded picks the item up later.
                recipientInventory.Furniture.AddItem(inventoryItem);
                receiver!.Send(new FurniListUpdateComposer());
            }

            handler.CompleteTransfer(transfer, item);
            quests.ProgressUserQuest(session, QuestType.FurniPick);

            return true;
        }
    }
}
