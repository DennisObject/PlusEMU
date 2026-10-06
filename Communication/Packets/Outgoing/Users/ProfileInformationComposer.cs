using System.Globalization;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.Communication.Packets.Outgoing.Users;

public class ProfileInformationComposer(PlayerProfileSnapshot profile) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ProfileInformationComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(profile.Id);
        packet.WriteString(profile.Username);
        packet.WriteString(profile.Look);
        packet.WriteString(profile.Motto);
        packet.WriteString((profile.CreatedAt ?? DateTimeOffset.UnixEpoch).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
        packet.WriteInteger(profile.AchievementPoints);
        packet.WriteInteger(profile.FriendCount);
        packet.WriteBoolean(profile.IsFriend);
        packet.WriteBoolean(profile.RequestedFriendship);
        packet.WriteBoolean(profile.Online);
        packet.WriteInteger(profile.Groups.Length);

        foreach (var group in profile.Groups)
        {
            packet.WriteInteger(group.Id);
            packet.WriteString(group.Name);
            packet.WriteString(group.Badge);
            packet.WriteString(group.FirstColor);
            packet.WriteString(group.SecondColor);
            packet.WriteBoolean(group.Favorite);
            packet.WriteInteger(0);
            packet.WriteBoolean(group.ForumEnabled);
        }

        packet.WriteInteger(profile.LastOnlineSeconds);
        packet.WriteBoolean(true);
    }
}
