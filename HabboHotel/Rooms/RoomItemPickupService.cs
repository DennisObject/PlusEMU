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
}

public sealed class RoomItemPickupService(
    IGameClientManager clients,
    IQuestManager quests,
    IRoomItemPickupStore store) : IRoomItemPickupService
{
    public Task PickUp(GameClient session, uint itemId)
    {
        var habbo = session.GetHabbo();
        var room = habbo.CurrentRoom;

        if (room == null) {
            return Task.CompletedTask;
        }

        var item = room.GetRoomItemHandler().GetItem(itemId);

        if (item == null) {
            return Task.CompletedTask;
        }

        lock (item) {
            if (!ReferenceEquals(habbo.CurrentRoom, room) ||
                !ReferenceEquals(room.GetRoomItemHandler().GetItem(itemId), item) || item.IsTemporary ||
                item.RoomId != room.Id || item.OwnerId is 0 or > int.MaxValue ||
                item.Definition.InteractionType == InteractionType.Postit) {
                return Task.CompletedTask;
            }

            var ownerId = (int)item.OwnerId;
            var mayTake = habbo.Access.Can(PermissionKeys.RoomItemTake);

            if (ownerId != habbo.Id && !mayTake && !room.CheckRights(session, false) &&
                !(room.Group != null && room.CheckRights(session, false, true))) {
                return Task.CompletedTask;
            }

            var receiver = ownerId == habbo.Id || mayTake ? session : clients.GetClientByUserId(ownerId);
            var receiverHabbo = receiver?.GetHabbo();
            var recipientId = mayTake ? habbo.Id : ownerId;
            var inventoryItem = item.ToInventoryItem();
            inventoryItem.OwnerId = (uint)recipientId;

            if (!store.PickUp(new(item.Id, room.Id, ownerId, recipientId, item.Definition.InteractionType))) {
                return Task.CompletedTask;
            }

            if (item.Definition.InteractionType is InteractionType.Tent or InteractionType.TentSmall) {
                room.RemoveTent(item.Id);
            }

            room.GetRoomItemHandler().RemoveFurniture(receiverHabbo == null ? null! : receiver!, item.Id);
            item.RoomId = 0;

            if (receiverHabbo != null) {
                receiverHabbo.Inventory.Furniture.AddItem(inventoryItem);
                receiver!.Send(new FurniListUpdateComposer());
            }

            quests.ProgressUserQuest(session, QuestType.FurniPick);

            return Task.CompletedTask;
        }
    }
}
