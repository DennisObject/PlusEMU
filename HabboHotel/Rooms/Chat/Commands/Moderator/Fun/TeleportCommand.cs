using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Moderator.Fun;

internal class TeleportCommand : IChatCommand
{
    public string Key => "teleport";

    public string Parameters => "";

    public string Description => "The ability to teleport anywhere within the room.";

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);
        if (user == null)
            return;
        user.TeleportEnabled = !user.TeleportEnabled;
        room.GetGameMap().GenerateMaps();
    }
}