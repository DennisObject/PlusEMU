using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Messenger;

namespace Plus.Communication.Packets.Outgoing.FriendList;

public sealed class MessengerCommunicationOutput : IMessengerCommunicationOutput
{
    public void InstantMessageError(GameClient session, MessengerMessageErrors error, int userId) =>
        session.Send(new InstantMessageErrorComposer(error, userId));

    public void Notice(GameClient session, string text) => session.SendNotification(text);
}
