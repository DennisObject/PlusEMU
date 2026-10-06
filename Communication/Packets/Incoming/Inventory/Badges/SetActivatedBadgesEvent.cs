using System.Collections.Immutable;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Inventory.Badges;

namespace Plus.Communication.Packets.Incoming.Inventory.Badges;

internal sealed class SetActivatedBadgesEvent(IBadgeEquipmentService badges) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var requested = ImmutableArray.CreateBuilder<BadgeSlotSnapshot>(5);
        for (var index = 0; index < 5; index++)
        {
            var slot = packet.ReadInt();
            requested.Add(new(packet.ReadString(), slot));
        }
        return badges.Set(session, requested.MoveToImmutable());
    }
}
