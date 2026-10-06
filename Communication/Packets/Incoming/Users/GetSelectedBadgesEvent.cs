using Plus.HabboHotel.Badges;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Users;

internal sealed class GetSelectedBadgesEvent(IBadgeEquipmentService badges) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => badges.Show(session, packet.ReadInt());
}
