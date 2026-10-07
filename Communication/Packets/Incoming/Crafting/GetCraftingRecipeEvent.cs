using Plus.HabboHotel.Crafting;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Crafting;

internal sealed class GetCraftingRecipeEvent(ICraftingService crafting) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        crafting.GetRecipe(session, packet.ReadString());
        return Task.CompletedTask;
    }
}
