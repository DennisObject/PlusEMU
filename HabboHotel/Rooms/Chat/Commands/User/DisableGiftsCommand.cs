using Dapper;
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
        var allowGifts = !session.GetHabbo().AllowGifts;
        using var connection = _database.Connection();
        connection.Execute("UPDATE users_settings SET allow_gifts=@allowGifts WHERE user_id=@userId",
            new { allowGifts, userId = session.GetHabbo().Id });
        session.GetHabbo().AllowGifts = allowGifts;
        session.SendWhisper($"You're {(allowGifts ? "now" : "no longer")} accepting gifts.");
    }
}
