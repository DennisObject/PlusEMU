using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms.Chat.Commands.User;

internal class UnloadCommand : IChatCommand
{
    private readonly IRoomManager _roomManager;
    public string Key => "unload";

    public string Parameters => "%id%";

    public string Description => "Unload the current room.";

    public UnloadCommand(IRoomManager roomManager)
    {
        _roomManager = roomManager;
    }
    public void Execute(GameClient session, Room room, string[] parameters)
    {
        if (room.CheckRights(session, true) || session.GetHabbo().Access.Can(PermissionKeys.RoomUnloadAny)) {
            _roomManager.UnloadRoom(room.Id);
        }
    }
}
