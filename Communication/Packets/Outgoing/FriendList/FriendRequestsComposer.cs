using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.FriendList;

public class FriendRequestsComposer : IServerPacket
{
    private readonly IReadOnlyList<FriendRequestData> _requests;
    public uint MessageId => ServerPacketHeader.FriendRequestsComposer;

    public FriendRequestsComposer(IReadOnlyList<FriendRequestData> requests)
    {
        _requests = requests;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_requests.Count);
        packet.WriteInteger(_requests.Count);
        foreach (var request in _requests)
        {
            packet.WriteInteger(request.UserId);
            packet.WriteString(request.Username);
            packet.WriteString(request.Look);
        }
    }
}

public sealed record FriendRequestData(int UserId, string Username, string Look);
