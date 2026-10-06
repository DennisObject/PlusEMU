using Plus.HabboHotel.Permissions;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Moderator;

internal class MuteCommand : ITargetChatCommand
{
    private readonly IDatabase _database;
    public string Key => "mute";

    public string Parameters => "%username% %time%";

    public string Description => "Mute another user for a certain amount of time.";

    public bool MustBeInSameRoom => false;

    public MuteCommand(IDatabase database)
    {
        _database = database;
    }

    public Task Execute(GameClient session, Room room, Habbo target, string[] parameters)
    {
        if (!session.GetHabbo().Access.Outranks(target.Access))
        {
            session.SendWhisper("Oops, you cannot mute that user.");
            return Task.CompletedTask;
        }
        if (double.TryParse(parameters[0], out var time))
        {
            if (time > 600 && !session.GetHabbo().Access.Can(PermissionKeys.ModerationMuteLimitOverride))
                time = 600;
            using var connection = _database.Connection();
            connection.Execute("UPDATE users SET time_muted=@time WHERE id=@id LIMIT 1", new { time, target.Id });
            if (target.Client != null)
            {
                target.TimeMuted = time;
                target.Client.SendNotification($"You have been muted by a moderator for {time} seconds!");
            }
            session.SendWhisper($"You have successfully muted {target.Username} for {time} seconds.");
        }
        else
            session.SendWhisper("Please enter a valid integer.");

        return Task.CompletedTask;
    }
}
