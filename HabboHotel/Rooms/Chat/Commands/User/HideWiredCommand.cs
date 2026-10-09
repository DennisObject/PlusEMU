using Dapper;
using Plus.Database;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms.Chat.Commands.User;

internal class HideWiredCommand : IChatCommand
{
    private readonly IDatabase _database;

    public string Key => "hidewired";

    public string Parameters => "";

    public string Description => "Hide or show all wired furniture in your room.";

    public HideWiredCommand(IDatabase database)
    {
        _database = database;
    }

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        if (!room.CheckRights(session, true)) {
            session.SendWhisper("Oops, only the room owner can hide the wired in this room.");

            return;
        }

        room.SetWiredHidden(!room.HideWired);

        using var connection = _database.Connection();
        connection.Execute("UPDATE rooms SET hide_wired=@hidden WHERE id=@roomId LIMIT 1", new { hidden = room.HideWired, roomId = room.Id });

        session.SendWhisper(room.HideWired ? "Wired is now hidden. Type :hidewired again to show it." : "Wired is now shown.");
    }
}
