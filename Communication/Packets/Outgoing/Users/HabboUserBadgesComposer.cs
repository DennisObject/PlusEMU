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

        // WIN63 class_3168: slot, code, owner count, rarity id.
        foreach (var badge in equipped) {
            packet.WriteInteger(badge.Slot);
            packet.WriteString(badge.Code);
            packet.WriteInteger(badge.Rarity.OwnerCount);
            packet.WriteInteger((int)badge.Rarity.Tier);
        }
    }
}
