using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Inventory.Badges;

namespace Plus.Communication.Packets.Outgoing.Inventory.Badges;

public sealed class BadgesComposer(BadgeInventorySnapshot snapshot) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.BadgesComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(snapshot.Badges.Length);

        // WIN63 class_3622: badge id, code, owner count, rarity id.
        foreach (var badge in snapshot.Badges) {
            packet.WriteInteger(1);
            packet.WriteString(badge.Code);
            packet.WriteInteger(badge.Rarity.OwnerCount);
            packet.WriteInteger((int)badge.Rarity.Tier);
        }

        packet.WriteInteger(snapshot.Equipped.Length);

        foreach (var badge in snapshot.Equipped) {
            packet.WriteInteger(badge.Slot);
            packet.WriteString(badge.Code);
        }
    }
}
