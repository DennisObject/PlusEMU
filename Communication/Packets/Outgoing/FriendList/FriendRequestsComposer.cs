using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.FriendList;

public class FriendRequestsComposer : IServerPacket
{
    private readonly ImmutableArray<FriendRequestData> _requests;
    public uint MessageId => ServerPacketHeader.FriendRequestsComposer;

    public FriendRequestsComposer(IReadOnlyList<FriendRequestData> requests)
    {
        _requests = requests.ToImmutableArray();
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_requests.Length);
        packet.WriteInteger(_requests.Length);
        foreach (var request in _requests)
        {
            packet.WriteInteger(request.UserId);
            packet.WriteString(request.Username);
            packet.WriteString(request.Look);
        }
    }
}

public sealed record FriendRequestData(int UserId, string Username, string Look);
