using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Database;
using Dapper;
using Plus.HabboHotel.GameClients;

using Plus.HabboHotel.Rooms;

namespace Plus.Communication.RCON.Commands.User;

internal class ReloadUserMottoCommand : IRconCommand
{
    private readonly IDatabase _database;
    private readonly IGameClientManager _gameClientManager;
    public string Description => "This command is used to reload the users motto from the database.";

    public string Key => "reload_user_motto";
    public string Parameters => "%userId%";

    public ReloadUserMottoCommand(IDatabase database, IGameClientManager gameClientManager)
    {
        _database = database;
        _gameClientManager = gameClientManager;
    }

    public Task<bool> TryExecute(string[] parameters)
    {
        if (!int.TryParse(parameters[0], out var userId))
            return Task.FromResult(false);
        var client = _gameClientManager.GetClientByUserId(userId);
        if (client == null || client.GetHabbo() == null)
            return Task.FromResult(false);
        using var connection = _database.Connection();
        var motto = connection.QuerySingleOrDefault<string?>("SELECT `motto` FROM `users` WHERE `id` = @userId", new { userId });
        if (motto == null) return Task.FromResult(false);
        client.GetHabbo().Motto = motto;

        // If we're in a room, we cannot really send the packets, so flag this as completed successfully, since we already updated it.
        if (!client.GetHabbo().InRoom)
            return Task.FromResult(true);
        //We are in a room, let's try to run the packets.
        var room = client.GetHabbo().CurrentRoom;
        if (room != null)
        {
            var user = room.GetRoomUserManager().GetRoomUserByHabbo(client.GetHabbo().Id);
            if (user != null)
            {
                room.SendPacket(new UserChangeComposer(AvatarChangeSnapshot.Capture(user, false)));
                return Task.FromResult(true);
            }
        }
        return Task.FromResult(false);
    }
}
