using System.Collections.Immutable;
using System.Globalization;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Messenger;

namespace Plus.Communication.Packets.Outgoing.FriendList;

public class HabboSearchResultComposer : IServerPacket
{
    private readonly ImmutableArray<HabboSearchEntry> _friends;
    private readonly ImmutableArray<HabboSearchEntry> _otherUsers;
    public uint MessageId => ServerPacketHeader.HabboSearchResultComposer;

    public HabboSearchResultComposer(IReadOnlyList<HabboSearchEntry> friends, IReadOnlyList<HabboSearchEntry> otherUsers)
    {
        _friends = friends.ToImmutableArray();
        _otherUsers = otherUsers.ToImmutableArray();
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_friends.Length);
        foreach (var entry in _friends)
        {
            var friend = entry.User;
            var online = entry.Online;
            packet.WriteInteger(friend.UserId);
            packet.WriteString(friend.Username);
            packet.WriteString(friend.Motto);
            packet.WriteBoolean(online);
            packet.WriteBoolean(false);
            packet.WriteString(string.Empty);
            packet.WriteInteger(0);
            packet.WriteString(online ? friend.Figure : "");
            packet.WriteString(friend.LastOnlineAt?.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) ?? "0");
        }
        packet.WriteInteger(_otherUsers.Length);
        foreach (var entry in _otherUsers)
        {
            var otherUser = entry.User;
            var online = entry.Online;
            packet.WriteInteger(otherUser.UserId);
            packet.WriteString(otherUser.Username);
            packet.WriteString(otherUser.Motto);
            packet.WriteBoolean(online);
            packet.WriteBoolean(false);
            packet.WriteString(string.Empty);
            packet.WriteInteger(0);
            packet.WriteString(online ? otherUser.Figure : "");
            packet.WriteString(otherUser.LastOnlineAt?.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) ?? "0");
        }

    }
}

public sealed record HabboSearchEntry(SearchResult User, bool Online);
