using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Inventory.Badges;

namespace Plus.Communication.Packets.Outgoing.Inventory.Badges;

public sealed class BadgesComposer(BadgeInventorySnapshot snapshot) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.BadgesComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(snapshot.Codes.Length);
        foreach (var code in snapshot.Codes)
        {
            packet.WriteInteger(1);
            packet.WriteString(code);
        }
        packet.WriteInteger(snapshot.Equipped.Length);
        foreach (var badge in snapshot.Equipped)
        {
            packet.WriteInteger(badge.Slot);
            packet.WriteString(badge.Code);
        }
    }
}
