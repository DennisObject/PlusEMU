using Plus.Database;
using Dapper;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Moderator;

internal class UnmuteCommand : ITargetChatCommand
{
    private readonly IDatabase _database;
    public string Key => "unmute";

    public string Parameters => "%username%";

    public string Description => "Unmute a currently muted user.";

    public bool MustBeInSameRoom => false;

    public UnmuteCommand(IDatabase database)
    {
        _database = database;
    }

    public Task Execute(GameClient session, Room room, Habbo target, string[] parameters)
    {
        if (!session.GetHabbo().Access.Outranks(target.Access))
            return Task.CompletedTask;
        using var connection = _database.Connection();
        connection.Execute("UPDATE users SET time_muted=0 WHERE id=@id LIMIT 1", new { target.Id });
        target.TimeMuted = 0;
        target.Client?.SendNotification($"You have been un-muted by {session.GetHabbo().Username}!");
        session.SendWhisper($"You have successfully un-muted {target.Username}!");
        return Task.CompletedTask;
    }
}
