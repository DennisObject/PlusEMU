using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Inventory;

namespace Plus.Communication.Packets.Incoming.Inventory.Bots;

internal sealed class GetBotInventoryEvent(IInventoryShowcaseService inventory) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        inventory.ShowBots(session);

        return Task.CompletedTask;
    }
}
