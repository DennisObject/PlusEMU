using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.Chat.Commands.User;

internal class EjectAllCommand : IChatCommand
{
    private readonly IGameClientManager _gameClientManager;
    private readonly IDatabase _database;
    private readonly IRoomItemPickupService _pickup;
    public string Key => "ejectall";

    public string Parameters => "";

    public string Description => "Removes all of the items from the room.";

    public EjectAllCommand(IGameClientManager gameClientManager, IDatabase database, IRoomItemPickupService pickup)
    {
        _gameClientManager = gameClientManager;
        _database = database;
        _pickup = pickup;
    }

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        var habbo = session.GetHabbo();
        var owner = habbo.Id == room.OwnerId;

        if (owner && !room.CheckRights(session, true)) {
            return;
        }

        var items = room.GetRoomItemHandler().GetWallAndFloor.Where(item => !item.IsTemporary
            && (owner ? item.UserId != habbo.Id : item.UserId == habbo.Id)).ToArray();
        var handler = room.GetRoomItemHandler();

        if (!handler.TryReserveTransfers(items, out var transfer)) {
            return;
        }

        // Reserve the entire frozen set before the first music, reset, SQL or inventory side effect.
        try {
            room.GetWired()?.ResetRoomItems(items);
        }
        catch {
            handler.CancelUnstartedTransfer(transfer!);
            throw;
        }

        foreach (var player in items.Where(item => Music.RoomMusicDefinition.IsPlayer(item.Definition))) {
            if (!_pickup.TryPickUpReserved(session, player.Id, transfer!)) {
                return;
            }
        }

        var ordinary = items.Where(item => !Music.RoomMusicDefinition.IsPlayer(item.Definition)).ToArray();

        if (ordinary.Any(item => !handler.TransferIsCurrent(transfer!, item))) {
            return;
        }

        var requests = transfer!.Entries.Where(entry => ordinary.Contains(entry.Item)).Select(entry =>
            new RoomItemPickup(entry.ItemId, entry.RoomId, (int)entry.OwnerId, (int)entry.OwnerId,
                entry.Definition.InteractionType, false, entry.BaseItem)).ToArray();

        if (!handler.BeginTransferSql(transfer!, ordinary)) {
            return;
        }

        if (!new RoomItemPickupStore(_database).PickUpMany(requests)) {
            handler.CancelRolledBackTransfer(transfer!, ordinary);

            return;
        }

        foreach (var item in ordinary) {
            var receiver = _gameClientManager.GetClientByUserId(item.UserId);

            if (!handler.RemoveFurniture(receiver!, item, transfer)) {
                return;
            }

            item.RoomId = 0;

            if (receiver?.GetHabbo()?.Inventory is { } recipientInventory) {
                recipientInventory.Furniture.AddItem(item.ToInventoryItem());
                receiver.Send(new FurniListUpdateComposer());
            }

            handler.CompleteTransfer(transfer!, item);
        }
    }
}
