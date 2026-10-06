using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Messenger;

namespace Plus.Communication.Packets.Outgoing.FriendList;

public class FriendNotificationComposer : IServerPacket
{
    private readonly int _userId;
    private readonly MessengerEventTypes _type;
    private readonly string _data;
    public uint MessageId => ServerPacketHeader.FriendNotificationComposer;

    public FriendNotificationComposer(int userId, MessengerEventTypes type, string data)
    {
        _userId = userId;
        _type = type;
        _data = data;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(_userId.ToString());
        packet.WriteInteger(MessengerEventTypesUtility.GetEventTypePacketNum(_type));
        packet.WriteString(_data);
    }
}
