using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms.Chat.Commands.User;

internal class PickAllCommand : IChatCommand
{
    private readonly IDatabase _database;
    private readonly IRoomItemPickupService _pickup;
    public string Key => "pickall";

    public string Parameters => "";

    public string Description => "Picks up all of the furniture from your room.";

    public PickAllCommand(IDatabase database, IRoomItemPickupService pickup)
    {
        _database = database;
        _pickup = pickup;
    }

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        // Everything picked up lands in the loaded inventory, so neither memory nor storage moves without one.
        if (!room.CheckRights(session, true) || session.GetHabbo().Inventory == null) {
            return;
        }

        foreach (var player in room.GetRoomItemHandler().GetFloor.Where(item => item.UserId == session.GetHabbo().Id
                     && Music.RoomMusicDefinition.IsPlayer(item.Definition)).ToArray()) {
            if (!_pickup.TryPickUp(session, player.Id)) {
                return;
            }
        }

        if (room.GetRoomItemHandler().GetFloor.Any(item => Music.RoomMusicDefinition.IsPlayer(item.Definition) && item.UserId == session.GetHabbo().Id)) {
            return;
        }

        room.GetRoomItemHandler().RemoveItems(session);
        room.GetGameMap().GenerateMaps();
        using var connection = _database.Connection();
        connection.Execute("UPDATE items SET room_id=0 WHERE room_id=@roomId AND user_id=@userId",
            new { roomId = room.Id, userId = session.GetHabbo().Id });
        var items = room.GetRoomItemHandler().GetWallAndFloor.ToList();

        if (items.Count > 0) {
            session.SendWhisper("There are still more items in this room, manually remove them or use :ejectall to eject them!");
        }

        session.Send(new FurniListUpdateComposer());
    }
}
