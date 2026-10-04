using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Messenger;

namespace Plus.Communication.Packets.Outgoing.FriendList;

public class FriendRequestsComposer : IServerPacket
{
    private readonly ICollection<MessengerRequest> _requests;
    public uint MessageId => ServerPacketHeader.FriendRequestsComposer;

    public FriendRequestsComposer(ICollection<MessengerRequest> requests)
    {
        _requests = requests;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_requests.Count);
        packet.WriteInteger(_requests.Count);
        foreach (var request in _requests)
        {
            packet.WriteInteger(request.FromId);
            packet.WriteString(request.Username);
            var user = PlusEnvironment.Game.CacheManager.GenerateUser(request.FromId);
            packet.WriteString(user != null ? user.Look : "");
        }
    }
}