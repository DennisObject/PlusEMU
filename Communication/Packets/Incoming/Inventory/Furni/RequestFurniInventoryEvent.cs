using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Inventory.Furni;

internal sealed class RequestFurniInventoryEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        session.GetHabbo().Inventory.Furniture.SendInventory(session);
        return Task.CompletedTask;
    }
}
