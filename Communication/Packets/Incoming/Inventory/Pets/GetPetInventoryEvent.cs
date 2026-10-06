using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Inventory;

namespace Plus.Communication.Packets.Incoming.Inventory.Pets;

internal sealed class GetPetInventoryEvent(IInventoryShowcaseService inventory) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        inventory.ShowPets(session);

        return Task.CompletedTask;
    }
}
