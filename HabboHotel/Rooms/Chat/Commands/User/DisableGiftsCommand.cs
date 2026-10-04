using Plus.Database;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms.Chat.Commands.User;

internal class DisableGiftsCommand : IChatCommand
{
    private readonly IDatabase _database;
    public string Key => "disablegifts";

    public string Parameters => "";

    public string Description => "Allows you to disable the ability to receive gifts or to enable the ability to receive gifts.";

    public DisableGiftsCommand(IDatabase database)
    {
        _database = database;
    }

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        session.GetHabbo().AllowGifts = !session.GetHabbo().AllowGifts;
        session.SendWhisper($"You're {(session.GetHabbo().AllowGifts ? "now" : "no longer")} accepting gifts.");
        using var dbClient = _database.GetQueryReactor();
        dbClient.SetQuery($"UPDATE `users` SET `allow_gifts` = @AllowGifts WHERE `id` = '{session.GetHabbo().Id}'");
        dbClient.AddParameter("AllowGifts", session.GetHabbo().AllowGifts);
        dbClient.RunQuery();
    }
}