using Plus.Database;
using Dapper;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms.Chat.Commands.User;

internal class DisableMimicCommand : IChatCommand
{
    private readonly IDatabase _database;
    public string Key => "disablemimic";

    public string Parameters => "";

    public string Description => "Allows you to disable the ability to be mimiced or to enable the ability to be mimiced.";

    public DisableMimicCommand(IDatabase database)
    {
        _database = database;
    }

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        var habbo = session.GetHabbo();
        var value = !habbo.AllowMimic;
        using var connection = _database.Connection();

        if (connection.Execute("UPDATE users_settings SET allow_mimic = @value WHERE user_id = @userId LIMIT 1",
                new { value, userId = habbo.Id }) != 1) {
            throw new InvalidOperationException("User settings were not persisted.");
        }

        habbo.AllowMimic = value;
        session.SendWhisper($"You're {(value ? "now" : "no longer")} able to be mimiced.");
    }
}
