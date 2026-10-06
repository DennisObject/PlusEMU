using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Inventory;

namespace Plus.Communication.Packets.Incoming.Inventory.Badges;

internal sealed class GetBadgesEvent(IInventoryShowcaseService inventory) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        inventory.ShowBadges(session);

        return Task.CompletedTask;
    }
}
