using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Users;
using Plus.Utilities;

namespace Plus.Communication.Packets.Outgoing.Users;

public class ProfileInformationComposer : IServerPacket
{
    private readonly Habbo _habbo;
    private readonly GameClient _session;
    private readonly List<Group> _groups;
    private readonly int _friendCount;
    private readonly HabboStats _habboStats;

    public uint MessageId => ServerPacketHeader.ProfileInformationComposer;

    public ProfileInformationComposer(Habbo habbo, GameClient session, List<Group> groups, int friendCount, HabboStats habboStats)
    {
        _habbo = habbo;
        _session = session;
        _groups = groups;
        _friendCount = friendCount;
        _habboStats = habboStats;
    }

    public void Compose(IOutgoingPacket packet)
    {
        var origin = _habbo.AccountCreatedAt ?? DateTimeOffset.UnixEpoch;
        packet.WriteInteger(_habbo.Id);
        packet.WriteString(_habbo.Username);
        packet.WriteString(_habbo.Look);
        packet.WriteString(_habbo.Motto);
        packet.WriteString(origin.ToString("dd/MM/yyyy"));
        packet.WriteInteger(_habboStats?.AchievementPoints ?? 0);
        packet.WriteInteger(_friendCount); // Friend Count
        packet.WriteBoolean(_habbo.Id != _session.GetHabbo().Id && _session.GetHabbo().Messenger.FriendshipExists(_habbo.Id)); //  Is friend
        packet.WriteBoolean(_habbo.Id != _session.GetHabbo().Id && !_session.GetHabbo().Messenger.FriendshipExists(_habbo.Id) &&
                            _session.GetHabbo().Messenger.OutstandingFriendRequests.Contains(_habbo.Id)); // Sent friend request
        packet.WriteBoolean(PlusEnvironment.Game.ClientManager.GetClientByUserId(_habbo.Id) != null);
        packet.WriteInteger(_groups.Count);
        foreach (var group in _groups)
        {
            packet.WriteInteger(group.Id);
            packet.WriteString(group.Name);
            packet.WriteString(group.Badge);
            packet.WriteString(PlusEnvironment.Game.GroupManager.GetColourCode(group.Colour1, true));
            packet.WriteString(PlusEnvironment.Game.GroupManager.GetColourCode(group.Colour2, false));
            packet.WriteBoolean(_habboStats?.FavouriteGroupId == group.Id); // todo favs
            packet.WriteInteger(0); //what the fuck
            packet.WriteBoolean(group?.ForumEnabled ?? true); //HabboTalk
        }
        var lastOnline = _habbo.LastOnlineAt?.ToUnixTimeSeconds() ?? 0;
        packet.WriteInteger(Convert.ToInt32(UnixTimestamp.GetNow() - lastOnline)); // Last online
        packet.WriteBoolean(true); // Show the profile
    }
}
