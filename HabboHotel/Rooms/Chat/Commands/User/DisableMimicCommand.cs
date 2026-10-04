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
        session.GetHabbo().AllowMimic = !session.GetHabbo().AllowMimic;
        session.SendWhisper($"You're {(session.GetHabbo().AllowMimic ? "now" : "no longer")} able to be mimiced.");
        using var connection = _database.Connection();
        connection.Execute("UPDATE users SET allow_mimic = @AllowMimic WHERE id = @userId LIMIT 1",
            new { AllowMimic = session.GetHabbo().AllowMimic, userId = session.GetHabbo().Id });
    }
}