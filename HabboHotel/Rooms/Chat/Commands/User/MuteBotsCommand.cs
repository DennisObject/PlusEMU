using Dapper;
using Plus.Database;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms.Chat.Commands.User;

internal class MuteBotsCommand : IChatCommand
{
    private readonly IDatabase _database;
    public string Key => "mutebots";

    public string Parameters => "";

    public string Description => "Ignore bot chat or enable it again.";

    public MuteBotsCommand(IDatabase database)
    {
        _database = database;
    }

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        var muted = !session.GetHabbo().AllowBotSpeech;
        using var connection = _database.Connection();
        connection.Execute("UPDATE users_settings SET bots_muted=@muted WHERE user_id=@userId LIMIT 1",
            new { muted, userId = session.GetHabbo().Id });
        session.GetHabbo().AllowBotSpeech = muted;

        if (muted) {
            session.SendWhisper("Change successful, you can no longer see speech from bots.");
        }
        else {
            session.SendWhisper("Change successful, you can now see speech from bots.");
        }
    }
}
