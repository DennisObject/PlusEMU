using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Moderator.Fun;

internal class OverrideCommand : IChatCommand
{
    public string Key => "override";

    public string Parameters => "";

    public string Description => "Gives you the ability to walk over anything.";

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        var user = room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);

        if (user == null)
        {
            return;
        }

        user.AllowOverride = !user.AllowOverride;
        session.SendWhisper("Override mode updated.");
    }
}
