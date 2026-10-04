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
        session.GetHabbo().AllowBotSpeech = !session.GetHabbo().AllowBotSpeech;
        using (var dbClient = _database.GetQueryReactor())
        {
            dbClient.RunQuery($"UPDATE `users` SET `bots_muted` = '{session.GetHabbo().AllowBotSpeech}' WHERE `id` = '{session.GetHabbo().Id}' LIMIT 1");
        }
        if (session.GetHabbo().AllowBotSpeech)
            session.SendWhisper("Change successful, you can no longer see speech from bots.");
        else
            session.SendWhisper("Change successful, you can now see speech from bots.");
    }
}