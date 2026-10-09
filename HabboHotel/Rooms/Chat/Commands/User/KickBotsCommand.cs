using Plus.HabboHotel.Users.Inventory.Bots;
using Plus.Communication.Packets.Outgoing.Inventory.Bots;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms.Chat.Commands.User;

internal class KickBotsCommand : IChatCommand
{
    private readonly IDatabase _database;
    public string Key => "kickbots";

    public string Parameters => "";

    public string Description => "Kick all of the bots from the room.";

    public KickBotsCommand(IDatabase database)
    {
        _database = database;
    }

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        if (!room.CheckRights(session, true)) {
            session.SendWhisper("Oops, only the room owner can run this command!");

            return;
        }

        // Kicked bots go into the owner's loaded inventory, so none are moved without one.
        if (session.GetHabbo().Inventory is not { } inventory) {
            return;
        }

        foreach (var user in room.GetRoomUserManager().GetUserList().ToList()) {
            if (user == null || user.IsPet || !user.IsBot || user.BotData.IsTemporary) {
                continue;
            }

            RoomUser? botUser = null;

            if (!room.GetRoomUserManager().TryGetBot(user.BotData.Id, out botUser) || botUser.BotData is not { } botData) {
                return;
            }

            using var connection = _database.Connection();
            connection.Execute("UPDATE bots SET room_id=0 WHERE id=@id LIMIT 1", new { user.BotData.Id });
            inventory.Bots.AddBot(new(Convert.ToInt32(botData.Id), Convert.ToInt32(botData.OwnerId), botData.Name, botData.Motto,
                botData.Look, botData.Gender));
            session.Send(new BotInventoryComposer(BotInventorySnapshot.Capture(inventory.Bots.Bots.Values)));
            room.GetRoomUserManager().RemoveBot(botUser.VirtualId, false);
        }

        session.SendWhisper("Success, removed all bots.");
    }
}
