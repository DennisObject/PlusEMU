using Plus.HabboHotel.Crafting;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Crafting;

internal sealed class CraftEvent(ICraftingService crafting) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        crafting.Craft(session, packet.ReadUInt(), packet.ReadString());
        return Task.CompletedTask;
    }
}
