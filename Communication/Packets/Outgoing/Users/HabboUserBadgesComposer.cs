using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Inventory.Badges;

namespace Plus.Communication.Packets.Outgoing.Users;

public sealed class HabboUserBadgesComposer(int userId, ImmutableArray<BadgeSlotSnapshot> equipped) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.HabboUserBadgesComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(userId);
        packet.WriteInteger(equipped.Length);
        foreach (var badge in equipped)
        {
            packet.WriteInteger(badge.Slot);
            packet.WriteString(badge.Code);
        }
    }
}
