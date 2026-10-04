using Plus.Database;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms.Chat.Commands.User;

internal class MutePetsCommand : IChatCommand
{
    private readonly IDatabase _database;
    public string Key => "mutepets";

    public string Parameters => "";

    public string Description => "Ignore bot chat or enable it again.";

    public MutePetsCommand(IDatabase database)
    {
        _database = database;
    }

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        session.GetHabbo().AllowPetSpeech = !session.GetHabbo().AllowPetSpeech;
        using (var dbClient = _database.GetQueryReactor())
        {
            dbClient.RunQuery($"UPDATE `users` SET `pets_muted` = '{session.GetHabbo().AllowPetSpeech}' WHERE `id` = '{session.GetHabbo().Id}' LIMIT 1");
        }
        if (session.GetHabbo().AllowPetSpeech)
            session.SendWhisper("Change successful, you can no longer see speech from pets.");
        else
            session.SendWhisper("Change successful, you can now see speech from pets.");
    }
}