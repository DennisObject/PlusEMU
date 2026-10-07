using Plus.HabboHotel.Crafting;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Crafting;

internal sealed class GetCraftableProductsEvent(ICraftingService crafting) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        crafting.GetProducts(session, packet.ReadUInt());
        return Task.CompletedTask;
    }
}
