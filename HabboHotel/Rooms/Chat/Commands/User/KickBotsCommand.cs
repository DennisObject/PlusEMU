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
        if (!room.CheckRights(session, true))
        {
            session.SendWhisper("Oops, only the room owner can run this command!");

            return;
        }

        foreach (var user in room.GetRoomUserManager().GetUserList().ToList())
        {
            if (user == null || user.IsPet || !user.IsBot || user.BotData.IsTemporary)
            {
                continue;
            }

            RoomUser? botUser = null;

            if (!room.GetRoomUserManager().TryGetBot(user.BotData.Id, out botUser))
            {
                return;
            }

            using var connection = _database.Connection();
            connection.Execute("UPDATE bots SET room_id=0 WHERE id=@id LIMIT 1", new
            {
                user.BotData.Id
            });
            session.GetHabbo().Inventory.Bots.AddBot(new(Convert.ToInt32(botUser.BotData.Id), Convert.ToInt32(botUser.BotData.OwnerId), botUser.BotData.Name, botUser.BotData.Motto,
                botUser.BotData.Look, botUser.BotData.Gender));
            session.Send(new BotInventoryComposer(BotInventorySnapshot.Capture(session.GetHabbo().Inventory.Bots.Bots.Values)));
            room.GetRoomUserManager().RemoveBot(botUser.VirtualId, false);
        }

        session.SendWhisper("Success, removed all bots.");
    }
}
