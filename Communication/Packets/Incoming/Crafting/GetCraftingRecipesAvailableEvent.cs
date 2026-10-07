using Plus.HabboHotel.Crafting;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Crafting;

internal sealed class GetCraftingRecipesAvailableEvent(ICraftingService crafting) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var altarId = packet.ReadUInt();
        crafting.GetAvailable(session, altarId, CraftingPacket.ReadItems(packet));
        return Task.CompletedTask;
    }
}
