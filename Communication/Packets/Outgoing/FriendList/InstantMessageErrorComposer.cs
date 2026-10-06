using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Messenger;

namespace Plus.Communication.Packets.Outgoing.FriendList;

public class InstantMessageErrorComposer : IServerPacket
{
    private readonly MessengerMessageErrors _error;
    private readonly int _target;
    public uint MessageId => ServerPacketHeader.InstantMessageErrorComposer;

    public InstantMessageErrorComposer(MessengerMessageErrors error, int target)
    {
        _error = error;
        _target = target;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(MessengerMessageErrorsUtility.GetMessageErrorPacketNum(_error));
        packet.WriteInteger(_target);
        packet.WriteString("");
    }
}
