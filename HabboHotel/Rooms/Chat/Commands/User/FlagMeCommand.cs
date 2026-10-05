using Plus.HabboHotel.Permissions;
using Plus.Communication.Packets.Outgoing.Handshake;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;
using Plus.Utilities;

namespace Plus.HabboHotel.Rooms.Chat.Commands.User;

internal class FlagMeCommand : IChatCommand
{
    public string Key => "flagme";

    public string Parameters => "";

    public string Description => "Gives you the option to change your username.";

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        if (!NameChangePolicy.CanChange(session.GetHabbo(), DateTimeOffset.UtcNow))
        {
            session.SendWhisper("Sorry, it seems you currently do not have the option to change your username!");
            return;
        }
        session.GetHabbo().ChangingName = true;
        session.SendNotification("Please be aware that if your username is deemed as inappropriate, you will be banned without question.\r\rAlso note that Staff will NOT change your username again should you have an issue with what you have chosen.\r\rClose this window and click yourself to begin choosing a new username!");
        session.Send(new UserObjectComposer(UserObjectSnapshot.Capture(session.GetHabbo())));
    }

}