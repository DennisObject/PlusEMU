using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Dapper;
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
        if (session.GetHabbo().Id == room.OwnerId) {
            //Let us check anyway.
            if (!room.CheckRights(session, true)) {
                return;
            }

            foreach (var item in room.GetRoomItemHandler().GetWallAndFloor.ToList()) {
                if (item == null || item.UserId == session.GetHabbo().Id) {
                    continue;
                }

                if (Music.RoomMusicDefinition.IsPlayer(item.Definition)) {
                    _pickup.TryPickUp(session, item.Id);
                    continue;
                }

                var targetClient = _gameClientManager.GetClientByUserId(item.UserId);

                if (targetClient != null && targetClient.GetHabbo() != null) {
                    room.GetRoomItemHandler().RemoveFurniture(targetClient, item.Id);
                    targetClient.GetHabbo().Inventory.Furniture.AddItem(item.ToInventoryItem());
                    targetClient.Send(new FurniListUpdateComposer());
                }
                else {
                    room.GetRoomItemHandler().RemoveFurniture(null, item.Id);
                    using var connection = _database.Connection();
                    connection.Execute("UPDATE items SET room_id=0 WHERE id=@id LIMIT 1", new { item.Id });
                }
            }
        }
        else {
            foreach (var item in room.GetRoomItemHandler().GetWallAndFloor.ToList()) {
                if (item == null || item.UserId != session.GetHabbo().Id) {
                    continue;
                }

                if (Music.RoomMusicDefinition.IsPlayer(item.Definition)) {
                    _pickup.TryPickUp(session, item.Id);
                    continue;
                }

                var targetClient = _gameClientManager.GetClientByUserId(item.UserId);

                if (targetClient != null && targetClient.GetHabbo() != null) {
                    room.GetRoomItemHandler().RemoveFurniture(targetClient, item.Id);
                    targetClient.GetHabbo().Inventory.Furniture.AddItem(item.ToInventoryItem());
                    targetClient.Send(new FurniListUpdateComposer());
                }
                else {
                    room.GetRoomItemHandler().RemoveFurniture(null, item.Id);
                    using var connection = _database.Connection();
                    connection.Execute("UPDATE items SET room_id=0 WHERE id=@id LIMIT 1", new { item.Id });
                }
            }
        }
    }
}
