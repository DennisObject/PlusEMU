using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.Communication.Packets.Outgoing.Users;

public sealed class HabboGroupBadgesComposer(ImmutableArray<GroupBadgeSnapshot> badges) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.HabboGroupBadgesComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(badges.Length);

        foreach (var badge in badges) {
            packet.WriteInteger(badge.GroupId);
            packet.WriteString(badge.Badge);
        }
    }
}
